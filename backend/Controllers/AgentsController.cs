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
[Route("api/agents")]
public class AgentsController : ControllerBase
{
    private readonly KleanDbContext _db;

    private readonly ServiceAnonymisation _anonymisation;

    public AgentsController(KleanDbContext db, ServiceAnonymisation anonymisation)
    {
        _db = db;
        _anonymisation = anonymisation;
    }

    [JournaliserAcces(RessourceAccedee.Agent, NatureAcces.Liste)]
    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet]
    public async Task<ActionResult<PageDto<AgentDto>>> Lister([FromQuery] PaginationDto pagination)
    {
        var requete = _db.Agents
            .AsNoTracking()
            .OrderBy(a => a.Nom).ThenBy(a => a.Prenom);

        var total = await requete.CountAsync();

        var agents = await requete
            .Skip(pagination.ANegliger)
            .Take(pagination.Taille)
            .Select(a => new AgentDto
            {
                Id = a.Id,
                EntrepriseId = a.EntrepriseId,
                Nom = a.Nom,
                Prenom = a.Prenom,
                Email = a.Email,
                Telephone = a.Telephone,
                Adresse = a.Adresse,
                Latitude = a.Latitude,
                Longitude = a.Longitude,
                DateEmbauche = a.DateEmbauche,
                Actif = a.Actif,
                DateAnonymisation = a.DateAnonymisation
            })
            .ToListAsync();

        return Ok(new PageDto<AgentDto>
        {
            Elements = agents,
            Total = total,
            Page = pagination.Page,
            Taille = pagination.Taille
        });
    }

    [JournaliserAcces(RessourceAccedee.Agent, NatureAcces.Lecture)]
    [Authorize(Roles = RolesKlean.Interne)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AgentDto>> Obtenir(Guid id)
    {
        var agent = await _db.Agents
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new AgentDto
            {
                Id = a.Id,
                EntrepriseId = a.EntrepriseId,
                Nom = a.Nom,
                Prenom = a.Prenom,
                Email = a.Email,
                Telephone = a.Telephone,
                Adresse = a.Adresse,
                Latitude = a.Latitude,
                Longitude = a.Longitude,
                DateEmbauche = a.DateEmbauche,
                Actif = a.Actif,
                DateAnonymisation = a.DateAnonymisation
            })
            .FirstOrDefaultAsync();

        return agent is null ? NotFound() : Ok(agent);
    }

    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpPost]
    public async Task<ActionResult<AgentDto>> Creer(AgentCreateDto dto)
    {
        var agent = new Agent
        {
            Nom = dto.Nom,
            Prenom = dto.Prenom,
            Email = dto.Email,
            Telephone = dto.Telephone,
            Adresse = dto.Adresse,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            // Npgsql refuse un DateTime dont le Kind est Unspecified, ce que
            // produit la liaison JSON d'une date sans fuseau (CLAUDE.md §10).
            DateEmbauche = DateTime.SpecifyKind(dto.DateEmbauche, DateTimeKind.Utc),
            Actif = dto.Actif
        };

        _db.Agents.Add(agent);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Obtenir), new { id = agent.Id }, Vers(agent));
    }

    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Modifier(Guid id, AgentCreateDto dto)
    {
        var agent = await _db.Agents.FindAsync(id);
        if (agent is null) return NotFound();

        agent.Nom = dto.Nom;
        agent.Prenom = dto.Prenom;
        agent.Email = dto.Email;
        agent.Telephone = dto.Telephone;
        agent.Adresse = dto.Adresse;
        agent.Latitude = dto.Latitude;
        agent.Longitude = dto.Longitude;
        agent.DateEmbauche = DateTime.SpecifyKind(dto.DateEmbauche, DateTimeKind.Utc);
        agent.Actif = dto.Actif;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Supprimer(Guid id)
    {
        var agent = await _db.Agents.FindAsync(id);
        if (agent is null) return NotFound();

        _db.Agents.Remove(agent);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Restrict sur Intervention.AgentId et Remplacement.AgentRemplacantId
            // depuis l'origine : l'historique du planning ne doit pas disparaître
            // avec l'agent. Et sur Absence.AgentId depuis la migration
            // SuppressionRestreinteSurLaChaineExploitation.
            //
            // ⚠️ Les deux comptes sont nécessaires. Ce message ne comptait que les
            // interventions ; un agent bloqué par ses seules absences lisait donc
            // « porte 0 intervention(s) et ne peut pas être supprimé », ce qui
            // ressemble à un défaut du logiciel plutôt qu'à une règle.
            var interventions = await _db.Interventions.CountAsync(i => i.AgentId == id);
            var absences = await _db.Absences.CountAsync(a => a.AgentId == id);

            return Conflict(
                $"Cet agent porte {interventions} intervention(s) et {absences} absence(s), "
                + "et ne peut pas être supprimé : son historique de planning et ses arrêts "
                + "partiraient avec lui. Le passer à Actif = false l'écarte de tout "
                + "classement de remplaçants sans rien perdre.");
        }

        return NoContent();
    }

    /// <summary>
    /// Pseudonymise un agent : remplace ses données identifiantes, celles de son compte
    /// de connexion s'il en a un, et les motifs en texte libre de ses absences. La ligne
    /// et son historique sont conservés.
    ///
    /// C'est la réponse au conflit de l'étape 17 : <c>Restrict</c> sur
    /// <c>Intervention.AgentId</c> rend un agent insupprimable dès qu'il a travaillé —
    /// le bon choix pour l'intégrité du planning, incompatible avec une demande
    /// d'effacement.
    ///
    /// ⚠️ <b>Irréversible, et administrateur seul.</b> Pas de <c>Responsable</c> : ce
    /// n'est pas un geste d'exploitation, c'est une réponse à l'exercice d'un droit, et
    /// rien ne la défait.
    ///
    /// ⚠️ Le compte rendu énumère <b>ce qui subsiste</b>. Un retour qui n'annoncerait que
    /// des succès laisserait promettre un effacement total à qui le demande.
    /// </summary>
    [Authorize(Roles = RolesKlean.Administrateur)]
    [JournaliserAcces(RessourceAccedee.Agent, NatureAcces.Anonymisation)]
    [HttpPost("{id:guid}/anonymiser")]
    public async Task<ActionResult<AnonymisationDto>> Anonymiser(Guid id)
    {
        var resultat = await _anonymisation.ExecuterAsync(id);

        if (resultat is null) return NotFound();

        return resultat.Value.Conflit is { } conflit
            ? Conflict(conflit)
            : Ok(resultat.Value.Valeur);
    }

    private static AgentDto Vers(Agent a) => new()
    {
        Id = a.Id,
        EntrepriseId = a.EntrepriseId,
        Nom = a.Nom,
        Prenom = a.Prenom,
        Email = a.Email,
        Telephone = a.Telephone,
        Adresse = a.Adresse,
        Latitude = a.Latitude,
        Longitude = a.Longitude,
        DateEmbauche = a.DateEmbauche,
        Actif = a.Actif,
        DateAnonymisation = a.DateAnonymisation
    };
}
