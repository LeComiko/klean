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
        // Les deux, et pas seulement le vidage : ReinitialiserAsync remet aussi à zéro le
        // limiteur de débit, les courriels retenus, l'horloge et les secrets retenus par la
        // fixture ; le vidage qui suit en retire les deux entreprises.
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
        // Un état propre au test, partagé par les deux services comme le singleton de l'hôte.
        var etat = new EtatInstallation();
        var db = services.GetRequiredService<KleanDbContext>();
        var tenant = services.GetRequiredService<ITenantContext>();
        var installation = new ServiceInstallation(
            db, services.GetRequiredService<UserManager<Utilisateur>>(), tenant, etat);
        var amorcage = new AmorcageService(
            db,
            services.GetRequiredService<RoleManager<IdentityRole<Guid>>>(),
            configuration,
            tenant,
            installation,
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

    /// <summary>
    /// La garde de <see cref="ServiceInstallation.CreerAsync"/> : sur une connexion déjà ouverte,
    /// le réglage de la RLS serait celui de l'ouverture, pas celui de la requête d'installation.
    /// </summary>
    [Fact]
    public async Task Une_connexion_deja_ouverte_est_refusee_sans_rien_ecrire()
    {
        using (var portee = _fixture.PorteeAuProprietaire())
        {
            var services = portee.ServiceProvider;
            var db = services.GetRequiredService<KleanDbContext>();
            await db.Database.OpenConnectionAsync();

            var erreur = await Record.ExceptionAsync(() => services.GetRequiredService<ServiceInstallation>()
                .CreerAsync(new NouvelleInstallation(
                    "Gamma Propreté", "30000000000003", Territoire.Metropole,
                    "patron@test.local", "Patron", "Gamma", "AmorcageTest2026", MotDePasseProvisoire: false),
                    CancellationToken.None));

            Assert.IsType<InvalidOperationException>(erreur);
            Assert.Contains("Connexion déjà ouverte", erreur.Message);
        }

        Assert.Empty(await EntreprisesAsync());
        Assert.Equal(0, await ComptesAsync());
    }

    /// <summary>
    /// Une sortie sans validation oublie ce qu'elle a suivi. Sur un SIRET pris, l'entreprise reste
    /// « Added » ; sur un refus d'Identity, elle reste suivie, enregistrée dans une transaction
    /// défaite : un SaveChanges ultérieur de la même portée travaillerait sur une ligne fantôme.
    /// </summary>
    [Theory]
    [InlineData("40000000000004", "AmorcageTest2026", IssueInstallation.SiretPris)]
    [InlineData("30000000000003", "court", IssueInstallation.Refusee)]
    public async Task Une_creation_refusee_ne_laisse_rien_au_suivi(string siret, string motDePasse, IssueInstallation attendue)
    {
        await PoserOrphelineAsync();

        using var portee = _fixture.PorteeAuProprietaire();
        var db = portee.ServiceProvider.GetRequiredService<KleanDbContext>();

        var resultat = await portee.ServiceProvider.GetRequiredService<ServiceInstallation>()
            .CreerAsync(new NouvelleInstallation(
                "Gamma Propreté", siret, Territoire.Metropole,
                "patron@test.local", "Patron", "Gamma", motDePasse, MotDePasseProvisoire: false),
                CancellationToken.None);

        Assert.Equal(attendue, resultat.Issue);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal("Orpheline", Assert.Single(await EntreprisesAsync()).RaisonSociale);
        Assert.Equal(0, await ComptesAsync());
    }

    /// <summary>
    /// Deux démarrages sur la même base vide : le second attend le verrou, relit les comptes, et
    /// trouve celui du premier. <c>DejaInstallee</c> est alors un succès, pas un refus de démarrer.
    ///
    /// La course est provoquée, pas espérée (CLAUDE.md §10) : le premier tient le verrou dans une
    /// transaction ouverte ; le test attend que l'amorçage y soit bloqué, puis valide.
    /// </summary>
    [Fact]
    public async Task Un_compte_cree_pendant_l_attente_du_verrou_vaut_installation()
    {
        using var premier = _fixture.PorteeAuProprietaire();
        var dbPremier = premier.ServiceProvider.GetRequiredService<KleanDbContext>();
        var utilisateurs = premier.ServiceProvider.GetRequiredService<UserManager<Utilisateur>>();

        await using var transaction = await dbPremier.Database.BeginTransactionAsync();
        await dbPremier.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({ServiceInstallation.EspaceVerrou}, 0)");

        var entreprise = new Entreprise { RaisonSociale = "Premier démarrage", Siret = "50000000000005" };
        dbPremier.Entreprises.Add(entreprise);
        await dbPremier.SaveChangesAsync();
        var compte = new Utilisateur
        {
            UserName = "premier@test.local", Email = "premier@test.local", EntrepriseId = entreprise.Id,
            Nom = "Premier", Prenom = "Démarrage", Actif = true
        };
        Assert.True((await utilisateurs.CreateAsync(compte, "AmorcageTest2026")).Succeeded);

        var amorcage = AmorcerAsync(VariablesEt());

        Assert.True(await AttendreBlocageSurVerrouAsync(), "L'amorçage n'a jamais attendu le verrou.");
        await transaction.CommitAsync();

        var (erreur, etat) = await amorcage;

        Assert.Null(erreur);
        Assert.True(etat.Installee);
        Assert.Equal("Premier démarrage", Assert.Single(await EntreprisesAsync()).RaisonSociale);
        Assert.Equal(1, await ComptesAsync());
    }

    /// <summary>Une autre session attend un verrou consultatif. Lu au propriétaire : sous klean_app, pg_stat_activity ne montre pas les autres.</summary>
    private async Task<bool> AttendreBlocageSurVerrouAsync()
    {
        using var portee = _fixture.PorteeAuProprietaire();
        var db = portee.ServiceProvider.GetRequiredService<KleanDbContext>();

        for (var essai = 0; essai < 100; essai++)
        {
            var attentes = await db.Database.SqlQueryRaw<int>("""
                SELECT count(*)::int AS "Value" FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event_type = 'Lock' AND wait_event = 'advisory'
                """).SingleAsync();
            if (attentes > 0) return true;
            await Task.Delay(100);
        }

        return false;
    }

    /// <summary>
    /// Après le vidage, toutes les tables sont vides, sauf deux. Une table sans clé étrangère vers
    /// celles du TRUNCATE échappe à la cascade : <c>AccesJournalises</c> l'a fait, et ses lignes
    /// passaient d'une série à l'autre. Une table ajoutée demain sans être nommée fait rougir ici.
    /// </summary>
    [Fact]
    public async Task Le_vidage_n_oublie_aucune_table()
    {
        // Gardées, et pourquoi :
        //  - __EFMigrationsHistory : le vider ferait rejouer toutes les migrations sur des tables qui existent ;
        //  - AspNetRoles : les cinq rôles, créés par l'amorçage au démarrage, ne changent jamais.
        string[] gardees = ["__EFMigrationsHistory", "AspNetRoles"];

        // Une base qui a servi : des comptes, une session, des lignes de journal.
        await _fixture.ReinitialiserAsync();
        var administrateur = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
        (await administrateur.GetAsync("/api/agents")).EnsureSuccessStatusCode();
        Assert.NotEqual(0, (await LignesParTableAsync())["AccesJournalises"]);
        var etat = _fixture.Fabrique.Services.GetRequiredService<EtatInstallation>();
        Assert.True(etat.Installee, "Deux comptes existent : l'API doit se savoir installée, comme après l'amorçage.");

        await _fixture.ViderAsync();
        Assert.False(etat.Installee);

        var restantes = (await LignesParTableAsync())
            .Where(t => !gardees.Contains(t.Key) && t.Value != 0)
            .Select(t => $"{t.Key} ({t.Value})")
            .ToList();
        Assert.Empty(restantes);
    }

    private async Task<Dictionary<string, long>> LignesParTableAsync()
    {
        using var portee = _fixture.PorteeAuProprietaire();
        var db = portee.ServiceProvider.GetRequiredService<KleanDbContext>();

        var tables = await db.Database.SqlQueryRaw<string>(
            "SELECT tablename AS \"Value\" FROM pg_tables WHERE schemaname = 'public'").ToListAsync();

        var lignes = new Dictionary<string, long>();
        foreach (var table in tables)
        {
            // Le nom vient du catalogue, pas d'une saisie : seul le guillemet est à doubler.
            var comptage = "SELECT count(*) AS \"Value\" FROM \"" + table.Replace("\"", "\"\"") + "\"";
            lignes[table] = await db.Database.SqlQueryRaw<long>(comptage).SingleAsync();
        }

        return lignes;
    }
}
