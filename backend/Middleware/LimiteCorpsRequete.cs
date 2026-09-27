namespace Klean.Api.Middleware;

/// <summary>
/// Plafonne la taille du corps d'une requête.
///
/// Kestrel porte déjà une limite — trente mégaoctets par défaut, ramenée à un
/// mégaoctet par <c>Program.cs</c>. Ce middleware ne la remplace pas, il la rend
/// <b>vraie indépendamment du serveur</b>, et c'est la seule raison de son
/// existence :
///
/// <list type="number">
/// <item>La limite de Kestrel vit dans les options de Kestrel. Un hôte qui n'est
/// pas Kestrel — IIS, HTTP.sys, ou le <c>TestServer</c> de la série de tests — ne
/// les lit pas, et le plafond disparaît sans un mot. La série l'a montré : un
/// corps de deux mégaoctets était lu et lié en entier, puis refusé par un
/// <c>[MaxLength(200)]</c>, c'est-à-dire après l'allocation que le plafond existe
/// pour éviter.</item>
/// <item>Une limite qu'aucun test ne peut atteindre est une limite dont personne
/// ne saura qu'elle a été retirée.</item>
/// </list>
///
/// ⚠️ Le contrôle porte sur <c>Content-Length</c>, donc <b>avant</b> qu'un octet
/// de corps soit lu. Un corps en fragments (<c>Transfer-Encoding: chunked</c>)
/// n'annonce pas sa taille et échappe à ce contrôle : c'est la limite de Kestrel
/// qui l'arrête pendant la lecture. Les deux sont nécessaires, aucune ne couvre
/// le cas de l'autre.
/// </summary>
public class LimiteCorpsRequete
{
    private readonly RequestDelegate _suivant;
    private readonly long _maximum;

    public LimiteCorpsRequete(RequestDelegate suivant, long maximum)
    {
        _suivant = suivant;
        _maximum = maximum;
    }

    public async Task InvokeAsync(HttpContext contexte)
    {
        var annoncee = contexte.Request.ContentLength;

        if (annoncee > _maximum)
        {
            contexte.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            contexte.Response.ContentType = "text/plain; charset=utf-8";

            // La taille autorisée est dite : elle n'est pas un secret, et une
            // intégration qui envoie trop gros a besoin de savoir de combien.
            await contexte.Response.WriteAsync(
                $"Corps de requête trop volumineux : {annoncee} octets pour un maximum "
                + $"de {_maximum}.");

            return;
        }

        await _suivant(contexte);
    }
}
