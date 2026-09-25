using System.Net;
using System.Net.Http.Json;
using Klean.Api.DTOs;
using Klean.Api.Tests.Infrastructure;

namespace Klean.Api.Tests;

/// <summary>
/// Moteur d'affectation (CLAUDE.md §8). Deux familles de tests nettement
/// séparées, parce que le moteur fait deux choses différentes :
///
/// — les <b>filtres durs</b> : l'agent est absent de la liste, pas mal classé ;
/// — les <b>notes</b> : chacune vérifiée sur un chiffre calculé à la main, pas
///   sur un simple « supérieur à zéro ».
///
/// La semaine de référence est celle du lundi 7 septembre 2026, et
/// l'intervention à pourvoir tombe le mercredi 9 à 8 h 30 pour deux heures.
/// Des dates fixes : un jeu de données calé sur <c>DateTime.UtcNow</c> change de
/// signification chaque semaine et finit par échouer un lundi matin.
/// </summary>
[Collection(CollectionApi.Nom)]
public class AffectationTests : IAsyncLifetime
{
    private static readonly DateTime Lundi = new(2026, 9, 7);
    private static readonly DateTime Mercredi = new(2026, 9, 9);
    private static readonly TimeSpan HuitHeureTrente = new(8, 30, 0);
    private const int DureeMinutes = 120;

    // Notre-Dame de Paris : le site à pourvoir.
    private const double SiteLatitude = 48.8530;
    private const double SiteLongitude = 2.3499;

    // Tour Eiffel — 4,1 km du site.
    private const double ProcheLatitude = 48.8584;
    private const double ProcheLongitude = 2.2945;

    // Versailles — 17,1 km du site.
    private const double MoyenneLatitude = 48.8014;
    private const double MoyenneLongitude = 2.1301;

    // Lyon — bien au-delà des 30 km qui annulent la note.
    private const double LoinLatitude = 45.7640;
    private const double LoinLongitude = 4.8357;

    private readonly FixtureApi _fixture;
    private HttpClient _client = null!;
    private SiteDto _site = null!;
    private PrestationDto _prestation = null!;
    private InterventionDto _cible = null!;

    public AffectationTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        var clientSyndic = await Creer<ClientDto>("/api/clients", new ClientCreateDto
        {
            Nom = "Syndic Cité",
            Type = "Syndic",
            Ville = "Paris"
        });

        _site = await Creer<SiteDto>("/api/sites", new SiteCreateDto
        {
            ClientId = clientSyndic.Id,
            Nom = "Résidence Cité",
            Adresse = "1 parvis Notre-Dame",
            CodePostal = "75004",
            Ville = "Paris",
            Latitude = SiteLatitude,
            Longitude = SiteLongitude
        });

        var contrat = await Creer<ContratDto>("/api/contrats", new ContratCreateDto
        {
            SiteId = _site.Id,
            DateDebut = new DateTime(2026, 1, 1),
            Frequence = "Lundi, mercredi, vendredi"
        });

        _prestation = await Creer<PrestationDto>("/api/prestations", new PrestationCreateDto
        {
            ContratId = contrat.Id,
            Libelle = "Nettoyage parties communes",
            DureeEstimeeMinutes = DureeMinutes
        });

        _cible = await CreerIntervention(null, Mercredi, HuitHeureTrente);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ----------------------------------------------------------------- filtres

    [Fact]
    public async Task Agent_proche_et_experimente_sort_en_tete()
    {
        var proche = await CreerAgent("Diallo", ProcheLatitude, ProcheLongitude);
        var loin = await CreerAgent("Moreau", LoinLatitude, LoinLongitude);

        // Cinq passages antérieurs sur le site, la semaine précédente : hors de
        // la fenêtre de charge et de repos, donc seule l'expérience bouge.
        for (var jour = 1; jour <= 5; jour++)
        {
            await CreerIntervention(proche.Id, new DateTime(2026, 9, jour), HuitHeureTrente);
        }

        var candidats = await Candidats(_cible.Id);

        Assert.Equal(2, candidats.Count);
        Assert.Equal(proche.Id, candidats[0].AgentId);
        Assert.Equal(loin.Id, candidats[1].AgentId);
        Assert.True(candidats[0].Score > candidats[1].Score);
    }

