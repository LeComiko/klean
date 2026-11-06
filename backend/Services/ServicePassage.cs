using Klean.Api.Data;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Klean.Api.Services;

public enum IssuePassage
{
    Ok,
    Introuvable,
    Refusee,
    Conflit
}

/// <summary>Résultat d'un pointage. <c>Message</c> est écrit pour l'agent, qui le lira sur place.</summary>
public readonly record struct ResultatPassage(IssuePassage Issue, VacationAgentDto? Valeur, string? Message)
{
    public static ResultatPassage Ok(VacationAgentDto valeur) => new(IssuePassage.Ok, valeur, null);
    public static ResultatPassage Introuvable() => new(IssuePassage.Introuvable, null, null);
    public static ResultatPassage Refusee(string message) => new(IssuePassage.Refusee, null, message);
    public static ResultatPassage Conflit(string message) => new(IssuePassage.Conflit, null, message);
}

/// <summary>
/// Le passage de l'agent : sa journée, et les deux pointages qui certifient une vacation
/// (SPEC.md, étape 25).
///
/// <code>
/// Planifiee ──démarrer──► EnCours ──terminer──► Terminee       (deux pointages SurSite)
///                                          └──► NonCertifiee   (sinon)
/// </code>
///
/// ⚠️ <b>Seul l'agent affecté pointe.</b> Chaque lecture est cadrée sur l'agent du jeton,
/// en plus du filtre global : un chef qui pointerait pour son agent viderait la preuve de
/// son sens, et une vacation d'un autre agent est inconnue — 404, pas 403.
///
/// ⚠️ <b>Les coordonnées reçues ne sortent pas de ce service.</b> Elles servent à
/// <see cref="Evaluer"/>, puis disparaissent avec la requête : <see cref="Pointage"/> n'a
/// aucune colonne pour les recevoir.
/// </summary>
public class ServicePassage
{
    /// <summary>
    /// Le rayon d'un site. Large, parce qu'un site est un bâtiment, parfois une résidence
    /// entière, et que ses coordonnées sont celles de son adresse. À éprouver pendant le
    /// pilote avant d'en faire un réglage.
    /// </summary>
    public const int RayonMetres = 200;

    /// <summary>
    /// Au-delà, la position ne permet pas de trancher. À l'intérieur d'un bâtiment, un
    /// téléphone annonce couramment plusieurs dizaines de mètres.
    /// </summary>
    public const int PrecisionMaximaleMetres = 100;

    /// <summary>Le nom qu'EF donne à l'index unique du <c>DbContext</c> : un début et une fin par vacation.</summary>
    private const string IndexUnPointageParMoment = "IX_Pointages_InterventionId_Moment";

    private readonly KleanDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly TimeProvider _horloge;

    public ServicePassage(KleanDbContext db, ITenantContext tenant, TimeProvider horloge)
    {
        _db = db;
        _tenant = tenant;
        _horloge = horloge;
    }

    /// <summary>
    /// La preuve d'une position, au regard du site.
    ///
    /// L'ordre des tests est le point : une position très lointaine est <c>HorsRayon</c>
    /// même imprécise — dix kilomètres à cent cinquante mètres près ne laissent aucun
    /// doute —, et c'est seulement quand la marge d'erreur pourrait la ramener dans le
    /// rayon que l'imprécision empêche de trancher.
    /// </summary>
    public static (PreuvePointage Preuve, int? DistanceMetres, int? PrecisionMetres) Evaluer(
        PositionDto? position, double latitudeSite, double longitudeSite)
    {
        if (position is null) return (PreuvePointage.NonLocalisee, null, null);

        var distance = AffectationService.Haversine(
            position.Latitude, position.Longitude, latitudeSite, longitudeSite) * 1000;
        var precision = position.PrecisionMetres;

        var preuve = distance - precision > RayonMetres ? PreuvePointage.HorsRayon
            : precision > PrecisionMaximaleMetres ? PreuvePointage.Imprecise
            : distance <= RayonMetres ? PreuvePointage.SurSite
            : PreuvePointage.HorsRayon;

        return (preuve, (int)Math.Round(distance), (int)Math.Round(precision));
    }

