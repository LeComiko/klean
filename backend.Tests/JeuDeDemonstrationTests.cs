using System.Net.Http.Json;
using Klean.Api.Data;
using Klean.Api.Demonstration;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Klean.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Klean.Api.Tests;

/// <summary>
/// Le jeu de démonstration installable (SPEC.md, étape 28, lot A).
///
/// ⚠️ <b>C'est la raison d'être de la forme choisie.</b> Le script Python de l'étape 15 a
/// cessé de tourner à l'étape 23 sans que rien le signale. Joué à chaque série, ce jeu
/// échoue ici le jour où un contrôleur refuse ce qu'il pose — pas chez l'inconnu qui suit
/// le <c>README</c>.
///
/// L'horloge est fixée au mercredi 18 novembre 2026 : la même date donne le même jeu, et
/// les comptes exacts ci-dessous le figent.
/// </summary>
[Collection(CollectionApi.Nom)]
public class JeuDeDemonstrationTests : IAsyncLifetime
{
    private static readonly DateOnly Aujourdhui = new(2026, 11, 18);

    private readonly FixtureApi _fixture;
    private HttpClient _client = null!;

    public JeuDeDemonstrationTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _fixture.Fabrique.Horloge.Fixer(Aujourdhui);
        _client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Le_jeu_s_installe_et_rend_compte_de_ce_qu_il_a_pose()
    {
        var rapport = await InstallerAsync();

        Assert.Equal(JeuDeDemonstration.RaisonSociale, rapport.Entreprise);
        Assert.Equal(Aujourdhui, rapport.Aujourdhui);
        Assert.Equal((2, 5, 6, 3), (rapport.Clients, rapport.Sites, rapport.Agents, rapport.TypesPrestation));
        Assert.Equal((1, 1), (rapport.RemplacementsEnRecherche, rapport.RemplacementsPourvus));

        // Figés pour cette date : un jeu qui change sans qu'on le décide se voit ici. Du
        // 4 novembre au 16 décembre inclus, le 11 novembre férié et aucun contrat ne
        // l'incluant : Bellevue 30, Haussmann 18, Daumesnil 12, République 42, Bastille
        // 30 le soir et 6 samedis sans titulaire — les six non pourvues.
        Assert.Equal((138, 6), (rapport.Vacations, rapport.NonPourvues));

        // Lot B. Nguyen pointe ses treize jours passés, dont deux sans preuve suffisante ; les
        // vingt-sept des autres agents sont constatées sans preuve — Diallo 9, Costa 5,
        // Moreau 4, Bernard 9. Le nettoyant vitres, entré à 3 L pour un seuil de 4.
        Assert.Equal((11, 29), (rapport.Terminees, rapport.NonCertifiees));
        Assert.Equal((2, 1), (rapport.AnomaliesOuvertes, rapport.AnomaliesTraitees));
        Assert.Equal((5, 1), (rapport.Produits, rapport.ProduitsSousSeuil));
        Assert.Equal(4, rapport.Comptes.Count);
    }

    [Fact]
    public async Task L_entreprise_est_renommee_sur_son_territoire()
    {
        await InstallerAsync();

        var entreprise = Assert.Single(await _client.ListerAsync<EntrepriseDto>("/api/entreprises"));

        Assert.Equal(JeuDeDemonstration.RaisonSociale, entreprise.RaisonSociale);
        Assert.Equal(nameof(Territoire.Metropole), entreprise.Territoire);
    }

    /// <summary>
    /// La semaine en cours a quelque chose à montrer, y compris une vacation non pourvue :
    /// c'est la première page qu'ouvre un administrateur.
    /// </summary>
    [Fact]
    public async Task Le_planning_de_la_semaine_en_cours_a_des_vacations_et_des_non_pourvues()
    {
        await InstallerAsync();

        var semaine = await PlanningAsync(new DateOnly(2026, 11, 16));

        Assert.True(semaine.TotalVacations > 0);
        Assert.True(semaine.TotalNonPourvues > 0);
        Assert.Equal(6, semaine.Agents.Count());
    }

