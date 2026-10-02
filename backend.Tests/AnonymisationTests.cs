using System.Net;
using System.Net.Http.Json;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Klean.Api.Services;
using Klean.Api.Data;
using Klean.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Klean.Api.Tests;

/// <summary>
/// Pseudonymisation d'un agent (SPEC.md étape 17, lot B).
///
/// <b>Ce que l'opération résout.</b> <c>Restrict</c> sur <c>Intervention.AgentId</c> rend
/// un agent impossible à supprimer dès qu'il a travaillé — le bon choix pour l'intégrité
/// du planning, et incompatible avec une demande d'effacement. Remplacer les données
/// identifiantes en conservant la ligne satisfait les deux.
///
/// ⚠️ <b>Ce qu'elle ne résout pas, et que ces tests affirment aussi.</b> L'identifiant
/// survit, donc c'est une pseudonymisation au sens de l'article 4(5), pas une
/// anonymisation. Le compte rendu l'énumère, et un test le vérifie : une opération qui
/// n'annoncerait que des succès laisserait promettre un effacement total.
/// </summary>
[Collection(CollectionApi.Nom)]
public class AnonymisationTests : IAsyncLifetime
{
    private readonly FixtureApi _fixture;
    private HttpClient _client = null!;

    public AnonymisationTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------------------------------------------------------------------
    // L'agent
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Les_donnees_identifiantes_de_l_agent_sont_remplacees()
    {
        var agent = await CreerAgentRenseigne();

        var compteRendu = await Anonymiser(agent);
        var relu = await _client.GetFromJsonAsync<AgentDto>($"/api/agents/{agent}");

        Assert.NotNull(relu);
        Assert.Equal(ServiceAnonymisation.Pseudonyme, relu.Nom);
        Assert.Null(relu.Email);
        Assert.Null(relu.Telephone);
        Assert.Null(relu.Adresse);
        Assert.NotNull(relu.DateAnonymisation);

        // Aucune trace du nom d'origine, nulle part dans la ligne rendue.
        Assert.DoesNotContain("Diallo", relu.Nom);
        Assert.DoesNotContain("Aminata", relu.Prenom);
        Assert.Equal(compteRendu.Pseudonyme, $"{relu.Nom} {relu.Prenom}");
    }

    [Fact]
    public async Task Les_coordonnees_passent_a_null_et_non_a_zero()
    {
        // ⚠️ LE test de ce fichier côté données. Une latitude de zéro passe le
        // [Range(-90, 90)], ne lève aucune erreur, place l'agent dans le golfe de Guinée
        // à 600 km au sud d'Accra, et fausse silencieusement toutes les notes de distance
        // du moteur de remplacement. `null` donne la note neutre de 50, qui est la réponse
        // honnête à « on ne sait plus où il habite » (CLAUDE.md §8).
        var agent = await CreerAgentRenseigne();

        await Anonymiser(agent);
        var relu = await _client.GetFromJsonAsync<AgentDto>($"/api/agents/{agent}");

        Assert.Null(relu!.Latitude);
        Assert.Null(relu.Longitude);
    }

    [Fact]
    public async Task L_agent_anonymise_est_ecarte_des_candidats()
    {
        var agent = await CreerAgentRenseigne();
        var intervention = await ChaineAvecVacation();

        var avant = await Candidats(intervention);
        Assert.Contains(avant, c => c.AgentId == agent);

        await Anonymiser(agent);

        // `Actif = false` est un filtre dur : l'agent n'apparaît plus du tout, et ne
        // revient pas avec un score nul (CLAUDE.md §8).
        Assert.DoesNotContain(await Candidats(intervention), c => c.AgentId == agent);
    }

