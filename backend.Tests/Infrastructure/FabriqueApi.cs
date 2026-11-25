using Klean.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Klean.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Tests.Infrastructure;

/// <summary>
/// Démarre l'API en mémoire contre la base <c>klean_test</c>.
/// </summary>
public class FabriqueApi : WebApplicationFactory<Program>
{
    /// <summary>
    /// Chaîne de la base de test. Surchargeable par la variable
    /// d'environnement <c>KLEAN_TEST_CONNECTION</c>, sans quoi l'intégration
    /// continue devrait connaître le nom d'utilisateur du poste de développement.
    /// </summary>
    public static string ChaineTest =>
        Environment.GetEnvironmentVariable("KLEAN_TEST_CONNECTION")
        ?? $"Host=localhost;Port=5432;Database=klean_test;Username={Environment.UserName}";

    /// <summary>Clé de signature propre aux tests : jamais celle du développeur.</summary>
    private const string CleTest =
        "cle-de-signature-reservee-aux-tests-automatises-48-octets-minimum";

    /// <summary>Tiré à chaque exécution : aucune valeur littérale d'un code ne circule dans le dépôt.</summary>
    public static readonly string CodeDInstallation = CodeInstallation.Tirer();

    /// <summary>
    /// ⚠️ Les valeurs de test passent par des **variables d'environnement**, et
    /// non par <c>ConfigureAppConfiguration</c>. Avec le modèle d'hébergement
    /// minimal, les instructions de plus haut niveau de <c>Program.cs</c>
    /// s'exécutent — donc lisent <c>ConnectionStrings:Klean</c> et
    /// <c>Jwt:Cle</c> — **avant** que les délégués de la fabrique ne
    /// s'appliquent. Un écrasement par <c>AddInMemoryCollection</c> arrive trop
    /// tard, échoue en silence, et la série vise la base de développement.
    ///
    /// Le constructeur statique s'exécute avant toute création d'hôte, l'hôte
    /// étant construit paresseusement au premier accès à <see cref="Services"/>.
    /// </summary>
    static FabriqueApi()
    {
        static void Poser(string cle, string valeur) =>
            Environment.SetEnvironmentVariable(cle, valeur);

        Poser("ConnectionStrings__Klean", ChaineTest);
        Poser("Jwt__Cle", CleTest);
        Poser("Jwt__Emetteur", "klean-test");
        Poser("Jwt__Audience", "klean-test");
        Poser("Amorcage__EmailAdmin", "amorcage@test.local");
        Poser("Amorcage__MotDePasseAdmin", "AmorcageTest2026");
        Poser("Amorcage__RaisonSocialeEntreprise", "Amorçage Test");
        Poser("Amorcage__SiretEntreprise", "00000000000001");
        // La base de la série a toujours des comptes : il ne sert qu'aux tests qui la vident.
        Poser("Installation__Code", CodeDInstallation);

        // Surface HTTP (étape 16). Une seule origine déclarée : il en faut une
        // pour prouver qu'elle passe, et son unicité prouve que les autres non.
        Poser("Cors__Origines__0", OrigineDeclaree);

        // Port de destination de la redirection. Explicite, parce que TestServer
        // n'a aucune adresse d'écoute dont le déduire — sans cette valeur
        // UseHttpsRedirection laisse passer en clair avec un simple
        // avertissement, et le test de redirection passerait au vert à l'envers.
        Poser("Http__PortHttps", "443");

        // Proxy inverse (étape 18). Un réseau déclaré, pour prouver que ce qui en
        // vient est cru et que ce qui n'en vient pas ne l'est pas.
        Poser("Proxy__Reseaux__0", ReseauProxy);
    }

    /// <summary>Seul réseau dont la série croit les en-têtes X-Forwarded-*.</summary>
    public const string ReseauProxy = "10.0.0.0/8";

    /// <summary>Seule origine que la politique CORS de la série autorise.</summary>
    public const string OrigineDeclaree = "https://klean-declare.test";

    /// <summary>Origine qui ne figure nulle part dans la configuration.</summary>
    public const string OrigineInconnue = "https://klean-pirate.test";

    /// <summary>
    /// Courriels retenus au lieu d'être envoyés. Partagé par toute la série, remis
    /// à zéro par la fixture entre chaque test.
    /// </summary>
    public ServiceCourrielDeTest Courriels { get; } = new();

