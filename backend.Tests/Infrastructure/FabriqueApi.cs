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
    }

    /// <summary>
    /// Courriels retenus au lieu d'être envoyés. Partagé par toute la série, remis
    /// à zéro par la fixture entre chaque test.
    /// </summary>
    public ServiceCourrielDeTest Courriels { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // ⚠️ Ici, et pas dans le constructeur statique : la substitution de service
        // fonctionne bien par ConfigureServices, contrairement à la configuration —
        // les services sont construits après que ces délégués se sont appliqués.
        // C'est la configuration, lue par les instructions de haut niveau de
        // Program.cs, qui doit passer par des variables d'environnement.
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IServiceCourriel>();
            services.AddSingleton<IServiceCourriel>(Courriels);
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
