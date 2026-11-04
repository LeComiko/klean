using System.Net;
using System.Net.Http.Json;
using Klean.Api.Data;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Klean.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Klean.Api.Tests;

/// <summary>
/// Le stock — étape 22, lot B : la consommation par type, le besoin prévu, et les deux
/// alertes.
///
/// Trois choses à garder, et chacune a sa raison dans SPEC.md : le besoin ne compte que
/// ce qui <b>reste à faire</b> ; le stock d'un site ne couvre <b>que ce site</b> ; et une
/// vacation sans type ne consomme rien, mais la prévision le <b>dit</b>.
/// </summary>
[Collection(CollectionApi.Nom)]
public class PrevisionStockTests : IAsyncLifetime
{
    private const string Consommations = "/api/consommations-type";
    private const string Prevision = "/api/stocks/prevision";

    /// <summary>Un lundi : la prévision de quatre semaines court jusqu'au dimanche 20 juin inclus.</summary>
    private static readonly DateOnly Aujourdhui = new(2027, 5, 24);

    private readonly FixtureApi _fixture;
    private HttpClient _admin = null!;

    private Guid _detergent, _sacs, _hall, _parking, _depot, _siteA, _siteB;
    private Guid _hallA, _parkingB, _libreA;

    public PrevisionStockTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _fixture.Fabrique.Horloge.Fixer(Aujourdhui);
        _admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        _detergent = await Produit("Détergent sols", seuil: 5);
        _sacs = await Produit("Sacs 100 L", seuil: 0);
        _hall = await Type("Hall d'entrée");
        _parking = await Type("Parking");
        _depot = (await Creer<EmplacementDto>("/api/emplacements", new EmplacementCreateDto { Nom = "Dépôt", Type = "Depot" })).Id;

        var client = await Creer<ClientDto>("/api/clients", new ClientCreateDto { Nom = "Syndic", Type = "Syndic" });
        _siteA = await Site(client.Id, "Résidence A");
        _siteB = await Site(client.Id, "Résidence B");

        _hallA = await Prestation(_siteA, "Hall", _hall);
        _parkingB = await Prestation(_siteB, "Parking", _parking);
        _libreA = await Prestation(_siteA, "Vitres", type: null);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------- la consommation par type

    [Fact]
    public async Task Une_consommation_se_cree_se_relit_se_modifie_et_se_supprime()
    {
        var creee = await Consommer(_hall, _detergent, 0.5m);

        var relue = await _admin.GetFromJsonAsync<ConsommationTypeDto>($"{Consommations}/{creee.Id}");
        Assert.Equal((_hall, _detergent, 0.5m), (relue!.TypePrestationId, relue.ProduitId, relue.QuantiteParPassage));

        var modification = await _admin.PutAsJsonAsync($"{Consommations}/{creee.Id}", new ConsommationTypeCreateDto
        {
            TypePrestationId = _hall, ProduitId = _detergent, QuantiteParPassage = 0.75m
        });
        Assert.Equal(HttpStatusCode.NoContent, modification.StatusCode);
        Assert.Equal(0.75m, (await _admin.GetFromJsonAsync<ConsommationTypeDto>($"{Consommations}/{creee.Id}"))!.QuantiteParPassage);

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"{Consommations}/{creee.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"{Consommations}/{creee.Id}")).StatusCode);
    }

    [Fact]
    public async Task Le_filtre_par_type_s_applique_avant_le_comptage()
    {
        await Consommer(_hall, _detergent, 0.5m);
        await Consommer(_hall, _sacs, 2);
        await Consommer(_parking, _detergent, 1);

        var page = await _admin.GetFromJsonAsync<PageDto<ConsommationTypeDto>>($"{Consommations}?typePrestationId={_hall}");

        Assert.Equal(2, page!.Total);
        Assert.All(page.Elements, c => Assert.Equal(_hall, c.TypePrestationId));

        var parProduit = await _admin.ListerAsync<ConsommationTypeDto>($"{Consommations}?produitId={_sacs}");
        Assert.Equal([_hall], parProduit.Select(c => c.TypePrestationId));
    }