    [Fact]
    public async Task Agent_en_absence_n_apparait_pas()
    {
        var absent = await CreerAgent("Bernard", ProcheLatitude, ProcheLongitude);
        var disponible = await CreerAgent("Petit", ProcheLatitude, ProcheLongitude);

        await Creer<AbsenceDto>("/api/absences", new AbsenceCreateDto
        {
            AgentId = absent.Id,
            Type = "Conge",
            DateDebut = new DateTime(2026, 9, 8),
            DateFin = new DateTime(2026, 9, 11)
        });

        var candidats = await Candidats(_cible.Id);

        Assert.DoesNotContain(candidats, c => c.AgentId == absent.Id);
        Assert.Contains(candidats, c => c.AgentId == disponible.Id);
    }

    [Fact]
    public async Task Absence_saisie_en_jours_pleins_exclut_bien_l_agent()
    {
        // Le piège : une absence d'un seul jour arrive avec les heures à zéro.
        // Un chevauchement testé à la minute — 9 septembre 0 h 00 contre une
        // vacation à 8 h 30 — laisserait l'agent candidat un jour où il est
        // en arrêt. La comparaison porte donc sur la journée.
        var absent = await CreerAgent("Rousseau", ProcheLatitude, ProcheLongitude);

        await Creer<AbsenceDto>("/api/absences", new AbsenceCreateDto
        {
            AgentId = absent.Id,
            Type = "Maladie",
            DateDebut = Mercredi,
            DateFin = Mercredi
        });

        var candidats = await Candidats(_cible.Id);

        Assert.Empty(candidats);
    }

    [Fact]
    public async Task Agent_inactif_n_apparait_pas()
    {
        var inactif = await CreerAgent("Garnier", ProcheLatitude, ProcheLongitude, actif: false);
        var actif = await CreerAgent("Leroy", ProcheLatitude, ProcheLongitude);

        var candidats = await Candidats(_cible.Id);

        Assert.DoesNotContain(candidats, c => c.AgentId == inactif.Id);
        Assert.Contains(candidats, c => c.AgentId == actif.Id);
    }

    [Fact]
    public async Task Agent_deja_affecte_sur_un_creneau_chevauchant_n_apparait_pas()
    {
        var occupe = await CreerAgent("Fournier", ProcheLatitude, ProcheLongitude);

        // 9 h 00 – 11 h 00 contre 8 h 30 – 10 h 30.
        await CreerIntervention(occupe.Id, Mercredi, new TimeSpan(9, 0, 0));

        var candidats = await Candidats(_cible.Id);

        Assert.Empty(candidats);
    }

    [Fact]
    public async Task Agent_affecte_le_meme_jour_hors_chevauchement_reste_candidat()
    {
        var occupe = await CreerAgent("Chevalier", ProcheLatitude, ProcheLongitude);

        // 11 h 00 – 13 h 00, la vacation cible finit à 10 h 30 : trente minutes
        // d'écart, donc moins de onze heures. La règle de repos prise à la
        // lettre écarterait cet agent et viderait la liste sur tout planning
        // réel. Elle ne s'applique qu'entre deux journées de travail
        // distinctes — voir AffectationService.ReposInsuffisant.
        await CreerIntervention(occupe.Id, Mercredi, new TimeSpan(11, 0, 0));

        var candidats = await Candidats(_cible.Id);

        Assert.Single(candidats);
        Assert.Equal(occupe.Id, candidats[0].AgentId);
    }

    [Fact]
    public async Task Moins_de_onze_heures_de_repos_ecarte_l_agent()
    {
        var fatigue = await CreerAgent("Marchand", ProcheLatitude, ProcheLongitude);

        // La veille de 20 h 00 à 22 h 00 : 10 h 30 de repos avant 8 h 30.
        await CreerIntervention(fatigue.Id, new DateTime(2026, 9, 8), new TimeSpan(20, 0, 0));

        var candidats = await Candidats(_cible.Id);

        Assert.Empty(candidats);
    }

