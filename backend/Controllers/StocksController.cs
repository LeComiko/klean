using Klean.Api.DTOs;
using Klean.Api.Data;
using Klean.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Controllers;

/// <summary>
/// Les quantités en stock, en lecture seule depuis l'étape 22.
///
/// ⚠️ Ni création, ni modification, ni suppression : une quantité résulte de mouvements
/// (<c>POST /api/mouvements-stock</c>), et une ligne écrite à la main n'aurait pas
/// d'histoire. Une ligne à zéro reste : elle dit qu'un lieu a porté ce produit.
/// </summary>
[Authorize]
[ApiController]
[Route("api/stocks")]
public class StocksController : ControllerBase
{
    private readonly KleanDbContext _db;

    public StocksController(KleanDbContext db) => _db = db;

    /// <summary>
    /// Trois filtres facultatifs, appliqués avant le comptage comme les filtres de parenté
    /// (CLAUDE.md §6 bis) : <c>Total</c> est celui de la liste filtrée.
    /// </summary>
    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet]
    public async Task<ActionResult<PageDto<StockDto>>> Lister(
        [FromQuery] PaginationDto pagination,
        [FromQuery] Guid? produitId,
        [FromQuery] Guid? emplacementId,
        [FromQuery] Guid? siteId)
    {
        var requete = _db.Stocks.AsNoTracking();

        if (produitId is not null) requete = requete.Where(s => s.ProduitId == produitId);
        if (emplacementId is not null) requete = requete.Where(s => s.EmplacementId == emplacementId);
        if (siteId is not null) requete = requete.Where(s => s.SiteId == siteId);

        var total = await requete.CountAsync();

        var stocks = await requete
            .OrderBy(s => s.ProduitId).ThenBy(s => s.Id)
            .Skip(pagination.ANegliger)
            .Take(pagination.Taille)
            .Select(s => new StockDto
            {
                Id = s.Id,
                EntrepriseId = s.EntrepriseId,
                ProduitId = s.ProduitId,
                EmplacementId = s.EmplacementId,
                SiteId = s.SiteId,
                Quantite = s.Quantite
            })
            .ToListAsync();

        return Ok(new PageDto<StockDto>
        {
            Elements = stocks,
            Total = total,
            Page = pagination.Page,
            Taille = pagination.Taille
        });
    }

    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StockDto>> Obtenir(Guid id)
    {
        var stock = await _db.Stocks.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);

        return stock is null ? NotFound() : Ok(Vers(stock));
    }

    private static StockDto Vers(Stock s) => new()
    {
        Id = s.Id,
        EntrepriseId = s.EntrepriseId,
        ProduitId = s.ProduitId,
        EmplacementId = s.EmplacementId,
        SiteId = s.SiteId,
        Quantite = s.Quantite
    };
}
