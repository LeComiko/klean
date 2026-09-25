using Klean.Api.DTOs;
using Klean.Api.Data;
using Klean.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/interventions")]
public class InterventionsController : ControllerBase
{
    private readonly KleanDbContext _db;

    public InterventionsController(KleanDbContext db) => _db = db;

    [Authorize(Roles = RolesKlean.InterneEtAgent)]
    [HttpGet]
    public async Task<ActionResult<PageDto<InterventionDto>>> Lister([FromQuery] PaginationDto pagination)
    {
        var requete = _db.Interventions
            .AsNoTracking()
            .OrderBy(i => i.DatePrevue).ThenBy(i => i.HeurePrevue);

        var total = await requete.CountAsync();

        var interventions = await requete
            .Skip(pagination.ANegliger)
            .Take(pagination.Taille)
            .Select(i => new InterventionDto
            {
                Id = i.Id,
                EntrepriseId = i.EntrepriseId,
                PrestationId = i.PrestationId,
                AgentId = i.AgentId,
                DatePrevue = i.DatePrevue,
                HeurePrevue = i.HeurePrevue,
                Statut = i.Statut.ToString()
            })
            .ToListAsync();

        return Ok(new PageDto<InterventionDto>
        {
            Elements = interventions,
            Total = total,
            Page = pagination.Page,
            Taille = pagination.Taille
        });
    }

    [Authorize(Roles = RolesKlean.InterneEtAgent)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InterventionDto>> Obtenir(Guid id)
    {
        var intervention = await _db.Interventions
            .AsNoTracking()
            .Where(i => i.Id == id)
            .Select(i => new InterventionDto
            {
                Id = i.Id,
                EntrepriseId = i.EntrepriseId,
                PrestationId = i.PrestationId,
                AgentId = i.AgentId,
                DatePrevue = i.DatePrevue,
                HeurePrevue = i.HeurePrevue,
                Statut = i.Statut.ToString()
            })
            .FirstOrDefaultAsync();

        return intervention is null ? NotFound() : Ok(intervention);
    }

    [Authorize(Roles = RolesKlean.Exploitation)]
    [HttpPost]
    public async Task<ActionResult<InterventionDto>> Creer(InterventionCreateDto dto)
    {
        if (!Enum.TryParse<StatutIntervention>(dto.Statut, out var statut))
        {
            return BadRequest($"Statut d'intervention « {dto.Statut} » invalide. Valeurs acceptées : {string.Join(", ", Enum.GetNames<StatutIntervention>())}.");
        }

        if (!await _db.Prestations.AnyAsync(p => p.Id == dto.PrestationId))
        {
            return BadRequest($"Prestation {dto.PrestationId} inconnue.");
        }

        if (dto.AgentId.HasValue && !await _db.Agents.AnyAsync(a => a.Id == dto.AgentId.Value))
        {
            return BadRequest($"Agent {dto.AgentId.Value} inconnu.");
        }

        var intervention = new Intervention
        {
            PrestationId = dto.PrestationId,
            AgentId = dto.AgentId,
            DatePrevue = DateTime.SpecifyKind(dto.DatePrevue, DateTimeKind.Utc),
            HeurePrevue = dto.HeurePrevue,
            Statut = statut
        };

        _db.Interventions.Add(intervention);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Obtenir), new { id = intervention.Id }, Vers(intervention));
    }

    [Authorize(Roles = RolesKlean.Interne)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Modifier(Guid id, InterventionCreateDto dto)
    {
        if (!Enum.TryParse<StatutIntervention>(dto.Statut, out var statut))
        {
            return BadRequest($"Statut d'intervention « {dto.Statut} » invalide. Valeurs acceptées : {string.Join(", ", Enum.GetNames<StatutIntervention>())}.");
        }

        var intervention = await _db.Interventions.FindAsync(id);
        if (intervention is null) return NotFound();

        if (intervention.PrestationId != dto.PrestationId
            && !await _db.Prestations.AnyAsync(p => p.Id == dto.PrestationId))
        {
            return BadRequest($"Prestation {dto.PrestationId} inconnue.");
        }

        if (dto.AgentId.HasValue && dto.AgentId != intervention.AgentId
            && !await _db.Agents.AnyAsync(a => a.Id == dto.AgentId.Value))
        {
            return BadRequest($"Agent {dto.AgentId.Value} inconnu.");
        }

        intervention.PrestationId = dto.PrestationId;
        intervention.AgentId = dto.AgentId;
        intervention.DatePrevue = DateTime.SpecifyKind(dto.DatePrevue, DateTimeKind.Utc);
        intervention.HeurePrevue = dto.HeurePrevue;
        intervention.Statut = statut;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = RolesKlean.Exploitation)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Supprimer(Guid id)
    {
        var intervention = await _db.Interventions.FindAsync(id);
        if (intervention is null) return NotFound();

        _db.Interventions.Remove(intervention);

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

    private static InterventionDto Vers(Intervention i) => new()
    {
        Id = i.Id,
        EntrepriseId = i.EntrepriseId,
        PrestationId = i.PrestationId,
        AgentId = i.AgentId,
        DatePrevue = i.DatePrevue,
        HeurePrevue = i.HeurePrevue,
        Statut = i.Statut.ToString()
    };
}