    /// <summary>
    /// Les vacations du jour de l'agent, celles du territoire de l'entreprise, plus toute
    /// vacation encore <c>EnCours</c> d'un jour passé : commencée tard la veille, ou
    /// oubliée, elle doit rester à portée de « Terminer ».
    /// </summary>
    public async Task<List<VacationAgentDto>> MaJourneeAsync(CancellationToken annulation)
    {
        var agentId = AgentDuJeton();
        var (debut, fin) = BornesDuJour(await AujourdhuiAsync(annulation));

        var vacations = await Projeter(_db.Interventions.AsNoTracking()
                .Where(i => i.AgentId == agentId
                            && ((i.DatePrevue >= debut && i.DatePrevue < fin)
                                || i.Statut == StatutIntervention.EnCours)))
            .OrderBy(v => v.DatePrevue).ThenBy(v => v.HeurePrevue)
            .ToListAsync(annulation);

        await AjouterDetailsAsync(vacations, annulation);
        return vacations;
    }

    public Task<ResultatPassage> DemarrerAsync(
        Guid interventionId, PositionDto? position, Guid auteurId, CancellationToken annulation) =>
        PointerAsync(interventionId, MomentPointage.Debut, position, pointsFaits: [], auteurId, annulation);

    /// <summary>
    /// La fin du passage, et son compte rendu : chaque point actif du type est enregistré, fait
    /// s'il figure dans <paramref name="pointsFaits"/>, non fait sinon.
    /// </summary>
    public Task<ResultatPassage> TerminerAsync(
        Guid interventionId, PositionDto? position, IReadOnlyCollection<Guid> pointsFaits, Guid auteurId,
        CancellationToken annulation) =>
        PointerAsync(interventionId, MomentPointage.Fin, position, pointsFaits, auteurId, annulation);

    private async Task<ResultatPassage> PointerAsync(
        Guid interventionId, MomentPointage moment, PositionDto? position, IReadOnlyCollection<Guid> pointsFaits,
        Guid auteurId, CancellationToken annulation)
    {
        var refusPosition = RefusDePosition(position);
        if (refusPosition is not null) return ResultatPassage.Refusee(refusPosition);

        var agentId = AgentDuJeton();

        var cible = await _db.Interventions
            .Where(i => i.Id == interventionId && i.AgentId == agentId)
            .Select(i => new
            {
                Intervention = i,
                i.Prestation!.Contrat!.Site!.Latitude,
                i.Prestation.Contrat.Site.Longitude,
                i.Prestation.TypePrestationId
            })
            .FirstOrDefaultAsync(annulation);

        if (cible is null) return ResultatPassage.Introuvable();

        var intervention = cible.Intervention;

        if (moment == MomentPointage.Debut)
        {
            if (intervention.Statut != StatutIntervention.Planifiee)
            {
                return ResultatPassage.Refusee(
                    $"Vacation au statut {intervention.Statut} : seule une vacation "
                    + $"{StatutIntervention.Planifiee} se démarre.");
            }

            var aujourdhui = await AujourdhuiAsync(annulation);
            var jour = DateOnly.FromDateTime(intervention.DatePrevue);

            if (jour != aujourdhui)
            {
                return ResultatPassage.Refusee(
                    $"Cette vacation est prévue le {jour:dd/MM/yyyy} : seule une vacation du jour se démarre.");
            }
        }
        else if (intervention.Statut != StatutIntervention.EnCours)
        {
            return ResultatPassage.Refusee(
                $"Vacation au statut {intervention.Statut} : seule une vacation "
                + $"{StatutIntervention.EnCours} se termine. La démarrer d'abord.");
        }

        var points = moment == MomentPointage.Fin
            ? await PointsActifsAsync(cible.TypePrestationId, annulation)
            : [];

        // ⚠️ Un point coché qui n'est pas de la checklist de cette vacation est refusé, avant
        // toute écriture : l'enregistrer en silence ferait déclarer fait ce qui n'a pas été
        // demandé, et l'ignorer cacherait un écran désaccordé.
        var inconnu = pointsFaits.Where(id => points.All(p => p.Id != id)).Cast<Guid?>().FirstOrDefault();

        if (inconnu is not null)
        {
            return ResultatPassage.Refusee(
                $"Le point de contrôle {inconnu} n'est pas dans la checklist de cette vacation. Recharger la journée.");
        }

        var (preuve, distance, precision) = Evaluer(position, cible.Latitude, cible.Longitude);

        _db.Pointages.Add(new Pointage
        {
            InterventionId = intervention.Id,
            Moment = moment,
            Horodatage = _horloge.GetUtcNow().UtcDateTime,
            Preuve = preuve,
            DistanceMetres = distance,
            PrecisionMetres = precision,
            AuteurId = auteurId
        });

        if (moment == MomentPointage.Debut)
        {
            intervention.Statut = StatutIntervention.EnCours;
        }
        else
        {
            // ⚠️ Les deux pointages, et non la fin seule : un agent qui démarre de chez lui
            // et termine sur place n'a pas prouvé qu'il a passé la vacation sur le site.
            var preuveDuDebut = await _db.Pointages.AsNoTracking()
                .Where(p => p.InterventionId == intervention.Id && p.Moment == MomentPointage.Debut)
                .Select(p => (PreuvePointage?)p.Preuve)
                .FirstOrDefaultAsync(annulation);

            intervention.Statut = preuveDuDebut == PreuvePointage.SurSite && preuve == PreuvePointage.SurSite
                ? StatutIntervention.Terminee
                : StatutIntervention.NonCertifiee;

            // Le libellé du moment, copié : un point renommé demain ne réécrit pas ce compte rendu.
            _db.ControlesEffectues.AddRange(points.Select(p => new ControleEffectue
            {
                InterventionId = intervention.Id,
                PointControleId = p.Id,
                Libelle = p.Libelle,
                Ordre = p.Ordre,
                Fait = pointsFaits.Contains(p.Id)
            }));
        }

        try
        {
            await _db.SaveChangesAsync(annulation);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException
               {
                   SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: IndexUnPointageParMoment
               })
        {
            // Deux appuis, ou deux téléphones : l'autre pointage est passé le premier.
            return ResultatPassage.Conflit(
                "Ce passage vient d'être pointé, depuis cet appareil ou un autre. Recharger la journée.");
        }

        var vacation = await Projeter(_db.Interventions.AsNoTracking().Where(i => i.Id == intervention.Id))
            .ToListAsync(annulation);

        await AjouterDetailsAsync(vacation, annulation);
        return ResultatPassage.Ok(vacation.Single());
    }

