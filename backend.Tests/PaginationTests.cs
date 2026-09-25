using System.Net.Http.Json;
using Klean.Api.DTOs;
using Klean.Api.Tests.Infrastructure;

namespace Klean.Api.Tests;

[Collection(CollectionApi.Nom)]
public class PaginationTests : IAsyncLifetime
{
    private readonly FixtureApi _fixture;
    private HttpClient _client = null!;

    public PaginationTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Une_liste_sans_parametre_applique_les_valeurs_par_defaut()
    {
        await CreerClients(3);

        var page = await _client.PageAsync<ClientDto>("/api/clients");

        Assert.Equal(1, page.Page);
        Assert.Equal(PaginationDto.TailleParDefaut, page.Taille);
        Assert.Equal(3, page.Total);
        Assert.Equal(3, page.Elements.Count());
    }

    [Fact]
    public async Task Le_decoupage_en_pages_ne_perd_ni_ne_duplique_aucune_ligne()
    {
        var crees = await CreerClients(7);

        var premiere = await _client.PageAsync<ClientDto>("/api/clients?page=1&taille=3");
        var deuxieme = await _client.PageAsync<ClientDto>("/api/clients?page=2&taille=3");
        var troisieme = await _client.PageAsync<ClientDto>("/api/clients?page=3&taille=3");

        Assert.Equal(3, premiere.Elements.Count());
        Assert.Equal(3, deuxieme.Elements.Count());
        Assert.Single(troisieme.Elements);

        // Total constant : c'est le nombre de lignes visibles, pas celui de la page.
        Assert.All([premiere, deuxieme, troisieme], p => Assert.Equal(7, p.Total));

        var reconstitue = premiere.Elements
            .Concat(deuxieme.Elements)
            .Concat(troisieme.Elements)
            .Select(c => c.Id)
            .ToList();

        Assert.Equal(7, reconstitue.Distinct().Count());
        Assert.Equal(crees.Order(), reconstitue.Order());
    }

    [Fact]
    public async Task Une_taille_demesuree_est_ramenee_au_plafond()
    {
        await CreerClients(2);

        var page = await _client.PageAsync<ClientDto>("/api/clients?taille=100000");

        Assert.Equal(PaginationDto.TailleMaximale, page.Taille);
    }

    [Theory]
    [InlineData("page=0", 1)]
    [InlineData("page=-5", 1)]
    public async Task Une_page_invalide_est_ramenee_a_la_premiere(string requete, int attendue)
    {
        await CreerClients(2);

        var page = await _client.PageAsync<ClientDto>($"/api/clients?{requete}");

        Assert.Equal(attendue, page.Page);
        Assert.Equal(2, page.Elements.Count());
    }

    [Theory]
    [InlineData("taille=0")]
    [InlineData("taille=-3")]
    public async Task Une_taille_invalide_retombe_sur_la_valeur_par_defaut(string requete)
    {
        await CreerClients(2);

        var page = await _client.PageAsync<ClientDto>($"/api/clients?{requete}");

        Assert.Equal(PaginationDto.TailleParDefaut, page.Taille);
    }

    [Fact]
    public async Task Une_page_au_dela_des_donnees_est_vide_mais_garde_le_total()
    {
        await CreerClients(2);

        var page = await _client.PageAsync<ClientDto>("/api/clients?page=99&taille=10");

        Assert.Empty(page.Elements);
        Assert.Equal(2, page.Total);
        Assert.Equal(99, page.Page);
    }

    [Theory]
    [InlineData("/api/entreprises")]
    [InlineData("/api/clients")]
    [InlineData("/api/sites")]
    [InlineData("/api/contrats")]
    [InlineData("/api/prestations")]
    [InlineData("/api/agents")]
    [InlineData("/api/interventions")]
    [InlineData("/api/absences")]
    [InlineData("/api/remplacements")]
    [InlineData("/api/produits")]
    [InlineData("/api/stocks")]
    public async Task Les_onze_listes_renvoient_une_enveloppe(string route)
    {
        var page = await _client.PageAsync<object>(route);

        Assert.Equal(1, page.Page);
        Assert.Equal(PaginationDto.TailleParDefaut, page.Taille);
        Assert.True(page.Total >= 0);
    }

    [Fact]
    public async Task Le_total_ne_compte_que_les_lignes_visibles_par_l_appelant()
    {
        // Deux entreprises, deux lignes. Chacune doit voir un total de 1 : le
        // compte passe par la même requête filtrée que les éléments, sinon il
        // trahirait l'existence des données de l'autre tenant.
        await CreerClients(1);

        var beta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);
        var reponse = await beta.PostAsJsonAsync("/api/clients",
            new ClientCreateDto { Nom = "Client de Beta", Type = "Syndic" });
        reponse.EnsureSuccessStatusCode();

        var pageAlpha = await _client.PageAsync<ClientDto>("/api/clients");
        var pageBeta = await beta.PageAsync<ClientDto>("/api/clients");

        Assert.Equal(1, pageAlpha.Total);
        Assert.Equal(1, pageBeta.Total);
    }

    private async Task<List<Guid>> CreerClients(int combien)
    {
        var ids = new List<Guid>();

        for (var i = 0; i < combien; i++)
        {
            var reponse = await _client.PostAsJsonAsync("/api/clients",
                new ClientCreateDto { Nom = $"Client {i:D2}", Type = "Syndic" });

            reponse.EnsureSuccessStatusCode();

            var cree = await reponse.Content.ReadFromJsonAsync<ClientDto>();
            ids.Add(cree!.Id);
        }

        return ids;
    }
}
