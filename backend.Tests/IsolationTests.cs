using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using Klean.Api.DTOs;
using Klean.Api.Data;
using Klean.Api.Models;
using Klean.Api.Services;
using Klean.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Klean.Api.Tests;

/// <summary>
/// Les trois tests obligatoires de l'étape 7. Ils exigent deux entreprises :
/// une seule ne prouverait rien, puisque tout lui appartiendrait.
/// </summary>
[Collection(CollectionApi.Nom)]
public class IsolationTests : IAsyncLifetime
{
    private readonly FixtureApi _fixture;
    private HttpClient _alpha = null!;
    private HttpClient _beta = null!;

    public IsolationTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _alpha = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
        _beta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Un_administrateur_ne_voit_aucune_donnee_d_une_autre_entreprise()
    {
        var chezAlpha = await CreerClient(_alpha, "Client d'Alpha");
        var chezBeta = await CreerClient(_beta, "Client de Beta");

        var vusParAlpha = await _alpha.ListerAsync<ClientDto>("/api/clients");
        var vusParBeta = await _beta.ListerAsync<ClientDto>("/api/clients");

        Assert.NotNull(vusParAlpha);
        Assert.NotNull(vusParBeta);

        Assert.Equal([chezAlpha.Id], vusParAlpha.Select(c => c.Id));
        Assert.Equal([chezBeta.Id], vusParBeta.Select(c => c.Id));

        Assert.All(vusParAlpha, c => Assert.Equal(_fixture.EntrepriseAlpha, c.EntrepriseId));
        Assert.All(vusParBeta, c => Assert.Equal(_fixture.EntrepriseBeta, c.EntrepriseId));
    }

    [Fact]
    public async Task Une_creation_recoit_l_entreprise_du_jeton()
    {
        var cree = await CreerClient(_alpha, "Créé par Alpha");

        Assert.Equal(_fixture.EntrepriseAlpha, cree.EntrepriseId);

        var relu = await _alpha.GetFromJsonAsync<ClientDto>($"/api/clients/{cree.Id}");
        Assert.NotNull(relu);
        Assert.Equal(_fixture.EntrepriseAlpha, relu.EntrepriseId);
    }

