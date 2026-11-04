using System.Net;
using System.Net.Http.Json;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Klean.Api.Tests.Infrastructure;

namespace Klean.Api.Tests;

/// <summary>
/// Vérifie la matrice des droits de SPEC.md, étape 8, et les deux axes
/// d'isolation internes à une entreprise.
/// </summary>
[Collection(CollectionApi.Nom)]
public class AutorisationsTests : IAsyncLifetime
{
    private readonly FixtureApi _fixture;

    private HttpClient _admin = null!;
    private HttpClient _responsable = null!;
    private HttpClient _chefEquipe = null!;

    private ClientDto _clientA = null!;
    private ClientDto _clientB = null!;
    private SiteDto _siteA = null!;
    private SiteDto _siteB = null!;
    private AgentDto _agentUn = null!;
    private AgentDto _agentDeux = null!;
    private PrestationDto _prestation = null!;

    public AutorisationsTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        _clientA = await Creer<ClientDto>(_admin, "/api/clients",
            new ClientCreateDto { Nom = "Client A", Type = "Syndic" });
        _clientB = await Creer<ClientDto>(_admin, "/api/clients",
            new ClientCreateDto { Nom = "Client B", Type = "Syndic" });

        _siteA = await CreerSite(_clientA.Id, "Site de A");
        _siteB = await CreerSite(_clientB.Id, "Site de B");

        var contrat = await Creer<ContratDto>(_admin, "/api/contrats", new ContratCreateDto
        {
            SiteId = _siteA.Id,
            DateDebut = new DateTime(2026, 1, 1),
            Frequence = "Lundi"
        });

        _prestation = await Creer<PrestationDto>(_admin, "/api/prestations", new PrestationCreateDto
        {
            ContratId = contrat.Id,
            Libelle = "Parties communes",
            DureeEstimeeMinutes = 120
        });

        _agentUn = await CreerAgent("Un");
        _agentDeux = await CreerAgent("Deux");

        _responsable = await _fixture.ClientDeRoleAsync(
            _admin, RolesKlean.Responsable, "resp@test.local");
        _chefEquipe = await _fixture.ClientDeRoleAsync(
            _admin, RolesKlean.ChefEquipe, "chef@test.local");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ---------- Responsable ----------

    [Theory]
    [InlineData("/api/clients")]
    [InlineData("/api/sites")]
    [InlineData("/api/contrats")]
    [InlineData("/api/prestations")]
    [InlineData("/api/agents")]
    [InlineData("/api/produits")]
    public async Task Responsable_lit_les_donnees_de_reference_sans_pouvoir_les_creer(string route)
    {
        var lecture = await _responsable.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, lecture.StatusCode);

