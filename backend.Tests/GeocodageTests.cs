using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Klean.Api.Services;
using Klean.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Klean.Api.Tests;

/// <summary>
/// <see cref="ServiceGeocodage"/> seul, sur une collection de services neuve : ni hôte ni base.
/// </summary>
public class GeocodageTests
{
    private const string Recherche = "20 avenue de Segur Paris";

    private static IConfiguration Configuration(Dictionary<string, string?>? reglages = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(reglages ?? []).Build();

    private static (ServiceGeocodage Service, GeocodageDeTest Faux) Construire(
        Dictionary<string, string?>? reglages = null, Action<ILoggingBuilder>? journal = null)
    {
        var configuration = Configuration(reglages);
        var faux = new GeocodageDeTest();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        if (journal is not null) services.AddLogging(journal);
        ServiceGeocodage.Enregistrer(services, configuration);
        services.AddHttpClient(ServiceGeocodage.NomClient).ConfigurePrimaryHttpMessageHandler(() => faux);
        return (services.BuildServiceProvider().GetRequiredService<ServiceGeocodage>(), faux);
    }

    // ------------------------------------------------------------- les issues

    [Fact]
    public async Task Une_adresse_trouvee_rend_latitude_puis_longitude_et_l_adresse_du_service()
    {
        var (service, _) = Construire();

        var trouve = Assert.IsType<ResultatGeocodage.Trouve>(await service.LocaliserAsync(Recherche, "75007", default));

        Assert.Equal(48.850699, trouve.Latitude);
        Assert.Equal(2.308628, trouve.Longitude);
        Assert.Equal("20 Avenue de Ségur 75007 Paris", trouve.AdresseTrouvee);
        Assert.False(trouve.AVerifier);
    }

    [Theory]
    [InlineData("0.5", "housenumber")]
    [InlineData("", "housenumber")]
    [InlineData("0.97", "street")]
    [InlineData("0.97", "municipality")]
    public async Task Un_score_bas_ou_absent_ou_un_point_qui_n_est_pas_un_numero_est_a_verifier(string score, string type)
    {
        var (service, faux) = Construire();
        faux.Repondre = (_, _) => Task.FromResult(GeocodageDeTest.Json(GeocodageDeTest.Trouve(score: score, type: type)));

        Assert.True(Assert.IsType<ResultatGeocodage.Trouve>(await service.LocaliserAsync(Recherche, null, default)).AVerifier);
    }

    [Fact]
    public async Task Aucune_adresse_trouvee_est_introuvable_et_non_une_coordonnee_nulle()
    {
        var (service, faux) = Construire();
        faux.Repondre = (_, _) => Task.FromResult(GeocodageDeTest.Json(GeocodageDeTest.Vide));

        Assert.IsType<ResultatGeocodage.Introuvable>(await service.LocaliserAsync(Recherche, null, default));
    }

    public static TheoryData<string, HttpStatusCode> ReponsesIllisibles => new()
    {
        { GeocodageDeTest.Trouve(latitude: 91), HttpStatusCode.OK },
        { GeocodageDeTest.Trouve(longitude: -181), HttpStatusCode.OK },
        { """{"features":[{"geometry":{"coordinates":[2.3]},"properties":{"label":"x"}}]}""", HttpStatusCode.OK },
        { """{"features":[{"geometry":{"coordinates":["2.3","48.8"]},"properties":{"label":"x"}}]}""", HttpStatusCode.OK },
        { """{"features":[{"geometry":{"coordinates":[2.3,48.8]},"properties":{}}]}""", HttpStatusCode.OK },
        { "pas du JSON", HttpStatusCode.OK },
        { GeocodageDeTest.Trouve(), HttpStatusCode.Found },
        { GeocodageDeTest.Trouve(), HttpStatusCode.BadRequest },
        { GeocodageDeTest.Trouve(), HttpStatusCode.NotFound },
        { GeocodageDeTest.Trouve(), HttpStatusCode.InternalServerError },
    };

    [Theory]
    [MemberData(nameof(ReponsesIllisibles))]
    public async Task Une_reponse_illisible_hors_bornes_ou_hors_2xx_est_injoignable(string corps, HttpStatusCode statut)
    {
        var (service, faux) = Construire();
        faux.Repondre = (_, _) => Task.FromResult(GeocodageDeTest.Json(corps, statut));

        Assert.IsType<ResultatGeocodage.Injoignable>(await service.LocaliserAsync(Recherche, null, default));
    }

    [Fact]
    public async Task Un_service_injoignable_est_injoignable()
    {
        var (service, faux) = Construire();
        faux.Repondre = (_, _) => throw new HttpRequestException("Connexion refusée");

        Assert.IsType<ResultatGeocodage.Injoignable>(await service.LocaliserAsync(Recherche, null, default));
    }

    [Fact]
    public async Task Une_reponse_qui_ne_vient_pas_est_injoignable_dans_le_delai()
    {
        var (service, faux) = Construire(new() { ["Geocodage:DelaiSecondes"] = "1" });
        faux.Repondre = async (_, annulation) =>
        {
            await Task.Delay(Timeout.Infinite, annulation);
            throw new InvalidOperationException("inatteignable");
        };

        var chrono = Stopwatch.StartNew();
        Assert.IsType<ResultatGeocodage.Injoignable>(await service.LocaliserAsync(Recherche, null, default));
        Assert.True(chrono.Elapsed < TimeSpan.FromSeconds(5));
    }

    /// <summary>En-têtes tout de suite, puis un corps qui ne finit pas : le délai doit couvrir le corps.</summary>
    [Fact]
    public async Task Un_corps_servi_au_compte_gouttes_est_injoignable_dans_le_delai()
    {
        var (service, faux) = Construire(new() { ["Geocodage:DelaiSecondes"] = "1" });
        faux.Repondre = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new FluxSansFin())
        });

        var chrono = Stopwatch.StartNew();
        Assert.IsType<ResultatGeocodage.Injoignable>(await service.LocaliserAsync(Recherche, null, default));
        Assert.True(chrono.Elapsed < TimeSpan.FromSeconds(5));
    }

    /// <summary>Sans le plafond, le libellé serait tronqué et la réponse rendue « trouvée » : le test distingue.</summary>
    [Fact]
    public async Task Une_reponse_de_plus_de_64_Kio_est_injoignable()
    {
        var (service, faux) = Construire();
        faux.Repondre = (_, _) => Task.FromResult(GeocodageDeTest.Json(GeocodageDeTest.Trouve(label: new string('a', 70_000))));

        Assert.IsType<ResultatGeocodage.Injoignable>(await service.LocaliserAsync(Recherche, null, default));
    }

    /// <summary>
    /// Le faux lit le jeton, comme le ferait le réseau : rien d'autre dans la chaîne ne le lit pour un
    /// corps déjà en mémoire. Sabotage : le <c>catch … when</c> retiré, l'annulation est rendue injoignable.
    /// </summary>
    [Fact]
    public async Task L_annulation_de_la_requete_remonte_au_lieu_d_etre_rendue_injoignable()
    {
        var (service, faux) = Construire();
        faux.Repondre = async (_, annulation) =>
        {
            await Task.Delay(Timeout.Infinite, annulation);
            throw new InvalidOperationException("inatteignable");
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.LocaliserAsync(Recherche, null, new CancellationToken(true)));
    }

    [Fact]
    public async Task L_adresse_trouvee_perd_ses_caracteres_de_bidirectionnalite_et_reste_bornee()
    {
        var (service, faux) = Construire();
        faux.Repondre = (_, _) => Task.FromResult(GeocodageDeTest.Json(
            GeocodageDeTest.Trouve(label: "20 Avenue\u202E de Ségur\u200F " + new string('x', 300))));

        var trouve = Assert.IsType<ResultatGeocodage.Trouve>(await service.LocaliserAsync(Recherche, null, default));

        Assert.DoesNotContain('\u202E', trouve.AdresseTrouvee);
        Assert.DoesNotContain('\u200F', trouve.AdresseTrouvee);
        Assert.StartsWith("20 Avenue de Ségur", trouve.AdresseTrouvee);
        Assert.Equal(ServiceGeocodage.LongueurMaximaleAdresseTrouvee, trouve.AdresseTrouvee.Length);
    }

    private static async Task<ResultatGeocodage> LocaliserLibelleAsync(string label)
    {
        var (service, faux) = Construire();
        faux.Repondre = (_, _) => Task.FromResult(GeocodageDeTest.Json(GeocodageDeTest.Trouve(label: label)));
        return await service.LocaliserAsync(Recherche, null, default);
    }

    [Theory]
    [InlineData("5", true)]
    [InlineData("-0.1", true)]
    [InlineData("1", false)]
    public async Task Un_score_hors_de_0_a_1_est_traite_comme_absent(string score, bool aVerifier)
    {
        var (service, faux) = Construire();
        faux.Repondre = (_, _) => Task.FromResult(GeocodageDeTest.Json(GeocodageDeTest.Trouve(score: score, type: "housenumber")));

        Assert.Equal(aVerifier, Assert.IsType<ResultatGeocodage.Trouve>(await service.LocaliserAsync(Recherche, null, default)).AVerifier);
    }

    /// <summary>U+E0041 est hors du plan de base : parcouru par <c>char</c>, chaque moitié de la paire serait un substitut, donc gardée.</summary>
    [Fact]
    public async Task Un_caractere_de_format_hors_du_plan_de_base_est_retire()
    {
        var trouve = Assert.IsType<ResultatGeocodage.Trouve>(await LocaliserLibelleAsync("Paris\U000E0041"));

        Assert.Equal("Paris", trouve.AdresseTrouvee);
    }

    [Fact]
    public async Task Un_libelle_fait_uniquement_de_caracteres_invisibles_est_injoignable()
    {
        Assert.IsType<ResultatGeocodage.Injoignable>(await LocaliserLibelleAsync("\u200B\uFEFF"));
    }

    [Fact]
    public async Task Les_retours_a_la_ligne_et_les_suites_d_espaces_deviennent_une_seule_espace()
    {
        var trouve = Assert.IsType<ResultatGeocodage.Trouve>(await LocaliserLibelleAsync("Rue de\nla Paix   75002"));

        Assert.Equal("Rue de la Paix 75002", trouve.AdresseTrouvee);
    }

    [Fact]
    public async Task Une_suite_d_espaces_unicode_devient_une_seule_espace_ASCII()
    {
        var trouve = Assert.IsType<ResultatGeocodage.Trouve>(await LocaliserLibelleAsync("Rue\u00A0 \u2003de la Paix"));

        Assert.Equal("Rue de la Paix", trouve.AdresseTrouvee);
    }

    [Fact]
    public async Task La_troncature_ne_coupe_pas_une_paire_de_substituts()
    {
        var trouve = Assert.IsType<ResultatGeocodage.Trouve>(
            await LocaliserLibelleAsync(new string('a', 199) + "\U0001F600"));

        Assert.Equal(199, trouve.AdresseTrouvee.Length);
        Assert.DoesNotContain(trouve.AdresseTrouvee, char.IsSurrogate);
    }

    // ------------------------------------------------------------- le disjoncteur

    [Fact]
    public async Task Apres_un_429_aucun_appel_ne_part_jusqu_a_l_echeance()
    {
        var (service, faux) = Construire();
        faux.Repondre = (_, _) =>
        {
            var reponse = GeocodageDeTest.Json("{}", HttpStatusCode.TooManyRequests);
            reponse.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return Task.FromResult(reponse);
        };

        Assert.IsType<ResultatGeocodage.Injoignable>(await service.LocaliserAsync(Recherche, null, default));
        Assert.IsType<ResultatGeocodage.Injoignable>(await service.LocaliserAsync(Recherche, null, default));

        Assert.Single(faux.Requetes);

        service.Rearmer();
        faux.Reinitialiser();
        Assert.IsType<ResultatGeocodage.Trouve>(await service.LocaliserAsync(Recherche, null, default));
    }

    /// <summary>La borne basse : un <c>Retry-After</c> de 1 vaut 5 s, car la Géoplateforme bloque 5 s.</summary>
    [Fact]
    public async Task Un_Retry_After_de_1_seconde_garde_quand_meme_la_pause_de_5_secondes()
    {
        var (service, faux) = Construire();
        faux.Repondre = (_, _) =>
        {
            var reponse = GeocodageDeTest.Json("{}", HttpStatusCode.TooManyRequests);
            reponse.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
            return Task.FromResult(reponse);
        };

        Assert.IsType<ResultatGeocodage.Injoignable>(await service.LocaliserAsync(Recherche, null, default));
        Assert.IsType<ResultatGeocodage.Injoignable>(await service.LocaliserAsync(Recherche, null, default));
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        Assert.IsType<ResultatGeocodage.Injoignable>(await service.LocaliserAsync(Recherche, null, default));

        Assert.Single(faux.Requetes);
    }

    // ------------------------------------------------------------- la requête sortante

    [Theory]
    [InlineData("12 rue & co #? %+ //hote é Paris", "1&limit=50")]
    [InlineData("3 place du Marché Lyon", "69001")]
    public async Task La_requete_ne_porte_que_q_limit_index_et_un_code_postal_a_cinq_chiffres(string recherche, string codePostal)
    {
        var (service, faux) = Construire();

        await service.LocaliserAsync(recherche, codePostal, default);

        var vue = Assert.Single(faux.Requetes);
        Assert.Equal("https", vue.Uri.Scheme);
        Assert.Equal("data.geopf.fr", vue.Uri.Host);
        Assert.Equal("/geocodage/search", vue.Uri.AbsolutePath);

        var parametres = QueryHelpers.ParseQuery(vue.Uri.Query);
        var attendus = codePostal == "69001"
            ? new[] { "index", "limit", "postcode", "q" }
            : new[] { "index", "limit", "q" };
        Assert.Equal(attendus, parametres.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(recherche, parametres["q"].ToString());
        Assert.Equal("1", parametres["limit"].ToString());
        Assert.Equal("address", parametres["index"].ToString());
        if (codePostal == "69001") Assert.Equal("69001", parametres["postcode"].ToString());
    }

    [Fact]
    public async Task La_requete_ne_porte_que_le_User_Agent_KLEAN()
    {
        var (service, faux) = Construire();

        await service.LocaliserAsync(Recherche, null, default);

        var vue = Assert.Single(faux.Requetes);
        Assert.Equal(["User-Agent"], vue.Entetes.Keys.ToArray());
        Assert.Equal("KLEAN", vue.Entetes["User-Agent"]);
    }

    [Fact]
    public async Task Une_base_sans_barre_finale_garde_son_dernier_segment()
    {
        var (service, faux) = Construire(new() { ["Geocodage:Adresse"] = "https://geocodage.test/racine/geocodage" });

        await service.LocaliserAsync(Recherche, null, default);

        Assert.Equal("/racine/geocodage/search", Assert.Single(faux.Requetes).Uri.AbsolutePath);
    }

    // ------------------------------------------------------------- la configuration réelle du client

    /// <summary>
    /// Le seul test qui prouve les quatre verrous : le faux gestionnaire ne suit rien, ne garde aucun
    /// cookie et n'ajoute aucun en-tête de lui-même. Un sabotage par ligne de <c>UseSocketsHttpHandler</c>.
    /// </summary>
    [Fact]
    public void Le_gestionnaire_reel_ne_suit_pas_les_redirections_ne_garde_aucun_cookie_et_ne_propage_rien()
    {
        var configuration = Configuration();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        ServiceGeocodage.Enregistrer(services, configuration);

        HttpMessageHandler gestionnaire = services.BuildServiceProvider()
            .GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(ServiceGeocodage.NomClient);
        while (gestionnaire is DelegatingHandler intermediaire) gestionnaire = intermediaire.InnerHandler!;

        var primaire = Assert.IsType<SocketsHttpHandler>(gestionnaire);
        Assert.False(primaire.AllowAutoRedirect);
        Assert.False(primaire.UseCookies);
        Assert.Equal(DecompressionMethods.None, primaire.AutomaticDecompression);

        using var activite = new Activity("essai").SetIdFormat(ActivityIdFormat.W3C).Start();
        var porteur = new Dictionary<string, string>();
        primaire.ActivityHeadersPropagator?.Inject(activite, porteur,
            (support, cle, valeur) => ((Dictionary<string, string>)support!)[cle] = valeur);
        Assert.Empty(porteur);
    }

    // ------------------------------------------------------------- le démarrage

    [Theory]
    [InlineData("http://data.geopf.fr/geocodage")]
    [InlineData("/geocodage")]
    [InlineData("pas une adresse")]
    [InlineData("https://moi:secret@data.geopf.fr/geocodage")]
    [InlineData("https://data.geopf.fr/geocodage?x=1")]
    [InlineData("https://data.geopf.fr/geocodage#fin")]
    public void Une_adresse_de_service_invalide_fait_refuser_le_demarrage(string adresse)
    {
        var erreur = Assert.Throws<InvalidOperationException>(
            () => ServiceGeocodage.Lire(Configuration(new() { ["Geocodage:Adresse"] = adresse })));

        Assert.Contains("Geocodage:Adresse", erreur.Message);
        Assert.DoesNotContain("secret", erreur.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("21")]
    [InlineData("-3")]
    [InlineData("abc")]
    [InlineData("5.5")]
    public void Un_delai_hors_de_1_a_20_fait_refuser_le_demarrage(string delai)
    {
        var erreur = Assert.Throws<InvalidOperationException>(
            () => ServiceGeocodage.Lire(Configuration(new() { ["Geocodage:DelaiSecondes"] = delai })));

        Assert.Contains("Geocodage:DelaiSecondes", erreur.Message);
    }

    [Fact]
    public void Sans_reglage_le_service_vise_la_Geoplateforme_en_cinq_secondes()
    {
        var reglages = ServiceGeocodage.Lire(Configuration());

        Assert.Equal(new Uri(ServiceGeocodage.AdresseParDefaut), reglages.Adresse);
        Assert.Equal(5, reglages.DelaiSecondes);
    }

    /// <summary>Sans ce test, retirer l'appel à <c>Lire</c> dans <c>Enregistrer</c> laisserait la série verte.</summary>
    [Fact]
    public void Enregistrer_refuse_une_configuration_invalide()
    {
        var configuration = Configuration(new() { ["Geocodage:Adresse"] = "http://data.geopf.fr/geocodage" });

        Assert.Throws<InvalidOperationException>(() => ServiceGeocodage.Enregistrer(new ServiceCollection(), configuration));
    }

    // ------------------------------------------------------------- le journal technique

    /// <summary>
    /// La garde principale, éprouvée seule : la règle propre au fournisseur de capture l'emporte sur le
    /// <c>Warning</c> de <c>appsettings.json</c>. Sabotage : <c>RemoveAllLoggers()</c> retiré, le test
    /// rougit — .NET masque déjà la requête dans ses traces, d'où la recherche de <c>geocodage/search?</c>.
    /// </summary>
    [Fact]
    public async Task Le_client_n_ecrit_ni_l_adresse_ni_l_URL_dans_aucun_journal()
    {
        const string Jeton = "Zqxwvjeton";
        var capture = new FournisseurCapture();
        var (service, _) = Construire(journal: journal => journal
            .AddProvider(capture)
            .AddFilter<FournisseurCapture>(null, LogLevel.Trace));

        await service.LocaliserAsync($"12 rue {Jeton} Paris", null, default);

        var lignes = capture.Lignes.ToArray();
        Assert.DoesNotContain(lignes, ligne => ligne.Contains(Jeton, StringComparison.Ordinal));
        Assert.DoesNotContain(lignes, ligne => ligne.Contains("geocodage/search?", StringComparison.Ordinal));
    }

    [Fact]
    public void Appsettings_regle_le_client_HTTP_a_Warning_et_le_developpement_ne_le_redefinit_pas()
    {
        var racine = new DirectoryInfo(AppContext.BaseDirectory);
        while (racine is not null && !File.Exists(Path.Combine(racine.FullName, "backend", "appsettings.json"))) racine = racine.Parent;
        Assert.NotNull(racine);

        JsonElement Niveaux(string fichier) => JsonDocument
            .Parse(File.ReadAllText(Path.Combine(racine!.FullName, "backend", fichier)))
            .RootElement.GetProperty("Logging").GetProperty("LogLevel");

        Assert.Equal("Warning", Niveaux("appsettings.json").GetProperty("System.Net.Http.HttpClient").GetString());
        Assert.DoesNotContain(Niveaux("appsettings.Development.json").EnumerateObject(),
            p => p.Name.StartsWith("System.Net.Http", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------- aides

    private sealed class FluxSansFin : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken annulation = default)
        {
            await Task.Delay(Timeout.Infinite, annulation);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken annulation) =>
            ReadAsync(buffer.AsMemory(offset, count), annulation).AsTask();
    }

    /// <summary>Messages et portées, sans <c>ISupportExternalScope</c> : la fabrique de journaux appelle alors <c>BeginScope</c> du fournisseur.</summary>
    private sealed class FournisseurCapture : ILoggerProvider
    {
        public ConcurrentQueue<string> Lignes { get; } = new();

        public ILogger CreateLogger(string categorie) => new Journal(this, categorie);

        public void Dispose() { }

        private sealed class Journal(FournisseurCapture fournisseur, string categorie) : ILogger
        {
            public IDisposable BeginScope<TState>(TState etat) where TState : notnull
            {
                fournisseur.Lignes.Enqueue($"{categorie} portée {etat}");
                return Fin.Instance;
            }

            public bool IsEnabled(LogLevel niveau) => true;

            public void Log<TState>(LogLevel niveau, EventId evenement, TState etat, Exception? exception,
                Func<TState, Exception?, string> formateur) =>
                fournisseur.Lignes.Enqueue($"{categorie} {formateur(etat, exception)}");
        }

        private sealed class Fin : IDisposable
        {
            public static readonly Fin Instance = new();
            public void Dispose() { }
        }
    }
}
