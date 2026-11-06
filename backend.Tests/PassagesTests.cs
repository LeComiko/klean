using System.Net;
using System.Net.Http.Json;
using Klean.Api.Data;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Klean.Api.Services;
using Klean.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Klean.Api.Tests;

/// <summary>
/// Le passage de l'agent — étape 25, lot A : sa journée, et les deux pointages qui
/// certifient une vacation.
///
/// Trois choses à garder, et chacune a sa raison dans SPEC.md : seuls deux pointages
/// <c>SurSite</c> certifient ; aucune coordonnée n'est enregistrée ; et une vacation
/// pointée ne change plus — ni d'agent, ni de date, ni de statut.
///
/// Décor : le mercredi 10 mars 2027, 10 h à Paris. Une résidence sur le parvis
/// Notre-Dame ; Sow y a une vacation aujourd'hui, une demain ; Keita une aujourd'hui.
/// </summary>
[Collection(CollectionApi.Nom)]
public class PassagesTests : IAsyncLifetime
{
    private static readonly DateOnly Aujourdhui = new(2027, 3, 10);
    private const double LatitudeSite = 48.8530;
    private const double LongitudeSite = 2.3499;

    /// <summary>Environ 111 m par millième de degré de latitude.</summary>
    private static PositionDto AuNord(double metres, double precision = 15) =>
        new() { Latitude = LatitudeSite + metres / 111_200, Longitude = LongitudeSite, PrecisionMetres = precision };

    private static readonly PositionDto SurPlace = AuNord(0);

    private readonly FixtureApi _fixture;
    private HttpClient _admin = null!;
    private HttpClient _sow = null!;
    private PrestationDto _prestation = null!;
    private AgentDto _agentSow = null!;
    private AgentDto _agentKeita = null!;
    private InterventionDto _aujourdhui = null!;

    public PassagesTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _fixture.Fabrique.Horloge.Fixer(Aujourdhui);
        _admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        var syndic = await Creer<ClientDto>("/api/clients", new ClientCreateDto { Nom = "Syndic Cité", Type = "Syndic" });
        var site = await Creer<SiteDto>("/api/sites", new SiteCreateDto
        {
            ClientId = syndic.Id,
            Nom = "Résidence Cité",
            Adresse = "1 parvis Notre-Dame",
            Ville = "Paris",
            Latitude = LatitudeSite,
            Longitude = LongitudeSite,
            Consignes = "Badge à l'accueil"
        });
        var contrat = await Creer<ContratDto>("/api/contrats", new ContratCreateDto
        {
            SiteId = site.Id, DateDebut = new DateTime(2027, 1, 1), Frequence = "Quotidien"
        });
        _prestation = await Creer<PrestationDto>("/api/prestations", new PrestationCreateDto
        {
            ContratId = contrat.Id, Libelle = "Parties communes", DureeEstimeeMinutes = 90
        });

        _agentSow = await CreerAgent("Sow");
        _agentKeita = await CreerAgent("Keita");

        _aujourdhui = await CreerVacation(_agentSow.Id, Aujourdhui, new TimeSpan(8, 0, 0));
        _sow = await _fixture.ClientDeRoleAsync(_admin, RolesKlean.Agent, "sow@test.local", agentId: _agentSow.Id);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------------ la journée

    [Fact]
    public async Task La_journee_ne_porte_que_les_vacations_du_jour_de_l_agent()
    {
        await CreerVacation(_agentSow.Id, Aujourdhui.AddDays(1), new TimeSpan(8, 0, 0));
        await CreerVacation(_agentKeita.Id, Aujourdhui, new TimeSpan(14, 0, 0));

        var journee = await Journee(_sow);

        var vacation = Assert.Single(journee);
        Assert.Equal(_aujourdhui.Id, vacation.Id);
        Assert.Equal(("Résidence Cité", "1 parvis Notre-Dame", "Paris", "Badge à l'accueil"),
            (vacation.Site, vacation.Adresse, vacation.Ville, vacation.Consignes));
        Assert.Equal(("Parties communes", 90, "Planifiee"), (vacation.Prestation, vacation.DureeEstimeeMinutes, vacation.Statut));
        Assert.Null(vacation.Debut);
    }

