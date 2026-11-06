using System.Security.Claims;
using Klean.Api.Data;
using Klean.Api.Models;
using Klean.Api.Services;
using Klean.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Klean.Api.Tests;

/// <summary>
/// La sécurité au niveau des lignes : la base garde l'entreprise, quand bien même un filtre
/// global manquerait (SPEC.md, étape 26).
///
/// ⚠️ <b>Ces tests passent sous les filtres d'EF, exprès.</b> Le reste de la série éprouve les
/// filtres ; ceux-ci éprouvent ce qui reste quand un filtre est oublié. D'où
/// <c>IgnoreQueryFilters()</c>, interdit dans l'application (CLAUDE.md §11) et employé ici pour
/// simuler l'oubli, et du SQL écrit à la main pour passer sous le forçage d'<c>EntrepriseId</c>.
///
/// Le contexte est obtenu comme celui d'une requête : par injection, avec l'intercepteur, sous
/// un utilisateur dont le jeton porte une entreprise.
/// </summary>
[Collection(CollectionApi.Nom)]
public class SecuriteDesLignesTests : IAsyncLifetime
{
    private readonly FixtureApi _fixture;

    public SecuriteDesLignesTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();

        await _fixture.AvecServicesAsync(async services =>
        {
            var db = services.GetRequiredService<KleanDbContext>();

            db.Produits.AddRange(
                Produit(_fixture.EntrepriseAlpha, "Détergent Alpha"),
                Produit(_fixture.EntrepriseAlpha, "Javel Alpha"),
                Produit(_fixture.EntrepriseBeta, "Détergent Beta"));
            await db.SaveChangesAsync();
        });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------- l'API sous le rôle

    /// <summary>
    /// ⚠️ La garde la plus importante de la série : sans elle, retirer l'intercepteur rendrait
    /// toutes les politiques inertes en silence, l'API se connectant en superutilisateur.
    /// </summary>
    [Fact]
    public async Task Une_requete_lit_et_ecrit_sous_klean_app_avec_son_entreprise()
    {
        await SousEntrepriseAsync(_fixture.EntrepriseAlpha, async db =>
        {
            Assert.Equal(BasculeRoleApplicatif.Role, await Scalaire(db, "SELECT current_user::text"));
            Assert.Equal(_fixture.EntrepriseAlpha.ToString(),
                await Scalaire(db, $"SELECT current_setting('{BasculeRoleApplicatif.Reglage}')"));
        });
    }

    [Fact]
    public async Task Sous_DesactiverFiltrage_la_connexion_reste_au_proprietaire()
    {
        await _fixture.AvecServicesAsync(async services =>
        {
            var db = services.GetRequiredService<KleanDbContext>();

            Assert.NotEqual(BasculeRoleApplicatif.Role, await Scalaire(db, "SELECT current_user::text"));
            Assert.Equal(3, await db.Produits.CountAsync());
        });
    }

    // ------------------------------------------------------------- un filtre oublié

    [Fact]
    public async Task Un_filtre_oublie_ne_rend_que_l_entreprise_du_jeton()
    {
        await SousEntrepriseAsync(_fixture.EntrepriseAlpha, async db =>
        {
            var produits = await db.Produits.IgnoreQueryFilters().Select(p => p.Nom).ToListAsync();

            Assert.Equal(["Détergent Alpha", "Javel Alpha"], produits.Order());

            // L'entreprise elle-même, sur son `Id` : sans politique, la raison sociale et le
            // SIRET de toutes les entreprises de l'instance sortiraient.
            var entreprise = Assert.Single(await db.Entreprises.IgnoreQueryFilters().ToListAsync());
            Assert.Equal(_fixture.EntrepriseAlpha, entreprise.Id);
        });
    }

