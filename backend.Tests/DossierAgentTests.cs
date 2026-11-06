using System.Net;
using System.Net.Http.Json;
using System.Text;
using Klean.Api.Data;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Klean.Api.Services;
using Klean.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Klean.Api.Tests;

/// <summary>
/// Dossier complet d'un agent — <c>GET /api/agents/{id}/dossier</c> (RGPD.md §9.4).
///
/// Il sert l'accès de l'article 15 et la portabilité de l'article 20, deux droits que le
/// produit ne servait pas : il fallait composer la réponse à partir de cinq appels, sans
/// garantie de n'en oublier aucun.
///
/// ⚠️ Un dossier est une divulgation massive par construction, et ce que cette série
/// garde est donc double : qu'il soit <b>complet</b> — un oubli fait une réponse
/// incomplète à une demande légale — et qu'il reste <b>borné</b> : il ne doit ni livrer
/// l'empreinte du mot de passe, ni nommer les collègues qui ont consulté la fiche, ni
/// traverser la frontière du tenant.
/// </summary>
[Collection(CollectionApi.Nom)]
public class DossierAgentTests : IAsyncLifetime
{
    private readonly FixtureApi _fixture;
    private HttpClient _client = null!;

    public DossierAgentTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------- la complétude

    [Fact]
    public async Task Le_dossier_porte_la_fiche_de_l_agent()
    {
        var agent = await CreerAgentRenseigne();

        var dossier = await Dossier(agent);

        Assert.Equal(agent, dossier.Agent.Id);
        Assert.Equal("Diallo", dossier.Agent.Nom);
        Assert.Equal("14 rue des Lilas, Paris", dossier.Agent.Adresse);
        Assert.Equal(48.8566, dossier.Agent.Latitude);
        Assert.True(dossier.Genere > DateTime.UtcNow.AddMinutes(-5), "Le dossier n'est pas daté.");
    }

    [Fact]
    public async Task Le_dossier_porte_les_absences_avec_leur_type_et_leurs_dates()
    {
        var agent = await CreerAgentRenseigne();
        await CreerAbsence(agent, "Conge", "Vacances d'été",
            new DateTime(2026, 7, 1), new DateTime(2026, 7, 15));
        await CreerAbsence(agent, "Maladie", null,
            new DateTime(2026, 9, 1), new DateTime(2026, 9, 3));

        var dossier = await Dossier(agent);

        Assert.Equal(2, dossier.Absences.Count);

        // Le motif est communiqué quand il existe : c'est une donnée de la personne, et
        // l'article 15 ne permet pas de la lui cacher dans sa propre réponse d'accès.
        var conge = dossier.Absences.Single(a => a.Type == "Conge");
        Assert.Equal("Vacances d'été", conge.Motif);

        // L'arrêt maladie n'en porte pas, le serveur le refuse depuis l'étape 8.
        Assert.Null(dossier.Absences.Single(a => a.Type == "Maladie").Motif);
    }

    [Fact]
    public async Task Le_dossier_nomme_le_site_et_la_ville_plutot_qu_un_identifiant()
    {
        var agent = await CreerAgentRenseigne();
        await ChaineAvecVacation(agent);

        var dossier = await Dossier(agent);

        var vacation = Assert.Single(dossier.Interventions);

        // ⚠️ C'est l'exigence de forme intelligible de l'article 15. Une liste de
        // `PrestationId` en UUID ne dit rien à un salarié qui demande où il a
        // travaillé — et c'est exactement ce que `InterventionDto` aurait rendu.
        Assert.Equal("Résidence Dossier", vacation.Site);
        Assert.Equal("Paris", vacation.Ville);
        Assert.Equal("Nettoyage parties communes", vacation.Prestation);
        Assert.Equal(new TimeSpan(8, 30, 0), vacation.HeurePrevue);
    }

