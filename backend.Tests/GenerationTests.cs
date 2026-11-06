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
/// Génération des vacations depuis la récurrence des prestations —
/// <c>POST /api/planning/generer</c> (SPEC.md, étape 20).
///
/// L'horloge est fixée à chaque test : « douze semaines à partir d'aujourd'hui » ne
/// s'éprouve pas sur un jour qui change. Deux semaines de référence :
///
/// - du lundi 24 mai au dimanche 6 juin 2027, sans aucun jour férié ;
/// - du lundi 3 au dimanche 9 mai 2027, qui porte l'Ascension (jeudi 6) et la
///   Victoire 1945 (samedi 8).
///
/// Les vacations créées sont relues <b>en base</b>, par prestation : le rapport dit ce
/// que le service croit avoir fait, la base dit ce qu'il a fait.
/// </summary>
[Collection(CollectionApi.Nom)]
public class GenerationTests : IAsyncLifetime
{
    private static readonly DateOnly Lundi24Mai = new(2027, 5, 24);
    private static readonly DateOnly Dimanche6Juin = new(2027, 6, 6);
    private static readonly DateOnly Lundi3Mai = new(2027, 5, 3);
    private static readonly DateOnly Dimanche9Mai = new(2027, 5, 9);

    private static readonly TimeSpan Sept = new(7, 0, 0);

    private readonly FixtureApi _fixture;
    private HttpClient _client = null!;

    public GenerationTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
        _fixture.Fabrique.Horloge.Fixer(Lundi24Mai);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ----------------------------------------------------------- ce qui est créé

    [Fact]
    public async Task Les_jours_coches_sont_generes_a_l_heure_de_la_prestation_avec_le_titulaire()
    {
        var titulaire = await Agent("Titulaire");
        var prestation = await Prestation(await Contrat(), ["Lundi", "Jeudi"], titulaire.Id);

        var rapport = await Rapport(Dimanche6Juin);

        Assert.Equal(4, rapport.Creees);
        Assert.Equal(4, rapport.Pourvues);
        Assert.Empty(rapport.NonPourvues);
        Assert.Equal(Utc(Lundi24Mai), rapport.Du);
        Assert.Equal(Utc(Dimanche6Juin), rapport.Au);

        var vacations = await Vacations(prestation.Id);

        Assert.Equal(
            [new DateTime(2027, 5, 24), new DateTime(2027, 5, 27), new DateTime(2027, 5, 31), new DateTime(2027, 6, 3)],
            vacations.Select(v => v.DatePrevue.Date));
        Assert.All(vacations, v =>
        {
            Assert.Equal(Sept, v.HeurePrevue);
            Assert.Equal(titulaire.Id, v.AgentId);
            Assert.Equal(StatutIntervention.Planifiee, v.Statut);
        });
    }

    [Fact]
    public async Task Une_prestation_sans_jours_n_est_jamais_generee()
    {
        var prestation = await Prestation(await Contrat(), [], titulaire: null);

        var rapport = await Rapport(Dimanche6Juin);

        Assert.Equal(0, rapport.Creees);
        Assert.Empty(await Vacations(prestation.Id));
    }

    [Fact]
    public async Task Rien_n_est_genere_hors_des_dates_du_contrat_bornes_incluses()
    {
        var contrat = await Contrat(debut: new DateTime(2027, 5, 27), fin: new DateTime(2027, 6, 3));
        var prestation = await Prestation(contrat, ["Lundi", "Jeudi"], titulaire: null);

        await Rapport(Dimanche6Juin);

        Assert.Equal(
            [new DateTime(2027, 5, 27), new DateTime(2027, 5, 31), new DateTime(2027, 6, 3)],
            (await Vacations(prestation.Id)).Select(v => v.DatePrevue.Date));
    }

    [Fact]
    public async Task Un_contrat_inactif_n_est_pas_genere()
    {
        var contrat = await Contrat(actif: false);
        var prestation = await Prestation(contrat, ["Lundi"], titulaire: null);

        await Rapport(Dimanche6Juin);

        Assert.Empty(await Vacations(prestation.Id));
    }

    // ------------------------------------------------------------- l'idempotence

