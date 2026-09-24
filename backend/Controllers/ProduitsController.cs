using Klean.Api.DTOs;
using Klean.Api.Data;
using Klean.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Controllers;

[ApiController]
[Route("api/produits")]
public class ProduitsController : ControllerBase
{
    private readonly KleanDbContext _db;

    public ProduitsController(KleanDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProduitDto>>> Lister()
    {
        var produits = await _db.Produits
            .AsNoTracking()
            .OrderBy(p => p.Nom)
            .Select(p => new ProduitDto
            {
                Id = p.Id,
                EntrepriseId = p.EntrepriseId,
                Nom = p.Nom,
                Reference = p.Reference,
                Unite = p.Unite,
                SeuilAlerte = p.SeuilAlerte
            })
            .ToListAsync();

        return Ok(produits);
    }

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
                SeuilAlerte = p.SeuilAlerte
            })
            .FirstOrDefaultAsync();

        return produit is null ? NotFound() : Ok(produit);
    }

    [HttpPost]
    public async Task<ActionResult<ProduitDto>> Creer(ProduitCreateDto dto)
    {
        var produit = new Produit
        {
            EntrepriseId = dto.EntrepriseId,
            Nom = dto.Nom,
            Reference = dto.Reference,
            Unite = dto.Unite,
            SeuilAlerte = dto.SeuilAlerte
        };

        _db.Produits.Add(produit);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Obtenir), new { id = produit.Id }, Vers(produit));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Modifier(Guid id, ProduitCreateDto dto)
    {
        var produit = await _db.Produits.FindAsync(id);
        if (produit is null) return NotFound();

        produit.Nom = dto.Nom;
        produit.Reference = dto.Reference;
        produit.Unite = dto.Unite;
        produit.SeuilAlerte = dto.SeuilAlerte;

        await _db.SaveChangesAsync();
        return NoContent();
    }

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
            return Conflict("Suppression refusée par la base : des données rattachées l'empêchent.");
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
        SeuilAlerte = p.SeuilAlerte
    };
}
