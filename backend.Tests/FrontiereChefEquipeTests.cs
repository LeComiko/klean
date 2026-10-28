using System.Net;
using System.Net.Http.Json;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Klean.Api.Tests.Infrastructure;

namespace Klean.Api.Tests;

/// <summary>
/// La frontière du chef d'équipe — étape 19, lot B.
///
/// Deux chefs, deux équipes, un agent sans équipe, et sur chaque donnée du tableau de
/// SPEC.md la même question : un chef voit-il son équipe, et rien qu'elle ? Le patron et
/// les responsables, eux, voient tout. C'est une frontière du même ordre que celle du
/// syndic : avec plusieurs chefs, une fuite montrerait à chacun les équipes des autres.
///
/// La semaine du lundi 9 novembre 2026 :
///
/// | Vacation | Prestation (titulaire) | Agent | Vue par |
/// |---|---|---|---|
/// | lundi 8 h | A (a1) | a1, absent ce jour | chef A |
/// | lundi 8 h | B (b1) | b1, absent ce jour | chef B |
/// | mardi 8 h | A (a1) | aucun | chef A, par le titulaire |
/// | mardi 8 h | B (b1) | aucun | chef B, par le titulaire |
/// | mercredi 8 h | sans titulaire | aucun | aucun chef |
/// | jeudi 14 h | A (a1) | b1 | chef B, par l'agent — pas A |
///
/// La dernière ligne garde la priorité de l'agent sur le titulaire : une vacation
/// affectée appartient à l'équipe de qui la fait.
/// </summary>
[Collection(CollectionApi.Nom)]
public class FrontiereChefEquipeTests : IAsyncLifetime
{
    private static readonly DateTime Lundi = new(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc);

    private readonly FixtureApi _fixture;

    private HttpClient _admin = null!;
    private HttpClient _chefA = null!;
    private HttpClient _chefB = null!;

    private Guid _a1, _a2, _b1, _libre;
    private Guid _lundiA, _lundiB, _mardiA, _mardiB, _mercrediSansTitulaire, _jeudiCroisee;
    private Guid _absenceA, _absenceB;
    private Guid _remplacementA, _remplacementB;

    public FrontiereChefEquipeTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        _chefA = await _fixture.ClientDeRoleAsync(_admin, RolesKlean.ChefEquipe, "chef.a@test.local");
        _chefB = await _fixture.ClientDeRoleAsync(_admin, RolesKlean.ChefEquipe, "chef.b@test.local");
        var chefA = await IdDuCompte(_chefA);
        var chefB = await IdDuCompte(_chefB);

        _a1 = await Agent("Arnaud", chefA);
        _a2 = await Agent("Aubert", chefA);
        _b1 = await Agent("Bernard", chefB);
        _libre = await Agent("Lefebvre", null);

        var client = await Creer<ClientDto>("/api/clients", new ClientCreateDto { Nom = "Syndic", Type = "Syndic" });
        var site = await Creer<SiteDto>("/api/sites", new SiteCreateDto
        {
            ClientId = client.Id, Nom = "Résidence", Adresse = "1 rue Haute", Latitude = 48.85, Longitude = 2.35
        });
        var contrat = await Creer<ContratDto>("/api/contrats", new ContratCreateDto
        {
            SiteId = site.Id, DateDebut = new DateTime(2026, 1, 1), Frequence = "À la demande"
        });

        var prestationA = await Prestation(contrat.Id, "Halls A", _a1);
        var prestationB = await Prestation(contrat.Id, "Halls B", _b1);
        var prestationLibre = await Prestation(contrat.Id, "Conteneurs", null);

        _lundiA = await Vacation(prestationA, _a1, 0, 8);
        _lundiB = await Vacation(prestationB, _b1, 0, 8);
        _mardiA = await Vacation(prestationA, null, 1, 8);
        _mardiB = await Vacation(prestationB, null, 1, 8);
        _mercrediSansTitulaire = await Vacation(prestationLibre, null, 2, 8);
        _jeudiCroisee = await Vacation(prestationA, _b1, 3, 14);

        // Chaque absence ouvre un remplacement sur la vacation du lundi de l'agent.
        _absenceA = await Absence(_a1);
        _absenceB = await Absence(_b1);

        var remplacements = await _admin.ListerAsync<RemplacementDto>("/api/remplacements");
        _remplacementA = remplacements.Single(r => r.InterventionId == _lundiA).Id;
        _remplacementB = remplacements.Single(r => r.InterventionId == _lundiB).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------- les agents