    /// <summary>
    /// Ses passages sont des données le concernant, et les seules dont il est l'auteur
    /// (RGPD.md §9.8) : ils manquaient au dossier, écrit à l'étape 17, et la réponse
    /// d'accès avait l'air complète. Rangés sous leur vacation, et seulement les siens.
    /// </summary>
    [Fact]
    public async Task Le_dossier_porte_ses_passages_sous_leur_vacation()
    {
        var agent = await CreerAgentRenseigne();
        var collegue = await CreerAgentRenseigne("Keita", "Awa");
        var vacation = await ChaineAvecVacation(agent);
        var autre = await ChaineAvecVacation(collegue);

        await AvecDbAsync(async db =>
        {
            db.Pointages.AddRange(
                Pointage(vacation, MomentPointage.Debut, PreuvePointage.SurSite, 40, 15),
                Pointage(vacation, MomentPointage.Fin, PreuvePointage.HorsRayon, 900, 30),
                Pointage(autre, MomentPointage.Debut, PreuvePointage.SurSite, 10, 10));
            db.ControlesEffectues.AddRange(
                Controle(vacation, "Vitres", 1, fait: true),
                Controle(vacation, "Hall", 0, fait: false),
                Controle(autre, "Ascenseur", 0, fait: true));
            db.Anomalies.AddRange(
                AnomalieDe(vacation, "Fuite sous l'escalier"),
                AnomalieDe(autre, "Porte cassée"));
            await db.SaveChangesAsync();
        });

        var passage = Assert.Single((await Dossier(agent)).Interventions);

        Assert.Equal(nameof(PreuvePointage.SurSite), passage.Debut!.Preuve);
        Assert.Equal(40, passage.Debut.DistanceMetres);
        Assert.Equal(nameof(PreuvePointage.HorsRayon), passage.Fin!.Preuve);
        Assert.Equal(900, passage.Fin.DistanceMetres);

        // Dans l'ordre de la checklist, coché ou non : un point non fait est aussi une
        // déclaration de l'agent.
        Assert.Equal(["Hall", "Vitres"], passage.Checklist.Select(c => c.Libelle));
        Assert.Equal([false, true], passage.Checklist.Select(c => c.Fait));

        var anomalie = Assert.Single(passage.Anomalies);
        Assert.Equal("Fuite sous l'escalier", anomalie.Description);
        Assert.Equal(nameof(StatutAnomalie.Traitee), anomalie.Statut);
        Assert.Equal(2, anomalie.Photos);
    }

