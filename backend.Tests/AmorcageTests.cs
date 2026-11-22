using Klean.Api.Data;
using Klean.Api.Demonstration;
using Klean.Api.Models;
using Klean.Api.Services;
using Klean.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Klean.Api.Tests;

/// <summary>
/// Les démarrages sans compte (SPEC.md, étape 34, lot A2) : les trois variables d'amorçage
/// créent l'entreprise et l'administrateur ; sinon le code d'installation fait attendre
/// l'assistant ; sinon le démarrage est refusé, en nommant les deux sorties.
///
/// ⚠️ <b>Cette classe vide <c>klean_test</c></b>, comptes compris, et la remet en forme en
/// sortant : sans le <see cref="DisposeAsync"/>, la classe suivante trouverait une base vide.
/// L'amorçage est construit à la main, sur une configuration propre au test : celle de
/// l'hôte porte les variables de la fabrique.
///
/// Chaque cas relit en base, depuis une autre portée que celle de l'amorçage.
/// </summary>
[Collection(CollectionApi.Nom)]
public class AmorcageTests : IAsyncLifetime
{
    private readonly FixtureApi _fixture;

    public AmorcageTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        await _fixture.ViderAsync();
    }

    public Task DisposeAsync() => _fixture.ReinitialiserAsync();

    private static readonly Dictionary<string, string?> Variables = new()
    {
        ["Amorcage:EmailAdmin"] = "patron@test.local",
        ["Amorcage:MotDePasseAdmin"] = "AmorcageTest2026",
        ["Amorcage:RaisonSocialeEntreprise"] = "Gamma Propreté",
        ["Amorcage:SiretEntreprise"] = "30000000000003"
    };

    private static readonly (string, string?) Code = ("Installation:Code", FabriqueApi.CodeDInstallation);

    private static Dictionary<string, string?> Avec(params (string Cle, string? Valeur)[] reglages) =>
        reglages.ToDictionary(r => r.Cle, r => r.Valeur);

    private static Dictionary<string, string?> VariablesEt(params (string Cle, string? Valeur)[] reglages)
    {
        var tout = new Dictionary<string, string?>(Variables);
        foreach (var (cle, valeur) in reglages) tout[cle] = valeur;
        return tout;
    }

    private async Task<(Exception? Erreur, EtatInstallation Etat)> AmorcerAsync(Dictionary<string, string?> reglages)
    {
        using var portee = _fixture.PorteeAuProprietaire();
        var services = portee.ServiceProvider;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(reglages).Build();
        var etat = new EtatInstallation();
        var amorcage = new AmorcageService(
            services.GetRequiredService<KleanDbContext>(),
            services.GetRequiredService<RoleManager<IdentityRole<Guid>>>(),
            configuration,
            services.GetRequiredService<ITenantContext>(),
            services.GetRequiredService<ServiceInstallation>(),
            CodeInstallation.Lire(configuration),
            etat);
        return (await Record.ExceptionAsync(amorcage.ExecuterAsync), etat);
    }

    private async Task<List<Entreprise>> EntreprisesAsync()
    {
        using var portee = _fixture.PorteeAuProprietaire();
        return await portee.ServiceProvider.GetRequiredService<KleanDbContext>()
            .Entreprises.AsNoTracking().ToListAsync();
    }

    private async Task<int> ComptesAsync()
    {
        using var portee = _fixture.PorteeAuProprietaire();
        return await portee.ServiceProvider.GetRequiredService<KleanDbContext>().Users.CountAsync();
    }

    /// <summary>Une entreprise posée au propriétaire, sans compte : le reste d'un amorçage d'avant le lot A2.</summary>
    private async Task PoserOrphelineAsync()
    {
        await _fixture.AvecServicesAsync(async services =>
        {
            var db = services.GetRequiredService<KleanDbContext>();
            db.Entreprises.Add(new Entreprise { RaisonSociale = "Orpheline", Siret = "40000000000004" });
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task Ni_code_ni_variables_le_demarrage_est_refuse_en_nommant_les_deux_sorties()
    {
        var (erreur, _) = await AmorcerAsync(Avec());

        Assert.IsType<InvalidOperationException>(erreur);
        Assert.Contains("Installation:Code", erreur.Message);
        Assert.Contains("Amorcage:EmailAdmin", erreur.Message);
        Assert.Empty(await EntreprisesAsync());
    }

    [Fact]
    public async Task Code_seul_l_API_attend_l_assistant()
    {
        var (erreur, etat) = await AmorcerAsync(Avec(Code));

        Assert.Null(erreur);
        Assert.Empty(await EntreprisesAsync());
        Assert.Equal(0, await ComptesAsync());
        Assert.False(etat.EntrepriseOrpheline);
        Assert.False(etat.Installee);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Variables_presentes_l_amorcage_cree_l_entreprise_et_l_administrateur_provisoire(bool avecCode)
    {
        var (erreur, etat) = await AmorcerAsync(avecCode ? VariablesEt(Code) : VariablesEt());

        Assert.Null(erreur);
        Assert.True(etat.Installee);

        var entreprise = Assert.Single(await EntreprisesAsync());
        Assert.Equal("Gamma Propreté", entreprise.RaisonSociale);
        Assert.Equal("30000000000003", entreprise.Siret);

        await _fixture.AvecServicesAsync(async services =>
        {
            var utilisateurs = services.GetRequiredService<UserManager<Utilisateur>>();
            var compte = Assert.Single(await utilisateurs.Users.AsNoTracking().ToListAsync());

            Assert.Equal("patron@test.local", compte.Email);
            Assert.Equal(compte.Email, compte.UserName);
            Assert.Equal(entreprise.Id, compte.EntrepriseId);
            Assert.True(compte.MotDePasseProvisoire);
            Assert.True(compte.Actif);
            Assert.Equal(RolesKlean.Administrateur, Assert.Single(await utilisateurs.GetRolesAsync(compte)));
            Assert.True(await utilisateurs.CheckPasswordAsync(compte, "AmorcageTest2026"));
        });
    }

    [Fact]
    public async Task Variables_a_moitie_sans_code_le_demarrage_est_refuse()
    {
        var (erreur, _) = await AmorcerAsync(Avec(
            ("Amorcage:EmailAdmin", "patron@test.local"),
            ("Amorcage:RaisonSocialeEntreprise", "Gamma Propreté")));

        Assert.IsType<InvalidOperationException>(erreur);
        Assert.Contains("incomplet", erreur.Message);
        Assert.Empty(await EntreprisesAsync());
    }

    /// <summary>Le <c>.env</c> d'un serveur installé en 0.1.x : adresse et raison sociale restées, mot de passe vidé.</summary>
    [Fact]
    public async Task Variables_a_moitie_avec_code_l_API_attend_l_assistant()
    {
        var (erreur, etat) = await AmorcerAsync(Avec(
            ("Amorcage:EmailAdmin", "patron@test.local"),
            ("Amorcage:RaisonSocialeEntreprise", "Gamma Propreté"),
            ("Amorcage:MotDePasseAdmin", ""),
            Code));

        Assert.Null(erreur);
        Assert.Empty(await EntreprisesAsync());
        Assert.Equal(0, await ComptesAsync());
        Assert.False(etat.Installee);
    }

    [Fact]
    public async Task Un_administrateur_refuse_par_Identity_ne_laisse_aucune_entreprise()
    {
        var (erreur, etat) = await AmorcerAsync(VariablesEt(("Amorcage:MotDePasseAdmin", "court")));

        Assert.IsType<InvalidOperationException>(erreur);
        Assert.Contains(nameof(IssueInstallation.Refusee), erreur.Message);
        Assert.False(etat.Installee);
        Assert.Empty(await EntreprisesAsync());
        Assert.Equal(0, await ComptesAsync());
    }

    [Fact]
    public async Task Une_orpheline_et_des_variables_le_demarrage_est_refuse_avec_la_marche_a_suivre()
    {
        await PoserOrphelineAsync();

        var (erreur, _) = await AmorcerAsync(VariablesEt());

        Assert.IsType<InvalidOperationException>(erreur);
        Assert.Contains("DEPLOIEMENT.md", erreur.Message);
        Assert.Equal(EtatInstallation.MessageOrpheline, erreur.Message);
        Assert.Equal("Orpheline", Assert.Single(await EntreprisesAsync()).RaisonSociale);
        Assert.Equal(0, await ComptesAsync());
    }

    [Fact]
    public async Task Une_orpheline_et_le_code_l_etat_le_note()
    {
        await PoserOrphelineAsync();

        var (erreur, etat) = await AmorcerAsync(Avec(Code));

        Assert.Null(erreur);
        Assert.True(etat.EntrepriseOrpheline);
        Assert.False(etat.Installee);
        Assert.Single(await EntreprisesAsync());
    }

    [Fact]
    public async Task Avec_des_comptes_l_amorcage_ne_fait_rien_et_note_l_installation()
    {
        await _fixture.ReinitialiserAsync();

        var (erreur, etat) = await AmorcerAsync(Avec());

        Assert.Null(erreur);
        Assert.True(etat.Installee);
        Assert.False(etat.EntrepriseOrpheline);
        Assert.Equal(2, (await EntreprisesAsync()).Count);
        Assert.Equal(2, await ComptesAsync());
    }

    /// <summary>
    /// L'instance qui attend l'assistant n'a pas d'entreprise : la démonstration le dit, et
    /// nomme les deux façons d'en créer une, au lieu de parler d'« amorçage ».
    /// </summary>
    [Fact]
    public async Task Sans_entreprise_la_demonstration_nomme_l_assistant()
    {
        var sortie = new StringWriter();

        var code = await JeuDeDemonstration.ExecuterCommandeAsync(_fixture.Fabrique.Services, sortie);

        Assert.Equal(1, code);
        Assert.Contains("Créez d'abord l'entreprise", sortie.ToString());
        Assert.Contains("assistant d'installation", sortie.ToString());
        Assert.Empty(await EntreprisesAsync());
    }
}
