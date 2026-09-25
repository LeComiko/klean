using System.Net.Http.Json;
using Klean.Api.DTOs;

namespace Klean.Api.Tests.Infrastructure;

internal static class ExtensionsHttp
{
    /// <summary>
    /// Déballe une réponse paginée. Les onze listes renvoient
    /// <c>{ elements, total, page, taille }</c> depuis l'étape 8 : sans plafond
    /// de taille, ces endpoints étaient un vecteur d'épuisement de ressources.
    /// </summary>
    public static async Task<List<T>> ListerAsync<T>(this HttpClient client, string route)
    {
        var page = await client.GetFromJsonAsync<PageDto<T>>(route)
            ?? throw new InvalidOperationException($"Réponse paginée illisible sur {route}.");

        return page.Elements.ToList();
    }

    public static async Task<PageDto<T>> PageAsync<T>(this HttpClient client, string route)
    {
        return await client.GetFromJsonAsync<PageDto<T>>(route)
            ?? throw new InvalidOperationException($"Réponse paginée illisible sur {route}.");
    }
}
