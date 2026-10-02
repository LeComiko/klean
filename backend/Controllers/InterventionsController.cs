using Klean.Api.DTOs;
using Klean.Api.Filtres;
using Klean.Api.Data;
using Klean.Api.Models;
using Klean.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/interventions")]
public class InterventionsController : ControllerBase
{
    private readonly KleanDbContext _db;
    private readonly AffectationService _affectation;
    private readonly RemplacementService _remplacements;

    public InterventionsController(
        KleanDbContext db, AffectationService affectation, RemplacementService remplacements)
    {
        _db = db;
        _affectation = affectation;
        _remplacements = remplacements;
    }

    [Authorize(Roles = RolesKlean.InterneEtAgent)]
    [HttpGet]
    public async Task<ActionResult<PageDto<InterventionDto>>> Lister([FromQuery] PaginationDto pagination)
    {
        var requete = _db.Interventions
            .AsNoTracking()
            .OrderBy(i => i.DatePrevue).ThenBy(i => i.HeurePrevue);

        var total = await requete.CountAsync();

        var interventions = await requete
            .Skip(pagination.ANegliger)
            .Take(pagination.Taille)
            .Select(i => new InterventionDto
            {
                Id = i.Id,
                EntrepriseId = i.EntrepriseId,
                PrestationId = i.PrestationId,
                AgentId = i.AgentId,
                DatePrevue = i.DatePrevue,
                HeurePrevue = i.HeurePrevue,
                Statut = i.Statut.ToString()
            })
            .ToListAsync();

        return Ok(new PageDto<InterventionDto>
        {
            Elements = interventions,
            Total = total,
            Page = pagination.Page,
            Taille = pagination.Taille
        });
    }

    [Authorize(Roles = RolesKlean.InterneEtAgent)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InterventionDto>> Obtenir(Guid id)
    {
        var intervention = await _db.Interventions
            .AsNoTracking()
            .Where(i => i.Id == id)
            .Select(i => new InterventionDto
            {
                Id = i.Id,
                EntrepriseId = i.EntrepriseId,
                PrestationId = i.PrestationId,
                AgentId = i.AgentId,
                DatePrevue = i.DatePrevue,
                HeurePrevue = i.HeurePrevue,
                Statut = i.Statut.ToString()
            })
            .FirstOrDefaultAsync();

        return intervention is null ? NotFound() : Ok(intervention);
    }

    /// <summary>
    /// Agents candidats à cette intervention, classés par score décroissant.
    ///
    /// Réservé aux rôles internes : la liste nomme des agents, et la matrice des
    /// droits ne donne aucune lecture sur les agents à <c>Agent</c> ni à
    /// <c>ClientSyndic</c>.
    ///
    /// Sans enveloppe de pagination, contrairement aux onze listes de l'étape 8 :
    /// la taille est bornée par le nombre d'agents actifs de l'entreprise, pas
    /// par la requête, et tronquer un classement le rend faux.
    /// </summary>
    /// <summary>
    /// Ouvre — ou retrouve — le remplacement de cette vacation.
    ///
    /// <b>Idempotent.</b> 200 quand un remplacement actif existait déjà, 201 quand
    /// il vient d'être créé. Le corps est le même dans les deux cas : l'appelant
    /// n'a pas à distinguer, c'est le point.
    ///
    /// Placé sur l'intervention et non sur <c>/api/remplacements</c> parce que
    /// l'écran part de la vacation : il connaît l'identifiant de l'intervention et
    /// justement pas celui du remplacement, qui peut ne pas exister. Un
    /// <c>GET</c> puis un <c>POST</c> approximeraient cette opération en deux
    /// appels, avec une fenêtre de course entre les deux — et
    /// <c>GET /api/remplacements</c> n'a aucun filtre, il faudrait balayer.
    ///
    /// Réservé à <c>Exploitation</c> : l'opération écrit.
    /// </summary>
    [Authorize(Roles = RolesKlean.Exploitation)]
    [HttpPost("{id:guid}/remplacement")]
    public async Task<ActionResult<RemplacementDto>> OuvrirRemplacement(Guid id)
    {
        var resultat = await _remplacements.OuvrirAsync(id);

        return resultat.Issue switch
        {
            IssueRemplacement.Introuvable => NotFound($"Intervention {id} inconnue."),
            IssueRemplacement.Refusee => BadRequest(resultat.Message),
            _ when resultat.Cree => StatusCode(StatusCodes.Status201Created, resultat.Valeur),
            _ => Ok(resultat.Valeur)
        };
    }

    [JournaliserAcces(RessourceAccedee.Candidats, NatureAcces.Liste)]
    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet("{id:guid}/candidats")]
    public async Task<ActionResult<IEnumerable<CandidatDto>>> Candidats(Guid id)
    {
        var candidats = await _affectation.GetCandidatsAsync(id);

        return candidats is null ? NotFound() : Ok(candidats);
    }

