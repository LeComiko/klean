using System.Security.Claims;
using Klean.Api.DTOs;
using Klean.Api.Filtres;
using Klean.Api.Models;
using Klean.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Klean.Api.Controllers;

/// <summary>
/// La double authentification (SPEC.md, étape 23) : le second temps de la connexion,
/// l'équipement d'un téléphone, les codes de récupération, et la réinitialisation par un
/// administrateur.
///
/// Sous <c>/api/auth</c>, comme la connexion : le cookie de défi n'est envoyé qu'à ce
/// chemin.
///
/// ⚠️ <b>401 pour l'état de la session ou du défi, 400 pour un code faux</b>
/// (CLAUDE.md §10). Un défi échu se règle en recommençant la connexion ; un code faux, en
/// le retapant. <c>connexion/code</c> rejoint donc <c>SansRafraichissement</c> côté écran.
/// </summary>
[ApiController]
[Route("api/auth")]
public class SecondFacteurController : ControllerBase
{
    private const string DefiPerime =
        "La connexion a expiré ou n'est plus valable. Saisir à nouveau l'adresse et le mot de passe.";

    private const string CompteVerrouille =
        "Compte verrouillé après cinq échecs. Réessayer dans quinze minutes.";

    private readonly UserManager<Utilisateur> _utilisateurs;
    private readonly ServiceSession _session;
    private readonly ServiceSecondFacteur _secondFacteur;
    private readonly LimiteurDebit _debit;

    public SecondFacteurController(
        UserManager<Utilisateur> utilisateurs,
        ServiceSession session,
        ServiceSecondFacteur secondFacteur,
        LimiteurDebit debit)
    {
        _utilisateurs = utilisateurs;
        _session = session;
        _secondFacteur = secondFacteur;
        _debit = debit;
    }

    /// <summary>
    /// Second temps de la connexion : un code de l'application, ou un code de
    /// récupération, ouvre la session.
    ///
    /// ⚠️ <b>Un échec est compté par le verrouillage d'Identity, avec ceux du mot de
    /// passe</b> : cinq en tout, puis quinze minutes. Sans ce partage, le million de codes
    /// à six chiffres s'essaierait à côté du verrou, au seul rythme de la limitation de
    /// débit. Le compteur ne repart à zéro qu'au succès complet : Identity ne le remet pas
    /// à zéro sur un bon mot de passe quand le second facteur est actif.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("connexion/code")]
    public async Task<ActionResult<SessionDto>> Code(CodeSecondFacteurDto dto)
    {
        var parAdresse = LimiteParAdresse();
        if (parAdresse is not null) return parAdresse;

        var utilisateur = await _secondFacteur.CompteDuDefiAsync(HttpContext, EtapeDefi.SecondFacteur);

        if (utilisateur is null)
        {
            ServiceSecondFacteur.EffacerDefi(HttpContext);
            return Unauthorized(DefiPerime);
        }

        var parCompte = LimiteParCompte(utilisateur);
        if (parCompte is not null) return parCompte;

        if (RefusDeForme(dto) is { } refus) return BadRequest(refus);

        if (await _utilisateurs.IsLockedOutAsync(utilisateur))
        {
            ServiceSecondFacteur.EffacerDefi(HttpContext);
            return Unauthorized(CompteVerrouille);
        }

        var reconnu = dto.Code is not null
            ? await _secondFacteur.VerifierCodeAsync(utilisateur, dto.Code)
            : await _secondFacteur.ConsommerCodeRecuperationAsync(utilisateur, dto.CodeRecuperation);

        if (!reconnu)
        {
            await _utilisateurs.AccessFailedAsync(utilisateur);

            if (await _utilisateurs.IsLockedOutAsync(utilisateur))
            {
                ServiceSecondFacteur.EffacerDefi(HttpContext);
                return Unauthorized(CompteVerrouille);
            }

            return BadRequest(dto.Code is not null
                ? "Code invalide. Saisir le code affiché maintenant par l'application ; "
                  + "un code déjà utilisé ne sert pas deux fois."
                : "Code de récupération invalide ou déjà utilisé.");
        }

        await _utilisateurs.ResetAccessFailedCountAsync(utilisateur);
        ServiceSecondFacteur.EffacerDefi(HttpContext);

        return Ok(await _session.OuvrirAsync(HttpContext, utilisateur));
    }