    [Fact]
    public async Task Un_entrepriseId_force_dans_le_corps_est_ignore()
    {
        // LE test que la version d'origine de l'étape ratait. Elle ne vérifiait
        // que le cas 00000000-…, donc précisément celui qui fonctionnait, alors
        // qu'un identifiant réel d'une autre entreprise passait.
        //
        // EntrepriseId ne figure plus dans le contrat d'entrée : on l'injecte
        // donc en JSON brut, comme le ferait une requête forgée.
        var corps = $$"""
            {
              "entrepriseId": "{{_fixture.EntrepriseBeta}}",
              "nom": "Injecté chez Beta",
              "type": "Syndic"
            }
            """;

        var reponse = await _alpha.PostAsync("/api/clients",
            new StringContent(corps, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);

        var cree = await reponse.Content.ReadFromJsonAsync<ClientDto>();
        Assert.NotNull(cree);

        // La ligne appartient à Alpha, pas à Beta.
        Assert.Equal(_fixture.EntrepriseAlpha, cree.EntrepriseId);

        // Et rien n'est apparu chez Beta.
        var vusParBeta = await _beta.ListerAsync<ClientDto>("/api/clients");
        Assert.NotNull(vusParBeta);
        Assert.Empty(vusParBeta);
    }

    [Fact]
    public async Task Une_ligne_d_une_autre_entreprise_est_introuvable()
    {
        var chezBeta = await CreerClient(_beta, "Client de Beta");

        var lecture = await _alpha.GetAsync($"/api/clients/{chezBeta.Id}");
        Assert.Equal(HttpStatusCode.NotFound, lecture.StatusCode);

        var modification = await _alpha.PutAsJsonAsync($"/api/clients/{chezBeta.Id}",
            new ClientCreateDto { Nom = "Détourné", Type = "Autre" });
        Assert.Equal(HttpStatusCode.NotFound, modification.StatusCode);

        var suppression = await _alpha.DeleteAsync($"/api/clients/{chezBeta.Id}");
        Assert.Equal(HttpStatusCode.NotFound, suppression.StatusCode);

        // Beta le voit toujours : aucune de ces tentatives n'a abouti.
        var relu = await _beta.GetAsync($"/api/clients/{chezBeta.Id}");
        Assert.Equal(HttpStatusCode.OK, relu.StatusCode);
    }

    [Fact]
    public async Task La_liste_des_entreprises_ne_montre_que_la_sienne()
    {
        // Entreprise ne porte pas de colonne EntrepriseId : son propre Id est le
        // tenant. Sans filtre sur cet Id, cet endpoint exposerait la raison
        // sociale et le SIRET de tous les clients de l'instance.
        var vuesParAlpha = await _alpha.ListerAsync<EntrepriseDto>("/api/entreprises");

        Assert.NotNull(vuesParAlpha);
        Assert.Equal([_fixture.EntrepriseAlpha], vuesParAlpha.Select(e => e.Id));
        Assert.DoesNotContain(_fixture.EntrepriseBeta, vuesParAlpha.Select(e => e.Id));
    }

    [Fact]
    public async Task Une_cle_etrangere_d_une_autre_entreprise_est_refusee()
    {
        var clientDeBeta = await CreerClient(_beta, "Client de Beta");

        // Le contrôle d'existence passe par le filtre : le client de Beta est
        // invisible depuis Alpha, donc inconnu.
        var reponse = await _alpha.PostAsJsonAsync("/api/sites", new SiteCreateDto
        {
            ClientId = clientDeBeta.Id,
            Nom = "Site greffé",
            Adresse = "1 rue Ailleurs",
            Latitude = 48.85,
            Longitude = 2.33
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("inconnu", await reponse.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/clients")]
    [InlineData("/api/sites")]
    [InlineData("/api/agents")]
    [InlineData("/api/interventions")]
    [InlineData("/api/entreprises")]
    public async Task Sans_jeton_les_donnees_sont_inaccessibles(string route)
    {
        // Le filtre laisse passer quand le tenant est nul — hors requête
        // authentifiée. Cette échappatoire n'est sûre que parce que les onze
        // contrôleurs métier portent [Authorize]. Ce test garde cet invariant.
        var anonyme = _fixture.ClientAnonymeSansRedirection();

        var reponse = await anonyme.GetAsync(route);

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
        Assert.Null(reponse.Headers.Location);
    }

    [Fact]
    public void Aucun_CreateDto_n_expose_EntrepriseId()
    {
        // Garde du contrat. Le retrait d'EntrepriseId des DTO d'entrée est ce
        // qui empêche réellement une requête forgée de viser une autre
        // entreprise : le liant ignore une propriété qui n'existe pas. Ce test
        // échoue dès que quelqu'un la remet.
        var fautifs = typeof(ClientCreateDto).Assembly
            .GetTypes()
            .Where(t => t.Name.EndsWith("CreateDto", StringComparison.Ordinal))
            .Where(t => t.GetProperty("EntrepriseId", BindingFlags.Public | BindingFlags.Instance) is not null)
            .Select(t => t.Name)
            .ToList();

        Assert.Empty(fautifs);
    }

    [Fact]
    public async Task Le_forcage_du_tenant_ecrase_une_valeur_deja_posee()
    {
        // Ceinture et bretelles de la garde précédente : même si une valeur
        // atteint l'entité — par un DTO remis, ou par du code interne —,
        // SaveChanges doit l'écraser. Ce test touche le DbContext directement,
        // parce que c'est là que vit l'invariant.
        using var portee = _fixture.Fabrique.Services.CreateScope();

        var accesseur = portee.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accesseur.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(JwtService.RevendicationEntrepriseId, _fixture.EntrepriseAlpha.ToString())],
                authenticationType: "Test"))
        };

        try
        {
            var db = portee.ServiceProvider.GetRequiredService<KleanDbContext>();

            var client = new Client
            {
                Nom = "Posé sur Beta",
                Type = TypeClient.Syndic,
                // Valeur non vide, et bien réelle : le mécanisme conditionnel
                // d'origine ne l'aurait pas écrasée.
                EntrepriseId = _fixture.EntrepriseBeta
            };

            db.Clients.Add(client);
            await db.SaveChangesAsync();

            Assert.Equal(_fixture.EntrepriseAlpha, client.EntrepriseId);

            var enBase = await db.Clients.AsNoTracking()
                .Where(c => c.Id == client.Id)
                .Select(c => c.EntrepriseId)
                .SingleAsync();

            Assert.Equal(_fixture.EntrepriseAlpha, enBase);
        }
        finally
        {
            accesseur.HttpContext = null;
        }

        // Et Beta ne voit rien de plus.
        var vusParBeta = await _beta.ListerAsync<ClientDto>("/api/clients");
        Assert.NotNull(vusParBeta);
        Assert.Empty(vusParBeta);
    }

    private static async Task<ClientDto> CreerClient(HttpClient client, string nom)
    {
        var reponse = await client.PostAsJsonAsync("/api/clients",
            new ClientCreateDto { Nom = nom, Type = "Syndic" });

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);

        return await reponse.Content.ReadFromJsonAsync<ClientDto>()
            ?? throw new InvalidOperationException("Réponse de création illisible.");
    }
}
