using System.Security.Cryptography;
using System.Text;
using Klean.Api.Services;

namespace Klean.Api.Middleware;

/// <summary>
/// Protection CSRF par double soumission, conséquence directe du passage du jeton
/// en cookie.
///
/// Un jeton porté par un cookie est une <i>ambient credential</i> : le navigateur
/// l'envoie de lui-même, donc un formulaire hébergé ailleurs peut déclencher une
/// écriture authentifiée. Avec un en-tête <c>Authorization</c>, le problème
/// n'existait pas — c'est le prix du <c>HttpOnly</c>, et il se paie ici.
///
/// <c>SameSite=Strict</c> bloque déjà l'essentiel des cas. Cette vérification est
/// la seconde barrière : elle ne dépend pas du comportement du navigateur, et
/// couvre les clients anciens ainsi que les chemins où <c>SameSite</c> ne
/// s'applique pas.
///
/// La valeur attendue vit dans un cookie lisible par le script, que le client
/// recopie dans un en-tête. Un site tiers peut faire envoyer le cookie ; il ne
/// peut pas le <b>lire</b> pour fabriquer l'en-tête, la politique de même origine
/// s'y oppose.
/// </summary>
public class VerificationCsrf
{
    private static readonly HashSet<string> MethodesSansEffet =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    private readonly RequestDelegate _suivant;

    public VerificationCsrf(RequestDelegate suivant) => _suivant = suivant;

    public async Task InvokeAsync(HttpContext contexte)
    {
        if (!Concernee(contexte))
        {
            await _suivant(contexte);
            return;
        }

        var attendu = contexte.Request.Cookies[ServiceSession.CookieCsrf];
        var fourni = contexte.Request.Headers[ServiceSession.EnteteCsrf].ToString();

        if (string.IsNullOrEmpty(attendu) || !Identiques(attendu, fourni))
        {
            contexte.Response.StatusCode = StatusCodes.Status403Forbidden;
            contexte.Response.ContentType = "text/plain; charset=utf-8";

            await contexte.Response.WriteAsync(
                $"Jeton anti-CSRF absent ou invalide. Recopier la valeur du cookie "
                + $"« {ServiceSession.CookieCsrf} » dans l'en-tête « {ServiceSession.EnteteCsrf} ».");

            return;
        }

        await _suivant(contexte);
    }

    /// <summary>
    /// Ne s'applique qu'aux écritures authentifiées <b>par cookie</b>. Une requête
    /// sans cookie de session ne porte aucune autorisation implicite : il n'y a
    /// alors rien à détourner, et exiger l'en-tête empêcherait la connexion
    /// elle-même.
    /// </summary>
    private static bool Concernee(HttpContext contexte)
    {
        if (MethodesSansEffet.Contains(contexte.Request.Method)) return false;

        return contexte.Request.Cookies.ContainsKey(ServiceSession.CookieAcces)
               || contexte.Request.Cookies.ContainsKey(ServiceSession.CookieRafraichissement);
    }

    /// <summary>Comparaison à temps constant : la valeur est un secret de session.</summary>
    private static bool Identiques(string attendu, string fourni) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(attendu), Encoding.UTF8.GetBytes(fourni));
}
