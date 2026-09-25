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

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PrestationDto>>> Lister()
    {
        var prestations = await _db.Prestations
            .AsNoTracking()
            .OrderBy(p => p.Libelle)
            .Select(p => new PrestationDto
            {
                Id = p.Id,
                EntrepriseId = p.EntrepriseId,
                ContratId = p.ContratId,
                Libelle = p.Libelle,
                DureeEstimeeMinutes = p.DureeEstimeeMinutes
            })
            .ToListAsync();

        return Ok(prestations);
    }

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
            return Conflict("Suppression refusée par la base : des données rattachées l'empêchent.");
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
