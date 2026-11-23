using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Klean.Api.Services;

/// <summary>Ce que rend une localisation. Aucune autre issue : l'appelant n'a pas à lire d'exception.</summary>
public abstract record ResultatGeocodage
{
    public sealed record Trouve(double Latitude, double Longitude, string AdresseTrouvee, bool AVerifier) : ResultatGeocodage;

    /// <summary>Le service a répondu, et n'a rien trouvé.</summary>
    public sealed record Introuvable : ResultatGeocodage;

    /// <summary>Tout le reste : injoignable, lent, refus, réponse illisible, disjoncteur ouvert.</summary>
    public sealed record Injoignable : ResultatGeocodage;
}

public sealed record ReglagesGeocodage(Uri Adresse, int DelaiSecondes);

/// <summary>
/// La localisation d'un site par la Géoplateforme de l'IGN (0.3.0, SPEC.md lot A3 points 9 et 10).
/// Ce qui part : la voie, le code postal et la commune — rien d'autre que <c>User-Agent</c> et
/// <c>Host</c>.
///
/// Classe concrète, sans interface (CLAUDE.md §11) : la série substitue le gestionnaire primaire du
/// client nommé <see cref="NomClient"/>.
///
/// ⚠️ Le client est verrouillé dans <see cref="Enregistrer"/>, et chaque verrou ferme une fuite que le
/// faux gestionnaire de la série ne verrait pas : redirection suivie, cookie renvoyé d'une entreprise à
/// l'autre, <c>traceparent</c> qui porterait l'identifiant de corrélation de KLEAN, URL — donc adresse —
/// dans le journal. <c>GeocodageTests</c> les vérifie sur le gestionnaire réel.
/// </summary>
public sealed class ServiceGeocodage
{
    public const string NomClient = "geocodage";
    public const string AdresseParDefaut = "https://data.geopf.fr/geocodage";
    public const int DelaiParDefaut = 5;

    /// <summary>Strictement sous les 30 s du délai de requête : sinon le jeton de la requête gagnerait, et un 504 remplacerait le 503.</summary>
    public const int DelaiMaximal = 20;

    public const int TailleMaximaleReponse = 64 * 1024;

    /// <summary>À éprouver sur le terrain avant d'en faire un réglage — la règle du rayon du passage (§9 ter).</summary>
    public const double SeuilScore = 0.7;

    public const int LongueurMaximaleAdresseTrouvee = 200;

    private static readonly TimeSpan PauseParDefaut = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PauseMaximale = TimeSpan.FromSeconds(30);

    private readonly IHttpClientFactory _fabrique;
    private readonly string _base;

    /// <summary>Le disjoncteur : avant cet instant (ticks UTC), aucun appel ne part.</summary>
    private long _reprise;

    public ServiceGeocodage(IHttpClientFactory fabrique, IConfiguration configuration)
    {
        _fabrique = fabrique;
        _base = Lire(configuration).Adresse.AbsoluteUri.TrimEnd('/');
    }

    /// <summary>
    /// Lit et valide la configuration. ⚠️ Appelée deux fois, et les deux sont nécessaires : par
    /// <see cref="Enregistrer"/>, ce qui fait refuser le démarrage ; et dans le délégué du client, pour
    /// la valeur finale de la configuration.
    /// </summary>
    public static ReglagesGeocodage Lire(IConfiguration configuration)
    {
        var brute = configuration["Geocodage:Adresse"];
        var texte = string.IsNullOrWhiteSpace(brute) ? AdresseParDefaut : brute.Trim();

        // La valeur n'est pas recopiée dans le message : elle pourrait porter des identifiants.
        if (!Uri.TryCreate(texte, UriKind.Absolute, out var adresse)
            || adresse.Scheme != Uri.UriSchemeHttps
            || adresse.UserInfo.Length > 0
            || adresse.Query.Length > 0
            || adresse.Fragment.Length > 0)
        {
            throw new InvalidOperationException(
                "Geocodage:Adresse doit être une adresse absolue en https, sans identifiants, requête ni "
                + $"fragment : configurée à moitié. Par défaut : {AdresseParDefaut}.");
        }

        var delai = DelaiParDefaut;
        var delaiBrut = configuration["Geocodage:DelaiSecondes"];

        if (!string.IsNullOrWhiteSpace(delaiBrut))
        {
            if (!int.TryParse(delaiBrut.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out delai)
                || delai < 1 || delai > DelaiMaximal)
            {
                throw new InvalidOperationException(
                    $"Geocodage:DelaiSecondes doit être un entier de 1 à {DelaiMaximal} : configuré à moitié. "
                    + $"Par défaut : {DelaiParDefaut}.");
            }
        }

        return new ReglagesGeocodage(adresse, delai);
    }

