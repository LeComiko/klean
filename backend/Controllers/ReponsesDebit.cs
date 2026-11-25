using System.Globalization;
using Microsoft.AspNetCore.Mvc;

namespace Klean.Api.Controllers;

internal static class ReponsesDebit
{
    public const string MessageParDefaut = "Trop de tentatives. Réessayer dans quelques minutes.";

    /// <summary>
    /// 429 avec <c>Retry-After</c>, en secondes : l'attente restante, à défaut la fenêtre. Posé dans
    /// l'action qui écrit la réponse : aucun gestionnaire d'exception ne la vide ensuite.
    /// </summary>
    public static ObjectResult TropDeTentatives(this ControllerBase controleur, TimeSpan attente, TimeSpan fenetre,
        string message = MessageParDefaut)
    {
        var secondes = attente > TimeSpan.Zero ? attente : fenetre;
        controleur.Response.Headers.RetryAfter =
            ((int)Math.Ceiling(secondes.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        return controleur.StatusCode(StatusCodes.Status429TooManyRequests, message);
    }
}