    /// <summary>
    /// Une position hors des bornes. L'écran ne peut pas en produire — le navigateur rend
    /// des valeurs valides — : c'est une requête forgée, ou un défaut, et le refus le dit.
    /// </summary>
    private static string? RefusDePosition(PositionDto? position)
    {
        if (position is null) return null;

        if (position.Latitude is < -90 or > 90 || position.Longitude is < -180 or > 180)
        {
            return "Position illisible : la latitude va de -90 à 90, la longitude de -180 à 180.";
        }

        return position.PrecisionMetres < 0
            ? "Position illisible : la précision est une distance, jamais négative."
            : null;
    }

    /// <summary>
    /// La route est réservée au rôle <c>Agent</c>, dont la création exige une fiche : un
    /// jeton d'agent sans fiche est un état impossible, et on refuse de poursuivre plutôt
    /// que de lire sans cadrage.
    /// </summary>
    private Guid AgentDuJeton() =>
        _tenant.AgentId
        ?? throw new InvalidOperationException(
            "Jeton d'agent sans revendication « agentId ». Refus de poursuivre : "
            + "le pointage ne serait cadré sur personne.");

    private async Task<DateOnly> AujourdhuiAsync(CancellationToken annulation) =>
        Territoires.Aujourdhui(_horloge, await Territoires.DeLEntrepriseAsync(_db, _tenant.EntrepriseId, annulation));

