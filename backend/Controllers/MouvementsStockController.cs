using System.Security.Claims;
using Klean.Api.DTOs;
using Klean.Api.Data;
using Klean.Api.Models;
using Klean.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Controllers;

/// <summary>
/// Les mouvements de stock (SPEC.md, étape 22) : lus par les rôles internes, enregistrés
/// par <c>Exploitation</c>.
///
/// ⚠️ Ni <c>PUT</c> ni <c>DELETE</c>, et c'est la règle, pas un manque : un mouvement est
/// l'histoire d'une quantité. Une erreur se corrige par un mouvement de plus — un
/// inventaire, une sortie — et l'historique dit les deux.
///
/// L'auteur est un identifiant de compte, jamais un nom : la route n'est donc pas
/// journalisée (CLAUDE.md §7 bis), et la personne se résout par le compte.
/// </summary>
[Authorize]
[ApiController]
[Route("api/mouvements-stock")]
public class MouvementsStockController : ControllerBase
{
    private readonly KleanDbContext _db;
    private readonly ServiceStock _stock;

    public MouvementsStockController(KleanDbContext db, ServiceStock stock)
    {
        _db = db;
        _stock = stock;
    }

    /// <summary>Du plus récent au plus ancien ; <c>?produitId=</c> filtre avant le comptage.</summary>
    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet]
    public async Task<ActionResult<PageDto<MouvementStockDto>>> Lister(
        [FromQuery] PaginationDto pagination, [FromQuery] Guid? produitId)
    {
        var requete = _db.MouvementsStock.AsNoTracking();

        if (produitId is not null) requete = requete.Where(m => m.ProduitId == produitId);

        var total = await requete.CountAsync();

        var mouvements = await requete
            .OrderByDescending(m => m.DateMouvement).ThenBy(m => m.Id)
            .Skip(pagination.ANegliger)
            .Take(pagination.Taille)
            .ToListAsync();

        return Ok(new PageDto<MouvementStockDto>
        {
            Elements = mouvements.Select(ServiceStock.Vers).ToList(),
            Total = total,
            Page = pagination.Page,
            Taille = pagination.Taille
        });
    }

    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MouvementStockDto>> Obtenir(Guid id)
    {
        var mouvement = await _db.MouvementsStock.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);

        return mouvement is null ? NotFound() : Ok(ServiceStock.Vers(mouvement));
    }

    [Authorize(Roles = RolesKlean.Exploitation)]
    [HttpPost]
    public async Task<ActionResult<MouvementStockDto>> Enregistrer(
        MouvementStockCreateDto dto, CancellationToken annulation)
    {
        var auteur = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var resultat = await _stock.EnregistrerAsync(dto, auteur, annulation);

        if (resultat.Mouvement is not { } mouvement) return BadRequest(resultat.Refus);

        return CreatedAtAction(nameof(Obtenir), new { id = mouvement.Id }, mouvement);
    }
}
