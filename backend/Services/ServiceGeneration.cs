using Klean.Api.Data;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Services;

/// <summary>
/// Crée les vacations à venir à partir de la récurrence des prestations (SPEC.md,
/// étape 20).
///
/// Quatre règles, chacune arbitrée :
///
/// 1. <b>Elle n'ajoute que.</b> Aucune vacation existante n'est modifiée ni supprimée,
///    quel que soit l'écart avec sa prestation : le rapport signale les orphelines, un
///    humain décide.
/// 2. <b>Elle est idempotente</b> sur le couple prestation et jour. Une vacation de la
///    prestation ce jour-là, <i>quel que soit son statut</i>, suffit à ne rien créer —
///    une annulation est une décision, que la génération suivante ne doit pas défaire.
/// 3. <b>Le titulaire n'est affecté que s'il est éligible</b>, par
///    <see cref="AffectationService.EstEligibleAsync"/> : aucun chemin n'affecte un agent
///    inéligible (CLAUDE.md §9), celui-ci pas plus que les autres.
/// 4. <b>Les jours fériés sont sautés</b>, sauf contrat marqué <c>InclutJoursFeries</c>.
///
/// Comme partout, le filtrage multi-tenant est celui du <c>DbContext</c> : la génération
/// s'exécute dans une requête authentifiée, ce qui est précisément la raison pour
/// laquelle elle est à la demande et non une tâche de fond.
/// </summary>
public class ServiceGeneration
{
    /// <summary>Horizon maximal, compté à partir d'aujourd'hui.</summary>
    public const int SemainesMaximum = 12;

    /// <summary>Politique de délai de requête propre à la génération, déclarée dans <c>Program.cs</c>.</summary>
    public const string PolitiqueDelai = "generation";

    private readonly KleanDbContext _db;
    private readonly AffectationService _affectation;
    private readonly ITenantContext _tenant;
    private readonly TimeProvider _horloge;

    public ServiceGeneration(
        KleanDbContext db, AffectationService affectation, ITenantContext tenant, TimeProvider horloge)
    {
        _db = db;
        _affectation = affectation;
        _tenant = tenant;
        _horloge = horloge;
    }

    /// <summary>
    /// Le jour UTC. ⚠️ Entre minuit et une ou deux heures du matin à Paris, c'est encore
    /// la veille : la génération peut alors créer une vacation du jour qui vient de
    /// finir. Sans conséquence au-delà du bruit — elle est idempotente et l'écran la
    /// montre — et un fuseau par entreprise n'existe pas dans le modèle.
    /// </summary>
    public DateOnly Aujourdhui => DateOnly.FromDateTime(_horloge.GetUtcNow().UtcDateTime);

    public DateOnly Horizon => Aujourdhui.AddDays(SemainesMaximum * 7);

    public const string CauseSansTitulaire = "Aucun titulaire désigné sur la prestation.";

    public const string CauseTitulaireIneligible =
        "Le titulaire n'est pas candidat ce jour-là : il est inactif, absent, déjà affecté "
        + "sur une vacation qui chevauche, ou son repos de 11 h ne serait pas respecté.";

    /// <summary>
    /// Génère d'aujourd'hui à <paramref name="jusquAu"/> inclus. Les bornes sont vérifiées
    /// par le contrôleur, qui en fait des 400.
    /// </summary>
    /// <param name="annulation">
    /// ⚠️ Celui de la requête, que le délai déclenche. Sans lui, un délai dépassé laissait
    /// la génération aller au bout et valider, puis coupait la réponse en cours
    /// d'écriture : un 200 au corps tronqué, sur un travail fait — mesuré. Avec lui, la
    /// transaction est défaite et l'appelant reçoit un 504 sur un travail qui n'a pas eu
    /// lieu. L'idempotence rendait la première forme rattrapable ; seule la seconde est
    /// lisible.
    /// </param>
    public async Task<RapportGenerationDto> GenererAsync(DateOnly jusquAu, CancellationToken annulation = default)
    {
        var du = Aujourdhui;

        var rapport = new RapportGenerationDto { Du = Utc(du), Au = Utc(jusquAu) };

        // Tout ou rien : une génération interrompue au milieu laisserait une période à
        // moitié remplie, que l'idempotence rattraperait mais que personne ne verrait.
        await using var transaction = await _db.Database.BeginTransactionAsync(annulation);

        // ⚠️ Deux générations simultanées d'une même entreprise — un double clic suffit —
        // liraient chacune « rien n'existe » et créeraient chaque vacation deux fois :
        // l'idempotence se lit avant d'écrire, elle ne protège pas d'une course. Le verrou
        // consultatif de PostgreSQL sérialise les deux, et tombe avec la transaction.
        // Aucune contrainte d'unicité ne peut le remplacer : deux vacations d'une même
        // prestation le même jour sont légitimes quand on les saisit à la main.
        await _db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({CleDeVerrou()})", annulation);

        var prestations = await PrestationsGenerablesAsync(du, jusquAu, annulation);

        rapport.Orphelines = await OrphelinesAsync(du, jusquAu, annulation);

        var dejaPresentes = await CreneauxOccupesAsync(
            prestations.Select(p => p.Id).ToList(), du, jusquAu, annulation);

        for (var jour = du; jour <= jusquAu; jour = jour.AddDays(1))
        {
            var drapeau = JoursDeLaSemaine.De(jour.DayOfWeek);
            var ferie = JoursFeries.Nom(jour);

            // L'ordre chronologique n'est pas cosmétique : l'éligibilité d'un titulaire
            // dépend de ses vacations de la veille et du jour même — repos de 11 h,
            // chevauchement —, donc de celles que cette boucle vient de créer.
            foreach (var prestation in prestations
                         .Where(p => p.Jours.HasFlag(drapeau) && p.Couvre(jour))
                         .OrderBy(p => p.HeureDebut))
            {
                annulation.ThrowIfCancellationRequested();

                if (ferie is not null && !prestation.InclutJoursFeries)
                {
                    rapport.Feries.Add(Ligne(prestation, jour, null, $"{ferie}, que le contrat n'inclut pas."));
                    continue;
                }

                if (dejaPresentes.Contains((prestation.Id, jour)))
                {
                    rapport.DejaPresentes++;
                    continue;
                }

                await CreerAsync(prestation, jour, rapport, annulation);
            }
        }

        await _db.SaveChangesAsync(annulation);
        await transaction.CommitAsync(annulation);

        return rapport;
    }

