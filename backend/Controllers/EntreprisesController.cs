using Klean.Api.DTOs;
using Klean.Api.Data;
using Klean.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/entreprises")]
public class EntreprisesController : ControllerBase
{
    private readonly KleanDbContext _db;

    public EntreprisesController(KleanDbContext db) => _db = db;

    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet]
    public async Task<ActionResult<PageDto<EntrepriseDto>>> Lister([FromQuery] PaginationDto pagination)
    {
        var requete = _db.Entreprises
            .AsNoTracking()
            .OrderBy(e => e.RaisonSociale);

        var total = await requete.CountAsync();

        var entreprises = await requete
            .Skip(pagination.ANegliger)
            .Take(pagination.Taille)
            .Select(e => new EntrepriseDto
            {
                Id = e.Id,
                RaisonSociale = e.RaisonSociale,
                Siret = e.Siret,
                DateCreation = e.DateCreation,
                Territoire = e.Territoire.ToString(),
                GeocodageActif = e.GeocodageActif,
                GeocodageActiveLe = e.GeocodageActiveLe,
                GeocodageDesactiveLe = e.GeocodageDesactiveLe
            })
            .ToListAsync();

        return Ok(new PageDto<EntrepriseDto>
        {
            Elements = entreprises,
            Total = total,
            Page = pagination.Page,
            Taille = pagination.Taille
        });
    }

    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EntrepriseDto>> Obtenir(Guid id)
    {
        var entreprise = await _db.Entreprises
            .AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new EntrepriseDto
            {
                Id = e.Id,
                RaisonSociale = e.RaisonSociale,
                Siret = e.Siret,
                DateCreation = e.DateCreation,
                Territoire = e.Territoire.ToString(),
                GeocodageActif = e.GeocodageActif,
                GeocodageActiveLe = e.GeocodageActiveLe,
                GeocodageDesactiveLe = e.GeocodageDesactiveLe
            })
            .FirstOrDefaultAsync();

        return entreprise is null ? NotFound() : Ok(entreprise);
    }

    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Modifier(Guid id, EntrepriseCreateDto dto)
    {
        var entreprise = await _db.Entreprises.FindAsync(id);
        if (entreprise is null) return NotFound();

        // Les noms exacts : `Enum.TryParse` accepterait « 0 », c'est-à-dire un territoire que
        // personne n'a choisi (CLAUDE.md §5).
        if (!Enum.GetNames<Territoire>().Contains(dto.Territoire))
        {
            return BadRequest(
                $"Territoire « {dto.Territoire} » invalide. Valeurs acceptées : "
                + $"{string.Join(", ", Enum.GetNames<Territoire>())}. Il fixe le fuseau du jour "
                + "et le calendrier des jours fériés.");
        }

        entreprise.RaisonSociale = dto.RaisonSociale;
        entreprise.Siret = dto.Siret;
        entreprise.Territoire = Enum.Parse<Territoire>(dto.Territoire);

        // ⚠️ Une date par sens, posée au seul changement d'état : un PUT qui ne change pas le
        // réglage — enregistrer la raison sociale — ne doit rien dater.
        if (dto.GeocodageActif != entreprise.GeocodageActif)
        {
            if (dto.GeocodageActif) entreprise.GeocodageActiveLe = DateTime.UtcNow;
            else entreprise.GeocodageDesactiveLe = DateTime.UtcNow;
            entreprise.GeocodageActif = dto.GeocodageActif;
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict($"Le SIRET « {dto.Siret} » est déjà enregistré.");
        }

        return NoContent();
    }
}