    [Fact]
    public async Task L_agent_anonymise_reste_dans_la_liste_avec_sa_date()
    {
        // Il ne disparaît pas : la ligne est conservée, c'est tout l'objet de l'opération.
        // Et la date permet à l'écran de ne pas présenter « Agent anonymisé » comme un
        // agent ordinaire dont quelqu'un aurait mal saisi le nom.
        var agent = await CreerAgentRenseigne();
        await Anonymiser(agent);

        var page = await _client.PageAsync<AgentDto>("/api/agents");
        var ligne = page.Elements.Single(a => a.Id == agent);

        Assert.NotNull(ligne.DateAnonymisation);
        Assert.False(ligne.Actif);
    }

    [Fact]
    public async Task Deux_agents_anonymises_restent_distinguables()
    {
        // Sans le suffixe, une entreprise qui a pseudonymisé trois agents lit trois lignes
        // identiques dans son planning et ne peut plus suivre trois historiques distincts.
        // Le suffixe ne divulgue rien : `InterventionDto.AgentId` expose déjà
        // l'identifiant entier, qui n'a jamais été secret.
        var premier = await CreerAgentRenseigne();
        var second = await CreerAgentRenseigne("Nguyen", "Linh");

        var unCompteRendu = await Anonymiser(premier);
        var autreCompteRendu = await Anonymiser(second);

        Assert.NotEqual(unCompteRendu.Pseudonyme, autreCompteRendu.Pseudonyme);
        Assert.StartsWith(ServiceAnonymisation.Pseudonyme, unCompteRendu.Pseudonyme);
        Assert.StartsWith(ServiceAnonymisation.Pseudonyme, autreCompteRendu.Pseudonyme);
    }

    // ---------------------------------------------------------------------------
    // L'historique conservé
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Les_interventions_sont_conservees_et_restent_rattachees()
    {
        // Le cœur du compromis : elles sont la preuve de passages facturés. Les supprimer
        // pour satisfaire un effacement détruirait l'historique du planning.
        var agent = await CreerAgentRenseigne();
        var intervention = await ChaineAvecVacation(agent);

        var compteRendu = await Anonymiser(agent);

        Assert.Equal(1, compteRendu.InterventionsConservees);

        var relue = await _client.GetFromJsonAsync<InterventionDto>($"/api/interventions/{intervention}");
        Assert.Equal(agent, relue!.AgentId);
    }

    [Fact]
    public async Task Les_motifs_d_absence_sont_effaces_mais_les_absences_demeurent()
    {
        var agent = await CreerAgentRenseigne();
        var motivee = await CreerAbsence(agent, "Conge", "Congé posé en octobre",
            new DateTime(2026, 12, 21), new DateTime(2026, 12, 26));
        var maladie = await CreerAbsence(agent, "Maladie", null,
            new DateTime(2026, 11, 2), new DateTime(2026, 11, 4));

        var compteRendu = await Anonymiser(agent);

        Assert.Equal(2, compteRendu.AbsencesConservees);
        // Une seule portait un motif : l'arrêt maladie n'en a jamais eu le droit.
        Assert.Equal(1, compteRendu.MotifsEffaces);

        Assert.Null((await _client.GetFromJsonAsync<AbsenceDto>($"/api/absences/{motivee}"))!.Motif);
        Assert.NotNull(await _client.GetFromJsonAsync<AbsenceDto>($"/api/absences/{maladie}"));
    }

    // ---------------------------------------------------------------------------
    // Le compte de connexion
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Le_compte_lie_ne_peut_plus_se_connecter()
    {
        // ⚠️ Sans cette moitié, l'« effacement » laisse le nom et l'adresse de courriel de
        // la personne dans AspNetUsers — et lui laisse son accès.
        var agent = await CreerAgentRenseigne();
        const string courriel = "aminata.agent@test.local";
        var compte = await _fixture.ClientDeRoleAsync(
            _client, RolesKlean.Agent, courriel, agentId: agent);

        var compteRendu = await Anonymiser(agent);
        Assert.True(compteRendu.CompteNeutralise);

        var anonyme = _fixture.ClientAnonyme();
        var connexion = await anonyme.PostAsJsonAsync("/api/auth/connexion",
            new ConnexionDto { Email = courriel, MotDePasse = FixtureApi.MotDePasse });

        Assert.Equal(HttpStatusCode.Unauthorized, connexion.StatusCode);
    }

