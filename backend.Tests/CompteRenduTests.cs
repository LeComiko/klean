using System.Net;
using System.Net.Http.Headers;
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
/// Le compte rendu d'un passage — étape 25, lot B : la checklist du type, et les anomalies
/// avec leurs photos.
///
/// Quatre choses à garder, et chacune a sa raison dans SPEC.md : une checklist incomplète ne
/// retient pas l'agent ; ce qui a été déclaré garde le libellé du moment ; une photo perd ses
/// métadonnées sur le serveur, quoi que l'écran ait fait ; et chacun ne voit que les
/// anomalies de son périmètre.
///
/// Décor : le mercredi 10 mars 2027. Un hall d'entrée, sa checklist de trois points dont un
/// désactivé ; Sow y a une vacation aujourd'hui, Keita une autre ailleurs dans la même
/// résidence.
/// </summary>
[Collection(CollectionApi.Nom)]
public class CompteRenduTests : IAsyncLifetime
{
    private static readonly DateOnly Aujourdhui = new(2027, 3, 10);
    private const string Points = "/api/points-controle";
    private const string Anomalies = "/api/anomalies";

    private static readonly PositionDto SurPlace = new() { Latitude = 48.8530, Longitude = 2.3499, PrecisionMetres = 10 };

    private readonly FixtureApi _fixture;
    private HttpClient _admin = null!;
    private HttpClient _sow = null!;
    private Guid _hall, _contrat;
    private AgentDto _agentSow = null!, _agentKeita = null!;
    private PointControleDto _corbeilles = null!, _sol = null!, _retire = null!;
    private InterventionDto _vacation = null!;

    public CompteRenduTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _fixture.Fabrique.Horloge.Fixer(Aujourdhui);
        _admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        _hall = (await Creer<TypePrestationDto>("/api/types-prestation",
            new TypePrestationCreateDto { Libelle = "Hall d'entrée", DureeParDefautMinutes = 60 })).Id;

        _sol = await Point("Laver le sol", ordre: 2);
        _corbeilles = await Point("Vider les corbeilles", ordre: 1);
        _retire = await Point("Cirer la rampe", ordre: 3, actif: false);

        var client = await Creer<ClientDto>("/api/clients", new ClientCreateDto { Nom = "Syndic Cité", Type = "Syndic" });
        var site = await Creer<SiteDto>("/api/sites", new SiteCreateDto
        {
            ClientId = client.Id, Nom = "Résidence Cité", Adresse = "1 parvis Notre-Dame",
            Latitude = SurPlace.Latitude, Longitude = SurPlace.Longitude
        });
        _contrat = (await Creer<ContratDto>("/api/contrats", new ContratCreateDto
        {
            SiteId = site.Id, DateDebut = new DateTime(2027, 1, 1), Frequence = "Quotidien"
        })).Id;

        _agentSow = await CreerAgent("Sow");
        _agentKeita = await CreerAgent("Keita");

        _vacation = await CreerVacation(await Prestation(_hall), _agentSow.Id, new TimeSpan(8, 0, 0));
        _sow = await _fixture.ClientDeRoleAsync(_admin, RolesKlean.Agent, "sow@test.local", agentId: _agentSow.Id);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------------ les points de contrôle

    [Fact]
    public async Task Les_points_se_listent_par_type_dans_leur_ordre()
    {
        var autreType = (await Creer<TypePrestationDto>("/api/types-prestation",
            new TypePrestationCreateDto { Libelle = "Parking", DureeParDefautMinutes = 30 })).Id;
        await Point("Ramasser les papiers", type: autreType);

        var page = await _admin.GetFromJsonAsync<PageDto<PointControleDto>>($"{Points}?typePrestationId={_hall}");

        Assert.Equal(3, page!.Total);
        Assert.Equal(["Vider les corbeilles", "Laver le sol", "Cirer la rampe"], page.Elements.Select(p => p.Libelle));
    }

