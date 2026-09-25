using System.Net.Http.Headers;
using System.Net.Http.Json;
using Klean.Api.Data;
using Klean.Api.DTOs;
using Klean.Api.Models;
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

    /// <summary>Client HTTP porteur d'un jeton valide pour le compte demandé.</summary>
    public async Task<HttpClient> ClientConnecteAsync(string email)
    {
        var client = Fabrique.CreateClient();

        var reponse = await client.PostAsJsonAsync("/api/auth/connexion",
            new ConnexionDto { Email = email, MotDePasse = MotDePasse });

        reponse.EnsureSuccessStatusCode();

        var jeton = await reponse.Content.ReadFromJsonAsync<JetonDto>()
            ?? throw new InvalidOperationException("Réponse de connexion illisible.");

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", jeton.Jeton);

        return client;
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

    public HttpClient ClientAnonyme() => Fabrique.CreateClient();

    /// <summary>
    /// Client qui ne suit pas les redirections. Indispensable pour prouver
    /// qu'une requête sans jeton répond 401 et non 302 : le client par défaut
    /// suivrait la redirection et le test passerait pour la mauvaise raison.
    /// </summary>
    public HttpClient ClientAnonymeSansRedirection() =>
        Fabrique.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
}

[CollectionDefinition(Nom)]
public class CollectionApi : ICollectionFixture<FixtureApi>
{
    public const string Nom = "api";
}
