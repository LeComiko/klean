using Klean.Api.Data;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Services;

/// <summary>
/// Classe les agents candidats à une intervention (CLAUDE.md §8).
///
/// Deux temps nettement séparés : des <b>filtres durs</b> qui retirent un agent
/// de la liste, puis un <b>score</b> qui ordonne ceux qui restent. Un agent
/// écarté par un filtre dur n'apparaît pas avec une mauvaise note : il
/// n'apparaît pas du tout. La confusion entre les deux est la faute classique
/// de ce genre de moteur — un agent en congé qui remonte en queue de liste
/// finit par être proposé un jour de forte tension.
///
/// Toutes les lectures passent par le contexte filtré : le moteur ne voit que
/// les agents du tenant, sans qu'une seule ligne de ce fichier ne mentionne
/// l'entreprise.
/// </summary>
public class AffectationService
{
    private readonly KleanDbContext _db;

    public AffectationService(KleanDbContext db) => _db = db;

    /// <summary>Rayon terrestre moyen, en kilomètres.</summary>
    private const double RayonTerreKm = 6371;

    /// <summary>Repos légal entre deux vacations.</summary>
    private const double ReposHeures = 11;

    private const int PassagesPourNoteMaximale = 10;
    private const double DistanceNulleKm = 30;
    private const double ChargePleineHeures = 35;

    private const double PoidsExperience = 35;
    private const double PoidsDistance = 30;
    private const double PoidsCharge = 35;

    /// <summary>Note attribuée quand l'agent n'a pas de coordonnées.</summary>
    private const double NoteDistanceNeutre = 50;

