using System.Net;
using System.Net.Http.Json;
using Klean.Api.DTOs;
using Klean.Api.Tests.Infrastructure;

namespace Klean.Api.Tests;

/// <summary>
/// Les filtres durs du moteur d'affectation s'appliquent à <b>toutes</b> les
/// écritures qui portent un agent, pas seulement au cycle de remplacement.
///
/// ⚠️ Ces tests gardent la correction d'une faille réelle. Avant elle,
/// <c>PUT /api/interventions/{id}</c> écrivait <c>AgentId</c> après avoir
/// seulement vérifié que l'agent existait, et acceptait avec un 204 un agent en
/// arrêt maladie ce jour-là, absent de la liste des candidats. Aucune requête
/// forgée n'était nécessaire : un Responsable pouvait double-affecter un agent,
/// l'affecter pendant son arrêt, ou violer son repos de 11 h.
///
/// Décor : vacation du mercredi 9 septembre 2026, 9 h – 11 h, sur un site de
/// Paris. Sow est le titulaire, Keita est libre.
/// </summary>
[Collection(CollectionApi.Nom)]
public class EligibiliteTests : IAsyncLifetime
{
    private static readonly DateTime Mercredi = new(2026, 9, 9);
    private static readonly TimeSpan NeufHeures = new(9, 0, 0);

    private readonly FixtureApi _fixture;
    private HttpClient _client = null!;
    private PrestationDto _prestation = null!;
    private AgentDto _sow = null!;
    private AgentDto _keita = null!;

    public EligibiliteTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        var syndic = await Creer<ClientDto>("/api/clients", new ClientCreateDto
        {
            Nom = "Syndic Cité",
            Type = "Syndic"
        });

        var site = await Creer<SiteDto>("/api/sites", new SiteCreateDto
        {
            ClientId = syndic.Id,
            Nom = "Résidence Cité",
            Adresse = "1 parvis Notre-Dame",
            Latitude = 48.8530,
            Longitude = 2.3499
        });

        var contrat = await Creer<ContratDto>("/api/contrats", new ContratCreateDto
        {
            SiteId = site.Id,
            DateDebut = new DateTime(2026, 1, 1),
            Frequence = "Mercredi"
        });

        _prestation = await Creer<PrestationDto>("/api/prestations", new PrestationCreateDto
        {
            ContratId = contrat.Id,
            Libelle = "Nettoyage parties communes",
            DureeEstimeeMinutes = 120
        });

        _sow = await CreerAgent("Sow");
        _keita = await CreerAgent("Keita");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // --------------------------------------------------------------- création

