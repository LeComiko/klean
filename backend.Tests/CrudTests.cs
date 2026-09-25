using System.Net;
using System.Net.Http.Json;
using Klean.Api.DTOs;
using Klean.Api.Tests.Infrastructure;

namespace Klean.Api.Tests;

[Collection(CollectionApi.Nom)]
public class CrudTests : IAsyncLifetime
{
    private readonly FixtureApi _fixture;
    private HttpClient _client = null!;

    public CrudTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Chaine_complete_creee_puis_relue()
    {
        var client = await Creer<ClientDto>("/api/clients", new ClientCreateDto
        {
            Nom = "Syndic Bellevue",
            Type = "Syndic",
            Ville = "Paris"
        });
        Assert.Equal("Syndic", client.Type);

        var site = await Creer<SiteDto>("/api/sites", new SiteCreateDto
        {
            ClientId = client.Id,
            Nom = "Résidence Bellevue",
            Adresse = "12 rue de Vaugirard",
            CodePostal = "75015",
            Ville = "Paris",
            Latitude = 48.8421,
            Longitude = 2.3219,
            Consignes = "Badge à l'accueil"
        });
        Assert.Equal(48.8421, site.Latitude, 4);
        Assert.Equal("Badge à l'accueil", site.Consignes);

        var contrat = await Creer<ContratDto>("/api/contrats", new ContratCreateDto
        {
            SiteId = site.Id,
            DateDebut = new DateTime(2026, 1, 1),
            Frequence = "Lundi, mercredi, vendredi"
        });
        Assert.Null(contrat.DateFin);

        var prestation = await Creer<PrestationDto>("/api/prestations", new PrestationCreateDto
        {
            ContratId = contrat.Id,
            Libelle = "Nettoyage parties communes",
            DureeEstimeeMinutes = 120
        });
        Assert.Equal(120, prestation.DureeEstimeeMinutes);

        var agent = await Creer<AgentDto>("/api/agents", new AgentCreateDto
        {
            Nom = "Diallo",
            Prenom = "Aminata",
            DateEmbauche = new DateTime(2024, 3, 15),
            Latitude = 48.8566,
            Longitude = 2.3522
        });

        var intervention = await Creer<InterventionDto>("/api/interventions", new InterventionCreateDto
        {
            PrestationId = prestation.Id,
            AgentId = agent.Id,
            DatePrevue = new DateTime(2026, 9, 7),
            HeurePrevue = new TimeSpan(8, 30, 0),
            Statut = "Planifiee"
        });

        // Relecture : ce qui a été écrit doit revenir identique.
        var relue = await _client.GetFromJsonAsync<InterventionDto>(
            $"/api/interventions/{intervention.Id}");

        Assert.NotNull(relue);
        Assert.Equal(new TimeSpan(8, 30, 0), relue.HeurePrevue);
        Assert.Equal("Planifiee", relue.Statut);
        Assert.Equal(agent.Id, relue.AgentId);
    }

    [Fact]
    public async Task Date_recue_sans_fuseau_est_stockee_en_utc()
    {
        // Npgsql refuse un DateTime dont le Kind est Unspecified, ce que produit
        // la liaison JSON d'une date nue. La garde est le SpecifyKind des
        // contrôleurs (CLAUDE.md §10) : sans elle, cette création échoue.
        var agent = await Creer<AgentDto>("/api/agents", new AgentCreateDto
        {
            Nom = "Nguyen",
            Prenom = "Linh",
            DateEmbauche = new DateTime(2024, 3, 15)
        });

        var relu = await _client.GetFromJsonAsync<AgentDto>($"/api/agents/{agent.Id}");

        Assert.NotNull(relu);
        Assert.Equal(new DateTime(2024, 3, 15), relu.DateEmbauche.Date);
        Assert.Equal(DateTimeKind.Utc, relu.DateEmbauche.Kind);
    }

    [Fact]
    public async Task Vacation_sans_agent_est_acceptee()
    {
        var prestation = await ChainePrestation();

        var intervention = await Creer<InterventionDto>("/api/interventions", new InterventionCreateDto
        {
            PrestationId = prestation.Id,
            AgentId = null,
            DatePrevue = new DateTime(2026, 9, 8),
            HeurePrevue = new TimeSpan(14, 0, 0),
            Statut = "Planifiee"
        });

        Assert.Null(intervention.AgentId);
    }

    [Theory]
    [InlineData("/api/clients", "Bailleur", "Type de client")]
    [InlineData("/api/stocks", "Camion", "Type d'emplacement")]
    public async Task Enum_invalide_renvoie_400_avec_les_valeurs_acceptees(
        string route, string valeurInvalide, string debutDuMessage)
    {
        object corps = route == "/api/clients"
            ? new ClientCreateDto { Nom = "X", Type = valeurInvalide }
            : new StockCreateDto
            {
                ProduitId = (await Creer<ProduitDto>("/api/produits", new ProduitCreateDto
                {
                    Nom = "Détergent",
                    Unite = "litre",
                    SeuilAlerte = 10m
                })).Id,
                TypeEmplacement = valeurInvalide,
                EmplacementId = Guid.NewGuid(),
                Quantite = 1m
            };

        var reponse = await _client.PostAsJsonAsync(route, corps);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        var message = await reponse.Content.ReadAsStringAsync();
        Assert.Contains(debutDuMessage, message);
        Assert.Contains(valeurInvalide, message);
    }

