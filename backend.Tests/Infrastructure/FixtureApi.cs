using System.Net.Http.Json;
using Klean.Api.Data;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Klean.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Klean.Api.Tests.Infrastructure;

/// <summary>
/// Partagée par toute la série. Migre la base de test une fois, puis fournit
/// une réinitialisation entre chaque test.
///
/// Deux entreprises, et c'est le point essentiel : tout test d'isolation a
/// besoin d'un tenant témoin. Une seule entreprise ne prouve rien.
/// </summary>
public class FixtureApi : IAsyncLifetime
{
    public const string MotDePasse = "MotDePasseTest2026";

    public FabriqueApi Fabrique { get; private set; } = null!;

    public Guid EntrepriseAlpha { get; private set; }
    public Guid EntrepriseBeta { get; private set; }

    public const string AdminAlpha = "admin.alpha@test.local";
    public const string AdminBeta = "admin.beta@test.local";

    public async Task InitializeAsync()
    {
        // La migration passe par un contexte autonome, avant que l'application
        // ne démarre : AmorcageService s'exécute au démarrage et échouerait sur
        // une base sans tables.
        var options = new DbContextOptionsBuilder<KleanDbContext>()
            .UseNpgsql(FabriqueApi.ChaineTest)
            .Options;

        await using (var db = new KleanDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        Fabrique = new FabriqueApi();

        // Garde : la configuration effective de l'application doit viser la base
        // de test, pas celle de développement. Vérifiée avant toute écriture.
        Fabrique.VerifierCiblageBaseDeTest();

        await ReinitialiserAsync();
    }

    public Task DisposeAsync()
    {
        Fabrique.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Vide les tables métier et les comptes, puis recrée les deux entreprises
    /// et leur administrateur. Les cinq rôles sont conservés : ils sont créés
    /// par l'amorçage au démarrage et ne changent jamais.
    /// </summary>
    public async Task ReinitialiserAsync()
    {
        using var portee = Fabrique.Services.CreateScope();
        var db = portee.ServiceProvider.GetRequiredService<KleanDbContext>();

        await db.Database.ExecuteSqlRawAsync("""
            TRUNCATE TABLE
                "Remplacements", "Absences", "Interventions", "Prestations",
                "Contrats", "Sites", "Clients", "Stocks", "Produits",
                "Agents", "Entreprises",
                "JetonsRafraichissement",
                "AspNetUserRoles", "AspNetUserClaims", "AspNetUserLogins",
                "AspNetUserTokens", "AspNetRoleClaims", "AspNetUsers"
            RESTART IDENTITY CASCADE;
            """);

        EntrepriseAlpha = await CreerEntrepriseEtAdmin(
            portee.ServiceProvider, "Alpha Propreté", "10000000000001", AdminAlpha);

        EntrepriseBeta = await CreerEntrepriseEtAdmin(
            portee.ServiceProvider, "Beta Nettoyage", "20000000000002", AdminBeta);
    }

    private static async Task<Guid> CreerEntrepriseEtAdmin(
        IServiceProvider services, string raisonSociale, string siret, string email)
    {
        var db = services.GetRequiredService<KleanDbContext>();
        var utilisateurs = services.GetRequiredService<UserManager<Utilisateur>>();

        var entreprise = new Entreprise
        {
            RaisonSociale = raisonSociale,
            Siret = siret,
            DateCreation = DateTime.UtcNow
        };

        db.Entreprises.Add(entreprise);
        await db.SaveChangesAsync();

        var admin = new Utilisateur
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            EntrepriseId = entreprise.Id,
            Nom = "Admin",
            Prenom = raisonSociale,
            DateCreation = DateTime.UtcNow,
            Actif = true
        };

        var creation = await utilisateurs.CreateAsync(admin, MotDePasse);
        if (!creation.Succeeded)
        {
            throw new InvalidOperationException(
                "Amorçage de fixture impossible : "
                + string.Join(" ; ", creation.Errors.Select(e => e.Description)));
        }

        await utilisateurs.AddToRoleAsync(admin, RolesKlean.Administrateur);
        return entreprise.Id;
    }

    /// <summary>
    /// Client HTTP porteur d'une session valide pour le compte demandé.
    ///
    /// Depuis l'étape 12, plus aucun jeton ne traverse le corps de la réponse : la
    /// session vit dans trois cookies, que <c>CookieContainer</c> renvoie tout
    /// seul. Reste à faire ce que fera le frontend — recopier le cookie anti-CSRF
    /// dans un en-tête, puisque le navigateur ne le fait pas de lui-même.
    /// </summary>
    public async Task<HttpClient> ClientConnecteAsync(string email)
    {
        var client = Fabrique.CreerClient();

        var reponse = await client.PostAsJsonAsync("/api/auth/connexion",
            new ConnexionDto { Email = email, MotDePasse = MotDePasse });

        reponse.EnsureSuccessStatusCode();

        ArmerCsrf(client, reponse);

        return client;
    }

    /// <summary>
    /// Recopie le cookie anti-CSRF de la réponse dans l'en-tête par défaut du
    /// client. Rejoue exactement ce que le frontend devra faire.
    /// </summary>
    public static void ArmerCsrf(HttpClient client, HttpResponseMessage reponse)
    {
        var valeur = ValeurDuCookie(reponse, ServiceSession.CookieCsrf)
            ?? throw new InvalidOperationException(
                $"La réponse ne pose pas le cookie « {ServiceSession.CookieCsrf} ».");

        client.DefaultRequestHeaders.Remove(ServiceSession.EnteteCsrf);
        client.DefaultRequestHeaders.Add(ServiceSession.EnteteCsrf, valeur);
    }

    /// <summary>Lit une valeur de cookie dans les en-têtes <c>Set-Cookie</c>.</summary>
    public static string? ValeurDuCookie(HttpResponseMessage reponse, string nom)
    {
        if (!reponse.Headers.TryGetValues("Set-Cookie", out var entetes)) return null;

        foreach (var entete in entetes)
        {
            var debut = $"{nom}=";
            if (!entete.StartsWith(debut, StringComparison.Ordinal)) continue;

            var valeur = entete[debut.Length..].Split(';')[0];

            // Un cookie effacé revient vide : ce n'est pas une valeur utilisable.
            return string.IsNullOrEmpty(valeur) ? null : valeur;
        }

        return null;
    }

    /// <summary>
    /// Crée un compte du rôle demandé via l'API — donc en passant par les mêmes
    /// contrôles qu'en production — puis rend un client connecté dessus.
    /// </summary>
    public async Task<HttpClient> ClientDeRoleAsync(
        HttpClient administrateur, string role, string email,
        Guid? agentId = null, Guid? clientId = null)
    {
        var creation = await administrateur.PostAsJsonAsync("/api/auth/comptes",
            new CompteCreateDto
            {
                Email = email,
                MotDePasse = MotDePasse,
                Nom = role,
                Role = role,
                AgentId = agentId,
                ClientId = clientId
            });

        if (!creation.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Création du compte {role} impossible : "
                + await creation.Content.ReadAsStringAsync());
        }

        return await ClientConnecteAsync(email);
    }

