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

    /// <summary>
    /// Dossier complet d'un agent — <c>GET /api/agents/{id}/dossier</c>.
    ///
    /// Sert l'accès de l'article 15 et la portabilité de l'article 20 : avant, il
    /// fallait composer la réponse à partir de cinq appels, sans garantie de n'en
    /// oublier aucun. Administrateur seul, et journalisé en
    /// <see cref="NatureAcces.Export"/>.
    ///
    /// ⚠️ Les filtres globaux multi-tenant suffisent au cadrage : un identifiant d'une
    /// autre entreprise rend 404 sans que rien ne soit écrit à la main ici. Le compte
    /// de connexion est la seule exception, <c>AspNetUsers</c> n'étant pas filtrée
    /// (CLAUDE.md §7) — d'où le <c>EntrepriseId</c> explicite dans sa recherche.
    /// </summary>
    [JournaliserAcces(RessourceAccedee.Agent, NatureAcces.Export)]
    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpGet("{id:guid}/dossier")]
    public async Task<ActionResult<DossierAgentDto>> Dossier(Guid id)
    {
        var agent = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);

        if (agent is null) return NotFound();

        var absences = await _db.Absences.AsNoTracking()
            .Where(a => a.AgentId == id)
            .OrderBy(a => a.DateDebut)
            .Select(a => new AbsenceDto
            {
                Id = a.Id,
                EntrepriseId = a.EntrepriseId,
                AgentId = a.AgentId,
                Type = a.Type.ToString(),
                DateDebut = a.DateDebut,
                DateFin = a.DateFin,
                Motif = a.Motif
            })
            .ToListAsync();

        // Le nom du site et sa ville, pas un PrestationId en UUID : l'article 15 exige
        // une forme intelligible, et une liste d'identifiants ne dit rien à un salarié
        // qui demande où il a travaillé.
        var interventions = await _db.Interventions.AsNoTracking()
            .Where(i => i.AgentId == id)
            .OrderBy(i => i.DatePrevue).ThenBy(i => i.HeurePrevue)
            .Select(i => new InterventionDuDossierDto
            {
                Id = i.Id,
                DatePrevue = i.DatePrevue,
                HeurePrevue = i.HeurePrevue,
                Statut = i.Statut.ToString(),
                Prestation = i.Prestation!.Libelle,
                Site = i.Prestation!.Contrat!.Site!.Nom,
                Ville = i.Prestation!.Contrat!.Site!.Ville
            })
            .ToListAsync();

        var remplacements = await _db.Remplacements.AsNoTracking()
            .Where(r => r.AgentRemplacantId == id)
            .OrderBy(r => r.DateCreation)
            .Select(r => new RemplacementDuDossierDto
            {
                Id = r.Id,
                DateCreation = r.DateCreation,
                Statut = r.Statut.ToString(),
                Score = r.Score
            })
            .ToListAsync();

        // ⚠️ Groupé par nature, jamais par lecteur : les lignes du journal nomment des
        // collègues, et l'article 15(4) écarte l'atteinte aux droits d'autrui. Le
        // nombre dit que la traçabilité existe ; les identités appartiennent à la
        // direction, par GET /api/journal-acces.
        var acces = await _db.AccesJournalises.AsNoTracking()
            .Where(a => a.CibleId == id)
            .GroupBy(a => a.Nature)
            .Select(g => new DecompteAccesDto
            {
                Nature = g.Key.ToString(),
                Acces = g.Count(),
                Premier = g.Min(a => a.Horodatage),
                Dernier = g.Max(a => a.Horodatage)
            })
            .ToListAsync();

        var dossier = new DossierAgentDto
        {
            Genere = DateTime.UtcNow,
            Agent = Vers(agent),
            Compte = await CompteDuDossierAsync(agent),
            Absences = absences,
            Interventions = interventions,
            Remplacements = remplacements,
            AccesALaFiche = acces
        };

        dossier.NonInclus.Add(
            "L'empreinte du mot de passe du compte, s'il en existe un : elle n'apporte "
            + "rien à la personne et sa divulgation affaiblirait le secret qu'elle "
            + "protège.");

        dossier.NonInclus.Add(
            "L'historique des connexions et les adresses IP associées : ils vivent dans "
            + "la table des jetons de rafraîchissement, dont la conservation est de "
            + "trente jours et dont la finalité est la sécurité des sessions, non la "
            + "description de la personne.");

        dossier.NonInclus.Add(
            "L'identité des personnes qui ont consulté cette fiche. Seuls leur nombre et "
            + "la nature de leur accès figurent ci-dessus : les nommer identifierait des "
            + "collègues dans une réponse destinée à une seule personne (article 15(4)).");

        dossier.NonInclus.Add(
            "Les consignes des sites et les coordonnées des clients : elles décrivent le "
            + "lieu et le donneur d'ordre, pas l'agent.");

        if (agent.DateAnonymisation is not null)
        {
            // Le dossier d'un agent pseudonymisé ne doit pas se lire comme un dossier
            // vide : il faut dire que les données ont été effacées, et quand, sinon il
            // ressemble à une fiche mal saisie.
            dossier.NonInclus.Add(
                $"Cet agent a été pseudonymisé le {agent.DateAnonymisation:yyyy-MM-dd} : "
                + "nom, prénom, courriel, téléphone, adresse et coordonnées ont été "
                + "remplacés ou effacés. Les interventions et les dates d'absence sont "
                + "conservées, rattachées à un identifiant technique qui subsiste "
                + "(pseudonymisation au sens de l'article 4(5), non anonymisation).");
        }

        return Ok(dossier);
    }

    /// <summary>
    /// Le compte de connexion rattaché, s'il existe.
    ///
    /// ⚠️ Le filtre sur <c>EntrepriseId</c> est écrit à la main, et il est obligatoire :
    /// <c>AspNetUsers</c> n'est pas couverte par les filtres globaux (CLAUDE.md §7).
    /// Sans lui, un compte d'une autre entreprise portant le même <c>AgentId</c>
    /// apparaîtrait dans ce dossier.
    /// </summary>
    private async Task<CompteDuDossierDto?> CompteDuDossierAsync(Agent agent)
    {
        var compte = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.AgentId == agent.Id && u.EntrepriseId == agent.EntrepriseId);

        if (compte is null) return null;

        var roles = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == compte.Id)
            .Join(_db.Roles.AsNoTracking(), ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
            .OrderBy(nom => nom)
            .ToListAsync();

        return new CompteDuDossierDto
        {
            Id = compte.Id,
            Email = compte.Email ?? string.Empty,
            Nom = compte.Nom,
            Prenom = compte.Prenom,
            Telephone = compte.PhoneNumber,
            DateCreation = compte.DateCreation,
            Actif = compte.Actif,
            Roles = roles
        };
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
