using Klean.Api.DTOs;
using Klean.Api.Data;
using Klean.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Klean.Api.Controllers;

/// <summary>
/// Ce qu'un passage de chaque type de prestation consomme (SPEC.md, étape 22, lot B). Lue
/// par les rôles internes, écrite par <c>Exploitation</c>, comme les mouvements.
///
/// Rien n'en dépend : une ligne se supprime sans condition, et la prévision suivante
/// compte simplement sans elle.
/// </summary>
[Authorize]
[ApiController]
[Route("api/consommations-type")]
public class ConsommationsTypeController : ControllerBase
{
    /// <summary>L'index unique <c>(TypePrestationId, ProduitId)</c>, nommé par EF.</summary>
    private const string IndexTypeProduit = "IX_ConsommationsType_TypePrestationId_ProduitId";

    private readonly KleanDbContext _db;

    public ConsommationsTypeController(KleanDbContext db) => _db = db;

    /// <summary>
    /// Deux filtres facultatifs, appliqués avant le comptage (CLAUDE.md §6 bis) : « ce que
    /// consomme ce type », « qui consomme ce produit ».
    /// </summary>
    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet]
    public async Task<ActionResult<PageDto<ConsommationTypeDto>>> Lister(
        [FromQuery] PaginationDto pagination,
        [FromQuery] Guid? typePrestationId,
        [FromQuery] Guid? produitId)
    {
        var requete = _db.ConsommationsType.AsNoTracking();

        if (typePrestationId is not null) requete = requete.Where(c => c.TypePrestationId == typePrestationId);
        if (produitId is not null) requete = requete.Where(c => c.ProduitId == produitId);

        var total = await requete.CountAsync();

        var consommations = await requete
            .OrderBy(c => c.TypePrestation!.Libelle).ThenBy(c => c.Produit!.Nom).ThenBy(c => c.Id)
            .Skip(pagination.ANegliger)
            .Take(pagination.Taille)
            .ToListAsync();

        return Ok(new PageDto<ConsommationTypeDto>
        {
            Elements = consommations.Select(Vers).ToList(),
            Total = total,
            Page = pagination.Page,
            Taille = pagination.Taille
        });
    }

    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ConsommationTypeDto>> Obtenir(Guid id)
    {
        var consommation = await _db.ConsommationsType.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);

        return consommation is null ? NotFound() : Ok(Vers(consommation));
    }

    [Authorize(Roles = RolesKlean.Exploitation)]
    [HttpPost]
    public async Task<ActionResult<ConsommationTypeDto>> Creer(ConsommationTypeCreateDto dto)
    {
        if (await RefusAsync(dto, produitActuel: null) is { } refus) return BadRequest(refus);
        if (await DoublonAsync(dto, exclu: null) is { } doublon) return Conflict(doublon);

        var consommation = new ConsommationType
        {
            TypePrestationId = dto.TypePrestationId,
            ProduitId = dto.ProduitId,
            QuantiteParPassage = dto.QuantiteParPassage
        };

        _db.ConsommationsType.Add(consommation);

        if (await EnregistrerAsync() is { } course) return Conflict(course);

        return CreatedAtAction(nameof(Obtenir), new { id = consommation.Id }, Vers(consommation));
    }

    [Authorize(Roles = RolesKlean.Exploitation)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Modifier(Guid id, ConsommationTypeCreateDto dto)
    {
        var consommation = await _db.ConsommationsType.FindAsync(id);
        if (consommation is null) return NotFound();

        if (await RefusAsync(dto, produitActuel: consommation.ProduitId) is { } refus) return BadRequest(refus);
        if (await DoublonAsync(dto, exclu: id) is { } doublon) return Conflict(doublon);

        consommation.TypePrestationId = dto.TypePrestationId;
        consommation.ProduitId = dto.ProduitId;
        consommation.QuantiteParPassage = dto.QuantiteParPassage;

        if (await EnregistrerAsync() is { } course) return Conflict(course);

        return NoContent();
    }

    [Authorize(Roles = RolesKlean.Exploitation)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Supprimer(Guid id)
    {
        var consommation = await _db.ConsommationsType.FindAsync(id);
        if (consommation is null) return NotFound();

        _db.ConsommationsType.Remove(consommation);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Un type ou un produit inconnu — ou d'une autre entreprise, les filtres globaux
    /// s'appliquant — est refusé. On n'attache pas un produit désactivé, puisqu'il ne se
    /// réapprovisionne plus ; la ligne qui le portait déjà se corrige encore.
    /// </summary>
    private async Task<string?> RefusAsync(ConsommationTypeCreateDto dto, Guid? produitActuel)
    {
        if (!await _db.TypesPrestation.AnyAsync(t => t.Id == dto.TypePrestationId))
        {
            return $"Type de prestation {dto.TypePrestationId} inconnu.";
        }

        var produit = await _db.Produits.AsNoTracking()
            .Where(p => p.Id == dto.ProduitId)
            .Select(p => new { p.Nom, p.Actif })
            .FirstOrDefaultAsync();

        if (produit is null) return $"Produit {dto.ProduitId} inconnu.";

        if (!produit.Actif && dto.ProduitId != produitActuel)
        {
            return $"Le produit « {produit.Nom} » est désactivé : le réactiver avant de lui attacher "
                + "une consommation.";
        }

        return null;
    }

    /// <summary>
    /// Deux lignes pour le même type et le même produit donneraient deux réponses à
    /// « combien ce passage consomme-t-il », et la prévision les additionnerait.
    /// </summary>
    private async Task<string?> DoublonAsync(ConsommationTypeCreateDto dto, Guid? exclu)
    {
        var existe = await _db.ConsommationsType.AnyAsync(c =>
            c.TypePrestationId == dto.TypePrestationId && c.ProduitId == dto.ProduitId && c.Id != exclu);

        return existe ? "Ce type consomme déjà ce produit : modifier la ligne existante." : null;
    }

    /// <summary>
    /// Enregistre, et rend le refus si l'index unique a tranché à la place de
    /// <see cref="DoublonAsync"/> : deux saisies du même couple lues avant l'écriture de
    /// l'autre. Sans cela, la seconde répondrait 500.
    /// </summary>
    private async Task<string?> EnregistrerAsync()
    {
        try
        {
            await _db.SaveChangesAsync();
            return null;
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: IndexTypeProduit
        })
        {
            return "Cette consommation vient d'être enregistrée par ailleurs. Recharger la liste.";
        }
    }

    private static ConsommationTypeDto Vers(ConsommationType c) => new()
    {
        Id = c.Id,
        EntrepriseId = c.EntrepriseId,
        TypePrestationId = c.TypePrestationId,
        ProduitId = c.ProduitId,
        QuantiteParPassage = c.QuantiteParPassage
    };
}