    [Fact]
    public async Task Les_sessions_en_cours_du_compte_lie_sont_coupees()
    {
        var agent = await CreerAgentRenseigne();
        var sien = await _fixture.ClientDeRoleAsync(
            _client, RolesKlean.Agent, "linh.agent@test.local", agentId: agent);

        // Il a une session valide avant.
        (await sien.GetAsync("/api/auth/moi")).EnsureSuccessStatusCode();

        var compteRendu = await Anonymiser(agent);
        Assert.True(compteRendu.SessionsRevoquees >= 1);

        // ⚠️ Les deux assertions, et elles ne disent pas la même chose. Révoquer un jeton
        // de rafraîchissement ne périme pas un jeton d'accès, qui est signé et vaut quinze
        // minutes (CLAUDE.md §10) : si seul `rafraichir` échouait, l'agent garderait un
        // quart d'heure d'accès à ses propres données. C'est le changement d'empreinte de
        // sécurité qui tue le jeton d'accès, parce qu'elle est vérifiée à chaque requête.
        Assert.Equal(HttpStatusCode.Unauthorized, (await sien.GetAsync("/api/auth/moi")).StatusCode);

        var rafraichissement = await sien.PostAsync("/api/auth/rafraichir", null);
        Assert.Equal(HttpStatusCode.Unauthorized, rafraichissement.StatusCode);
    }

    [Fact]
    public async Task L_identite_du_compte_lie_est_remplacee_en_base()
    {
        // ⚠️ Ce test existe parce qu'un sabotage est resté vert sans lui. `Le_compte_lie_
        // ne_peut_plus_se_connecter` échoue déjà par la seule substitution de l'adresse :
        // il ne dit donc rien du nom ni du prénom, qui pouvaient survivre dans
        // `AspNetUsers` sans que rien ne le signale. Un effacement qui laisse « Aminata
        // Diallo » dans la table des comptes n'est pas un effacement.
        //
        // Lu en base et non par l'API : le compte ne peut plus se connecter, et aucun
        // endpoint ne liste les comptes (CLAUDE.md §7).
        var agent = await CreerAgentRenseigne();
        await _fixture.ClientDeRoleAsync(
            _client, RolesKlean.Agent, "aminata.identite@test.local", agentId: agent);

        // ⚠️ Un numéro posé en base avant l'opération, et ce n'est pas du décor :
        // `ClientDeRoleAsync` crée le compte sans téléphone, donc une assertion
        // `Assert.Null(PhoneNumber)` passait quoi qu'il arrive — le sabotage qui retire
        // l'effacement du téléphone est resté vert pour cette seule raison. Une assertion
        // sur un champ déjà vide ne vérifie rien.
        await _fixture.AvecServicesAsync(async services =>
        {
            var db = services.GetRequiredService<KleanDbContext>();
            var compte = await db.Users.FirstAsync(u => u.AgentId == agent);

            compte.PhoneNumber = "0612345678";
            compte.PhoneNumberConfirmed = true;

            await db.SaveChangesAsync();
        });

        var avant = await Compte(agent);
        Assert.Equal("Agent", avant!.Nom);
        Assert.Equal("0612345678", avant.PhoneNumber);
        var empreinteAvant = avant.SecurityStamp;

        await Anonymiser(agent);

        var apres = await Compte(agent);

        Assert.NotNull(apres);
        Assert.Equal(ServiceAnonymisation.Pseudonyme, apres.Nom);
        Assert.Equal(string.Empty, apres.Prenom);
        Assert.Null(apres.PhoneNumber);
        Assert.False(apres.PhoneNumberConfirmed);
        Assert.False(apres.EmailConfirmed);
        Assert.DoesNotContain("aminata.identite", apres.Email!);
        Assert.DoesNotContain("aminata.identite", apres.UserName!);

        // ⚠️ Les deux gardes, assertées séparément parce qu'elles se couvrent l'une
        // l'autre dans `OnTokenValidated` : il refuse une requête dont le compte est
        // inactif **ou** dont l'empreinte a changé. Retirer l'une des deux laissait donc
        // la série verte, et c'est exactement ce qu'un sabotage a montré.
        Assert.False(apres.Actif);
        Assert.NotEqual(empreinteAvant, apres.SecurityStamp);
    }

