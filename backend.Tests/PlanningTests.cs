using System.Net;
using System.Net.Http.Json;
using Klean.Api.DTOs;
using Klean.Api.Tests.Infrastructure;

namespace Klean.Api.Tests;

/// <summary>
/// Vue planning hebdomadaire.
///
/// Le jeu de référence est celui que <c>SPEC.md</c> annonce en validation de
/// l'étape 11 : <b>4 agents, 17 vacations, 34 heures, 3 non pourvues</b>. Il est
/// construit ici plutôt que dans un script SQL — un script de démonstration
/// dérive du schéma sans que rien ne le signale, alors qu'une fixture échoue le
/// jour où le modèle change.
///
/// Semaine du lundi 7 au dimanche 13 septembre 2026. Vacations de deux heures,
/// ce qui donne les 34 heures sans arrondi.
/// </summary>
[Collection(CollectionApi.Nom)]
public class PlanningTests : IAsyncLifetime
{
    private static readonly DateTime Lundi = new(2026, 9, 7);
    private static readonly DateTime Mardi = new(2026, 9, 8);
    private static readonly DateTime Mercredi = new(2026, 9, 9);
    private static readonly DateTime Jeudi = new(2026, 9, 10);
    private static readonly DateTime Vendredi = new(2026, 9, 11);
    private static readonly DateTime Samedi = new(2026, 9, 12);
    private static readonly DateTime Dimanche = new(2026, 9, 13);
    private static readonly DateTime LundiSuivant = new(2026, 9, 14);

    private static readonly TimeSpan Huit = new(8, 0, 0);
    private static readonly TimeSpan Onze = new(11, 0, 0);
    private static readonly TimeSpan Quatorze = new(14, 0, 0);
    private static readonly TimeSpan DixSept = new(17, 0, 0);

    private readonly FixtureApi _fixture;
    private HttpClient _client = null!;

    private PrestationDto _parties = null!;
    private PrestationDto _vitres = null!;
    private SiteDto _bellevue = null!;
    private SiteDto _cite = null!;

    private AgentDto _bamba = null!;
    private AgentDto _diallo = null!;
    private AgentDto _keita = null!;
    private AgentDto _sow = null!;

    public PlanningTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        var syndic = await Creer<ClientDto>("/api/clients", new ClientCreateDto
        {
            Nom = "Syndic Cité",
            Type = "Syndic"
        });

        _bellevue = await CreerSite(syndic.Id, "Résidence Bellevue", 48.8421, 2.3219);
        _cite = await CreerSite(syndic.Id, "Résidence Cité", 48.8530, 2.3499);

        _parties = await CreerPrestation(_bellevue.Id, "Nettoyage parties communes");
        _vitres = await CreerPrestation(_cite.Id, "Vitrerie");

        _bamba = await CreerAgent("Bamba");
        _diallo = await CreerAgent("Diallo");
        _keita = await CreerAgent("Keita");
        _sow = await CreerAgent("Sow");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ----------------------------------------------------- le jeu de référence

    [Fact]
    public async Task Le_jeu_de_reference_donne_4_agents_17_vacations_34_heures_3_non_pourvues()
    {
        await Semer();

        var planning = await Planning(Lundi);

        Assert.Equal(4, planning.Agents.Count);
        Assert.Equal(17, planning.TotalVacations);
        Assert.Equal(34, planning.TotalHeures);
        Assert.Equal(3, planning.TotalNonPourvues);
        Assert.Equal(3, planning.NonPourvues.Count);

        Assert.Equal(Lundi, planning.Lundi.Date);
        Assert.Equal(Dimanche, planning.Dimanche.Date);
    }

    [Fact]
    public async Task Les_agents_sont_en_lignes_triees_par_nom()
    {
        await Semer();

        var planning = await Planning(Lundi);

        Assert.Equal(["Bamba", "Diallo", "Keita", "Sow"], planning.Agents.Select(a => a.Nom));
        Assert.Equal([4, 4, 4, 3], planning.Agents.Select(a => a.NombreVacations));
        Assert.Equal([8, 8, 8, 6], planning.Agents.Select(a => a.Heures));
    }

    [Fact]
    public async Task Les_heures_par_agent_et_les_vacations_sans_agent_font_le_total()
    {
        await Semer();

        var planning = await Planning(Lundi);

        var affectees = planning.Agents.Sum(a => a.NombreVacations);
        var heuresAffectees = planning.Agents.Sum(a => a.Heures);

        Assert.Equal(15, affectees);
        Assert.Equal(30, heuresAffectees);

        // Les deux vacations jamais affectées n'ont pas de ligne d'agent, et
        // comptent pourtant dans le total : c'est tout l'intérêt de l'agrégat
        // serveur, un client qui additionne les lignes trouverait 30.
        Assert.Equal(17, planning.TotalVacations);
        Assert.Equal(34, planning.TotalHeures);
    }

