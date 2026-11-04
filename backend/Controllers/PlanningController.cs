using Klean.Api.DTOs;
using Klean.Api.Filtres;
using Klean.Api.Models;
using Klean.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;

namespace Klean.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/planning")]
public class PlanningController : ControllerBase
{
    private readonly PlanningService _planning;
    private readonly ServiceGeneration _generation;

    public PlanningController(PlanningService planning, ServiceGeneration generation)
    {
        _planning = planning;
        _generation = generation;
    }

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
    /// Crée les vacations à venir depuis la récurrence des prestations, d'aujourd'hui à
    /// <c>jusquAu</c> inclus (SPEC.md, étape 20). Idempotent : rejouée, elle ne recrée
    /// rien, et elle ne modifie ni ne supprime jamais une vacation existante.
    ///
    /// Réservée à <c>Exploitation</c>, comme toute création de vacation.
    ///
    /// Pas journalisée (CLAUDE.md §7 bis) : c'est une écriture, et le rapport ne nomme
    /// aucun agent.
    ///
    /// Délai de deux minutes au lieu du délai général : c'est la seule opération dont la
    /// durée croît avec la taille de l'entreprise.
    /// </summary>
    [Authorize(Roles = RolesKlean.Exploitation)]
    [RequestTimeout(ServiceGeneration.PolitiqueDelai)]
    [HttpPost("generer")]
    public async Task<ActionResult<RapportGenerationDto>> Generer(
        GenerationDemandeDto dto, CancellationToken annulation)
    {
        if (dto.JusquAu is null)
        {
            return BadRequest(
                "Champ « jusquAu » obligatoire, au format AAAA-MM-JJ : le dernier jour à générer, inclus.");
        }

        var jusquAu = DateOnly.FromDateTime(dto.JusquAu.Value);

        // Le jour du territoire de l'entreprise, pas le jour UTC (Territoires).
        var aujourdhui = await _generation.AujourdhuiAsync(annulation);
        var horizon = aujourdhui.AddDays(ServiceGeneration.SemainesMaximum * 7);

        if (jusquAu < aujourdhui)
        {
            return BadRequest(
                $"La date {jusquAu:yyyy-MM-dd} est passée. La génération part d'aujourd'hui, "
                + $"{aujourdhui:yyyy-MM-dd}, et ne crée jamais de vacation dans le passé.");
        }

        if (jusquAu > horizon)
        {
            return BadRequest(
                $"La génération porte sur {ServiceGeneration.SemainesMaximum} semaines au plus, "
                + $"soit jusqu'au {horizon:yyyy-MM-dd}. Générer plus loin figerait "
                + "un planning que les absences et les contrats auront changé d'ici là.");
        }

        return Ok(await _generation.GenererAsync(jusquAu, annulation));
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