        // Un corps vide suffit : le 403 tombe avant toute validation de modèle.
        var creation = await _responsable.PostAsJsonAsync(route, new { });
        Assert.Equal(HttpStatusCode.Forbidden, creation.StatusCode);
    }

    [Fact]
    public async Task Responsable_ecrit_sur_l_exploitation()
    {
        var intervention = await Creer<InterventionDto>(_responsable, "/api/interventions",
            new InterventionCreateDto
            {
                PrestationId = _prestation.Id,
                AgentId = _agentUn.Id,
                DatePrevue = new DateTime(2026, 9, 7),
                HeurePrevue = new TimeSpan(8, 0, 0),
                Statut = "Planifiee"
            });
        Assert.Equal(_fixture.EntrepriseAlpha, intervention.EntrepriseId);

        await Creer<AbsenceDto>(_responsable, "/api/absences", new AbsenceCreateDto
        {
            AgentId = _agentUn.Id,
            Type = "Conge",
            DateDebut = new DateTime(2026, 9, 14),
            DateFin = new DateTime(2026, 9, 18)
        });
    }

    // ---------- Chef d'équipe ----------

    [Fact]
    public async Task ChefEquipe_modifie_une_intervention_mais_n_en_cree_pas()
    {
        // Depuis l'étape 19, le chef ne voit que son équipe : sans ce rattachement,
        // l'intervention lui serait introuvable et le PUT rendrait 404.
        var chef = (await _chefEquipe.GetFromJsonAsync<ProfilDto>("/api/auth/moi"))!.Id;
        var rattachement = await _admin.PutAsJsonAsync($"/api/agents/{_agentUn.Id}", new AgentCreateDto
        {
            Nom = _agentUn.Nom,
            Prenom = _agentUn.Prenom,
            DateEmbauche = _agentUn.DateEmbauche,
            ChefEquipeId = chef
        });
        Assert.Equal(HttpStatusCode.NoContent, rattachement.StatusCode);

        var intervention = await CreerIntervention(_agentUn.Id, new DateTime(2026, 9, 7));

        var modification = await _chefEquipe.PutAsJsonAsync(
            $"/api/interventions/{intervention.Id}",
            new InterventionCreateDto
            {
                PrestationId = intervention.PrestationId,
                AgentId = intervention.AgentId,
                DatePrevue = intervention.DatePrevue,
                HeurePrevue = intervention.HeurePrevue,
                Statut = "Terminee"
            });
        Assert.Equal(HttpStatusCode.NoContent, modification.StatusCode);

        var creation = await _chefEquipe.PostAsJsonAsync("/api/interventions", new { });
        Assert.Equal(HttpStatusCode.Forbidden, creation.StatusCode);

        var suppression = await _chefEquipe.DeleteAsync($"/api/interventions/{intervention.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, suppression.StatusCode);
    }

    [Theory]
    [InlineData("/api/absences")]
    [InlineData("/api/remplacements")]
    [InlineData("/api/stocks")]
    [InlineData("/api/clients")]
    public async Task ChefEquipe_lit_mais_n_ecrit_pas_hors_interventions(string route)
    {
        var lecture = await _chefEquipe.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, lecture.StatusCode);

        var creation = await _chefEquipe.PostAsJsonAsync(route, new { });
        Assert.Equal(HttpStatusCode.Forbidden, creation.StatusCode);
    }

    // ---------- Agent ----------

    [Fact]
    public async Task Agent_ne_voit_que_ses_propres_interventions()
    {
        var sienne = await CreerIntervention(_agentUn.Id, new DateTime(2026, 9, 7));
        var autre = await CreerIntervention(_agentDeux.Id, new DateTime(2026, 9, 8));

        var agent = await _fixture.ClientDeRoleAsync(
            _admin, RolesKlean.Agent, "agent@test.local", agentId: _agentUn.Id);

        var vues = await agent.ListerAsync<InterventionDto>("/api/interventions");

        Assert.NotNull(vues);
        Assert.Equal([sienne.Id], vues.Select(i => i.Id));

        var celleDeLAutre = await agent.GetAsync($"/api/interventions/{autre.Id}");
        Assert.Equal(HttpStatusCode.NotFound, celleDeLAutre.StatusCode);
    }

    [Theory]
    [InlineData("/api/clients")]
    [InlineData("/api/sites")]
    [InlineData("/api/agents")]
    [InlineData("/api/agents/choix")]
    [InlineData("/api/absences")]
    [InlineData("/api/entreprises")]
    public async Task Agent_n_a_acces_a_rien_d_autre(string route)
    {
        var agent = await _fixture.ClientDeRoleAsync(
            _admin, RolesKlean.Agent, "agent.cloisonne@test.local", agentId: _agentUn.Id);

        var reponse = await agent.GetAsync(route);

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    // ---------- Client / Syndic ----------

    [Fact]
    public async Task ClientSyndic_ne_voit_que_son_client_et_ses_sites()
    {
        var syndic = await _fixture.ClientDeRoleAsync(
            _admin, RolesKlean.ClientSyndic, "syndic.a@test.local", clientId: _clientA.Id);

        var clients = await syndic.ListerAsync<ClientDto>("/api/clients");
        Assert.NotNull(clients);
        Assert.Equal([_clientA.Id], clients.Select(c => c.Id));

        var sites = await syndic.ListerAsync<SiteDto>("/api/sites");
        Assert.NotNull(sites);
        Assert.Equal([_siteA.Id], sites.Select(s => s.Id));

        // Le site du client B appartient à la MÊME entreprise : c'est le
        // deuxième axe d'isolation qui le masque, pas le tenant.
        var siteDeB = await syndic.GetAsync($"/api/sites/{_siteB.Id}");
        Assert.Equal(HttpStatusCode.NotFound, siteDeB.StatusCode);

        var clientB = await syndic.GetAsync($"/api/clients/{_clientB.Id}");
        Assert.Equal(HttpStatusCode.NotFound, clientB.StatusCode);
    }

    [Fact]
    public async Task ClientSyndic_ne_peut_rien_ecrire()
    {
        var syndic = await _fixture.ClientDeRoleAsync(
            _admin, RolesKlean.ClientSyndic, "syndic.lecture@test.local", clientId: _clientA.Id);

        var creation = await syndic.PostAsJsonAsync("/api/sites", new { });
        Assert.Equal(HttpStatusCode.Forbidden, creation.StatusCode);

        var modification = await syndic.PutAsJsonAsync($"/api/sites/{_siteA.Id}", new { });
        Assert.Equal(HttpStatusCode.Forbidden, modification.StatusCode);

        var suppression = await syndic.DeleteAsync($"/api/sites/{_siteA.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, suppression.StatusCode);
    }

    [Theory]
    [InlineData("/api/contrats")]
    [InlineData("/api/prestations")]
    [InlineData("/api/interventions")]
    [InlineData("/api/agents")]
    [InlineData("/api/agents/choix")]
    [InlineData("/api/absences")]
    public async Task ClientSyndic_n_atteint_pas_le_reste_du_systeme(string route)
    {
        var syndic = await _fixture.ClientDeRoleAsync(
            _admin, RolesKlean.ClientSyndic, "syndic.cloisonne@test.local", clientId: _clientA.Id);

        var reponse = await syndic.GetAsync(route);

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    // ---------- Provisionnement et règles métier ----------

    [Fact]
    public async Task Creation_et_suppression_d_entreprise_ne_sont_plus_exposees()
    {
        // Provisionnement d'instance, pas gestion de tenant.
        var creation = await _admin.PostAsJsonAsync("/api/entreprises",
            new EntrepriseCreateDto { RaisonSociale = "Intruse", Siret = "40000000000004" });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, creation.StatusCode);

        var sienne = await _admin.ListerAsync<EntrepriseDto>("/api/entreprises");
        var suppression = await _admin.DeleteAsync($"/api/entreprises/{sienne![0].Id}");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, suppression.StatusCode);
    }

    [Fact]
    public async Task Arret_maladie_avec_motif_refuse()
    {
        var reponse = await _admin.PostAsJsonAsync("/api/absences", new AbsenceCreateDto
        {
            AgentId = _agentUn.Id,
            Type = "Maladie",
            DateDebut = new DateTime(2026, 9, 9),
            DateFin = new DateTime(2026, 9, 11),
            Motif = "Opération du genou"
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("donnée de santé", await reponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Arret_maladie_sans_motif_accepte_et_motif_conserve_ailleurs()
    {
        var maladie = await Creer<AbsenceDto>(_admin, "/api/absences", new AbsenceCreateDto
        {
            AgentId = _agentUn.Id,
            Type = "Maladie",
            DateDebut = new DateTime(2026, 9, 9),
            DateFin = new DateTime(2026, 9, 11)
        });
        Assert.Null(maladie.Motif);

        var indisponibilite = await Creer<AbsenceDto>(_admin, "/api/absences", new AbsenceCreateDto
        {
            AgentId = _agentUn.Id,
            Type = "Indisponibilite",
            DateDebut = new DateTime(2026, 9, 15),
            DateFin = new DateTime(2026, 9, 15),
            Motif = "Formation sécurité"
        });
        Assert.Equal("Formation sécurité", indisponibilite.Motif);
    }

    [Fact]
    public async Task Statut_de_remplacement_non_modifiable_par_le_crud()
    {
        var intervention = await CreerIntervention(_agentUn.Id, new DateTime(2026, 9, 7));

        var remplacement = await Creer<RemplacementDto>(_admin, "/api/remplacements",
            new RemplacementCreateDto
            {
                InterventionId = intervention.Id,
                Statut = nameof(StatutRemplacement.Recherche)
            });

        var forcage = await _admin.PutAsJsonAsync($"/api/remplacements/{remplacement.Id}",
            new RemplacementCreateDto
            {
                InterventionId = intervention.Id,
                AgentRemplacantId = _agentDeux.Id,
                Statut = nameof(StatutRemplacement.Accepte),
                Score = 90
            });

        Assert.Equal(HttpStatusCode.BadRequest, forcage.StatusCode);
        Assert.Contains("ne se modifie pas ici", await forcage.Content.ReadAsStringAsync());

        // Rien n'a bougé.
        var relu = await _admin.GetFromJsonAsync<RemplacementDto>(
            $"/api/remplacements/{remplacement.Id}");
        Assert.Equal(nameof(StatutRemplacement.Recherche), relu!.Statut);
        Assert.Null(relu.AgentRemplacantId);
    }

    // ---------- fabriques ----------

    private static async Task<T> Creer<T>(HttpClient client, string route, object corps)
    {
        var reponse = await client.PostAsJsonAsync(route, corps);

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);

        return await reponse.Content.ReadFromJsonAsync<T>()
            ?? throw new InvalidOperationException($"Réponse illisible sur {route}.");
    }

    private Task<SiteDto> CreerSite(Guid clientId, string nom) =>
        Creer<SiteDto>(_admin, "/api/sites", new SiteCreateDto
        {
            ClientId = clientId,
            Nom = nom,
            Adresse = "1 rue du Test",
            Latitude = 48.85,
            Longitude = 2.33
        });

    private Task<AgentDto> CreerAgent(string prenom) =>
        Creer<AgentDto>(_admin, "/api/agents", new AgentCreateDto
        {
            Nom = "Agent",
            Prenom = prenom,
            DateEmbauche = new DateTime(2024, 1, 1)
        });

    private Task<InterventionDto> CreerIntervention(Guid agentId, DateTime date) =>
        Creer<InterventionDto>(_admin, "/api/interventions", new InterventionCreateDto
        {
            PrestationId = _prestation.Id,
            AgentId = agentId,
            DatePrevue = date,
            HeurePrevue = new TimeSpan(8, 0, 0),
            Statut = "Planifiee"
        });
}
