using Klean.Api.DTOs;
using Klean.Api.Filtres;
using Klean.Api.Models;
using Klean.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Klean.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/planning")]
public class PlanningController : ControllerBase
{
    private readonly PlanningService _planning;

    public PlanningController(PlanningService planning) => _planning = planning;

    /// <summary>
    /// Planning d'une semaine. <c>lundi</c> est obligatoire et doit tomber un
    /// lundi.
    ///
    /// Pas de valeur par défaut calculée sur l'horloge du serveur : elle rendrait
    /// la réponse dépendante du moment de l'appel, donc les tests dépendants du
    /// jour où ils tournent, et l'écran incapable de dire quelle semaine il
    /// montre. Le client sait toujours quelle semaine il affiche.
    ///
    /// Réservé aux rôles internes. La grille nomme tous les agents de
    /// l'entreprise, et la matrice des droits ne donne aucune lecture sur les
    /// agents au rôle <c>Agent</c> : le filtre multi-tenant restreindrait bien ses
    /// interventions, mais pas la liste des noms en colonne de gauche. Un agent
    /// qui doit voir son propre planning aura son endpoint dédié.
    /// </summary>
    [JournaliserAcces(RessourceAccedee.Planning, NatureAcces.Liste)]
    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet]
    public async Task<ActionResult<PlanningSemaineDto>> Semaine([FromQuery] DateOnly? lundi)
    {
        if (lundi is null)
        {
            return BadRequest(
                "Paramètre « lundi » obligatoire, au format AAAA-MM-JJ. "
                + "Exemple : /api/planning?lundi=2026-09-07.");
        }

        if (lundi.Value.DayOfWeek != DayOfWeek.Monday)
        {
            return BadRequest(
                $"La date {lundi.Value:yyyy-MM-dd} est un {EnFrancais(lundi.Value.DayOfWeek)}. "
                + "Le planning est hebdomadaire et commence un lundi.");
        }

        // Npgsql refuse un DateTime dont le Kind est Unspecified, ce que produit
        // ToDateTime (CLAUDE.md §10). DateOnly en entrée évite le piège côté
        // liaison ; SpecifyKind le ferme côté requête.
        var debut = DateTime.SpecifyKind(
            lundi.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        return Ok(await _planning.GetSemaineAsync(debut));
    }

    /// <summary>
    /// Le nom du jour, en français. <c>DayOfWeek.ToString()</c> rend « wednesday »
    /// quelle que soit la culture du serveur, et une culture française ne peut pas
    /// être supposée présente sur la machine d'exploitation. Le message part vers
    /// un humain : il est écrit en français comme tout le domaine (CLAUDE.md §11).
    /// </summary>
    private static string EnFrancais(DayOfWeek jour) => jour switch
    {
        DayOfWeek.Monday => "lundi",
        DayOfWeek.Tuesday => "mardi",
        DayOfWeek.Wednesday => "mercredi",
        DayOfWeek.Thursday => "jeudi",
        DayOfWeek.Friday => "vendredi",
        DayOfWeek.Saturday => "samedi",
        _ => "dimanche"
    };
}