    /// <summary>Enregistre le service et son client nommé. Appelée par <c>Program.cs</c>, et par la série sur une collection neuve.</summary>
    public static void Enregistrer(IServiceCollection services, IConfiguration configuration)
    {
        Lire(configuration);

        services.AddSingleton<ServiceGeocodage>();

        services.AddHttpClient(NomClient, (fournisseur, client) =>
            {
                var reglages = Lire(fournisseur.GetRequiredService<IConfiguration>());
                client.Timeout = TimeSpan.FromSeconds(reglages.DelaiSecondes);
                client.MaxResponseContentBufferSize = TailleMaximaleReponse;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("KLEAN");
            })
            .UseSocketsHttpHandler((gestionnaire, _) =>
            {
                gestionnaire.AllowAutoRedirect = false;
                gestionnaire.UseCookies = false;
                gestionnaire.ActivityHeadersPropagator = DistributedContextPropagator.CreateNoOutputPropagator();
                gestionnaire.AutomaticDecompression = DecompressionMethods.None;
            })
            // La garde principale du journal technique : un niveau Debug réglé par l'exploitant ne la
            // défait pas. Le niveau de appsettings.json en est la redondance (CLAUDE.md §10).
            .RemoveAllLoggers();
    }

    /// <summary>
    /// <paramref name="recherche"/> est déjà contrôlée par l'appelant : de 3 à 200 caractères, une
    /// lettre ou un chiffre en tête — les bornes mesurées du service. Le code postal n'est envoyé que
    /// s'il compte exactement cinq chiffres, sinon le service répondrait 400.
    /// </summary>
    public async Task<ResultatGeocodage> LocaliserAsync(string recherche, string? codePostal, CancellationToken annulation)
    {
        if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _reprise)) return new ResultatGeocodage.Injoignable();

        // ⚠️ Concaténation et encodage un par un, jamais une combinaison d'Uri : celle-ci perdrait le
        // dernier segment d'une base sans « / » final, et ferait changer d'hôte une partie relative en « // ».
        var url = new StringBuilder(_base)
            .Append("/search?q=").Append(Uri.EscapeDataString(recherche));

        if (codePostal is { Length: 5 } && codePostal.All(char.IsAsciiDigit))
        {
            url.Append("&postcode=").Append(codePostal);
        }

        url.Append("&limit=1&index=address");

        try
        {
            // GetAsync en mode par défaut : le corps est mis en tampon, donc le plafond de 64 Kio et le
            // délai couvrent tout le corps — y compris une réponse servie au compte-gouttes.
            using var reponse = await _fabrique.CreateClient(NomClient).GetAsync(url.ToString(), annulation);

            if (reponse.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Suspendre(reponse);
                return new ResultatGeocodage.Injoignable();
            }

            if (!reponse.IsSuccessStatusCode) return new ResultatGeocodage.Injoignable();

            return Analyser(await reponse.Content.ReadAsByteArrayAsync(annulation));
        }
        catch (OperationCanceledException) when (annulation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Dont le délai du client : une TaskCanceledException, mais avec un jeton de requête non annulé.
            return new ResultatGeocodage.Injoignable();
        }
    }

    /// <summary>Réarme le disjoncteur. Réservé à la série, qui partage une instance entre les tests.</summary>
    internal void Rearmer() => Interlocked.Exchange(ref _reprise, 0);

    private void Suspendre(HttpResponseMessage reponse)
    {
        var entete = reponse.Headers.RetryAfter;
        var pause = entete?.Delta
            ?? (entete?.Date is { } date ? date - DateTimeOffset.UtcNow : PauseParDefaut);

        // La Géoplateforme bloque 5 s : relancer avant prolongerait le blocage. Un Retry-After de 0 ou de 1
        // vaut donc la même pause que l'absence d'en-tête.
        if (pause < PauseParDefaut) pause = PauseParDefaut;
        if (pause > PauseMaximale) pause = PauseMaximale;

        var echeance = (DateTime.UtcNow + pause).Ticks;
        var actuelle = Interlocked.Read(ref _reprise);

        // Une pause ne se raccourcit pas : seule l'échéance la plus tardive est gardée.
        while (actuelle < echeance)
        {
            var lue = Interlocked.CompareExchange(ref _reprise, echeance, actuelle);
            if (lue == actuelle) break;
            actuelle = lue;
        }
    }

    private static ResultatGeocodage Analyser(byte[] corps)
    {
        using var document = JsonDocument.Parse(corps);
        var features = document.RootElement.GetProperty("features");

        if (features.ValueKind != JsonValueKind.Array) return new ResultatGeocodage.Injoignable();
        if (features.GetArrayLength() == 0) return new ResultatGeocodage.Introuvable();

        var premier = features[0];
        var coordonnees = premier.GetProperty("geometry").GetProperty("coordinates");

        // GeoJSON : [longitude, latitude].
        if (coordonnees.ValueKind != JsonValueKind.Array || coordonnees.GetArrayLength() < 2
            || coordonnees[0].ValueKind != JsonValueKind.Number || coordonnees[1].ValueKind != JsonValueKind.Number
            || !coordonnees[0].TryGetDouble(out var longitude) || !coordonnees[1].TryGetDouble(out var latitude)
            || !double.IsFinite(latitude) || latitude < -90 || latitude > 90
            || !double.IsFinite(longitude) || longitude < -180 || longitude > 180)
        {
            return new ResultatGeocodage.Injoignable();
        }

        var proprietes = premier.GetProperty("properties");

        var label = proprietes.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String
            ? Nettoyer(l.GetString()!)
            : "";

        if (label.Length == 0) return new ResultatGeocodage.Injoignable();

        // Un score hors de [0, 1], ou non fini, est traité comme absent : donc à vérifier.
        double? score = proprietes.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number
            && s.TryGetDouble(out var valeur) && double.IsFinite(valeur) && valeur >= 0 && valeur <= 1 ? valeur : null;

        var type = proprietes.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()
            : null;

        // Un score absent, une adresse placée au centre de sa rue ou de sa commune : à vérifier.
        var aVerifier = !(score >= SeuilScore) || type != "housenumber";

        return new ResultatGeocodage.Trouve(latitude, longitude, label, aVerifier);
    }

    /// <summary>
    /// Un texte d'un tiers, renvoyé à l'écran : une seule ligne, sans caractère de contrôle ni de mise en
    /// forme, et borné. Le filtre se fait par catégorie Unicode : <c>Control</c> et <c>Format</c> couvrent
    /// la bidirectionnalité — qui pourrait faire afficher une autre adresse que celle trouvée —, les
    /// espaces de largeur nulle, U+FEFF, U+00AD et U+2060 ; une liste écrite à la main serait redondante.
    /// Les retours à la ligne, les séparateurs de ligne ou de paragraphe et les espaces Unicode
    /// (<c>SpaceSeparator</c>, dont U+00A0) deviennent une espace.
    /// ⚠️ Le parcours se fait par <b>rune</b>, pas par <c>char</c> : un caractère de format hors du plan
    /// de base (les étiquettes U+E0000, par exemple) s'écrit en deux substituts, que la catégorie d'un
    /// <c>char</c> classe tous deux <c>Surrogate</c>, donc gardés.
    /// </summary>
    private static string Nettoyer(string brut)
    {
        var sb = new StringBuilder(brut.Length);

        foreach (var rune in brut.EnumerateRunes())
        {
            var categorie = Rune.GetUnicodeCategory(rune);

            if (rune.Value is '\t' or '\n' or '\r' or '\v' or '\f' or 0x85
                || categorie is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                    or UnicodeCategory.SpaceSeparator)
            {
                sb.Append(' ');
            }
            else if (categorie is not (UnicodeCategory.Control or UnicodeCategory.Format))
            {
                sb.Append(rune.ToString());
            }
        }

        // Les suites d'espaces — celles du texte, comme celles que les remplacements viennent de créer.
        var propre = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (propre.Length <= LongueurMaximaleAdresseTrouvee) return propre;

        // Jamais au milieu d'une paire de substituts : un demi-caractère s'afficherait en « � ».
        var longueur = char.IsHighSurrogate(propre[LongueurMaximaleAdresseTrouvee - 1])
            ? LongueurMaximaleAdresseTrouvee - 1
            : LongueurMaximaleAdresseTrouvee;

        return propre[..longueur].TrimEnd();
    }
}