    [Fact]
    public async Task Cle_etrangere_inconnue_renvoie_400()
    {
        var reponse = await _client.PostAsJsonAsync("/api/sites", new SiteCreateDto
        {
            ClientId = Guid.NewGuid(),
            Nom = "X",
            Adresse = "Y",
            Latitude = 48.8,
            Longitude = 2.3
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("inconnu", await reponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Contrat_avec_date_de_fin_anterieure_refuse()
    {
        var site = await ChaineSite();

        var reponse = await _client.PostAsJsonAsync("/api/contrats", new ContratCreateDto
        {
            SiteId = site.Id,
            DateDebut = new DateTime(2026, 6, 1),
            DateFin = new DateTime(2026, 1, 1),
            Frequence = "Lundi"
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Theory]
    [InlineData("entreprises")]
    [InlineData("clients")]
    [InlineData("sites")]
    [InlineData("agents")]
    [InlineData("contrats")]
    [InlineData("prestations")]
    [InlineData("interventions")]
    [InlineData("absences")]
    [InlineData("remplacements")]
    [InlineData("produits")]
    [InlineData("stocks")]
    public async Task Identifiant_inexistant_renvoie_404(string ressource)
    {
        var reponse = await _client.GetAsync($"/api/{ressource}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }

    [Fact]
    public async Task Suppression_agent_porteur_d_interventions_renvoie_409()
    {
        var prestation = await ChainePrestation();

        var agent = await Creer<AgentDto>("/api/agents", new AgentCreateDto
        {
            Nom = "Bloque",
            Prenom = "Agent",
            DateEmbauche = new DateTime(2024, 1, 1)
        });

        await Creer<InterventionDto>("/api/interventions", new InterventionCreateDto
        {
            PrestationId = prestation.Id,
            AgentId = agent.Id,
            DatePrevue = new DateTime(2026, 9, 7),
            HeurePrevue = new TimeSpan(8, 0, 0),
            Statut = "Planifiee"
        });

        var reponse = await _client.DeleteAsync($"/api/agents/{agent.Id}");

        // Restrict volontaire sur Intervention.AgentId : l'historique du planning
        // ne disparaît pas avec l'agent, et le message doit indiquer la sortie.
        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        var message = await reponse.Content.ReadAsStringAsync();
        Assert.Contains("1 intervention", message);
        Assert.Contains("Actif = false", message);
    }

    [Fact]
    public async Task Suppression_client_emporte_ses_sites_en_cascade()
    {
        var site = await ChaineSite();
        var clientId = site.ClientId;

        var suppression = await _client.DeleteAsync($"/api/clients/{clientId}");
        Assert.Equal(HttpStatusCode.NoContent, suppression.StatusCode);

        var siteRelu = await _client.GetAsync($"/api/sites/{site.Id}");
        Assert.Equal(HttpStatusCode.NotFound, siteRelu.StatusCode);
    }

    [Fact]
    public async Task Siret_deja_pris_par_une_autre_entreprise_renvoie_409()
    {
        // Le SIRET est unique à l'échelle de l'instance, pas du tenant : deux
        // entreprises clientes ne peuvent pas déclarer le même. La contrainte est
        // en base, hors portée des filtres, donc Alpha se heurte à celui de Beta
        // sans pouvoir le lire.
        var sienne = await _client.ListerAsync<EntrepriseDto>("/api/entreprises");
        Assert.NotNull(sienne);
        var alpha = Assert.Single(sienne);

        var reponse = await _client.PutAsJsonAsync($"/api/entreprises/{alpha.Id}",
            new EntrepriseCreateDto
            {
                RaisonSociale = alpha.RaisonSociale,
                Siret = "20000000000002"
            });

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        Assert.Contains("déjà enregistré", await reponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Latitude_hors_bornes_refusee()
    {
        var reponse = await _client.PostAsJsonAsync("/api/sites", new SiteCreateDto
        {
            ClientId = Guid.NewGuid(),
            Nom = "X",
            Adresse = "Y",
            Latitude = 120,
            Longitude = 2.3
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    // ---- fabriques de contexte ----

    private async Task<T> Creer<T>(string route, object corps)
    {
        var reponse = await _client.PostAsJsonAsync(route, corps);

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);

        return await reponse.Content.ReadFromJsonAsync<T>()
            ?? throw new InvalidOperationException($"Réponse illisible sur {route}.");
    }

    private async Task<SiteDto> ChaineSite()
    {
        var client = await Creer<ClientDto>("/api/clients", new ClientCreateDto
        {
            Nom = "Client " + Guid.NewGuid().ToString("N")[..8],
            Type = "Copropriete"
        });

        return await Creer<SiteDto>("/api/sites", new SiteCreateDto
        {
            ClientId = client.Id,
            Nom = "Site de test",
            Adresse = "1 rue du Test",
            Latitude = 48.85,
            Longitude = 2.33
        });
    }

    private async Task<PrestationDto> ChainePrestation()
    {
        var site = await ChaineSite();

        var contrat = await Creer<ContratDto>("/api/contrats", new ContratCreateDto
        {
            SiteId = site.Id,
            DateDebut = new DateTime(2026, 1, 1),
            Frequence = "Mardi, jeudi"
        });

        return await Creer<PrestationDto>("/api/prestations", new PrestationCreateDto
        {
            ContratId = contrat.Id,
            Libelle = "Vitrerie",
            DureeEstimeeMinutes = 90
        });
    }
}