    // ------------------------------------------------------------------- création

    private async Task CreerAsync(
        PrestationGenerable prestation, DateOnly jour, RapportGenerationDto rapport, CancellationToken annulation)
    {
        Guid? agent = null;
        var cause = CauseSansTitulaire;

        if (prestation.AgentTitulaireId is { } titulaire)
        {
            var creneau = new Creneau(
                prestation.SiteId, prestation.Latitude, prestation.Longitude,
                Utc(jour), prestation.HeureDebut, prestation.DureeEstimeeMinutes);

            if (await _affectation.EstEligibleAsync(titulaire, creneau, interventionAExclure: null))
            {
                agent = titulaire;
            }
            else
            {
                cause = CauseTitulaireIneligible;
            }
        }

        var intervention = new Intervention
        {
            PrestationId = prestation.Id,
            AgentId = agent,
            DatePrevue = Utc(jour),
            HeurePrevue = prestation.HeureDebut,
            Statut = StatutIntervention.Planifiee
        };

        _db.Interventions.Add(intervention);
        rapport.Creees++;

        if (agent is null)
        {
            rapport.NonPourvues.Add(Ligne(prestation, jour, intervention.Id, cause));
            return;
        }

        rapport.Pourvues++;

        // ⚠️ Écrite tout de suite, et c'est ce qui rend la règle 3 vraie : le moteur lit
        // la base, et une affectation encore en mémoire ne compterait ni comme
        // chevauchement ni comme repos pour la vacation suivante du même agent. Une
        // vacation sans agent n'occupe personne, elle attend l'écriture finale. Le suivi
        // est vidé à chaque fois, sinon chaque écriture parcourrait toutes les précédentes.
        await _db.SaveChangesAsync(annulation);
        _db.ChangeTracker.Clear();
    }

    // ------------------------------------------------------------------- lectures

    private async Task<List<PrestationGenerable>> PrestationsGenerablesAsync(
        DateOnly du, DateOnly au, CancellationToken annulation)
    {
        var debut = Utc(du);
        var fin = Utc(au);

        return await _db.Prestations
            .AsNoTracking()
            .Where(p => p.Jours != JoursSemaine.Aucun
                        && p.HeureDebut != null
                        && p.Contrat!.Actif
                        && p.Contrat!.DateDebut <= fin
                        && (p.Contrat!.DateFin == null || p.Contrat!.DateFin >= debut))
            .Select(p => new PrestationGenerable(
                p.Id,
                p.Libelle,
                p.Jours,
                p.HeureDebut!.Value,
                p.DureeEstimeeMinutes,
                p.AgentTitulaireId,
                p.Contrat!.DateDebut,
                p.Contrat!.DateFin,
                p.Contrat!.InclutJoursFeries,
                p.Contrat!.SiteId,
                p.Contrat!.Site!.Nom,
                p.Contrat!.Site!.Latitude,
                p.Contrat!.Site!.Longitude))
            .ToListAsync(annulation);
    }

    /// <summary>Couples prestation et jour qui portent déjà une vacation, tous statuts confondus.</summary>
    private async Task<HashSet<(Guid, DateOnly)>> CreneauxOccupesAsync(
        List<Guid> prestations, DateOnly du, DateOnly au, CancellationToken annulation)
    {
        var debut = Utc(du);
        var lendemain = Utc(au.AddDays(1));

        var existantes = await _db.Interventions
            .AsNoTracking()
            .Where(i => prestations.Contains(i.PrestationId)
                        && i.DatePrevue >= debut
                        && i.DatePrevue < lendemain)
            .Select(i => new { i.PrestationId, i.DatePrevue })
            .ToListAsync(annulation);

        return existantes
            .Select(e => (e.PrestationId, DateOnly.FromDateTime(e.DatePrevue)))
            .ToHashSet();
    }