    [Fact]
    public async Task Un_type_ne_consomme_un_produit_qu_une_fois()
    {
        await Consommer(_hall, _detergent, 0.5m);

        var doublon = await _admin.PostAsJsonAsync(Consommations, Corps(_hall, _detergent, 1));

        Assert.Equal(HttpStatusCode.Conflict, doublon.StatusCode);
        Assert.Contains("modifier la ligne existante", await doublon.Content.ReadAsStringAsync());

        // Et la modification d'une autre ligne vers ce couple tombe sur le même refus.
        var autre = await Consommer(_hall, _sacs, 2);
        var deplacement = await _admin.PutAsJsonAsync($"{Consommations}/{autre.Id}", Corps(_hall, _detergent, 2));
        Assert.Equal(HttpStatusCode.Conflict, deplacement.StatusCode);
    }

    [Fact]
    public async Task Un_couple_ecrit_entre_la_verification_et_l_insertion_rend_409_et_non_500()
    {
        // ⚠️ La course est provoquée, pas espérée (CLAUDE.md §10) : la ligne concurrente est
        // insérée dans une transaction laissée ouverte, la vérification du contrôleur ne la
        // voit pas, son insertion attend sur l'index, et la validation la fait tomber.
        using var portee = _fixture.Fabrique.Services.CreateScope();
        var db = portee.ServiceProvider.GetRequiredService<KleanDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "ConsommationsType" ("Id", "EntrepriseId", "TypePrestationId", "ProduitId", "QuantiteParPassage")
            VALUES ({Guid.NewGuid()}, {_fixture.EntrepriseAlpha}, {_hall}, {_detergent}, 1)
            """);

        var creation = _admin.PostAsJsonAsync(Consommations, Corps(_hall, _detergent, 0.5m));

        await AttendreUneAttenteDeVerrouAsync(creation);
        await transaction.CommitAsync();

        var reponse = await creation;
        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        Assert.Contains("Recharger", await reponse.Content.ReadAsStringAsync());
        Assert.Single(await _admin.ListerAsync<ConsommationTypeDto>(Consommations));
    }

    [Fact]
    public async Task Un_type_ou_un_produit_d_une_autre_entreprise_est_inconnu()
    {
        var beta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);

        var typeAlpha = await beta.PostAsJsonAsync(Consommations, Corps(_hall, Guid.NewGuid(), 1));
        Assert.Equal(HttpStatusCode.BadRequest, typeAlpha.StatusCode);
        Assert.Contains($"Type de prestation {_hall} inconnu", await typeAlpha.Content.ReadAsStringAsync());

        var produitBeta = (await (await beta.PostAsJsonAsync("/api/produits", new ProduitCreateDto
        {
            Nom = "Détergent Beta", Unite = "litre"
        })).Content.ReadFromJsonAsync<ProduitDto>())!.Id;

        var produitDeBeta = await _admin.PostAsJsonAsync(Consommations, Corps(_hall, produitBeta, 1));
        Assert.Equal(HttpStatusCode.BadRequest, produitDeBeta.StatusCode);
        Assert.Contains($"Produit {produitBeta} inconnu", await produitDeBeta.Content.ReadAsStringAsync());

        await Consommer(_hall, _detergent, 0.5m);
        Assert.Empty(await beta.ListerAsync<ConsommationTypeDto>(Consommations));
    }

    [Fact]
    public async Task Un_produit_desactive_ne_s_attache_plus_mais_sa_ligne_se_corrige_encore()
    {
        var existante = await Consommer(_hall, _detergent, 0.5m);
        await Desactiver(_detergent, "Détergent sols");

        var nouvelle = await _admin.PostAsJsonAsync(Consommations, Corps(_parking, _detergent, 1));
        Assert.Equal(HttpStatusCode.BadRequest, nouvelle.StatusCode);
        Assert.Contains("désactivé : le réactiver", await nouvelle.Content.ReadAsStringAsync());

        var correction = await _admin.PutAsJsonAsync($"{Consommations}/{existante.Id}", Corps(_hall, _detergent, 0.6m));
        Assert.Equal(HttpStatusCode.NoContent, correction.StatusCode);
    }

    [Fact]
    public async Task Un_produit_ou_un_type_consomme_ne_se_supprime_pas_et_le_refus_dit_par_ou_passer()
    {
        var ligne = await Consommer(_parking, _sacs, 2);

        var produit = await _admin.DeleteAsync($"/api/produits/{_sacs}");
        Assert.Equal(HttpStatusCode.Conflict, produit.StatusCode);
        var messageProduit = await produit.Content.ReadAsStringAsync();
        Assert.Contains("1 consommation(s)", messageProduit);
        Assert.Contains("les retirer d'abord", messageProduit);

        // Le parking porte une prestation : c'est elle que le refus nomme d'abord.
        var typePorte = await _admin.DeleteAsync($"/api/types-prestation/{_parking}");
        Assert.Contains("1 prestation(s)", await typePorte.Content.ReadAsStringAsync());

        var vide = await Type("Local poubelles");
        await Consommer(vide, _sacs, 1);
        var type = await _admin.DeleteAsync($"/api/types-prestation/{vide}");
        Assert.Equal(HttpStatusCode.Conflict, type.StatusCode);
        Assert.Contains("1 consommation(s)", await type.Content.ReadAsStringAsync());

        // La sortie nommée est franchissable : les consommations retirées, le produit part.
        foreach (var c in await _admin.ListerAsync<ConsommationTypeDto>($"{Consommations}?produitId={_sacs}"))
        {
            await _admin.DeleteAsync($"{Consommations}/{c.Id}");
        }

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/produits/{_sacs}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/types-prestation/{vide}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"{Consommations}/{ligne.Id}")).StatusCode);
    }

    [Fact]
    public async Task La_base_refuse_une_consommation_nulle()
    {
        var refus = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            using var portee = _fixture.Fabrique.Services.CreateScope();
            await portee.ServiceProvider.GetRequiredService<KleanDbContext>().Database.ExecuteSqlAsync($"""
                INSERT INTO "ConsommationsType" ("Id", "EntrepriseId", "TypePrestationId", "ProduitId", "QuantiteParPassage")
                VALUES ({Guid.NewGuid()}, {_fixture.EntrepriseAlpha}, {_hall}, {_detergent}, 0)
                """);
        });

        Assert.Equal("CK_ConsommationsType_QuantitePositive", refus.ConstraintName);
    }

    // ------------------------------------------------------------- le besoin prévu

    [Fact]
    public async Task Le_besoin_ne_compte_que_les_vacations_a_venir_de_la_periode()
    {
        await Consommer(_hall, _detergent, 0.5m);

        await Vacation(_hallA, 0, StatutIntervention.Planifiee);    // aujourd'hui : compte
        await Vacation(_hallA, 3, StatutIntervention.EnCours);      // compte
        await Vacation(_hallA, 27, StatutIntervention.Planifiee);   // dernier jour : compte
        await Vacation(_hallA, 28, StatutIntervention.Planifiee);   // lendemain de la période
        await Vacation(_hallA, -1, StatutIntervention.Planifiee);   // hier
        await Vacation(_hallA, 5, StatutIntervention.Annulee);      // ne consommera pas
        await Vacation(_hallA, 0, StatutIntervention.Terminee);     // a déjà consommé

        var prevision = await LirePrevision();

        Assert.Equal((Utc(Aujourdhui), Utc(Aujourdhui.AddDays(27)), 4), (prevision.Du, prevision.Au, prevision.Semaines));
        Assert.Equal(1.5m, Ligne(prevision, _detergent).Besoin);
    }

    [Fact]
    public async Task La_periode_part_du_jour_du_territoire()
    {
        // Dimanche 23 mai, 23 h UTC : lundi 24 à Paris.
        _fixture.Fabrique.Horloge.Fixer(Aujourdhui.AddDays(-1), heure: 23);

        Assert.Equal(Utc(Aujourdhui), (await LirePrevision()).Du);
    }

    [Fact]
    public async Task Le_besoin_multiplie_les_passages_par_la_consommation_de_leur_type_site_par_site()
    {
        await Consommer(_hall, _detergent, 0.5m);
        await Consommer(_hall, _sacs, 2);
        await Consommer(_parking, _detergent, 1);

        await Vacation(_hallA, 1);
        await Vacation(_hallA, 8);
        await Vacation(_parkingB, 2);
        await Vacation(_parkingB, 9);
        await Vacation(_parkingB, 16);

        var prevision = await LirePrevision();

        var detergent = Ligne(prevision, _detergent);
        Assert.Equal(4m, detergent.Besoin);
        Assert.Equal(1m, detergent.Sites.Single(s => s.SiteId == _siteA).Besoin);
        Assert.Equal(3m, detergent.Sites.Single(s => s.SiteId == _siteB).Besoin);

        var sacs = Ligne(prevision, _sacs);
        Assert.Equal(4m, sacs.Besoin);
        Assert.Equal([_siteA], sacs.Sites.Select(s => s.SiteId));
    }

    [Fact]
    public async Task Une_vacation_sans_type_ne_consomme_rien_et_la_prevision_le_dit()
    {
        await Consommer(_hall, _detergent, 0.5m);

        await Vacation(_libreA, 1);
        await Vacation(_libreA, 2);
        await Vacation(_libreA, 30);    // hors période : pas comptée non plus
        await Vacation(_hallA, 1);

        var prevision = await LirePrevision();

        Assert.Equal(2, prevision.VacationsSansType);
        Assert.Equal(0.5m, Ligne(prevision, _detergent).Besoin);
    }

    [Fact]
    public async Task L_horizon_vaut_quatre_semaines_par_defaut_et_reste_dans_celui_de_la_generation()
    {
        await Consommer(_hall, _detergent, 1);
        await Vacation(_hallA, 40);

        Assert.Equal(0m, Ligne(await LirePrevision(), _detergent).Besoin);

        var douze = (await _admin.GetFromJsonAsync<PrevisionStockDto>($"{Prevision}?semaines=12"))!;
        Assert.Equal((12, Utc(Aujourdhui.AddDays(83))), (douze.Semaines, douze.Au));
        Assert.Equal(1m, Ligne(douze, _detergent).Besoin);

        foreach (var hors in new[] { 0, 13, -1 })
        {
            var refus = await _admin.GetAsync($"{Prevision}?semaines={hors}");
            Assert.Equal(HttpStatusCode.BadRequest, refus.StatusCode);
            Assert.Contains("1 à 12 semaines", await refus.Content.ReadAsStringAsync());
        }
    }

    // ------------------------------------------------------------- les alertes

    [Fact]
    public async Task Sous_le_seuil_se_lit_tous_lieux_confondus_et_strictement()
    {
        await Mouvement(Entree(_detergent, emplacement: _depot, quantite: 2));
        await Mouvement(Entree(_detergent, site: _siteA, quantite: 2));

        var avant = Ligne(await LirePrevision(), _detergent);
        Assert.Equal((4m, 2m, true), (avant.StockTotal, avant.StockReserve, avant.SousLeSeuil));

        await Mouvement(Entree(_detergent, site: _siteB, quantite: 1));

        // Au seuil, plus en dessous : le seuil est la quantité « sous laquelle » on alerte.
        Assert.False(Ligne(await LirePrevision(), _detergent).SousLeSeuil);

        // Un seuil à zéro n'alerte jamais, même sans stock.
        Assert.False(Ligne(await LirePrevision(), _sacs).SousLeSeuil);
    }

    [Fact]
    public async Task Le_stock_d_un_site_ne_couvre_que_son_site()
    {
        await Consommer(_hall, _detergent, 1);
        await Consommer(_parking, _detergent, 1);
        await Vacation(_hallA, 1);
        await Vacation(_hallA, 2);
        await Vacation(_parkingB, 1);
        await Vacation(_parkingB, 2);

        // Le site A a cinq litres pour deux de besoin ; le B n'a rien ; le dépôt, un litre.
        // Additionné, le stock vaudrait six pour quatre de besoin : il masquerait le manque de B.
        await Mouvement(Entree(_detergent, site: _siteA, quantite: 5));
        await Mouvement(Entree(_detergent, emplacement: _depot, quantite: 1));

        var ligne = Ligne(await LirePrevision(), _detergent);

        Assert.Equal((6m, 4m), (ligne.StockTotal, ligne.Besoin));
        Assert.Equal(0m, ligne.Sites.Single(s => s.SiteId == _siteA).AApporter);
        Assert.Equal(2m, ligne.Sites.Single(s => s.SiteId == _siteB).AApporter);
        Assert.Equal((1m, true), (ligne.Manque, ligne.CouvertureCourte));

        // La réserve comble le reste.
        await Mouvement(Entree(_detergent, emplacement: _depot, quantite: 1));

        var couverte = Ligne(await LirePrevision(), _detergent);
        Assert.Equal((0m, false), (couverte.Manque, couverte.CouvertureCourte));
    }

    [Fact]
    public async Task Les_produits_en_alerte_viennent_en_tete_et_un_produit_desactive_sort()
    {
        var abrasif = await Produit("Abrasif", seuil: 0);
        var retire = await Produit("Ancien désinfectant", seuil: 10);
        await Desactiver(retire, "Ancien désinfectant");

        await Consommer(_hall, _sacs, 1);
        await Vacation(_hallA, 1);

        var prevision = await LirePrevision();

        // Sacs : couverture courte ; détergent : sous le seuil ; abrasif : rien.
        Assert.Equal([_sacs, _detergent, abrasif], prevision.Produits.Select(p => p.ProduitId));
        Assert.DoesNotContain(prevision.Produits, p => p.ProduitId == retire);
    }

    [Fact]
    public async Task La_prevision_reste_dans_son_entreprise()
    {
        await Consommer(_hall, _detergent, 1);
        await Vacation(_hallA, 1);

        var beta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);
        var prevision = (await beta.GetFromJsonAsync<PrevisionStockDto>(Prevision))!;

        Assert.Empty(prevision.Produits);
        Assert.Equal(0, prevision.VacationsSansType);
    }

    [Fact]
    public async Task Le_chef_d_equipe_lit_les_consommations_mais_pas_la_prevision()
    {
        // ⚠️ Ses vacations sont filtrées sur son équipe, le stock non : sa prévision
        // comparerait le besoin d'une équipe au stock de toute l'entreprise.
        var chef = await _fixture.ClientDeRoleAsync(_admin, RolesKlean.ChefEquipe, "chef.stock@test.local");
        var responsable = await _fixture.ClientDeRoleAsync(_admin, RolesKlean.Responsable, "resp.stock@test.local");

        Assert.Equal(HttpStatusCode.Forbidden, (await chef.GetAsync(Prevision)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await chef.GetAsync(Consommations)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await chef.PostAsJsonAsync(Consommations, Corps(_hall, _detergent, 1))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await responsable.GetAsync(Prevision)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await responsable.PostAsJsonAsync(Consommations, Corps(_hall, _detergent, 1))).StatusCode);
    }

    // ------------------------------------------------------------- outils

    private static ConsommationTypeCreateDto Corps(Guid type, Guid produit, decimal quantite) => new()
    {
        TypePrestationId = type, ProduitId = produit, QuantiteParPassage = quantite
    };

    private async Task<ConsommationTypeDto> Consommer(Guid type, Guid produit, decimal quantite) =>
        await Creer<ConsommationTypeDto>(Consommations, Corps(type, produit, quantite));

    private async Task<PrevisionStockDto> LirePrevision() =>
        (await _admin.GetFromJsonAsync<PrevisionStockDto>(Prevision))!;

    private static PrevisionProduitDto Ligne(PrevisionStockDto prevision, Guid produit) =>
        prevision.Produits.Single(p => p.ProduitId == produit);

    private static DateTime Utc(DateOnly jour) =>
        DateTime.SpecifyKind(jour.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

    private async Task Vacation(Guid prestation, int dansJours, StatutIntervention statut = StatutIntervention.Planifiee) =>
        await Creer<InterventionDto>("/api/interventions", new InterventionCreateDto
        {
            PrestationId = prestation,
            DatePrevue = Utc(Aujourdhui.AddDays(dansJours)),
            HeurePrevue = new TimeSpan(8, 0, 0),
            Statut = statut.ToString()
        });

    private static MouvementStockCreateDto Entree(Guid produit, Guid? emplacement = null, Guid? site = null, decimal quantite = 1) => new()
    {
        ProduitId = produit, Nature = "Entree", DestinationEmplacementId = emplacement, DestinationSiteId = site, Quantite = quantite
    };

    private async Task Mouvement(MouvementStockCreateDto corps) =>
        await Creer<MouvementStockDto>("/api/mouvements-stock", corps);

    private async Task<Guid> Produit(string nom, decimal seuil) =>
        (await Creer<ProduitDto>("/api/produits", new ProduitCreateDto { Nom = nom, Unite = "litre", SeuilAlerte = seuil })).Id;

    private async Task Desactiver(Guid produit, string nom)
    {
        var reponse = await _admin.PutAsJsonAsync($"/api/produits/{produit}", new ProduitCreateDto
        {
            Nom = nom, Unite = "litre", SeuilAlerte = 10, Actif = false
        });

        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);
    }

    private async Task<Guid> Type(string libelle) =>
        (await Creer<TypePrestationDto>("/api/types-prestation", new TypePrestationCreateDto { Libelle = libelle, DureeParDefautMinutes = 30 })).Id;

    private async Task<Guid> Site(Guid client, string nom) =>
        (await Creer<SiteDto>("/api/sites", new SiteCreateDto
        {
            ClientId = client, Nom = nom, Adresse = "1 rue Haute", Latitude = 48.85, Longitude = 2.35
        })).Id;

    private async Task<Guid> Prestation(Guid site, string libelle, Guid? type)
    {
        var contrat = await Creer<ContratDto>("/api/contrats", new ContratCreateDto
        {
            SiteId = site, DateDebut = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), Frequence = "Récurrent", Actif = true
        });

        return (await Creer<PrestationDto>("/api/prestations", new PrestationCreateDto
        {
            ContratId = contrat.Id, Libelle = libelle, DureeEstimeeMinutes = 30, TypePrestationId = type
        })).Id;
    }

    private async Task<T> Creer<T>(string route, object corps)
    {
        var reponse = await _admin.PostAsJsonAsync(route, corps);

        Assert.True(reponse.StatusCode == HttpStatusCode.Created, $"{route} : {await reponse.Content.ReadAsStringAsync()}");

        return (await reponse.Content.ReadFromJsonAsync<T>())!;
    }

    /// <summary>
    /// Attend que la requête soit bloquée sur l'index. Échoue si elle finit avant : elle
    /// n'aurait donc pas attendu, et le test n'éprouverait rien.
    /// </summary>
    private async Task AttendreUneAttenteDeVerrouAsync(Task requete)
    {
        using var portee = _fixture.Fabrique.Services.CreateScope();
        var db = portee.ServiceProvider.GetRequiredService<KleanDbContext>();

        for (var essai = 0; essai < 200; essai++)
        {
            Assert.False(requete.IsCompleted, "La requête a fini sans attendre l'index.");

            var bloquees = await db.Database.SqlQuery<int>($"""
                SELECT count(*)::int AS "Value" FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event_type = 'Lock'
                """).SingleAsync();

            if (bloquees > 0) return;

            await Task.Delay(25);
        }

        Assert.Fail("Aucune insertion n'a attendu l'index en cinq secondes.");
    }
}
