namespace Klean.Api.Middleware;

/// <summary>
/// En-têtes de sécurité, posés sur <b>toute</b> réponse de l'API.
///
/// Placé haut dans le pipeline, avant l'authentification : un 401, un 403 ou un
/// 404 doit les porter comme un 200. Un en-tête posé par un filtre de contrôleur
/// manquerait précisément les réponses produites avant que le contrôleur ne soit
/// atteint, c'est-à-dire toutes celles d'un attaquant.
///
/// ⚠️ Enregistré <b>après</b> <c>UseSwaggerUI</c>, et c'est voulu : Swagger sert
/// une page HTML avec du script et du style en ligne, que la politique ci-dessous
/// interdit. L'interface de Swagger court-circuite le pipeline, donc ses réponses
/// ne passent jamais ici — l'ordre suffit, aucune exception de chemin à écrire.
/// Elle n'est de toute façon montée qu'en développement.
///
/// ⚠️ Les en-têtes sont posés dans <c>OnStarting</c>, au moment où la réponse
/// part, et non à l'aller. <c>UseExceptionHandler</c> vide les en-têtes avant
/// d'écrire son 500 (étape 18) : posés à l'aller, ils manqueraient sur la réponse
/// d'erreur — un corps JSON sans <c>nosniff</c> ni politique, exactement celui
/// qu'un attaquant cherche à provoquer.
/// </summary>
public class EnTetesSecurite
{
    /// <summary>
    /// Politique de sécurité du contenu d'une API qui ne rend que du JSON :
    /// <b>rien</b> ne doit jamais être chargé ni exécuté depuis une de ses
    /// réponses.
    ///
    /// ⚠️ Ce n'est pas la politique du frontend, et il ne faut pas les confondre.
    /// Le document HTML de KLEAN charge une feuille de style, un script et quatre
    /// polices, tous servis par lui-même, et a donc besoin de <c>'self'</c> sur
    /// <c>script-src</c>, <c>style-src</c> et <c>font-src</c> — mais ce document
    /// n'est pas servi par l'API. Sa politique vit dans <c>frontend/csp.ts</c>, et
    /// c'est Caddy qui la renvoie en production. Ouvrir ces directives ici
    /// affaiblirait la surface de l'API pour un besoin qu'elle n'a pas.
    ///
    /// <c>frame-ancestors 'none'</c> fait le travail de <c>X-Frame-Options</c> pour
    /// les navigateurs récents ; l'en-tête historique reste posé pour les autres.
    /// <c>form-action 'none'</c> et <c>base-uri 'none'</c> couvrent le cas d'une
    /// réponse d'erreur rendue comme du HTML malgré le type de contenu.
    /// </summary>
    public const string Politique =
        "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    private readonly RequestDelegate _suivant;

    public EnTetesSecurite(RequestDelegate suivant) => _suivant = suivant;

    public Task InvokeAsync(HttpContext contexte)
    {
        contexte.Response.OnStarting(() =>
        {
            var entetes = contexte.Response.Headers;

            entetes["Content-Security-Policy"] = Politique;

            // Sans nosniff, un navigateur peut décider qu'une réponse JSON contenant
            // du balisage est du HTML, et l'exécuter dans l'origine de l'API.
            entetes["X-Content-Type-Options"] = "nosniff";

            // L'API n'émet aucun lien sortant. Ne rien fuiter est donc gratuit — et
            // les URL de KLEAN portent des identifiants de sites et d'agents.
            entetes["Referrer-Policy"] = "no-referrer";

            entetes["X-Frame-Options"] = "DENY";

            return Task.CompletedTask;
        });

        return _suivant(contexte);
    }
}
