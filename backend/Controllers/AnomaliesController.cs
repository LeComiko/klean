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
/// Les anomalies constatées sur place, et leurs photos (SPEC.md, étape 25, lot B).
///
/// L'agent les signale <b>pendant</b> son passage, sur sa propre vacation ; le chef d'équipe
/// et le responsable les lisent et les traitent, chacun dans son périmètre — les filtres
/// globaux suivent ceux des vacations. Rien pour le syndic avant le portail.
///
/// Non journalisées, et c'est motivé : l'auteur n'y est qu'un identifiant, comme sur un
/// mouvement de stock, et aucune ne nomme une personne par construction. ⚠️ Une photo, elle,
/// peut en montrer une : la question revient au lot D, avec la cartographie.
/// </summary>
[Authorize]
[ApiController]
[Route("api/anomalies")]
public class AnomaliesController : ControllerBase
{
    /// <summary>De quoi montrer ce qu'on décrit ; au-delà, c'est un reportage, pas une anomalie.</summary>
    public const int PhotosParAnomalie = 3;

    public const int LongueurMaxDescription = 1000;

    private readonly KleanDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly TimeProvider _horloge;

    public AnomaliesController(KleanDbContext db, ITenantContext tenant, TimeProvider horloge)
    {
        _db = db;
        _tenant = tenant;
        _horloge = horloge;
    }