    /// <summary>
    /// Vacations planifiées de la période qui ne correspondent plus à leur contrat ou à
    /// leur prestation. Seules les <c>Planifiee</c> : une vacation commencée ou terminée
    /// est un fait, pas un plan à revoir.
    ///
    /// ⚠️ La génération ne marque pas ce qu'elle crée, donc une vacation ajoutée à la main
    /// un jour non prévu par une prestation récurrente est signalée aussi. C'est voulu :
    /// le rapport demande un regard, il n'accuse personne. Et une prestation dont on a
    /// <i>retiré</i> tous les jours n'est plus récurrente — seules les règles du contrat
    /// s'appliquent alors à ses vacations.
    /// </summary>
    private async Task<List<LigneRapportGenerationDto>> OrphelinesAsync(
        DateOnly du, DateOnly au, CancellationToken annulation)
    {
        var debut = Utc(du);
        var lendemain = Utc(au.AddDays(1));

        var vacations = await _db.Interventions
            .AsNoTracking()
            .Where(i => i.Statut == StatutIntervention.Planifiee
                        && i.DatePrevue >= debut
                        && i.DatePrevue < lendemain)
            .OrderBy(i => i.DatePrevue).ThenBy(i => i.HeurePrevue)
            .Select(i => new
            {
                i.Id,
                i.PrestationId,
                i.DatePrevue,
                i.HeurePrevue,
                Prestation = i.Prestation!.Libelle,
                Site = i.Prestation!.Contrat!.Site!.Nom,
                i.Prestation!.Jours,
                i.Prestation!.HeureDebut,
                i.Prestation!.Contrat!.Actif,
                i.Prestation!.Contrat!.DateDebut,
                i.Prestation!.Contrat!.DateFin,
                i.Prestation!.Contrat!.InclutJoursFeries
            })
            .ToListAsync(annulation);

        var orphelines = new List<LigneRapportGenerationDto>();

        foreach (var v in vacations)
        {
            var jour = DateOnly.FromDateTime(v.DatePrevue);
            var recurrente = v.Jours != JoursSemaine.Aucun && v.HeureDebut is not null;
            var ferie = JoursFeries.Nom(jour);

            string? cause =
                !v.Actif ? "Le contrat n'est plus actif."
                : jour < DateOnly.FromDateTime(v.DateDebut)
                  || (v.DateFin is { } fin && jour > DateOnly.FromDateTime(fin))
                    ? "Hors des dates du contrat."
                : !recurrente ? null
                : !v.Jours.HasFlag(JoursDeLaSemaine.De(jour.DayOfWeek))
                    ? "Ce jour n'est pas prévu par la prestation."
                : v.HeurePrevue != v.HeureDebut
                    ? "L'heure ne correspond pas à celle de la prestation."
                : ferie is not null && !v.InclutJoursFeries
                    ? $"{ferie}, que le contrat n'inclut pas."
                : null;

            if (cause is null) continue;

            orphelines.Add(new LigneRapportGenerationDto
            {
                InterventionId = v.Id,
                Date = Utc(jour),
                Heure = v.HeurePrevue,
                PrestationId = v.PrestationId,
                Prestation = v.Prestation,
                Site = v.Site,
                Cause = cause
            });
        }

        return orphelines;
    }

    // ------------------------------------------------------------------ outillage

    /// <summary>
    /// Clé du verrou consultatif : l'entreprise, ramenée à 64 bits. Une collision entre
    /// deux entreprises ne ferait que sérialiser leurs générations, sans rien fausser.
    /// </summary>
    private long CleDeVerrou() =>
        BitConverter.ToInt64((_tenant.EntrepriseId ?? Guid.Empty).ToByteArray(), 0);

    /// <summary>Minuit UTC — Npgsql refuse un <c>DateTime</c> non daté (CLAUDE.md §10).</summary>
    private static DateTime Utc(DateOnly jour) =>
        DateTime.SpecifyKind(jour.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

    private static LigneRapportGenerationDto Ligne(
        PrestationGenerable prestation, DateOnly jour, Guid? interventionId, string cause) => new()
    {
        InterventionId = interventionId,
        Date = Utc(jour),
        Heure = prestation.HeureDebut,
        PrestationId = prestation.Id,
        Prestation = prestation.Libelle,
        Site = prestation.SiteNom,
        Cause = cause
    };

    private sealed record PrestationGenerable(
        Guid Id,
        string Libelle,
        JoursSemaine Jours,
        TimeSpan HeureDebut,
        int DureeEstimeeMinutes,
        Guid? AgentTitulaireId,
        DateTime ContratDebut,
        DateTime? ContratFin,
        bool InclutJoursFeries,
        Guid SiteId,
        string SiteNom,
        double Latitude,
        double Longitude)
    {
        /// <summary>Le contrat court-il ce jour-là ? Bornes incluses.</summary>
        public bool Couvre(DateOnly jour) =>
            jour >= DateOnly.FromDateTime(ContratDebut)
            && (ContratFin is null || jour <= DateOnly.FromDateTime(ContratFin.Value));
    }
}
