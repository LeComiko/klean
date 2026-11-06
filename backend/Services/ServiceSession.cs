using System.Security.Cryptography;
using System.Text;
using Klean.Api.Data;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Services;

/// <summary>Issue d'une opération de session.</summary>
public readonly record struct ResultatSession(SessionDto? Valeur, string? Erreur)
{
    public static ResultatSession Ok(SessionDto valeur) => new(valeur, null);
    public static ResultatSession Refus(string erreur) => new(null, erreur);
}

/// <summary>
/// Ouvre, rafraîchit et ferme les sessions.
///
/// Le jeton d'accès ne quitte jamais le corps d'une réponse : il part en cookie
/// <c>HttpOnly</c> <c>Secure</c> <c>SameSite=Strict</c>. C'est le remplacement du
/// <c>localStorage</c>, qui exposait un porteur de huit heures à la première faille
/// XSS — une seule ligne de script injectée suffisait à emporter une session
/// complète, dans le tenant d'un client.
///
/// Le jeton de rafraîchissement est opaque, aléatoire sur 256 bits, et
/// <b>seule son empreinte</b> est persistée. Il est donc révocable, contrairement
/// au JWT, et une fuite de la table ne permet pas de rejouer les sessions.
/// </summary>
public class ServiceSession
{
    public const string CookieAcces = "klean_acces";
    public const string CookieRafraichissement = "klean_rafraichissement";

    /// <summary>
    /// Volontairement lisible par le script : c'est la moitié visible du
    /// double-submit CSRF. Sa valeur n'authentifie rien à elle seule.
    /// </summary>
    public const string CookieCsrf = "klean_csrf";

    public const string EnteteCsrf = "X-Klean-CSRF";

    /// <summary>
    /// Chemin du cookie de rafraîchissement : il n'a aucune raison d'être envoyé
    /// aux endpoints métier, et ne pas l'y envoyer réduit d'autant sa surface.
    /// </summary>
    private const string CheminRafraichissement = "/api/auth";

    /// <summary>
    /// Durée <b>absolue</b> d'une session. La rotation reporte cette échéance
    /// telle quelle : une session ne se prolonge donc pas indéfiniment par simple
    /// activité. Passé ce délai, il faut se reconnecter.
    /// </summary>
    public static TimeSpan DureeSession => TimeSpan.FromDays(14);

    private readonly KleanDbContext _db;
    private readonly UserManager<Utilisateur> _utilisateurs;
    private readonly JwtService _jwt;

    public ServiceSession(KleanDbContext db, UserManager<Utilisateur> utilisateurs, JwtService jwt)
    {
        _db = db;
        _utilisateurs = utilisateurs;
        _jwt = jwt;
    }

    /// <summary>Connexion réussie : émet le couple de jetons et pose les trois cookies.</summary>
    public async Task<SessionDto> OuvrirAsync(HttpContext contexte, Utilisateur utilisateur)
    {
        var maintenant = DateTime.UtcNow;

        var valeur = await EmettreRafraichissementAsync(
            contexte, utilisateur.Id, maintenant.Add(DureeSession), null);

        return PoserCookies(contexte, utilisateur, valeur, await RolesAsync(utilisateur));
    }

