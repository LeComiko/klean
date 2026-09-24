using Klean.Api.DTOs;
using Klean.Api.Data;
using Klean.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Controllers;

[ApiController]
[Route("api/stocks")]
public class StocksController : ControllerBase
{
    private readonly KleanDbContext _db;

    public StocksController(KleanDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<StockDto>>> Lister()
    {
        var stocks = await _db.Stocks
            .AsNoTracking()
            .OrderBy(s => s.ProduitId)
            .Select(s => new StockDto
            {
                Id = s.Id,
                EntrepriseId = s.EntrepriseId,
                ProduitId = s.ProduitId,
                TypeEmplacement = s.TypeEmplacement.ToString(),
                EmplacementId = s.EmplacementId,
                Quantite = s.Quantite
            })
            .ToListAsync();

        return Ok(stocks);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StockDto>> Obtenir(Guid id)
    {
        var stock = await _db.Stocks
            .AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new StockDto
            {
                Id = s.Id,
                EntrepriseId = s.EntrepriseId,
                ProduitId = s.ProduitId,
                TypeEmplacement = s.TypeEmplacement.ToString(),
                EmplacementId = s.EmplacementId,
                Quantite = s.Quantite
            })
            .FirstOrDefaultAsync();

        return stock is null ? NotFound() : Ok(stock);
    }

    [HttpPost]
    public async Task<ActionResult<StockDto>> Creer(StockCreateDto dto)
    {
        if (!Enum.TryParse<TypeEmplacement>(dto.TypeEmplacement, out var type))
        {
            return BadRequest($"Type d'emplacement « {dto.TypeEmplacement} » invalide. Valeurs acceptées : {string.Join(", ", Enum.GetNames<TypeEmplacement>())}.");
        }

        if (!await _db.Produits.AnyAsync(p => p.Id == dto.ProduitId))
        {
            return BadRequest($"Produit {dto.ProduitId} inconnu.");
        }

        var stock = new Stock
        {
            EntrepriseId = dto.EntrepriseId,
            ProduitId = dto.ProduitId,
            TypeEmplacement = type,
            // EmplacementId est sans clé étrangère : selon le type, il pointe
            // vers un dépôt, un véhicule ou un site. Pas de contrôle d'existence
            // possible ici, c'est le prix de la décision consignée dans SPEC.md.
            EmplacementId = dto.EmplacementId,
            Quantite = dto.Quantite
        };

        _db.Stocks.Add(stock);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Obtenir), new { id = stock.Id }, Vers(stock));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Modifier(Guid id, StockCreateDto dto)
    {
        if (!Enum.TryParse<TypeEmplacement>(dto.TypeEmplacement, out var type))
        {
            return BadRequest($"Type d'emplacement « {dto.TypeEmplacement} » invalide. Valeurs acceptées : {string.Join(", ", Enum.GetNames<TypeEmplacement>())}.");
        }

        var stock = await _db.Stocks.FindAsync(id);
        if (stock is null) return NotFound();

        if (stock.ProduitId != dto.ProduitId && !await _db.Produits.AnyAsync(p => p.Id == dto.ProduitId))
        {
            return BadRequest($"Produit {dto.ProduitId} inconnu.");
        }

        stock.ProduitId = dto.ProduitId;
        stock.TypeEmplacement = type;
        stock.EmplacementId = dto.EmplacementId;
        stock.Quantite = dto.Quantite;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Supprimer(Guid id)
    {
        var stock = await _db.Stocks.FindAsync(id);
        if (stock is null) return NotFound();

        _db.Stocks.Remove(stock);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static StockDto Vers(Stock s) => new()
    {
        Id = s.Id,
        EntrepriseId = s.EntrepriseId,
        ProduitId = s.ProduitId,
        TypeEmplacement = s.TypeEmplacement.ToString(),
        EmplacementId = s.EmplacementId,
        Quantite = s.Quantite
    };
}
