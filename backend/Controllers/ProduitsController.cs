using Klean.Api.DTOs;
using Klean.Api.Data;
using Klean.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/produits")]
public class ProduitsController : ControllerBase
{
    private readonly KleanDbContext _db;

    public ProduitsController(KleanDbContext db) => _db = db;

    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet]
    public async Task<ActionResult<PageDto<ProduitDto>>> Lister([FromQuery] PaginationDto pagination)
    {
        var requete = _db.Produits
            .AsNoTracking()
            .OrderBy(p => p.Nom);

        var total = await requete.CountAsync();

        var produits = await requete
            .Skip(pagination.ANegliger)
            .Take(pagination.Taille)
            .Select(p => new ProduitDto
            {
                Id = p.Id,
                EntrepriseId = p.EntrepriseId,
                Nom = p.Nom,
                Reference = p.Reference,
                Unite = p.Unite,
                SeuilAlerte = p.SeuilAlerte,
                Actif = p.Actif
            })
            .ToListAsync();

        return Ok(new PageDto<ProduitDto>
        {
            Elements = produits,
            Total = total,
            Page = pagination.Page,
            Taille = pagination.Taille
        });
    }

    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProduitDto>> Obtenir(Guid id)
    {
        var produit = await _db.Produits
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new ProduitDto
            {
                Id = p.Id,
                EntrepriseId = p.EntrepriseId,
                Nom = p.Nom,
                Reference = p.Reference,
                Unite = p.Unite,
                SeuilAlerte = p.SeuilAlerte,
                Actif = p.Actif
            })
            .FirstOrDefaultAsync();

        return produit is null ? NotFound() : Ok(produit);
    }

    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpPost]
    public async Task<ActionResult<ProduitDto>> Creer(ProduitCreateDto dto)
    {
        var produit = new Produit
        {
            Nom = dto.Nom,
            Reference = dto.Reference,
            Unite = dto.Unite,
            SeuilAlerte = dto.SeuilAlerte,
            Actif = dto.Actif
        };

        _db.Produits.Add(produit);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Obtenir), new { id = produit.Id }, Vers(produit));
    }

    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Modifier(Guid id, ProduitCreateDto dto)
    {
        var produit = await _db.Produits.FindAsync(id);
        if (produit is null) return NotFound();

        produit.Nom = dto.Nom;
        produit.Reference = dto.Reference;
        produit.Unite = dto.Unite;
        produit.SeuilAlerte = dto.SeuilAlerte;
        produit.Actif = dto.Actif;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Supprimer(Guid id)
    {
        var produit = await _db.Produits.FindAsync(id);
        if (produit is null) return NotFound();

        _db.Produits.Remove(produit);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Restrict depuis le stock et ses mouvements. ⚠️ Avant l'étape 22, la sortie
            // nommée était « supprimer ses lignes de stock » ; elles ne se suppriment plus,
            // et un refus qui nomme une sortie fermée est un cul-de-sac (CLAUDE.md §10).
            var mouvements = await _db.MouvementsStock.CountAsync(m => m.ProduitId == id);

            return Conflict(
                $"Ce produit est cité par {mouvements} mouvement(s) de stock et ne peut pas "
                + "être supprimé : l'historique des quantités en dépend. Le désactiver : il "
                + "ne pourra plus entrer en stock, et ce qui en reste pourra encore sortir.");
        }

        return NoContent();
    }

    private static ProduitDto Vers(Produit p) => new()
    {
        Id = p.Id,
        EntrepriseId = p.EntrepriseId,
        Nom = p.Nom,
        Reference = p.Reference,
        Unite = p.Unite,
        SeuilAlerte = p.SeuilAlerte,
        Actif = p.Actif
    };
}