    private static (DateTime Debut, DateTime Fin) BornesDuJour(DateOnly jour) =>
        (DateTime.SpecifyKind(jour.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
         DateTime.SpecifyKind(jour.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc));

    private static IQueryable<VacationAgentDto> Projeter(IQueryable<Intervention> interventions) =>
        interventions.Select(i => new VacationAgentDto
        {
            Id = i.Id,
            DatePrevue = i.DatePrevue,
            HeurePrevue = i.HeurePrevue,
            DureeEstimeeMinutes = i.Prestation!.DureeEstimeeMinutes,
            Statut = i.Statut.ToString(),
            Prestation = i.Prestation.Libelle,
            Site = i.Prestation.Contrat!.Site!.Nom,
            Adresse = i.Prestation.Contrat.Site.Adresse,
            CodePostal = i.Prestation.Contrat.Site.CodePostal,
            Ville = i.Prestation.Contrat.Site.Ville,
            Consignes = i.Prestation.Contrat.Site.Consignes
        });

    /// <summary>Les points actifs d'un type, dans l'ordre du parcours. Aucun sans type.</summary>
    private async Task<List<PointControle>> PointsActifsAsync(Guid? typePrestationId, CancellationToken annulation) =>
        typePrestationId is null
            ? []
            : await _db.PointsControle.AsNoTracking()
                .Where(p => p.TypePrestationId == typePrestationId && p.Actif)
                .OrderBy(p => p.Ordre).ThenBy(p => p.Libelle)
                .ToListAsync(annulation);

    /// <summary>
    /// Les pointages, la checklist et les anomalies de chaque vacation : avant la fin, les points
    /// à cocher ; après, ce qui a été déclaré.
    /// </summary>
    private async Task AjouterDetailsAsync(List<VacationAgentDto> vacations, CancellationToken annulation)
    {
        var ids = vacations.Select(v => v.Id).ToList();

        var pointages = await _db.Pointages.AsNoTracking()
            .Where(p => ids.Contains(p.InterventionId))
            .ToListAsync(annulation);

        var types = await _db.Interventions.AsNoTracking()
            .Where(i => ids.Contains(i.Id))
            .Select(i => new { i.Id, i.Prestation!.TypePrestationId })
            .ToDictionaryAsync(i => i.Id, i => i.TypePrestationId, annulation);

        var typesUtiles = types.Values.Where(t => t is not null).Distinct().ToList();

        var points = await _db.PointsControle.AsNoTracking()
            .Where(p => typesUtiles.Contains(p.TypePrestationId) && p.Actif)
            .OrderBy(p => p.Ordre).ThenBy(p => p.Libelle)
            .ToListAsync(annulation);

        var declares = await _db.ControlesEffectues.AsNoTracking()
            .Where(c => ids.Contains(c.InterventionId))
            .OrderBy(c => c.Ordre).ThenBy(c => c.Libelle)
            .ToListAsync(annulation);

        var anomalies = await _db.Anomalies.AsNoTracking()
            .Where(a => ids.Contains(a.InterventionId))
            .OrderBy(a => a.DateSignalement)
            .Select(a => new { a.InterventionId, Dto = new AnomalieAgentDto
            {
                Id = a.Id, Description = a.Description, Photos = a.Photos.Count
            } })
            .ToListAsync(annulation);

        foreach (var vacation in vacations)
        {
            var fin = pointages.FirstOrDefault(p => p.InterventionId == vacation.Id && p.Moment == MomentPointage.Fin);

            vacation.Debut = VersDto(pointages.FirstOrDefault(p => p.InterventionId == vacation.Id && p.Moment == MomentPointage.Debut));
            vacation.Fin = VersDto(fin);

            vacation.Checklist = fin is not null
                ? declares.Where(c => c.InterventionId == vacation.Id)
                    .Select(c => new PointChecklistDto { PointControleId = c.PointControleId, Libelle = c.Libelle, Fait = c.Fait })
                    .ToList()
                : points.Where(p => p.TypePrestationId == types.GetValueOrDefault(vacation.Id))
                    .Select(p => new PointChecklistDto { PointControleId = p.Id, Libelle = p.Libelle })
                    .ToList();

            vacation.Anomalies = anomalies.Where(a => a.InterventionId == vacation.Id).Select(a => a.Dto).ToList();
        }
    }

    private static PointageDto? VersDto(Pointage? p) => p is null
        ? null
        : new PointageDto
        {
            Horodatage = p.Horodatage,
            Preuve = p.Preuve.ToString(),
            DistanceMetres = p.DistanceMetres,
            PrecisionMetres = p.PrecisionMetres
        };
}
