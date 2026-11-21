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
