using Klean.Api.DTOs;
using Klean.Api.Data;
using Klean.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Controllers;

[ApiController]
[Route("api/entreprises")]
public class EntreprisesController : ControllerBase
{
    private readonly KleanDbContext _db;

    public EntreprisesController(KleanDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<EntrepriseDto>>> Lister()
    {
        var entreprises = await _db.Entreprises
            .AsNoTracking()
            .OrderBy(e => e.RaisonSociale)
            .Select(e => new EntrepriseDto
            {
                Id = e.Id,
                RaisonSociale = e.RaisonSociale,
                Siret = e.Siret,
                DateCreation = e.DateCreation
            })
            .ToListAsync();

        return Ok(entreprises);
    }

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
                DateCreation = e.DateCreation
            })
            .FirstOrDefaultAsync();

        return entreprise is null ? NotFound() : Ok(entreprise);
    }

    [HttpPost]
    public async Task<ActionResult<EntrepriseDto>> Creer(EntrepriseCreateDto dto)
    {
        var entreprise = new Entreprise
        {
            RaisonSociale = dto.RaisonSociale,
            Siret = dto.Siret,
            DateCreation = DateTime.UtcNow
        };

        _db.Entreprises.Add(entreprise);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict($"Le SIRET « {dto.Siret} » est déjà enregistré.");
        }

        return CreatedAtAction(nameof(Obtenir), new { id = entreprise.Id }, Vers(entreprise));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Modifier(Guid id, EntrepriseCreateDto dto)
    {
        var entreprise = await _db.Entreprises.FindAsync(id);
        if (entreprise is null) return NotFound();

        entreprise.RaisonSociale = dto.RaisonSociale;
        entreprise.Siret = dto.Siret;

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

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Supprimer(Guid id)
    {
        var entreprise = await _db.Entreprises.FindAsync(id);
        if (entreprise is null) return NotFound();

        _db.Entreprises.Remove(entreprise);

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

    private static EntrepriseDto Vers(Entreprise e) => new()
    {
        Id = e.Id,
        RaisonSociale = e.RaisonSociale,
        Siret = e.Siret,
        DateCreation = e.DateCreation
    };
}