    [Fact]
    public async Task Un_compte_d_une_autre_entreprise_portant_le_meme_agentId_n_est_pas_touche()
    {
        // ⚠️ Le scénario n'est pas atteignable par l'API : `POST /api/auth/comptes` vérifie
        // que l'AgentId appartient à l'entreprise de l'appelant. Le filtre sur
        // `EntrepriseId` dans le service est donc une défense en profondeur — et une
        // défense en profondeur qu'aucun test n'exerce est une ligne qu'on peut retirer
        // sans rien casser, ce qu'un sabotage a confirmé.
        //
        // Il est donc construit en base, sous les validations, pour éprouver la garde
        // elle-même. `AspNetUsers` n'étant pas filtrée par le multi-tenant (CLAUDE.md §7),
        // c'est la seule chose qui sépare les deux entreprises ici.
        var agent = await CreerAgentRenseigne();

        var intrus = Guid.NewGuid();
        await _fixture.AvecServicesAsync(async services =>
        {
            var db = services.GetRequiredService<KleanDbContext>();

            db.Users.Add(new Utilisateur
            {
                Id = intrus,
                UserName = "intrus.beta@test.local",
                NormalizedUserName = "INTRUS.BETA@TEST.LOCAL",
                Email = "intrus.beta@test.local",
                NormalizedEmail = "INTRUS.BETA@TEST.LOCAL",
                EmailConfirmed = true,
                EntrepriseId = _fixture.EntrepriseBeta,
                // Le même AgentId, mais dans l'autre entreprise.
                AgentId = agent,
                Nom = "Intrus",
                Prenom = "Beta",
                Actif = true,
                SecurityStamp = Guid.NewGuid().ToString()
            });

            await db.SaveChangesAsync();
        });

        await Anonymiser(agent);

        await _fixture.AvecServicesAsync(async services =>
        {
            var db = services.GetRequiredService<KleanDbContext>();
            var reste = await db.Users.AsNoTracking().FirstAsync(u => u.Id == intrus);

            Assert.Equal("Intrus", reste.Nom);
            Assert.Equal("intrus.beta@test.local", reste.Email);
            Assert.True(reste.Actif);
        });
    }

    [Fact]
    public async Task Un_agent_sans_compte_est_anonymise_sans_erreur()
    {
        // Le cas courant : la plupart des agents n'ont pas de compte, l'étape 8 ayant
        // sécurisé ce rôle sans le servir.
        var agent = await CreerAgentRenseigne();

        var compteRendu = await Anonymiser(agent);

        Assert.False(compteRendu.CompteNeutralise);
        Assert.Equal(0, compteRendu.SessionsRevoquees);
    }

    // ---------------------------------------------------------------------------
    // Les refus
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Une_seconde_anonymisation_est_refusee_en_disant_la_date()
    {
        // ⚠️ Refus et non réussite silencieuse : l'opération est irréversible, et dire
        // « déjà fait » à qui s'est trompé d'identifiant vaut mieux que de lui répondre
        // « c'est bon » et de le laisser croire qu'il a traité le bon agent.
        var agent = await CreerAgentRenseigne();
        await Anonymiser(agent);

        var seconde = await _client.PostAsync($"/api/agents/{agent}/anonymiser", null);

        Assert.Equal(HttpStatusCode.Conflict, seconde.StatusCode);

        var detail = await seconde.Content.ReadAsStringAsync();
        Assert.Contains("déjà été anonymisé", detail);
        Assert.Contains(DateTime.UtcNow.ToString("dd/MM/yyyy"), detail);
    }