    [Fact]
    public async Task La_checklist_s_ecrit_par_l_administrateur_seul()
    {
        var responsable = await _fixture.ClientDeRoleAsync(_admin, RolesKlean.Responsable, "resp@test.local");

        Assert.Equal(HttpStatusCode.OK, (await responsable.GetAsync($"{Points}/{_sol.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await responsable.PostAsJsonAsync(Points, Corps("Essuyer", _hall))).StatusCode);
    }

    [Fact]
    public async Task Un_libelle_vide_ou_un_type_inconnu_est_refuse()
    {
        // Refusé par la validation de modèle, avant le contrôleur, donc en anglais : c'est
        // l'écran de référence qui rend ce cas inatteignable (CLAUDE.md §10).
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync(Points, Corps("   ", _hall))).StatusCode);

        var inconnu = await _admin.PostAsJsonAsync(Points, Corps("Essuyer", Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.BadRequest, inconnu.StatusCode);
        Assert.Contains("inconnu", await inconnu.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Un_type_qui_porte_une_checklist_ne_se_supprime_pas_et_le_dit()
    {
        var parking = (await Creer<TypePrestationDto>("/api/types-prestation",
            new TypePrestationCreateDto { Libelle = "Parking", DureeParDefautMinutes = 30 })).Id;
        await Point("Ramasser les papiers", type: parking);

        var suppression = await _admin.DeleteAsync($"/api/types-prestation/{parking}");

        Assert.Equal(HttpStatusCode.Conflict, suppression.StatusCode);
        Assert.Contains("1 point(s) de contrôle", await suppression.Content.ReadAsStringAsync());
    }

    // ------------------------------------------------------------------ la checklist du passage

    [Fact]
    public async Task La_journee_propose_les_points_actifs_dans_leur_ordre_sans_rien_coche()
    {
        var vacation = Assert.Single(await Journee());

        Assert.Equal(["Vider les corbeilles", "Laver le sol"], vacation.Checklist.Select(p => p.Libelle));
        Assert.All(vacation.Checklist, p => Assert.Null(p.Fait));
    }

    [Fact]
    public async Task Terminer_enregistre_chaque_point_fait_ou_non_et_ne_retient_pas_l_agent()
    {
        await Demarrer();

        var fin = await Terminer([_sol.Id]);

        // Un point non coché ne touche pas la certification : les deux pointages sont sur site.
        Assert.Equal("Terminee", fin.Statut);
        Assert.Equal(
            [("Vider les corbeilles", (bool?)false), ("Laver le sol", true)],
            fin.Checklist.Select(p => (p.Libelle, p.Fait)));
    }

    [Fact]
    public async Task Ce_qui_a_ete_declare_garde_le_libelle_du_moment()
    {
        await Demarrer();
        await Terminer([_corbeilles.Id, _sol.Id]);

        await _admin.PutAsJsonAsync($"{Points}/{_sol.Id}", Corps("Laver et sécher le sol", _hall, ordre: 2));
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"{Points}/{_corbeilles.Id}")).StatusCode);

        var relue = Assert.Single(await Journee()).Checklist;

        Assert.Equal(["Vider les corbeilles", "Laver le sol"], relue.Select(p => p.Libelle));
        Assert.Null(relue[0].PointControleId);
        Assert.Equal(_sol.Id, relue[1].PointControleId);
    }

    [Fact]
    public async Task Un_point_hors_de_la_checklist_est_refuse_avant_toute_ecriture()
    {
        await Demarrer();

        var reponse = await _sow.PostAsJsonAsync($"/api/interventions/{_vacation.Id}/terminer",
            new TerminaisonDto { Position = SurPlace, PointsFaits = [_retire.Id] });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("pas dans la checklist", await reponse.Content.ReadAsStringAsync());

        // Ni fin, ni compte rendu : la vacation est encore à terminer.
        var vacation = Assert.Single(await Journee());
        Assert.Equal(("EnCours", (PointageDto?)null), (vacation.Statut, vacation.Fin));
        Assert.Equal(0, await LireAsync(db => db.ControlesEffectues.CountAsync()));
    }

    [Fact]
    public async Task Une_vacation_sans_type_n_a_pas_de_checklist()
    {
        var sansType = await CreerVacation(await Prestation(type: null), _agentSow.Id, new TimeSpan(14, 0, 0));

        var vacation = (await Journee()).Single(v => v.Id == sansType.Id);

        Assert.Empty(vacation.Checklist);
    }

    // ------------------------------------------------------------------ les anomalies

    [Fact]
    public async Task Une_anomalie_se_signale_pendant_le_passage_seulement()
    {
        var avant = await _sow.PostAsJsonAsync(Anomalies, Signalement("Fuite sous l'escalier"));
        Assert.Equal(HttpStatusCode.BadRequest, avant.StatusCode);
        Assert.Contains("pendant le passage", await avant.Content.ReadAsStringAsync());

        await Demarrer();
        var pendant = await Signaler("Fuite sous l'escalier");
        Assert.Equal("Fuite sous l'escalier", pendant.Description);

        await Terminer([]);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _sow.PostAsJsonAsync(Anomalies, Signalement("Porte cassée"))).StatusCode);

