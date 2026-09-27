using Klean.Api.DTOs;
using Klean.Api.Data;
using Klean.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/sites")]
public class SitesController : ControllerBase
{
    private readonly KleanDbContext _db;

    public SitesController(KleanDbContext db) => _db = db;

    [Authorize(Roles = RolesKlean.InterneEtClientSyndic)]
    [HttpGet]
    public async Task<ActionResult<PageDto<SiteDto>>> Lister([FromQuery] PaginationDto pagination)
    {
        var requete = _db.Sites
            .AsNoTracking()
            .OrderBy(s => s.Nom);

        var total = await requete.CountAsync();

        var sites = await requete
            .Skip(pagination.ANegliger)
            .Take(pagination.Taille)
            .Select(s => new SiteDto
            {
                Id = s.Id,
                EntrepriseId = s.EntrepriseId,
                ClientId = s.ClientId,
                Nom = s.Nom,
                Adresse = s.Adresse,
                CodePostal = s.CodePostal,
                Ville = s.Ville,
                Latitude = s.Latitude,
                Longitude = s.Longitude,
                Consignes = s.Consignes
            })
            .ToListAsync();

        return Ok(new PageDto<SiteDto>
        {
            Elements = sites,
            Total = total,
            Page = pagination.Page,
            Taille = pagination.Taille
        });
    }

    [Authorize(Roles = RolesKlean.InterneEtClientSyndic)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SiteDto>> Obtenir(Guid id)
    {
        var site = await _db.Sites
            .AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new SiteDto
            {
                Id = s.Id,
                EntrepriseId = s.EntrepriseId,
                ClientId = s.ClientId,
                Nom = s.Nom,
                Adresse = s.Adresse,
                CodePostal = s.CodePostal,
                Ville = s.Ville,
                Latitude = s.Latitude,
                Longitude = s.Longitude,
                Consignes = s.Consignes
            })
            .FirstOrDefaultAsync();

        return site is null ? NotFound() : Ok(site);
    }

    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpPost]
    public async Task<ActionResult<SiteDto>> Creer(SiteCreateDto dto)
    {
        if (!await _db.Clients.AnyAsync(c => c.Id == dto.ClientId))
        {
            return BadRequest($"Client {dto.ClientId} inconnu.");
        }

        var site = new Site
        {
            ClientId = dto.ClientId,
            Nom = dto.Nom,
            Adresse = dto.Adresse,
            CodePostal = dto.CodePostal,
            Ville = dto.Ville,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            Consignes = dto.Consignes
        };

        _db.Sites.Add(site);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Obtenir), new { id = site.Id }, Vers(site));
    }

    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Modifier(Guid id, SiteCreateDto dto)
    {
        var site = await _db.Sites.FindAsync(id);
        if (site is null) return NotFound();

        if (site.ClientId != dto.ClientId && !await _db.Clients.AnyAsync(c => c.Id == dto.ClientId))
        {
            return BadRequest($"Client {dto.ClientId} inconnu.");
        }

        site.ClientId = dto.ClientId;
        site.Nom = dto.Nom;
        site.Adresse = dto.Adresse;
        site.CodePostal = dto.CodePostal;
        site.Ville = dto.Ville;
        site.Latitude = dto.Latitude;
        site.Longitude = dto.Longitude;
        site.Consignes = dto.Consignes;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Supprimer(Guid id)
    {
        var site = await _db.Sites.FindAsync(id);
        if (site is null) return NotFound();

        _db.Sites.Remove(site);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Restrict depuis la migration SuppressionRestreinteSurLaChaineExploitation.
            var contrats = await _db.Contrats.CountAsync(c => c.SiteId == id);

            return Conflict(
                $"Ce site porte {contrats} contrat(s) et ne peut pas être supprimé : sa "
                + "suppression effacerait aussi leurs prestations et toutes les "
                + "interventions déjà planifiées. Supprimer d'abord ses contrats.");
        }

        return NoContent();
    }

    private static SiteDto Vers(Site s) => new()
    {
        Id = s.Id,
        EntrepriseId = s.EntrepriseId,
        ClientId = s.ClientId,
        Nom = s.Nom,
        Adresse = s.Adresse,
        CodePostal = s.CodePostal,
        Ville = s.Ville,
        Latitude = s.Latitude,
        Longitude = s.Longitude,
        Consignes = s.Consignes
    };
}
