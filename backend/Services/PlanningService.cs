using Klean.Api.Data;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Services;

/// <summary>
/// Vue planning d'une semaine, du lundi au dimanche.
///
/// Trois requêtes, puis tout s'assemble en mémoire : les agents d'une entreprise
/// de propreté se comptent en dizaines et une semaine porte quelques centaines de
/// vacations. Une agrégation SQL par agent et par jour serait plus rapide et
/// beaucoup moins lisible, pour un gain invisible à cette échelle.
///
/// Comme partout, le filtrage multi-tenant est celui du <c>DbContext</c> : ce
/// fichier ne mentionne jamais l'entreprise.
/// </summary>
public class PlanningService
{
    private readonly KleanDbContext _db;

    public PlanningService(KleanDbContext db) => _db = db;

    /// <summary>
    /// Planning de la semaine commençant au lundi donné.
    /// </summary>
    /// <param name="lundi">
    /// Minuit UTC d'un lundi. La validation du jour de la semaine et la
    /// conversion depuis la requête appartiennent au contrôleur : c'est là que
    /// vit le 400.
    /// </param>
    public async Task<PlanningSemaineDto> GetSemaineAsync(DateTime lundi)
    {
        var lundiSuivant = lundi.AddDays(7);

        // Les vacations annulées ne sont pas du travail : elles sortent du
        // planning et de tous les agrégats, comme elles sortent des filtres du
        // moteur d'affectation.
        var vacations = await _db.Interventions
            .AsNoTracking()
            .Where(i => i.DatePrevue >= lundi
                        && i.DatePrevue < lundiSuivant
                        && i.Statut != StatutIntervention.Annulee)
            .Select(i => new LigneVacation(
                i.Id,
                i.AgentId,
                i.DatePrevue,
                i.HeurePrevue,
                i.Prestation!.DureeEstimeeMinutes,
                i.Prestation!.Contrat!.SiteId,
                i.Prestation!.Contrat!.Site!.Nom,
                i.Prestation!.Libelle,
                i.Statut))
            .ToListAsync();

        var absencesParAgent = await AbsencesParAgentAsync(lundi, lundiSuivant);

        var idsAffectes = vacations
            .Where(v => v.AgentId.HasValue)
            .Select(v => v.AgentId!.Value)
            .Distinct()
            .ToList();

        // Les agents inactifs apparaissent quand ils portent encore une vacation
        // de la semaine : sinon leurs heures compteraient dans le total sans
        // qu'aucune ligne ne les explique, et le planning ne s'additionnerait plus.
        var agents = await _db.Agents
            .AsNoTracking()
            .Where(a => a.Actif || idsAffectes.Contains(a.Id))
            .OrderBy(a => a.Nom).ThenBy(a => a.Prenom)
            .Select(a => new { a.Id, a.Nom, a.Prenom })
            .ToListAsync();

        var parAgent = vacations
            .Where(v => v.AgentId.HasValue)
            .GroupBy(v => v.AgentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var lignes = new List<PlanningAgentDto>();

        foreach (var agent in agents)
        {
            var siennes = parAgent.TryGetValue(agent.Id, out var liste) ? liste : [];

            lignes.Add(new PlanningAgentDto
            {
                AgentId = agent.Id,
                Nom = agent.Nom,
                Prenom = agent.Prenom,
                NombreVacations = siennes.Count,
                Heures = Heures(siennes),
                Vacations = siennes
                    .OrderBy(v => v.DatePrevue).ThenBy(v => v.HeurePrevue)
                    .Select(v => new PlanningVacationDto
                    {
                        InterventionId = v.Id,
                        Date = v.DatePrevue,
                        Heure = v.HeurePrevue,
                        DureeMinutes = v.DureeMinutes,
                        SiteId = v.SiteId,
                        SiteNom = v.SiteNom,
                        PrestationLibelle = v.PrestationLibelle,
                        Statut = v.Statut.ToString(),
                        NonPourvue = EstAbsent(absencesParAgent, agent.Id, v.DatePrevue)
                    })
                    .ToList()
            });
        }

        // Deux causes, un seul concept : la vacation n'aura personne dessus.
        var nonPourvues = vacations
            .Where(v => !v.AgentId.HasValue
                        || EstAbsent(absencesParAgent, v.AgentId!.Value, v.DatePrevue))
            .OrderBy(v => v.DatePrevue).ThenBy(v => v.HeurePrevue)
            .Select(v => new PlanningNonPourvueDto
            {
                InterventionId = v.Id,
                Date = v.DatePrevue,
                Heure = v.HeurePrevue,
                DureeMinutes = v.DureeMinutes,
                SiteId = v.SiteId,
                SiteNom = v.SiteNom,
                PrestationLibelle = v.PrestationLibelle,
                AgentId = v.AgentId,
                Cause = v.AgentId.HasValue ? "Agent absent" : "Aucun agent affecté"
            })
            .ToList();

        return new PlanningSemaineDto
        {
            Lundi = lundi,
            Dimanche = lundi.AddDays(6),
            Agents = lignes,
            NonPourvues = nonPourvues,
            TotalVacations = vacations.Count,
            TotalHeures = Heures(vacations),
            TotalNonPourvues = nonPourvues.Count
        };
    }

    /// <summary>
    /// Absences chevauchant la semaine, regroupées par agent. Bornes réduites au
    /// jour : une absence saisie « du 7 au 9 » arrive avec les heures à zéro, et
    /// couvre pourtant le 9 en entier. Même règle que les filtres durs du §8.
    /// </summary>
    private async Task<Dictionary<Guid, List<(DateTime Debut, DateTime Fin)>>> AbsencesParAgentAsync(
        DateTime lundi, DateTime lundiSuivant)
    {
        var lignes = await _db.Absences
            .AsNoTracking()
            .Where(a => a.DateDebut < lundiSuivant && a.DateFin >= lundi)
            .Select(a => new { a.AgentId, a.DateDebut, a.DateFin })
            .ToListAsync();

        return lignes
            .GroupBy(a => a.AgentId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(a => (a.DateDebut.Date, a.DateFin.Date)).ToList());
    }

    private static bool EstAbsent(
        Dictionary<Guid, List<(DateTime Debut, DateTime Fin)>> absences, Guid agentId, DateTime jour)
    {
        if (!absences.TryGetValue(agentId, out var siennes)) return false;

        var date = jour.Date;

        return siennes.Any(a => a.Debut <= date && a.Fin >= date);
    }

    private static double Heures(List<LigneVacation> vacations) =>
        Math.Round(vacations.Sum(v => v.DureeMinutes) / 60.0, 2);

    /// <summary>Projection plate d'une vacation, avant regroupement.</summary>
    private record LigneVacation(
        Guid Id,
        Guid? AgentId,
        DateTime DatePrevue,
        TimeSpan HeurePrevue,
        int DureeMinutes,
        Guid SiteId,
        string SiteNom,
        string PrestationLibelle,
        StatutIntervention Statut);
}