    /// <summary>
    /// Tire une clé et rend de quoi équiper le téléphone : la clé en clair, l'URI et le QR
    /// code. Le second facteur n'est pas encore actif.
    ///
    /// Ouvert à deux appelants : un compte connecté, depuis l'écran du compte ; et un
    /// compte que son rôle oblige et qui n'est pas équipé, muni du défi de l'étape
    /// <c>Equipement</c> — il n'a pas de session, et n'en aura pas avant d'avoir activé.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("second-facteur/preparer")]
    public async Task<ActionResult<PreparationSecondFacteurDto>> Preparer()
    {
        var (utilisateur, refus) = await AppelantAEquiperAsync();
        if (refus is not null) return refus;

        var preparation = await _secondFacteur.PreparerAsync(utilisateur!);

        return preparation is null
            ? Conflict("Le compte a été modifié en même temps. Recommencer.")
            : Ok(preparation);
    }

    /// <summary>
    /// Active le second facteur sur un code de la clé préparée, et rend les dix codes de
    /// récupération — une seule fois.
    ///
    /// L'activation régénère l'empreinte de sécurité : toute session ouverte sans second
    /// facteur, sur un autre appareil, meurt. Celle de l'appelant est rouverte, comme au
    /// changement de mot de passe, et dans le même ordre : révoquer, puis rouvrir
    /// (CLAUDE.md §6).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("second-facteur/activer")]
    public async Task<ActionResult<CodesRecuperationDto>> Activer(CodeSecondFacteurDto dto)
    {
        var parAdresse = LimiteParAdresse();
        if (parAdresse is not null) return parAdresse;

        var (utilisateur, refus) = await AppelantAEquiperAsync();
        if (refus is not null) return refus;

        var parCompte = LimiteParCompte(utilisateur!);
        if (parCompte is not null) return parCompte;

        var codes = await _secondFacteur.ActiverAsync(utilisateur!, dto.Code);

        if (codes is null)
        {
            return BadRequest(
                "Code invalide. Vérifier que l'heure du téléphone est automatique, ou "
                + "scanner à nouveau le QR code.");
        }

        await _session.RevoquerToutAsync(utilisateur!.Id, "Activation du second facteur");
        ServiceSecondFacteur.EffacerDefi(HttpContext);

        return Ok(new CodesRecuperationDto
        {
            Codes = codes,
            Session = await _session.OuvrirAsync(HttpContext, utilisateur)
        });
    }

    /// <summary>L'état du second facteur de l'appelant, pour l'écran du compte.</summary>
    [Authorize]
    [HttpGet("second-facteur")]
    public async Task<ActionResult<EtatSecondFacteurDto>> Etat()
    {
        var utilisateur = await AppelantConnecteAsync();
        if (utilisateur is null) return Unauthorized("Jeton sans identifiant exploitable.");

        return Ok(new EtatSecondFacteurDto
        {
            Actif = utilisateur.TwoFactorEnabled,
            Obligatoire = await _secondFacteur.EstObligeAsync(utilisateur),
            CodesRestants = await _secondFacteur.CodesRestantsAsync(utilisateur)
        });
    }

