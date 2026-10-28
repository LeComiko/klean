using System.Net;
using System.Net.Http.Json;
using Klean.Api.Data;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Klean.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Klean.Api.Tests;

/// <summary>
/// Le rattachement d'un agent à son chef d'équipe — étape 19, lot A.
///
/// Le lien vise un compte, et <c>AspNetUsers</c> n'est pas filtrée par le multi-tenant
/// (CLAUDE.md §7) : tout ce qui empêche de rattacher un agent au chef d'une autre
/// entreprise, ou à un compte qui n'est pas chef, est écrit à la main. Cette série garde
/// ces deux conditions <b>séparément</b>, chacune par un compte qui ne viole qu'elle.
/// C'est ce qui compte : au lot B, ce lien ouvrira l'équipe au chef.
/// </summary>
[Collection(CollectionApi.Nom)]
public class ChefEquipeTests : IAsyncLifetime
{
    private const string ChefAlpha = "chef.alpha@test.local";
    private const string ChefBeta = "chef.beta@test.local";
    private const string ResponsableAlpha = "responsable.alpha@test.local";

    private readonly FixtureApi _fixture;
    private HttpClient _admin = null!;

    public ChefEquipeTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------- le rattachement

    [Fact]
    public async Task Un_agent_cree_avec_son_chef_le_garde()
    {
        var chef = await CreerCompte(_admin, RolesKlean.ChefEquipe, ChefAlpha);

        var reponse = await _admin.PostAsJsonAsync("/api/agents", Fiche(chef));

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        var cree = await reponse.Content.ReadFromJsonAsync<AgentDto>();
        Assert.Equal(chef, cree!.ChefEquipeId);
        Assert.Equal(chef, await ChefEnBase(cree.Id));
    }

    [Fact]
    public async Task Un_agent_se_rattache_puis_se_detache_par_sa_fiche()
    {
        var chef = await CreerCompte(_admin, RolesKlean.ChefEquipe, ChefAlpha);
        var agent = await CreerAgent(null);

        var rattachement = await _admin.PutAsJsonAsync($"/api/agents/{agent}", Fiche(chef));
        Assert.Equal(HttpStatusCode.NoContent, rattachement.StatusCode);

        var relu = await _admin.GetFromJsonAsync<AgentDto>($"/api/agents/{agent}");
        Assert.Equal(chef, relu!.ChefEquipeId);

        var detachement = await _admin.PutAsJsonAsync($"/api/agents/{agent}", Fiche(null));
        Assert.Equal(HttpStatusCode.NoContent, detachement.StatusCode);
        Assert.Null(await ChefEnBase(agent));
    }

    [Fact]
    public async Task La_liste_des_agents_porte_le_rattachement()
    {
        var chef = await CreerCompte(_admin, RolesKlean.ChefEquipe, ChefAlpha);
        var agent = await CreerAgent(chef);

        var page = await _admin.PageAsync<AgentDto>("/api/agents");

        Assert.Equal(chef, page.Elements.Single(a => a.Id == agent).ChefEquipeId);
    }

    // ------------------------------------------------------------- les refus

    [Fact]
    public async Task Le_chef_d_une_autre_entreprise_est_refuse()
    {
        // ⚠️ LE test de cette série. Le compte a le bon rôle : seule l'entreprise le
        // disqualifie, et seul le filtre écrit à la main sur EntrepriseId le voit — les
        // filtres globaux ne couvrent pas AspNetUsers. Sans lui, le lot B ouvrirait
        // l'équipe d'Alpha à un chef de Beta.
        var adminBeta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);
        var intrus = await CreerCompte(adminBeta, RolesKlean.ChefEquipe, ChefBeta);

        var creation = await _admin.PostAsJsonAsync("/api/agents", Fiche(intrus));
        Assert.Equal(HttpStatusCode.BadRequest, creation.StatusCode);
        Assert.Contains("n'est pas un chef d'équipe de cette entreprise",
            await creation.Content.ReadAsStringAsync());