    /// <summary>Horloge de la génération. Libérée par la fixture entre chaque test.</summary>
    public HorlogeDeTest Horloge { get; } = new();

    /// <summary>Rôle et entreprise des connexions, relevés sous chaque commande. Inactive par défaut.</summary>
    public SondeInstallation Sonde { get; } = new();

    /// <summary>Le gestionnaire primaire du client de géocodage. Aucun appel ne sort de la série.</summary>
    public GeocodageDeTest Geocodage { get; } = new();

    private readonly string _environnement;

    /// <summary>
    /// <paramref name="environnement"/> n'est pas décoratif : trois comportements
    /// de l'étape 16 — HSTS, redirection HTTPS, <c>RequireHttpsMetadata</c> — sont
    /// <b>désactivés</b> en développement. Les éprouver demande un hôte qui n'y est
    /// pas, et les éprouver sur l'hôte de développement les verrait toujours
    /// absents, donc toujours au vert à l'envers.
    /// </summary>
    public FabriqueApi(string environnement = "Development") => _environnement = environnement;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environnement);

        // ⚠️ Ici, et pas dans le constructeur statique : la substitution de service
        // fonctionne bien par ConfigureServices, contrairement à la configuration —
        // les services sont construits après que ces délégués se sont appliqués.
        // C'est la configuration, lue par les instructions de haut niveau de
        // Program.cs, qui doit passer par des variables d'environnement.
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IServiceCourriel>();
            services.AddSingleton<IServiceCourriel>(Courriels);

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Horloge);

            // Relève le rôle et l'entreprise de la connexion sous les requêtes de l'assistant
            // (étape 34, lot A2). S'ajoute à BasculeRoleApplicatif sans le remplacer (EF Core 10).
            services.ConfigureDbContext<KleanDbContext>(options => options.AddInterceptors(Sonde));

            // Le gestionnaire primaire, et non un gestionnaire ajouté : toute la chaîne du client s'exécute (0.3.0).
            services.AddHttpClient(ServiceGeocodage.NomClient).ConfigurePrimaryHttpMessageHandler(() => Geocodage);
        });
    }

    /// <summary>
    /// Client de test parlant <c>https</c>.
    ///
    /// ⚠️ Le schéma n'est pas décoratif. Les cookies de session portent
    /// <c>Secure</c> — sans condition, y compris en développement — et
    /// <c>CookieContainer</c> respecte cet attribut : en <c>http</c> il stocke les
    /// cookies mais ne les renvoie jamais, et toute la série échoue en 401 sans
    /// que rien n'indique pourquoi. <c>TestServer</c> ignore le transport, le
    /// schéma ne coûte donc rien d'autre que d'être écrit.
    /// </summary>
    public HttpClient CreerClient(bool suivreRedirections = true, bool gererCookies = true) =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost/"),
            AllowAutoRedirect = suivreRedirections,
            HandleCookies = gererCookies
        });

    /// <summary>
    /// Client parlant <c>http</c>, pour la seule chose qui l'exige : prouver qu'une
    /// requête en clair est redirigée.
    ///
    /// ⚠️ Ne jamais s'en servir pour autre chose. <c>CookieContainer</c> respecte
    /// l'attribut <c>Secure</c> : sur une adresse de base en <c>http</c> il stocke
    /// les cookies et ne les renvoie jamais, et toute la série échouerait en 401
    /// sans que rien n'indique pourquoi (CLAUDE.md §10). D'où la gestion des
    /// cookies désactivée ici — il n'y a rien à gagner à la laisser en place.
    /// </summary>
    public HttpClient CreerClientHttp() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost/"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });

    /// <summary>
    /// Vérifie que la configuration effective pointe bien vers la base de test.
    /// Appelé par la fixture avant toute écriture : une garde, pas une assertion
    /// décorative.
    /// </summary>
    public void VerifierCiblageBaseDeTest()
    {
        using var portee = Services.CreateScope();
        var db = portee.ServiceProvider.GetRequiredService<KleanDbContext>();
        var base_ = db.Database.GetDbConnection().Database;

        if (base_ != "klean_test")
        {
            throw new InvalidOperationException(
                $"La série de tests vise la base « {base_} » au lieu de « klean_test ». "
                + "Arrêt immédiat : continuer effacerait des données de développement.");
        }
    }
}