    /// <summary>
    /// Dix codes de récupération neufs, qui annulent les précédents. Exige un code de
    /// l'application : une session volée ne doit pas suffire à s'en faire remettre.
    /// </summary>
    [Authorize]
    [HttpPost("second-facteur/codes")]
    public async Task<ActionResult<CodesRecuperationDto>> RegenererCodes(CodeSecondFacteurDto dto)
    {
        var utilisateur = await AppelantConnecteAsync();
        if (utilisateur is null) return Unauthorized("Jeton sans identifiant exploitable.");

        var limite = LimiteParAdresse() ?? LimiteParCompte(utilisateur);
        if (limite is not null) return limite;

        if (!utilisateur.TwoFactorEnabled)
        {
            return Conflict("La double authentification n'est pas active sur ce compte.");
        }

        if (!await _secondFacteur.VerifierCodeAsync(utilisateur, dto.Code))
        {
            return BadRequest("Code invalide. Saisir le code affiché maintenant par l'application.");
        }

        var codes = await _secondFacteur.NouveauxCodesAsync(utilisateur);

        return codes is null
            ? Conflict("Le compte a été modifié en même temps. Recommencer.")
            : Ok(new CodesRecuperationDto { Codes = codes });
    }

    /// <summary>
    /// Retire le second facteur de son propre compte, sur un code de l'application.
    ///
    /// Pour un compte que son rôle <b>oblige</b>, c'est le geste « changer de
    /// téléphone » : ses sessions sont fermées, celle-ci comprise, et la connexion
    /// suivante impose l'équipement du nouveau. Rouvrir une session ici en ferait une
    /// session sans second facteur, que <c>OnTokenValidated</c> refuserait d'ailleurs.
    /// Pour les autres, la session de l'appelant est rouverte.
    /// </summary>
    [Authorize]
    [HttpPost("second-facteur/desactiver")]
    public async Task<ActionResult<SessionDto>> Desactiver(CodeSecondFacteurDto dto)
    {
        var utilisateur = await AppelantConnecteAsync();
        if (utilisateur is null) return Unauthorized("Jeton sans identifiant exploitable.");

        var limite = LimiteParAdresse() ?? LimiteParCompte(utilisateur);
        if (limite is not null) return limite;

        if (!utilisateur.TwoFactorEnabled)
        {
            return Conflict("La double authentification n'est pas active sur ce compte.");
        }

        if (!await _secondFacteur.VerifierCodeAsync(utilisateur, dto.Code))
        {
            return BadRequest("Code invalide. Saisir le code affiché maintenant par l'application.");
        }

        if (!await _secondFacteur.RetirerAsync(utilisateur))
        {
            return Conflict("Le compte a été modifié en même temps. Recommencer.");
        }

        if (await _secondFacteur.EstObligeAsync(utilisateur))
        {
            await _session.FermerToutAsync(HttpContext, utilisateur);
            return NoContent();
        }

        await _session.RevoquerToutAsync(utilisateur.Id, "Désactivation du second facteur");

        return Ok(await _session.OuvrirAsync(HttpContext, utilisateur));
    }

