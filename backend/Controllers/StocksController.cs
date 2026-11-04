using Klean.Api.DTOs;
using Klean.Api.Data;
using Klean.Api.Models;
using Klean.Api.Services;
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
    private readonly ServicePrevisionStock _prevision;

    public StocksController(KleanDbContext db, ServicePrevisionStock prevision)
    {
        _db = db;
        _prevision = prevision;
    }

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

    /// <summary>
    /// Le besoin des <paramref name="semaines"/> à venir et les deux alertes, « sous le seuil »
    /// et « couverture courte » (SPEC.md, étape 22, lot B).
    ///
    /// ⚠️ <c>Exploitation</c> seule, et le chef d'équipe n'en est pas exclu par principe : ses
    /// vacations sont filtrées sur son équipe (CLAUDE.md §7), le stock ne l'est pas. Sa
    /// prévision comparerait le besoin de son équipe au stock de toute l'entreprise, et
    /// annoncerait une couverture que l'entreprise n'a pas. À rouvrir avec un rattachement
    /// des véhicules aux équipes.
    /// </summary>
    [Authorize(Roles = RolesKlean.Exploitation)]
    [HttpGet("prevision")]
    public async Task<ActionResult<PrevisionStockDto>> Prevision(
        CancellationToken annulation, [FromQuery] int semaines = ServicePrevisionStock.SemainesParDefaut)
    {
        // Vérifié ici plutôt que par [Range], dont le refus parlerait anglais (CLAUDE.md §10).
        if (semaines is < 1 or > ServicePrevisionStock.SemainesMaximum)
        {
            return BadRequest(
                $"La prévision porte sur 1 à {ServicePrevisionStock.SemainesMaximum} semaines, "
                + "l'horizon de la génération des vacations.");
        }

        return Ok(await _prevision.CalculerAsync(semaines, annulation));
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
