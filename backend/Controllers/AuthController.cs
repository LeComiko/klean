using System.Security.Claims;
using System.Security.Cryptography;
using Klean.Api.DTOs;
using Klean.Api.Data;
using Klean.Api.Filtres;
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
    private readonly ServiceAnonymisation _anonymisation;
    private readonly ServiceSecondFacteur _secondFacteur;

    public AuthController(
        KleanDbContext db,
        UserManager<Utilisateur> utilisateurs,
        SignInManager<Utilisateur> connexions,
        ServiceSession session,
        IServiceCourriel courriel,
        LimiteurDebit debit,
        IConfiguration configuration,
        ServiceAnonymisation anonymisation,
        ServiceSecondFacteur secondFacteur)
    {
        _anonymisation = anonymisation;
        _secondFacteur = secondFacteur;
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
    ///
    /// <b>200</b> avec la session si le mot de passe suffit ; <b>202</b> avec l'étape
    /// suivante — <c>SecondFacteur</c> ou <c>Equipement</c> — si le compte a, ou doit
    /// avoir, un second facteur (<see cref="SecondFacteurController"/>).
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

        return await SuiteDeLaConnexionAsync(utilisateur);
    }

    /// <summary>
    /// Premier temps de la première connexion : le mot de passe provisoire est remplacé
    /// par celui que la personne choisit (SPEC.md, étape 24). Le défi de la connexion
    /// tient lieu de preuve, le provisoire venant d'y être vérifié.
    ///
    /// La suite est celle d'une connexion ordinaire : le second facteur, ou son
    /// équipement, puis la session. ⚠️ Le changement régénère l'empreinte de sécurité,
    /// donc périme le défi qui a servi : l'étape suivante en reçoit un neuf.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("connexion/mot-de-passe")]
    public async Task<ActionResult<SessionDto>> PremierMotDePasse(NouveauMotDePasseDto dto)
    {
        var limite = LimiteAtteinte("premier-mot-de-passe", null,
            PlafondsDebit.ConnexionsParAdresse, PlafondsDebit.FenetreConnexionAdresse);
        if (limite is not null) return limite;

        var utilisateur = await _secondFacteur.CompteDuDefiAsync(HttpContext, EtapeDefi.ChangementMotDePasse);

        if (utilisateur is null)
        {
            ServiceSecondFacteur.EffacerDefi(HttpContext);
            return Unauthorized(SecondFacteurController.DefiPerime);
        }

        if (await _utilisateurs.CheckPasswordAsync(utilisateur, dto.NouveauMotDePasse))
        {
            return BadRequest("Choisir un mot de passe différent du mot de passe provisoire.");
        }

        // ⚠️ Posé AVANT la réinitialisation, qui enregistre le compte entier : le drapeau
        // et le mot de passe changent dans la même écriture. Dans l'autre ordre, une
        // seconde écriture qui échouerait laisserait un mot de passe choisi marqué
        // provisoire.
        utilisateur.MotDePasseProvisoire = false;

        var jeton = await _utilisateurs.GeneratePasswordResetTokenAsync(utilisateur);
        var resultat = await _utilisateurs.ResetPasswordAsync(utilisateur, jeton, dto.NouveauMotDePasse);

        if (!resultat.Succeeded)
        {
            return BadRequest(string.Join(" ; ", resultat.Errors.Select(e => e.Description)));
        }

        var suite = await SuiteDeLaConnexionAsync(utilisateur);

        // Une session ouverte : le défi n'a plus d'usage. Une étape suivante l'a déjà
        // remplacé par le sien.
        if (suite.Result is not AcceptedResult) ServiceSecondFacteur.EffacerDefi(HttpContext);

        return suite;
    }

    /// <summary>
    /// Ce qui suit un bon mot de passe. Le mot de passe ne suffit pas toujours, et aucune
    /// session n'est alors ouverte : un cookie de défi de cinq minutes mène à l'étape
    /// suivante.
    ///
    /// ⚠️ <b>L'ordre est imposé</b> : le mot de passe provisoire d'abord (étape 24), puis
    /// le second facteur ou son équipement (étape 23). Choisir le mot de passe régénère
    /// l'empreinte de sécurité ; un téléphone équipé avant serait lié à un défi aussitôt
    /// périmé.
    /// </summary>
    private async Task<ActionResult<SessionDto>> SuiteDeLaConnexionAsync(Utilisateur utilisateur)
    {
        if (utilisateur.MotDePasseProvisoire)
        {
            _secondFacteur.PoserDefi(HttpContext, utilisateur, EtapeDefi.ChangementMotDePasse);
            return Accepted(new EtapeConnexionDto { Etape = nameof(EtapeDefi.ChangementMotDePasse) });
        }

        if (utilisateur.TwoFactorEnabled)
        {
            _secondFacteur.PoserDefi(HttpContext, utilisateur, EtapeDefi.SecondFacteur);
            return Accepted(new EtapeConnexionDto { Etape = nameof(EtapeDefi.SecondFacteur) });
        }

        if (await _secondFacteur.EstObligeAsync(utilisateur))
        {
            _secondFacteur.PoserDefi(HttpContext, utilisateur, EtapeDefi.Equipement);
            return Accepted(new EtapeConnexionDto { Etape = nameof(EtapeDefi.Equipement) });
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
    /// Déconnexion de tous les appareils.
    ///
    /// Contrairement à <c>deconnexion</c>, celle-ci exige un jeton d'accès valide :
    /// elle régénère le <c>SecurityStamp</c>, donc elle périme les sessions de tout
    /// le compte, et un tel effet ne peut pas reposer sur le seul cookie de
    /// rafraîchissement — qui est précisément ce qu'un attaquant aurait volé.
    ///
    /// Le geste que cet endpoint rend possible : « je crois que quelqu'un d'autre
    /// est connecté à mon compte ». Sans lui, la seule réponse était d'attendre
    /// quinze minutes en espérant que l'autre ne rafraîchisse pas, ou de passer par
    /// une réinitialisation de mot de passe — qui exige un serveur SMTP, optionnel
    /// sur cette instance (CLAUDE.md §2).
    /// </summary>
    [Authorize]
    [HttpPost("deconnexion-totale")]
    public async Task<IActionResult> DeconnexionTotale()
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

        await _session.FermerToutAsync(HttpContext, utilisateur);

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

        // Un mot de passe choisi par la personne n'est plus provisoire. Posé avant :
        // la réinitialisation enregistre le compte entier, dans la même écriture.
        utilisateur.MotDePasseProvisoire = false;

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

    /// <summary>
    /// Changement de mot de passe par son propriétaire, depuis l'écran de compte.
    ///
    /// C'est le seul parcours de mot de passe qui ne dépend d'aucun serveur SMTP :
    /// la preuve d'identité est le mot de passe actuel, pas un lien reçu par
    /// courriel. Sur une instance sans courriel configuré — cas prévu par
    /// <c>CLAUDE.md</c> §2 — c'était jusqu'ici un geste impossible.
    ///
    /// <para>
    /// <b>L'appareil qui change le mot de passe reste connecté, les autres non.</b>
    /// Identity régénère le <c>SecurityStamp</c>, ce qui périme immédiatement tous
    /// les jetons d'accès du compte ; les sessions de rafraîchissement sont révoquées
    /// en plus. Puis une session neuve est ouverte pour l'appelant, parce qu'il vient
    /// de prouver son mot de passe actuel : c'est une ré-authentification, pas une
    /// prolongation silencieuse. Le renvoyer à l'écran de connexion pour retaper le
    /// mot de passe qu'il vient de choisir n'apporterait rien — qui tient cette
    /// session la tient déjà.
    /// </para>
    ///
    /// ⚠️ <b>L'ordre des deux gestes est impératif.</b> Révoquer puis rouvrir. Dans
    /// l'autre sens, le jeton de rafraîchissement tout juste émis est révoqué par le
    /// geste suivant, et l'utilisateur se retrouve déconnecté au premier
    /// renouvellement — quinze minutes plus tard, donc très loin de sa cause.
    ///
    /// ⚠️ <b>Un mot de passe actuel faux rend 400, jamais 401</b>, et ce n'est pas
    /// une préférence. Le client du frontend traite tout 401 comme une session morte :
    /// il tente un rafraîchissement, rejoue, puis purge la session. Répondre 401 sur
    /// une faute de frappe déconnecterait donc l'utilisateur au lieu de lui dire que
    /// son mot de passe est faux. Identity classe le cas en
    /// <c>PasswordMismatch</c>, qui passe par le chemin du 400 ci-dessous.
    /// </summary>
    [Authorize]
    [HttpPost("changer-mot-de-passe")]
    public async Task<ActionResult<SessionDto>> ChangerMotDePasse(ChangementMotDePasseDto dto)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(id, out var utilisateurId))
        {
            return Unauthorized("Jeton sans identifiant exploitable.");
        }

        // Clé de limitation prise sur le jeton, donc avant toute lecture en base :
        // une tentative refusée ne doit rien coûter.
        var limite = LimiteAtteinte("changement", utilisateurId.ToString(),
            PlafondsDebit.ChangementsParAdresse, PlafondsDebit.FenetreChangements,
            PlafondsDebit.ChangementsParCompte, PlafondsDebit.FenetreChangements);

        if (limite is not null) return limite;

        var utilisateur = await _utilisateurs.FindByIdAsync(utilisateurId.ToString());
        if (utilisateur is null)
        {
            return NotFound();
        }

        var resultat = await _utilisateurs.ChangePasswordAsync(
            utilisateur, dto.MotDePasseActuel, dto.NouveauMotDePasse);

        if (!resultat.Succeeded)
        {
            // Les descriptions d'Identity sont en français depuis l'étape 12 et
            // traversent l'API telles quelles : rien à traduire ici, et rien à
            // reformuler — l'écran n'a aucun moyen de retraduire un texte libre.
            return BadRequest(string.Join(" ; ", resultat.Errors.Select(e => e.Description)));
        }

        await _session.RevoquerToutAsync(utilisateur.Id, "Changement de mot de passe");

        // ⚠️ `utilisateur` est l'instance que ChangePasswordAsync vient de muter :
        // c'est d'elle que le nouveau jeton tire le SecurityStamp. Relire le compte
        // avant le changement, ou passer une copie plus ancienne, produirait un
        // cookie porteur de l'empreinte périmée — donc refusé par OnTokenValidated
        // à la requête suivante.
        return Ok(await _session.OuvrirAsync(HttpContext, utilisateur));
    }

    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpPost("comptes")]
    public async Task<ActionResult<CompteCreeDto>> CreerCompte(CompteCreateDto dto)
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
            Actif = true,
            MotDePasseProvisoire = true
        };

        // Tiré ici, jamais choisi par l'administrateur (SPEC.md, étape 24) : ce qu'il
        // transmet ne sert qu'à la première connexion, qui impose d'en changer.
        var motDePasse = TirerMotDePasseProvisoire();
        var creation = await _utilisateurs.CreateAsync(utilisateur, motDePasse);
        if (!creation.Succeeded)
        {
            return BadRequest(string.Join(" ; ", creation.Errors.Select(e => e.Description)));
        }

        var affectation = await _utilisateurs.AddToRoleAsync(utilisateur, dto.Role);
        if (!affectation.Succeeded)
        {
            return BadRequest(string.Join(" ; ", affectation.Errors.Select(e => e.Description)));
        }

        return Ok(new CompteCreeDto
        {
            Compte = VersCompte(utilisateur, dto.Role),
            MotDePasseProvisoire = motDePasse
        });
    }

    /// <summary>
    /// Les comptes de l'entreprise — <c>GET /api/auth/comptes</c>, administrateur seul
    /// (SPEC.md, étape 24). Les pseudonymisés en fin de liste, marqués de leur date.
    ///
    /// ⚠️ <b>Le cadrage sur l'entreprise est écrit à la main</b> : <c>AspNetUsers</c>
    /// n'est pas filtrée (CLAUDE.md §7). Sans cette ligne, la liste rendrait les comptes
    /// de toute l'instance.
    ///
    /// Non paginée, comme les candidats et les chefs d'équipe : sa taille est bornée par
    /// l'effectif et les clients syndics, pas par la requête. Journalisée : elle nomme
    /// des collègues.
    /// </summary>
    [JournaliserAcces(RessourceAccedee.Compte, NatureAcces.Liste)]
    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpGet("comptes")]
    public async Task<ActionResult<List<CompteDto>>> Comptes()
    {
        var entrepriseId = EntrepriseIdDuJeton();
        if (entrepriseId is null)
        {
            return Unauthorized("Jeton sans revendication entrepriseId.");
        }

        return Ok(await _db.Users
            .AsNoTracking()
            .Where(u => u.EntrepriseId == entrepriseId.Value)
            .OrderBy(u => u.DateAnonymisation != null)
            .ThenBy(u => u.Nom).ThenBy(u => u.Prenom).ThenBy(u => u.Email)
            .Select(u => new CompteDto
            {
                Id = u.Id,
                Email = u.Email ?? string.Empty,
                Nom = u.Nom,
                Prenom = u.Prenom,
                Role = _db.UserRoles
                    .Where(ur => ur.UserId == u.Id)
                    .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name)
                    .FirstOrDefault() ?? string.Empty,
                Actif = u.Actif,
                SecondFacteurActif = u.TwoFactorEnabled,
                MotDePasseProvisoire = u.MotDePasseProvisoire,
                AgentId = u.AgentId,
                ClientId = u.ClientId,
                DateCreation = u.DateCreation,
                DateAnonymisation = u.DateAnonymisation
            })
            .ToListAsync());
    }

    /// <summary>
    /// Ferme un compte de l'entreprise : la personne perd son accès le jour même, sans
    /// rien effacer (SPEC.md, étape 24). Réversible, contrairement à la pseudonymisation.
    ///
    /// ⚠️ <b>Les deux gestes sont nécessaires</b> : l'empreinte de sécurité régénérée
    /// périme le jeton d'accès en circulation, les sessions révoquées le rafraîchissement
    /// (CLAUDE.md §10). <c>Actif = false</c> seul laisserait quinze minutes.
    ///
    /// ⚠️ <b>Jamais sur soi-même</b>, et c'est ce refus qui garde « une entreprise conserve
    /// un administrateur actif » : l'appelant en est un, donc si la cible en est un autre,
    /// ils sont deux. Une garde « dernier administrateur » écrite ici serait inatteignable
    /// — la leçon de la pseudonymisation des comptes (CLAUDE.md §7 bis).
    ///
    /// Rend le compte, et non 204 : le journal ne consigne qu'une réponse porteuse d'un
    /// corps, et ce geste sur le compte d'autrui doit y laisser sa ligne.
    /// </summary>
    [JournaliserAcces(RessourceAccedee.Compte, NatureAcces.Desactivation)]
    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpPost("comptes/{id:guid}/desactiver")]
    public async Task<ActionResult<CompteDto>> DesactiverCompte(Guid id)
    {
        var (compte, refus) = await CompteDeLEntrepriseAsync(id);
        if (compte is null) return refus!;

        if (compte.Id.ToString() == User.FindFirstValue(ClaimTypes.NameIdentifier))
        {
            return Conflict("Un administrateur ne ferme pas son propre compte : un autre administrateur doit le faire.");
        }

        if (compte.DateAnonymisation is { } date)
        {
            return Conflict($"Ce compte a été pseudonymisé le {date:dd/MM/yyyy} : il est déjà fermé, définitivement.");
        }

        if (!compte.Actif)
        {
            return Conflict("Ce compte est déjà désactivé.");
        }

        compte.Actif = false;

        // Enregistre le compte entier : Actif part dans la même écriture.
        var ecriture = await _utilisateurs.UpdateSecurityStampAsync(compte);
        if (!ecriture.Succeeded)
        {
            return Conflict("Le compte a été modifié en même temps. Recommencer.");
        }

        await _session.RevoquerToutAsync(compte.Id, "Compte désactivé");

        return Ok(VersCompte(compte, await RoleDeAsync(compte)));
    }

    /// <summary>
    /// Rouvre un compte fermé. Le mot de passe et le second facteur sont ceux d'avant :
    /// la personne se reconnecte comme elle le faisait. Un compte pseudonymisé ne se
    /// rouvre pas — son identité n'existe plus.
    /// </summary>
    [JournaliserAcces(RessourceAccedee.Compte, NatureAcces.Reactivation)]
    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpPost("comptes/{id:guid}/reactiver")]
    public async Task<ActionResult<CompteDto>> ReactiverCompte(Guid id)
    {
        var (compte, refus) = await CompteDeLEntrepriseAsync(id);
        if (compte is null) return refus!;

        if (compte.DateAnonymisation is { } date)
        {
            return Conflict($"Ce compte a été pseudonymisé le {date:dd/MM/yyyy} : il ne se rouvre pas. Créer un compte neuf.");
        }

        if (compte.Actif)
        {
            return Conflict("Ce compte est déjà actif.");
        }

        compte.Actif = true;

        var ecriture = await _utilisateurs.UpdateAsync(compte);
        if (!ecriture.Succeeded)
        {
            return Conflict("Le compte a été modifié en même temps. Recommencer.");
        }

        return Ok(VersCompte(compte, await RoleDeAsync(compte)));
    }

    /// <summary>
    /// Pseudonymisation d'un compte de connexion <b>sans fiche agent</b> —
    /// <c>POST /api/auth/comptes/{id}/anonymiser</c> (RGPD.md §9.3).
    ///
    /// Comble le dernier trou d'effacement du produit. Un compte `Responsable`,
    /// `Administrateur` ou `ClientSyndic` porte `AgentId = null` : la pseudonymisation
    /// d'agent ne le touchait donc pas, et aucune route ne permettait d'effacer ses
    /// données — ni même de le désactiver, ce que la fixture de tests faisait
    /// directement en base, faute d'endpoint.
    ///
    /// ⚠️ <b>Cinq refus en 409, et chacun nomme sa sortie.</b> Déjà anonymisé, rattaché à
    /// un agent — auquel cas c'est la route de l'agent qu'il faut, parce qu'elle seule
    /// traite les trois endroits où la personne est nommée —, son propre compte, et le
    /// dernier administrateur actif de l'entreprise. Une friction n'a de valeur que si
    /// elle est franchissable.
    ///
    /// Journalisé en <see cref="RessourceAccedee.Compte"/> /
    /// <see cref="NatureAcces.Anonymisation"/> : aucune <i>lecture</i> de compte n'est
    /// journalisée — aucune route n'en liste — mais une écriture irréversible sur les
    /// données d'une personne doit laisser une trace.
    /// </summary>
    [JournaliserAcces(RessourceAccedee.Compte, NatureAcces.Anonymisation)]
    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpPost("comptes/{id:guid}/anonymiser")]
    public async Task<ActionResult<AnonymisationCompteDto>> AnonymiserCompte(Guid id)
    {
        var entrepriseId = EntrepriseIdDuJeton();
        if (entrepriseId is null)
        {
            return Unauthorized("Jeton sans revendication entrepriseId.");
        }

        var appelant = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(appelant, out var appelantId))
        {
            return Unauthorized("Jeton sans identifiant exploitable.");
        }

        var resultat = await _anonymisation.ExecuterSurCompteAsync(id, entrepriseId.Value, appelantId);

        if (resultat is null) return NotFound();

        return resultat.Value.Conflit is { } conflit
            ? Conflict(conflit)
            : Ok(resultat.Value.Valeur);
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
        var adresse = LimiteurDebit.CleDAdresse(HttpContext.Connection.RemoteIpAddress);

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
        //
        // Le repli vise https : depuis l'étape 16 le serveur de développement sert
        // la page en https, et un lien en clair y échouerait.
        var lien = _configuration["Frontend:Url"]?.TrimEnd('/') ?? "https://localhost:5173";

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

    /// <summary>
    /// Le compte visé, s'il appartient à l'entreprise de l'appelant ; sinon le refus.
    /// Un compte d'une autre entreprise rend 404, comme un compte inexistant :
    /// <c>AspNetUsers</c> n'étant pas filtrée, le cadrage est ici (CLAUDE.md §7).
    /// </summary>
    private async Task<(Utilisateur? Compte, ActionResult? Refus)> CompteDeLEntrepriseAsync(Guid id)
    {
        var entrepriseId = EntrepriseIdDuJeton();
        if (entrepriseId is null)
        {
            return (null, Unauthorized("Jeton sans revendication entrepriseId."));
        }

        var compte = await _utilisateurs.FindByIdAsync(id.ToString());

        return compte is null || compte.EntrepriseId != entrepriseId.Value
            ? (null, NotFound())
            : (compte, null);
    }

    private async Task<string> RoleDeAsync(Utilisateur compte) =>
        (await _utilisateurs.GetRolesAsync(compte)).FirstOrDefault() ?? string.Empty;

    private static CompteDto VersCompte(Utilisateur u, string role) => new()
    {
        Id = u.Id,
        Email = u.Email ?? string.Empty,
        Nom = u.Nom,
        Prenom = u.Prenom,
        Role = role,
        Actif = u.Actif,
        SecondFacteurActif = u.TwoFactorEnabled,
        MotDePasseProvisoire = u.MotDePasseProvisoire,
        AgentId = u.AgentId,
        ClientId = u.ClientId,
        DateCreation = u.DateCreation,
        DateAnonymisation = u.DateAnonymisation
    };

    // Sans I ni O, sans l, sans 0 ni 1 : le mot de passe se lit à voix haute ou se
    // recopie depuis un papier, et ces caractères s'y confondent.
    private const string Majuscules = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Minuscules = "abcdefghijkmnopqrstuvwxyz";
    private const string Chiffres = "23456789";

    /// <summary>
    /// Seize caractères en quatre groupes — « Hk7m-Q2xa-… » —, soit environ 93 bits.
    /// Tirés jusqu'à porter une majuscule, une minuscule et un chiffre : la règle
    /// d'Identity (CLAUDE.md §6), qui refuserait sinon la création.
    /// </summary>
    private static string TirerMotDePasseProvisoire()
    {
        const string alphabet = Majuscules + Minuscules + Chiffres;

        while (true)
        {
            var tire = RandomNumberGenerator.GetString(alphabet, 16);

            if (tire.Any(Majuscules.Contains) && tire.Any(Minuscules.Contains) && tire.Any(Chiffres.Contains))
            {
                return string.Join('-', tire.Chunk(4).Select(groupe => new string(groupe)));
            }
        }
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
