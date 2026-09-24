using Klean.Api.DTOs;
using Klean.Api.Data;
using Klean.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Controllers;

/// <summary>
/// CRUD brut sur les remplacements. Le cycle métier gardé — candidats,
/// proposer, accepter, refuser — est ajouté à l'étape 8 ; c'est lui qui doit
/// être utilisé en exploitation, ce PUT reste un outil d'administration.
/// </summary>
[ApiController]
[Route("api/remplacements")]
public class RemplacementsController : ControllerBase
{
    private readonly KleanDbContext _db;

    public RemplacementsController(KleanDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RemplacementDto>>> Lister()
    {
        var remplacements = await _db.Remplacements
            .AsNoTracking()
            .OrderByDescending(r => r.DateCreation)
            .Select(r => new RemplacementDto
            {
                Id = r.Id,
                EntrepriseId = r.EntrepriseId,
                InterventionId = r.InterventionId,
                AbsenceId = r.AbsenceId,
                AgentRemplacantId = r.AgentRemplacantId,
                Statut = r.Statut.ToString(),
                Score = r.Score,
                DateCreation = r.DateCreation
            })
            .ToListAsync();

        return Ok(remplacements);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RemplacementDto>> Obtenir(Guid id)
    {
        var remplacement = await _db.Remplacements
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new RemplacementDto
            {
                Id = r.Id,
                EntrepriseId = r.EntrepriseId,
                InterventionId = r.InterventionId,
                AbsenceId = r.AbsenceId,
                AgentRemplacantId = r.AgentRemplacantId,
                Statut = r.Statut.ToString(),
                Score = r.Score,
                DateCreation = r.DateCreation
            })
            .FirstOrDefaultAsync();

        return remplacement is null ? NotFound() : Ok(remplacement);
    }

    [HttpPost]
    public async Task<ActionResult<RemplacementDto>> Creer(RemplacementCreateDto dto)
    {
        if (!Enum.TryParse<StatutRemplacement>(dto.Statut, out var statut))
        {
            return BadRequest($"Statut de remplacement « {dto.Statut} » invalide. Valeurs acceptées : {string.Join(", ", Enum.GetNames<StatutRemplacement>())}.");
        }

        var erreur = await ValiderLiens(dto);
        if (erreur is not null) return BadRequest(erreur);

        var remplacement = new Remplacement
        {
            EntrepriseId = dto.EntrepriseId,
            InterventionId = dto.InterventionId,
            AbsenceId = dto.AbsenceId,
            AgentRemplacantId = dto.AgentRemplacantId,
            Statut = statut,
            Score = dto.Score,
            DateCreation = DateTime.UtcNow
        };

        _db.Remplacements.Add(remplacement);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Obtenir), new { id = remplacement.Id }, Vers(remplacement));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Modifier(Guid id, RemplacementCreateDto dto)
    {
        if (!Enum.TryParse<StatutRemplacement>(dto.Statut, out var statut))
        {
            return BadRequest($"Statut de remplacement « {dto.Statut} » invalide. Valeurs acceptées : {string.Join(", ", Enum.GetNames<StatutRemplacement>())}.");
        }

        var remplacement = await _db.Remplacements.FindAsync(id);
        if (remplacement is null) return NotFound();

        var erreur = await ValiderLiens(dto);
        if (erreur is not null) return BadRequest(erreur);

        remplacement.InterventionId = dto.InterventionId;
        remplacement.AbsenceId = dto.AbsenceId;
        remplacement.AgentRemplacantId = dto.AgentRemplacantId;
        remplacement.Statut = statut;
        remplacement.Score = dto.Score;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Supprimer(Guid id)
    {
        var remplacement = await _db.Remplacements.FindAsync(id);
        if (remplacement is null) return NotFound();

        _db.Remplacements.Remove(remplacement);

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

    private async Task<string?> ValiderLiens(RemplacementCreateDto dto)
    {
        if (!await _db.Interventions.AnyAsync(i => i.Id == dto.InterventionId))
        {
            return $"Intervention {dto.InterventionId} inconnue.";
        }

        if (dto.AbsenceId.HasValue && !await _db.Absences.AnyAsync(a => a.Id == dto.AbsenceId.Value))
        {
            return $"Absence {dto.AbsenceId.Value} inconnue.";
        }

        if (dto.AgentRemplacantId.HasValue
            && !await _db.Agents.AnyAsync(a => a.Id == dto.AgentRemplacantId.Value))
        {
            return $"Agent {dto.AgentRemplacantId.Value} inconnu.";
        }

        return null;
    }

    private static RemplacementDto Vers(Remplacement r) => new()
    {
        Id = r.Id,
        EntrepriseId = r.EntrepriseId,
        InterventionId = r.InterventionId,
        AbsenceId = r.AbsenceId,
        AgentRemplacantId = r.AgentRemplacantId,
        Statut = r.Statut.ToString(),
        Score = r.Score,
        DateCreation = r.DateCreation
    };
}
