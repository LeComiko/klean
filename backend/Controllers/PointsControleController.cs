using Klean.Api.DTOs;
using Klean.Api.Data;
using Klean.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Controllers;

/// <summary>
/// La checklist de chaque type de prestation (SPEC.md, étape 25, lot B). Donnée de
/// référence, comme le catalogue qu'elle prolonge : lue par les rôles internes, écrite par
/// l'administrateur seul.
///
/// Rien ne bloque la suppression d'un point : ce qui a déjà été coché garde son libellé, et
/// perd seulement le lien vers le point.
/// </summary>
[Authorize]
[ApiController]
[Route("api/points-controle")]
public class PointsControleController : ControllerBase
{
    private readonly KleanDbContext _db;

    public PointsControleController(KleanDbContext db) => _db = db;

    /// <summary>« La checklist de ce type » : le filtre s'applique avant le comptage (CLAUDE.md §6 bis).</summary>
    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet]
    public async Task<ActionResult<PageDto<PointControleDto>>> Lister(
        [FromQuery] PaginationDto pagination, [FromQuery] Guid? typePrestationId)
    {
        var requete = _db.PointsControle.AsNoTracking();

        if (typePrestationId is not null) requete = requete.Where(p => p.TypePrestationId == typePrestationId);

        var total = await requete.CountAsync();

        var points = await requete
            .OrderBy(p => p.TypePrestation!.Libelle).ThenBy(p => p.Ordre).ThenBy(p => p.Libelle).ThenBy(p => p.Id)
            .Skip(pagination.ANegliger)
            .Take(pagination.Taille)
            .ToListAsync();

        return Ok(new PageDto<PointControleDto>
        {
            Elements = points.Select(Vers).ToList(),
            Total = total,
            Page = pagination.Page,
            Taille = pagination.Taille
        });
    }

    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PointControleDto>> Obtenir(Guid id)
    {
        var point = await _db.PointsControle.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);

        return point is null ? NotFound() : Ok(Vers(point));
    }

    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpPost]
    public async Task<ActionResult<PointControleDto>> Creer(PointControleCreateDto dto)
    {
        if (await RefusAsync(dto) is { } refus) return BadRequest(refus);

        var point = new PointControle
        {
            TypePrestationId = dto.TypePrestationId,
            Libelle = dto.Libelle.Trim(),
            Ordre = dto.Ordre,
            Actif = dto.Actif
        };

        _db.PointsControle.Add(point);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Obtenir), new { id = point.Id }, Vers(point));
    }

    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Modifier(Guid id, PointControleCreateDto dto)
    {
        var point = await _db.PointsControle.FindAsync(id);
        if (point is null) return NotFound();

        if (await RefusAsync(dto) is { } refus) return BadRequest(refus);

        point.TypePrestationId = dto.TypePrestationId;
        point.Libelle = dto.Libelle.Trim();
        point.Ordre = dto.Ordre;
        point.Actif = dto.Actif;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Supprimer(Guid id)
    {
        var point = await _db.PointsControle.FindAsync(id);
        if (point is null) return NotFound();

        _db.PointsControle.Remove(point);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Un type inconnu — ou d'une autre entreprise, les filtres globaux s'appliquant — est
    /// refusé. Un libellé vide n'arrive pas jusqu'ici : <c>[Required]</c> le refuse avant,
    /// espaces compris, et l'écran de référence le rend inatteignable (CLAUDE.md §10).
    /// </summary>
    private async Task<string?> RefusAsync(PointControleCreateDto dto)
    {
        return await _db.TypesPrestation.AnyAsync(t => t.Id == dto.TypePrestationId)
            ? null
            : $"Type de prestation {dto.TypePrestationId} inconnu.";
    }

    private static PointControleDto Vers(PointControle p) => new()
    {
        Id = p.Id,
        EntrepriseId = p.EntrepriseId,
        TypePrestationId = p.TypePrestationId,
        Libelle = p.Libelle,
        Ordre = p.Ordre,
        Actif = p.Actif
    };
}
