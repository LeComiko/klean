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
    /// Quatre filtres, qui sont les questions d'un contrôle : <c>utilisateurId</c>
    /// — qu'a lu cette personne ; <c>ressource</c> — qui a lu les absences ;
    /// <c>nature</c> — qui a exporté ou pseudonymisé (SPEC.md, étape 27) ;
    /// <c>depuis</c> et <c>jusqua</c> — sur quelle période.
    ///
    /// ⚠️ Comme pour les filtres de la chaîne d'exploitation, ils entrent <b>avant</b>
    /// le comptage (CLAUDE.md §6 bis).
    ///
    /// ⚠️ <b><c>jusqua</c> est exclu</b> depuis l'étape 27 : « jusqu'au 12 inclus » s'écrit
    /// le 13 à minuit, sans viser une milliseconde.
    ///
    /// ⚠️ <b>Ressource et nature sont lues par leurs noms exacts</b>, jamais par
    /// <c>Enum.TryParse</c>, qui accepterait « 0 » et filtrerait sur la première valeur
    /// sans que personne l'ait choisie (CLAUDE.md §5).
    /// </summary>
    [Authorize(Roles = RolesKlean.Administrateur)]
    [JournaliserAcces(RessourceAccedee.JournalAcces, NatureAcces.Liste)]
    [HttpGet]
    public async Task<ActionResult<PageDto<JournalAccesDto>>> Lister(
        [FromQuery] PaginationDto pagination,
        [FromQuery] Guid? utilisateurId,
        [FromQuery] string? ressource,
        [FromQuery] string? nature,
        [FromQuery] DateTime? depuis,
        [FromQuery] DateTime? jusqua)
    {
        if (!Lire<RessourceAccedee>(ressource, out var filtreRessource))
        {
            return BadRequest(
                $"Ressource « {ressource} » invalide. Valeurs acceptées : "
                + $"{string.Join(", ", Enum.GetNames<RessourceAccedee>())}.");
        }

        if (!Lire<NatureAcces>(nature, out var filtreNature))
        {
            return BadRequest(
                $"Nature « {nature} » invalide. Valeurs acceptées : "
                + $"{string.Join(", ", Enum.GetNames<NatureAcces>())}.");
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

        if (filtreNature is not null)
        {
            requete = requete.Where(a => a.Nature == filtreNature);
        }

        if (depuis is not null)
        {
            var borne = Utc(depuis.Value);
            requete = requete.Where(a => a.Horodatage >= borne);
        }

        if (jusqua is not null)
        {
            var borne = Utc(jusqua.Value);
            requete = requete.Where(a => a.Horodatage < borne);
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

    /// <summary>
    /// Une valeur nulle ou vide laisse le filtre ouvert ; sinon, seul un nom exact de
    /// l'énumération passe.
    /// </summary>
    private static bool Lire<T>(string? texte, out T? valeur) where T : struct, Enum
    {
        valeur = null;

        if (string.IsNullOrWhiteSpace(texte)) return true;
        if (!Enum.GetNames<T>().Contains(texte)) return false;

        valeur = Enum.Parse<T>(texte);
        return true;
    }

    /// <summary>
    /// L'instant reçu, en UTC.
    ///
    /// Un instant — <c>2026-11-12T00:00:00Z</c> ou <c>…+01:00</c> — arrive déjà converti en
    /// UTC : le lieur de dates d'ASP.NET le normalise, et l'étiquette ne change rien. Une date
    /// nue arrive en <c>Unspecified</c>, que Npgsql refuse (CLAUDE.md §10) : elle est lue comme
    /// minuit UTC.
    ///
    /// ⚠️ Le cadrage de l'étape 27 affirmait l'inverse, mesuré sur le convertisseur de type,
    /// qui rend bien une heure locale. Ce n'est pas lui qui lie la requête : une sonde dans
    /// cette méthode a vu <c>Utc</c> pour les deux formes.
    /// </summary>
    private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
}