    [Fact]
    public async Task Les_vacations_d_un_agent_sont_triees_par_date_puis_heure()
    {
        await Semer();

        var sow = (await Planning(Lundi)).Agents.Single(a => a.Nom == "Sow");

        Assert.Equal([Huit, Onze, Quatorze], sow.Vacations.Select(v => v.Heure));
        Assert.All(sow.Vacations, v => Assert.Equal(Vendredi, v.Date.Date));
    }

    [Fact]
    public async Task Le_site_et_la_prestation_sont_nommes_sur_chaque_vacation()
    {
        await Semer();

        var planning = await Planning(Lundi);

        Assert.All(planning.Agents.SelectMany(a => a.Vacations), v =>
        {
            Assert.False(string.IsNullOrWhiteSpace(v.SiteNom));
            Assert.False(string.IsNullOrWhiteSpace(v.PrestationLibelle));
            Assert.Equal(120, v.DureeMinutes);
        });

        Assert.Contains(planning.Agents.SelectMany(a => a.Vacations),
            v => v.SiteNom == "Résidence Bellevue" && v.PrestationLibelle == "Nettoyage parties communes");
        Assert.Contains(planning.Agents.SelectMany(a => a.Vacations),
            v => v.SiteNom == "Résidence Cité" && v.PrestationLibelle == "Vitrerie");
    }

    // ------------------------------------------------------------ non pourvues

    [Fact]
    public async Task Une_vacation_sans_agent_est_non_pourvue_avec_sa_cause()
    {
        await Semer();

        var planning = await Planning(Lundi);

        var orphelines = planning.NonPourvues.Where(n => n.AgentId is null).ToList();

        Assert.Equal(2, orphelines.Count);
        Assert.All(orphelines, n => Assert.Equal("Aucun agent affecté", n.Cause));
    }

    [Fact]
    public async Task Une_vacation_dont_l_agent_est_absent_est_non_pourvue_et_reste_sur_sa_ligne()
    {
        await Semer();

        var planning = await Planning(Lundi);

        var absente = Assert.Single(planning.NonPourvues.Where(n => n.AgentId == _keita.Id));
        Assert.Equal("Agent absent", absente.Cause);
        Assert.Equal(Jeudi, absente.Date.Date);

        // La duplication est voulue : le trou doit se voir à sa place dans la
        // journée de Keita, et se lister sans parcourir toutes les lignes.
        var keita = planning.Agents.Single(a => a.Nom == "Keita");
        var surSaLigne = keita.Vacations.Single(v => v.InterventionId == absente.InterventionId);

        Assert.True(surSaLigne.NonPourvue);
        Assert.Equal(4, keita.NombreVacations);
        Assert.All(keita.Vacations.Where(v => v.Date.Date != Jeudi), v => Assert.False(v.NonPourvue));
    }

    [Fact]
    public async Task L_absence_a_aussi_declenche_un_remplacement_a_pourvoir()
    {
        // Le planning et le cycle de remplacement décrivent le même trou. Si les
        // deux ne s'accordent pas, l'écran montre une vacation à pourvoir que le
        // panneau de remplacement ignore.
        await Semer();

        var remplacements = await _client.ListerAsync<RemplacementDto>("/api/remplacements");
        var nonPourvue = (await Planning(Lundi)).NonPourvues.Single(n => n.AgentId == _keita.Id);

        var remplacement = Assert.Single(remplacements);
        Assert.Equal(nonPourvue.InterventionId, remplacement.InterventionId);
        Assert.Equal("Recherche", remplacement.Statut);
    }

    [Fact]
    public async Task Une_vacation_annulee_sort_du_planning_et_des_agregats()
    {
        await Semer();

        // Samedi : Bamba n'y a rien, donc la création passe les filtres durs.
        await CreerVacation(_parties, _bamba.Id, Samedi, Huit, "Annulee");

        var planning = await Planning(Lundi);

        Assert.Equal(17, planning.TotalVacations);
        Assert.Equal(34, planning.TotalHeures);
        Assert.Equal(4, planning.Agents.Single(a => a.Nom == "Bamba").NombreVacations);
    }

    // ------------------------------------------------------------------ bornes

    [Fact]
    public async Task Une_vacation_du_dimanche_est_dans_la_semaine()
    {
        await Semer();

        await CreerVacation(_parties, _bamba.Id, Dimanche, Huit, "Planifiee");

        var planning = await Planning(Lundi);

        Assert.Equal(18, planning.TotalVacations);
        Assert.Equal(5, planning.Agents.Single(a => a.Nom == "Bamba").NombreVacations);
    }