    /// <summary>
    /// Rotation : le jeton présenté est consommé et remplacé.
    ///
    /// Un jeton déjà révoqué qui se présente est traité comme un <b>rejeu</b> —
    /// donc comme une fuite — et toute la session de l'utilisateur est coupée. Sans
    /// cette réaction, la rotation ne protège de rien : un attaquant qui a copié un
    /// jeton s'en sert tranquillement en parallèle du porteur légitime.
    /// </summary>
    public async Task<ResultatSession> RafraichirAsync(HttpContext contexte)
    {
        if (!contexte.Request.Cookies.TryGetValue(CookieRafraichissement, out var presente)
            || string.IsNullOrWhiteSpace(presente))
        {
            return ResultatSession.Refus("Aucune session à rafraîchir.");
        }

        var empreinte = Empreinte(presente);
        var maintenant = DateTime.UtcNow;

        var jeton = await _db.JetonsRafraichissement
            .FirstOrDefaultAsync(j => j.Empreinte == empreinte);

        if (jeton is null)
        {
            EffacerCookies(contexte);
            return ResultatSession.Refus("Session inconnue ou expirée.");
        }

        if (!jeton.EstActif(maintenant))
        {
            // Déjà consommé ou révoqué : soit c'est un rejeu, soit le porteur
            // légitime rejoue un jeton qu'il croyait valide. Dans les deux cas la
            // prudence est la même, et son coût est une reconnexion.
            await RevoquerToutAsync(jeton.UtilisateurId, "Rejeu détecté");
            EffacerCookies(contexte);
            return ResultatSession.Refus(
                "Session révoquée : ce jeton de rafraîchissement avait déjà servi. "
                + "Par précaution, toutes les sessions de ce compte ont été coupées. "
                + "Se reconnecter.");
        }

        var utilisateur = await _utilisateurs.FindByIdAsync(jeton.UtilisateurId.ToString());

        if (utilisateur is null || !utilisateur.Actif)
        {
            await RevoquerToutAsync(jeton.UtilisateurId, "Compte désactivé");
            EffacerCookies(contexte);
            return ResultatSession.Refus("Ce compte est désactivé.");
        }

        var roles = await RolesAsync(utilisateur);

        // ⚠️ Un compte que son rôle oblige au second facteur n'a pas de session sans lui
        // (SPEC.md, étape 23). Celles ouvertes avant l'obligation, ou survivant à une
        // désactivation, s'arrêtent ici : sans ce refus, une session de quatorze jours
        // d'avant le déploiement continuerait de se renouveler par mot de passe seul.
        if (!utilisateur.TwoFactorEnabled && ServiceSecondFacteur.EstOblige(roles))
        {
            await RevoquerToutAsync(utilisateur.Id, "Second facteur exigé");
            EffacerCookies(contexte);
            return ResultatSession.Refus(
                "Ce compte doit activer la double authentification. Se reconnecter.");
        }

        var valeur = await EmettreRafraichissementAsync(
            contexte, utilisateur.Id, jeton.DateExpiration, jeton);

        return ResultatSession.Ok(PoserCookies(contexte, utilisateur, valeur, roles));
    }

    /// <summary>
    /// Déconnexion. Révoque le jeton présenté et efface les cookies. Volontairement
    /// tolérante : elle n'exige pas de jeton d'accès valide, sinon une session dont
    /// l'accès a expiré serait impossible à fermer proprement.
    /// </summary>
    public async Task FermerAsync(HttpContext contexte)
    {
        if (contexte.Request.Cookies.TryGetValue(CookieRafraichissement, out var presente)
            && !string.IsNullOrWhiteSpace(presente))
        {
            var empreinte = Empreinte(presente);

            var jeton = await _db.JetonsRafraichissement
                .FirstOrDefaultAsync(j => j.Empreinte == empreinte);

            if (jeton is not null && jeton.DateRevocation is null)
            {
                jeton.DateRevocation = DateTime.UtcNow;
                jeton.MotifRevocation = "Déconnexion";
                await _db.SaveChangesAsync();
            }
        }

        EffacerCookies(contexte);
    }

    /// <summary>
    /// Déconnexion de <b>tous les appareils</b>.
    ///
    /// C'est la réponse à la seule brèche assumée du lot A de l'étape 12 : une
    /// déconnexion ordinaire révoque le jeton de rafraîchissement, mais le jeton
    /// d'accès déjà émis reste valable jusqu'à quinze minutes — il est signé, et
    /// rien ne le rappelle. La fenêtre était acceptée parce que la fermer en
    /// permanence aurait coûté une lecture de plus par requête.
    ///
    /// Elle se ferme ici <b>sans rien coûter en régime normal</b> : régénérer le
    /// <c>SecurityStamp</c> périme instantanément tous les jetons d'accès du compte,
    /// parce que <c>OnTokenValidated</c> compare déjà l'empreinte du jeton à celle
    /// du compte à chaque requête. La vérification existait ; il manquait le geste
    /// qui la déclenche.
    ///
    /// Le jeton d'accès qui a servi à appeler cette méthode meurt avec les autres.
    /// C'est voulu : « tous les appareils » inclut celui-ci, et les cookies sont
    /// effacés dans la même réponse.
    /// </summary>
    /// <returns>Nombre de sessions de rafraîchissement coupées.</returns>
    public async Task<int> FermerToutAsync(HttpContext contexte, Utilisateur utilisateur)
    {
        await _utilisateurs.UpdateSecurityStampAsync(utilisateur);

        var coupees = await RevoquerToutAsync(
            utilisateur.Id, "Déconnexion de tous les appareils");

        EffacerCookies(contexte);

        return coupees;
    }

