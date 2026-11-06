using Klean.Api.DTOs;
using Klean.Api.Data;
using Klean.Api.Models;
using Klean.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
    /// <summary>
    /// Premier entier du verrou consultatif de l'import. La forme à deux entiers n'a aucune
    /// clé commune avec la forme à un <c>bigint</c> de la génération : les deux verrous ne
    /// peuvent pas se confondre, quelle que soit l'entreprise.
    /// </summary>
    private const int EspaceVerrouImport = 21;

    /// <summary>L'index unique <c>(EntrepriseId, lower(Libelle))</c> de la migration <c>LibelleDeTypeUnique</c>.</summary>
    private const string IndexLibelleUnique = "IX_TypesPrestation_EntrepriseId_Libelle_Casse";

    private readonly KleanDbContext _db;
    private readonly ITenantContext _tenant;

    public TypesPrestationController(KleanDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

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

        if (await EnregistrerAsync() is { } course) return Conflict(course);

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

        if (await EnregistrerAsync() is { } course) return Conflict(course);

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

            // Restrict aussi depuis la consommation par type (étape 22, lot B). Un type qu'aucune
            // prestation ne porte n'a rien d'historique : ses consommations se retirent.
            // Et depuis sa checklist (étape 25, lot B) : le refus compte les deux.
            if (prestations == 0)
            {
                var consommations = await _db.ConsommationsType.CountAsync(c => c.TypePrestationId == id);
                var points = await _db.PointsControle.CountAsync(p => p.TypePrestationId == id);

                return Conflict(
                    $"Ce type porte {consommations} consommation(s) de produit et {points} point(s) "
                    + "de contrôle : les retirer d'abord, ou le désactiver.");
            }

            return Conflict(
                $"Ce type est utilisé par {prestations} prestation(s) et ne peut pas être "
                + "supprimé : le stock et le devis s'appuient sur ce lien. Le désactiver le "
                + "retire de la saisie sans rien changer aux prestations qui le portent.");
        }

        return NoContent();
    }

    /// <summary>
    /// Ajoute au catalogue les types de <see cref="ListeTypePrestations"/> qui n'y sont pas
    /// encore (SPEC.md, étape 21, lot B).
    ///
    /// Rejouable sans rien abîmer : un libellé déjà présent, à la casse et aux espaces près,
    /// est laissé tel quel, durée corrigée et <b>désactivation comprises</b>. Un type désactivé est un type qu'on a retiré ; le recréer actif à
    /// chaque import annulerait ce retrait.
    /// </summary>
    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpPost("importer-liste-type")]
    public async Task<ActionResult<ImportListeTypeDto>> ImporterListeType(CancellationToken annulation)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(annulation);

        // ⚠️ Deux imports simultanés — un double clic suffit — liraient chacun un catalogue
        // vide. L'index unique empêcherait le doublon, mais l'un des deux échouerait au
        // milieu. Le verrou les sérialise : le second lit ce que le premier a écrit, et
        // répond « déjà présents » au lieu d'une erreur.
        await _db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock({EspaceVerrouImport}, {CleDeVerrou()})", annulation);

        var existants = (await _db.TypesPrestation.AsNoTracking()
                .Select(t => t.Libelle)
                .ToListAsync(annulation))
            .Select(Cle)
            .ToHashSet();

        var compteRendu = new ImportListeTypeDto();

        foreach (var (libelle, duree) in ListeTypePrestations.Elements)
        {
            if (!existants.Add(Cle(libelle)))
            {
                compteRendu.DejaPresents.Add(libelle);
                continue;
            }

            _db.TypesPrestation.Add(new TypePrestation { Libelle = libelle, DureeParDefautMinutes = duree });
            compteRendu.Crees.Add(libelle);
        }

        await _db.SaveChangesAsync(annulation);
        await transaction.CommitAsync(annulation);

        return Ok(compteRendu);
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

    /// <summary>
    /// Enregistre, et rend le refus si l'index unique a tranché à la place de
    /// <see cref="DoublonAsync"/> : deux créations du même libellé lues avant l'écriture de
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
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: IndexLibelleUnique
        })
        {
            return "Ce libellé vient d'être enregistré par ailleurs. Recharger le catalogue.";
        }
    }

    /// <summary>
    /// La comparaison de l'index, <c>lower()</c>, plus la suppression des espaces de bord que
    /// la saisie fait déjà. ⚠️ Une base créée avec la locale <c>C</c> ne mettrait en
    /// minuscules que l'ASCII : « É » et « é » y seraient deux libellés.
    /// </summary>
    private static string Cle(string libelle) => libelle.Trim().ToLowerInvariant();

    /// <summary>L'entreprise ramenée à 32 bits ; une collision ne ferait que sérialiser deux imports.</summary>
    private int CleDeVerrou() =>
        BitConverter.ToInt32((_tenant.EntrepriseId ?? Guid.Empty).ToByteArray(), 0);

    private static TypePrestationDto Vers(TypePrestation t) => new()
    {
        Id = t.Id,
        EntrepriseId = t.EntrepriseId,
        Libelle = t.Libelle,
        DureeParDefautMinutes = t.DureeParDefautMinutes,
        Actif = t.Actif
    };
}
