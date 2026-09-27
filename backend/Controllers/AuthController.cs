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
    private readonly IServiceCourriel _courriel;
    private readonly LimiteurDebit _debit;
    private readonly IConfiguration _configuration;

    public AuthController(
        KleanDbContext db,
        UserManager<Utilisateur> utilisateurs,
        SignInManager<Utilisateur> connexions,
        ServiceSession session,
        IServiceCourriel courriel,
        LimiteurDebit debit,
        IConfiguration configuration)
    {
        _db = db;
        _utilisateurs = utilisateurs;
        _connexions = connexions;
        _session = session;
        _courriel = courriel;
        _debit = debit;
        _configuration = configuration;
    }

    /// <summary>
    /// Connexion. Pose les trois cookies de session et ne renvoie <b>aucun</b>
    /// jeton dans le corps.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("connexion")]
    public async Task<ActionResult<SessionDto>> Connexion(ConnexionDto dto)
    {
        // Avant toute lecture en base : une requête refusée ne doit rien coûter.
        var limite = LimiteAtteinte("connexion", dto.Email,
            PlafondsDebit.ConnexionsParAdresse, PlafondsDebit.FenetreConnexionAdresse,
            PlafondsDebit.ConnexionsParCompte, PlafondsDebit.FenetreConnexionCompte);

        if (limite is not null) return limite;

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

    /// <summary>
    /// Demande de réinitialisation. Envoie un lien à usage unique, valable une
    /// heure.
    ///
    /// Répond <b>202 quoi qu'il arrive</b> dès lors que SMTP est configuré : que le
    /// compte existe, soit désactivé, ou n'existe pas. Distinguer les cas ferait de
    /// cet endpoint un vérificateur d'adresses ouvert à tous — on y saisit une
    /// adresse, la réponse dit si elle a un compte.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("mot-de-passe-oublie")]
    public async Task<IActionResult> MotDePasseOublie(MotDePasseOublieDto dto)
    {
        var limite = LimiteAtteinte("oubli", dto.Email,
            PlafondsDebit.OublisParAdresse, PlafondsDebit.FenetreOublis,
            PlafondsDebit.OublisParCompte, PlafondsDebit.FenetreOublis);

        if (limite is not null) return limite;

        // Vérifié avant toute recherche de compte : le 503 ne dépend donc pas de
        // l'adresse saisie, et ne révèle rien.
        if (!_courriel.Configure)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                "La réinitialisation de mot de passe est indisponible : aucun serveur SMTP "
                + "n'est configuré sur cette instance. Un administrateur doit renseigner "
                + "Courriel:Hote, Courriel:Port et Courriel:Expediteur. En attendant, seul "
                + "un administrateur peut débloquer un compte.");
        }

        var utilisateur = await _utilisateurs.FindByEmailAsync(dto.Email);

        if (utilisateur is not null && utilisateur.Actif)
        {
            var jeton = await _utilisateurs.GeneratePasswordResetTokenAsync(utilisateur);

            await _courriel.EnvoyerAsync(
                dto.Email,
                "KLEAN — réinitialisation de votre mot de passe",
                CorpsDuCourriel(dto.Email, jeton));
        }

        return Accepted();
    }

    /// <summary>
    /// Réinitialisation effective.
    ///
    /// Deux effets au-delà du changement de mot de passe, tous deux nécessaires :
    /// Identity régénère le <c>SecurityStamp</c>, ce qui invalide immédiatement tous
    /// les jetons d'accès en circulation ; et toutes les sessions de rafraîchissement
    /// sont révoquées. Quelqu'un qui réinitialise son mot de passe le fait souvent
    /// parce qu'il craint un accès illégitime — laisser les sessions ouvertes serait
    /// répondre à côté.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("reinitialiser")]
    public async Task<IActionResult> Reinitialiser(ReinitialiserDto dto)
    {
        var limite = LimiteAtteinte("reinitialisation", null,
            PlafondsDebit.ReinitialisationsParAdresse, PlafondsDebit.FenetreReinitialisations);

        if (limite is not null) return limite;

        var utilisateur = await _utilisateurs.FindByEmailAsync(dto.Email);

        if (utilisateur is null)
        {
            return BadRequest(LienInvalide);
        }

        var resultat = await _utilisateurs.ResetPasswordAsync(
            utilisateur, dto.Jeton, dto.NouveauMotDePasse);

        if (!resultat.Succeeded)
        {
            // Un jeton refusé donne le message générique — sinon la réponse dirait
            // « ce compte existe, mais votre jeton est faux ». Les règles de mot de
            // passe, elles, doivent être dites : l'utilisateur ne peut pas corriger
            // ce qu'il ne connaît pas.
            if (resultat.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
            {
                return BadRequest(LienInvalide);
            }

            return BadRequest(string.Join(" ; ", resultat.Errors.Select(e => e.Description)));
        }

        await _session.RevoquerToutAsync(utilisateur.Id, "Réinitialisation du mot de passe");

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

    private const string LienInvalide =
        "Ce lien de réinitialisation est invalide ou a expiré. En demander un nouveau.";

    /// <summary>
    /// Applique les deux dimensions de limitation. Le message est le même dans les
    /// deux cas : préciser laquelle a déclenché renseignerait un attaquant sur la
    /// forme de la défense.
    /// </summary>
    private ActionResult? LimiteAtteinte(
        string operation, string? compte,
        int parAdresse, TimeSpan fenetreAdresse,
        int? parCompte = null, TimeSpan? fenetreCompte = null)
    {
        var adresse = HttpContext.Connection.RemoteIpAddress?.ToString()
                      ?? LimiteurDebit.AdresseInconnue;

        if (!_debit.Autoriser($"{operation}:adresse:{adresse}", parAdresse, fenetreAdresse))
        {
            return TropDeTentatives();
        }

        if (parCompte is not null && !string.IsNullOrWhiteSpace(compte)
            && !_debit.Autoriser(
                $"{operation}:compte:{compte.ToLowerInvariant()}", parCompte.Value, fenetreCompte!.Value))
        {
            return TropDeTentatives();
        }

        return null;
    }

    private ActionResult TropDeTentatives() =>
        StatusCode(StatusCodes.Status429TooManyRequests,
            "Trop de tentatives. Réessayer dans quelques minutes.");

    private string CorpsDuCourriel(string email, string jeton)
    {
        // Le jeton d'Identity contient des caractères réservés dans une URL.
        var lien = _configuration["Frontend:Url"]?.TrimEnd('/') ?? "http://localhost:5173";

        var url = $"{lien}/reinitialiser"
                  + $"?email={Uri.EscapeDataString(email)}"
                  + $"&jeton={Uri.EscapeDataString(jeton)}";

        return $"""
            Bonjour,

            Une réinitialisation de mot de passe a été demandée pour ce compte KLEAN.

            Pour choisir un nouveau mot de passe, suivre ce lien :

            {url}

            Ce lien est valable une heure et ne fonctionne qu'une fois.

            Si vous n'êtes pas à l'origine de cette demande, ignorez ce message :
            votre mot de passe actuel reste valable.
            """;
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
