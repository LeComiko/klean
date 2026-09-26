using System.Security.Claims;
using Klean.Api.DTOs;
using Klean.Api.Data;
using Klean.Api.Models;
using Klean.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly KleanDbContext _db;
    private readonly UserManager<Utilisateur> _utilisateurs;
    private readonly SignInManager<Utilisateur> _connexions;
    private readonly ServiceSession _session;

    public AuthController(
        KleanDbContext db,
        UserManager<Utilisateur> utilisateurs,
        SignInManager<Utilisateur> connexions,
        ServiceSession session)
    {
        _db = db;
        _utilisateurs = utilisateurs;
        _connexions = connexions;
        _session = session;
    }

    /// <summary>
    /// Connexion. Pose les trois cookies de session et ne renvoie <b>aucun</b>
    /// jeton dans le corps.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("connexion")]
    public async Task<ActionResult<SessionDto>> Connexion(ConnexionDto dto)
    {
        var utilisateur = await _utilisateurs.FindByEmailAsync(dto.Email);

        // Même réponse que pour un mot de passe faux : ne pas révéler quels
        // comptes existent.
        if (utilisateur is null)
        {
            return Unauthorized("Identifiants invalides.");
        }

        if (!utilisateur.Actif)
        {
            return Unauthorized("Ce compte est désactivé.");
        }

        var resultat = await _connexions.CheckPasswordSignInAsync(
            utilisateur, dto.MotDePasse, lockoutOnFailure: true);

        if (resultat.IsLockedOut)
        {
            return Unauthorized("Compte verrouillé après cinq échecs. Réessayer dans quinze minutes.");
        }

        if (!resultat.Succeeded)
        {
            return Unauthorized("Identifiants invalides.");
        }

        return Ok(await _session.OuvrirAsync(HttpContext, utilisateur));
    }

    /// <summary>
    /// Renouvelle le jeton d'accès à partir du cookie de rafraîchissement, et fait
    /// tourner ce dernier.
    ///
    /// Anonyme par construction : le jeton d'accès vient d'expirer, c'est tout
    /// l'objet de l'appel. L'autorisation est portée par le cookie de
    /// rafraîchissement, et la protection CSRF s'applique — le middleware la
    /// déclenche sur la seule présence de ce cookie.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("rafraichir")]
    public async Task<ActionResult<SessionDto>> Rafraichir()
    {
        var resultat = await _session.RafraichirAsync(HttpContext);

        return resultat.Valeur is null ? Unauthorized(resultat.Erreur) : Ok(resultat.Valeur);
    }

    /// <summary>
    /// Déconnexion : révoque le jeton de rafraîchissement et efface les cookies.
    ///
    /// Anonyme délibérément. Exiger un jeton d'accès valide rendrait impossible la
    /// fermeture propre d'une session dont l'accès a expiré — l'utilisateur
    /// croirait s'être déconnecté alors que son jeton de rafraîchissement resterait
    /// valable quatorze jours.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("deconnexion")]
    public async Task<IActionResult> Deconnexion()
    {
        await _session.FermerAsync(HttpContext);

        return NoContent();
    }

    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpPost("comptes")]
    public async Task<ActionResult<ProfilDto>> CreerCompte(CompteCreateDto dto)
    {
        if (!RolesKlean.Tous.Contains(dto.Role))
        {
            return BadRequest($"Rôle « {dto.Role} » invalide. Valeurs acceptées : {string.Join(", ", RolesKlean.Tous)}.");
        }

        if (dto.Role == RolesKlean.Agent && !dto.AgentId.HasValue)
        {
            return BadRequest("Un compte de rôle Agent doit porter un AgentId.");
        }

        if (dto.Role == RolesKlean.ClientSyndic && !dto.ClientId.HasValue)
        {
            return BadRequest("Un compte de rôle ClientSyndic doit porter un ClientId.");
        }

        // Le compte créé appartient à l'entreprise de l'administrateur qui le
        // crée, jamais à une entreprise choisie dans le corps de la requête.
        var entrepriseId = EntrepriseIdDuJeton();
        if (entrepriseId is null)
        {
            return Unauthorized("Jeton sans revendication entrepriseId.");
        }

        if (dto.AgentId.HasValue
            && !await _db.Agents.AnyAsync(a => a.Id == dto.AgentId.Value && a.EntrepriseId == entrepriseId))
        {
            return BadRequest($"Agent {dto.AgentId.Value} inconnu dans cette entreprise.");
        }

        if (dto.ClientId.HasValue
            && !await _db.Clients.AnyAsync(c => c.Id == dto.ClientId.Value && c.EntrepriseId == entrepriseId))
        {
            return BadRequest($"Client {dto.ClientId.Value} inconnu dans cette entreprise.");
        }

        var utilisateur = new Utilisateur
        {
            // UserName doit valoir la même chose qu'Email, sinon Identity refuse.
            UserName = dto.Email,
            Email = dto.Email,
            EmailConfirmed = true,
            EntrepriseId = entrepriseId.Value,
            AgentId = dto.AgentId,
            ClientId = dto.ClientId,
            Nom = dto.Nom,
            Prenom = dto.Prenom,
            DateCreation = DateTime.UtcNow,
            Actif = true
        };

        var creation = await _utilisateurs.CreateAsync(utilisateur, dto.MotDePasse);
        if (!creation.Succeeded)
        {
            return BadRequest(string.Join(" ; ", creation.Errors.Select(e => e.Description)));
        }

        var affectation = await _utilisateurs.AddToRoleAsync(utilisateur, dto.Role);
        if (!affectation.Succeeded)
        {
            return BadRequest(string.Join(" ; ", affectation.Errors.Select(e => e.Description)));
        }

        return Ok(Vers(utilisateur, [dto.Role]));
    }

    [Authorize]
    [HttpGet("moi")]
    public async Task<ActionResult<ProfilDto>> Moi()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(id, out var utilisateurId))
        {
            return Unauthorized("Jeton sans identifiant exploitable.");
        }

        var utilisateur = await _utilisateurs.FindByIdAsync(utilisateurId.ToString());
        if (utilisateur is null)
        {
            return NotFound();
        }

        var roles = await _utilisateurs.GetRolesAsync(utilisateur);
        return Ok(Vers(utilisateur, roles));
    }

    private Guid? EntrepriseIdDuJeton()
    {
        var valeur = User.FindFirstValue(JwtService.RevendicationEntrepriseId);
        return Guid.TryParse(valeur, out var id) ? id : null;
    }

    private static ProfilDto Vers(Utilisateur u, IEnumerable<string> roles) => new()
    {
        Id = u.Id,
        Email = u.Email ?? string.Empty,
        Nom = u.Nom,
        Prenom = u.Prenom,
        EntrepriseId = u.EntrepriseId,
        AgentId = u.AgentId,
        ClientId = u.ClientId,
        Actif = u.Actif,
        Roles = roles
    };
}