    [Fact]
    public async Task Chaque_chef_ne_liste_que_ses_agents()
    {
        Assert.Equal(
            new[] { _a1, _a2 }.Order(),
            (await _chefA.ListerAsync<AgentDto>("/api/agents")).Select(a => a.Id).Order());
        Assert.Equal(
            [_b1],
            (await _chefB.ListerAsync<AgentDto>("/api/agents")).Select(a => a.Id));
    }

    [Fact]
    public async Task L_agent_d_une_autre_equipe_est_introuvable()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _chefA.GetAsync($"/api/agents/{_b1}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _chefA.GetAsync($"/api/agents/{_libre}")).StatusCode);
    }

    // ------------------------------------------------------------- les absences

    [Fact]
    public async Task Chaque_chef_ne_voit_que_les_absences_de_son_equipe()
    {
        Assert.Equal([_absenceA], (await _chefA.ListerAsync<AbsenceDto>("/api/absences")).Select(a => a.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await _chefA.GetAsync($"/api/absences/{_absenceB}")).StatusCode);
    }

    // ------------------------------------------------------------- les interventions

    [Fact]
    public async Task Le_chef_voit_les_vacations_de_ses_agents_et_celles_a_pourvoir_de_ses_titulaires()
    {
        Assert.Equal(
            new[] { _lundiA, _mardiA }.Order(),
            (await _chefA.ListerAsync<InterventionDto>("/api/interventions")).Select(i => i.Id).Order());
    }

    [Fact]
    public async Task Une_vacation_affectee_appartient_a_l_equipe_de_son_agent_et_non_du_titulaire()
    {
        // ⚠️ La prestation du jeudi a pour titulaire a1, mais c'est b1 qui la fait : elle
        // est à B. La donner à A montrerait à un chef le travail d'un agent qu'il
        // n'encadre pas.
        var vuesParB = (await _chefB.ListerAsync<InterventionDto>("/api/interventions")).Select(i => i.Id);

        Assert.Equal(new[] { _lundiB, _mardiB, _jeudiCroisee }.Order(), vuesParB.Order());
        Assert.Equal(HttpStatusCode.NotFound,
            (await _chefA.GetAsync($"/api/interventions/{_jeudiCroisee}")).StatusCode);
    }

