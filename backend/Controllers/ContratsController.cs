using Klean.Api.DTOs;
using Klean.Api.Data;
using Klean.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/contrats")]
public class ContratsController : ControllerBase
{
    private readonly KleanDbContext _db;

    public ContratsController(KleanDbContext db) => _db = db;

    /// <summary>
    /// Liste paginée, filtrable par <c>siteId</c> — la descente de niveau,
    /// « les contrats de ce site ».
    ///
    /// ⚠️ Le filtre entre <b>avant</b> le comptage. Un total calculé sur la table
    /// entière annoncerait « 40 contrats » au-dessus d'une liste qui en montre trois,
    /// et la pagination proposerait des pages vides.
    ///
    /// Un identifiant inconnu — ou appartenant à une autre entreprise, que les
    /// filtres globaux écartent — rend une page vide, pas un 404 : une liste filtrée
    /// sur rien reste une liste, et c'est une réponse, pas une erreur.
    /// </summary>
    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet]
    public async Task<ActionResult<PageDto<ContratDto>>> Lister(
        [FromQuery] PaginationDto pagination,
        [FromQuery] Guid? siteId)
    {
        var requete = _db.Contrats.AsNoTracking();

        if (siteId is not null)
        {
            requete = requete.Where(c => c.SiteId == siteId);
        }

        var total = await requete.CountAsync();

        var contrats = await requete
            .OrderByDescending(c => c.DateDebut)
            .Skip(pagination.ANegliger)
            .Take(pagination.Taille)
            .Select(c => new ContratDto
            {
                Id = c.Id,
                EntrepriseId = c.EntrepriseId,
                SiteId = c.SiteId,
                DateDebut = c.DateDebut,
                DateFin = c.DateFin,
                Frequence = c.Frequence,
                Actif = c.Actif
            })
            .ToListAsync();

        return Ok(new PageDto<ContratDto>
        {
            Elements = contrats,
            Total = total,
            Page = pagination.Page,
            Taille = pagination.Taille
        });
    }

    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContratDto>> Obtenir(Guid id)
    {
        var contrat = await _db.Contrats
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new ContratDto
            {
                Id = c.Id,
                EntrepriseId = c.EntrepriseId,
                SiteId = c.SiteId,
                DateDebut = c.DateDebut,
                DateFin = c.DateFin,
                Frequence = c.Frequence,
                Actif = c.Actif
            })
            .FirstOrDefaultAsync();

        return contrat is null ? NotFound() : Ok(contrat);
    }

    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpPost]
    public async Task<ActionResult<ContratDto>> Creer(ContratCreateDto dto)
    {
        if (!await _db.Sites.AnyAsync(s => s.Id == dto.SiteId))
        {
            return BadRequest($"Site {dto.SiteId} inconnu.");
        }

        if (dto.DateFin.HasValue && dto.DateFin.Value < dto.DateDebut)
        {
            return BadRequest("La date de fin précède la date de début.");
        }

        var contrat = new Contrat
        {
            SiteId = dto.SiteId,
            DateDebut = Utc(dto.DateDebut),
            DateFin = Utc(dto.DateFin),
            Frequence = dto.Frequence,
            Actif = dto.Actif
        };

        _db.Contrats.Add(contrat);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Obtenir), new { id = contrat.Id }, Vers(contrat));
    }

    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Modifier(Guid id, ContratCreateDto dto)
    {
        var contrat = await _db.Contrats.FindAsync(id);
        if (contrat is null) return NotFound();

        if (contrat.SiteId != dto.SiteId && !await _db.Sites.AnyAsync(s => s.Id == dto.SiteId))
        {
            return BadRequest($"Site {dto.SiteId} inconnu.");
        }

        if (dto.DateFin.HasValue && dto.DateFin.Value < dto.DateDebut)
        {
            return BadRequest("La date de fin précède la date de début.");
        }

        contrat.SiteId = dto.SiteId;
        contrat.DateDebut = Utc(dto.DateDebut);
        contrat.DateFin = Utc(dto.DateFin);
        contrat.Frequence = dto.Frequence;
        contrat.Actif = dto.Actif;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Supprimer(Guid id)
    {
        var contrat = await _db.Contrats.FindAsync(id);
        if (contrat is null) return NotFound();

        _db.Contrats.Remove(contrat);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Restrict depuis la migration SuppressionRestreinteSurLaChaineExploitation.
            var prestations = await _db.Prestations.CountAsync(p => p.ContratId == id);

            return Conflict(
                $"Ce contrat porte {prestations} prestation(s) et ne peut pas être "
                + "supprimé : sa suppression effacerait aussi toutes les interventions "
                + "déjà planifiées. Supprimer d'abord ses prestations. Pour arrêter un "
                + "contrat sans perdre son historique, renseigner sa date de fin ou le "
                + "passer à Actif = false.");
        }

        return NoContent();
    }

    // Npgsql refuse un DateTime dont le Kind est Unspecified (CLAUDE.md §10).
    private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
    private static DateTime? Utc(DateTime? d) => d.HasValue ? Utc(d.Value) : null;

    private static ContratDto Vers(Contrat c) => new()
    {
        Id = c.Id,
        EntrepriseId = c.EntrepriseId,
        SiteId = c.SiteId,
        DateDebut = c.DateDebut,
        DateFin = c.DateFin,
        Frequence = c.Frequence,
        Actif = c.Actif
    };
}