    [Authorize(Roles = RolesKlean.Exploitation)]
    [HttpPost]
    public async Task<ActionResult<InterventionDto>> Creer(InterventionCreateDto dto)
    {
        if (!Enum.TryParse<StatutIntervention>(dto.Statut, out var statut))
        {
            return BadRequest($"Statut d'intervention « {dto.Statut} » invalide. Valeurs acceptées : {string.Join(", ", Enum.GetNames<StatutIntervention>())}.");
        }

        if (!await _db.Prestations.AnyAsync(p => p.Id == dto.PrestationId))
        {
            return BadRequest($"Prestation {dto.PrestationId} inconnue.");
        }

        if (dto.AgentId.HasValue && !await _db.Agents.AnyAsync(a => a.Id == dto.AgentId.Value))
        {
            return BadRequest($"Agent {dto.AgentId.Value} inconnu.");
        }

        var refus = await RefuserSiIneligible(dto, interventionAExclure: null,
            "Créer la vacation sans agent, puis affecter quand le créneau est libre.");
        if (refus is not null) return refus;

        var intervention = new Intervention
        {
            PrestationId = dto.PrestationId,
            AgentId = dto.AgentId,
            DatePrevue = DateTime.SpecifyKind(dto.DatePrevue, DateTimeKind.Utc),
            HeurePrevue = dto.HeurePrevue,
            Statut = statut
        };

        _db.Interventions.Add(intervention);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Obtenir), new { id = intervention.Id }, Vers(intervention));
    }

    [Authorize(Roles = RolesKlean.Interne)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Modifier(Guid id, InterventionCreateDto dto)
    {
        if (!Enum.TryParse<StatutIntervention>(dto.Statut, out var statut))
        {
            return BadRequest($"Statut d'intervention « {dto.Statut} » invalide. Valeurs acceptées : {string.Join(", ", Enum.GetNames<StatutIntervention>())}.");
        }

        var intervention = await _db.Interventions.FindAsync(id);
        if (intervention is null) return NotFound();

        if (intervention.PrestationId != dto.PrestationId
            && !await _db.Prestations.AnyAsync(p => p.Id == dto.PrestationId))
        {
            return BadRequest($"Prestation {dto.PrestationId} inconnue.");
        }

        if (dto.AgentId.HasValue && dto.AgentId != intervention.AgentId
            && !await _db.Agents.AnyAsync(a => a.Id == dto.AgentId.Value))
        {
            return BadRequest($"Agent {dto.AgentId.Value} inconnu.");
        }

        var refus = await RefuserSiIneligible(dto, interventionAExclure: id,
            $"Voir GET /api/interventions/{id}/candidats.");
        if (refus is not null) return refus;

        intervention.PrestationId = dto.PrestationId;
        intervention.AgentId = dto.AgentId;
        intervention.DatePrevue = DateTime.SpecifyKind(dto.DatePrevue, DateTimeKind.Utc);
        intervention.HeurePrevue = dto.HeurePrevue;
        intervention.Statut = statut;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = RolesKlean.Exploitation)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Supprimer(Guid id)
    {
        var intervention = await _db.Interventions.FindAsync(id);
        if (intervention is null) return NotFound();

        _db.Interventions.Remove(intervention);

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

    /// <summary>
    /// Refuse l'écriture si l'agent demandé ne peut pas tenir le créneau, sur la
    /// création comme sur la modification.
    ///
    /// ⚠️ <b>Cette garde comble une faille réelle.</b> Avant elle, le PUT écrivait
    /// <c>AgentId</c> après avoir seulement vérifié que l'agent existait : un agent
    /// en arrêt maladie ce jour-là, absent de la liste des candidats, était accepté
    /// avec un 204. Les quatre filtres durs du moteur d'affectation étaient donc
    /// contournables par une requête parfaitement légitime d'un Responsable — sans
    /// rien forger. <c>CLAUDE.md</c> §9 affirmait par ailleurs qu'<c>AccepterAsync</c>
    /// était le seul endroit du code où une intervention change d'agent ; ce n'était
    /// pas le cas.
    ///
    /// La vérification porte sur l'horaire <b>demandé</b>, pas seulement sur le
    /// changement d'agent : déplacer une vacation en laissant le même agent peut
    /// tout autant la rendre illégale. Elle s'applique donc à chaque écriture qui
    /// porte un agent, même quand rien ne change — une ligne déjà illégale ne doit
    /// pas pouvoir être réenregistrée telle quelle. Pour la débloquer, il reste
    /// possible de la libérer d'abord, en envoyant <c>agentId</c> à null.
    ///
    /// Aucun drapeau de forçage : une échappatoire qui existe devient le chemin
    /// normal dès la première fois qu'elle dépanne.
    /// </summary>
    private async Task<ActionResult?> RefuserSiIneligible(
        InterventionCreateDto dto, Guid? interventionAExclure, string ouVoir)
    {
        if (!dto.AgentId.HasValue) return null;

        var creneau = await _affectation.CreneauAsync(
            dto.PrestationId, dto.DatePrevue, dto.HeurePrevue);

        if (creneau is null)
        {
            return BadRequest(
                $"Prestation {dto.PrestationId} sans contrat ni site rattaché : "
                + "impossible de situer le créneau.");
        }

        var eligible = await _affectation.EstEligibleAsync(
            dto.AgentId.Value, creneau.Value, interventionAExclure);

        return eligible
            ? null
            : BadRequest(AffectationService.MessageIneligibilite(dto.AgentId.Value, ouVoir));
    }

    private static InterventionDto Vers(Intervention i) => new()
    {
        Id = i.Id,
        EntrepriseId = i.EntrepriseId,
        PrestationId = i.PrestationId,
        AgentId = i.AgentId,
        DatePrevue = i.DatePrevue,
        HeurePrevue = i.HeurePrevue,
        Statut = i.Statut.ToString()
    };
}
