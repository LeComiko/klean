using Klean.Api.DTOs;
using Klean.Api.Filtres;
using Klean.Api.Data;
using Klean.Api.Models;
using Klean.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Controllers;

/// <summary>
/// Remplacements : le CRUD brut, et le cycle métier gardé — candidats,
/// proposer, accepter, refuser. C'est le cycle qui doit être utilisé en
/// exploitation ; le PUT reste un outil d'administration et refuse tout
/// changement de statut.
/// </summary>
[Authorize]
[ApiController]
[Route("api/remplacements")]
public class RemplacementsController : ControllerBase
{
    private readonly KleanDbContext _db;
    private readonly RemplacementService _cycle;

    public RemplacementsController(KleanDbContext db, RemplacementService cycle)
    {
        _db = db;
        _cycle = cycle;
    }

    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet]
    public async Task<ActionResult<PageDto<RemplacementDto>>> Lister([FromQuery] PaginationDto pagination)
    {
        var requete = _db.Remplacements
            .AsNoTracking()
            .OrderByDescending(r => r.DateCreation);

        var total = await requete.CountAsync();

        var remplacements = await requete
            .Skip(pagination.ANegliger)
            .Take(pagination.Taille)
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

        return Ok(new PageDto<RemplacementDto>
        {
            Elements = remplacements,
            Total = total,
            Page = pagination.Page,
            Taille = pagination.Taille
        });
    }

    [Authorize(Roles = RolesKlean.Interne)]
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

    [Authorize(Roles = RolesKlean.Exploitation)]
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

    [Authorize(Roles = RolesKlean.Exploitation)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Modifier(Guid id, RemplacementCreateDto dto)
    {
        if (!Enum.TryParse<StatutRemplacement>(dto.Statut, out var statut))
        {
            return BadRequest($"Statut de remplacement « {dto.Statut} » invalide. Valeurs acceptées : {string.Join(", ", Enum.GetNames<StatutRemplacement>())}.");
        }

        var remplacement = await _db.Remplacements.FindAsync(id);
        if (remplacement is null) return NotFound();

        // Le CRUD brut ne franchit pas la machine à états : il laissait forcer
        // Accepte, donc changer l'agent d'une intervention sans passer par la
        // seule méthode autorisée à le faire (CLAUDE.md §9). Les transitions
        // passent par les endpoints gardés de l'étape 12.
        if (statut != remplacement.Statut)
        {
            return BadRequest(
                $"Le statut d'un remplacement ne se modifie pas ici. Il vaut "
                + $"« {remplacement.Statut} » et doit être renvoyé tel quel. Utiliser "
                + "les actions proposer, accepter ou refuser.");
        }

        var erreur = await ValiderLiens(dto);
        if (erreur is not null) return BadRequest(erreur);

        remplacement.InterventionId = dto.InterventionId;
        remplacement.AbsenceId = dto.AbsenceId;
        remplacement.AgentRemplacantId = dto.AgentRemplacantId;
        remplacement.Score = dto.Score;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = RolesKlean.Exploitation)]
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

    // ------------------------------------------------------- cycle métier gardé

    /// <summary>
    /// Candidats au remplacement, classés par score décroissant. Réservé aux
    /// rôles internes, comme la liste de candidats d'une intervention : elle
    /// nomme des agents.
    /// </summary>
    [JournaliserAcces(RessourceAccedee.Candidats, NatureAcces.Liste)]
    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet("{id:guid}/candidats")]
    public async Task<ActionResult<IEnumerable<CandidatDto>>> Candidats(Guid id)
    {
        var candidats = await _cycle.GetCandidatsAsync(id);

        return candidats is null ? NotFound() : Ok(candidats);
    }

    [Authorize(Roles = RolesKlean.Exploitation)]
    [HttpPost("{id:guid}/proposer")]
    public async Task<ActionResult<RemplacementDto>> Proposer(Guid id, ProposerDto dto)
        => Traduire(await _cycle.ProposerAsync(id, dto.AgentId));

    /// <summary>
    /// Accepte la proposition : c'est ici, et nulle part ailleurs dans le cycle,
    /// que l'intervention change d'agent (CLAUDE.md §9).
    /// </summary>
    [Authorize(Roles = RolesKlean.Exploitation)]
    [HttpPost("{id:guid}/accepter")]
    public async Task<ActionResult<RemplacementDto>> Accepter(Guid id)
        => Traduire(await _cycle.AccepterAsync(id));

    [Authorize(Roles = RolesKlean.Exploitation)]
    [HttpPost("{id:guid}/refuser")]
    public async Task<ActionResult<RemplacementDto>> Refuser(Guid id)
        => Traduire(await _cycle.RefuserAsync(id));

    /// <summary>
    /// Une ressource inexistante donne 404, une transition impossible 400. La
    /// distinction est exigée par la spécification : « ce remplacement n'existe
    /// pas » et « ce remplacement n'est pas dans le bon état » n'appellent pas la
    /// même action côté appelant.
    /// </summary>
    private ActionResult<RemplacementDto> Traduire(ResultatRemplacement resultat) => resultat.Issue switch
    {
        IssueRemplacement.Ok => Ok(resultat.Valeur),
        IssueRemplacement.Introuvable => NotFound(),
        _ => BadRequest(resultat.Message)
    };

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
