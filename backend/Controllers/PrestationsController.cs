using Klean.Api.DTOs;
using Klean.Api.Data;
using Klean.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/prestations")]
public class PrestationsController : ControllerBase
{
    private readonly KleanDbContext _db;

    public PrestationsController(KleanDbContext db) => _db = db;

    /// <summary>
    /// Liste paginée, filtrable par <c>contratId</c> — la descente de niveau,
    /// « les prestations de ce contrat ».
    ///
    /// ⚠️ Le filtre entre <b>avant</b> le comptage. Un total calculé sur la table
    /// entière annoncerait « 40 prestations » au-dessus d'une liste qui en montre trois,
    /// et la pagination proposerait des pages vides.
    ///
    /// Un identifiant inconnu — ou appartenant à une autre entreprise, que les
    /// filtres globaux écartent — rend une page vide, pas un 404 : une liste filtrée
    /// sur rien reste une liste, et c'est une réponse, pas une erreur.
    /// </summary>
    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet]
    public async Task<ActionResult<PageDto<PrestationDto>>> Lister(
        [FromQuery] PaginationDto pagination,
        [FromQuery] Guid? contratId)
    {
        var requete = _db.Prestations.AsNoTracking();

        if (contratId is not null)
        {
            requete = requete.Where(p => p.ContratId == contratId);
        }

        var total = await requete.CountAsync();

        var prestations = await requete
            .OrderBy(p => p.Libelle)
            .Skip(pagination.ANegliger)
            .Take(pagination.Taille)
            .Select(p => new PrestationDto
            {
                Id = p.Id,
                EntrepriseId = p.EntrepriseId,
                ContratId = p.ContratId,
                Libelle = p.Libelle,
                DureeEstimeeMinutes = p.DureeEstimeeMinutes
            })
            .ToListAsync();

        return Ok(new PageDto<PrestationDto>
        {
            Elements = prestations,
            Total = total,
            Page = pagination.Page,
            Taille = pagination.Taille
        });
    }

    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PrestationDto>> Obtenir(Guid id)
    {
        var prestation = await _db.Prestations
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new PrestationDto
            {
                Id = p.Id,
                EntrepriseId = p.EntrepriseId,
                ContratId = p.ContratId,
                Libelle = p.Libelle,
                DureeEstimeeMinutes = p.DureeEstimeeMinutes
            })
            .FirstOrDefaultAsync();

        return prestation is null ? NotFound() : Ok(prestation);
    }

    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpPost]
    public async Task<ActionResult<PrestationDto>> Creer(PrestationCreateDto dto)
    {
        if (!await _db.Contrats.AnyAsync(c => c.Id == dto.ContratId))
        {
            return BadRequest($"Contrat {dto.ContratId} inconnu.");
        }

        var prestation = new Prestation
        {
            ContratId = dto.ContratId,
            Libelle = dto.Libelle,
            DureeEstimeeMinutes = dto.DureeEstimeeMinutes
        };

        _db.Prestations.Add(prestation);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Obtenir), new { id = prestation.Id }, Vers(prestation));
    }

    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Modifier(Guid id, PrestationCreateDto dto)
    {
        var prestation = await _db.Prestations.FindAsync(id);
        if (prestation is null) return NotFound();

        if (prestation.ContratId != dto.ContratId && !await _db.Contrats.AnyAsync(c => c.Id == dto.ContratId))
        {
            return BadRequest($"Contrat {dto.ContratId} inconnu.");
        }

        prestation.ContratId = dto.ContratId;
        prestation.Libelle = dto.Libelle;
        prestation.DureeEstimeeMinutes = dto.DureeEstimeeMinutes;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Supprimer(Guid id)
    {
        var prestation = await _db.Prestations.FindAsync(id);
        if (prestation is null) return NotFound();

        _db.Prestations.Remove(prestation);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Restrict depuis la migration SuppressionRestreinteSurLaChaineExploitation.
            var interventions = await _db.Interventions.CountAsync(i => i.PrestationId == id);

            return Conflict(
                $"Cette prestation porte {interventions} intervention(s) et ne peut pas "
                + "être supprimée : sa suppression effacerait des vacations du planning, "
                + "certifiées comprises. Supprimer d'abord ces vacations, ou les "
                + "annuler pour les conserver en historique.");
        }

        return NoContent();
    }

    private static PrestationDto Vers(Prestation p) => new()
    {
        Id = p.Id,
        EntrepriseId = p.EntrepriseId,
        ContratId = p.ContratId,
        Libelle = p.Libelle,
        DureeEstimeeMinutes = p.DureeEstimeeMinutes
    };
}