    /// <summary>
    /// L'horloge décalée : deux semaines d'historique, quatre à venir, rien au-delà. Le
    /// 4 novembre est le premier jour généré, un mercredi ; le 16 décembre, le dernier.
    /// </summary>
    [Fact]
    public async Task Le_planning_couvre_deux_semaines_passees_et_quatre_a_venir_et_rien_au_dela()
    {
        await InstallerAsync();

        Assert.Equal(0, (await PlanningAsync(new DateOnly(2026, 10, 26))).TotalVacations);
        Assert.True((await PlanningAsync(new DateOnly(2026, 11, 2))).TotalVacations > 0);
        Assert.True((await PlanningAsync(new DateOnly(2026, 12, 14))).TotalVacations > 0);
        Assert.Equal(0, (await PlanningAsync(new DateOnly(2026, 12, 21))).TotalVacations);

        var debut = await PlanningAsync(new DateOnly(2026, 11, 2));
        Assert.DoesNotContain(debut.Agents.SelectMany(a => a.Vacations), v => v.Date < new DateTime(2026, 11, 4));
    }

    /// <summary>
    /// L'arrêt maladie laisse un remplacement en recherche ; le congé, un remplacement pourvu
    /// par un autre agent que l'absent — ce que la forme garantit, et que le test constate.
    /// </summary>
    [Fact]
    public async Task Une_absence_laisse_un_remplacement_en_recherche_l_autre_est_pourvue_par_un_eligible()
    {
        await InstallerAsync();

        var remplacements = await _client.ListerAsync<RemplacementDto>("/api/remplacements?taille=200");
        var absences = await _client.ListerAsync<AbsenceDto>("/api/absences?taille=200");

        var recherche = Assert.Single(remplacements, r => r.Statut == "Recherche");
        var pourvu = Assert.Single(remplacements, r => r.Statut == "Accepte");

        var maladie = Assert.Single(absences, a => a.Type == "Maladie");
        Assert.Equal(maladie.Id, recherche.AbsenceId);
        Assert.Null(maladie.Motif);

        var conge = Assert.Single(absences, a => a.Type == "Conge");
        Assert.Equal(conge.Id, pourvu.AbsenceId);
        Assert.NotNull(pourvu.AgentRemplacantId);
        Assert.NotEqual(conge.AgentId, pourvu.AgentRemplacantId);

        // Le moteur, interrogé après coup, écarte l'agent en arrêt de sa propre vacation.
        var candidats = await _client.GetFromJsonAsync<List<CandidatDto>>(
            $"/api/remplacements/{recherche.Id}/candidats");
        Assert.NotEmpty(candidats!);
        Assert.DoesNotContain(candidats!, c => c.AgentId == maladie.AgentId);
    }

    [Fact]
    public async Task Un_second_passage_est_refuse_et_ne_change_rien()
    {
        await InstallerAsync();
        var avant = await CompterAsync();

        var refus = await Assert.ThrowsAsync<RefusDemonstration>(InstallerAsync);

        Assert.Contains("entreprise vide", refus.Message);
        Assert.Equal(avant, await CompterAsync());
    }

    /// <summary>
    /// Sous l'identité de l'administrateur d'Alpha, le jeu ne peut rien écrire chez Beta : les
    /// filtres et la sécurité au niveau des lignes s'appliquent comme à une requête.
    /// </summary>
    [Fact]
    public async Task L_autre_entreprise_reste_vide()
    {
        await InstallerAsync();

        var beta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);