    [Fact]
    public async Task Creer_refuse_un_agent_en_absence()
    {
        await CreerAbsence(_sow.Id, Mercredi, Mercredi);

        var reponse = await Poster(_sow.Id, Mercredi, NeufHeures);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("n'est pas candidat", await reponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Creer_refuse_un_agent_deja_affecte_sur_un_creneau_chevauchant()
    {
        await CreerIntervention(_sow.Id, Mercredi, new TimeSpan(10, 0, 0));

        // 9 h – 11 h contre 10 h – 12 h.
        var reponse = await Poster(_sow.Id, Mercredi, NeufHeures);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Creer_refuse_un_agent_inactif()
    {
        var retraite = await CreerAgent("Bamba", actif: false);

        var reponse = await Poster(retraite.Id, Mercredi, NeufHeures);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Creer_refuse_un_repos_de_moins_de_onze_heures()
    {
        // La veille de 20 h 30 à 22 h 30 : 10 h 30 de repos avant 9 h. Une
        // vacation finissant à 22 h pile donnerait exactement 11 h et serait
        // acceptée — la borne est incluse, la loi impose onze heures, pas plus.
        await CreerIntervention(_sow.Id, new DateTime(2026, 9, 8), new TimeSpan(20, 30, 0));

        var reponse = await Poster(_sow.Id, Mercredi, NeufHeures);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Creer_accepte_deux_sites_dans_la_meme_journee()
    {
        // Le geste le plus courant du métier. La garde ne doit pas le gêner :
        // le repos quotidien sépare deux journées, pas deux sites d'un matin.
        await CreerIntervention(_sow.Id, Mercredi, NeufHeures);

        var reponse = await Poster(_sow.Id, Mercredi, new TimeSpan(14, 0, 0));

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
    }

    [Fact]
    public async Task Creer_une_vacation_sans_agent_reste_accepte()
    {
        // Une vacation non pourvue n'a pas d'agent à juger.
        var reponse = await Poster(null, Mercredi, NeufHeures);

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
    }

    // ----------------------------------------------------------- modification

    [Fact]
    public async Task Modifier_refuse_de_poser_un_agent_en_arret_maladie()
    {
        // Le scénario exact de la faille.
        var intervention = await CreerIntervention(_sow.Id, Mercredi, NeufHeures);
        await CreerAbsence(_keita.Id, Mercredi, Mercredi);

        var candidats = await _client.GetFromJsonAsync<List<CandidatDto>>(
            $"/api/interventions/{intervention.Id}/candidats");
        Assert.DoesNotContain(candidats!, c => c.AgentId == _keita.Id);

        var reponse = await Modifier(intervention, _keita.Id, Mercredi, NeufHeures);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(_sow.Id, (await Relire(intervention.Id)).AgentId);
    }

    [Fact]
    public async Task Modifier_autorise_une_reaffectation_legitime()
    {
        // « Keita prend le site de Sow ce mercredi », sans absence de personne.
        // C'est un geste d'exploitation courant : la garde ne doit pas geler
        // AgentId, seulement refuser les agents qui ne peuvent pas y aller.
        var intervention = await CreerIntervention(_sow.Id, Mercredi, NeufHeures);

        var reponse = await Modifier(intervention, _keita.Id, Mercredi, NeufHeures);

        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);
        Assert.Equal(_keita.Id, (await Relire(intervention.Id)).AgentId);
    }

    [Fact]
    public async Task Modifier_refuse_un_deplacement_qui_rend_le_titulaire_ineligible()
    {
        // La seconde moitié de la faille, moins visible : changer l'horaire sans
        // changer l'agent peut tout autant produire une affectation illégale.
        var matin = await CreerIntervention(_sow.Id, Mercredi, NeufHeures);
        await CreerIntervention(_sow.Id, Mercredi, new TimeSpan(14, 0, 0));

        // Déplacer la vacation du matin sur celle de l'après-midi.
        var reponse = await Modifier(matin, _sow.Id, Mercredi, new TimeSpan(15, 0, 0));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(NeufHeures, (await Relire(matin.Id)).HeurePrevue);
    }

    [Fact]
    public async Task Modifier_sans_toucher_a_l_agent_reste_accepte()
    {
        // L'intervention ne doit pas se compter elle-même comme occupation :
        // sinon le titulaire serait exclu par chevauchement avec sa propre
        // vacation, et aucun changement de statut ne passerait plus.
        var intervention = await CreerIntervention(_sow.Id, Mercredi, NeufHeures);

        var reponse = await _client.PutAsJsonAsync($"/api/interventions/{intervention.Id}",
            new InterventionCreateDto
            {
                PrestationId = _prestation.Id,
                AgentId = _sow.Id,
                DatePrevue = Mercredi,
                HeurePrevue = NeufHeures,
                Statut = "NonCertifiee"
            });

        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);
        Assert.Equal("NonCertifiee", (await Relire(intervention.Id)).Statut);
    }

    [Fact]
    public async Task Liberer_la_vacation_reste_possible_meme_quand_l_agent_est_devenu_ineligible()
    {
        // L'échappatoire assumée : une ligne devenue illégale se débloque en la
        // libérant d'abord. Aucun drapeau de forçage n'est nécessaire.
        var intervention = await CreerIntervention(_sow.Id, Mercredi, NeufHeures);
        await CreerAbsence(_sow.Id, Mercredi, Mercredi);

        var bloque = await Modifier(intervention, _sow.Id, Mercredi, NeufHeures);
        Assert.Equal(HttpStatusCode.BadRequest, bloque.StatusCode);

        var libere = await Modifier(intervention, null, Mercredi, NeufHeures);

        Assert.Equal(HttpStatusCode.NoContent, libere.StatusCode);
        Assert.Null((await Relire(intervention.Id)).AgentId);
    }

    [Fact]
    public async Task Le_message_de_refus_nomme_les_quatre_causes_possibles()
    {
        // Le moteur produit un classement, pas un diagnostic : il ne sait pas
        // dire laquelle des quatre causes s'applique. Le message doit donc les
        // énumérer, sinon l'utilisateur en est réduit à deviner.
        await CreerAbsence(_sow.Id, Mercredi, Mercredi);

        var corps = await (await Poster(_sow.Id, Mercredi, NeufHeures)).Content.ReadAsStringAsync();

        Assert.Contains("inactif", corps);
        Assert.Contains("absent", corps);
        Assert.Contains("chevauche", corps);
        Assert.Contains("repos de 11 h", corps);
    }

    [Fact]
    public async Task Un_agent_de_l_autre_entreprise_reste_inconnu_avant_tout_jugement()
    {
        var beta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);
        var creation = await beta.PostAsJsonAsync("/api/agents", new AgentCreateDto
        {
            Nom = "Intrus",
            Prenom = "Beta",
            DateEmbauche = new DateTime(2024, 1, 1)
        });
        var intrus = await creation.Content.ReadFromJsonAsync<AgentDto>();
        Assert.NotNull(intrus);

        var reponse = await Poster(intrus.Id, Mercredi, NeufHeures);

        // Le filtre multi-tenant passe avant : « inconnu », pas « non candidat ».
        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("inconnu", await reponse.Content.ReadAsStringAsync());
    }

    // ------------------------------------------------------------- outillage

    private Task<HttpResponseMessage> Poster(Guid? agentId, DateTime date, TimeSpan heure) =>
        _client.PostAsJsonAsync("/api/interventions", new InterventionCreateDto
        {
            PrestationId = _prestation.Id,
            AgentId = agentId,
            DatePrevue = date,
            HeurePrevue = heure,
            Statut = "Planifiee"
        });

    private Task<HttpResponseMessage> Modifier(
        InterventionDto intervention, Guid? agentId, DateTime date, TimeSpan heure) =>
        _client.PutAsJsonAsync($"/api/interventions/{intervention.Id}", new InterventionCreateDto
        {
            PrestationId = _prestation.Id,
            AgentId = agentId,
            DatePrevue = date,
            HeurePrevue = heure,
            Statut = "Planifiee"
        });

    private async Task<InterventionDto> Relire(Guid id)
    {
        var relue = await _client.GetFromJsonAsync<InterventionDto>($"/api/interventions/{id}");
        Assert.NotNull(relue);
        return relue;
    }

    private async Task<InterventionDto> CreerIntervention(Guid? agentId, DateTime date, TimeSpan heure)
    {
        var reponse = await Poster(agentId, date, heure);
        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);

        return (await reponse.Content.ReadFromJsonAsync<InterventionDto>())!;
    }

    private Task<AgentDto> CreerAgent(string nom, bool actif = true) =>
        Creer<AgentDto>("/api/agents", new AgentCreateDto
        {
            Nom = nom,
            Prenom = "Test",
            DateEmbauche = new DateTime(2024, 1, 1),
            Latitude = 48.8584,
            Longitude = 2.2945,
            Actif = actif
        });

    private Task<AbsenceDto> CreerAbsence(Guid agentId, DateTime debut, DateTime fin) =>
        Creer<AbsenceDto>("/api/absences", new AbsenceCreateDto
        {
            AgentId = agentId,
            Type = "Maladie",
            DateDebut = debut,
            DateFin = fin
        });

    private async Task<T> Creer<T>(string route, object corps)
    {
        var reponse = await _client.PostAsJsonAsync(route, corps);

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);

        return await reponse.Content.ReadFromJsonAsync<T>()
            ?? throw new InvalidOperationException($"Réponse illisible sur {route}.");
    }
}