    [Fact]
    public async Task Onze_heures_de_repos_exactement_laisse_l_agent_candidat()
    {
        var repose = await CreerAgent("Girard", ProcheLatitude, ProcheLongitude);

        // La veille de 19 h 30 à 21 h 30 : exactement 11 h avant 8 h 30. La
        // borne est incluse — la loi impose onze heures, pas plus de onze.
        await CreerIntervention(repose.Id, new DateTime(2026, 9, 8), new TimeSpan(19, 30, 0));

        var candidats = await Candidats(_cible.Id);

        Assert.Single(candidats);
        Assert.Equal(repose.Id, candidats[0].AgentId);
    }

    [Fact]
    public async Task Le_repos_vaut_aussi_pour_la_vacation_du_lendemain()
    {
        var fatigue = await CreerAgent("Lambert", ProcheLatitude, ProcheLongitude);

        // Vacation à pourvoir le mercredi de 22 h 00 à minuit, et l'agent
        // reprend le jeudi à 6 h 00 : six heures de repos. La règle regarde des
        // deux côtés du créneau, pas seulement en amont.
        var tardive = await CreerIntervention(null, Mercredi, new TimeSpan(22, 0, 0));
        await CreerIntervention(fatigue.Id, new DateTime(2026, 9, 10), new TimeSpan(6, 0, 0));

        Assert.Empty(await Candidats(tardive.Id));

        // Témoin : la même vacation du jeudi laisse l'agent candidat pour le
        // créneau du mercredi matin, à 22 heures d'écart.
        Assert.Single(await Candidats(_cible.Id));
    }