    /// <summary>
    /// Candidats classés par score décroissant, ou <c>null</c> si l'intervention
    /// n'existe pas — le contrôleur en fait un 404.
    /// </summary>
    public async Task<List<CandidatDto>?> GetCandidatsAsync(Guid interventionId)
    {
        var cible = await _db.Interventions
            .AsNoTracking()
            .Where(i => i.Id == interventionId)
            .Select(i => new
            {
                i.Id,
                i.DatePrevue,
                i.HeurePrevue,
                DureeMinutes = i.Prestation!.DureeEstimeeMinutes,
                SiteId = i.Prestation!.Contrat!.SiteId,
                SiteLatitude = i.Prestation!.Contrat!.Site!.Latitude,
                SiteLongitude = i.Prestation!.Contrat!.Site!.Longitude
            })
            .FirstOrDefaultAsync();

        if (cible is null) return null;

        // Le créneau à pourvoir. DatePrevue porte le jour, HeurePrevue l'heure :
        // les deux se recomposent ici, une fois, plutôt qu'à chaque comparaison.
        var jour = DateTime.SpecifyKind(cible.DatePrevue.Date, DateTimeKind.Utc);
        var debut = jour + cible.HeurePrevue;
        var fin = debut.AddMinutes(cible.DureeMinutes);

        var reposDebut = debut.AddHours(-ReposHeures);
        var reposFin = fin.AddHours(ReposHeures);

        // Semaine du lundi au dimanche, pour la note de charge.
        var decalageLundi = ((int)jour.DayOfWeek + 6) % 7;
        var semaineDebut = jour.AddDays(-decalageLundi);
        var semaineFin = semaineDebut.AddDays(7);

        var agents = await _db.Agents
            .AsNoTracking()
            .Where(a => a.Actif)
            .Select(a => new { a.Id, a.Nom, a.Prenom, a.Latitude, a.Longitude })
            .ToListAsync();

        if (agents.Count == 0) return [];

        var absents = await AgentsAbsentsAsync(jour);
        var occupations = await OccupationsAsync(cible.Id, reposDebut, reposFin, semaineDebut, semaineFin);
        var passages = await PassagesParAgentAsync(cible.SiteId, jour);

        var candidats = new List<CandidatDto>();

        foreach (var agent in agents)
        {
            if (absents.Contains(agent.Id)) continue;

            var siennes = occupations.TryGetValue(agent.Id, out var liste) ? liste : [];

            if (siennes.Any(o => Chevauche(o, debut, fin))) continue;
            if (siennes.Any(o => ReposInsuffisant(o, jour, debut, fin, reposDebut, reposFin))) continue;

            var nombrePassages = passages.TryGetValue(agent.Id, out var n) ? n : 0;

            var distanceKm = agent.Latitude.HasValue && agent.Longitude.HasValue
                ? Haversine(agent.Latitude.Value, agent.Longitude.Value,
                            cible.SiteLatitude, cible.SiteLongitude)
                : (double?)null;

            var heuresSemaine = siennes
                .Where(o => o.Debut >= semaineDebut && o.Debut < semaineFin)
                .Sum(o => o.DureeMinutes) / 60.0;

            var noteExperience =
                Math.Min(nombrePassages, PassagesPourNoteMaximale) / (double)PassagesPourNoteMaximale * 100;

            var noteDistance = distanceKm.HasValue
                ? Math.Max(0, (DistanceNulleKm - distanceKm.Value) / DistanceNulleKm * 100)
                : NoteDistanceNeutre;

            var noteCharge =
                Math.Max(0, (ChargePleineHeures - heuresSemaine) / ChargePleineHeures * 100);

            var score = (noteExperience * PoidsExperience
                         + noteDistance * PoidsDistance
                         + noteCharge * PoidsCharge)
                        / (PoidsExperience + PoidsDistance + PoidsCharge);

            candidats.Add(new CandidatDto
            {
                AgentId = agent.Id,
                Nom = agent.Nom,
                Prenom = agent.Prenom,
                Score = Math.Round(score, 2),
                NoteExperience = Math.Round(noteExperience, 2),
                NoteDistance = Math.Round(noteDistance, 2),
                NoteCharge = Math.Round(noteCharge, 2),
                Passages = nombrePassages,
                DistanceKm = distanceKm.HasValue ? Math.Round(distanceKm.Value, 2) : null,
                HeuresSemaine = Math.Round(heuresSemaine, 2)
            });
        }

        return candidats
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Nom)
            .ThenBy(c => c.Prenom)
            .ToList();
    }

    /// <summary>
    /// Agents dont une absence couvre le jour de l'intervention.
    ///
    /// Comparaison au jour et non à la minute, volontairement : une absence
    /// saisie « du 7 au 9 » arrive avec les heures à zéro, et un test de
    /// chevauchement strict laisserait passer l'agent pour une vacation de 8 h 30
    /// le 9. L'absence couvre des journées entières, la comparaison aussi.
    /// </summary>
    private async Task<HashSet<Guid>> AgentsAbsentsAsync(DateTime jour)
    {
        var lendemain = jour.AddDays(1);

        var ids = await _db.Absences
            .AsNoTracking()
            .Where(a => a.DateDebut < lendemain && a.DateFin >= jour)
            .Select(a => a.AgentId)
            .Distinct()
            .ToListAsync();

        return [.. ids];
    }

    /// <summary>
    /// Interventions déjà affectées, sur la fenêtre utile : celle du repos légal
    /// réunie à celle de la semaine. Une seule requête, puis tout se calcule en
    /// mémoire — les agents d'une entreprise se comptent en dizaines, pas en
    /// millions.
    /// </summary>
    private async Task<Dictionary<Guid, List<Occupation>>> OccupationsAsync(
        Guid interventionId, DateTime reposDebut, DateTime reposFin,
        DateTime semaineDebut, DateTime semaineFin)
    {
        // Le filtrage SQL porte sur DatePrevue, donc au jour : la fenêtre est
        // élargie aux journées entières, le chevauchement exact est tranché
        // ensuite à la minute.
        var borneBasse = (reposDebut < semaineDebut ? reposDebut : semaineDebut).Date;
        var borneHaute = (reposFin > semaineFin ? reposFin : semaineFin).Date;

        var lignes = await _db.Interventions
            .AsNoTracking()
            .Where(i => i.Id != interventionId
                        && i.AgentId != null
                        && i.Statut != StatutIntervention.Annulee
                        && i.DatePrevue >= DateTime.SpecifyKind(borneBasse, DateTimeKind.Utc)
                        && i.DatePrevue <= DateTime.SpecifyKind(borneHaute, DateTimeKind.Utc))
            .Select(i => new
            {
                AgentId = i.AgentId!.Value,
                i.DatePrevue,
                i.HeurePrevue,
                i.Statut,
                DureeMinutes = i.Prestation!.DureeEstimeeMinutes
            })
            .ToListAsync();

        return lignes
            .Select(l => new Occupation(
                l.AgentId,
                DateTime.SpecifyKind(l.DatePrevue.Date, DateTimeKind.Utc) + l.HeurePrevue,
                l.DureeMinutes,
                l.Statut))
            .GroupBy(o => o.AgentId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>
    /// Passages antérieurs de chaque agent sur le site, tous statuts sauf
    /// <c>Annulee</c>. Seul le passé compte : un agent affecté trois fois la
    /// semaine prochaine ne connaît pas encore le site.
    /// </summary>
    private async Task<Dictionary<Guid, int>> PassagesParAgentAsync(Guid siteId, DateTime jour)
    {
        return await _db.Interventions
            .AsNoTracking()
            .Where(i => i.AgentId != null
                        && i.Statut != StatutIntervention.Annulee
                        && i.DatePrevue < jour
                        && i.Prestation!.Contrat!.SiteId == siteId)
            .GroupBy(i => i.AgentId!.Value)
            .Select(g => new { AgentId = g.Key, Nombre = g.Count() })
            .ToDictionaryAsync(x => x.AgentId, x => x.Nombre);
    }

    /// <summary>
    /// Chevauchement d'horaire. Restreint aux statuts <c>Planifiee</c> et
    /// <c>EnCours</c> (CLAUDE.md §8) : une vacation terminée ou non certifiée
    /// n'immobilise plus l'agent, la règle de repos s'en charge.
    /// </summary>
    private static bool Chevauche(Occupation o, DateTime debut, DateTime fin) =>
        (o.Statut == StatutIntervention.Planifiee || o.Statut == StatutIntervention.EnCours)
        && o.Debut < fin && o.Fin > debut;

    /// <summary>
    /// Repos légal de 11 h, entre deux <b>journées</b> de travail.
    ///
    /// ⚠️ <b>Écart assumé avec la lettre de CLAUDE.md §8</b>, qui écrit « une
    /// autre intervention se termine moins de 11 h avant, ou commence moins de
    /// 11 h après » sans restriction de jour. Appliquée telle quelle, la règle
    /// interdit à un agent d'enchaîner deux sites dans la même matinée : une
    /// vacation à 11 h tombe à 30 minutes de la fin de celle de 8 h 30, donc à
    /// moins de 11 h, donc l'agent disparaît de la liste. C'est précisément le
    /// symptôme que §8 annonçait — « si la liste des candidats revient vide,
    /// c'est la première piste » — et le métier le rend inacceptable : le
    /// nettoyage de bureaux vit d'agents qui font trois ou quatre sites par
    /// jour.
    ///
    /// Le repos quotidien de l'article L3131-1 s'intercale entre deux journées
    /// de travail, pas entre deux tâches d'une même journée. Deux vacations du
    /// même jour sont donc exemptées ; seul le chevauchement les sépare.
    ///
    /// Contrairement au chevauchement, tous les statuts comptent sauf
    /// <c>Annulee</c> : une vacation terminée hier soir a bel et bien été
    /// travaillée.
    /// </summary>
    private static bool ReposInsuffisant(
        Occupation o, DateTime jour, DateTime debut, DateTime fin,
        DateTime reposDebut, DateTime reposFin)
    {
        if (o.Debut.Date == jour.Date) return false;

        return (o.Fin <= debut && o.Fin > reposDebut)
               || (o.Debut >= fin && o.Debut < reposFin);
    }

    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = Radians(lat2 - lat1);
        var dLon = Radians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(Radians(lat1)) * Math.Cos(Radians(lat2))
                  * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        return RayonTerreKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double Radians(double degres) => degres * Math.PI / 180;

    /// <summary>Créneau déjà occupé par un agent.</summary>
    private readonly record struct Occupation(
        Guid AgentId, DateTime Debut, int DureeMinutes, StatutIntervention Statut)
    {
        public DateTime Fin => Debut.AddMinutes(DureeMinutes);
    }
}
