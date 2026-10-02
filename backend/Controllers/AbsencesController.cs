using Klean.Api.DTOs;
using Klean.Api.Filtres;
using Klean.Api.Data;
using Klean.Api.Models;
using Klean.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/absences")]
public class AbsencesController : ControllerBase
{
    private readonly KleanDbContext _db;
    private readonly RemplacementService _cycle;

    public AbsencesController(KleanDbContext db, RemplacementService cycle)
    {
        _db = db;
        _cycle = cycle;
    }

    /// <summary>
    /// Liste paginée, filtrable par <c>agentId</c> — la descente de niveau,
    /// « les absences de cet agent ».
    ///
    /// ⚠️ Le filtre entre <b>avant</b> le comptage. Un total calculé sur la table
    /// entière annoncerait « 40 absences » au-dessus d'une liste qui en montre trois,
    /// et la pagination proposerait des pages vides.
    ///
    /// Un identifiant inconnu — ou appartenant à une autre entreprise, que les
    /// filtres globaux écartent — rend une page vide, pas un 404 : une liste filtrée
    /// sur rien reste une liste, et c'est une réponse, pas une erreur.
    /// </summary>
    [JournaliserAcces(RessourceAccedee.Absence, NatureAcces.Liste)]
    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet]
    public async Task<ActionResult<PageDto<AbsenceDto>>> Lister(
        [FromQuery] PaginationDto pagination,
        [FromQuery] Guid? agentId)
    {
        var requete = _db.Absences.AsNoTracking();

        if (agentId is not null)
        {
            requete = requete.Where(a => a.AgentId == agentId);
        }

        var total = await requete.CountAsync();

        var absences = await requete
            .OrderByDescending(a => a.DateDebut)
            .Skip(pagination.ANegliger)
            .Take(pagination.Taille)
            .Select(a => new AbsenceDto
            {
                Id = a.Id,
                EntrepriseId = a.EntrepriseId,
                AgentId = a.AgentId,
                Type = a.Type.ToString(),
                DateDebut = a.DateDebut,
                DateFin = a.DateFin,
                Motif = a.Motif
            })
            .ToListAsync();

        return Ok(new PageDto<AbsenceDto>
        {
            Elements = absences,
            Total = total,
            Page = pagination.Page,
            Taille = pagination.Taille
        });
    }

    [JournaliserAcces(RessourceAccedee.Absence, NatureAcces.Lecture)]
    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AbsenceDto>> Obtenir(Guid id)
    {
        var absence = await _db.Absences
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new AbsenceDto
            {
                Id = a.Id,
                EntrepriseId = a.EntrepriseId,
                AgentId = a.AgentId,
                Type = a.Type.ToString(),
                DateDebut = a.DateDebut,
                DateFin = a.DateFin,
                Motif = a.Motif
            })
            .FirstOrDefaultAsync();

        return absence is null ? NotFound() : Ok(absence);
    }

    [Authorize(Roles = RolesKlean.Exploitation)]
    [HttpPost]
    public async Task<ActionResult<AbsenceDto>> Creer(AbsenceCreateDto dto)
    {
        if (!Enum.TryParse<TypeAbsence>(dto.Type, out var type))
        {
            return BadRequest($"Type d'absence « {dto.Type} » invalide. Valeurs acceptées : {string.Join(", ", Enum.GetNames<TypeAbsence>())}.");
        }

        if (!await _db.Agents.AnyAsync(a => a.Id == dto.AgentId))
        {
            return BadRequest($"Agent {dto.AgentId} inconnu.");
        }

        // Un motif d'arrêt maladie en texte libre est une donnée de santé au sens
        // de l'article 9 du RGPD, et le planning n'en fait rien : il lui suffit
        // de savoir QU'un agent est absent. Décision consignée dans SPEC.md.
        if (type == TypeAbsence.Maladie && !string.IsNullOrWhiteSpace(dto.Motif))
        {
            return BadRequest(
                "Un arrêt maladie ne peut pas porter de motif : c'est une donnée de "
                + "santé, et le planning n'en a pas besoin. Renseigner le type et les "
                + "dates seulement. Le motif reste disponible sur les autres types "
                + "d'absence.");
        }

        if (dto.DateFin < dto.DateDebut)
        {
            return BadRequest("La date de fin précède la date de début.");
        }

        var absence = new Absence
        {
            AgentId = dto.AgentId,
            Type = type,
            DateDebut = Utc(dto.DateDebut),
            DateFin = Utc(dto.DateFin),
            Motif = dto.Motif
        };

        _db.Absences.Add(absence);
        await _db.SaveChangesAsync();

        // Détection d'impacts sur le planning, dans la foulée de la création :
        // c'est l'interconnexion qui fait le produit (CLAUDE.md §1). Saisir une
        // absence et devoir penser à cliquer ailleurs pour découvrir les
        // vacations découvertes, c'est déjà avoir oublié de le faire.
        await _cycle.DetecterImpactsAsync(absence.Id);

        return CreatedAtAction(nameof(Obtenir), new { id = absence.Id }, Vers(absence));
    }

    /// <summary>
    /// Rattrapage : rejoue la détection d'impacts d'une absence déjà saisie.
    /// Idempotent — un second appel crée zéro remplacement. Utile après une
    /// modification des dates de l'absence, ou sur les absences saisies avant
    /// que la détection automatique n'existe.
    /// </summary>
    [Authorize(Roles = RolesKlean.Exploitation)]
    [HttpPost("{id:guid}/detecter-impacts")]
    public async Task<ActionResult<DetectionDto>> DetecterImpacts(Guid id)
    {
        var crees = await _cycle.DetecterImpactsAsync(id);

        return crees is null
            ? NotFound()
            : Ok(new DetectionDto { RemplacementsCrees = crees.Value });
    }

    [Authorize(Roles = RolesKlean.Exploitation)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Modifier(Guid id, AbsenceCreateDto dto)
    {
        if (!Enum.TryParse<TypeAbsence>(dto.Type, out var type))
        {
            return BadRequest($"Type d'absence « {dto.Type} » invalide. Valeurs acceptées : {string.Join(", ", Enum.GetNames<TypeAbsence>())}.");
        }

        var absence = await _db.Absences.FindAsync(id);
        if (absence is null) return NotFound();

        if (absence.AgentId != dto.AgentId && !await _db.Agents.AnyAsync(a => a.Id == dto.AgentId))
        {
            return BadRequest($"Agent {dto.AgentId} inconnu.");
        }

        // Un motif d'arrêt maladie en texte libre est une donnée de santé au sens
        // de l'article 9 du RGPD, et le planning n'en fait rien : il lui suffit
        // de savoir QU'un agent est absent. Décision consignée dans SPEC.md.
        if (type == TypeAbsence.Maladie && !string.IsNullOrWhiteSpace(dto.Motif))
        {
            return BadRequest(
                "Un arrêt maladie ne peut pas porter de motif : c'est une donnée de "
                + "santé, et le planning n'en a pas besoin. Renseigner le type et les "
                + "dates seulement. Le motif reste disponible sur les autres types "
                + "d'absence.");
        }

        if (dto.DateFin < dto.DateDebut)
        {
            return BadRequest("La date de fin précède la date de début.");
        }

        absence.AgentId = dto.AgentId;
        absence.Type = type;
        absence.DateDebut = Utc(dto.DateDebut);
        absence.DateFin = Utc(dto.DateFin);
        absence.Motif = dto.Motif;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = RolesKlean.Exploitation)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Supprimer(Guid id)
    {
        var absence = await _db.Absences.FindAsync(id);
        if (absence is null) return NotFound();

        _db.Absences.Remove(absence);

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

    private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);

    private static AbsenceDto Vers(Absence a) => new()
    {
        Id = a.Id,
        EntrepriseId = a.EntrepriseId,
        AgentId = a.AgentId,
        Type = a.Type.ToString(),
        DateDebut = a.DateDebut,
        DateFin = a.DateFin,
        Motif = a.Motif
    };
}
