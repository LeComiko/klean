using Klean.Api.DTOs;
using Klean.Api.Data;
using Klean.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Controllers;

/// <summary>
/// Le catalogue des prestations de l'entreprise (SPEC.md, étape 21).
///
/// Donnée de référence : lue par les trois rôles internes — le chef d'équipe en aura
/// besoin pour ses devis —, écrite par l'administrateur seul, comme les produits.
/// </summary>
[Authorize]
[ApiController]
[Route("api/types-prestation")]
public class TypesPrestationController : ControllerBase
{
    private readonly KleanDbContext _db;

    public TypesPrestationController(KleanDbContext db) => _db = db;

    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet]
    public async Task<ActionResult<PageDto<TypePrestationDto>>> Lister([FromQuery] PaginationDto pagination)
    {
        var requete = _db.TypesPrestation.AsNoTracking();

        var total = await requete.CountAsync();

        var types = await requete
            .OrderBy(t => t.Libelle)
            .Skip(pagination.ANegliger)
            .Take(pagination.Taille)
            .Select(t => new TypePrestationDto
            {
                Id = t.Id,
                EntrepriseId = t.EntrepriseId,
                Libelle = t.Libelle,
                DureeParDefautMinutes = t.DureeParDefautMinutes,
                Actif = t.Actif
            })
            .ToListAsync();

        return Ok(new PageDto<TypePrestationDto>
        {
            Elements = types,
            Total = total,
            Page = pagination.Page,
            Taille = pagination.Taille
        });
    }

    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TypePrestationDto>> Obtenir(Guid id)
    {
        var type = await _db.TypesPrestation.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);

        return type is null ? NotFound() : Ok(Vers(type));
    }

    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpPost]
    public async Task<ActionResult<TypePrestationDto>> Creer(TypePrestationCreateDto dto)
    {
        var libelle = dto.Libelle.Trim();

        if (await DoublonAsync(libelle, exclu: null) is { } doublon) return Conflict(doublon);

        var type = new TypePrestation
        {
            Libelle = libelle,
            DureeParDefautMinutes = dto.DureeParDefautMinutes,
            Actif = dto.Actif
        };

        _db.TypesPrestation.Add(type);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Obtenir), new { id = type.Id }, Vers(type));
    }

    /// <summary>
    /// Renommer un type ne touche aucune prestation : leur libellé est une copie, et c'est
    /// voulu (SPEC.md, étape 21). Désactiver passe aussi par ici, <c>Actif = false</c>.
    /// </summary>
    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Modifier(Guid id, TypePrestationCreateDto dto)
    {
        var type = await _db.TypesPrestation.FindAsync(id);
        if (type is null) return NotFound();

        var libelle = dto.Libelle.Trim();

        if (await DoublonAsync(libelle, exclu: id) is { } doublon) return Conflict(doublon);

        type.Libelle = libelle;
        type.DureeParDefautMinutes = dto.DureeParDefautMinutes;
        type.Actif = dto.Actif;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Supprimer(Guid id)
    {
        var type = await _db.TypesPrestation.FindAsync(id);
        if (type is null) return NotFound();

        _db.TypesPrestation.Remove(type);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Restrict sur Prestation.TypePrestationId. Le refus nomme la sortie : une
            // friction qui ne dit pas par où passer est un cul-de-sac (CLAUDE.md §10).
            var prestations = await _db.Prestations.CountAsync(p => p.TypePrestationId == id);

            return Conflict(
                $"Ce type est utilisé par {prestations} prestation(s) et ne peut pas être "
                + "supprimé : le stock et le devis s'appuient sur ce lien. Le désactiver le "
                + "retire de la saisie sans rien changer aux prestations qui le portent.");
        }

        return NoContent();
    }

    /// <summary>
    /// Le refus d'un libellé déjà pris dans l'entreprise, à la casse près, ou <c>null</c>.
    ///
    /// ⚠️ Un type désactivé compte : le recréer sous le même nom donnerait deux « Hall
    /// d'entrée » dans l'historique, et l'import de la liste type, qui s'y fie pour ne rien
    /// recréer, le recréerait à chaque fois. Le message dit alors de le réactiver.
    /// </summary>
    private async Task<string?> DoublonAsync(string libelle, Guid? exclu)
    {
        var existant = await _db.TypesPrestation.AsNoTracking()
            .Where(t => t.Libelle.ToLower() == libelle.ToLower() && t.Id != exclu)
            .Select(t => new { t.Libelle, t.Actif })
            .FirstOrDefaultAsync();

        if (existant is null) return null;

        return existant.Actif
            ? $"Le type « {existant.Libelle} » existe déjà."
            : $"Le type « {existant.Libelle} » existe déjà, désactivé : le réactiver plutôt que le recréer.";
    }

    private static TypePrestationDto Vers(TypePrestation t) => new()
    {
        Id = t.Id,
        EntrepriseId = t.EntrepriseId,
        Libelle = t.Libelle,
        DureeParDefautMinutes = t.DureeParDefautMinutes,
        Actif = t.Actif
    };
}
