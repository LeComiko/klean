using System.Diagnostics;

namespace Klean.Api.Middleware;

/// <summary>
/// Renvoie sur <b>toute</b> réponse l'identifiant qui relie cette requête aux
/// lignes du journal du serveur (SPEC.md étape 18).
///
/// L'identifiant n'est pas inventé ici : c'est le <c>TraceId</c> de l'activité
/// qu'ASP.NET ouvre pour chaque requête, celui que le formateur JSON écrit déjà
/// dans la portée de chaque ligne de journal. Un second identifiant, fabriqué à
/// côté, obligerait à tenir la correspondance entre les deux.
///
/// Son usage est humain : un utilisateur qui voit « Erreur interne » le recopie,
/// l'exploitant le cherche dans <c>docker compose logs</c>, et trouve l'exception.
/// Sans lui, un 500 en production ne se relie à rien.
///
/// ⚠️ Posé dans <c>OnStarting</c>, pas avant l'appel au suivant.
/// <c>UseExceptionHandler</c> vide les en-têtes de la réponse avant d'écrire la
/// sienne : un en-tête posé à l'aller disparaîtrait précisément sur le 500, la
/// seule réponse où il sert vraiment.
/// </summary>
public class IdentifiantCorrelation
{
    public const string Entete = "X-Correlation-Id";

    private readonly RequestDelegate _suivant;

    public IdentifiantCorrelation(RequestDelegate suivant) => _suivant = suivant;

    public Task InvokeAsync(HttpContext contexte)
    {
        var identifiant = Valeur(contexte);

        contexte.Response.OnStarting(() =>
        {
            contexte.Response.Headers[Entete] = identifiant;
            return Task.CompletedTask;
        });

        return _suivant(contexte);
    }

    /// <summary>
    /// Le <c>TraceId</c> de l'activité courante, en 32 caractères hexadécimaux. À
    /// défaut d'activité — aucune n'est ouverte si rien n'écoute —, l'identifiant
    /// de requête d'ASP.NET, qui figure lui aussi dans la portée du journal.
    /// </summary>
    public static string Valeur(HttpContext contexte) =>
        Activity.Current?.TraceId.ToHexString() ?? contexte.TraceIdentifier;
}
