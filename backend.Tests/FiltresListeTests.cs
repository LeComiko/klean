using System.Net;
using System.Net.Http.Json;
using Klean.Api.DTOs;
using Klean.Api.Tests.Infrastructure;

namespace Klean.Api.Tests;

/// <summary>
/// Les filtres de parenté des quatre listes de la chaîne d'exploitation —
/// <c>?clientId=</c> sur les sites, <c>?siteId=</c> sur les contrats,
/// <c>?contratId=</c> sur les prestations, <c>?agentId=</c> sur les absences.
///
/// Ce qu'ils servent est la <b>descente de niveau</b> : « les sites de ce client ».
/// Sans eux, un écran de référence ne peut proposer que la liste entière, et sur une
/// entreprise de cinquante sites la mise en route devient une recherche visuelle.
///
/// ⚠️ Deux défauts de cette famille ne cassent rien de visible, et c'est pourquoi
/// ils sont éprouvés nommément : un total compté sur la table entière, qui annonce
/// quarante lignes au-dessus d'une liste qui en montre trois ; et un identifiant
/// illisible silencieusement ignoré, qui rendrait <b>toutes</b> les lignes sous un
/// bandeau affirmant n'en montrer qu'une partie.
/// </summary>
[Collection(CollectionApi.Nom)]
public class FiltresListeTests : IAsyncLifetime
{
    private readonly FixtureApi _fixture;
    private HttpClient _client = null!;

    public FiltresListeTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Les_sites_filtres_par_client_ne_rendent_que_les_siens()
    {
        var retenu = await CreerClient("Syndic Bellevue");
        var autre = await CreerClient("Gestionnaire Nord");

        await CreerSite(retenu, "Résidence A");
        await CreerSite(retenu, "Résidence B");
        await CreerSite(autre, "Tour Nord");

        var page = await _client.PageAsync<SiteDto>($"/api/sites?clientId={retenu}");

        Assert.Equal(2, page.Total);
        Assert.Equal(2, page.Elements.Count());
        Assert.All(page.Elements, s => Assert.Equal(retenu, s.ClientId));
    }

    [Fact]
    public async Task Sans_filtre_la_liste_reste_complete()
    {
        // Le filtre est facultatif : son absence ne doit pas se comporter comme un
        // filtre sur Guid.Empty, qui rendrait une liste systématiquement vide.
        var premier = await CreerClient("Syndic Bellevue");
        var second = await CreerClient("Gestionnaire Nord");

        await CreerSite(premier, "Résidence A");
        await CreerSite(second, "Tour Nord");

        var page = await _client.PageAsync<SiteDto>("/api/sites");

        Assert.Equal(2, page.Total);
    }

    [Fact]
    public async Task Le_total_d_une_liste_filtree_compte_les_lignes_du_filtre_pas_de_la_table()
    {
        // ⚠️ LE test de ce fichier. Un comptage posé avant le Where rendrait 5 :
        // l'écran annoncerait « 5 sites » et la pagination offrirait une deuxième
        // page vide, sans qu'aucune erreur ne soit levée.
        var retenu = await CreerClient("Syndic Bellevue");
        var autre = await CreerClient("Gestionnaire Nord");

        await CreerSite(retenu, "Résidence A");
        await CreerSite(retenu, "Résidence B");
        await CreerSite(retenu, "Résidence C");
        await CreerSite(autre, "Tour Nord");
        await CreerSite(autre, "Tour Sud");

        var page = await _client.PageAsync<SiteDto>($"/api/sites?clientId={retenu}&taille=2");

        Assert.Equal(3, page.Total);
        Assert.Equal(2, page.Elements.Count());
    }

    [Fact]
    public async Task Le_filtre_se_combine_avec_la_pagination_sans_perdre_ni_dupliquer()
    {
        var retenu = await CreerClient("Syndic Bellevue");
        var autre = await CreerClient("Gestionnaire Nord");

        for (var i = 0; i < 5; i++) await CreerSite(retenu, $"Résidence {i:D2}");
        await CreerSite(autre, "Tour Nord");

        var premiere = await _client.PageAsync<SiteDto>($"/api/sites?clientId={retenu}&page=1&taille=2");
        var deuxieme = await _client.PageAsync<SiteDto>($"/api/sites?clientId={retenu}&page=2&taille=2");
        var troisieme = await _client.PageAsync<SiteDto>($"/api/sites?clientId={retenu}&page=3&taille=2");

        var reconstitue = premiere.Elements
            .Concat(deuxieme.Elements)
            .Concat(troisieme.Elements)
            .Select(s => s.Id)
            .ToList();

        Assert.Equal(5, reconstitue.Count);
        Assert.Equal(5, reconstitue.Distinct().Count());
        Assert.All([premiere, deuxieme, troisieme], p => Assert.Equal(5, p.Total));
    }

