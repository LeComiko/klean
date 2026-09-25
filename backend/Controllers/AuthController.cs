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
    private readonly JwtService _jwt;

    public AuthController(
        KleanDbContext db,
        UserManager<Utilisateur> utilisateurs,
        SignInManager<Utilisateur> connexions,
        JwtService jwt)
    {
        _db = db;
        _utilisateurs = utilisateurs;
        _connexions = connexions;
        _jwt = jwt;
    }

    [AllowAnonymous]
    [HttpPost("connexion")]
    public async Task<ActionResult<JetonDto>> Connexion(ConnexionDto dto)
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

        var roles = await _utilisateurs.GetRolesAsync(utilisateur);

        return Ok(new JetonDto
        {
            Jeton = _jwt.Generer(utilisateur, roles),
            Expiration = DateTime.UtcNow.Add(JwtService.Duree),
            Profil = Vers(utilisateur, roles)
        });
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
