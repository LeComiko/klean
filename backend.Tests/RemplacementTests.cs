using System.Net;
using System.Net.Http.Json;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Klean.Api.Tests.Infrastructure;

namespace Klean.Api.Tests;

/// <summary>
/// Cycle de remplacement (CLAUDE.md §9). Le scénario complet, et surtout les
/// transitions <b>invalides</b> : une machine à états qui ne refuse rien n'est
/// pas une machine à états. Chaque refus doit donner 400 avec un message, et
/// une ressource inexistante 404 — la distinction est vérifiée séparément.
///
/// Décor : l'agent Sow est affecté au mercredi 9 septembre 2026 de 9 h à 11 h,
/// l'agent Keita est disponible pour le remplacer.
/// </summary>
[Collection(CollectionApi.Nom)]
public class RemplacementTests : IAsyncLifetime
{
    private static readonly DateTime Mercredi = new(2026, 9, 9);
    private static readonly TimeSpan NeufHeures = new(9, 0, 0);

    private const double SiteLatitude = 48.8530;
    private const double SiteLongitude = 2.3499;
    private const double AgentLatitude = 48.8584;
    private const double AgentLongitude = 2.2945;

    private readonly FixtureApi _fixture;
    private HttpClient _client = null!;
    private PrestationDto _prestation = null!;
    private AgentDto _titulaire = null!;
    private AgentDto _remplacant = null!;
    private InterventionDto _intervention = null!;

    public RemplacementTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        var syndic = await Creer<ClientDto>("/api/clients", new ClientCreateDto
        {
            Nom = "Syndic Cité",
            Type = "Syndic",
            Ville = "Paris"
        });