    [Fact]
    public async Task Une_vacation_du_lundi_suivant_n_est_pas_dans_la_semaine()
    {
        await Semer();

        await CreerVacation(_parties, _bamba.Id, LundiSuivant, Huit, "Planifiee");

        Assert.Equal(17, (await Planning(Lundi)).TotalVacations);
        Assert.Equal(1, (await Planning(LundiSuivant)).TotalVacations);
    }

    [Fact]
    public async Task La_semaine_precedente_est_vide_mais_garde_ses_lignes_d_agents()
    {
        await Semer();

        var planning = await Planning(new DateTime(2026, 8, 31));

        Assert.Equal(0, planning.TotalVacations);
        Assert.Equal(0, planning.TotalHeures);
        Assert.Empty(planning.NonPourvues);

        // Une grille sans lignes ne dit pas qui est libre.
        Assert.Equal(4, planning.Agents.Count);
        Assert.All(planning.Agents, a => Assert.Empty(a.Vacations));
    }

    [Fact]
    public async Task Un_agent_sans_aucune_vacation_apparait_avec_une_ligne_vide()
    {
        await Semer();

        var nouveau = await CreerAgent("Zidane");

        var planning = await Planning(Lundi);

        var ligne = planning.Agents.Single(a => a.AgentId == nouveau.Id);
        Assert.Empty(ligne.Vacations);
        Assert.Equal(0, ligne.NombreVacations);
        Assert.Equal(0, ligne.Heures);
        Assert.Equal(5, planning.Agents.Count);
    }

    [Fact]
    public async Task Un_agent_inactif_qui_porte_encore_une_vacation_garde_sa_ligne()
    {
        // Sinon ses heures compteraient dans le total sans qu'aucune ligne ne les
        // explique, et le planning ne s'additionnerait plus à l'écran.
        await Semer();

        var sortie = await _client.PutAsJsonAsync($"/api/agents/{_sow.Id}", new AgentCreateDto
        {
            Nom = _sow.Nom,
            Prenom = _sow.Prenom,
            DateEmbauche = _sow.DateEmbauche,
            Latitude = _sow.Latitude,
            Longitude = _sow.Longitude,
            Actif = false
        });
        Assert.Equal(HttpStatusCode.NoContent, sortie.StatusCode);

        var planning = await Planning(Lundi);

        Assert.Equal(4, planning.Agents.Count);
        Assert.Equal(3, planning.Agents.Single(a => a.Nom == "Sow").NombreVacations);
        Assert.Equal(34, planning.TotalHeures);
    }

    // ------------------------------------------------------------- paramétrage

    [Fact]
    public async Task Une_date_qui_n_est_pas_un_lundi_renvoie_400()
    {
        var reponse = await _client.GetAsync("/api/planning?lundi=2026-09-09");

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);