        var relue = Assert.Single(Assert.Single(await Journee()).Anomalies);
        Assert.Equal(pendant.Id, relue.Id);
    }

    [Fact]
    public async Task Une_description_vide_ou_trop_longue_est_refusee_en_francais()
    {
        await Demarrer();

        var vide = await _sow.PostAsJsonAsync(Anomalies, Signalement("  "));
        Assert.Equal(HttpStatusCode.BadRequest, vide.StatusCode);
        Assert.Contains("Décrire l'anomalie", await vide.Content.ReadAsStringAsync());

        var longue = await _sow.PostAsJsonAsync(Anomalies, Signalement(new string('x', 1001)));
        Assert.Equal(HttpStatusCode.BadRequest, longue.StatusCode);
        Assert.Contains("1000 caractères", await longue.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Un_agent_ne_signale_rien_sur_la_vacation_d_un_collegue()
    {
        await Demarrer();
        var keita = await _fixture.ClientDeRoleAsync(_admin, RolesKlean.Agent, "keita@test.local", agentId: _agentKeita.Id);

        Assert.Equal(HttpStatusCode.NotFound,
            (await keita.PostAsJsonAsync(Anomalies, Signalement("Fuite"))).StatusCode);
    }

    // ------------------------------------------------------------------ les photos

    [Fact]
    public async Task Une_photo_perd_sa_position_avant_d_entrer_en_base()
    {
        await Demarrer();
        var anomalie = await Signaler("Fuite sous l'escalier");

        var ajout = await EnvoyerPhoto(anomalie.Id, JpegDeTest.AvecPosition());
        Assert.Equal(HttpStatusCode.Created, ajout.StatusCode);
        var photo = (await ajout.Content.ReadFromJsonAsync<PhotoAnomalieDto>())!;

        // ⚠️ En base, pas seulement à la relecture : c'est la base qu'une fuite livrerait.
        var stockee = await LireAsync(db => db.PhotosAnomalie.AsNoTracking().Select(p => p.Contenu).SingleAsync());
        Assert.False(JpegDeTest.Contient(stockee, JpegDeTest.Position));
        Assert.Equal(JpegDeTest.SansPosition(), stockee);
        Assert.Equal(stockee.Length, photo.Taille);

        var lue = await _admin.GetAsync($"{Anomalies}/{anomalie.Id}/photos/{photo.Id}");
        Assert.Equal("image/jpeg", lue.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", lue.Headers.CacheControl?.ToString());
        Assert.Equal(stockee, await lue.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Ce_qui_n_est_pas_un_jpeg_est_refuse()
    {
        await Demarrer();
        var anomalie = await Signaler("Fuite");

        // Un vrai JPEG, mal annoncé : c'est le type déclaré qui est refusé, pas le contenu.
        var malAnnonce = new ByteArrayContent(JpegDeTest.AvecPosition());
        malAnnonce.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var refus = await _sow.PostAsync($"{Anomalies}/{anomalie.Id}/photos", malAnnonce);
        Assert.Equal(HttpStatusCode.BadRequest, refus.StatusCode);
        Assert.Contains("en JPEG", await refus.Content.ReadAsStringAsync());

        var faux = await EnvoyerPhoto(anomalie.Id, [0xFF, 0xD8, 0x00, 0x01]);
        Assert.Equal(HttpStatusCode.BadRequest, faux.StatusCode);
        Assert.Contains("pas une photo JPEG lisible", await faux.Content.ReadAsStringAsync());

        Assert.Equal(0, await LireAsync(db => db.PhotosAnomalie.CountAsync()));
    }

    [Fact]
    public async Task Trois_photos_au_plus_par_anomalie()
    {
        await Demarrer();
        var anomalie = await Signaler("Fuite");

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await EnvoyerPhoto(anomalie.Id, JpegDeTest.AvecPosition())).StatusCode);
        }

        var quatrieme = await EnvoyerPhoto(anomalie.Id, JpegDeTest.AvecPosition());
        Assert.Equal(HttpStatusCode.Conflict, quatrieme.StatusCode);
        Assert.Equal(3, Assert.Single(Assert.Single(await Journee()).Anomalies).Photos);
    }

    // ------------------------------------------------------------------ le traitement, et qui voit quoi

    [Fact]
    public async Task Le_responsable_lit_les_anomalies_ouvertes_et_les_traite_une_fois()
    {
        await Demarrer();
        var anomalie = await Signaler("Fuite sous l'escalier");
        await EnvoyerPhoto(anomalie.Id, JpegDeTest.AvecPosition());

        var ouvertes = await _admin.GetFromJsonAsync<PageDto<AnomalieDto>>($"{Anomalies}?statut=Ouverte");
        var lue = Assert.Single(ouvertes!.Elements);
        Assert.Equal(("Résidence Cité", "Ouverte", 1), (lue.Site, lue.Statut, lue.Photos.Count));

        var traitement = await _admin.PostAsync($"{Anomalies}/{anomalie.Id}/traiter", null);
        Assert.Equal(HttpStatusCode.OK, traitement.StatusCode);
        var traitee = (await traitement.Content.ReadFromJsonAsync<AnomalieDto>())!;
        Assert.Equal("Traitee", traitee.Statut);
        Assert.NotNull(traitee.DateTraitement);
        Assert.NotNull(traitee.TraiteeParId);

        Assert.Equal(0, (await _admin.GetFromJsonAsync<PageDto<AnomalieDto>>($"{Anomalies}?statut=Ouverte"))!.Total);

        var seconde = await _admin.PostAsync($"{Anomalies}/{anomalie.Id}/traiter", null);
        Assert.Equal(HttpStatusCode.BadRequest, seconde.StatusCode);
        Assert.Contains("déjà traitée", await seconde.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Un_statut_de_liste_inconnu_est_refuse()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.GetAsync($"{Anomalies}?statut=0")).StatusCode);
    }

    [Fact]
    public async Task Un_chef_ne_voit_que_les_anomalies_de_son_equipe()
    {
        await Demarrer();
        var anomalie = await Signaler("Fuite");
        var photo = (await (await EnvoyerPhoto(anomalie.Id, JpegDeTest.AvecPosition())).Content
            .ReadFromJsonAsync<PhotoAnomalieDto>())!;

        var chefDeSow = await _fixture.ClientDeRoleAsync(_admin, RolesKlean.ChefEquipe, "chef.sow@test.local");
        var autreChef = await _fixture.ClientDeRoleAsync(_admin, RolesKlean.ChefEquipe, "chef.autre@test.local");
        await Rattacher(_agentSow, (await chefDeSow.GetFromJsonAsync<ProfilDto>("/api/auth/moi"))!.Id);

        Assert.Equal(1, (await chefDeSow.GetFromJsonAsync<PageDto<AnomalieDto>>(Anomalies))!.Total);

        Assert.Equal(0, (await autreChef.GetFromJsonAsync<PageDto<AnomalieDto>>(Anomalies))!.Total);
        Assert.Equal(HttpStatusCode.NotFound, (await autreChef.GetAsync($"{Anomalies}/{anomalie.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await autreChef.GetAsync($"{Anomalies}/{anomalie.Id}/photos/{photo.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await autreChef.PostAsync($"{Anomalies}/{anomalie.Id}/traiter", null)).StatusCode);
    }

    [Fact]
    public async Task Un_agent_lit_ses_photos_et_pas_celles_d_un_collegue()
    {
        await Demarrer();
        var anomalie = await Signaler("Fuite");
        var photo = (await (await EnvoyerPhoto(anomalie.Id, JpegDeTest.AvecPosition())).Content
            .ReadFromJsonAsync<PhotoAnomalieDto>())!;
        var route = $"{Anomalies}/{anomalie.Id}/photos/{photo.Id}";

        Assert.Equal(HttpStatusCode.OK, (await _sow.GetAsync(route)).StatusCode);

        var keita = await _fixture.ClientDeRoleAsync(_admin, RolesKlean.Agent, "keita@test.local", agentId: _agentKeita.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await keita.GetAsync(route)).StatusCode);

        // La liste reste aux rôles internes.
        Assert.Equal(HttpStatusCode.Forbidden, (await _sow.GetAsync(Anomalies)).StatusCode);
    }

    [Fact]
    public async Task Un_agent_n_ajoute_pas_de_photo_a_l_anomalie_d_un_collegue()
    {
        // À part de la lecture, qui ne repose que sur le filtre global : ici, le cadrage écrit
        // dans le contrôleur le double, et seul un test isolé dit s'il tient sans lui.
        await Demarrer();
        var anomalie = await Signaler("Fuite");
        var keita = await _fixture.ClientDeRoleAsync(_admin, RolesKlean.Agent, "keita@test.local", agentId: _agentKeita.Id);

        Assert.Equal(HttpStatusCode.NotFound,
            (await keita.PostAsync($"{Anomalies}/{anomalie.Id}/photos", Jpeg(JpegDeTest.AvecPosition()))).StatusCode);
        Assert.Equal(0, await LireAsync(db => db.PhotosAnomalie.CountAsync()));
    }

    [Fact]
    public async Task Une_photo_ne_s_ajoute_plus_une_fois_le_passage_termine()
    {
        await Demarrer();
        var anomalie = await Signaler("Fuite");
        await Terminer([]);

        var tardive = await EnvoyerPhoto(anomalie.Id, JpegDeTest.AvecPosition());

        Assert.Equal(HttpStatusCode.BadRequest, tardive.StatusCode);
        Assert.Contains("pendant le passage", await tardive.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Le_syndic_ne_voit_aucun_compte_rendu_meme_sous_l_api()
    {
        // ⚠️ Aucune route ne s'ouvre au syndic : seule une lecture sous l'API, avec son tenant,
        // éprouve la clause qui l'exclut des quatre filtres. Sans ce test, elle pourrait
        // disparaître sans rien casser — et le portail l'ouvrirait en grand.
        await Demarrer();
        var anomalie = await Signaler("Fuite");
        await EnvoyerPhoto(anomalie.Id, JpegDeTest.AvecPosition());
        await Terminer([_sol.Id]);

        var syndic = new TenantDeTest(_fixture.EntrepriseAlpha, clientId: Guid.NewGuid());
        var patron = new TenantDeTest(_fixture.EntrepriseAlpha, clientId: null);

        Assert.Equal((0, 0, 0, 0), await CompterAsync(syndic));
        Assert.Equal((2, 2, 1, 1), await CompterAsync(patron));
    }

    [Fact]
    public async Task Une_autre_entreprise_ne_voit_rien()
    {
        await Demarrer();
        var anomalie = await Signaler("Fuite");
        var beta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);

        Assert.Equal(0, (await beta.GetFromJsonAsync<PageDto<AnomalieDto>>(Anomalies))!.Total);
        Assert.Equal(HttpStatusCode.NotFound, (await beta.GetAsync($"{Anomalies}/{anomalie.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await beta.GetAsync($"{Points}/{_sol.Id}")).StatusCode);
    }

    // ------------------------------------------------------------------ outils

    private async Task Demarrer()
    {
        var reponse = await _sow.PostAsJsonAsync($"/api/interventions/{_vacation.Id}/demarrer",
            new PointageCreateDto { Position = SurPlace });
        Assert.True(reponse.IsSuccessStatusCode, await reponse.Content.ReadAsStringAsync());
    }

    private async Task<VacationAgentDto> Terminer(List<Guid> pointsFaits)
    {
        var reponse = await _sow.PostAsJsonAsync($"/api/interventions/{_vacation.Id}/terminer",
            new TerminaisonDto { Position = SurPlace, PointsFaits = pointsFaits });
        Assert.True(reponse.IsSuccessStatusCode, await reponse.Content.ReadAsStringAsync());

        return (await reponse.Content.ReadFromJsonAsync<VacationAgentDto>())!;
    }

    private async Task<List<VacationAgentDto>> Journee() =>
        (await _sow.GetFromJsonAsync<List<VacationAgentDto>>("/api/interventions/ma-journee"))!;

    private AnomalieCreateDto Signalement(string description) =>
        new() { InterventionId = _vacation.Id, Description = description };

    private async Task<AnomalieAgentDto> Signaler(string description)
    {
        var reponse = await _sow.PostAsJsonAsync(Anomalies, Signalement(description));
        Assert.True(reponse.StatusCode == HttpStatusCode.Created, await reponse.Content.ReadAsStringAsync());

        return (await reponse.Content.ReadFromJsonAsync<AnomalieAgentDto>())!;
    }

    private static ByteArrayContent Jpeg(byte[] octets)
    {
        var contenu = new ByteArrayContent(octets);
        contenu.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return contenu;
    }

    private Task<HttpResponseMessage> EnvoyerPhoto(Guid anomalie, byte[] octets) =>
        _sow.PostAsync($"{Anomalies}/{anomalie}/photos", Jpeg(octets));

    private static PointControleCreateDto Corps(string libelle, Guid type, int ordre = 0, bool actif = true) =>
        new() { TypePrestationId = type, Libelle = libelle, Ordre = ordre, Actif = actif };

    private Task<PointControleDto> Point(string libelle, int ordre = 0, bool actif = true, Guid? type = null) =>
        Creer<PointControleDto>(Points, Corps(libelle, type ?? _hall, ordre, actif));

    private async Task<Guid> Prestation(Guid? type) =>
        (await Creer<PrestationDto>("/api/prestations", new PrestationCreateDto
        {
            ContratId = _contrat, Libelle = "Hall", DureeEstimeeMinutes = 60, TypePrestationId = type
        })).Id;

    private async Task<InterventionDto> CreerVacation(Guid prestation, Guid agent, TimeSpan heure) =>
        await Creer<InterventionDto>("/api/interventions", new InterventionCreateDto
        {
            PrestationId = prestation,
            AgentId = agent,
            DatePrevue = Aujourdhui.ToDateTime(TimeOnly.MinValue),
            HeurePrevue = heure,
            Statut = "Planifiee"
        });

    private Task<AgentDto> CreerAgent(string nom) =>
        Creer<AgentDto>("/api/agents", new AgentCreateDto
        {
            Nom = nom, Prenom = "Test", DateEmbauche = new DateTime(2024, 1, 1), Latitude = 48.8584, Longitude = 2.2945
        });

    private async Task Rattacher(AgentDto agent, Guid chef)
    {
        var reponse = await _admin.PutAsJsonAsync($"/api/agents/{agent.Id}", new AgentCreateDto
        {
            Nom = agent.Nom, Prenom = agent.Prenom, DateEmbauche = agent.DateEmbauche,
            Latitude = agent.Latitude, Longitude = agent.Longitude, Actif = true, ChefEquipeId = chef
        });
        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);
    }

    private async Task<T> Creer<T>(string route, object corps)
    {
        var reponse = await _admin.PostAsJsonAsync(route, corps);

        Assert.True(reponse.StatusCode == HttpStatusCode.Created, await reponse.Content.ReadAsStringAsync());
        return (await reponse.Content.ReadFromJsonAsync<T>())!;
    }

    /// <summary>Pointages, contrôles déclarés, anomalies et photos, vus par ce tenant.</summary>
    private async Task<(int, int, int, int)> CompterAsync(ITenantContext tenant)
    {
        (int, int, int, int) compte = default;

        await _fixture.AvecServicesAsync(async services =>
        {
            await using var db = new KleanDbContext(
                services.GetRequiredService<DbContextOptions<KleanDbContext>>(), tenant);

            compte = (await db.Pointages.CountAsync(), await db.ControlesEffectues.CountAsync(),
                await db.Anomalies.CountAsync(), await db.PhotosAnomalie.CountAsync());
        });

        return compte;
    }

    private sealed class TenantDeTest(Guid entrepriseId, Guid? clientId) : ITenantContext
    {
        public Guid? EntrepriseId => entrepriseId;
        public Guid? ClientId => clientId;
        public Guid? AgentId => null;
        public Guid? ChefEquipeId => null;
        public bool FiltrageActif => true;
        public void DesactiverFiltrage() => throw new InvalidOperationException("Pas dans ce test.");
        public Guid PoserEntrepriseInstallation() => throw new InvalidOperationException("Pas dans ce test.");
    }

    private async Task<T> LireAsync<T>(Func<KleanDbContext, Task<T>> lecture)
    {
        T resultat = default!;
        await _fixture.AvecServicesAsync(async services =>
            resultat = await lecture(services.GetRequiredService<KleanDbContext>()));
        return resultat;
    }
}