    [Fact]
    public async Task Seul_l_administrateur_anonymise()
    {
        // Pas de Responsable : ce n'est pas un geste d'exploitation, c'est une réponse à
        // l'exercice d'un droit, et rien ne la défait.
        var agent = await CreerAgentRenseigne();
        var responsable = await _fixture.ClientDeRoleAsync(
            _client, RolesKlean.Responsable, "responsable.anonymisation@test.local");

        var refus = await responsable.PostAsync($"/api/agents/{agent}/anonymiser", null);

        Assert.Equal(HttpStatusCode.Forbidden, refus.StatusCode);

        // Et rien n'a bougé.
        var relu = await _client.GetFromJsonAsync<AgentDto>($"/api/agents/{agent}");
        Assert.Null(relu!.DateAnonymisation);
        Assert.Equal("Diallo", relu.Nom);
    }

    [Fact]
    public async Task Un_agent_d_une_autre_entreprise_rend_404()
    {
        // Les filtres globaux l'écartent avant le service : l'agent n'existe pas pour
        // l'appelant, et la réponse ne doit pas trahir le contraire.
        var agent = await CreerAgentRenseigne();

        var beta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);
        var tentative = await beta.PostAsync($"/api/agents/{agent}/anonymiser", null);

        Assert.Equal(HttpStatusCode.NotFound, tentative.StatusCode);

