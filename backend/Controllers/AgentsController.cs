using Klean.Api.DTOs;
using Klean.Api.Data;
using Klean.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/agents")]
public class AgentsController : ControllerBase
{
    private readonly KleanDbContext _db;

    public AgentsController(KleanDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AgentDto>>> Lister()
    {
        var agents = await _db.Agents
            .AsNoTracking()
            .OrderBy(a => a.Nom).ThenBy(a => a.Prenom)
            .Select(a => new AgentDto
            {
                Id = a.Id,
                EntrepriseId = a.EntrepriseId,
                Nom = a.Nom,
                Prenom = a.Prenom,
                Email = a.Email,
                Telephone = a.Telephone,
                Adresse = a.Adresse,
                Latitude = a.Latitude,
                Longitude = a.Longitude,
                DateEmbauche = a.DateEmbauche,
                Actif = a.Actif
            })
            .ToListAsync();

        return Ok(agents);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AgentDto>> Obtenir(Guid id)
    {
        var agent = await _db.Agents
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new AgentDto
            {
                Id = a.Id,
                EntrepriseId = a.EntrepriseId,
                Nom = a.Nom,
                Prenom = a.Prenom,
                Email = a.Email,
                Telephone = a.Telephone,
                Adresse = a.Adresse,
                Latitude = a.Latitude,
                Longitude = a.Longitude,
                DateEmbauche = a.DateEmbauche,
                Actif = a.Actif
            })
            .FirstOrDefaultAsync();

        return agent is null ? NotFound() : Ok(agent);
    }

    [HttpPost]
    public async Task<ActionResult<AgentDto>> Creer(AgentCreateDto dto)
    {
        var agent = new Agent
        {
            Nom = dto.Nom,
            Prenom = dto.Prenom,
            Email = dto.Email,
            Telephone = dto.Telephone,
            Adresse = dto.Adresse,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            // Npgsql refuse un DateTime dont le Kind est Unspecified, ce que
            // produit la liaison JSON d'une date sans fuseau (CLAUDE.md §10).
            DateEmbauche = DateTime.SpecifyKind(dto.DateEmbauche, DateTimeKind.Utc),
            Actif = dto.Actif
        };

        _db.Agents.Add(agent);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Obtenir), new { id = agent.Id }, Vers(agent));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Modifier(Guid id, AgentCreateDto dto)
    {
        var agent = await _db.Agents.FindAsync(id);
        if (agent is null) return NotFound();

        agent.Nom = dto.Nom;
        agent.Prenom = dto.Prenom;
        agent.Email = dto.Email;
        agent.Telephone = dto.Telephone;
        agent.Adresse = dto.Adresse;
        agent.Latitude = dto.Latitude;
        agent.Longitude = dto.Longitude;
        agent.DateEmbauche = DateTime.SpecifyKind(dto.DateEmbauche, DateTimeKind.Utc);
        agent.Actif = dto.Actif;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Supprimer(Guid id)
    {
        var agent = await _db.Agents.FindAsync(id);
        if (agent is null) return NotFound();

        _db.Agents.Remove(agent);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Restrict volontaire sur Intervention.AgentId et
            // Remplacement.AgentRemplacantId : l'historique du planning ne doit
            // pas disparaître avec l'agent (CLAUDE.md §7, migration InitialCreate).
            var interventions = await _db.Interventions.CountAsync(i => i.AgentId == id);
            return Conflict(
                $"Cet agent porte {interventions} intervention(s) et ne peut pas être supprimé. " +
                "Le passer à Actif = false pour l'écarter du planning.");
        }

        return NoContent();
    }

    private static AgentDto Vers(Agent a) => new()
    {
        Id = a.Id,
        EntrepriseId = a.EntrepriseId,
        Nom = a.Nom,
        Prenom = a.Prenom,
        Email = a.Email,
        Telephone = a.Telephone,
        Adresse = a.Adresse,
        Latitude = a.Latitude,
        Longitude = a.Longitude,
        DateEmbauche = a.DateEmbauche,
        Actif = a.Actif
    };
}