        var site = await Creer<SiteDto>("/api/sites", new SiteCreateDto
        {
            ClientId = syndic.Id,
            Nom = "Résidence Cité",
            Adresse = "1 parvis Notre-Dame",
            Latitude = SiteLatitude,
            Longitude = SiteLongitude
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

        _titulaire = await CreerAgent("Sow");
        _remplacant = await CreerAgent("Keita");

        _intervention = await CreerIntervention(_titulaire.Id, Mercredi, NeufHeures, "Planifiee");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------- détection

    [Fact]
    public async Task La_creation_d_une_absence_declenche_la_detection()
    {
        await CreerAbsence(_titulaire.Id, Mercredi, Mercredi);

        var remplacement = Assert.Single(await Remplacements());

        Assert.Equal("Recherche", remplacement.Statut);
        Assert.Equal(_intervention.Id, remplacement.InterventionId);
        Assert.NotNull(remplacement.AbsenceId);
        Assert.Null(remplacement.AgentRemplacantId);
        Assert.Null(remplacement.Score);
    }

    [Fact]
    public async Task La_detection_est_idempotente()
    {
        var absence = await CreerAbsence(_titulaire.Id, Mercredi, Mercredi);

        // La création a déjà détecté. Le rattrapage ne doit rien recréer, sinon
        // le panneau de remplacement afficherait deux fois la même vacation.
        var rattrapage = await _client.PostAsync($"/api/absences/{absence.Id}/detecter-impacts", null);
        Assert.Equal(HttpStatusCode.OK, rattrapage.StatusCode);

        var detection = await rattrapage.Content.ReadFromJsonAsync<DetectionDto>();
        Assert.NotNull(detection);
        Assert.Equal(0, detection.RemplacementsCrees);
        Assert.Single(await Remplacements());
    }

    [Fact]
    public async Task La_detection_ignore_les_interventions_hors_de_l_absence()
    {
        // Absence le jeudi, intervention le mercredi.
        await CreerAbsence(_titulaire.Id, new DateTime(2026, 9, 10), new DateTime(2026, 9, 10));

        Assert.Empty(await Remplacements());
    }

    [Fact]
    public async Task La_detection_ne_couvre_que_les_interventions_planifiees()
    {
        var terminee = await CreerIntervention(
            _titulaire.Id, Mercredi, new TimeSpan(14, 0, 0), "Terminee");

        await CreerAbsence(_titulaire.Id, Mercredi, Mercredi);

        var remplacement = Assert.Single(await Remplacements());
        Assert.Equal(_intervention.Id, remplacement.InterventionId);
        Assert.NotEqual(terminee.Id, remplacement.InterventionId);
    }

    [Fact]
    public async Task La_detection_couvre_chaque_jour_de_l_absence()
    {
        var jeudi = await CreerIntervention(
            _titulaire.Id, new DateTime(2026, 9, 10), NeufHeures, "Planifiee");

        // Absence du mercredi au jeudi : les deux vacations sont découvertes.
        await CreerAbsence(_titulaire.Id, Mercredi, new DateTime(2026, 9, 10));

        var remplacements = await Remplacements();

        Assert.Equal(2, remplacements.Count);
        Assert.Contains(remplacements, r => r.InterventionId == _intervention.Id);
        Assert.Contains(remplacements, r => r.InterventionId == jeudi.Id);
    }

    [Fact]
    public async Task Une_absence_inconnue_renvoie_404_a_la_detection()
    {
        var reponse = await _client.PostAsync(
            $"/api/absences/{Guid.NewGuid()}/detecter-impacts", null);

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }

    // ------------------------------------------------------------- candidats

    [Fact]
    public async Task L_agent_absent_n_est_pas_candidat_a_son_propre_remplacement()
    {
        // Le point élégant du montage : aucune ligne de code ne le prévoit.
        // Le filtre d'absence du moteur d'affectation suffit.
        var remplacement = await RemplacementEnRecherche();

        var candidats = await Candidats(remplacement.Id);

        Assert.DoesNotContain(candidats, c => c.AgentId == _titulaire.Id);
        Assert.Contains(candidats, c => c.AgentId == _remplacant.Id);
    }

    [Fact]
    public async Task Un_remplacement_inconnu_renvoie_404_sur_les_candidats()
    {
        var reponse = await _client.GetAsync($"/api/remplacements/{Guid.NewGuid()}/candidats");

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }

    // ------------------------------------------------------------ transitions

    [Fact]
    public async Task Proposer_passe_en_Propose_avec_un_score()
    {
        var remplacement = await RemplacementEnRecherche();

        var propose = await Agir(remplacement.Id, "proposer", new ProposerDto { AgentId = _remplacant.Id });

        Assert.Equal("Propose", propose.Statut);
        Assert.Equal(_remplacant.Id, propose.AgentRemplacantId);
        Assert.NotNull(propose.Score);
        Assert.True(propose.Score > 0);
    }

    [Fact]
    public async Task Accepter_sur_statut_Recherche_renvoie_400()
    {
        var remplacement = await RemplacementEnRecherche();

        var reponse = await _client.PostAsync($"/api/remplacements/{remplacement.Id}/accepter", null);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("Recherche", await reponse.Content.ReadAsStringAsync());

        // Et l'intervention n'a pas bougé.
        Assert.Equal(_titulaire.Id, (await Intervention()).AgentId);
    }

    [Fact]
    public async Task Refuser_sur_statut_Recherche_renvoie_400()
    {
        var remplacement = await RemplacementEnRecherche();

        var reponse = await _client.PostAsync($"/api/remplacements/{remplacement.Id}/refuser", null);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Refuser_revient_en_Recherche_sans_reaffecter_l_intervention()
    {
        var remplacement = await RemplacementEnRecherche();
        await Agir(remplacement.Id, "proposer", new ProposerDto { AgentId = _remplacant.Id });

        var refuse = await Agir(remplacement.Id, "refuser");

        Assert.Equal("Recherche", refuse.Statut);
        Assert.Null(refuse.AgentRemplacantId);
        Assert.Null(refuse.Score);

        // Le point essentiel : un refus ne touche pas au planning.
        Assert.Equal(_titulaire.Id, (await Intervention()).AgentId);
    }

    [Fact]
    public async Task Proposer_puis_accepter_change_reellement_l_agent_de_l_intervention()
    {
        var remplacement = await RemplacementEnRecherche();
        await Agir(remplacement.Id, "proposer", new ProposerDto { AgentId = _remplacant.Id });

        var accepte = await Agir(remplacement.Id, "accepter");

        Assert.Equal("Accepte", accepte.Statut);
        Assert.Equal(_remplacant.Id, accepte.AgentRemplacantId);
        Assert.NotNull(accepte.Score);

        // La seule vérification qui compte vraiment.
        Assert.Equal(_remplacant.Id, (await Intervention()).AgentId);
    }

    [Fact]
    public async Task Proposer_apres_acceptation_renvoie_400()
    {
        var remplacement = await RemplacementEnRecherche();
        await Agir(remplacement.Id, "proposer", new ProposerDto { AgentId = _remplacant.Id });
        await Agir(remplacement.Id, "accepter");

        var troisieme = await CreerAgent("Traore");
        var reponse = await _client.PostAsJsonAsync(
            $"/api/remplacements/{remplacement.Id}/proposer",
            new ProposerDto { AgentId = troisieme.Id });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("Accepte", await reponse.Content.ReadAsStringAsync());

        // L'agent accepté reste en place : la transition refusée n'a rien écrit.
        Assert.Equal(_remplacant.Id, (await Intervention()).AgentId);
    }

    [Fact]
    public async Task Accepter_deux_fois_renvoie_400()
    {
        var remplacement = await RemplacementEnRecherche();
        await Agir(remplacement.Id, "proposer", new ProposerDto { AgentId = _remplacant.Id });
        await Agir(remplacement.Id, "accepter");

        var reponse = await _client.PostAsync($"/api/remplacements/{remplacement.Id}/accepter", null);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Proposer_apres_un_refus_est_possible()
    {
        var remplacement = await RemplacementEnRecherche();
        await Agir(remplacement.Id, "proposer", new ProposerDto { AgentId = _remplacant.Id });
        await Agir(remplacement.Id, "refuser");

        var second = await CreerAgent("Bamba");
        var propose = await Agir(remplacement.Id, "proposer", new ProposerDto { AgentId = second.Id });

        Assert.Equal("Propose", propose.Statut);
        Assert.Equal(second.Id, propose.AgentRemplacantId);
    }

    [Fact]
    public async Task Les_trois_actions_renvoient_404_sur_un_id_inexistant()
    {
        var inexistant = Guid.NewGuid();

        var proposer = await _client.PostAsJsonAsync(
            $"/api/remplacements/{inexistant}/proposer",
            new ProposerDto { AgentId = _remplacant.Id });
        var accepter = await _client.PostAsync($"/api/remplacements/{inexistant}/accepter", null);
        var refuser = await _client.PostAsync($"/api/remplacements/{inexistant}/refuser", null);

        Assert.Equal(HttpStatusCode.NotFound, proposer.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, accepter.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, refuser.StatusCode);
    }

    // ---------------------------------------------------- garde d'éligibilité

    [Fact]
    public async Task Proposer_l_agent_absent_lui_meme_est_refuse()
    {
        // Sans cette garde, les filtres durs du moteur seraient décoratifs :
        // il suffirait de proposer à la main l'agent en arrêt maladie.
        var remplacement = await RemplacementEnRecherche();

        var reponse = await _client.PostAsJsonAsync(
            $"/api/remplacements/{remplacement.Id}/proposer",
            new ProposerDto { AgentId = _titulaire.Id });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("n'est pas candidat", await reponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Proposer_un_agent_deja_affecte_sur_le_creneau_est_refuse()
    {
        var remplacement = await RemplacementEnRecherche();

        // Le remplaçant prend une autre vacation qui chevauche : 10 h – 12 h.
        await CreerIntervention(_remplacant.Id, Mercredi, new TimeSpan(10, 0, 0), "Planifiee");

        var reponse = await _client.PostAsJsonAsync(
            $"/api/remplacements/{remplacement.Id}/proposer",
            new ProposerDto { AgentId = _remplacant.Id });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("n'est pas candidat", await reponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task L_agent_devenu_indisponible_entre_la_proposition_et_l_acceptation_est_refuse()
    {
        var remplacement = await RemplacementEnRecherche();
        await Agir(remplacement.Id, "proposer", new ProposerDto { AgentId = _remplacant.Id });

        // Le remplaçant se déclare absent après la proposition. Accepter sans
        // revérifier poserait un agent absent sur la vacation.
        await CreerAbsence(_remplacant.Id, Mercredi, Mercredi);

        var reponse = await _client.PostAsync($"/api/remplacements/{remplacement.Id}/accepter", null);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("n'est plus candidat", await reponse.Content.ReadAsStringAsync());
        Assert.Equal(_titulaire.Id, (await Intervention()).AgentId);
    }

    [Fact]
    public async Task Proposer_un_agent_inconnu_renvoie_400()
    {
        var remplacement = await RemplacementEnRecherche();

        var reponse = await _client.PostAsJsonAsync(
            $"/api/remplacements/{remplacement.Id}/proposer",
            new ProposerDto { AgentId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("inconnu", await reponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Un_agent_de_l_autre_entreprise_n_est_pas_proposable()
    {
        var remplacement = await RemplacementEnRecherche();

        var beta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);
        var creation = await beta.PostAsJsonAsync("/api/agents", new AgentCreateDto
        {
            Nom = "Intrus",
            Prenom = "Beta",
            DateEmbauche = new DateTime(2024, 1, 1),
            Latitude = AgentLatitude,
            Longitude = AgentLongitude
        });
        var intrus = await creation.Content.ReadFromJsonAsync<AgentDto>();
        Assert.NotNull(intrus);

        var reponse = await _client.PostAsJsonAsync(
            $"/api/remplacements/{remplacement.Id}/proposer",
            new ProposerDto { AgentId = intrus.Id });

        // Le filtre multi-tenant le rend littéralement inconnu.
        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("inconnu", await reponse.Content.ReadAsStringAsync());
    }

    // ------------------------------------------------------------ autorisations

    [Fact]
    public async Task Le_chef_d_equipe_n_agit_pas_hors_de_son_equipe()
    {
        // Le cycle est ouvert au chef depuis l'étape 19, sur son équipe seulement. Ce
        // chef n'en a pas : le remplacement lui est inconnu, en lecture comme en
        // écriture. Le cas positif est dans FrontiereChefEquipeTests.
        var remplacement = await RemplacementEnRecherche();

        var chef = await _fixture.ClientDeRoleAsync(
            _client, "ChefEquipe", "chef.remplacement@test.local");

        var lecture = await chef.GetAsync($"/api/remplacements/{remplacement.Id}/candidats");
        Assert.Equal(HttpStatusCode.NotFound, lecture.StatusCode);

        var action = await chef.PostAsJsonAsync(
            $"/api/remplacements/{remplacement.Id}/proposer",
            new ProposerDto { AgentId = _remplacant.Id });
        Assert.Equal(HttpStatusCode.NotFound, action.StatusCode);
    }

    [Fact]
    public async Task Sans_jeton_le_cycle_est_inaccessible()
    {
        var remplacement = await RemplacementEnRecherche();
        var anonyme = _fixture.ClientAnonymeSansRedirection();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonyme.PostAsync($"/api/remplacements/{remplacement.Id}/accepter", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonyme.GetAsync($"/api/remplacements/{remplacement.Id}/candidats")).StatusCode);
    }

    // ---------- Ouverture idempotente : POST /api/interventions/{id}/remplacement

    [Fact]
    public async Task Ouvrir_cree_un_remplacement_en_recherche()
    {
        // Une vacation jamais affectée n'a aucun remplacement : aucune absence ne
        // l'a déclenchée. C'est le cas que l'écran de planning doit couvrir.
        var libre = await CreerIntervention(null, Mercredi, new TimeSpan(14, 0, 0), "Planifiee");

        var reponse = await _client.PostAsync($"/api/interventions/{libre.Id}/remplacement", null);

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);

        var remplacement = await reponse.Content.ReadFromJsonAsync<RemplacementDto>();
        Assert.NotNull(remplacement);
        Assert.Equal(libre.Id, remplacement.InterventionId);
        Assert.Equal("Recherche", remplacement.Statut);
        Assert.Null(remplacement.AgentRemplacantId);
        Assert.Null(remplacement.Score);

        // Null volontairement : une ouverture manuelle ne sait pas de quelle
        // absence elle découle, et la deviner serait une inférence.
        Assert.Null(remplacement.AbsenceId);
    }

    [Fact]
    public async Task Ouvrir_deux_fois_rend_le_meme_remplacement_sans_doublon()
    {
        // LE test de l'endpoint. Sans idempotence, deux responsables qui cliquent
        // la même vacation créent deux lignes, dont une reste en Recherche et
        // devient invisible — les vacations non pourvues viennent du planning, pas
        // de cette table.
        var libre = await CreerIntervention(null, Mercredi, new TimeSpan(14, 0, 0), "Planifiee");

        var premier = await Ouvrir(libre.Id, HttpStatusCode.Created);
        var second = await Ouvrir(libre.Id, HttpStatusCode.OK);

        Assert.Equal(premier.Id, second.Id);
        Assert.Single(await Remplacements(), r => r.InterventionId == libre.Id);
    }

    [Fact]
    public async Task Ouvrir_retrouve_celui_pose_par_la_detection_d_impacts()
    {
        // L'autre moitié du cas d'écran : le mercredi d'un agent absent porte déjà
        // un remplacement, créé automatiquement à la saisie de l'absence. L'écran
        // ne sait pas lequel des deux cas il a sous la souris, et n'a pas à le savoir.
        var attendu = await RemplacementEnRecherche();

        var obtenu = await Ouvrir(_intervention.Id, HttpStatusCode.OK);

        Assert.Equal(attendu.Id, obtenu.Id);
        Assert.NotNull(obtenu.AbsenceId);
        Assert.Single(await Remplacements());
    }

    [Fact]
    public async Task Ouvrir_rend_aussi_un_remplacement_deja_accepte()
    {
        // Accepte fait partie des statuts actifs : rouvrir ne doit pas créer un
        // second remplacement sur une vacation déjà pourvue.
        var remplacement = await RemplacementEnRecherche();
        await Agir(remplacement.Id, "proposer", new ProposerDto { AgentId = _remplacant.Id });
        await Agir(remplacement.Id, "accepter");

        var obtenu = await Ouvrir(_intervention.Id, HttpStatusCode.OK);

        Assert.Equal(remplacement.Id, obtenu.Id);
        Assert.Equal("Accepte", obtenu.Statut);
        Assert.Single(await Remplacements());
    }

    [Fact]
    public async Task Ouvrir_apres_un_remplacement_refuse_en_cree_un_nouveau()
    {
        // Refuse et Annule sont de l'historique, pas des statuts actifs : ils
        // n'empêchent pas d'ouvrir une nouvelle recherche. Même règle que la
        // détection d'impacts, et c'est volontaire — deux règles d'unicité
        // divergeraient.
        var libre = await CreerIntervention(null, Mercredi, new TimeSpan(14, 0, 0), "Planifiee");

        var historique = await Creer<RemplacementDto>("/api/remplacements", new RemplacementCreateDto
        {
            InterventionId = libre.Id,
            Statut = "Refuse"
        });

        var ouvert = await Ouvrir(libre.Id, HttpStatusCode.Created);

        Assert.NotEqual(historique.Id, ouvert.Id);
        Assert.Equal("Recherche", ouvert.Statut);
        Assert.Equal(2, (await Remplacements()).Count(r => r.InterventionId == libre.Id));
    }

    [Fact]
    public async Task Ouvrir_sur_une_vacation_annulee_est_refuse()
    {
        var annulee = await CreerIntervention(null, Mercredi, new TimeSpan(14, 0, 0), "Annulee");

        var reponse = await _client.PostAsync($"/api/interventions/{annulee.Id}/remplacement", null);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        var message = await reponse.Content.ReadAsStringAsync();
        Assert.Contains("Annulee", message);
        Assert.Contains("Planifiee", message);
        Assert.Empty(await Remplacements());
    }

    [Fact]
    public async Task Ouvrir_expose_un_remplacement_existant_meme_sur_une_vacation_annulee()
    {
        // L'existant est cherché avant le contrôle de statut : annuler la vacation
        // ne doit pas faire disparaître le remplacement déjà ouvert de l'écran.
        var remplacement = await RemplacementEnRecherche();

        var annulation = await _client.PutAsJsonAsync(
            $"/api/interventions/{_intervention.Id}",
            new InterventionCreateDto
            {
                PrestationId = _prestation.Id,
                AgentId = null,
                DatePrevue = Mercredi,
                HeurePrevue = NeufHeures,
                Statut = "Annulee"
            });
        Assert.Equal(HttpStatusCode.NoContent, annulation.StatusCode);

        var obtenu = await Ouvrir(_intervention.Id, HttpStatusCode.OK);

        Assert.Equal(remplacement.Id, obtenu.Id);
    }

    [Fact]
    public async Task Ouvrir_sur_une_intervention_inconnue_renvoie_404()
    {
        // La distinction 404 / 400 doit rester nette : ressource absente contre
        // transition refusée.
        var reponse = await _client.PostAsync(
            $"/api/interventions/{Guid.NewGuid()}/remplacement", null);

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }

    [Fact]
    public async Task Ouvrir_hors_de_l_equipe_du_chef_renvoie_404()
    {
        // L'opération écrit, et elle est ouverte au chef depuis l'étape 19 : c'est la
        // frontière de son équipe qui la cadre. Ce chef n'en a pas — rien n'est ouvert.
        var chef = await _fixture.ClientDeRoleAsync(
            _client, RolesKlean.ChefEquipe, "chef.ouvrir@test.local");

        var reponse = await chef.PostAsync(
            $"/api/interventions/{_intervention.Id}/remplacement", null);

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
        Assert.Empty(await Remplacements());
    }

    [Fact]
    public async Task Ouvrir_sur_l_intervention_d_une_autre_entreprise_renvoie_404()
    {
        // Le filtre multi-tenant rend l'intervention invisible, donc inconnue.
        // L'endpoint écrit : sans cette garde, Beta ouvrirait un remplacement sur
        // une vacation d'Alpha.
        var beta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);

        var reponse = await beta.PostAsync(
            $"/api/interventions/{_intervention.Id}/remplacement", null);

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
        Assert.Empty(await Remplacements());
    }

    [Fact]
    public async Task Ouvrir_sans_jeton_renvoie_401()
    {
        var anonyme = _fixture.ClientAnonymeSansRedirection();

        var reponse = await anonyme.PostAsync(
            $"/api/interventions/{_intervention.Id}/remplacement", null);

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    // ------------------------------------------------------------- outillage

    /// <summary>Absence du titulaire sur la vacation, donc un remplacement à pourvoir.</summary>
    private async Task<RemplacementDto> RemplacementEnRecherche()
    {
        await CreerAbsence(_titulaire.Id, Mercredi, Mercredi);

        return Assert.Single(await Remplacements());
    }

    private async Task<RemplacementDto> Ouvrir(Guid interventionId, HttpStatusCode attendu)
    {
        var reponse = await _client.PostAsync(
            $"/api/interventions/{interventionId}/remplacement", null);

        Assert.Equal(attendu, reponse.StatusCode);

        return await reponse.Content.ReadFromJsonAsync<RemplacementDto>()
            ?? throw new InvalidOperationException("Réponse d'ouverture illisible.");
    }

    private async Task<RemplacementDto> Agir(Guid id, string action, object? corps = null)
    {
        var reponse = corps is null
            ? await _client.PostAsync($"/api/remplacements/{id}/{action}", null)
            : await _client.PostAsJsonAsync($"/api/remplacements/{id}/{action}", corps);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);

        return await reponse.Content.ReadFromJsonAsync<RemplacementDto>()
            ?? throw new InvalidOperationException($"Réponse illisible sur {action}.");
    }

    private Task<List<RemplacementDto>> Remplacements() =>
        _client.ListerAsync<RemplacementDto>("/api/remplacements");

    private async Task<List<CandidatDto>> Candidats(Guid remplacementId)
    {
        var reponse = await _client.GetAsync($"/api/remplacements/{remplacementId}/candidats");
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);

        return await reponse.Content.ReadFromJsonAsync<List<CandidatDto>>()
            ?? throw new InvalidOperationException("Liste de candidats illisible.");
    }

    private async Task<InterventionDto> Intervention()
    {
        var relue = await _client.GetFromJsonAsync<InterventionDto>(
            $"/api/interventions/{_intervention.Id}");

        Assert.NotNull(relue);
        return relue;
    }

    private Task<AgentDto> CreerAgent(string nom) =>
        Creer<AgentDto>("/api/agents", new AgentCreateDto
        {
            Nom = nom,
            Prenom = "Test",
            DateEmbauche = new DateTime(2024, 1, 1),
            Latitude = AgentLatitude,
            Longitude = AgentLongitude
        });

    private Task<AbsenceDto> CreerAbsence(Guid agentId, DateTime debut, DateTime fin) =>
        Creer<AbsenceDto>("/api/absences", new AbsenceCreateDto
        {
            AgentId = agentId,
            Type = "Maladie",
            DateDebut = debut,
            DateFin = fin
        });

    private Task<InterventionDto> CreerIntervention(
        Guid? agentId, DateTime date, TimeSpan heure, string statut) =>
        Creer<InterventionDto>("/api/interventions", new InterventionCreateDto
        {
            PrestationId = _prestation.Id,
            AgentId = agentId,
            DatePrevue = date,
            HeurePrevue = heure,
            Statut = statut
        });

    private async Task<T> Creer<T>(string route, object corps)
    {
        var reponse = await _client.PostAsJsonAsync(route, corps);

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);

        return await reponse.Content.ReadFromJsonAsync<T>()
            ?? throw new InvalidOperationException($"Réponse illisible sur {route}.");
    }
}