        var relu = await _client.GetFromJsonAsync<AgentDto>($"/api/agents/{agent}");
        Assert.Null(relu!.DateAnonymisation);
    }

    [Fact]
    public async Task Un_agent_inexistant_rend_404()
    {
        var tentative = await _client.PostAsync($"/api/agents/{Guid.NewGuid()}/anonymiser", null);

        Assert.Equal(HttpStatusCode.NotFound, tentative.StatusCode);
    }

    // ---------------------------------------------------------------------------
    // Le compte rendu, et le journal
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Le_compte_rendu_enumere_ce_qui_subsiste()
    {
        // ⚠️ La moitié du compte rendu qui compte. Un retour qui n'annoncerait que des
        // succès laisserait promettre un effacement total à qui le demande.
        var agent = await CreerAgentRenseigne();
        await ChaineAvecVacation(agent);
        await CreerAbsence(agent, "Maladie", null,
            new DateTime(2026, 11, 2), new DateTime(2026, 11, 4));

        var compteRendu = await Anonymiser(agent);

        Assert.NotEmpty(compteRendu.Subsiste);

        var tout = string.Join(" ", compteRendu.Subsiste);

        // La qualification juridique exacte, dite et non supposée.
        Assert.Contains("pseudonymisation", tout);
        Assert.Contains("4(5)", tout);
        // L'historique conservé, et le résidu de santé.
        Assert.Contains("intervention", tout);
        Assert.Contains("Maladie", tout);
        Assert.Contains("donnée de santé", tout);
    }

    [Fact]
    public void Le_compte_rendu_ne_porte_aucune_donnee_identifiante()
    {
        // Même garde que sur le journal : il voyage, il est lu, il peut finir recopié
        // dans un dossier de réponse à une demande d'effacement.
        var interdites = new[]
        {
            "Nom", "Prenom", "Adresse", "Email", "Telephone", "Latitude", "Longitude", "Motif"
        };

        var fautives = typeof(AnonymisationDto).GetProperties()
            .Select(p => p.Name)
            .Where(interdites.Contains)
            .ToList();

        Assert.Empty(fautives);
    }

    [Fact]
    public async Task L_operation_est_journalisee_en_anonymisation()
    {
        // `NatureAcces.Anonymisation` existait dans l'énumération depuis le lot A et
        // n'était écrite par personne. Un effacement non tracé est précisément ce qu'un
        // journal RGPD existe pour empêcher.
        var agent = await CreerAgentRenseigne();

        await Anonymiser(agent);

        var journal = await _client.PageAsync<JournalAccesDto>("/api/journal-acces?page=1&taille=200");
        var ligne = journal.Elements.Single(l => l.Nature == nameof(NatureAcces.Anonymisation));

        Assert.Equal(nameof(RessourceAccedee.Agent), ligne.Ressource);
        Assert.Equal(agent, ligne.CibleId);
    }

    [Fact]
    public async Task Un_refus_d_anonymisation_ne_laisse_rien_au_journal()
    {
        // Le 409 d'une seconde tentative ne divulgue rien et ne modifie rien.
        var agent = await CreerAgentRenseigne();
        await Anonymiser(agent);

        var avant = await _client.PageAsync<JournalAccesDto>("/api/journal-acces?page=1&taille=200");
        await _client.PostAsync($"/api/agents/{agent}/anonymiser", null);
        var apres = await _client.PageAsync<JournalAccesDto>("/api/journal-acces?page=1&taille=200");

        var connues = avant.Elements.Select(l => l.Id).ToHashSet();
        var nouvelles = apres.Elements
            .Where(l => !connues.Contains(l.Id)
                && l.Ressource != nameof(RessourceAccedee.JournalAcces))
            .ToList();

        Assert.Empty(nouvelles);
    }

    // ---- Fabrication ----

    private async Task<AnonymisationDto> Anonymiser(Guid agent)
    {
        var reponse = await _client.PostAsync($"/api/agents/{agent}/anonymiser", null);

        if (!reponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Anonymisation refusée ({(int)reponse.StatusCode}) : "
                + await reponse.Content.ReadAsStringAsync());
        }

        return (await reponse.Content.ReadFromJsonAsync<AnonymisationDto>())!;
    }

    /// <summary>
    /// Le compte rattaché à un agent, lu en base.
    ///
    /// ⚠️ Aucun endpoint ne liste ni ne lit les comptes (CLAUDE.md §7), et celui-ci ne
    /// peut plus se connecter après l'opération : la base est le seul chemin.
    /// `AsNoTracking` pour relire ce qui est écrit, et non ce que le contexte a en
    /// mémoire — sans quoi le test passerait même si rien n'avait été enregistré.
    /// </summary>
    private async Task<Utilisateur?> Compte(Guid agent)
    {
        Utilisateur? trouve = null;

        await _fixture.AvecServicesAsync(async services =>
        {
            var db = services.GetRequiredService<KleanDbContext>();

            trouve = await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.AgentId == agent
                    && u.EntrepriseId == _fixture.EntrepriseAlpha);
        });

        return trouve;
    }

    private async Task<List<CandidatDto>> Candidats(Guid intervention)
    {
        return (await _client.GetFromJsonAsync<List<CandidatDto>>(
            $"/api/interventions/{intervention}/candidats"))!;
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

    private async Task<Guid> CreerAbsence(
        Guid agent, string type, string? motif, DateTime debut, DateTime fin)
    {
        var creee = await Creer<AbsenceDto>("/api/absences", new AbsenceCreateDto
        {
            AgentId = agent,
            Type = type,
            Motif = motif,
            DateDebut = debut,
            DateFin = fin
        });

        return creee.Id;
    }

    private async Task<Guid> ChaineAvecVacation(Guid? agent = null)
    {
        var client = await Creer<ClientDto>("/api/clients",
            new ClientCreateDto { Nom = "Syndic Anonymisation", Type = "Syndic" });
        var site = await Creer<SiteDto>("/api/sites", new SiteCreateDto
        {
            ClientId = client.Id,
            Nom = "Résidence Anonymisation",
            Adresse = "1 rue du Test",
            Latitude = 48.8421,
            Longitude = 2.3219
        });
        var contrat = await Creer<ContratDto>("/api/contrats", new ContratCreateDto
        {
            SiteId = site.Id,
            DateDebut = new DateTime(2026, 1, 1),
            Frequence = "Lundi"
        });
        var prestation = await Creer<PrestationDto>("/api/prestations", new PrestationCreateDto
        {
            ContratId = contrat.Id,
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