    [Fact]
    public async Task Rejouee_la_generation_ne_recree_rien()
    {
        var prestation = await Prestation(await Contrat(), ["Lundi", "Jeudi"], await Id(Agent("Titulaire")));

        await Rapport(Dimanche6Juin);
        var seconde = await Rapport(Dimanche6Juin);

        Assert.Equal(0, seconde.Creees);
        Assert.Equal(4, seconde.DejaPresentes);
        Assert.Equal(4, (await Vacations(prestation.Id)).Count);
    }

    /// <summary>
    /// Une annulation est une décision humaine. La génération suivante qui recréerait la
    /// vacation la déferait sans que personne ne s'en aperçoive.
    /// </summary>
    [Fact]
    public async Task Une_vacation_annulee_n_est_pas_recreee()
    {
        var prestation = await Prestation(await Contrat(), ["Lundi"], titulaire: null);
        await Rapport(Lundi24Mai);

        var vacation = (await Vacations(prestation.Id)).Single();
        await Modifier($"/api/interventions/{vacation.Id}", new
        {
            vacation.PrestationId,
            AgentId = (Guid?)null,
            vacation.DatePrevue,
            vacation.HeurePrevue,
            Statut = "Annulee"
        });

        var rapport = await Rapport(Lundi24Mai);

        Assert.Equal(0, rapport.Creees);
        Assert.Equal(1, rapport.DejaPresentes);
        Assert.Single(await Vacations(prestation.Id));
    }