    [Fact]
    public async Task Une_vacation_sans_agent_ni_titulaire_n_est_vue_par_aucun_chef()
    {
        foreach (var chef in new[] { _chefA, _chefB })
        {
            Assert.Equal(HttpStatusCode.NotFound,
                (await chef.GetAsync($"/api/interventions/{_mercrediSansTitulaire}")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK,
            (await _admin.GetAsync($"/api/interventions/{_mercrediSansTitulaire}")).StatusCode);
    }

    [Fact]
    public async Task Le_chef_ne_modifie_pas_une_vacation_d_une_autre_equipe()
    {
        var reponse = await _chefA.PutAsJsonAsync($"/api/interventions/{_lundiB}", new InterventionCreateDto
        {
            PrestationId = Guid.NewGuid(), AgentId = null, DatePrevue = Lundi, HeurePrevue = new TimeSpan(8, 0, 0),
            Statut = "Annulee"
        });

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
        Assert.Equal("Planifiee", (await _admin.GetFromJsonAsync<InterventionDto>($"/api/interventions/{_lundiB}"))!.Statut);
    }

    [Fact]
    public async Task Le_chef_n_affecte_pas_un_agent_hors_de_son_equipe()
    {
        var mardi = (await _admin.GetFromJsonAsync<InterventionDto>($"/api/interventions/{_mardiA}"))!;

        foreach (var etranger in new[] { _b1, _libre })
        {
            var reponse = await _chefA.PutAsJsonAsync($"/api/interventions/{_mardiA}", new InterventionCreateDto
            {
                PrestationId = mardi.PrestationId, AgentId = etranger, DatePrevue = mardi.DatePrevue,
                HeurePrevue = mardi.HeurePrevue, Statut = mardi.Statut
            });

            Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        }

        Assert.Null((await _admin.GetFromJsonAsync<InterventionDto>($"/api/interventions/{_mardiA}"))!.AgentId);
    }

    // ------------------------------------------------------------- le planning

    [Fact]
    public async Task Le_planning_du_chef_ne_porte_que_son_equipe()
    {
        var semaine = await Planning(_chefA);

        Assert.Equal(new[] { _a1, _a2 }.Order(), semaine.Agents.Select(a => a.AgentId).Order());
        Assert.Equal(2, semaine.TotalVacations);

        // Le lundi d'a1, absent, et le mardi à pourvoir de son titulaire.
        Assert.Equal(new[] { _lundiA, _mardiA }.Order(), semaine.NonPourvues.Select(v => v.InterventionId).Order());
    }

    [Fact]
    public async Task Le_planning_du_chef_ne_nomme_personne_d_une_autre_equipe()
    {
        // Sur le corps brut : un nom peut sortir par un champ qu'aucune assertion typée
        // ne regarde.
        var corps = await _chefA.GetStringAsync($"/api/planning?lundi={Lundi:yyyy-MM-dd}");

        Assert.DoesNotContain("Bernard", corps);
        Assert.DoesNotContain("Lefebvre", corps);
        Assert.Contains("Arnaud", corps);
    }

    [Fact]
    public async Task Le_responsable_voit_toujours_tout()
    {
        var responsable = await _fixture.ClientDeRoleAsync(_admin, RolesKlean.Responsable, "resp@test.local");

        var semaine = await Planning(responsable);

        Assert.Equal(4, semaine.Agents.Count);
        Assert.Equal(6, semaine.TotalVacations);
        Assert.Equal(6, (await responsable.ListerAsync<InterventionDto>("/api/interventions")).Count);
        Assert.Equal(2, (await responsable.ListerAsync<RemplacementDto>("/api/remplacements")).Count);
    }

    [Fact]
    public async Task Un_chef_sans_equipe_ne_voit_rien_des_equipes()
    {
        var chefSeul = await _fixture.ClientDeRoleAsync(_admin, RolesKlean.ChefEquipe, "chef.seul@test.local");

        Assert.Empty(await chefSeul.ListerAsync<AgentDto>("/api/agents"));
        Assert.Empty(await chefSeul.ListerAsync<AbsenceDto>("/api/absences"));
        Assert.Empty(await chefSeul.ListerAsync<InterventionDto>("/api/interventions"));
        Assert.Empty(await chefSeul.ListerAsync<RemplacementDto>("/api/remplacements"));
        Assert.Equal(0, (await Planning(chefSeul)).TotalVacations);
    }

    // ------------------------------------------------------------- les remplacements

    [Fact]
    public async Task Chaque_chef_ne_voit_que_les_remplacements_de_son_equipe()
    {
        Assert.Equal([_remplacementA],
            (await _chefA.ListerAsync<RemplacementDto>("/api/remplacements")).Select(r => r.Id));
        Assert.Equal(HttpStatusCode.NotFound,
            (await _chefA.GetAsync($"/api/remplacements/{_remplacementB}")).StatusCode);
    }

    [Fact]
    public async Task Les_candidats_du_chef_sont_son_equipe()
    {
        // Mardi, personne n'est absent : le patron voit les quatre agents, le chef A ses
        // deux.
        var parLeChef = await _chefA.GetFromJsonAsync<List<CandidatDto>>($"/api/interventions/{_mardiA}/candidats");
        var parLePatron = await _admin.GetFromJsonAsync<List<CandidatDto>>($"/api/interventions/{_mardiA}/candidats");

        Assert.Equal(new[] { _a1, _a2 }.Order(), parLeChef!.Select(c => c.AgentId).Order());
        Assert.Equal(4, parLePatron!.Count);
    }

    [Fact]
    public async Task Les_candidats_d_un_remplacement_sont_aussi_cadres()
    {
        // Lundi, a1 et b1 sont absents : il reste a2 au chef A, a2 et Lefebvre au patron.
        var parLeChef = await _chefA.GetFromJsonAsync<List<CandidatDto>>($"/api/remplacements/{_remplacementA}/candidats");
        var parLePatron = await _admin.GetFromJsonAsync<List<CandidatDto>>($"/api/remplacements/{_remplacementA}/candidats");

        Assert.Equal([_a2], parLeChef!.Select(c => c.AgentId));
        Assert.Equal(new[] { _a2, _libre }.Order(), parLePatron!.Select(c => c.AgentId).Order());

        Assert.Equal(HttpStatusCode.NotFound,
            (await _chefA.GetAsync($"/api/remplacements/{_remplacementB}/candidats")).StatusCode);
    }

    [Fact]
    public async Task Le_chef_pourvoit_une_vacation_de_son_equipe_par_le_cycle()
    {
        var proposition = await _chefA.PostAsJsonAsync($"/api/remplacements/{_remplacementA}/proposer",
            new ProposerDto { AgentId = _a2 });
        Assert.Equal(HttpStatusCode.OK, proposition.StatusCode);

        var acceptation = await _chefA.PostAsync($"/api/remplacements/{_remplacementA}/accepter", null);
        Assert.Equal(HttpStatusCode.OK, acceptation.StatusCode);

        Assert.Equal(_a2, (await _admin.GetFromJsonAsync<InterventionDto>($"/api/interventions/{_lundiA}"))!.AgentId);
    }

    [Fact]
    public async Task Le_chef_ne_propose_pas_un_agent_hors_de_son_equipe()
    {
        // Lefebvre est candidat pour le patron. Pour le chef A, il n'existe pas.
        var reponse = await _chefA.PostAsJsonAsync($"/api/remplacements/{_remplacementA}/proposer",
            new ProposerDto { AgentId = _libre });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Null((await _admin.GetFromJsonAsync<RemplacementDto>($"/api/remplacements/{_remplacementA}"))!.AgentRemplacantId);
    }

    [Fact]
    public async Task Le_chef_n_agit_pas_sur_le_remplacement_d_une_autre_equipe()
    {
        var proposition = await _chefA.PostAsJsonAsync($"/api/remplacements/{_remplacementB}/proposer",
            new ProposerDto { AgentId = _a2 });
        Assert.Equal(HttpStatusCode.NotFound, proposition.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await _chefA.PostAsync($"/api/remplacements/{_remplacementB}/refuser", null)).StatusCode);
    }

    [Fact]
    public async Task Le_chef_ouvre_un_remplacement_dans_son_equipe_et_nulle_part_ailleurs()
    {
        var ailleurs = await _chefA.PostAsync($"/api/interventions/{_mardiB}/remplacement", null);
        Assert.Equal(HttpStatusCode.NotFound, ailleurs.StatusCode);

        var chezLui = await _chefA.PostAsync($"/api/interventions/{_mardiA}/remplacement", null);
        Assert.Equal(HttpStatusCode.Created, chezLui.StatusCode);

        var ouverts = await _admin.ListerAsync<RemplacementDto>("/api/remplacements");
        Assert.DoesNotContain(ouverts, r => r.InterventionId == _mardiB);
        Assert.Contains(ouverts, r => r.InterventionId == _mardiA);
    }

    // ------------------------------------------------------------- outils

    private async Task<PlanningSemaineDto> Planning(HttpClient client) =>
        (await client.GetFromJsonAsync<PlanningSemaineDto>($"/api/planning?lundi={Lundi:yyyy-MM-dd}"))!;

    private static async Task<Guid> IdDuCompte(HttpClient client) =>
        (await client.GetFromJsonAsync<ProfilDto>("/api/auth/moi"))!.Id;

    private async Task<T> Creer<T>(string route, object corps)
    {
        var reponse = await _admin.PostAsJsonAsync(route, corps);

        if (!reponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{route} : {await reponse.Content.ReadAsStringAsync()}");
        }

        return (await reponse.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<Guid> Agent(string nom, Guid? chef) =>
        (await Creer<AgentDto>("/api/agents", new AgentCreateDto
        {
            Nom = nom, Prenom = "Test", DateEmbauche = new DateTime(2024, 1, 1), ChefEquipeId = chef
        })).Id;

    private async Task<Guid> Prestation(Guid contrat, string libelle, Guid? titulaire) =>
        (await Creer<PrestationDto>("/api/prestations", new PrestationCreateDto
        {
            ContratId = contrat, Libelle = libelle, DureeEstimeeMinutes = 120, AgentTitulaireId = titulaire
        })).Id;

    private async Task<Guid> Vacation(Guid prestation, Guid? agent, int jour, int heure) =>
        (await Creer<InterventionDto>("/api/interventions", new InterventionCreateDto
        {
            PrestationId = prestation, AgentId = agent, DatePrevue = Lundi.AddDays(jour),
            HeurePrevue = new TimeSpan(heure, 0, 0), Statut = "Planifiee"
        })).Id;

    private async Task<Guid> Absence(Guid agent) =>
        (await Creer<AbsenceDto>("/api/absences", new AbsenceCreateDto
        {
            AgentId = agent, Type = "Conge", DateDebut = Lundi, DateFin = Lundi
        })).Id;
}