    [Fact]
    public async Task Une_vacation_laissee_en_cours_reste_dans_la_journee_du_lendemain_et_se_termine()
    {
        await Demarrer(_aujourdhui.Id, SurPlace);
        _fixture.Fabrique.Horloge.Fixer(Aujourdhui.AddDays(1));

        Assert.Equal([_aujourdhui.Id], (await Journee(_sow)).Select(v => v.Id));

        var fin = await _sow.PostAsJsonAsync($"/api/interventions/{_aujourdhui.Id}/terminer", Corps(SurPlace));
        Assert.Equal(HttpStatusCode.OK, fin.StatusCode);
    }

    [Fact]
    public async Task La_journee_est_reservee_au_role_agent()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await _admin.GetAsync("/api/interventions/ma-journee")).StatusCode);
    }

    // ------------------------------------------------------------------ la certification

    [Fact]
    public async Task Deux_pointages_sur_site_certifient_le_passage()
    {
        var debut = await Demarrer(_aujourdhui.Id, AuNord(150));
        Assert.Equal(("EnCours", "SurSite"), (debut.Statut, debut.Debut!.Preuve));
        Assert.Equal(150, debut.Debut.DistanceMetres!.Value, tolerance: 2);

        var fin = await Terminer(_aujourdhui.Id, SurPlace);

        Assert.Equal(("Terminee", "SurSite", 0), (fin.Statut, fin.Fin!.Preuve, fin.Fin.DistanceMetres));
        Assert.Equal("Terminee", (await Relire(_aujourdhui.Id)).Statut);
    }

    [Fact]
    public async Task Un_debut_hors_du_rayon_laisse_le_passage_non_certifie_meme_fini_sur_place()
    {
        // ⚠️ La fin seule ne suffit pas : démarrer de chez soi et finir sur place ne prouve
        // pas que la vacation s'est passée sur le site.
        Assert.Equal("HorsRayon", (await Demarrer(_aujourdhui.Id, AuNord(1_000))).Debut!.Preuve);

        var fin = await Terminer(_aujourdhui.Id, SurPlace);

        Assert.Equal("NonCertifiee", fin.Statut);
    }

    [Fact]
    public async Task Une_fin_sans_position_laisse_le_passage_non_certifie_et_le_dit()
    {
        await Demarrer(_aujourdhui.Id, SurPlace);

        var fin = await Terminer(_aujourdhui.Id, position: null);

        Assert.Equal(("NonCertifiee", "NonLocalisee"), (fin.Statut, fin.Fin!.Preuve));
        Assert.Null(fin.Fin.DistanceMetres);
        Assert.Null(fin.Fin.PrecisionMetres);
    }

    [Fact]
    public async Task Une_position_imprecise_ne_certifie_pas()
    {
        await Demarrer(_aujourdhui.Id, SurPlace);

        var fin = await Terminer(_aujourdhui.Id, AuNord(50, precision: 300));

        Assert.Equal(("NonCertifiee", "Imprecise"), (fin.Statut, fin.Fin!.Preuve));
    }

    [Theory]
    [InlineData(0, 15, PreuvePointage.SurSite)]
    [InlineData(195, 15, PreuvePointage.SurSite)]
    [InlineData(195, 100, PreuvePointage.SurSite)]
    [InlineData(250, 15, PreuvePointage.HorsRayon)]
    [InlineData(150, 101, PreuvePointage.Imprecise)]
    [InlineData(250, 300, PreuvePointage.Imprecise)]
    // Dix kilomètres à trois cents mètres près : l'imprécision ne laisse aucun doute.
    [InlineData(10_000, 300, PreuvePointage.HorsRayon)]
    public void La_preuve_se_tranche_aux_bornes(double metres, double precision, PreuvePointage attendue)
    {
        var (preuve, _, _) = ServicePassage.Evaluer(AuNord(metres, precision), LatitudeSite, LongitudeSite);

        Assert.Equal(attendue, preuve);
    }

    [Fact]
    public void Sans_position_la_preuve_est_non_localisee_sans_distance()
    {
        Assert.Equal((PreuvePointage.NonLocalisee, (int?)null, (int?)null),
            ServicePassage.Evaluer(null, LatitudeSite, LongitudeSite));
    }

    // ------------------------------------------------------------------ la minimisation

    [Fact]
    public async Task Aucune_coordonnee_n_est_enregistree()
    {
        await Demarrer(_aujourdhui.Id, AuNord(150));

        // ⚠️ La garde est structurelle : la table n'a aucune colonne pour recevoir une
        // position. Une latitude ajoutée demain ferait rougir ce test avant d'arriver en
        // production — et c'est là qu'elle se verrait, pas dans une assertion sur une valeur.
        var colonnes = await LireAsync(db => db.Database
            .SqlQuery<string>($"""
                SELECT column_name AS "Value" FROM information_schema.columns
                WHERE table_name = 'Pointages' ORDER BY column_name
                """)
            .ToListAsync());

        Assert.Equal(
            ["AuteurId", "DistanceMetres", "EntrepriseId", "Horodatage", "Id", "InterventionId",
             "Moment", "PrecisionMetres", "Preuve"],
            colonnes);

        var pointage = await LireAsync(db => db.Pointages.AsNoTracking().SingleAsync());
        Assert.Equal((PreuvePointage.SurSite, 15), (pointage.Preuve, pointage.PrecisionMetres));
        Assert.Equal(Aujourdhui, DateOnly.FromDateTime(pointage.Horodatage));
    }

    [Fact]
    public async Task Une_position_illisible_est_refusee_en_francais()
    {
        var reponse = await _sow.PostAsJsonAsync($"/api/interventions/{_aujourdhui.Id}/demarrer",
            Corps(new PositionDto { Latitude = 95, Longitude = 2, PrecisionMetres = 10 }));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("Position illisible", await reponse.Content.ReadAsStringAsync());
        Assert.Equal("Planifiee", (await Relire(_aujourdhui.Id)).Statut);
    }

    // ------------------------------------------------------------------ qui pointe, et quand

    [Fact]
    public async Task Seul_l_agent_affecte_pointe_sa_vacation()
    {
        var keita = await _fixture.ClientDeRoleAsync(_admin, RolesKlean.Agent, "keita@test.local", agentId: _agentKeita.Id);

        var parUnCollegue = await keita.PostAsJsonAsync($"/api/interventions/{_aujourdhui.Id}/demarrer", Corps(SurPlace));
        Assert.Equal(HttpStatusCode.NotFound, parUnCollegue.StatusCode);

        // Un chef qui pointerait pour son agent viderait la preuve de son sens.
        var parLeChef = await _admin.PostAsJsonAsync($"/api/interventions/{_aujourdhui.Id}/demarrer", Corps(SurPlace));
        Assert.Equal(HttpStatusCode.Forbidden, parLeChef.StatusCode);

        Assert.Equal("Planifiee", (await Relire(_aujourdhui.Id)).Statut);
    }

    [Fact]
    public async Task Seule_une_vacation_du_jour_se_demarre()
    {
        var demain = await CreerVacation(_agentSow.Id, Aujourdhui.AddDays(1), new TimeSpan(8, 0, 0));

        var reponse = await _sow.PostAsJsonAsync($"/api/interventions/{demain.Id}/demarrer", Corps(SurPlace));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("seule une vacation du jour", await reponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Les_transitions_impossibles_sont_refusees_et_nommees()
    {
        var finAvantDebut = await _sow.PostAsJsonAsync($"/api/interventions/{_aujourdhui.Id}/terminer", Corps(SurPlace));
        Assert.Equal(HttpStatusCode.BadRequest, finAvantDebut.StatusCode);
        Assert.Contains("La démarrer d'abord", await finAvantDebut.Content.ReadAsStringAsync());

        await Demarrer(_aujourdhui.Id, SurPlace);

        var secondDebut = await _sow.PostAsJsonAsync($"/api/interventions/{_aujourdhui.Id}/demarrer", Corps(SurPlace));
        Assert.Equal(HttpStatusCode.BadRequest, secondDebut.StatusCode);

        await Terminer(_aujourdhui.Id, SurPlace);

        var secondeFin = await _sow.PostAsJsonAsync($"/api/interventions/{_aujourdhui.Id}/terminer", Corps(SurPlace));
        Assert.Equal(HttpStatusCode.BadRequest, secondeFin.StatusCode);
    }

    [Fact]
    public async Task Une_vacation_annulee_ne_se_demarre_pas()
    {
        await Modifier(_aujourdhui, "Annulee");

        var reponse = await _sow.PostAsJsonAsync($"/api/interventions/{_aujourdhui.Id}/demarrer", Corps(SurPlace));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Un_debut_deja_ecrit_rend_409_et_non_500()
    {
        // Ce que laisserait un double appui : le premier début écrit pendant que le second
        // lisait encore une vacation planifiée. L'index unique tranche, et le service le dit.
        await AvecDbAsync(async db =>
        {
            db.Pointages.Add(new Pointage
            {
                EntrepriseId = _fixture.EntrepriseAlpha,
                InterventionId = _aujourdhui.Id,
                Moment = MomentPointage.Debut,
                Horodatage = DateTime.UtcNow,
                Preuve = PreuvePointage.SurSite,
                AuteurId = Guid.NewGuid()
            });
            await db.SaveChangesAsync();
        });

        var reponse = await _sow.PostAsJsonAsync($"/api/interventions/{_aujourdhui.Id}/demarrer", Corps(SurPlace));

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        Assert.Contains("Recharger", await reponse.Content.ReadAsStringAsync());
        Assert.Equal(1, await LireAsync(db => db.Pointages.CountAsync()));
    }

    // ------------------------------------------------------------------ la saisie manuelle

    [Theory]
    [InlineData("Terminee")]
    [InlineData("EnCours")]
    public async Task Les_statuts_du_pointage_ne_se_saisissent_pas(string statut)
    {
        var creation = await PosterVacation(_agentKeita.Id, Aujourdhui, new TimeSpan(14, 0, 0), statut);
        Assert.Equal(HttpStatusCode.BadRequest, creation.StatusCode);
        Assert.Contains("NonCertifiee", await creation.Content.ReadAsStringAsync());

        var modification = await Modifier(_aujourdhui, statut);
        Assert.Equal(HttpStatusCode.BadRequest, modification.StatusCode);
    }

    [Fact]
    public async Task Une_vacation_faite_sans_pointage_se_saisit_non_certifiee()
    {
        Assert.Equal(HttpStatusCode.NoContent, (await Modifier(_aujourdhui, "NonCertifiee")).StatusCode);
        Assert.Equal("NonCertifiee", (await Relire(_aujourdhui.Id)).Statut);
    }

    [Fact]
    public async Task Une_ligne_terminee_avant_l_etape_reste_modifiable_sans_changer_de_statut()
    {
        await AvecDbAsync(db => db.Interventions
            .Where(i => i.Id == _aujourdhui.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Statut, StatutIntervention.Terminee)));

        Assert.Equal(HttpStatusCode.NoContent, (await Modifier(_aujourdhui, "Terminee")).StatusCode);
    }

    [Fact]
    public async Task Une_vacation_pointee_ne_se_modifie_ni_ne_se_supprime()
    {
        await Demarrer(_aujourdhui.Id, SurPlace);

        var modification = await Modifier(_aujourdhui, "Annulee");
        Assert.Equal(HttpStatusCode.Conflict, modification.StatusCode);
        Assert.Contains("pointée par son agent", await modification.Content.ReadAsStringAsync());

        var suppression = await _admin.DeleteAsync($"/api/interventions/{_aujourdhui.Id}");
        Assert.Equal(HttpStatusCode.Conflict, suppression.StatusCode);
        Assert.Contains("pointée par son agent", await suppression.Content.ReadAsStringAsync());

        Assert.Equal("EnCours", (await Relire(_aujourdhui.Id)).Statut);
    }

    [Fact]
    public async Task Un_remplacement_ne_change_pas_l_agent_d_une_vacation_demarree()
    {
        var ouverture = await _admin.PostAsync($"/api/interventions/{_aujourdhui.Id}/remplacement", null);
        var remplacement = (await ouverture.Content.ReadFromJsonAsync<RemplacementDto>())!;

        var proposition = await _admin.PostAsJsonAsync($"/api/remplacements/{remplacement.Id}/proposer",
            new ProposerDto { AgentId = _agentKeita.Id });
        Assert.Equal(HttpStatusCode.OK, proposition.StatusCode);

        await Demarrer(_aujourdhui.Id, SurPlace);

        var acceptation = await _admin.PostAsync($"/api/remplacements/{remplacement.Id}/accepter", null);

        Assert.Equal(HttpStatusCode.BadRequest, acceptation.StatusCode);
        Assert.Contains("ne change plus d'agent", await acceptation.Content.ReadAsStringAsync());
        Assert.Equal(_agentSow.Id, (await Relire(_aujourdhui.Id)).AgentId);
    }

    // ------------------------------------------------------------------ outils

    private static PointageCreateDto Corps(PositionDto? position) => new() { Position = position };

    private async Task<List<VacationAgentDto>> Journee(HttpClient client) =>
        (await client.GetFromJsonAsync<List<VacationAgentDto>>("/api/interventions/ma-journee"))!;

    private Task<VacationAgentDto> Demarrer(Guid id, PositionDto? position) => Pointer(id, "demarrer", position);

    private Task<VacationAgentDto> Terminer(Guid id, PositionDto? position) => Pointer(id, "terminer", position);

    private async Task<VacationAgentDto> Pointer(Guid id, string geste, PositionDto? position)
    {
        var reponse = await _sow.PostAsJsonAsync($"/api/interventions/{id}/{geste}", Corps(position));

        Assert.True(reponse.IsSuccessStatusCode, await reponse.Content.ReadAsStringAsync());
        return (await reponse.Content.ReadFromJsonAsync<VacationAgentDto>())!;
    }

    private async Task<InterventionDto> Relire(Guid id) =>
        (await _admin.GetFromJsonAsync<InterventionDto>($"/api/interventions/{id}"))!;

    private Task<HttpResponseMessage> Modifier(InterventionDto vacation, string statut) =>
        _admin.PutAsJsonAsync($"/api/interventions/{vacation.Id}", new InterventionCreateDto
        {
            PrestationId = vacation.PrestationId,
            AgentId = vacation.AgentId,
            DatePrevue = vacation.DatePrevue,
            HeurePrevue = vacation.HeurePrevue,
            Statut = statut
        });

    private Task<HttpResponseMessage> PosterVacation(Guid agentId, DateOnly jour, TimeSpan heure, string statut = "Planifiee") =>
        _admin.PostAsJsonAsync("/api/interventions", new InterventionCreateDto
        {
            PrestationId = _prestation.Id,
            AgentId = agentId,
            DatePrevue = jour.ToDateTime(TimeOnly.MinValue),
            HeurePrevue = heure,
            Statut = statut
        });

    private async Task<InterventionDto> CreerVacation(Guid agentId, DateOnly jour, TimeSpan heure)
    {
        var reponse = await PosterVacation(agentId, jour, heure);
        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);

        return (await reponse.Content.ReadFromJsonAsync<InterventionDto>())!;
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
        var reponse = await _admin.PostAsJsonAsync(route, corps);

        Assert.True(reponse.StatusCode == HttpStatusCode.Created, await reponse.Content.ReadAsStringAsync());
        return (await reponse.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<T> LireAsync<T>(Func<KleanDbContext, Task<T>> lecture)
    {
        T resultat = default!;
        await AvecDbAsync(async db => resultat = await lecture(db));
        return resultat;
    }

    /// <summary>
    /// Sous l'API, sans tenant : réservé à ce qu'aucune route n'expose — l'état d'une colonne,
    /// une ligne qu'aucun geste ne peut écrire.
    /// </summary>
    private Task AvecDbAsync(Func<KleanDbContext, Task> action) =>
        _fixture.AvecServicesAsync(services => action(services.GetRequiredService<KleanDbContext>()));
}
