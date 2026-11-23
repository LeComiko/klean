using System.Globalization;
using System.Net;
using System.Text;

namespace Klean.Api.Tests.Infrastructure;

/// <summary>
/// Le gestionnaire <b>primaire</b> du client de géocodage, substitué : toute la chaîne du client —
/// journalisation comprise — s'exécute comme en production. ⚠️ Il ne suit aucune redirection, ne
/// garde aucun cookie et n'ajoute aucun en-tête de lui-même : ce qu'il ne fait pas, seul le test de la
/// configuration réelle le prouve.
/// </summary>
public sealed class GeocodageDeTest : HttpMessageHandler
{
    public sealed record RequeteVue(Uri Uri, IReadOnlyDictionary<string, string> Entetes);

    private readonly List<RequeteVue> _requetes = [];

    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Repondre { get; set; } = ParDefaut;

    public IReadOnlyList<RequeteVue> Requetes
    {
        get { lock (_requetes) return _requetes.ToList(); }
    }

    public void Reinitialiser()
    {
        lock (_requetes) _requetes.Clear();
        Repondre = ParDefaut;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken annulation)
    {
        lock (_requetes)
        {
            _requetes.Add(new RequeteVue(requete.RequestUri!,
                requete.Headers.ToDictionary(e => e.Key, e => string.Join(",", e.Value), StringComparer.OrdinalIgnoreCase)));
        }

        return Repondre(requete, annulation);
    }

    /// <summary>La fabrique de clients libère ses gestionnaires : celui-ci, partagé, doit lui survivre.</summary>
    protected override void Dispose(bool disposing) { }

    private static Task<HttpResponseMessage> ParDefaut(HttpRequestMessage _, CancellationToken __) =>
        Task.FromResult(Json(Trouve()));

    public static HttpResponseMessage Json(string json, HttpStatusCode statut = HttpStatusCode.OK) =>
        new(statut) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public const string Vide = """{"type":"FeatureCollection","features":[]}""";

    /// <summary>La forme mesurée le 23 novembre 2026 sur une adresse publique.</summary>
    public static string Trouve(double latitude = 48.850699, double longitude = 2.308628,
        string label = "20 Avenue de Ségur 75007 Paris", string score = "0.97", string type = "housenumber")
    {
        static string N(double x) => x.ToString("R", CultureInfo.InvariantCulture);
        var proprieteScore = score.Length == 0 ? "" : $"\"score\":{score},";
        return "{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\","
            + $"\"geometry\":{{\"type\":\"Point\",\"coordinates\":[{N(longitude)},{N(latitude)}]}},"
            + $"\"properties\":{{\"label\":{System.Text.Json.JsonSerializer.Serialize(label)},{proprieteScore}\"type\":\"{type}\"}}}}]}}";
    }
}