        // Le nom du jour est en français : DayOfWeek.ToString() rend « wednesday »
        // quelle que soit la culture du serveur, et le message part vers un humain.
        var corps = await reponse.Content.ReadAsStringAsync();
        Assert.Contains("mercredi", corps);
        Assert.Contains("commence un lundi", corps);
        Assert.DoesNotContain("wednesday", corps.ToLowerInvariant());
    }

    [Fact]
    public async Task Le_parametre_lundi_est_obligatoire()
    {
        // Pas de valeur par défaut sur l'horloge du serveur : la réponse ne doit
        // pas dépendre du moment de l'appel.
        var reponse = await _client.GetAsync("/api/planning");

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("obligatoire", await reponse.Content.ReadAsStringAsync());
    }

    // ------------------------------------------------- isolation et droits

    [Fact]
    public async Task Le_planning_d_une_autre_entreprise_ignore_ces_donnees()
    {
        await Semer();

        var beta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);

        var vueBeta = await beta.GetFromJsonAsync<PlanningSemaineDto>(
            $"/api/planning?lundi={Lundi:yyyy-MM-dd}");

        Assert.NotNull(vueBeta);
        Assert.Equal(0, vueBeta.TotalVacations);
        Assert.Empty(vueBeta.Agents);
        Assert.Equal(17, (await Planning(Lundi)).TotalVacations);
    }

    [Fact]
    public async Task Le_chef_d_equipe_consulte_le_planning()
    {
        var chef = await _fixture.ClientDeRoleAsync(_client, "ChefEquipe", "chef.planning@test.local");

        var reponse = await chef.GetAsync($"/api/planning?lundi={Lundi:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }

    [Fact]
    public async Task L_agent_n_a_pas_acces_au_planning_general()
    {
        // La grille nomme tous les agents de l'entreprise. Le filtre multi-tenant
        // restreindrait ses interventions, pas la colonne des noms.
        var clientAgent = await _fixture.ClientDeRoleAsync(
            _client, "Agent", "agent.planning@test.local", agentId: _bamba.Id);

        var reponse = await clientAgent.GetAsync($"/api/planning?lundi={Lundi:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    [Fact]
    public async Task Le_client_syndic_n_a_pas_acces_au_planning()
    {
        var syndicId = (await _client.ListerAsync<ClientDto>("/api/clients")).Single().Id;
        var syndic = await _fixture.ClientDeRoleAsync(
            _client, "ClientSyndic", "syndic.planning@test.local", clientId: syndicId);

        var reponse = await syndic.GetAsync($"/api/planning?lundi={Lundi:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    [Fact]
    public async Task Sans_jeton_la_reponse_est_401()
    {
        var anonyme = _fixture.ClientAnonymeSansRedirection();

        var reponse = await anonyme.GetAsync($"/api/planning?lundi={Lundi:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
        Assert.Null(reponse.Headers.Location);
    }

    // ------------------------------------------------------------- outillage

    /// <summary>
    /// Le jeu de référence : 17 vacations de deux heures sur la semaine du
    /// 7 septembre 2026, dont 15 affectées à quatre agents et 2 jamais pourvues,
    /// puis un arrêt maladie de Keita le jeudi qui découvre la troisième.
    ///
    /// L'absence est saisie <b>après</b> les vacations, comme dans la vie : posée
    /// avant, la garde d'éligibilité refuserait à juste titre d'affecter Keita
    /// ce jour-là.
    /// </summary>
    private async Task Semer()
    {
        foreach (var jour in new[] { Lundi, Mardi, Mercredi, Jeudi })
        {
            await CreerVacation(_parties, _bamba.Id, jour, Huit, "Planifiee");
            await CreerVacation(_vitres, _diallo.Id, jour, Onze, "Planifiee");
        }

        foreach (var jour in new[] { Lundi, Mardi, Mercredi })
        {
            await CreerVacation(_parties, _keita.Id, jour, Quatorze, "Planifiee");
        }

        await CreerVacation(_vitres, _keita.Id, Jeudi, DixSept, "Planifiee");

        await CreerVacation(_parties, _sow.Id, Vendredi, Huit, "Planifiee");
        await CreerVacation(_vitres, _sow.Id, Vendredi, Onze, "Planifiee");
        await CreerVacation(_parties, _sow.Id, Vendredi, Quatorze, "Planifiee");

        // Jamais affectées.
        await CreerVacation(_parties, null, Jeudi, Quatorze, "Planifiee");
        await CreerVacation(_vitres, null, Vendredi, DixSept, "Planifiee");

        await Creer<AbsenceDto>("/api/absences", new AbsenceCreateDto
        {
            AgentId = _keita.Id,
            Type = "Maladie",
            DateDebut = Jeudi,
            DateFin = Jeudi
        });
    }

    private async Task<PlanningSemaineDto> Planning(DateTime lundi)
    {
        var reponse = await _client.GetAsync($"/api/planning?lundi={lundi:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);

        return await reponse.Content.ReadFromJsonAsync<PlanningSemaineDto>()
            ?? throw new InvalidOperationException("Planning illisible.");
    }

    private Task<InterventionDto> CreerVacation(
        PrestationDto prestation, Guid? agentId, DateTime date, TimeSpan heure, string statut) =>
        Creer<InterventionDto>("/api/interventions", new InterventionCreateDto
        {
            PrestationId = prestation.Id,
            AgentId = agentId,
            DatePrevue = date,
            HeurePrevue = heure,
            Statut = statut
        });

    private Task<SiteDto> CreerSite(Guid clientId, string nom, double latitude, double longitude) =>
        Creer<SiteDto>("/api/sites", new SiteCreateDto
        {
            ClientId = clientId,
            Nom = nom,
            Adresse = "1 rue de Test",
            Latitude = latitude,
            Longitude = longitude
        });

    private async Task<PrestationDto> CreerPrestation(Guid siteId, string libelle)
    {
        var contrat = await Creer<ContratDto>("/api/contrats", new ContratCreateDto
        {
            SiteId = siteId,
            DateDebut = new DateTime(2026, 1, 1),
            Frequence = "Du lundi au vendredi"
        });

        return await Creer<PrestationDto>("/api/prestations", new PrestationCreateDto
        {
            ContratId = contrat.Id,
            Libelle = libelle,
            DureeEstimeeMinutes = 120
        });
    }

    private Task<AgentDto> CreerAgent(string nom) =>
        Creer<AgentDto>("/api/agents", new AgentCreateDto
        {
            Nom = nom,
            Prenom = "Test",
            DateEmbauche = new DateTime(2024, 1, 1),
            Latitude = 48.8584,
            Longitude = 2.2945
        });

    private async Task<T> Creer<T>(string route, object corps)
    {
        var reponse = await _client.PostAsJsonAsync(route, corps);

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);

        return await reponse.Content.ReadFromJsonAsync<T>()
            ?? throw new InvalidOperationException(
                $"Réponse illisible sur {route} : {await reponse.Content.ReadAsStringAsync()}");
    }
}