    /// <summary>Les anomalies de l'entreprise, ou de l'équipe pour un chef, les plus récentes d'abord.</summary>
    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet]
    public async Task<ActionResult<PageDto<AnomalieDto>>> Lister(
        [FromQuery] PaginationDto pagination, [FromQuery] string? statut)
    {
        var requete = _db.Anomalies.AsNoTracking();

        if (!string.IsNullOrEmpty(statut))
        {
            // Par ses noms exacts : `Enum.TryParse` accepterait « 0 » (CLAUDE.md §5).
            if (!Enum.GetNames<StatutAnomalie>().Contains(statut))
            {
                return BadRequest(
                    $"Statut d'anomalie « {statut} » invalide. Valeurs acceptées : {string.Join(", ", Enum.GetNames<StatutAnomalie>())}.");
            }

            var lu = Enum.Parse<StatutAnomalie>(statut);
            requete = requete.Where(a => a.Statut == lu);
        }

        var total = await requete.CountAsync();

        var anomalies = await Projeter(requete
                .OrderByDescending(a => a.DateSignalement).ThenBy(a => a.Id)
                .Skip(pagination.ANegliger)
                .Take(pagination.Taille))
            .ToListAsync();

        return Ok(new PageDto<AnomalieDto>
        {
            Elements = anomalies,
            Total = total,
            Page = pagination.Page,
            Taille = pagination.Taille
        });
    }

    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AnomalieDto>> Obtenir(Guid id)
    {
        var anomalie = await Projeter(_db.Anomalies.AsNoTracking().Where(a => a.Id == id)).FirstOrDefaultAsync();

        return anomalie is null ? NotFound() : Ok(anomalie);
    }

    /// <summary>
    /// Le signalement, par l'agent affecté, pendant le passage. Une vacation qui n'est pas la
    /// sienne est inconnue — 404 —, et le cadrage est écrit en plus du filtre global, comme
    /// pour le pointage.
    /// </summary>
    [Authorize(Roles = RolesKlean.Agent)]
    [HttpPost]
    public async Task<ActionResult<AnomalieAgentDto>> Signaler(AnomalieCreateDto dto)
    {
        var description = dto.Description?.Trim() ?? string.Empty;

        if (description.Length == 0) return BadRequest("Décrire l'anomalie : ce qui a été constaté, et où.");

        if (description.Length > LongueurMaxDescription)
        {
            return BadRequest($"La description tient en {LongueurMaxDescription} caractères au plus.");
        }

        var vacation = await VacationDeLAgentAsync(dto.InterventionId);
        if (vacation is null) return NotFound();

        if (vacation.Statut != StatutIntervention.EnCours) return BadRequest(RefusHorsPassage(vacation.Statut));

        var anomalie = new Anomalie
        {
            InterventionId = vacation.Id,
            Description = description,
            DateSignalement = _horloge.GetUtcNow().UtcDateTime,
            AuteurId = Appelant()
        };

        _db.Anomalies.Add(anomalie);
        await _db.SaveChangesAsync();

        return StatusCode(StatusCodes.Status201Created,
            new AnomalieAgentDto { Id = anomalie.Id, Description = anomalie.Description, Photos = 0 });
    }

    /// <summary>
    /// Une photo, en corps brut <c>image/jpeg</c>. Le plafond de corps de l'API, un mégaoctet,
    /// s'applique avant toute lecture ; l'écran réduit la photo pour y tenir.
    ///
    /// ⚠️ Les métadonnées sont retirées <b>ici</b>, quoi que l'écran ait fait : c'est la seule
    /// garde qu'une requête forgée ne contourne pas.
    /// </summary>
    [Authorize(Roles = RolesKlean.Agent)]
    [HttpPost("{id:guid}/photos")]
    public async Task<ActionResult<PhotoAnomalieDto>> AjouterPhoto(Guid id, CancellationToken annulation)
    {
        var anomalie = await _db.Anomalies
            .Where(a => a.Id == id && a.Intervention!.AgentId == _tenant.AgentId)
            .Select(a => new { a.Id, a.Intervention!.Statut, Photos = a.Photos.Count })
            .FirstOrDefaultAsync(annulation);

        if (anomalie is null) return NotFound();

        if (anomalie.Statut != StatutIntervention.EnCours) return BadRequest(RefusHorsPassage(anomalie.Statut));

        if (anomalie.Photos >= PhotosParAnomalie)
        {
            return Conflict($"{PhotosParAnomalie} photos au plus par anomalie : celle-ci est complète.");
        }

        if (Request.ContentType?.Split(';')[0].Trim() != "image/jpeg")
        {
            return BadRequest("Envoyer la photo en JPEG, telle que l'écran la prépare.");
        }

        using var tampon = new MemoryStream();
        await Request.Body.CopyToAsync(tampon, annulation);

        var nettoyee = MetadonneesJpeg.Nettoyer(tampon.GetBuffer().AsSpan(0, (int)tampon.Length));
        if (nettoyee is null) return BadRequest("Ce fichier n'est pas une photo JPEG lisible.");

        var photo = new PhotoAnomalie
        {
            AnomalieId = anomalie.Id,
            Contenu = nettoyee,
            Taille = nettoyee.Length,
            DateAjout = _horloge.GetUtcNow().UtcDateTime
        };

        _db.PhotosAnomalie.Add(photo);
        await _db.SaveChangesAsync(annulation);

        return StatusCode(StatusCodes.Status201Created, new PhotoAnomalieDto
        {
            Id = photo.Id, AnomalieId = photo.AnomalieId, Taille = photo.Taille, DateAjout = photo.DateAjout
        });
    }

    /// <summary>
    /// Le contenu d'une photo. Ouvert à l'agent pour les siennes, que le filtre global cadre.
    /// ⚠️ Jamais mise en cache : une photo de parties communes peut montrer un résident.
    /// </summary>
    [Authorize(Roles = RolesKlean.InterneEtAgent)]
    [HttpGet("{id:guid}/photos/{photoId:guid}")]
    public async Task<IActionResult> LirePhoto(Guid id, Guid photoId, CancellationToken annulation)
    {
        var contenu = await _db.PhotosAnomalie.AsNoTracking()
            .Where(p => p.Id == photoId && p.AnomalieId == id)
            .Select(p => p.Contenu)
            .FirstOrDefaultAsync(annulation);

        if (contenu is null) return NotFound();

        Response.Headers.CacheControl = "private, no-store";
        return File(contenu, "image/jpeg");
    }

    /// <summary>Le geste qui ferme une anomalie. Une seconde fois est refusée, en disant quand.</summary>
    [Authorize(Roles = RolesKlean.Interne)]
    [HttpPost("{id:guid}/traiter")]
    public async Task<ActionResult<AnomalieDto>> Traiter(Guid id)
    {
        var anomalie = await _db.Anomalies.FindAsync(id);
        if (anomalie is null) return NotFound();

        if (anomalie.Statut == StatutAnomalie.Traitee)
        {
            return BadRequest($"Anomalie déjà traitée le {anomalie.DateTraitement:dd/MM/yyyy}.");
        }

        anomalie.Statut = StatutAnomalie.Traitee;
        anomalie.DateTraitement = _horloge.GetUtcNow().UtcDateTime;
        anomalie.TraiteeParId = Appelant();

        await _db.SaveChangesAsync();

        return Ok(await Projeter(_db.Anomalies.AsNoTracking().Where(a => a.Id == id)).SingleAsync());
    }

    private Task<Intervention?> VacationDeLAgentAsync(Guid interventionId) =>
        _db.Interventions.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == interventionId && i.AgentId == _tenant.AgentId);

    private static string RefusHorsPassage(StatutIntervention statut) =>
        $"Vacation au statut {statut} : une anomalie se signale pendant le passage, "
        + $"sur une vacation {StatutIntervention.EnCours}.";

    private Guid Appelant() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>⚠️ Projette sans le contenu des photos : leurs seuls identifiants.</summary>
    private static IQueryable<AnomalieDto> Projeter(IQueryable<Anomalie> anomalies) =>
        anomalies.Select(a => new AnomalieDto
        {
            Id = a.Id,
            InterventionId = a.InterventionId,
            SiteId = a.Intervention!.Prestation!.Contrat!.SiteId,
            Site = a.Intervention.Prestation.Contrat.Site!.Nom,
            DatePrevue = a.Intervention.DatePrevue,
            Description = a.Description,
            Statut = a.Statut.ToString(),
            DateSignalement = a.DateSignalement,
            AuteurId = a.AuteurId,
            DateTraitement = a.DateTraitement,
            TraiteeParId = a.TraiteeParId,
            Photos = a.Photos.OrderBy(p => p.DateAjout).Select(p => p.Id).ToList()
        });
}