    /// <summary>
    /// Coupe toutes les sessions actives d'un compte. Utilisée par la détection de
    /// rejeu, la désactivation d'un compte et la réinitialisation de mot de passe.
    ///
    /// ⚠️ Ne touche pas au <c>SecurityStamp</c> : les jetons d'accès en circulation
    /// survivent jusqu'à leur échéance. Pour les tuer aussi, voir
    /// <see cref="FermerToutAsync"/>.
    /// </summary>
    public async Task<int> RevoquerToutAsync(Guid utilisateurId, string motif)
    {
        var maintenant = DateTime.UtcNow;

        var actifs = await _db.JetonsRafraichissement
            .Where(j => j.UtilisateurId == utilisateurId && j.DateRevocation == null)
            .ToListAsync();

        foreach (var jeton in actifs)
        {
            jeton.DateRevocation = maintenant;
            jeton.MotifRevocation = motif;
        }

        if (actifs.Count > 0) await _db.SaveChangesAsync();

        return actifs.Count;
    }

    private async Task<string> EmettreRafraichissementAsync(
        HttpContext contexte, Guid utilisateurId, DateTime expiration, JetonRafraichissement? remplace)
    {
        var valeur = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        var jeton = new JetonRafraichissement
        {
            UtilisateurId = utilisateurId,
            Empreinte = Empreinte(valeur),
            DateCreation = DateTime.UtcNow,
            DateExpiration = expiration,
            AdresseIp = contexte.Connection.RemoteIpAddress?.ToString()
        };

        _db.JetonsRafraichissement.Add(jeton);

        if (remplace is not null)
        {
            remplace.DateRevocation = DateTime.UtcNow;
            remplace.MotifRevocation = "Rotation";
            remplace.RemplacePar = jeton.Id;
        }

        await _db.SaveChangesAsync();

        return valeur;
    }

    private SessionDto PoserCookies(
        HttpContext contexte, Utilisateur utilisateur, string rafraichissement, IList<string> roles)
    {
        var expiration = DateTime.UtcNow.Add(JwtService.DureeAcces);

        contexte.Response.Cookies.Append(
            CookieAcces, _jwt.Generer(utilisateur, roles),
            Options(JwtService.DureeAcces, "/"));

        contexte.Response.Cookies.Append(
            CookieRafraichissement, rafraichissement,
            Options(DureeSession, CheminRafraichissement));

        contexte.Response.Cookies.Append(
            CookieCsrf, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            Options(DureeSession, "/", lisibleParLeScript: true));

        return new SessionDto
        {
            Expiration = expiration,
            Profil = new ProfilDto
            {
                Id = utilisateur.Id,
                Email = utilisateur.Email ?? string.Empty,
                Nom = utilisateur.Nom,
                Prenom = utilisateur.Prenom,
                EntrepriseId = utilisateur.EntrepriseId,
                AgentId = utilisateur.AgentId,
                ClientId = utilisateur.ClientId,
                Actif = utilisateur.Actif,
                Roles = roles
            }
        };
    }

    private void EffacerCookies(HttpContext contexte)
    {
        contexte.Response.Cookies.Delete(CookieAcces, Options(TimeSpan.Zero, "/"));
        contexte.Response.Cookies.Delete(
            CookieRafraichissement, Options(TimeSpan.Zero, CheminRafraichissement));
        contexte.Response.Cookies.Delete(
            CookieCsrf, Options(TimeSpan.Zero, "/", lisibleParLeScript: true));
    }

    private async Task<IList<string>> RolesAsync(Utilisateur utilisateur) =>
        await _utilisateurs.GetRolesAsync(utilisateur);

    /// <summary>
    /// <c>Secure</c> est posé sans condition, y compris en développement :
    /// les navigateurs traitent <c>http://localhost</c> comme un contexte sûr, donc
    /// rien ne casse, et un drapeau conditionnel finirait par rester à false en
    /// production. La série de tests appelle l'API en <c>https</c> pour la même
    /// raison — <c>CookieContainer</c> respecte l'attribut.
    /// </summary>
    private static CookieOptions Options(TimeSpan duree, string chemin, bool lisibleParLeScript = false) => new()
    {
        HttpOnly = !lisibleParLeScript,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = chemin,
        MaxAge = duree == TimeSpan.Zero ? null : duree,
        IsEssential = true
    };

    private static string Empreinte(string valeur) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valeur)));
}