    [Fact]
    public async Task Un_parent_inconnu_rend_une_page_vide_et_non_une_erreur()
    {
        // Une liste filtrée sur rien reste une liste. Un 404 obligerait l'écran à
        // distinguer « ce client n'existe pas » de « ce client n'a pas de site »,
        // alors que la seconde réponse est la bonne dans les deux cas.
        var client = await CreerClient("Syndic Bellevue");
        await CreerSite(client, "Résidence A");

        var page = await _client.PageAsync<SiteDto>($"/api/sites?clientId={Guid.NewGuid()}");

        Assert.Empty(page.Elements);
        Assert.Equal(0, page.Total);
    }

    [Fact]
    public async Task Un_parent_d_une_autre_entreprise_rend_une_page_vide()
    {
        // Le filtre ne contourne pas les filtres globaux : il s'y ajoute. Un
        // identifiant volé à une autre entreprise ne rend donc rien — et surtout
        // pas la liste complète, ce que ferait un filtre appliqué après coup en
        // mémoire sur une requête non filtrée.
        var beta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);
        var clientDeBeta = await CreerClient("Client de Beta", beta);
        await CreerSite(clientDeBeta, "Site de Beta", beta);

        var mien = await CreerClient("Syndic Bellevue");
        await CreerSite(mien, "Résidence A");

        var page = await _client.PageAsync<SiteDto>($"/api/sites?clientId={clientDeBeta}");

