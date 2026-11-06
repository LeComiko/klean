using System.Net;
using System.Net.Http.Json;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Klean.Api.Services;
using Klean.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Klean.Api.Tests;

/// <summary>
/// Pseudonymisation d'un compte de connexion sans fiche agent — RGPD.md §9.3.
///
/// Le trou que ce lot comble : un compte `Responsable`, `Administrateur` ou
/// `ClientSyndic` porte <c>AgentId = null</c>, donc la pseudonymisation d'agent ne le
/// touchait pas. Son nom, son prénom et son courriel restaient en base indéfiniment, et
/// il n'existait même pas de route pour le désactiver — la fixture de tests le fait
/// directement en base, faute d'endpoint.
///
/// ⚠️ Ce qui est éprouvé ici est moins l'effacement que les **cinq refus**. Sur une
/// opération irréversible, un refus mal placé fait perdre des données et un refus
/// manquant peut couper une entreprise de son dernier administrateur.
/// </summary>
[Collection(CollectionApi.Nom)]
public class AnonymisationCompteTests : IAsyncLifetime
{
    private const string Cible = "a.effacer@test.local";

    private readonly FixtureApi _fixture;
    private HttpClient _client = null!;

    public AnonymisationCompteTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------- l'effacement

    [Fact]
    public async Task L_identite_du_compte_est_remplacee_en_base()
    {
        var compte = await CreerResponsable();

        // ⚠️ Le téléphone est posé AVANT, sinon l'assertion sur sa disparition serait
        // vide : `ClientDeRoleAsync` crée les comptes sans téléphone, et
        // `Assert.Null(PhoneNumber)` passerait quoi qu'il arrive. Leçon du lot B.
        await PoserTelephone(compte, "0612345678");

        var rendu = await Anonymiser(compte);

        // ⚠️ Relecture **en base**, champ par champ, et non par l'API. Les trois gardes du
        // compte se couvrent l'une l'autre — `OnTokenValidated` refuse un compte inactif
        // OU une empreinte changée, et la connexion échoue par la seule substitution
        // d'adresse — donc un test qui se contente de « il ne peut plus se connecter »
        // laisse retirer n'importe laquelle des trois (CLAUDE.md §7 bis).
        var apres = await LireEnBase(compte);

        Assert.Equal(ServiceAnonymisation.PseudonymeCompte, apres.Nom);
        Assert.DoesNotContain("Responsable", apres.Prenom);
        Assert.Equal($"{compte}@anonymise.invalid", apres.Email);
        Assert.Equal($"{compte}@anonymise.invalid", apres.UserName);
        Assert.Equal($"{compte}@anonymise.invalid".ToUpperInvariant(), apres.NormalizedEmail);
        Assert.Null(apres.PhoneNumber);
        Assert.False(apres.PhoneNumberConfirmed);
        Assert.False(apres.EmailConfirmed);
        Assert.False(apres.Actif);
        Assert.NotNull(apres.DateAnonymisation);

        Assert.Equal(apres.Email, rendu.AdresseSubstituee);
        Assert.Equal(compte, rendu.UtilisateurId);
    }