        var agent = await CreerAgent(null);
        var modification = await _admin.PutAsJsonAsync($"/api/agents/{agent}", Fiche(intrus));
        Assert.Equal(HttpStatusCode.BadRequest, modification.StatusCode);
        Assert.Null(await ChefEnBase(agent));
    }

    [Fact]
    public async Task Un_compte_de_l_entreprise_qui_n_est_pas_chef_est_refuse()
    {
        // Le symétrique : la bonne entreprise, le mauvais rôle. Un responsable, et
        // l'administrateur lui-même — sans la condition de rôle, n'importe quel compte
        // deviendrait chef d'une équipe.
        var responsable = await CreerCompte(_admin, RolesKlean.Responsable, ResponsableAlpha);
        var administrateur = await IdDuCompte(FixtureApi.AdminAlpha);

        foreach (var compte in new[] { responsable, administrateur })
        {
            var reponse = await _admin.PostAsJsonAsync("/api/agents", Fiche(compte));
            Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        }
    }

    [Fact]
    public async Task Un_identifiant_inconnu_est_refuse()
    {
        var reponse = await _admin.PostAsJsonAsync("/api/agents", Fiche(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Un_chef_desactive_reste_acceptable()
    {
        // Voulu : refuser obligerait à rattacher ailleurs toute l'équipe d'un chef parti
        // avant de pouvoir corriger le téléphone de l'un de ses agents.
        var chef = await CreerCompte(_admin, RolesKlean.ChefEquipe, ChefAlpha);
        var agent = await CreerAgent(chef);
        await _fixture.DesactiverCompteAsync(ChefAlpha);

        var reponse = await _admin.PutAsJsonAsync($"/api/agents/{agent}", Fiche(chef));

        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);
    }

    // ------------------------------------------------------------- la liste des chefs

    [Fact]
    public async Task La_liste_rend_les_chefs_de_l_entreprise_et_eux_seuls()
    {
        var chef = await CreerCompte(_admin, RolesKlean.ChefEquipe, ChefAlpha);
        await CreerCompte(_admin, RolesKlean.Responsable, ResponsableAlpha);

        var adminBeta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);
        await CreerCompte(adminBeta, RolesKlean.ChefEquipe, ChefBeta);

        var chefs = await _admin.GetFromJsonAsync<List<ChefEquipeDto>>("/api/agents/chefs-equipe");

        var seul = Assert.Single(chefs!);
        Assert.Equal(chef, seul.Id);
        Assert.Equal(RolesKlean.ChefEquipe, seul.Nom);
        Assert.True(seul.Actif);
    }

    [Fact]
    public async Task La_liste_signale_un_chef_desactive()
    {
        await CreerCompte(_admin, RolesKlean.ChefEquipe, ChefAlpha);
        await _fixture.DesactiverCompteAsync(ChefAlpha);

        var chefs = await _admin.GetFromJsonAsync<List<ChefEquipeDto>>("/api/agents/chefs-equipe");

        Assert.False(Assert.Single(chefs!).Actif);
    }

    [Fact]
    public async Task La_liste_ne_rend_ni_courriel_ni_telephone()
    {
        // Sur le corps brut, pas sur le DTO : une propriété ajoutée demain partirait sans
        // qu'aucune assertion typée la voie.
        await CreerCompte(_admin, RolesKlean.ChefEquipe, ChefAlpha);

        var corps = await _admin.GetStringAsync("/api/agents/chefs-equipe");

        Assert.DoesNotContain(ChefAlpha, corps);
        Assert.DoesNotContain("email", corps, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("telephone", corps, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(RolesKlean.Responsable, ResponsableAlpha)]
    [InlineData(RolesKlean.ChefEquipe, ChefAlpha)]
    public async Task La_liste_est_reservee_a_l_administrateur(string role, string email)
    {
        await CreerCompte(_admin, role, email);
        var client = await _fixture.ClientConnecteAsync(email);

        var reponse = await client.GetAsync("/api/agents/chefs-equipe");

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    [Fact]
    public async Task La_liste_est_journalisee_et_compte_les_chefs_rendus()
    {
        await CreerCompte(_admin, RolesKlean.ChefEquipe, ChefAlpha);
        await CreerCompte(_admin, RolesKlean.ChefEquipe, "chef2.alpha@test.local");

        var avant = await LignesDuJournalSurLesComptes();
        await _admin.GetFromJsonAsync<List<ChefEquipeDto>>("/api/agents/chefs-equipe");
        var nouvelles = (await LignesDuJournalSurLesComptes()).Skip(avant.Count).ToList();

        var ligne = Assert.Single(nouvelles);
        Assert.Equal(NatureAcces.Liste, ligne.Nature);
        Assert.Equal(2, ligne.Lignes);
        Assert.Null(ligne.CibleId);
    }

    // ------------------------------------------------------------- outils

    private static AgentCreateDto Fiche(Guid? chef) => new()
    {
        Nom = "Martin",
        Prenom = "Léa",
        DateEmbauche = new DateTime(2024, 3, 15),
        ChefEquipeId = chef
    };

    private async Task<Guid> CreerAgent(Guid? chef)
    {
        var reponse = await _admin.PostAsJsonAsync("/api/agents", Fiche(chef));
        reponse.EnsureSuccessStatusCode();
        return (await reponse.Content.ReadFromJsonAsync<AgentDto>())!.Id;
    }

    /// <summary>Crée le compte par l'API, donc sous ses propres contrôles, et rend son identifiant.</summary>
    private static async Task<Guid> CreerCompte(HttpClient administrateur, string role, string email)
    {
        var reponse = await administrateur.PostAsJsonAsync("/api/auth/comptes", new CompteCreateDto
        {
            Email = email,
            MotDePasse = FixtureApi.MotDePasse,
            Nom = role,
            Prenom = "Test",
            Role = role
        });

        reponse.EnsureSuccessStatusCode();
        return (await reponse.Content.ReadFromJsonAsync<ProfilDto>())!.Id;
    }

    private async Task<Guid> IdDuCompte(string email)
    {
        var id = Guid.Empty;

        await _fixture.AvecServicesAsync(async services =>
        {
            var compte = await services.GetRequiredService<UserManager<Utilisateur>>().FindByEmailAsync(email);
            id = compte!.Id;
        });

        return id;
    }

    /// <summary>La valeur stockée, relue hors de l'API.</summary>
    private async Task<Guid?> ChefEnBase(Guid agent)
    {
        Guid? chef = null;

        await _fixture.AvecServicesAsync(async services =>
        {
            var db = services.GetRequiredService<KleanDbContext>();
            chef = await db.Agents.AsNoTracking()
                .Where(a => a.Id == agent).Select(a => a.ChefEquipeId).SingleAsync();
        });

        return chef;
    }

    private async Task<List<AccesJournalise>> LignesDuJournalSurLesComptes()
    {
        var lignes = new List<AccesJournalise>();

        await _fixture.AvecServicesAsync(async services =>
        {
            var db = services.GetRequiredService<KleanDbContext>();
            lignes = await db.AccesJournalises.AsNoTracking()
                .Where(a => a.Ressource == RessourceAccedee.Compte)
                .OrderBy(a => a.Horodatage)
                .ToListAsync();
        });

        return lignes;
    }
}
