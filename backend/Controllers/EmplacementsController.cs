using Klean.Api.DTOs;
using Klean.Api.Data;
using Klean.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Controllers;

/// <summary>
/// Les dépôts et véhicules de l'entreprise (SPEC.md, étape 22). Lus par les rôles
/// internes, écrits par <c>Exploitation</c>, comme les mouvements qui les visent.
/// </summary>
[Authorize]
[ApiController]
[Route("api/emplacements")]
public class EmplacementsController : ControllerBase
{
    private readonly KleanDbContext _db;

    public EmplacementsController(KleanDbContext db) => _db = db;

    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet]
    public async Task<ActionResult<PageDto<EmplacementDto>>> Lister([FromQuery] PaginationDto pagination)
    {
        var requete = _db.Emplacements.AsNoTracking();

        var total = await requete.CountAsync();

        var emplacements = await requete
            .OrderBy(e => e.Nom).ThenBy(e => e.Id)
            .Skip(pagination.ANegliger)
            .Take(pagination.Taille)
            .ToListAsync();

        return Ok(new PageDto<EmplacementDto>
        {
            Elements = emplacements.Select(Vers).ToList(),
            Total = total,
            Page = pagination.Page,
            Taille = pagination.Taille
        });
    }

    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EmplacementDto>> Obtenir(Guid id)
    {
        var emplacement = await _db.Emplacements.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);

        return emplacement is null ? NotFound() : Ok(Vers(emplacement));
    }

    [Authorize(Roles = RolesKlean.Exploitation)]
    [HttpPost]
    public async Task<ActionResult<EmplacementDto>> Creer(EmplacementCreateDto dto)
    {
        if (RefusDuType(dto.Type, out var type) is { } refus) return BadRequest(refus);

        var emplacement = new Emplacement { Nom = dto.Nom.Trim(), Type = type, Actif = dto.Actif };

        _db.Emplacements.Add(emplacement);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Obtenir), new { id = emplacement.Id }, Vers(emplacement));
    }

    /// <summary>Désactiver passe par ici, <c>Actif = false</c>.</summary>
    [Authorize(Roles = RolesKlean.Exploitation)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Modifier(Guid id, EmplacementCreateDto dto)
    {
        var emplacement = await _db.Emplacements.FindAsync(id);
        if (emplacement is null) return NotFound();

        if (RefusDuType(dto.Type, out var type) is { } refus) return BadRequest(refus);

        emplacement.Nom = dto.Nom.Trim();
        emplacement.Type = type;
        emplacement.Actif = dto.Actif;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = RolesKlean.Exploitation)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Supprimer(Guid id)
    {
        var emplacement = await _db.Emplacements.FindAsync(id);
        if (emplacement is null) return NotFound();

        _db.Emplacements.Remove(emplacement);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Restrict depuis le stock et les mouvements. Le refus nomme la sortie : une
            // friction qui ne dit pas par où passer est un cul-de-sac (CLAUDE.md §10).
            var mouvements = await _db.MouvementsStock.CountAsync(m =>
                m.OrigineEmplacementId == id || m.DestinationEmplacementId == id);

            return Conflict(
                $"Cet emplacement est cité par {mouvements} mouvement(s) de stock et ne peut pas "
                + "être supprimé : l'historique des quantités en dépend. Le désactiver : il ne "
                + "recevra plus rien, et ce qui s'y trouve pourra encore en sortir.");
        }

        return NoContent();
    }

    /// <summary>
    /// Les noms exacts : <c>Enum.TryParse</c> accepterait « 1 ». Le site n'y figure pas — il
    /// n'est pas un emplacement, il existe déjà.
    /// </summary>
    private static string? RefusDuType(string valeur, out TypeEmplacement type)
    {
        if (Enum.GetNames<TypeEmplacement>().Contains(valeur))
        {
            type = Enum.Parse<TypeEmplacement>(valeur);
            return null;
        }

        type = default;
        return $"Type d'emplacement « {valeur} » invalide. Valeurs acceptées : "
            + $"{string.Join(", ", Enum.GetNames<TypeEmplacement>())}. Un site n'est pas un "
            + "emplacement : un stock sur site vise le site lui-même.";
    }

    private static EmplacementDto Vers(Emplacement e) => new()
    {
        Id = e.Id,
        EntrepriseId = e.EntrepriseId,
        Nom = e.Nom,
        Type = e.Type.ToString(),
        Actif = e.Actif
    };
}