    [Fact]
    public async Task L_adresse_d_origine_ne_subsiste_nulle_part()
    {
        var compte = await CreerResponsable();

        await Anonymiser(compte);

        var apres = await LireEnBase(compte);

        // Les quatre colonnes d'Identity, pas seulement `Email` : `UserName` et les deux
        // formes normalisées portent la même adresse, et Identity cherche sur les
        // normalisées.
        foreach (var valeur in new[] { apres.Email, apres.UserName, apres.NormalizedEmail, apres.NormalizedUserName })
        {
            Assert.DoesNotContain("effacer", valeur ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Le_compte_anonymise_ne_peut_plus_se_connecter()
    {
        var compte = await CreerResponsable();
        await Anonymiser(compte);

        var reponse = await _fixture.ClientAnonyme().PostAsJsonAsync("/api/auth/connexion",
            new ConnexionDto { Email = Cible, MotDePasse = FixtureApi.MotDePasse });

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task Les_sessions_ouvertes_du_compte_sont_coupees()
    {
        var compte = await CreerResponsable();
        var sien = await _fixture.ClientConnecteAsync(Cible);

        Assert.Equal(HttpStatusCode.OK, (await sien.GetAsync("/api/auth/moi")).StatusCode);

        var empreinteAvant = (await LireEnBase(compte)).SecurityStamp;

        var rendu = await Anonymiser(compte);

        Assert.Equal(HttpStatusCode.Unauthorized, (await sien.GetAsync("/api/auth/moi")).StatusCode);

        // ⚠️ Le témoin sur `rafraichir` est indispensable et distinct : un jeton d'accès
        // est signé et vaut quinze minutes, donc `moi` seul ne dit rien de la révocation
        // (CLAUDE.md §10).
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await sien.PostAsync("/api/auth/rafraichir", null)).StatusCode);

        Assert.True(rendu.SessionsRevoquees >= 1, "Aucune session comptée comme révoquée.");

        // ⚠️ Et l'empreinte de sécurité se vérifie **en base**, pas par ses effets. Les
        // deux assertions HTTP ci-dessus restent vertes sans elle, parce que
        // `OnTokenValidated` refuse un compte inactif **ou** une empreinte changée, et
        // que `Actif = false` suffit à les produire. Un sabotage qui retirait
        // `UpdateSecurityStampAsync` est passé inaperçu pour cette raison exacte —
        // troisième fois que ces deux gardes se couvrent l'une l'autre dans ce projet.
        Assert.NotEqual(empreinteAvant, (await LireEnBase(compte)).SecurityStamp);
    }

    [Fact]
    public async Task Le_compte_rendu_enumere_ce_qui_subsiste()
    {
        var compte = await CreerResponsable();

        // Faire lire des données personnelles par ce compte, pour que le journal porte
        // des lignes à son nom.
        var sien = await _fixture.ClientConnecteAsync(Cible);
        await sien.GetAsync("/api/agents");

        var rendu = await Anonymiser(compte);

        // ⚠️ La raison pour laquelle l'identifiant doit survivre, et c'est l'exact
        // symétrique des interventions d'un agent : le journal d'accès attribue chaque
        // lecture de données personnelles à un `UtilisateurId`. Supprimer le compte
        // rendrait illisible le journal qui existe pour rendre des comptes.
        Assert.Contains(rendu.Subsiste, m => m.Contains("identifiant technique"));
        Assert.Contains(rendu.Subsiste, m => m.Contains("4(5)"));
        Assert.Contains(rendu.Subsiste, m => m.Contains("journal d'accès"));
        Assert.Contains(rendu.Subsiste, m => m.Contains("rôle"));
    }

    // ----------------------------------------------------------------- les refus

    [Fact]
    public async Task Une_seconde_tentative_est_refusee_en_disant_la_date()
    {
        var compte = await CreerResponsable();
        await Anonymiser(compte);

        var seconde = await _client.PostAsync($"/api/auth/comptes/{compte}/anonymiser", null);

        Assert.Equal(HttpStatusCode.Conflict, seconde.StatusCode);

        var message = await seconde.Content.ReadAsStringAsync();
        Assert.Contains("déjà été anonymisé le", message);
        Assert.Contains(DateTime.UtcNow.ToString("dd/MM/yyyy"), message);
    }

    [Fact]
    public async Task Un_compte_rattache_a_un_agent_est_renvoye_vers_la_route_de_l_agent()
    {
        var agent = await CreerAgent();
        var compte = await CreerCompte(RolesKlean.Agent, "agent.compte@test.local", agentId: agent);

        var reponse = await _client.PostAsync($"/api/auth/comptes/{compte}/anonymiser", null);

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);

        // ⚠️ Un refus qui ne nomme pas la sortie est un cul-de-sac. Le message doit
        // porter la route exacte, identifiant compris — leçon des `Restrict` de
        // l'étape 16 bis, où un sabotage qui vidait le message de sa marche à suivre
        // était passé inaperçu parce que le test ne vérifiait que le code.
        var message = await reponse.Content.ReadAsStringAsync();
        Assert.Contains($"/api/agents/{agent}/anonymiser", message);
        Assert.Contains("laisserait la fiche agent intacte", message);
    }

    [Fact]
    public async Task La_sortie_nommee_est_franchissable()
    {
        // L'autre moitié du test précédent : la friction n'a de valeur que si le chemin
        // indiqué fonctionne vraiment.
        var agent = await CreerAgent();
        var compte = await CreerCompte(RolesKlean.Agent, "agent.compte2@test.local", agentId: agent);

        var parLAgent = await _client.PostAsync($"/api/agents/{agent}/anonymiser", null);
        Assert.Equal(HttpStatusCode.OK, parLAgent.StatusCode);

        // Et le compte lié a bien été traité au passage, date comprise.
        var apres = await LireEnBase(compte);
        Assert.False(apres.Actif);
        Assert.NotNull(apres.DateAnonymisation);
        Assert.Equal($"{agent}@anonymise.invalid", apres.Email);
    }

    [Fact]
    public async Task Un_administrateur_ne_peut_pas_anonymiser_son_propre_compte()
    {
        var soi = await IdentifiantDuCompte(FixtureApi.AdminAlpha);

        var reponse = await _client.PostAsync($"/api/auth/comptes/{soi}/anonymiser", null);

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        Assert.Contains("son propre compte", await reponse.Content.ReadAsStringAsync());

        // Et rien n'a bougé : l'appelant est toujours connecté et actif.
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/auth/moi")).StatusCode);
        Assert.True((await LireEnBase(soi)).Actif);
    }

    [Fact]
    public async Task Une_entreprise_conserve_toujours_un_administrateur_actif()
    {
        // ⚠️ L'invariante réelle, et c'est le **refus de s'effacer soi-même** qui la
        // porte — pas une garde « dernier administrateur », qui serait inatteignable :
        // l'appelant est forcément un administrateur actif, donc si la cible en est un
        // autre, ils sont deux. Un sabotage a démontré qu'une telle garde ne faisait
        // rougir aucun test, n'ayant aucun cas à traiter.
        var second = await CreerCompte(RolesKlean.Administrateur, "admin.second@test.local");
        var premier = await IdentifiantDuCompte(FixtureApi.AdminAlpha);

        var depuisLeSecond = await _fixture.ClientConnecteAsync("admin.second@test.local");

        // Deux administrateurs actifs : l'un peut effacer l'autre.
        Assert.Equal(HttpStatusCode.OK,
            (await depuisLeSecond.PostAsync($"/api/auth/comptes/{premier}/anonymiser", null)).StatusCode);

        // Le survivant ne peut pas s'effacer lui-même : l'entreprise garde donc au moins
        // un compte capable d'en créer un autre.
        var refus = await depuisLeSecond.PostAsync($"/api/auth/comptes/{second}/anonymiser", null);
        Assert.Equal(HttpStatusCode.Conflict, refus.StatusCode);

        Assert.True((await LireEnBase(second)).Actif, "Le dernier administrateur a été neutralisé.");
    }

    [Fact]
    public async Task Anonymiser_l_agent_du_dernier_administrateur_est_refuse()
    {
        // ⚠️ Le trou que le sabotage de la garde inatteignable a fait apparaître, et il
        // est bien réel sur CE chemin. `POST /api/auth/comptes` exige un `AgentId` pour
        // le rôle Agent mais ne l'interdit à aucun autre : un compte `Administrateur`
        // peut donc porter une fiche agent — le cas normal dans une entreprise de cinq
        // personnes où le responsable fait aussi le ménage.
        //
        // Anonymiser cette fiche neutralise le compte **sans** passer par le refus de
        // s'effacer soi-même, qui ne garde que l'autre route. L'entreprise se
        // retrouverait sans aucun administrateur, et sans rattrapage par l'API.
        var agent = await CreerAgent();

        await _fixture.AvecServicesAsync(async services =>
        {
            var utilisateurs = services.GetRequiredService<UserManager<Utilisateur>>();
            var admin = await utilisateurs.FindByEmailAsync(FixtureApi.AdminAlpha);

            admin!.AgentId = agent;
            await utilisateurs.UpdateAsync(admin);
        });

        var refus = await _client.PostAsync($"/api/agents/{agent}/anonymiser", null);

        Assert.Equal(HttpStatusCode.Conflict, refus.StatusCode);
        Assert.Contains("dernier administrateur", await refus.Content.ReadAsStringAsync());

        // Et rien n'a bougé : ni la fiche, ni le compte.
        var fiche = await _client.GetFromJsonAsync<AgentDto>($"/api/agents/{agent}");
        Assert.Null(fiche!.DateAnonymisation);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/auth/moi")).StatusCode);
    }

    [Fact]
    public async Task La_sortie_du_dernier_administrateur_est_franchissable()
    {
        // L'autre moitié : le refus nomme « créer d'abord un second administrateur », et
        // ce chemin doit fonctionner. Une friction n'a de valeur que si elle est
        // franchissable.
        var agent = await CreerAgent();

        await _fixture.AvecServicesAsync(async services =>
        {
            var utilisateurs = services.GetRequiredService<UserManager<Utilisateur>>();
            var admin = await utilisateurs.FindByEmailAsync(FixtureApi.AdminAlpha);

            admin!.AgentId = agent;
            await utilisateurs.UpdateAsync(admin);
        });

        await CreerCompte(RolesKlean.Administrateur, "admin.renfort@test.local");

        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsync($"/api/agents/{agent}/anonymiser", null)).StatusCode);
    }

    [Fact]
    public async Task Un_responsable_n_est_pas_le_dernier_administrateur()
    {
        // La garde ne doit pas déborder : elle ne porte que sur les administrateurs. Un
        // responsable unique s'anonymise sans difficulté.
        var compte = await CreerResponsable();

        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsync($"/api/auth/comptes/{compte}/anonymiser", null)).StatusCode);
    }

    [Fact]
    public async Task Un_compte_d_une_autre_entreprise_rend_404()
    {
        var compte = await CreerResponsable();

        var intrus = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);

        // ⚠️ `AspNetUsers` n'est pas filtrée par le multi-tenant : le cadrage repose
        // entièrement sur le filtre écrit à la main dans le service, et c'est ici sa
        // seule garde — contrairement au chemin de l'agent, aucune validation en amont ne
        // rattraperait l'oubli.
        var reponse = await intrus.PostAsync($"/api/auth/comptes/{compte}/anonymiser", null);
        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);

        // Et le compte visé est intact.
        Assert.True((await LireEnBase(compte)).Actif);
    }

    [Fact]
    public async Task Un_identifiant_inconnu_rend_404()
    {
        var reponse = await _client.PostAsync($"/api/auth/comptes/{Guid.NewGuid()}/anonymiser", null);
        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }

    // ----------------------------------------------------------------- les droits

    [Fact]
    public async Task L_operation_est_reservee_a_l_administrateur()
    {
        var compte = await CreerResponsable();

        var responsable = await _fixture.ClientDeRoleAsync(
            _client, RolesKlean.Responsable, "autre.responsable@test.local");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await responsable.PostAsync($"/api/auth/comptes/{compte}/anonymiser", null)).StatusCode);

        Assert.True((await LireEnBase(compte)).Actif);
    }

    [Fact]
    public async Task L_operation_est_journalisee_en_Compte_et_Anonymisation()
    {
        var compte = await CreerResponsable();

        await Anonymiser(compte);

        var journal = await _client.PageAsync<JournalAccesDto>("/api/journal-acces?taille=200");

        var ligne = Assert.Single(journal.Elements,
            l => l.Ressource == nameof(RessourceAccedee.Compte));

        Assert.Equal(nameof(NatureAcces.Anonymisation), ligne.Nature);
        Assert.Equal(compte, ligne.CibleId);
    }

    [Fact]
    public async Task Un_refus_ne_laisse_aucune_ligne_au_journal()
    {
        var compte = await CreerResponsable();
        await Anonymiser(compte);

        var avant = await CompterLignesCompte();

        var refus = await _client.PostAsync($"/api/auth/comptes/{compte}/anonymiser", null);
        Assert.Equal(HttpStatusCode.Conflict, refus.StatusCode);

        // Règle 5 du §7 bis : seules les divulgations et les écritures abouties sont
        // consignées. Un 409 n'a rien changé, et le filtre d'action ne journalise pas un
        // `ObjectResult` de conflit.
        //
        // ⚠️ On compte les lignes de ressource `Compte`, pas le total du journal : lire
        // le journal **est** journalisé, donc deux appels au total encadrant l'opération
        // mesurent leur propre observation et montrent +1 quoi qu'il arrive. Le premier
        // écrit de ce test a été vert à l'envers pour cette raison exacte.
        Assert.Equal(avant, await CompterLignesCompte());
    }

    // ------------------------------------------------------------------- outils

    private async Task<AnonymisationCompteDto> Anonymiser(Guid compte)
    {
        var reponse = await _client.PostAsync($"/api/auth/comptes/{compte}/anonymiser", null);

        if (!reponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"POST /api/auth/comptes/{compte}/anonymiser → {(int)reponse.StatusCode} : "
                + await reponse.Content.ReadAsStringAsync());
        }

        return (await reponse.Content.ReadFromJsonAsync<AnonymisationCompteDto>())!;
    }

    private Task<Guid> CreerResponsable() => CreerCompte(RolesKlean.Responsable, Cible);

    private async Task<Guid> CreerCompte(
        string role, string email, Guid? agentId = null, Guid? clientId = null)
    {
        var reponse = await _fixture.CreerCompteAsync(_client, new CompteCreateDto
        {
            Email = email,
            Nom = role,
            Prenom = role,
            Role = role,
            AgentId = agentId,
            ClientId = clientId
        });

        if (!reponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Création du compte {role} impossible : "
                + await reponse.Content.ReadAsStringAsync());
        }

        return (await reponse.Content.ReadFromJsonAsync<CompteCreeDto>())!.Compte.Id;
    }

    private async Task<Guid> CreerAgent()
    {
        var reponse = await _client.PostAsJsonAsync("/api/agents", new AgentCreateDto
        {
            Nom = "Diallo",
            Prenom = "Aminata",
            DateEmbauche = new DateTime(2024, 3, 15)
        });

        reponse.EnsureSuccessStatusCode();

        return (await reponse.Content.ReadFromJsonAsync<AgentDto>())!.Id;
    }

    private async Task<Utilisateur> LireEnBase(Guid id)
    {
        Utilisateur? compte = null;

        await _fixture.AvecServicesAsync(async services =>
        {
            var utilisateurs = services.GetRequiredService<UserManager<Utilisateur>>();
            compte = await utilisateurs.FindByIdAsync(id.ToString());
        });

        return compte ?? throw new InvalidOperationException($"Compte {id} introuvable en base.");
    }

    private async Task PoserTelephone(Guid id, string numero)
    {
        await _fixture.AvecServicesAsync(async services =>
        {
            var utilisateurs = services.GetRequiredService<UserManager<Utilisateur>>();
            var compte = await utilisateurs.FindByIdAsync(id.ToString());

            compte!.PhoneNumber = numero;
            compte.PhoneNumberConfirmed = true;

            await utilisateurs.UpdateAsync(compte);
        });
    }

    private async Task<Guid> IdentifiantDuCompte(string email)
    {
        var identifiant = Guid.Empty;

        await _fixture.AvecServicesAsync(async services =>
        {
            var utilisateurs = services.GetRequiredService<UserManager<Utilisateur>>();
            identifiant = (await utilisateurs.FindByEmailAsync(email))!.Id;
        });

        return identifiant;
    }

    /// <summary>
    /// Les lignes de ressource <c>Compte</c>, et non le total du journal.
    ///
    /// ⚠️ Lire le journal est journalisé : compter le total avant et après une opération
    /// mesure sa propre observation, et rend +1 même quand l'opération n'a rien écrit.
    /// </summary>
    private async Task<int> CompterLignesCompte()
    {
        var journal = await _client.PageAsync<JournalAccesDto>("/api/journal-acces?taille=200");

        return journal.Elements.Count(l => l.Ressource == nameof(RessourceAccedee.Compte));
    }
}