        Assert.Empty(page.Elements);
        Assert.Equal(0, page.Total);
    }

    [Theory]
    [InlineData("/api/sites?clientId=pas-un-identifiant")]
    [InlineData("/api/contrats?siteId=12345")]
    [InlineData("/api/prestations?contratId=cafe")]
    [InlineData("/api/absences?agentId=null")]
    public async Task Un_identifiant_illisible_est_refuse_plutot_qu_ignore(string route)
    {
        // ⚠️ Le défaut que ce test ferme : un identifiant illisible silencieusement
        // ignoré rendrait la liste **entière** sous un bandeau affirmant ne montrer
        // que les lignes d'un parent. L'écran mentirait sans qu'aucune erreur ne
        // soit levée nulle part. La liaison de modèle refuse en 400, et c'est bien
        // ce qu'on veut : il n'y a pas de lecture raisonnable d'un « cafe » en Guid.
        var reponse = await _client.GetAsync(route);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Theory]
    [InlineData("/api/sites?clientId=")]
    [InlineData("/api/contrats?siteId=")]
    [InlineData("/api/prestations?contratId=")]
    [InlineData("/api/absences?agentId=")]
    public async Task Un_parametre_vide_vaut_absence_de_filtre(string route)
    {
        // ⚠️ Mesuré, et contre-intuitif : un `Guid?` recevant une valeur **vide** est
        // lié à `null` par ASP.NET, pas refusé. Le serveur rend donc la liste entière,
        // ce qui est la bonne réponse — mais l'écran doit lire ce paramètre de la même
        // façon. S'il traitait une chaîne vide comme un filtre, il afficherait
        // « Sites de … » au-dessus de la liste complète de l'entreprise.
        var reponse = await _client.GetAsync(route);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }

    [Fact]
    public async Task La_descente_de_niveau_traverse_la_chaine_complete()
    {
        // Le parcours de mise en route, de haut en bas : chaque niveau ne doit
        // montrer que les enfants du niveau choisi, et un voisin du même rang doit
        // en rester absent.
        var client = await CreerClient("Syndic Bellevue");
        var site = await CreerSite(client, "Résidence Bellevue");
        var siteVoisin = await CreerSite(client, "Résidence Voisine");

        var contrat = await CreerContrat(site, "Lundi, mercredi, vendredi");
        var contratVoisin = await CreerContrat(siteVoisin, "Mardi, jeudi");

        var prestation = await CreerPrestation(contrat, "Nettoyage parties communes");
        await CreerPrestation(contratVoisin, "Nettoyage vitres");

        var sites = await _client.PageAsync<SiteDto>($"/api/sites?clientId={client}");
        Assert.Equal(2, sites.Total);

        var contrats = await _client.PageAsync<ContratDto>($"/api/contrats?siteId={site}");
        Assert.Equal(1, contrats.Total);
        Assert.Equal(contrat, contrats.Elements.Single().Id);

        var prestations = await _client.PageAsync<PrestationDto>($"/api/prestations?contratId={contrat}");
        Assert.Equal(1, prestations.Total);
        Assert.Equal(prestation, prestations.Elements.Single().Id);
    }

    [Fact]
    public async Task Les_absences_filtrees_par_agent_ne_rendent_que_les_siennes()
    {
        // C'est le filtre qui sert le panneau de remplacement : partir d'un agent
        // et voir ses absences, plutôt que la liste de toute l'entreprise.
        var retenu = await CreerAgent("Diallo");
        var autre = await CreerAgent("Nguyen");

        await CreerAbsence(retenu, "Conge", new DateTime(2026, 9, 7), new DateTime(2026, 9, 9));
        await CreerAbsence(retenu, "Maladie", new DateTime(2026, 10, 1), new DateTime(2026, 10, 2));
        await CreerAbsence(autre, "Retard", new DateTime(2026, 9, 7), new DateTime(2026, 9, 7));

        var page = await _client.PageAsync<AbsenceDto>($"/api/absences?agentId={retenu}");

        Assert.Equal(2, page.Total);
        Assert.All(page.Elements, a => Assert.Equal(retenu, a.AgentId));
    }

    // ---- Fabrication ----

    private async Task<Guid> CreerClient(string nom, HttpClient? avec = null)
    {
        var reponse = await (avec ?? _client).PostAsJsonAsync("/api/clients",
            new ClientCreateDto { Nom = nom, Type = "Syndic" });

        return await Identifiant<ClientDto>(reponse, c => c.Id);
    }

    private async Task<Guid> CreerSite(Guid client, string nom, HttpClient? avec = null)
    {
        var reponse = await (avec ?? _client).PostAsJsonAsync("/api/sites",
            new SiteCreateDto
            {
                ClientId = client,
                Nom = nom,
                Adresse = "12 rue de Vaugirard",
                Ville = "Paris",
                Latitude = 48.8421,
                Longitude = 2.3219
            });

        return await Identifiant<SiteDto>(reponse, s => s.Id);
    }

    private async Task<Guid> CreerContrat(Guid site, string frequence)
    {
        var reponse = await _client.PostAsJsonAsync("/api/contrats",
            new ContratCreateDto
            {
                SiteId = site,
                DateDebut = new DateTime(2026, 1, 1),
                Frequence = frequence
            });

        return await Identifiant<ContratDto>(reponse, c => c.Id);
    }

    private async Task<Guid> CreerPrestation(Guid contrat, string libelle)
    {
        var reponse = await _client.PostAsJsonAsync("/api/prestations",
            new PrestationCreateDto
            {
                ContratId = contrat,
                Libelle = libelle,
                DureeEstimeeMinutes = 120
            });

        return await Identifiant<PrestationDto>(reponse, p => p.Id);
    }

    private async Task<Guid> CreerAgent(string nom)
    {
        var reponse = await _client.PostAsJsonAsync("/api/agents",
            new AgentCreateDto
            {
                Nom = nom,
                Prenom = "Aminata",
                DateEmbauche = new DateTime(2024, 3, 15)
            });

        return await Identifiant<AgentDto>(reponse, a => a.Id);
    }

    private async Task<Guid> CreerAbsence(Guid agent, string type, DateTime debut, DateTime fin)
    {
        var reponse = await _client.PostAsJsonAsync("/api/absences",
            new AbsenceCreateDto { AgentId = agent, Type = type, DateDebut = debut, DateFin = fin });

        return await Identifiant<AbsenceDto>(reponse, a => a.Id);
    }

    private static async Task<Guid> Identifiant<T>(HttpResponseMessage reponse, Func<T, Guid> lire)
    {
        if (!reponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Création refusée ({(int)reponse.StatusCode}) : "
                + await reponse.Content.ReadAsStringAsync());
        }

        var cree = await reponse.Content.ReadFromJsonAsync<T>()
            ?? throw new InvalidOperationException("Réponse de création illisible.");

        return lire(cree);
    }
}