    /// <summary>
    /// Retire le second facteur d'un autre compte de l'entreprise : le téléphone est
    /// perdu, et les codes de récupération aussi. Le compte repasse par l'équipement à sa
    /// connexion suivante.
    ///
    /// ⚠️ <b>Le cadrage sur l'entreprise est écrit à la main</b> : <c>AspNetUsers</c>
    /// n'est pas filtrée (CLAUDE.md §7). Un compte d'une autre entreprise rend 404,
    /// comme un compte inexistant.
    ///
    /// ⚠️ <b>Jamais sur soi-même.</b> Qui est connecté a son second facteur ; qui a tout
    /// perdu ne peut pas l'être. Le dernier administrateur dans ce cas n'a pas de sortie
    /// par l'API, délibérément : il passe par le script d'exploitation (SPEC.md,
    /// étape 23, lot C).
    ///
    /// Journalisée, alors que rien n'est lu : c'est l'exception écrite à la règle 5 de
    /// CLAUDE.md §7 bis, sur le précédent de l'anonymisation.
    /// </summary>
    [JournaliserAcces(RessourceAccedee.Compte, NatureAcces.ReinitialisationSecondFacteur)]
    [Authorize(Roles = RolesKlean.Administrateur)]
    [HttpPost("comptes/{id:guid}/reinitialiser-second-facteur")]
    public async Task<ActionResult<ReinitialisationSecondFacteurDto>> Reinitialiser(Guid id)
    {
        var appelant = await AppelantConnecteAsync();
        if (appelant is null) return Unauthorized("Jeton sans identifiant exploitable.");

        var compte = await _utilisateurs.FindByIdAsync(id.ToString());

        if (compte is null || compte.EntrepriseId != appelant.EntrepriseId) return NotFound();

        if (compte.Id == appelant.Id)
        {
            return Conflict(
                "Un administrateur ne réinitialise pas son propre second facteur. Pour "
                + "changer de téléphone : « Désactiver » depuis l'écran du compte, puis "
                + "équiper le nouveau à la connexion suivante.");
        }

        if (!compte.TwoFactorEnabled)
        {
            return Conflict("Ce compte n'a pas de double authentification active : rien à réinitialiser.");
        }

        if (!await _secondFacteur.RetirerAsync(compte))
        {
            return Conflict("Le compte a été modifié en même temps. Recommencer.");
        }

        var sessions = await _session.RevoquerToutAsync(compte.Id, "Réinitialisation du second facteur");

        return Ok(new ReinitialisationSecondFacteurDto
        {
            UtilisateurId = compte.Id,
            SessionsRevoquees = sessions
        });
    }

    /// <summary>
    /// Le compte à équiper : l'appelant connecté, sinon le porteur d'un défi
    /// <c>Equipement</c>. Un compte déjà équipé est refusé — sa clé ne se remplace pas
    /// sans passer par la désactivation, qui exige un code.
    /// </summary>
    private async Task<(Utilisateur? Utilisateur, ActionResult? Refus)> AppelantAEquiperAsync()
    {
        var utilisateur = User.Identity?.IsAuthenticated == true
            ? await AppelantConnecteAsync()
            : await _secondFacteur.CompteDuDefiAsync(HttpContext, EtapeDefi.Equipement);

        if (utilisateur is null) return (null, Unauthorized(DefiPerime));

        if (utilisateur.TwoFactorEnabled)
        {
            return (null, Conflict(
                "La double authentification est déjà active sur ce compte. Pour changer "
                + "de téléphone, la désactiver d'abord."));
        }

        return (utilisateur, null);
    }

    private async Task<Utilisateur?> AppelantConnecteAsync()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(id, out var utilisateurId)
            ? await _utilisateurs.FindByIdAsync(utilisateurId.ToString())
            : null;
    }

    private static string? RefusDeForme(CodeSecondFacteurDto dto) =>
        (dto.Code is null) == (dto.CodeRecuperation is null)
            ? "Saisir le code à six chiffres de l'application, ou un code de récupération."
            : null;

    private ActionResult? LimiteParAdresse()
    {
        var adresse = HttpContext.Connection.RemoteIpAddress?.ToString() ?? LimiteurDebit.AdresseInconnue;

        return _debit.Autoriser($"code:adresse:{adresse}", PlafondsDebit.CodesParAdresse, PlafondsDebit.FenetreCodes)
            ? null
            : TropDeTentatives();
    }

    private ActionResult? LimiteParCompte(Utilisateur utilisateur) =>
        _debit.Autoriser($"code:compte:{utilisateur.Id}", PlafondsDebit.CodesParCompte, PlafondsDebit.FenetreCodes)
            ? null
            : TropDeTentatives();

    /// <summary>Le message de <c>AuthController</c> : il ne nomme pas la dimension atteinte.</summary>
    private ActionResult TropDeTentatives() =>
        StatusCode(StatusCodes.Status429TooManyRequests,
            "Trop de tentatives. Réessayer dans quelques minutes.");
}