    /// <summary>
    /// Une requête sans entreprise — anonyme, ou du code hors requête — ne voit rien. C'est
    /// aussi ce qui ferme le trou de CLAUDE.md §7 : un <c>[Authorize]</c> retiré laissait les
    /// filtres d'EF tout passer, faute de tenant.
    /// </summary>
    [Fact]
    public async Task Sans_entreprise_la_base_ne_rend_aucune_ligne()
    {
        await SousEntrepriseAsync(null, async db =>
        {
            Assert.Equal(BasculeRoleApplicatif.Role, await Scalaire(db, "SELECT current_user::text"));
            Assert.Equal(0, await db.Produits.IgnoreQueryFilters().CountAsync());
            Assert.Equal(0, await db.Entreprises.IgnoreQueryFilters().CountAsync());
        });
    }

    // ------------------------------------------------------------- l'écriture

    /// <summary>
    /// Sous le forçage d'<c>EntrepriseId</c> du <c>DbContext</c> : une ligne qui atteindrait la base
    /// avec l'entreprise d'un autre est refusée par <c>WITH CHECK</c>.
    /// </summary>
    [Fact]
    public async Task Ecrire_une_ligne_pour_une_autre_entreprise_est_refuse()
    {
        await SousEntrepriseAsync(_fixture.EntrepriseAlpha, async db =>
        {
            var refus = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"""
                INSERT INTO "Produits" ("Id", "EntrepriseId", "Nom", "Unite", "SeuilAlerte", "Actif")
                VALUES ({Guid.NewGuid()}, {_fixture.EntrepriseBeta}, 'Intrus', 'L', 0, true)
                """));

            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refus.SqlState);
        });
    }

    [Fact]
    public async Task Deplacer_une_ligne_vers_une_autre_entreprise_est_refuse()
    {
        await SousEntrepriseAsync(_fixture.EntrepriseAlpha, async db =>
        {
            var refus = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"""
                UPDATE "Produits" SET "EntrepriseId" = {_fixture.EntrepriseBeta} WHERE "Nom" = 'Javel Alpha'
                """));

            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refus.SqlState);
        });
    }

    /// <summary>
    /// Les lignes d'une autre entreprise ne sont ni modifiables ni supprimables : elles
    /// n'existent pas pour la requête. Zéro ligne touchée, et elles sont intactes ensuite.
    /// </summary>
    [Fact]
    public async Task Les_lignes_d_une_autre_entreprise_ne_se_modifient_ni_ne_se_suppriment()
    {
        await SousEntrepriseAsync(_fixture.EntrepriseAlpha, async db =>
        {
            Assert.Equal(0, await db.Database.ExecuteSqlAsync(
                $"""UPDATE "Produits" SET "Nom" = 'Écrasé' WHERE "EntrepriseId" = {_fixture.EntrepriseBeta}"""));
            Assert.Equal(0, await db.Database.ExecuteSqlAsync(
                $"""DELETE FROM "Produits" WHERE "EntrepriseId" = {_fixture.EntrepriseBeta}"""));
        });

        await _fixture.AvecServicesAsync(async services =>
        {
            var db = services.GetRequiredService<KleanDbContext>();
            var beta = await db.Produits.SingleAsync(p => p.EntrepriseId == _fixture.EntrepriseBeta);

            Assert.Equal("Détergent Beta", beta.Nom);
        });
    }

    // ------------------------------------------------------------- le catalogue

    /// <summary>
    /// L'invariante, sur le modèle d'<c>IsolationTests</c> : toute table qui porte
    /// <c>EntrepriseId</c> a la RLS active et sa politique. Une table ajoutée demain sans elle
    /// fait rougir la série, puisque le filtre global ne suffit plus à la garder seul.
    ///
    /// ⚠️ <c>AspNetUsers</c> est la seule exemptée, et la raison est celle de CLAUDE.md §7 :
    /// Identity la lit avant toute authentification.
    /// </summary>
    [Fact]
    public async Task Toute_table_portant_EntrepriseId_est_sous_sa_politique()
    {
        var sansPolitique = await LireAsync(db => db.Database.SqlQueryRaw<string>("""
            SELECT c.relname AS "Value"
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = 'public'
            WHERE c.relkind = 'r'
              AND (EXISTS (SELECT 1 FROM information_schema.columns col
                           WHERE col.table_schema = 'public' AND col.table_name = c.relname
                             AND col.column_name = 'EntrepriseId')
                   OR c.relname = 'Entreprises')
              AND c.relname <> 'AspNetUsers'
              AND NOT (c.relrowsecurity
                       AND EXISTS (SELECT 1 FROM pg_policies p
                                   WHERE p.schemaname = 'public' AND p.tablename = c.relname
                                     AND p.qual LIKE '%klean.entreprise%'
                                     AND p.with_check LIKE '%klean.entreprise%'))
            """).ToListAsync());

        Assert.Empty(sansPolitique);

        // Et la liste n'est pas vide par accident : les vingt et une tables sont bien là.
        var protegees = await LireAsync(db => db.Database.SqlQueryRaw<string>("""
            SELECT tablename AS "Value" FROM pg_policies
            WHERE schemaname = 'public' AND policyname = 'entreprise'
            """).ToListAsync());

        Assert.Equal(21, protegees.Count);
    }

    /// <summary>
    /// Un rôle qui pourrait contourner la RLS, ou posséder une table, la rendrait décorative ;
    /// un rôle qui pourrait toucher la structure ne serait plus celui que le cadrage décrit.
    /// </summary>
    [Fact]
    public async Task Le_role_applicatif_ne_contourne_rien_et_ne_touche_pas_la_structure()
    {
        var role = await LireAsync(db => db.Database.SqlQueryRaw<string>("""
            SELECT concat_ws(',', rolsuper, rolbypassrls, rolcanlogin) AS "Value"
            FROM pg_roles WHERE rolname = 'klean_app'
            """).SingleAsync());

        // Des booléens en texte de PostgreSQL : `f` pour faux.
        Assert.Equal("f,f,f", role);

        var droits = await LireAsync(db => db.Database.SqlQueryRaw<string>("""
            SELECT concat_ws(',',
                (SELECT count(*) FROM pg_tables WHERE schemaname = 'public' AND tableowner = 'klean_app'),
                has_schema_privilege('klean_app', 'public', 'CREATE'),
                has_table_privilege('klean_app', '"Agents"', 'TRUNCATE'),
                has_table_privilege('klean_app', '"__EFMigrationsHistory"', 'SELECT')) AS "Value"
            """).SingleAsync());

        Assert.Equal("0,f,f,f", droits);
    }

    // ------------------------------------------------------------------------ outils

    private static Produit Produit(Guid entreprise, string nom) => new()
    {
        EntrepriseId = entreprise,
        Nom = nom,
        Unite = "L",
        SeuilAlerte = 0
    };

    /// <summary>
    /// Un contexte obtenu comme celui d'une requête authentifiée : par injection, sous un
    /// utilisateur dont le jeton porte cette entreprise — ou aucune.
    /// </summary>
    private async Task SousEntrepriseAsync(Guid? entreprise, Func<KleanDbContext, Task> action)
    {
        using var portee = _fixture.Fabrique.Services.CreateScope();
        var accesseur = portee.ServiceProvider.GetRequiredService<IHttpContextAccessor>();

        // Sans entreprise, un visiteur **anonyme** : un jeton authentifié sans entreprise est
        // refusé plus tôt par `TenantContext`, et c'est une autre garde.
        var identite = entreprise is null
            ? new ClaimsIdentity()
            : new ClaimsIdentity(
                [new Claim(JwtService.RevendicationEntrepriseId, entreprise.Value.ToString())], "Test");

        accesseur.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identite) };

        try
        {
            await action(portee.ServiceProvider.GetRequiredService<KleanDbContext>());
        }
        finally
        {
            accesseur.HttpContext = null;
        }
    }

    /// <summary>Au propriétaire : le catalogue se lit hors de la RLS.</summary>
    private async Task<T> LireAsync<T>(Func<KleanDbContext, Task<T>> lecture)
    {
        T resultat = default!;
        await _fixture.AvecServicesAsync(async services =>
            resultat = await lecture(services.GetRequiredService<KleanDbContext>()));
        return resultat;
    }

    private static Task<string> Scalaire(KleanDbContext db, string sql) =>
        db.Database.SqlQueryRaw<string>($"SELECT ({sql}) AS \"Value\"").SingleAsync();
}
