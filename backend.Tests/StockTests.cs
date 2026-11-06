using System.Net;
using System.Net.Http.Json;
using Klean.Api.Data;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Klean.Api.Services;
using Klean.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Klean.Api.Tests;

/// <summary>
/// Le stock — étape 22, lot A : emplacements, mouvements, et la quantité qui n'en est
/// que la conséquence.
///
/// Trois choses à garder, et chacune a sa raison dans SPEC.md : un stock est en un lieu
/// <b>nommé</b> — emplacement ou site, jamais un Guid libre ; une quantité ne change que
/// par un <b>mouvement</b>, qui reste ; et un mouvement refusé n'écrit <b>rien</b>.
/// </summary>
[Collection(CollectionApi.Nom)]
public class StockTests : IAsyncLifetime
{
    private const string Mouvements = "/api/mouvements-stock";

    private readonly FixtureApi _fixture;
    private HttpClient _admin = null!;

    private Guid _detergent, _depot, _vehicule, _site;

    public StockTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        _detergent = await Produit(_admin, "Détergent sols");
        _depot = await Emplacement(_admin, "Dépôt central", "Depot");
        _vehicule = await Emplacement(_admin, "Kangoo AB-123-CD", "Vehicule");

        var client = await Creer<ClientDto>(_admin, "/api/clients", new ClientCreateDto { Nom = "Syndic", Type = "Syndic" });
        _site = (await Creer<SiteDto>(_admin, "/api/sites", new SiteCreateDto
        {
            ClientId = client.Id, Nom = "Résidence", Adresse = "1 rue Haute", Latitude = 48.85, Longitude = 2.35
        })).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------- les emplacements