    /// <summary>
    /// Un double clic suffit. Sans le verrou consultatif, chaque requête lit « rien
    /// n'existe » avant que l'autre n'écrive, et chaque vacation est créée deux fois.
    ///
    /// ⚠️ Le titulaire n'est pas décoratif : il élargit la fenêtre de course. Sans lui,
    /// quatorze vacations s'écrivent en un seul aller-retour, la première requête finit
    /// avant que la seconde ait lu, et ce test restait vert avec le verrou retiré — le
    /// sabotage l'a montré. Avec lui, chaque création interroge le moteur et s'écrit
    /// aussitôt.
    /// </summary>
    [Fact]
    public async Task Deux_generations_simultanees_ne_dupliquent_rien()
    {
        var prestation = await Prestation(await Contrat(), TousLesJours, await Id(Agent("Titulaire")));

        var corps = new { JusquAu = Dimanche6Juin.ToString("yyyy-MM-dd") };
        var reponses = await Task.WhenAll(
            _client.PostAsJsonAsync("/api/planning/generer", corps),
            _client.PostAsJsonAsync("/api/planning/generer", corps));

        Assert.All(reponses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(14, (await Vacations(prestation.Id)).Count);
    }

    // ---------------------------------------------------------------- le titulaire

    [Fact]
    public async Task Sans_titulaire_la_vacation_est_creee_non_pourvue_et_signalee()
    {
        var prestation = await Prestation(await Contrat(), ["Lundi"], titulaire: null);

        var rapport = await Rapport(Lundi24Mai);

        var ligne = Assert.Single(rapport.NonPourvues);
        Assert.Equal(ServiceGeneration.CauseSansTitulaire, ligne.Cause);
        Assert.Equal("Sols", ligne.Prestation);
        Assert.Equal("Résidence Bellevue", ligne.Site);

        var vacation = Assert.Single(await Vacations(prestation.Id));
        Assert.Null(vacation.AgentId);
        Assert.Equal(vacation.Id, ligne.InterventionId);
    }

    /// <summary>
    /// Aucun chemin n'affecte un agent inéligible (CLAUDE.md §9), et la génération pas
    /// plus qu'un autre : un titulaire en congé laisse la vacation à pourvoir.
    /// </summary>
    [Fact]
    public async Task Un_titulaire_absent_laisse_la_vacation_non_pourvue_ce_jour_la_seulement()
    {
        var titulaire = await Agent("Zebulon");
        var prestation = await Prestation(await Contrat(), ["Lundi", "Jeudi"], titulaire.Id);

        await Creer<AbsenceDto>("/api/absences", new
        {
            AgentId = titulaire.Id,
            Type = "Conge",
            DateDebut = new DateTime(2027, 5, 27),
            DateFin = new DateTime(2027, 5, 27)
        });

        var reponse = await Generer(Dimanche6Juin);
        var brut = await reponse.Content.ReadAsStringAsync();
        var rapport = (await reponse.Content.ReadFromJsonAsync<RapportGenerationDto>())!;

        Assert.Equal(3, rapport.Pourvues);
        var ligne = Assert.Single(rapport.NonPourvues);
        Assert.Equal(new DateTime(2027, 5, 27), ligne.Date.Date);
        Assert.Equal(ServiceGeneration.CauseTitulaireIneligible, ligne.Cause);

        var jeudi = (await Vacations(prestation.Id)).Single(v => v.DatePrevue.Date == new DateTime(2027, 5, 27));
        Assert.Null(jeudi.AgentId);

        // Le rapport ne nomme personne : ni nom, ni identifiant d'agent. L'assertion
        // porte sur le corps brut, comme pour le dossier : une propriété ajoutée demain
        // n'apparaîtrait dans aucune assertion typée.
        Assert.DoesNotContain("Zebulon", brut);
        Assert.DoesNotContain(titulaire.Id.ToString(), brut, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Le moteur lit la base. Une vacation affectée mais encore en mémoire ne compterait
    /// pas comme occupation, et le même titulaire serait affecté deux fois au même
    /// créneau — par le chemin même qui prétend vérifier son éligibilité.
    /// </summary>
    [Fact]
    public async Task Un_titulaire_n_est_pas_affecte_a_deux_vacations_qui_se_chevauchent()
    {
        var titulaire = await Id(Agent("Titulaire"));
        var contrat = await Contrat();
        var sols = await Prestation(contrat, ["Lundi"], titulaire, libelle: "Sols");
        var vitres = await Prestation(contrat, ["Lundi"], titulaire, libelle: "Vitres");

        var rapport = await Rapport(Lundi24Mai);

        Assert.Equal(2, rapport.Creees);
        Assert.Equal(1, rapport.Pourvues);
        Assert.Equal(ServiceGeneration.CauseTitulaireIneligible, Assert.Single(rapport.NonPourvues).Cause);

        var affectees = (await Vacations(sols.Id)).Concat(await Vacations(vitres.Id))
            .Count(v => v.AgentId == titulaire);
        Assert.Equal(1, affectees);
    }

    // ------------------------------------------------------------ les jours fériés

    [Fact]
    public async Task Les_jours_feries_sont_sautes_et_nommes_dans_le_rapport()
    {
        _fixture.Fabrique.Horloge.Fixer(Lundi3Mai);
        var prestation = await Prestation(await Contrat(), TousLesJours, titulaire: null);

        var rapport = await Rapport(Dimanche9Mai);

        Assert.Equal(5, rapport.Creees);
        Assert.Equal(
            ["Ascension, que le contrat n'inclut pas.", "Victoire 1945, que le contrat n'inclut pas."],
            rapport.Feries.Select(f => f.Cause));
        Assert.All(rapport.Feries, f => Assert.Null(f.InterventionId));

        var jours = (await Vacations(prestation.Id)).Select(v => v.DatePrevue.Date).ToList();
        Assert.DoesNotContain(new DateTime(2027, 5, 6), jours);
        Assert.DoesNotContain(new DateTime(2027, 5, 8), jours);
    }

    [Fact]
    public async Task Un_contrat_qui_inclut_les_feries_y_est_genere()
    {
        _fixture.Fabrique.Horloge.Fixer(Lundi3Mai);
        var prestation = await Prestation(await Contrat(inclutFeries: true), TousLesJours, titulaire: null);

        var rapport = await Rapport(Dimanche9Mai);

        Assert.Equal(7, rapport.Creees);
        Assert.Empty(rapport.Feries);
        Assert.Equal(7, (await Vacations(prestation.Id)).Count);
    }

    [Fact]
    public async Task Une_entreprise_d_Alsace_Moselle_ne_genere_pas_son_Vendredi_saint()
    {
        // 2027 : Vendredi saint le 26 mars. Férié en Alsace-Moselle seulement.
        _fixture.Fabrique.Horloge.Fixer(new DateOnly(2027, 3, 22));
        await Territoire("AlsaceMoselle");
        var prestation = await Prestation(await Contrat(), ["Vendredi"], titulaire: null);

        var rapport = await Rapport(new DateOnly(2027, 4, 2));

        Assert.Equal(["Vendredi saint, que le contrat n'inclut pas."], rapport.Feries.Select(f => f.Cause));
        Assert.Equal([new DateTime(2027, 4, 2)], (await Vacations(prestation.Id)).Select(v => v.DatePrevue.Date));
    }

    [Fact]
    public async Task En_Metropole_le_Vendredi_saint_est_travaille()
    {
        _fixture.Fabrique.Horloge.Fixer(new DateOnly(2027, 3, 22));
        var prestation = await Prestation(await Contrat(), ["Vendredi"], titulaire: null);

        var rapport = await Rapport(new DateOnly(2027, 4, 2));

        Assert.Empty(rapport.Feries);
        Assert.Equal(2, (await Vacations(prestation.Id)).Count);
    }

    // ------------------------------------------------------------------ le fuseau

    [Fact]
    public async Task Le_jour_de_depart_est_celui_du_territoire_et_non_le_jour_UTC()
    {
        // Dimanche 23 mai, 23 h UTC : lundi 24 à 1 h du matin à Paris, dimanche 19 h en
        // Guadeloupe. Jusqu'au 4 novembre 2026, la génération partait du dimanche partout.
        _fixture.Fabrique.Horloge.Fixer(new DateOnly(2027, 5, 23), heure: 23);
        var prestation = await Prestation(await Contrat(), ["Dimanche", "Lundi"], titulaire: null);

        var paris = await Rapport(Lundi24Mai);

        Assert.Equal(Utc(Lundi24Mai), paris.Du);
        Assert.Equal([new DateTime(2027, 5, 24)], (await Vacations(prestation.Id)).Select(v => v.DatePrevue.Date));

        await Territoire("Guadeloupe");
        var guadeloupe = await Rapport(Lundi24Mai);

        Assert.Equal(Utc(new DateOnly(2027, 5, 23)), guadeloupe.Du);
    }

    [Fact]
    public async Task Le_refus_d_une_date_passee_compte_en_jour_du_territoire()
    {
        // Lundi 24 à 1 h à Paris : demander jusqu'au dimanche 23 est demander le passé.
        _fixture.Fabrique.Horloge.Fixer(new DateOnly(2027, 5, 23), heure: 23);

        var reponse = await Generer(new DateOnly(2027, 5, 23));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("2027-05-24", await reponse.Content.ReadAsStringAsync());
    }

    // ------------------------------------------------------------- les orphelines

    /// <summary>
    /// L'heure de la prestation change : les vacations déjà créées restent à l'ancienne
    /// heure, la génération ne les recrée pas — le jour est occupé — et elle le dit.
    /// </summary>
    [Fact]
    public async Task Une_heure_changee_signale_les_vacations_sans_les_toucher()
    {
        var contrat = await Contrat();
        var prestation = await Prestation(contrat, ["Lundi", "Jeudi"], titulaire: null);
        await Rapport(Dimanche6Juin);

        await Modifier($"/api/prestations/{prestation.Id}", CorpsPrestation(contrat, ["Lundi", "Jeudi"], null, heure: "09:00:00"));

        var rapport = await Rapport(Dimanche6Juin);

        Assert.Equal(0, rapport.Creees);
        Assert.Equal(4, rapport.Orphelines.Count);
        Assert.All(rapport.Orphelines, o =>
            Assert.Equal("L'heure ne correspond pas à celle de la prestation.", o.Cause));
        Assert.All(await Vacations(prestation.Id), v => Assert.Equal(Sept, v.HeurePrevue));
    }

    [Fact]
    public async Task Un_jour_retire_signale_ses_vacations_sans_les_supprimer()
    {
        var contrat = await Contrat();
        var prestation = await Prestation(contrat, ["Lundi", "Jeudi"], titulaire: null);
        await Rapport(Dimanche6Juin);

        await Modifier($"/api/prestations/{prestation.Id}", CorpsPrestation(contrat, ["Lundi"], null));

        var rapport = await Rapport(Dimanche6Juin);

        Assert.Equal(
            [new DateTime(2027, 5, 27), new DateTime(2027, 6, 3)],
            rapport.Orphelines.Select(o => o.Date.Date));
        Assert.All(rapport.Orphelines, o => Assert.Equal("Ce jour n'est pas prévu par la prestation.", o.Cause));
        Assert.Equal(4, (await Vacations(prestation.Id)).Count);
    }

    /// <summary>
    /// Le cas réel du territoire posé après coup : les vendredis ont été générés en
    /// Métropole, puis l'entreprise se déclare en Alsace-Moselle. Le Vendredi saint déjà
    /// créé n'est pas supprimé — la génération n'ajoute que —, il est signalé.
    /// </summary>
    [Fact]
    public async Task Un_territoire_pose_apres_coup_signale_le_ferie_deja_genere()
    {
        _fixture.Fabrique.Horloge.Fixer(new DateOnly(2027, 3, 22));
        var prestation = await Prestation(await Contrat(), ["Vendredi"], titulaire: null);
        await Rapport(new DateOnly(2027, 4, 2));

        await Territoire("AlsaceMoselle");
        var rapport = await Rapport(new DateOnly(2027, 4, 2));

        var orpheline = Assert.Single(rapport.Orphelines);
        Assert.Equal((new DateTime(2027, 3, 26), "Vendredi saint, que le contrat n'inclut pas."),
            (orpheline.Date.Date, orpheline.Cause));
        Assert.Equal(2, (await Vacations(prestation.Id)).Count);
    }

    [Fact]
    public async Task Un_contrat_desactive_signale_ses_vacations_planifiees_seulement()
    {
        var contrat = await Contrat();
        var prestation = await Prestation(contrat, ["Lundi", "Jeudi"], titulaire: null);
        await Rapport(Dimanche6Juin);

        // Une vacation terminée est un fait, pas un plan : elle ne se signale pas.
        var premiere = (await Vacations(prestation.Id)).First();
        await Modifier($"/api/interventions/{premiere.Id}", new
        {
            premiere.PrestationId,
            AgentId = (Guid?)null,
            premiere.DatePrevue,
            premiere.HeurePrevue,
            Statut = "NonCertifiee"
        });

        await Modifier($"/api/contrats/{contrat.Id}", new
        {
            contrat.SiteId,
            contrat.DateDebut,
            contrat.Frequence,
            Actif = false
        });

        var rapport = await Rapport(Dimanche6Juin);

        Assert.Equal(3, rapport.Orphelines.Count);
        Assert.All(rapport.Orphelines, o => Assert.Equal("Le contrat n'est plus actif.", o.Cause));
        Assert.Equal(4, (await Vacations(prestation.Id)).Count);
    }

    /// <summary>
    /// Une génération interrompue — délai dépassé, appelant parti — ne laisse rien : elle
    /// est défaite en entier, y compris les vacations pourvues déjà écrites en cours de
    /// route.
    ///
    /// L'ordre est garanti par le verrou consultatif : la seconde génération attend que la
    /// première ait fini, validée ou défaite, avant de lire. Si la première avait validé
    /// malgré l'annulation, la seconde ne créerait rien.
    ///
    /// ⚠️ <b>Le travail doit durer plus longtemps que la minuterie, de loin.</b> Sur quatre
    /// semaines, soit 560 vacations, la génération finissait en moins de 300 ms une fois le
    /// processus chaud : l'annulation n'arrivait jamais, et le test échouait dans la suite
    /// complète en passant isolé. D'où l'horizon entier. Raccourcir la minuterie ne vaut
    /// rien : l'annulation partirait avant que la requête n'atteigne le serveur, et le test
    /// passerait sans rien éprouver.
    /// </summary>
    [Fact]
    public async Task Une_generation_interrompue_est_defaite_en_entier()
    {
        for (var n = 0; n < 20; n++)
        {
            await Prestation(await Contrat(), TousLesJours, await Id(Agent($"Titulaire{n}")));
        }

        var horizon = Lundi24Mai.AddDays(7 * ServiceGeneration.SemainesMaximum);

        using (var minuterie = new CancellationTokenSource(TimeSpan.FromMilliseconds(300)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                _client.PostAsJsonAsync("/api/planning/generer",
                    new { JusquAu = horizon.ToString("yyyy-MM-dd") }, minuterie.Token));
        }

        var rapport = await Rapport(horizon);

        // Le 14 juillet et le 15 août tombent dans la période, et sont sautés.
        var jours = Enumerable.Range(0, horizon.DayNumber - Lundi24Mai.DayNumber + 1)
            .Count(d => JoursFeries.Nom(Lundi24Mai.AddDays(d)) is null);

        Assert.Equal(0, rapport.DejaPresentes);
        Assert.Equal(20 * jours, rapport.Creees);
    }

    /// <summary>
    /// Le délai général de 30 secondes coupait une génération de 8 850 vacations, mesurée
    /// à 47 secondes. Aucun test ne peut l'éprouver en temps réel ; celui-ci garde au moins
    /// que la politique propre est déclarée sur l'action et existe dans la configuration.
    /// </summary>
    [Fact]
    public void La_generation_a_son_propre_delai()
    {
        var action = typeof(Klean.Api.Controllers.PlanningController).GetMethod("Generer")!;
        var attribut = action.GetCustomAttributes(typeof(Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutAttribute), false)
            .Cast<Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutAttribute>()
            .Single();

        Assert.Equal(ServiceGeneration.PolitiqueDelai, attribut.PolicyName);

        var options = _fixture.Fabrique.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutOptions>>()
            .Value;

        Assert.Equal(TimeSpan.FromMinutes(2), options.Policies[ServiceGeneration.PolitiqueDelai].Timeout);
    }

    // ----------------------------------------------------------------- les bornes

    [Fact]
    public async Task Sans_date_de_fin_la_demande_est_refusee()
    {
        var reponse = await _client.PostAsJsonAsync("/api/planning/generer", new { });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("jusquAu", await reponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Une_date_passee_est_refusee()
    {
        var reponse = await Generer(Lundi24Mai.AddDays(-1));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("passée", await reponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Douze_semaines_passent_et_un_jour_de_plus_est_refuse()
    {
        var prestation = await Prestation(await Contrat(), ["Lundi"], titulaire: null);
        var horizon = Lundi24Mai.AddDays(7 * ServiceGeneration.SemainesMaximum);

        var refus = await Generer(horizon.AddDays(1));
        Assert.Equal(HttpStatusCode.BadRequest, refus.StatusCode);
        Assert.Contains("12 semaines", await refus.Content.ReadAsStringAsync());
        Assert.Empty(await Vacations(prestation.Id));

        var rapport = await Rapport(horizon);
        Assert.Equal(13, rapport.Creees);
    }

    [Fact]
    public async Task Le_chef_d_equipe_ne_genere_pas()
    {
        var chef = await _fixture.ClientDeRoleAsync(_client, RolesKlean.ChefEquipe, "chef@test.local");

        var reponse = await chef.PostAsJsonAsync("/api/planning/generer",
            new { JusquAu = Dimanche6Juin.ToString("yyyy-MM-dd") });

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    [Fact]
    public async Task Une_autre_entreprise_ne_genere_pas_les_prestations_d_alpha()
    {
        var prestation = await Prestation(await Contrat(), ["Lundi", "Jeudi"], titulaire: null);
        var beta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);

        var reponse = await beta.PostAsJsonAsync("/api/planning/generer",
            new { JusquAu = Dimanche6Juin.ToString("yyyy-MM-dd") });
        var rapport = (await reponse.Content.ReadFromJsonAsync<RapportGenerationDto>())!;

        Assert.Equal(0, rapport.Creees);
        Assert.Empty(await Vacations(prestation.Id));
    }

    // ------------------------------------------------------------------ outillage

    private static readonly string[] TousLesJours =
        ["Lundi", "Mardi", "Mercredi", "Jeudi", "Vendredi", "Samedi", "Dimanche"];

    private static DateTime Utc(DateOnly jour) =>
        DateTime.SpecifyKind(jour.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

    /// <summary>Change le territoire d'Alpha par l'API, comme le ferait l'administrateur.</summary>
    private async Task Territoire(string territoire)
    {
        var alpha = Assert.Single(await _client.ListerAsync<EntrepriseDto>("/api/entreprises"));

        var reponse = await _client.PutAsJsonAsync($"/api/entreprises/{alpha.Id}", new EntrepriseCreateDto
        {
            RaisonSociale = alpha.RaisonSociale, Siret = alpha.Siret, Territoire = territoire
        });

        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);
    }

    private Task<HttpResponseMessage> Generer(DateOnly jusquAu) =>
        _client.PostAsJsonAsync("/api/planning/generer", new { JusquAu = jusquAu.ToString("yyyy-MM-dd") });

    private async Task<RapportGenerationDto> Rapport(DateOnly jusquAu)
    {
        var reponse = await Generer(jusquAu);

        Assert.True(reponse.StatusCode == HttpStatusCode.OK, await reponse.Content.ReadAsStringAsync());

        return (await reponse.Content.ReadFromJsonAsync<RapportGenerationDto>())!;
    }

    /// <summary>Lues en base, hors de tout tenant : la vérité, pas ce que l'API veut bien montrer.</summary>
    private async Task<List<Intervention>> Vacations(Guid prestationId)
    {
        using var portee = _fixture.PorteeAuProprietaire();
        var db = portee.ServiceProvider.GetRequiredService<KleanDbContext>();

        return await db.Interventions.AsNoTracking()
            .Where(i => i.PrestationId == prestationId)
            .OrderBy(i => i.DatePrevue).ThenBy(i => i.HeurePrevue)
            .ToListAsync();
    }

    private async Task<T> Creer<T>(string route, object corps)
    {
        var reponse = await _client.PostAsJsonAsync(route, corps);

        Assert.True(reponse.StatusCode == HttpStatusCode.Created, await reponse.Content.ReadAsStringAsync());

        return (await reponse.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task Modifier(string route, object corps)
    {
        var reponse = await _client.PutAsJsonAsync(route, corps);

        Assert.True(reponse.StatusCode == HttpStatusCode.NoContent, await reponse.Content.ReadAsStringAsync());
    }

    private async Task<ContratDto> Contrat(
        DateTime? debut = null, DateTime? fin = null, bool actif = true, bool inclutFeries = false)
    {
        var client = await Creer<ClientDto>("/api/clients", new ClientCreateDto
        {
            Nom = "Syndic " + Guid.NewGuid().ToString("N")[..8],
            Type = "Syndic"
        });

        var site = await Creer<SiteDto>("/api/sites", new SiteCreateDto
        {
            ClientId = client.Id,
            Nom = "Résidence Bellevue",
            Adresse = "1 rue du Test",
            Latitude = 48.85,
            Longitude = 2.33
        });

        return await Creer<ContratDto>("/api/contrats", new ContratCreateDto
        {
            SiteId = site.Id,
            DateDebut = debut ?? new DateTime(2027, 1, 1),
            DateFin = fin,
            Frequence = "Récurrent",
            Actif = actif,
            InclutJoursFeries = inclutFeries
        });
    }

    private Task<PrestationDto> Prestation(
        ContratDto contrat, string[] jours, Guid? titulaire, string libelle = "Sols") =>
        Creer<PrestationDto>("/api/prestations", CorpsPrestation(contrat, jours, titulaire, libelle: libelle));

    private static object CorpsPrestation(
        ContratDto contrat, string[] jours, Guid? titulaire, string heure = "07:00:00", string libelle = "Sols") => new
    {
        ContratId = contrat.Id,
        Libelle = libelle,
        DureeEstimeeMinutes = 120,
        Jours = jours,
        HeureDebut = jours.Length == 0 ? null : heure,
        AgentTitulaireId = titulaire
    };

    private Task<AgentDto> Agent(string nom) =>
        Creer<AgentDto>("/api/agents", new AgentCreateDto
        {
            Nom = nom,
            Prenom = "Test",
            DateEmbauche = new DateTime(2024, 1, 1),
            Latitude = 48.86,
            Longitude = 2.35
        });

    private static async Task<Guid> Id(Task<AgentDto> agent) => (await agent).Id;
}