    [Fact]
    public async Task Un_agent_de_l_autre_entreprise_n_est_jamais_candidat()
    {
        var alpha = await CreerAgent("Dupont", ProcheLatitude, ProcheLongitude);

        var beta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);
        var reponse = await beta.PostAsJsonAsync("/api/agents", new AgentCreateDto
        {
            Nom = "Intrus",
            Prenom = "Beta",
            DateEmbauche = new DateTime(2024, 1, 1),
            Latitude = SiteLatitude,
            Longitude = SiteLongitude
        });
        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);

        var candidats = await Candidats(_cible.Id);

        Assert.Single(candidats);
        Assert.Equal(alpha.Id, candidats[0].AgentId);
    }

    // -------------------------------------------------------------------- notes

    [Fact]
    public async Task Distance_haversine_correspond_a_la_realite()
    {
        await CreerAgent("Tour", ProcheLatitude, ProcheLongitude);

        var candidats = await Candidats(_cible.Id);

        // Notre-Dame – Tour Eiffel : 4,1 km à vol d'oiseau.
        Assert.NotNull(candidats[0].DistanceKm);
        Assert.Equal(4.1, candidats[0].DistanceKm!.Value, 1);
    }

    [Fact]
    public async Task Note_de_distance_decroit_avec_les_kilometres()
    {
        var proche = await CreerAgent("Proche", ProcheLatitude, ProcheLongitude);
        var moyenne = await CreerAgent("Moyenne", MoyenneLatitude, MoyenneLongitude);
        var loin = await CreerAgent("Loin", LoinLatitude, LoinLongitude);

        var candidats = (await Candidats(_cible.Id)).ToDictionary(c => c.AgentId);

        // (30 − km) / 30 × 100, plancher à zéro.
        Assert.Equal(86.3, candidats[proche.Id].NoteDistance, 0);
        Assert.Equal(43.0, candidats[moyenne.Id].NoteDistance, 0);
        Assert.Equal(0, candidats[loin.Id].NoteDistance);
    }

    [Fact]
    public async Task Note_de_distance_vaut_cinquante_sans_coordonnees()
    {
        var sansCoordonnees = await CreerAgent("Inconnu", null, null);

        var candidats = await Candidats(_cible.Id);

        Assert.Null(candidats[0].DistanceKm);
        Assert.Equal(50, candidats[0].NoteDistance);
        Assert.Equal(sansCoordonnees.Id, candidats[0].AgentId);
    }

    [Fact]
    public async Task Note_d_experience_suit_les_passages_anterieurs()
    {
        var agent = await CreerAgent("Sanchez", ProcheLatitude, ProcheLongitude);

        for (var jour = 1; jour <= 3; jour++)
        {
            await CreerIntervention(agent.Id, new DateTime(2026, 9, jour), HuitHeureTrente);
        }

        var candidats = await Candidats(_cible.Id);

        Assert.Equal(3, candidats[0].Passages);
        Assert.Equal(30, candidats[0].NoteExperience);
    }

    [Fact]
    public async Task Note_d_experience_plafonne_a_dix_passages()
    {
        var agent = await CreerAgent("Vidal", ProcheLatitude, ProcheLongitude);

        // Quatorze passages en août : au-delà du plafond de dix.
        for (var jour = 1; jour <= 14; jour++)
        {
            await CreerIntervention(agent.Id, new DateTime(2026, 8, jour), HuitHeureTrente);
        }

        var candidats = await Candidats(_cible.Id);

        Assert.Equal(14, candidats[0].Passages);
        Assert.Equal(100, candidats[0].NoteExperience);
    }

    [Fact]
    public async Task Les_passages_a_venir_ne_comptent_pas_comme_experience()
    {
        // « Expérience du site » veut dire connaître le site. Un agent affecté
        // trois fois la semaine prochaine ne le connaît pas encore : compter ces
        // passages reviendrait à récompenser le fait d'être déjà planifié.
        var agent = await CreerAgent("Perrin", ProcheLatitude, ProcheLongitude);

        foreach (var jour in new[] { 16, 17, 18 })
        {
            await CreerIntervention(agent.Id, new DateTime(2026, 9, jour), HuitHeureTrente);
        }

        var candidats = await Candidats(_cible.Id);

        Assert.Equal(0, candidats[0].Passages);
        Assert.Equal(0, candidats[0].NoteExperience);
    }

    [Fact]
    public async Task Note_de_charge_decroit_avec_les_heures_de_la_semaine()
    {
        var charge = await CreerAgent("Charge", ProcheLatitude, ProcheLongitude);
        var libre = await CreerAgent("Libre", ProcheLatitude, ProcheLongitude);

        // Quatre vacations de deux heures du jeudi au dimanche : huit heures
        // dans la semaine du lundi 7, après la vacation cible pour ne pas
        // contaminer l'expérience du site.
        foreach (var jour in new[] { 10, 11, 12, 13 })
        {
            await CreerIntervention(charge.Id, new DateTime(2026, 9, jour), HuitHeureTrente);
        }

        var candidats = (await Candidats(_cible.Id)).ToDictionary(c => c.AgentId);

        Assert.Equal(8, candidats[charge.Id].HeuresSemaine);
        Assert.Equal(77.14, candidats[charge.Id].NoteCharge, 2);

        Assert.Equal(0, candidats[libre.Id].HeuresSemaine);
        Assert.Equal(100, candidats[libre.Id].NoteCharge);
    }

    [Fact]
    public async Task La_charge_de_la_semaine_suivante_ne_compte_pas()
    {
        var agent = await CreerAgent("Fabre", ProcheLatitude, ProcheLongitude);

        // Lundi 14 septembre : semaine suivante, la note de charge l'ignore.
        await CreerIntervention(agent.Id, new DateTime(2026, 9, 14), HuitHeureTrente);

        var candidats = await Candidats(_cible.Id);

        Assert.Equal(0, candidats[0].HeuresSemaine);
        Assert.Equal(100, candidats[0].NoteCharge);
    }

    [Fact]
    public async Task Le_score_est_la_moyenne_ponderee_35_30_35()
    {
        var agent = await CreerAgent("Noel", MoyenneLatitude, MoyenneLongitude);

        for (var jour = 1; jour <= 4; jour++)
        {
            await CreerIntervention(agent.Id, new DateTime(2026, 9, jour), HuitHeureTrente);
        }

        var candidat = (await Candidats(_cible.Id)).Single();

        var attendu = Math.Round(
            (candidat.NoteExperience * 35
             + candidat.NoteDistance * 30
             + candidat.NoteCharge * 35) / 100,
            2);

        Assert.Equal(attendu, candidat.Score);

        // Et les données brutes sont bien là : le classement doit pouvoir être
        // contesté, pas seulement lu.
        Assert.Equal(4, candidat.Passages);
        Assert.NotNull(candidat.DistanceKm);
        Assert.Equal(0, candidat.HeuresSemaine);
    }

    // ------------------------------------------------------------------ surface

    [Fact]
    public async Task Intervention_inconnue_renvoie_404()
    {
        var reponse = await _client.GetAsync($"/api/interventions/{Guid.NewGuid()}/candidats");

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }

    [Fact]
    public async Task Une_intervention_sans_agent_disponible_renvoie_une_liste_vide()
    {
        // Pas de 404 : l'intervention existe, c'est la liste qui est vide. La
        // distinction compte à l'écran de remplacement — « introuvable » et
        // « personne ne peut y aller » ne se traitent pas pareil.
        var reponse = await _client.GetAsync($"/api/interventions/{_cible.Id}/candidats");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Empty(await reponse.Content.ReadFromJsonAsync<List<CandidatDto>>() ?? []);
    }

    [Fact]
    public async Task Le_chef_d_equipe_peut_consulter_les_candidats()
    {
        await CreerAgent("Aubert", ProcheLatitude, ProcheLongitude);

        var chef = await _fixture.ClientDeRoleAsync(
            _client, "ChefEquipe", "chef.affectation@test.local");

        var reponse = await chef.GetAsync($"/api/interventions/{_cible.Id}/candidats");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }

    [Fact]
    public async Task L_agent_ne_peut_pas_consulter_les_candidats()
    {
        // La liste nomme des agents, et la matrice des droits ne donne aucune
        // lecture sur les agents au rôle Agent.
        var agent = await CreerAgent("Simon", ProcheLatitude, ProcheLongitude);

        var clientAgent = await _fixture.ClientDeRoleAsync(
            _client, "Agent", "agent.affectation@test.local", agentId: agent.Id);

        var reponse = await clientAgent.GetAsync($"/api/interventions/{_cible.Id}/candidats");

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    [Fact]
    public async Task Le_client_syndic_ne_peut_pas_consulter_les_candidats()
    {
        var syndic = await _fixture.ClientDeRoleAsync(
            _client, "ClientSyndic", "syndic.affectation@test.local",
            clientId: (await _client.ListerAsync<ClientDto>("/api/clients")).Single().Id);

        var reponse = await syndic.GetAsync($"/api/interventions/{_cible.Id}/candidats");

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    [Fact]
    public async Task Sans_jeton_la_reponse_est_401()
    {
        var anonyme = _fixture.ClientAnonymeSansRedirection();

        var reponse = await anonyme.GetAsync($"/api/interventions/{_cible.Id}/candidats");

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    // ------------------------------------------------------------------ outillage

    private async Task<List<CandidatDto>> Candidats(Guid interventionId)
    {
        var reponse = await _client.GetAsync($"/api/interventions/{interventionId}/candidats");
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);

        return await reponse.Content.ReadFromJsonAsync<List<CandidatDto>>()
            ?? throw new InvalidOperationException("Liste de candidats illisible.");
    }

    private Task<AgentDto> CreerAgent(string nom, double? latitude, double? longitude, bool actif = true) =>
        Creer<AgentDto>("/api/agents", new AgentCreateDto
        {
            Nom = nom,
            Prenom = "Test",
            DateEmbauche = new DateTime(2024, 1, 1),
            Latitude = latitude,
            Longitude = longitude,
            Actif = actif
        });

    private Task<InterventionDto> CreerIntervention(Guid? agentId, DateTime date, TimeSpan heure) =>
        Creer<InterventionDto>("/api/interventions", new InterventionCreateDto
        {
            PrestationId = _prestation.Id,
            AgentId = agentId,
            DatePrevue = date,
            HeurePrevue = heure,
            Statut = "Planifiee"
        });

    private async Task<T> Creer<T>(string route, object corps)
    {
        var reponse = await _client.PostAsJsonAsync(route, corps);

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);

        return await reponse.Content.ReadFromJsonAsync<T>()
            ?? throw new InvalidOperationException($"Réponse illisible sur {route}.");
    }
}