    [Fact]
    public async Task Un_emplacement_se_relit_et_reste_dans_son_entreprise()
    {
        var relu = await _admin.GetFromJsonAsync<EmplacementDto>($"/api/emplacements/{_depot}");

        Assert.Equal(("Dépôt central", "Depot", true), (relu!.Nom, relu.Type, relu.Actif));

        var beta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);
        Assert.Empty(await beta.ListerAsync<EmplacementDto>("/api/emplacements"));
        Assert.Equal(HttpStatusCode.NotFound, (await beta.GetAsync($"/api/emplacements/{_depot}")).StatusCode);
    }

    [Fact]
    public async Task Un_site_n_est_pas_un_type_d_emplacement()
    {
        var reponse = await _admin.PostAsJsonAsync("/api/emplacements", new EmplacementCreateDto
        {
            Nom = "Résidence", Type = "Site"
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("vise le site lui-même", await reponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Un_emplacement_cite_ne_se_supprime_pas_et_le_refus_nomme_la_desactivation()
    {
        await Poster(Entree(_detergent, emplacement: _depot, quantite: 10));

        var reponse = await _admin.DeleteAsync($"/api/emplacements/{_depot}");

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        var message = await reponse.Content.ReadAsStringAsync();
        Assert.Contains("1 mouvement(s)", message);
        Assert.Contains("désactiver", message, StringComparison.OrdinalIgnoreCase);

        // Jamais cité : il se supprime.
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/emplacements/{_vehicule}")).StatusCode);
    }

    // ------------------------------------------------------------- les mouvements

    [Fact]
    public async Task Deux_entrees_au_meme_lieu_font_une_seule_ligne_de_stock()
    {
        await Poster(Entree(_detergent, emplacement: _depot, quantite: 10));
        await Poster(Entree(_detergent, emplacement: _depot, quantite: 5.5m));

        var lignes = await _admin.ListerAsync<StockDto>($"/api/stocks?produitId={_detergent}");

        var ligne = Assert.Single(lignes);
        Assert.Equal((_depot, (Guid?)null, 15.5m), (ligne.EmplacementId!.Value, ligne.SiteId, ligne.Quantite));
    }

    [Fact]
    public async Task Une_sortie_excessive_est_refusee_et_n_ecrit_rien()
    {
        await Poster(Entree(_detergent, emplacement: _depot, quantite: 10));
        await Poster(Sortie(_detergent, emplacement: _depot, quantite: 4));

        var refus = await _admin.PostAsJsonAsync(Mouvements, Sortie(_detergent, emplacement: _depot, quantite: 7));

        Assert.Equal(HttpStatusCode.BadRequest, refus.StatusCode);
        Assert.Contains("Stock insuffisant au départ : 6 disponible(s), 7 demandé(s)", await refus.Content.ReadAsStringAsync());
        Assert.Equal(6m, await Quantite(emplacement: _depot));
        Assert.Equal(2, (await _admin.ListerAsync<MouvementStockDto>(Mouvements)).Count);
    }

    [Fact]
    public async Task Une_sortie_d_un_lieu_jamais_approvisionne_est_refusee()
    {
        var refus = await _admin.PostAsJsonAsync(Mouvements, Sortie(_detergent, emplacement: _vehicule, quantite: 1));

        Assert.Equal(HttpStatusCode.BadRequest, refus.StatusCode);
        Assert.Contains("Stock insuffisant au départ : 0 disponible(s)", await refus.Content.ReadAsStringAsync());
        Assert.Empty(await _admin.ListerAsync<StockDto>("/api/stocks"));
    }

    [Fact]
    public async Task Un_transfert_deplace_du_depot_au_vehicule_puis_au_site()
    {
        await Poster(Entree(_detergent, emplacement: _depot, quantite: 10));

        await Poster(Transfert(_detergent, origine: (_depot, null), destination: (_vehicule, null), quantite: 4));
        await Poster(Transfert(_detergent, origine: (_vehicule, null), destination: (null, _site), quantite: 1.5m));

        Assert.Equal(6m, await Quantite(emplacement: _depot));
        Assert.Equal(2.5m, await Quantite(emplacement: _vehicule));
        Assert.Equal(1.5m, await Quantite(site: _site));
    }

    [Fact]
    public async Task Un_inventaire_fixe_la_quantite_et_garde_l_ecart()
    {
        await Poster(Entree(_detergent, emplacement: _depot, quantite: 10));

        var manque = await Poster(Inventaire(_detergent, emplacement: _depot, comptee: 7));
        Assert.Equal(-3m, manque.Ecart);
        Assert.Equal(7m, await Quantite(emplacement: _depot));

        // Un lieu jamais approvisionné : l'écart est tout ce qu'on a trouvé.
        var trouve = await Poster(Inventaire(_detergent, emplacement: _vehicule, comptee: 2));
        Assert.Equal(2m, trouve.Ecart);

        // Compter zéro est un inventaire légitime.
        var vide = await Poster(Inventaire(_detergent, emplacement: _depot, comptee: 0));
        Assert.Equal(-7m, vide.Ecart);
        Assert.Equal(0m, await Quantite(emplacement: _depot));
    }

    public static TheoryData<string, object, string> FormesRefusees => new()
    {
        { "nature numérique", new { Nature = "0", Quantite = 1, DestinationEmplacementId = Guid.Empty }, "invalide" },
        { "entrée avec origine", new { Nature = "Entree", Quantite = 1, OrigineEmplacementId = Guid.Empty, DestinationEmplacementId = Guid.Empty }, "n'a pas d'origine" },
        { "sortie sans origine", new { Nature = "Sortie", Quantite = 1 }, "l'origine est requise" },
        { "sortie avec destination", new { Nature = "Sortie", Quantite = 1, OrigineEmplacementId = Guid.Empty, DestinationEmplacementId = Guid.Empty }, "n'a pas de destination" },
        { "transfert sans destination", new { Nature = "Transfert", Quantite = 1, OrigineEmplacementId = Guid.Empty }, "la destination est requise" },
        { "inventaire sans lieu", new { Nature = "Inventaire", Quantite = 1 }, "la destination est requise" },
        { "emplacement et site", new { Nature = "Entree", Quantite = 1, DestinationEmplacementId = Guid.Empty, DestinationSiteId = Guid.Empty }, "pas les deux" },
        { "quantité nulle", new { Nature = "Entree", Quantite = 0, DestinationEmplacementId = Guid.Empty }, "positive" },
    };

    [Theory]
    [MemberData(nameof(FormesRefusees))]
    public async Task Les_lieux_exiges_dependent_de_la_nature(string cas, object forme, string attendu)
    {
        // Les Guid.Empty sont remplacés par de vrais lieux : le refus doit tenir à la
        // forme, pas à un lieu inconnu.
        var corps = System.Text.Json.JsonSerializer.SerializeToNode(forme)!.AsObject();
        corps["produitId"] = _detergent;
        foreach (var cle in new[] { "OrigineEmplacementId", "DestinationEmplacementId" })
        {
            if (corps.ContainsKey(cle)) corps[cle] = cle.StartsWith("Origine") ? _depot : _vehicule;
        }
        if (corps.ContainsKey("DestinationSiteId")) corps["DestinationSiteId"] = _site;

        var reponse = await _admin.PostAsJsonAsync(Mouvements, corps);

        Assert.True(reponse.StatusCode == HttpStatusCode.BadRequest, cas);
        Assert.Contains(attendu, await reponse.Content.ReadAsStringAsync());
        Assert.Empty(await _admin.ListerAsync<MouvementStockDto>(Mouvements));
    }

    [Fact]
    public async Task Un_transfert_vers_son_propre_lieu_est_refuse()
    {
        await Poster(Entree(_detergent, emplacement: _depot, quantite: 10));

        var reponse = await _admin.PostAsJsonAsync(Mouvements,
            Transfert(_detergent, origine: (_depot, null), destination: (_depot, null), quantite: 1));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("deux lieux différents", await reponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Un_produit_ou_un_lieu_d_une_autre_entreprise_est_inconnu()
    {
        var beta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);
        var produitBeta = await Produit(beta, "Produit de Beta");
        var depotBeta = await Emplacement(beta, "Dépôt de Beta", "Depot");

        var produit = await _admin.PostAsJsonAsync(Mouvements, Entree(produitBeta, emplacement: _depot, quantite: 1));
        var lieu = await _admin.PostAsJsonAsync(Mouvements, Entree(_detergent, emplacement: depotBeta, quantite: 1));

        Assert.Contains("inconnu", await produit.Content.ReadAsStringAsync());
        Assert.Contains("inconnu", await lieu.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, lieu.StatusCode);
    }

    [Fact]
    public async Task Un_emplacement_desactive_ne_recoit_plus_mais_se_vide_et_se_compte()
    {
        await Poster(Entree(_detergent, emplacement: _vehicule, quantite: 5));
        await _admin.PutAsJsonAsync($"/api/emplacements/{_vehicule}", new EmplacementCreateDto
        {
            Nom = "Kangoo AB-123-CD", Type = "Vehicule", Actif = false
        });

        var entree = await _admin.PostAsJsonAsync(Mouvements, Entree(_detergent, emplacement: _vehicule, quantite: 1));
        Assert.Equal(HttpStatusCode.BadRequest, entree.StatusCode);
        Assert.Contains("désactivé", await entree.Content.ReadAsStringAsync());

        await Poster(Inventaire(_detergent, emplacement: _vehicule, comptee: 4));
        await Poster(Transfert(_detergent, origine: (_vehicule, null), destination: (_depot, null), quantite: 4));

        Assert.Equal(0m, await Quantite(emplacement: _vehicule));
    }

    [Fact]
    public async Task Un_produit_desactive_ne_rentre_plus_mais_sort()
    {
        await Poster(Entree(_detergent, emplacement: _depot, quantite: 5));
        var desactivation = await _admin.PutAsJsonAsync($"/api/produits/{_detergent}", new ProduitCreateDto
        {
            Nom = "Détergent sols", Unite = "litre", SeuilAlerte = 2, Actif = false
        });
        Assert.Equal(HttpStatusCode.NoContent, desactivation.StatusCode);
        Assert.False((await _admin.GetFromJsonAsync<ProduitDto>($"/api/produits/{_detergent}"))!.Actif);

        var entree = await _admin.PostAsJsonAsync(Mouvements, Entree(_detergent, emplacement: _depot, quantite: 1));
        Assert.Equal(HttpStatusCode.BadRequest, entree.StatusCode);
        Assert.Contains("désactivé", await entree.Content.ReadAsStringAsync());

        await Poster(Sortie(_detergent, emplacement: _depot, quantite: 5));
    }

    [Fact]
    public async Task Le_serveur_pose_l_auteur_et_la_date()
    {
        var moi = (await _admin.GetFromJsonAsync<ProfilDto>("/api/auth/moi"))!.Id;
        var avant = DateTime.UtcNow.AddSeconds(-5);

        var mouvement = await Poster(Entree(_detergent, emplacement: _depot, quantite: 1));

        Assert.Equal(moi, mouvement.AuteurId);
        Assert.InRange(mouvement.DateMouvement, avant, DateTime.UtcNow.AddSeconds(5));
    }

    [Fact]
    public async Task Un_responsable_enregistre_un_mouvement()
    {
        var responsable = await _fixture.ClientDeRoleAsync(_admin, RolesKlean.Responsable, "resp.stock@test.local");

        var reponse = await responsable.PostAsJsonAsync(Mouvements, Entree(_detergent, emplacement: _depot, quantite: 3));

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
    }

    // ------------------------------------------------------------- ce qui ne s'écrit plus

    [Fact]
    public async Task Ni_un_mouvement_ni_une_quantite_ne_se_modifient_a_la_main()
    {
        var mouvement = await Poster(Entree(_detergent, emplacement: _depot, quantite: 10));
        var stock = Assert.Single(await _admin.ListerAsync<StockDto>("/api/stocks"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await _admin.PutAsJsonAsync($"{Mouvements}/{mouvement.Id}", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await _admin.DeleteAsync($"{Mouvements}/{mouvement.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await _admin.PostAsJsonAsync("/api/stocks", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await _admin.PutAsJsonAsync($"/api/stocks/{stock.Id}", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await _admin.DeleteAsync($"/api/stocks/{stock.Id}")).StatusCode);
    }

    [Fact]
    public async Task Le_stock_se_filtre_par_lieu_avant_le_comptage()
    {
        var autre = await Produit(_admin, "Sacs poubelle");
        await Poster(Entree(_detergent, emplacement: _depot, quantite: 10));
        await Poster(Entree(autre, emplacement: _depot, quantite: 100));
        await Poster(Transfert(_detergent, origine: (_depot, null), destination: (_vehicule, null), quantite: 2));

        var auDepot = await _admin.PageAsync<StockDto>($"/api/stocks?emplacementId={_depot}");
        var enVehicule = await _admin.PageAsync<StockDto>($"/api/stocks?emplacementId={_vehicule}");

        Assert.Equal(2, auDepot.Total);
        Assert.Equal(1, enVehicule.Total);
        Assert.Equal(0, (await _admin.PageAsync<StockDto>($"/api/stocks?siteId={_site}")).Total);
    }

    // ------------------------------------------------------------- ce qui ne se supprime plus

    [Fact]
    public async Task Un_produit_cite_ne_se_supprime_pas_et_le_refus_nomme_la_desactivation()
    {
        await Poster(Entree(_detergent, emplacement: _depot, quantite: 1));

        var reponse = await _admin.DeleteAsync($"/api/produits/{_detergent}");

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        var message = await reponse.Content.ReadAsStringAsync();
        Assert.Contains("1 mouvement(s)", message);
        Assert.Contains("désactiver", message, StringComparison.OrdinalIgnoreCase);

        var jamaisUtilise = await Produit(_admin, "Jamais utilisé");
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/produits/{jamaisUtilise}")).StatusCode);
    }

    [Fact]
    public async Task Un_site_cite_par_un_mouvement_ne_se_supprime_pas_meme_sans_contrat()
    {
        await Poster(Inventaire(_detergent, site: _site, comptee: 3));

        var reponse = await _admin.DeleteAsync($"/api/sites/{_site}");

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        var message = await reponse.Content.ReadAsStringAsync();
        Assert.Contains("1 mouvement(s)", message);
        // ⚠️ Pas de sortie illusoire : vider le stock ne débloquerait rien.
        Assert.DoesNotContain("Supprimer d'abord ses contrats", message);
    }

    // ------------------------------------------------------------- la concurrence et la base

    [Fact]
    public async Task Deux_sorties_simultanees_attendent_le_verrou_et_la_seconde_est_refusee()
    {
        // ⚠️ La course est provoquée, pas espérée (CLAUDE.md §10). Le test prend lui-même
        // le verrou du service : les deux sorties doivent l'attendre — sinon elles ne le
        // prennent pas, et le test échoue avant même de compter. Relâché, elles passent une
        // à une : la première prend 6 litres sur 10, la seconde n'en trouve plus que 4.
        await Poster(Entree(_detergent, emplacement: _depot, quantite: 10));

        using var portee = _fixture.PorteeAuProprietaire();
        var db = portee.ServiceProvider.GetRequiredService<KleanDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock({ServiceStock.EspaceVerrou}, {ServiceStock.CleDeVerrou(_fixture.EntrepriseAlpha)})");

        var sorties = Enumerable.Range(0, 2)
            .Select(_ => _admin.PostAsJsonAsync(Mouvements, Sortie(_detergent, emplacement: _depot, quantite: 6)))
            .ToArray();

        await AttendreDesSessionsBloqueesAsync(2, sorties);
        await transaction.CommitAsync();

        var reponses = await Task.WhenAll(sorties);

        Assert.Single(reponses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(reponses, r => r.StatusCode == HttpStatusCode.BadRequest);
        Assert.Equal(4m, await Quantite(emplacement: _depot));
    }

    [Fact]
    public async Task La_base_refuse_un_stock_sans_lieu_negatif_ou_en_double()
    {
        // Les gardes de dernier ressort, éprouvées sous le service : seule la base répond.
        await Poster(Entree(_detergent, emplacement: _depot, quantite: 10));
        var entreprise = _fixture.EntrepriseAlpha;

        var sansLieu = await Assert.ThrowsAsync<PostgresException>(() => SqlAsync(db => db.Database.ExecuteSqlAsync($"""
            INSERT INTO "Stocks" ("Id", "EntrepriseId", "ProduitId", "Quantite") VALUES ({Guid.NewGuid()}, {entreprise}, {_detergent}, 1)
            """)));
        Assert.Equal("CK_Stocks_UnLieu", sansLieu.ConstraintName);

        var deuxLieux = await Assert.ThrowsAsync<PostgresException>(() => SqlAsync(db => db.Database.ExecuteSqlAsync($"""
            INSERT INTO "Stocks" ("Id", "EntrepriseId", "ProduitId", "EmplacementId", "SiteId", "Quantite")
            VALUES ({Guid.NewGuid()}, {entreprise}, {_detergent}, {_vehicule}, {_site}, 1)
            """)));
        Assert.Equal("CK_Stocks_UnLieu", deuxLieux.ConstraintName);

        var negatif = await Assert.ThrowsAsync<PostgresException>(() => SqlAsync(db => db.Database.ExecuteSqlAsync($"""
            UPDATE "Stocks" SET "Quantite" = -1 WHERE "EmplacementId" = {_depot}
            """)));
        Assert.Equal("CK_Stocks_QuantitePositive", negatif.ConstraintName);

        var doublon = await Assert.ThrowsAsync<PostgresException>(() => SqlAsync(db => db.Database.ExecuteSqlAsync($"""
            INSERT INTO "Stocks" ("Id", "EntrepriseId", "ProduitId", "EmplacementId", "Quantite")
            VALUES ({Guid.NewGuid()}, {entreprise}, {_detergent}, {_depot}, 1)
            """)));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, doublon.SqlState);
    }

    // ------------------------------------------------------------- outils

    private static object Entree(Guid produit, Guid? emplacement = null, Guid? site = null, decimal quantite = 1) => new MouvementStockCreateDto
    {
        ProduitId = produit, Nature = "Entree", DestinationEmplacementId = emplacement, DestinationSiteId = site, Quantite = quantite
    };

    private static object Sortie(Guid produit, Guid? emplacement = null, Guid? site = null, decimal quantite = 1) => new MouvementStockCreateDto
    {
        ProduitId = produit, Nature = "Sortie", OrigineEmplacementId = emplacement, OrigineSiteId = site, Quantite = quantite
    };

    private static object Transfert(Guid produit, (Guid? Emplacement, Guid? Site) origine, (Guid? Emplacement, Guid? Site) destination, decimal quantite) =>
        new MouvementStockCreateDto
        {
            ProduitId = produit, Nature = "Transfert",
            OrigineEmplacementId = origine.Emplacement, OrigineSiteId = origine.Site,
            DestinationEmplacementId = destination.Emplacement, DestinationSiteId = destination.Site,
            Quantite = quantite
        };

    private static object Inventaire(Guid produit, Guid? emplacement = null, Guid? site = null, decimal comptee = 0) => new MouvementStockCreateDto
    {
        ProduitId = produit, Nature = "Inventaire", DestinationEmplacementId = emplacement, DestinationSiteId = site, Quantite = comptee
    };

    private async Task<MouvementStockDto> Poster(object corps)
    {
        var reponse = await _admin.PostAsJsonAsync(Mouvements, corps);

        Assert.True(reponse.StatusCode == HttpStatusCode.Created, await reponse.Content.ReadAsStringAsync());

        return (await reponse.Content.ReadFromJsonAsync<MouvementStockDto>())!;
    }

    private async Task<decimal> Quantite(Guid? emplacement = null, Guid? site = null)
    {
        var filtre = emplacement is not null ? $"emplacementId={emplacement}" : $"siteId={site}";
        var lignes = await _admin.ListerAsync<StockDto>($"/api/stocks?produitId={_detergent}&{filtre}");

        return lignes.SingleOrDefault()?.Quantite ?? 0m;
    }

    private static async Task<Guid> Produit(HttpClient client, string nom) =>
        (await Creer<ProduitDto>(client, "/api/produits", new ProduitCreateDto { Nom = nom, Unite = "litre", SeuilAlerte = 2 })).Id;

    private static async Task<Guid> Emplacement(HttpClient client, string nom, string type) =>
        (await Creer<EmplacementDto>(client, "/api/emplacements", new EmplacementCreateDto { Nom = nom, Type = type })).Id;

    private static async Task<T> Creer<T>(HttpClient client, string route, object corps)
    {
        var reponse = await client.PostAsJsonAsync(route, corps);

        Assert.True(reponse.StatusCode == HttpStatusCode.Created, $"{route} : {await reponse.Content.ReadAsStringAsync()}");

        return (await reponse.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task SqlAsync(Func<KleanDbContext, Task<int>> requete)
    {
        using var portee = _fixture.PorteeAuProprietaire();
        await requete(portee.ServiceProvider.GetRequiredService<KleanDbContext>());
    }

    /// <summary>
    /// Attend que <paramref name="combien"/> sessions soient bloquées sur un verrou. Échoue
    /// si une requête finit avant : elle n'a donc pas attendu, et le test n'éprouverait rien.
    /// </summary>
    private async Task AttendreDesSessionsBloqueesAsync(int combien, Task[] requetes)
    {
        using var portee = _fixture.PorteeAuProprietaire();
        var db = portee.ServiceProvider.GetRequiredService<KleanDbContext>();

        for (var essai = 0; essai < 200; essai++)
        {
            Assert.False(requetes.Any(r => r.IsCompleted), "Une sortie a fini sans attendre le verrou.");

            var bloquees = await db.Database.SqlQuery<int>($"""
                SELECT count(*)::int AS "Value" FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event_type = 'Lock'
                """).SingleAsync();

            if (bloquees >= combien) return;

            await Task.Delay(25);
        }

        Assert.Fail($"Moins de {combien} sessions bloquées sur le verrou en cinq secondes.");
    }
}