        Assert.Empty(await beta.ListerAsync<ClientDto>("/api/clients"));
        Assert.Empty(await beta.ListerAsync<AgentDto>("/api/agents"));
        Assert.NotEqual(JeuDeDemonstration.RaisonSociale,
            Assert.Single(await beta.ListerAsync<EntrepriseDto>("/api/entreprises")).RaisonSociale);
    }

    /// <summary>
    /// La commande ne choisit pas d'entreprise : sur une instance qui en compte plusieurs —
    /// la série en a deux —, elle refuse, sans rien écrire.
    /// </summary>
    [Fact]
    public async Task La_commande_refuse_une_instance_a_plusieurs_entreprises()
    {
        var sortie = new StringWriter();

        var code = await JeuDeDemonstration.ExecuterCommandeAsync(_fixture.Fabrique.Services, sortie);

        Assert.Equal(1, code);
        Assert.Contains("2 entreprises", sortie.ToString());
        Assert.Empty(await _client.ListerAsync<ClientDto>("/api/clients"));
    }

    [Fact]
    public async Task Le_jeu_ne_laisse_aucune_ligne_au_journal_d_acces()
    {
        await InstallerAsync();

        // Le jeu a lu les candidats d'un remplacement, une lecture journalisée par l'API. Ni
        // celle-là ni aucune autre : la lecture que voici est la première, et ne contient
        // jamais sa propre ligne.
        Assert.Empty(await _client.ListerAsync<JournalAccesDto>("/api/journal-acces?taille=200"));
    }

    /// <summary>
    /// Un compte par rôle, provisoire : le mot de passe rendu une fois est celui que le serveur
    /// attend, et la première connexion exige d'en changer. L'agent est lié à sa fiche, le
    /// syndic à son client.
    /// </summary>
    [Fact]
    public async Task Un_compte_par_role_provisoire_et_dont_le_mot_de_passe_ouvre_la_premiere_connexion()
    {
        var rapport = await InstallerAsync();

        var comptes = await _client.GetFromJsonAsync<List<CompteDto>>("/api/auth/comptes");
        var agents = await _client.ListerAsync<AgentDto>("/api/agents?taille=200");
        var clients = await _client.ListerAsync<ClientDto>("/api/clients?taille=200");

        Assert.Equal(
            [RolesKlean.Agent, RolesKlean.ChefEquipe, RolesKlean.ClientSyndic, RolesKlean.Responsable],
            rapport.Comptes.Select(c => c.Role).Order());
        Assert.Equal(4, rapport.Comptes.Select(c => c.MotDePasseProvisoire).Distinct().Count());

        foreach (var cree in rapport.Comptes)
        {
            var compte = Assert.Single(comptes!, c => c.Email == cree.Email);
            Assert.True(compte.MotDePasseProvisoire);
            Assert.Equal(cree.Role, compte.Role);
        }

        var agent = Assert.Single(comptes!, c => c.Role == RolesKlean.Agent);
        Assert.Equal(Assert.Single(agents, a => a.Nom == "Nguyen").Id, agent.AgentId);

        var syndic = Assert.Single(comptes!, c => c.Role == RolesKlean.ClientSyndic);
        Assert.Equal(Assert.Single(clients, c => c.Nom == "Bellevue Gestion").Id, syndic.ClientId);

        // Le mot de passe affiché est le bon : le serveur demande d'en choisir un autre.
        var connexion = await _fixture.ClientAnonyme().PostAsJsonAsync("/api/auth/connexion",
            new ConnexionDto { Email = agent.Email, MotDePasse = rapport.Comptes.Single(c => c.Role == RolesKlean.Agent).MotDePasseProvisoire });

        Assert.Equal(System.Net.HttpStatusCode.Accepted, connexion.StatusCode);
        Assert.Equal("ChangementMotDePasse", (await connexion.Content.ReadFromJsonAsync<EtapeConnexionDto>())!.Etape);
    }

    [Fact]
    public async Task Le_chef_d_equipe_encadre_trois_agents_et_eux_seuls()
    {
        await InstallerAsync();

        var chef = Assert.Single((await _client.GetFromJsonAsync<List<CompteDto>>("/api/auth/comptes"))!,
            c => c.Role == RolesKlean.ChefEquipe);
        var agents = await _client.ListerAsync<AgentDto>("/api/agents?taille=200");

        Assert.Equal(["Bernard", "Lefèvre", "Nguyen"],
            agents.Where(a => a.ChefEquipeId == chef.Id).Select(a => a.Nom).Order());
        Assert.Equal(3, agents.Count(a => a.ChefEquipeId is null));
    }

    /// <summary>
    /// Les pointages des jours passés, tranchés par le serveur : le jeu n'a donné qu'une
    /// position, et la vacation du jour reste à démarrer depuis <i>Ma journée</i>.
    /// </summary>
    [Fact]
    public async Task Les_passages_passes_sont_pointes_par_le_serveur_et_celui_du_jour_reste_a_faire()
    {
        await InstallerAsync();

        await _fixture.AvecServicesAsync(async services =>
        {
            var db = services.GetRequiredService<KleanDbContext>();
            var nguyen = await db.Agents.Where(a => a.EntrepriseId == _fixture.EntrepriseAlpha && a.Nom == "Nguyen")
                .Select(a => a.Id).SingleAsync();

            var preuves = await db.Pointages.Where(p => p.EntrepriseId == _fixture.EntrepriseAlpha)
                .GroupBy(p => p.Preuve).Select(g => new { g.Key, Nombre = g.Count() }).ToListAsync();
            Assert.Equal(
                [(PreuvePointage.SurSite, 24), (PreuvePointage.HorsRayon, 1), (PreuvePointage.NonLocalisee, 1)],
                preuves.OrderBy(p => p.Key).Select(p => (p.Key, p.Nombre)));

            var controles = await db.ControlesEffectues.Where(c => c.EntrepriseId == _fixture.EntrepriseAlpha).ToListAsync();
            Assert.Equal(52, controles.Count);
            Assert.Single(controles, c => !c.Fait);

            var aujourdhui = DateTime.SpecifyKind(Aujourdhui.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var duJour = await db.Interventions.SingleAsync(i => i.AgentId == nguyen && i.DatePrevue == aujourdhui);
            Assert.Equal(StatutIntervention.Planifiee, duJour.Statut);
        });
    }

    [Fact]
    public async Task Trois_anomalies_dont_une_traitee_le_lendemain_par_l_administrateur()
    {
        await InstallerAsync();

        var anomalies = await _client.ListerAsync<AnomalieDto>("/api/anomalies?taille=200");

        Assert.Equal(3, anomalies.Count);
        var traitee = Assert.Single(anomalies, a => a.Statut == "Traitee");
        Assert.NotNull(traitee.TraiteeParId);
        Assert.Equal(traitee.DateSignalement.Date.AddDays(1), traitee.DateTraitement!.Value.Date);
        Assert.All(anomalies, a => Assert.Equal("Bureaux République", a.Site));
    }

    [Fact]
    public async Task Le_nettoyant_vitres_est_sous_son_seuil_et_l_inventaire_laisse_un_ecart()
    {
        await InstallerAsync();

        var produits = await _client.ListerAsync<ProduitDto>("/api/produits?taille=200");
        var stocks = await _client.ListerAsync<StockDto>("/api/stocks?taille=200");
        var mouvements = await _client.ListerAsync<MouvementStockDto>("/api/mouvements-stock?taille=200");

        var vitres = Assert.Single(produits, p => p.Nom == "Nettoyant vitres");
        Assert.Equal(3m, stocks.Where(s => s.ProduitId == vitres.Id).Sum(s => s.Quantite));

        var inventaire = Assert.Single(mouvements, m => m.Nature == "Inventaire");
        Assert.Equal(-2m, inventaire.Ecart);
    }

    /// <summary>
    /// La validation des attributs, que la liaison ferait avant le contrôleur. ⚠️ Éprouvée
    /// directement, parce qu'aucun contrôleur ne revérifie un format de courriel : retirée,
    /// une donnée fautive du jeu passait sans bruit, ce qu'un sabotage a montré.
    /// </summary>
    [Fact]
    public void Un_DTO_que_ses_attributs_refusent_arrete_le_jeu()
    {
        var refus = Assert.Throws<InvalidOperationException>(() => JeuDeDemonstration.Valide(new AgentCreateDto
        {
            Nom = "Martin",
            Prenom = "Claire",
            Email = "pas une adresse"
        }));

        Assert.Contains(nameof(AgentCreateDto), refus.Message);
    }

    // ---------------------------------------------------------------------------

    private async Task<RapportDemonstration> InstallerAsync()
    {
        Guid administrateur = Guid.Empty;

        await _fixture.AvecServicesAsync(async services =>
        {
            var utilisateurs = services.GetRequiredService<UserManager<Utilisateur>>();
            administrateur = (await utilisateurs.FindByEmailAsync(FixtureApi.AdminAlpha))!.Id;
        });

        return await new JeuDeDemonstration(_fixture.Fabrique.Services)
            .InstallerAsync(_fixture.EntrepriseAlpha, administrateur);
    }

    private async Task<PlanningSemaineDto> PlanningAsync(DateOnly lundi) =>
        (await _client.GetFromJsonAsync<PlanningSemaineDto>($"/api/planning?lundi={lundi:yyyy-MM-dd}"))!;

    private async Task<(int, int, int, int)> CompterAsync()
    {
        var resultat = (0, 0, 0, 0);

        await _fixture.AvecServicesAsync(async services =>
        {
            var db = services.GetRequiredService<KleanDbContext>();
            resultat = (
                await db.Clients.CountAsync(c => c.EntrepriseId == _fixture.EntrepriseAlpha),
                await db.Agents.CountAsync(a => a.EntrepriseId == _fixture.EntrepriseAlpha),
                await db.Interventions.CountAsync(i => i.EntrepriseId == _fixture.EntrepriseAlpha),
                await db.Absences.CountAsync(a => a.EntrepriseId == _fixture.EntrepriseAlpha));
        });

        return resultat;
    }
}