    /// <summary>
    /// ⚠️ Une photo peut montrer un tiers, et qui a traité l'anomalie est un collègue :
    /// l'article 15(4) écarte les deux. Vérifié sur le <b>corps brut</b>, comme l'empreinte
    /// du mot de passe : une propriété ajoutée demain n'apparaîtrait dans aucune
    /// assertion typée.
    /// </summary>
    [Fact]
    public async Task Le_dossier_compte_les_photos_sans_les_livrer_ni_nommer_qui_a_traite()
    {
        var agent = await CreerAgentRenseigne();
        var vacation = await ChaineAvecVacation(agent);
        var anomalie = AnomalieDe(vacation, "Tache au plafond");

        await AvecDbAsync(async db =>
        {
            db.Anomalies.Add(anomalie);
            await db.SaveChangesAsync();
        });

        var reponse = await _client.GetAsync($"/api/agents/{agent}/dossier");
        var brut = await reponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.DoesNotContain(Convert.ToBase64String(ContenuPhoto), brut);
        // Des noms de propriété, entre guillemets : `NonInclus` dit en toutes lettres que
        // « le contenu des photos » est retenu, et le mot nu s'y trouverait.
        Assert.DoesNotContain("\"contenu\"", brut, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(anomalie.TraiteeParId!.Value.ToString(), brut);
        Assert.DoesNotContain("\"traiteeParId\"", brut, StringComparison.OrdinalIgnoreCase);

        // Et la retenue est dite, sinon elle ressemble à un oubli.
        var dossier = (await reponse.Content.ReadFromJsonAsync<DossierAgentDto>())!;
        Assert.Contains(dossier.NonInclus, n => n.Contains("photos d'anomalie"));
        Assert.Contains(dossier.NonInclus, n => n.Contains("jamais été conservée"));
    }

    /// <summary>
    /// Être désigné titulaire d'une prestation est une donnée le concernant : c'est ce
    /// que la génération lui affectera (SPEC.md, étape 20). Seules les siennes, et
    /// nommées par leur site comme ses vacations.
    /// </summary>
    [Fact]
    public async Task Le_dossier_porte_les_prestations_dont_l_agent_est_titulaire()
    {
        var agent = await CreerAgentRenseigne();
        var collegue = await CreerAgentRenseigne("Keita", "Awa");
        var contrat = await ContratDuDossier();

        await Creer<PrestationDto>("/api/prestations", new
        {
            ContratId = contrat,
            Libelle = "Vitrerie",
            DureeEstimeeMinutes = 60,
            Jours = new[] { "Jeudi", "Lundi" },
            HeureDebut = "07:30:00",
            AgentTitulaireId = agent
        });
        await Creer<PrestationDto>("/api/prestations", new
        {
            ContratId = contrat,
            Libelle = "Sols",
            DureeEstimeeMinutes = 60,
            Jours = new[] { "Mardi" },
            HeureDebut = "07:30:00",
            AgentTitulaireId = collegue
        });

        var prestation = Assert.Single((await Dossier(agent)).PrestationsTitulaire);

        Assert.Equal("Vitrerie", prestation.Libelle);
        Assert.Equal("Résidence Dossier", prestation.Site);
        Assert.Equal("Paris", prestation.Ville);
        Assert.Equal(["Lundi", "Jeudi"], prestation.Jours);
        Assert.Equal(new TimeSpan(7, 30, 0), prestation.HeureDebut);
    }

    [Fact]
    public async Task Le_dossier_porte_le_score_attribue_a_l_agent()
    {
        var agent = await CreerAgentRenseigne();
        var absent = await CreerAgentRenseigne("Martin", "Paul");
        var intervention = await ChaineAvecVacation(absent);

        // Ouvrir un remplacement sur la vacation, puis proposer notre agent.
        var remplacement = await Creer<RemplacementDto>(
            $"/api/interventions/{intervention}/remplacement", new { });
        await _client.PostAsJsonAsync($"/api/remplacements/{remplacement.Id}/proposer",
            new ProposerDto { AgentId = agent });

        var dossier = await Dossier(agent);

        var propose = Assert.Single(dossier.Remplacements);

        // ⚠️ Communiqué exprès. Le score est une **évaluation** de la personne — un des
        // trois critères qui rendent l'AIPD probable — et la lui cacher dans sa propre
        // réponse d'accès serait précisément ce que l'article 15 interdit.
        Assert.NotNull(propose.Score);
        Assert.InRange(propose.Score!.Value, 0, 100);
        Assert.Equal("Propose", propose.Statut);
    }

    [Fact]
    public async Task Le_dossier_porte_le_compte_de_connexion_rattache()
    {
        var agent = await CreerAgentRenseigne();
        await _fixture.ClientDeRoleAsync(_client, RolesKlean.Agent, "dossier.agent@test.local",
            agentId: agent);

        var dossier = await Dossier(agent);

        Assert.NotNull(dossier.Compte);
        Assert.Equal("dossier.agent@test.local", dossier.Compte.Email);
        Assert.Contains(RolesKlean.Agent, dossier.Compte.Roles);
        Assert.True(dossier.Compte.Actif);
    }

    [Fact]
    public async Task Un_agent_sans_compte_rend_un_dossier_sans_compte()
    {
        // Le cas le plus fréquent : la plupart des agents n'ont aucun accès au produit.
        // Un dossier qui échouerait ici serait inutilisable en pratique.
        var dossier = await Dossier(await CreerAgentRenseigne());

        Assert.Null(dossier.Compte);
        Assert.Empty(dossier.Absences);
        Assert.Empty(dossier.Interventions);
    }

    // ------------------------------------------------------------------ les bornes

    [Fact]
    public async Task Le_dossier_ne_contient_aucune_empreinte_de_mot_de_passe()
    {
        var agent = await CreerAgentRenseigne();
        await _fixture.ClientDeRoleAsync(_client, RolesKlean.Agent, "dossier.secret@test.local",
            agentId: agent);

        var empreinte = await EmpreinteDuMotDePasse("dossier.secret@test.local");
        Assert.False(string.IsNullOrEmpty(empreinte), "Le compte de test n'a pas de mot de passe.");

        // ⚠️ Sur le corps brut, et pas sur le DTO : une propriété ajoutée demain à
        // `CompteDuDossierDto` n'apparaîtrait dans aucune assertion typée, alors qu'elle
        // partirait bien dans la réponse. Une empreinte n'est pas communicable au titre
        // de l'article 15 — elle n'apporte rien à la personne et sa divulgation
        // affaiblirait le secret qu'elle protège.
        var brut = await (await _client.GetAsync($"/api/agents/{agent}/dossier"))
            .Content.ReadAsStringAsync();

        Assert.DoesNotContain(empreinte, brut);
        Assert.DoesNotContain("passwordHash", brut, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", brut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Les_acces_a_la_fiche_sont_comptes_sans_nommer_qui_a_lu()
    {
        var agent = await CreerAgentRenseigne();

        // Deux lectures de la fiche par l'administrateur : le journal en garde trace.
        await _client.GetAsync($"/api/agents/{agent}");
        await _client.GetAsync($"/api/agents/{agent}");

        var dossier = await Dossier(agent);

        var lectures = dossier.AccesALaFiche.Single(a => a.Nature == nameof(NatureAcces.Lecture));
        Assert.Equal(2, lectures.Acces);
        Assert.True(lectures.Dernier >= lectures.Premier);

        // ⚠️ Le cœur de ce test : l'identifiant du lecteur ne doit pas sortir. Les lignes
        // du journal nomment des collègues, et l'article 15(4) écarte l'atteinte aux
        // droits d'autrui. Le nombre dit que la traçabilité existe ; les identités
        // appartiennent à la direction, par GET /api/journal-acces.
        var administrateur = await IdentifiantDuCompte(FixtureApi.AdminAlpha);
        var brut = await (await _client.GetAsync($"/api/agents/{agent}/dossier"))
            .Content.ReadAsStringAsync();

        Assert.DoesNotContain(administrateur.ToString(), brut);
        Assert.DoesNotContain("utilisateurId", brut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Un_agent_d_une_autre_entreprise_rend_404()
    {
        var agent = await CreerAgentRenseigne();

        var intrus = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);

        var reponse = await intrus.GetAsync($"/api/agents/{agent}/dossier");
        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }

    [Fact]
    public async Task Un_compte_d_une_autre_entreprise_portant_le_meme_agentId_n_est_pas_joint()
    {
        var agent = await CreerAgentRenseigne();

        // ⚠️ Construit directement en base, sous les validations : `POST
        // /api/auth/comptes` vérifie que l'AgentId appartient au tenant de l'appelant,
        // donc ce compte n'est pas créable par l'API. Le filtre manuel sur EntrepriseId
        // dans le contrôleur est une défense en profondeur — et une défense qu'aucun
        // test n'exerce est une ligne qu'on peut retirer sans rien casser.
        await _fixture.AvecServicesAsync(async services =>
        {
            var utilisateurs = services
                .GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Utilisateur>>();

            var beta = await utilisateurs.FindByEmailAsync(FixtureApi.AdminBeta);

            var intrus = new Utilisateur
            {
                UserName = "intrus.dossier@test.local",
                Email = "intrus.dossier@test.local",
                EmailConfirmed = true,
                EntrepriseId = beta!.EntrepriseId,
                AgentId = agent,
                Nom = "Intrus",
                Prenom = "Beta",
                DateCreation = DateTime.UtcNow,
                Actif = true
            };

            await utilisateurs.CreateAsync(intrus, FixtureApi.MotDePasse);
        });

        var dossier = await Dossier(agent);

        Assert.Null(dossier.Compte);
    }

    [Fact]
    public async Task Le_dossier_nomme_le_chef_d_equipe_plutot_qu_un_identifiant()
    {
        var chef = await _fixture.ClientDeRoleAsync(_client, RolesKlean.ChefEquipe, "chef.dossier@test.local");
        var chefId = (await chef.GetFromJsonAsync<ProfilDto>("/api/auth/moi"))!.Id;

        var agent = await Creer<AgentDto>("/api/agents", new AgentCreateDto
        {
            Nom = "Diallo", Prenom = "Aminata", DateEmbauche = new DateTime(2024, 3, 15), ChefEquipeId = chefId
        });

        var dossier = await Dossier(agent.Id);

        // `ClientDeRoleAsync` pose le rôle comme nom et laisse le prénom vide.
        Assert.Equal(RolesKlean.ChefEquipe, dossier.ChefEquipe);
    }

    [Fact]
    public async Task Le_chef_d_une_autre_entreprise_n_est_pas_nomme_dans_le_dossier()
    {
        var agent = await CreerAgentRenseigne();

        // ⚠️ Rattachement posé directement en base, sous la validation : l'API refuse un
        // chef d'une autre entreprise. La recherche du nom, sur AspNetUsers non filtrée,
        // est une défense en profondeur, et ce test est le seul qui l'exerce.
        await _fixture.AvecServicesAsync(async services =>
        {
            var utilisateurs = services
                .GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Utilisateur>>();
            var beta = await utilisateurs.FindByEmailAsync(FixtureApi.AdminBeta);

            var db = services.GetRequiredService<Klean.Api.Data.KleanDbContext>();
            var ligne = await db.Agents.FindAsync(agent);
            ligne!.ChefEquipeId = beta!.Id;
            await db.SaveChangesAsync();
        });

        var dossier = await Dossier(agent);

        Assert.Null(dossier.ChefEquipe);
        Assert.DoesNotContain("Beta Nettoyage", await _client.GetStringAsync($"/api/agents/{agent}/dossier"));
    }

    [Fact]
    public async Task Le_dossier_annonce_ce_qu_il_ne_contient_pas()
    {
        var dossier = await Dossier(await CreerAgentRenseigne());

        // Même raison d'être que `Subsiste` sur la pseudonymisation : qui répond à une
        // demande d'accès doit savoir ce qu'il transmet et ce qu'il retient, sans relire
        // le code. Quatre mentions au moins, dont celles qui expliquent les deux bornes
        // ci-dessus.
        Assert.True(dossier.NonInclus.Count >= 4, "Les mentions d'exclusion ont disparu.");
        Assert.Contains(dossier.NonInclus, m => m.Contains("mot de passe"));
        Assert.Contains(dossier.NonInclus, m => m.Contains("15(4)"));
        Assert.Contains(dossier.NonInclus, m => m.Contains("adresses IP"));
    }

    [Fact]
    public async Task Le_dossier_d_un_agent_pseudonymise_dit_qu_il_l_a_ete_et_quand()
    {
        var agent = await CreerAgentRenseigne();
        await _client.PostAsync($"/api/agents/{agent}/anonymiser", null);

        var dossier = await Dossier(agent);

        // Sans cette mention, le dossier se lirait comme une fiche mal saisie : un nom
        // « Agent anonymisé », pas d'adresse, pas de coordonnées. Il faut dire que les
        // données ont été effacées, et quand.
        Assert.NotNull(dossier.Agent.DateAnonymisation);
        Assert.Contains(dossier.NonInclus, m => m.Contains("pseudonymisé le"));
        Assert.Contains(dossier.NonInclus, m => m.Contains("4(5)"));
    }

    // ------------------------------------------------------------------- les droits

    [Fact]
    public async Task Le_dossier_est_reserve_a_l_administrateur()
    {
        var agent = await CreerAgentRenseigne();

        var responsable = await _fixture.ClientDeRoleAsync(
            _client, RolesKlean.Responsable, "dossier.responsable@test.local");

        // Un responsable lit la fiche d'un agent — c'est son métier — et ne sort pas son
        // dossier complet. Ce sont deux gestes différents, et le second est une réponse
        // à une demande légale, qui appartient à la direction.
        Assert.Equal(HttpStatusCode.OK,
            (await responsable.GetAsync($"/api/agents/{agent}")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await responsable.GetAsync($"/api/agents/{agent}/dossier")).StatusCode);
    }

    [Fact]
    public async Task Sortir_un_dossier_est_journalise_en_Export()
    {
        var agent = await CreerAgentRenseigne();

        await Dossier(agent);

        var journal = await _client.PageAsync<JournalAccesDto>("/api/journal-acces?taille=200");

        var ligne = Assert.Single(journal.Elements,
            l => l.Nature == nameof(NatureAcces.Export));

        Assert.Equal(nameof(RessourceAccedee.Agent), ligne.Ressource);
        Assert.Equal(agent, ligne.CibleId);

        // Un dossier est une ligne, pas une liste : `Compter` ne trouve ni `Elements` ni
        // énumérable, et rend 1.
        Assert.Equal(1, ligne.Lignes);
    }

    [Fact]
    public async Task Un_agent_inconnu_ne_laisse_aucune_ligne_au_journal()
    {
        var reponse = await _client.GetAsync($"/api/agents/{Guid.NewGuid()}/dossier");
        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);

        var journal = await _client.PageAsync<JournalAccesDto>("/api/journal-acces?taille=200");

        // Règle 5 du §7 bis : seules les divulgations sont consignées. Rien n'est sorti.
        Assert.DoesNotContain(journal.Elements, l => l.Nature == nameof(NatureAcces.Export));
    }

    // ------------------------------------------------------------------------ outils

    /// <summary>Des octets reconnaissables : il suffit de les chercher dans le corps.</summary>
    private static readonly byte[] ContenuPhoto = Encoding.ASCII.GetBytes("PHOTO-QUI-NE-SORT-PAS-DU-DOSSIER");

    private Pointage Pointage(
        Guid vacation, MomentPointage moment, PreuvePointage preuve, int distance, int precision) => new()
    {
        EntrepriseId = _fixture.EntrepriseAlpha,
        InterventionId = vacation,
        Moment = moment,
        Horodatage = DateTime.UtcNow,
        Preuve = preuve,
        DistanceMetres = distance,
        PrecisionMetres = precision,
        AuteurId = Guid.NewGuid()
    };

    private ControleEffectue Controle(Guid vacation, string libelle, int ordre, bool fait) => new()
    {
        EntrepriseId = _fixture.EntrepriseAlpha,
        InterventionId = vacation,
        Libelle = libelle,
        Ordre = ordre,
        Fait = fait
    };

    /// <summary>Une anomalie traitée par un collègue, avec deux photos.</summary>
    private Anomalie AnomalieDe(Guid vacation, string description) => new()
    {
        EntrepriseId = _fixture.EntrepriseAlpha,
        InterventionId = vacation,
        Description = description,
        Statut = StatutAnomalie.Traitee,
        DateSignalement = DateTime.UtcNow.AddHours(-2),
        AuteurId = Guid.NewGuid(),
        DateTraitement = DateTime.UtcNow,
        TraiteeParId = Guid.NewGuid(),
        Photos =
        [
            new PhotoAnomalie
            {
                EntrepriseId = _fixture.EntrepriseAlpha,
                Contenu = ContenuPhoto,
                Taille = ContenuPhoto.Length,
                DateAjout = DateTime.UtcNow
            },
            new PhotoAnomalie
            {
                EntrepriseId = _fixture.EntrepriseAlpha,
                Contenu = ContenuPhoto,
                Taille = ContenuPhoto.Length,
                DateAjout = DateTime.UtcNow
            }
        ]
    };

    /// <summary>Sous l'API : aucune route n'écrit un pointage daté librement, ni une anomalie traitée d'avance.</summary>
    private Task AvecDbAsync(Func<KleanDbContext, Task> action) =>
        _fixture.AvecServicesAsync(services => action(services.GetRequiredService<KleanDbContext>()));

    private async Task<DossierAgentDto> Dossier(Guid agent)
    {
        var reponse = await _client.GetAsync($"/api/agents/{agent}/dossier");

        if (!reponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GET /api/agents/{agent}/dossier → {(int)reponse.StatusCode} : "
                + await reponse.Content.ReadAsStringAsync());
        }

        return (await reponse.Content.ReadFromJsonAsync<DossierAgentDto>())!;
    }

    private async Task<string?> EmpreinteDuMotDePasse(string email)
    {
        string? empreinte = null;

        await _fixture.AvecServicesAsync(async services =>
        {
            var utilisateurs = services
                .GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Utilisateur>>();

            empreinte = (await utilisateurs.FindByEmailAsync(email))?.PasswordHash;
        });

        return empreinte;
    }

    private async Task<Guid> IdentifiantDuCompte(string email)
    {
        var identifiant = Guid.Empty;

        await _fixture.AvecServicesAsync(async services =>
        {
            var utilisateurs = services
                .GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Utilisateur>>();

            identifiant = (await utilisateurs.FindByEmailAsync(email))!.Id;
        });

        return identifiant;
    }

    private async Task<Guid> CreerAgentRenseigne(string nom = "Diallo", string prenom = "Aminata")
    {
        var cree = await Creer<AgentDto>("/api/agents", new AgentCreateDto
        {
            Nom = nom,
            Prenom = prenom,
            Email = $"{prenom.ToLowerInvariant()}@test.local",
            Telephone = "0612345678",
            Adresse = "14 rue des Lilas, Paris",
            Latitude = 48.8566,
            Longitude = 2.3522,
            DateEmbauche = new DateTime(2024, 3, 15)
        });

        return cree.Id;
    }

    private async Task CreerAbsence(
        Guid agent, string type, string? motif, DateTime debut, DateTime fin)
    {
        await Creer<AbsenceDto>("/api/absences", new AbsenceCreateDto
        {
            AgentId = agent,
            Type = type,
            Motif = motif,
            DateDebut = debut,
            DateFin = fin
        });
    }

    private async Task<Guid> ChaineAvecVacation(Guid? agent = null)
    {
        var prestation = await Creer<PrestationDto>("/api/prestations", new PrestationCreateDto
        {
            ContratId = await ContratDuDossier(),
            Libelle = "Nettoyage parties communes",
            DureeEstimeeMinutes = 120
        });
        var intervention = await Creer<InterventionDto>("/api/interventions", new InterventionCreateDto
        {
            PrestationId = prestation.Id,
            AgentId = agent,
            DatePrevue = new DateTime(2026, 9, 7),
            HeurePrevue = new TimeSpan(8, 30, 0),
            Statut = "Planifiee"
        });

        return intervention.Id;
    }

    private async Task<Guid> ContratDuDossier()
    {
        var client = await Creer<ClientDto>("/api/clients",
            new ClientCreateDto { Nom = "Syndic Dossier", Type = "Syndic" });
        var site = await Creer<SiteDto>("/api/sites", new SiteCreateDto
        {
            ClientId = client.Id,
            Nom = "Résidence Dossier",
            Adresse = "1 rue du Dossier",
            Ville = "Paris",
            Latitude = 48.8421,
            Longitude = 2.3219
        });
        var contrat = await Creer<ContratDto>("/api/contrats", new ContratCreateDto
        {
            SiteId = site.Id,
            DateDebut = new DateTime(2026, 1, 1),
            Frequence = "Lundi"
        });

        return contrat.Id;
    }

    private async Task<T> Creer<T>(string route, object corps)
    {
        var reponse = await _client.PostAsJsonAsync(route, corps);

        if (!reponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"POST {route} → {(int)reponse.StatusCode} : "
                + await reponse.Content.ReadAsStringAsync());
        }

        return (await reponse.Content.ReadFromJsonAsync<T>())!;
    }
}
