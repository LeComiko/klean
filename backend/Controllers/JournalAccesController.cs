using Klean.Api.DTOs;
using Klean.Api.Data;
using Klean.Api.Filtres;
using Klean.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Controllers;

/// <summary>
/// Lecture du journal d'accès aux données personnelles (SPEC.md étape 17).
///
/// ⚠️ <b>Lecture seule, et c'est structurel.</b> Aucun <c>POST</c>, <c>PUT</c> ni
/// <c>DELETE</c> : un journal qu'une API permet de modifier ou d'élaguer ne prouve
/// rien. Les lignes sont écrites par le filtre <see cref="JournaliserAccesAttribute"/>,
/// et purgées par <c>Scripts/purge-journal-acces.sql</c> — un geste d'exploitation
/// sur la base, délibérément hors de portée de l'application.
///
/// ⚠️ <b>Administrateur seul.</b> Le journal dit qui a consulté quoi : ouvert au
/// <c>Responsable</c> ou au <c>ChefEquipe</c>, il deviendrait un outil de surveillance
/// des collègues, ce qui est exactement ce qu'un journal RGPD n'est pas censé être.
/// </summary>
[Authorize]
[ApiController]
[Route("api/journal-acces")]
public class JournalAccesController : ControllerBase
{
    private readonly KleanDbContext _db;

    public JournalAccesController(KleanDbContext db) => _db = db;

    /// <summary>
    /// Les accès consignés, du plus récent au plus ancien.
    ///
    /// Trois filtres, qui sont les trois questions d'un contrôle : <c>utilisateurId</c>
    /// — qu'a lu cette personne ; <c>ressource</c> — qui a lu les absences ;
    /// <c>depuis</c> et <c>jusqua</c> — sur quelle période.
    ///
    /// ⚠️ Comme pour les filtres de la chaîne d'exploitation, ils entrent <b>avant</b>
    /// le comptage (CLAUDE.md §6 bis).
    /// </summary>
    [Authorize(Roles = RolesKlean.Administrateur)]
    [JournaliserAcces(RessourceAccedee.JournalAcces, NatureAcces.Liste)]
    [HttpGet]
    public async Task<ActionResult<PageDto<JournalAccesDto>>> Lister(
        [FromQuery] PaginationDto pagination,
        [FromQuery] Guid? utilisateurId,
        [FromQuery] string? ressource,
        [FromQuery] DateTime? depuis,
        [FromQuery] DateTime? jusqua)
    {
        RessourceAccedee? filtreRessource = null;

        if (!string.IsNullOrWhiteSpace(ressource))
        {
            if (!Enum.TryParse<RessourceAccedee>(ressource, out var analysee))
            {
                return BadRequest(
                    $"Ressource « {ressource} » invalide. Valeurs acceptées : "
                    + $"{string.Join(", ", Enum.GetNames<RessourceAccedee>())}.");
            }

            filtreRessource = analysee;
        }

        var requete = _db.AccesJournalises.AsNoTracking();

        if (utilisateurId is not null)
        {
            requete = requete.Where(a => a.UtilisateurId == utilisateurId);
        }

        if (filtreRessource is not null)
        {
            requete = requete.Where(a => a.Ressource == filtreRessource);
        }

        // Npgsql refuse un DateTime dont le Kind est Unspecified, ce que produit la
        // liaison d'une date nue dans la chaîne de requête (CLAUDE.md §10).
        if (depuis is not null)
        {
            var borne = Utc(depuis.Value);
            requete = requete.Where(a => a.Horodatage >= borne);
        }

        if (jusqua is not null)
        {
            var borne = Utc(jusqua.Value);
            requete = requete.Where(a => a.Horodatage <= borne);
        }

        var total = await requete.CountAsync();

        var acces = await requete
            .OrderByDescending(a => a.Horodatage)
            .Skip(pagination.ANegliger)
            .Take(pagination.Taille)
            .Select(a => new JournalAccesDto
            {
                Id = a.Id,
                EntrepriseId = a.EntrepriseId,
                UtilisateurId = a.UtilisateurId,
                Ressource = a.Ressource.ToString(),
                Nature = a.Nature.ToString(),
                CibleId = a.CibleId,
                Lignes = a.Lignes,
                Horodatage = a.Horodatage
            })
            .ToListAsync();

        return Ok(new PageDto<JournalAccesDto>
        {
            Elements = acces,
            Total = total,
            Page = pagination.Page,
            Taille = pagination.Taille
        });
    }

    private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
}