    public HttpClient ClientAnonyme() => Fabrique.CreerClient();

    /// <summary>
    /// Client sans gestion automatique des cookies : l'en-tête <c>Cookie</c> est
    /// posé à la main. Indispensable pour rejouer un jeton révoqué ou en altérer
    /// un — ce que <c>CookieContainer</c> ne permet pas, puisqu'il ne rend jamais
    /// une valeur qu'il a remplacée.
    /// </summary>
    public HttpClient ClientSansCookies() => Fabrique.CreerClient(gererCookies: false);

    /// <summary>
    /// En-tête <c>Set-Cookie</c> complet, attributs compris. Sert à vérifier
    /// <c>HttpOnly</c>, <c>Secure</c> et <c>SameSite</c>, qui ne sont pas des
    /// détails : ce sont eux qui font la différence entre un cookie de session et
    /// un jeton que la première XSS emporte.
    /// </summary>
    public static string? EnteteDuCookie(HttpResponseMessage reponse, string nom)
    {
        if (!reponse.Headers.TryGetValues("Set-Cookie", out var entetes)) return null;

        return entetes.FirstOrDefault(e => e.StartsWith($"{nom}=", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ouvre une portée de services de l'application pour manipuler la base
    /// directement. Réservé à ce que l'API n'expose pas : désactiver un compte,
    /// vieillir une session. Aucun test métier ne doit passer par là — il
    /// contournerait précisément ce qu'il prétend vérifier.
    /// </summary>
    public async Task AvecServicesAsync(Func<IServiceProvider, Task> action)
    {
        using var portee = Fabrique.Services.CreateScope();
        await action(portee.ServiceProvider);
    }

    /// <summary>Désactive un compte en base : aucun endpoint ne le fait encore.</summary>
    public async Task DesactiverCompteAsync(string email)
    {
        await AvecServicesAsync(async services =>
        {
            var utilisateurs = services.GetRequiredService<UserManager<Utilisateur>>();
            var utilisateur = await utilisateurs.FindByEmailAsync(email)
                ?? throw new InvalidOperationException($"Compte {email} introuvable.");

            utilisateur.Actif = false;
            await utilisateurs.UpdateAsync(utilisateur);
        });
    }

    /// <summary>
    /// Client qui ne suit pas les redirections. Indispensable pour prouver
    /// qu'une requête sans jeton répond 401 et non 302 : le client par défaut
    /// suivrait la redirection et le test passerait pour la mauvaise raison.
    /// </summary>
    public HttpClient ClientAnonymeSansRedirection() =>
        Fabrique.CreerClient(suivreRedirections: false);
}

[CollectionDefinition(Nom)]
public class CollectionApi : ICollectionFixture<FixtureApi>
{
    public const string Nom = "api";
}
