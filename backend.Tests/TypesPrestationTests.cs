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
/// Le catalogue des prestations — étape 21, lots A et B.
///
/// Trois choses à garder, et chacune a sa raison dans SPEC.md : le catalogue est
/// <b>par entreprise</b> ; le libellé d'une prestation est une <b>copie</b>, qu'un type
/// renommé ne réécrit pas ; un type utilisé se retire par <b>désactivation</b>, sa
/// suppression étant refusée.
/// </summary>
[Collection(CollectionApi.Nom)]
public class TypesPrestationTests : IAsyncLifetime
{
    private const string Route = "/api/types-prestation";
    private const string RouteImport = "/api/types-prestation/importer-liste-type";

    private readonly FixtureApi _fixture;
    private HttpClient _admin = null!;
    private Guid _contrat;

    public TypesPrestationTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        var client = await Creer<ClientDto>("/api/clients", new ClientCreateDto { Nom = "Syndic", Type = "Syndic" });
        var site = await Creer<SiteDto>("/api/sites", new SiteCreateDto
        {
            ClientId = client.Id, Nom = "Résidence", Adresse = "1 rue Haute", Latitude = 48.85, Longitude = 2.35
        });
        _contrat = (await Creer<ContratDto>("/api/contrats", new ContratCreateDto
        {
            SiteId = site.Id, DateDebut = new DateTime(2026, 1, 1), Frequence = "Hebdomadaire"
        })).Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------- le catalogue

    [Fact]
    public async Task Un_type_se_cree_et_se_relit()
    {
        var cree = await Type("Hall d'entrée", 30);

        var relu = await _admin.GetFromJsonAsync<TypePrestationDto>($"{Route}/{cree.Id}");

        Assert.Equal("Hall d'entrée", relu!.Libelle);
        Assert.Equal(30, relu.DureeParDefautMinutes);
        Assert.True(relu.Actif);
        Assert.Equal(_fixture.EntrepriseAlpha, relu.EntrepriseId);
    }

    [Fact]
    public async Task Le_catalogue_est_propre_a_chaque_entreprise()
    {
        var alpha = await Type("Hall d'entrée", 30);

        var beta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);

        Assert.Empty(await beta.ListerAsync<TypePrestationDto>(Route));
        Assert.Equal(HttpStatusCode.NotFound, (await beta.GetAsync($"{Route}/{alpha.Id}")).StatusCode);

        // Le même libellé est libre chez Beta : l'unicité est par entreprise.
        var chezBeta = await beta.PostAsJsonAsync(Route, new TypePrestationCreateDto
        {
            Libelle = "Hall d'entrée", DureeParDefautMinutes = 45
        });
        Assert.Equal(HttpStatusCode.Created, chezBeta.StatusCode);
    }

    [Fact]
    public async Task Un_libelle_deja_pris_est_refuse_a_la_casse_pres()
    {
        await Type("Hall d'entrée", 30);

        var doublon = await _admin.PostAsJsonAsync(Route, new TypePrestationCreateDto
        {
            Libelle = "  HALL D'ENTRÉE ", DureeParDefautMinutes = 20
        });

        Assert.Equal(HttpStatusCode.Conflict, doublon.StatusCode);
        Assert.Contains("existe déjà", await doublon.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Un_libelle_pris_par_un_type_desactive_dit_de_le_reactiver()
    {
        var type = await Type("Vitres", 60);
        await Modifier(type.Id, "Vitres", 60, actif: false);

        var doublon = await _admin.PostAsJsonAsync(Route, new TypePrestationCreateDto
        {
            Libelle = "vitres", DureeParDefautMinutes = 60
        });

        Assert.Equal(HttpStatusCode.Conflict, doublon.StatusCode);
        Assert.Contains("réactiver", await doublon.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Renommer_un_type_en_un_libelle_pris_est_refuse()
    {
        await Type("Hall d'entrée", 30);
        var escaliers = await Type("Escaliers", 45);

        var reponse = await _admin.PutAsJsonAsync($"{Route}/{escaliers.Id}", new TypePrestationCreateDto
        {
            Libelle = "hall d'entrée", DureeParDefautMinutes = 45
        });

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);

        // Le garder sous son propre nom n'est pas un doublon.
        Assert.Equal(HttpStatusCode.NoContent, (await Modifier(escaliers.Id, "Escaliers", 50, actif: true)).StatusCode);
    }

    [Fact]
    public async Task La_duree_par_defaut_a_les_bornes_de_celle_d_une_prestation()
    {
        var nulle = await _admin.PostAsJsonAsync(Route, new TypePrestationCreateDto
        {
            Libelle = "Hall", DureeParDefautMinutes = 0
        });

        Assert.Equal(HttpStatusCode.BadRequest, nulle.StatusCode);
    }

    [Theory]
    [InlineData(RolesKlean.Responsable, "resp@test.local")]
    [InlineData(RolesKlean.ChefEquipe, "chef@test.local")]
    public async Task Les_roles_internes_lisent_le_catalogue_sans_l_ecrire(string role, string email)
    {
        await Type("Hall d'entrée", 30);
        var client = await _fixture.ClientDeRoleAsync(_admin, role, email);

        Assert.Single(await client.ListerAsync<TypePrestationDto>(Route));

        var creation = await client.PostAsJsonAsync(Route, new { });
        Assert.Equal(HttpStatusCode.Forbidden, creation.StatusCode);
    }

    // ------------------------------------------------------------- le lien depuis la prestation

    [Fact]
    public async Task Une_prestation_garde_son_type()
    {
        var type = await Type("Hall d'entrée", 30);

        var prestation = await Prestation("Hall + escalier B", type.Id);

        Assert.Equal(type.Id, prestation.TypePrestationId);
        Assert.Equal(type.Id, (await _admin.GetFromJsonAsync<PrestationDto>($"/api/prestations/{prestation.Id}"))!.TypePrestationId);
    }

    [Fact]
    public async Task Une_prestation_change_de_type_ou_le_perd()
    {
        var hall = await Type("Hall d'entrée", 30);
        var escaliers = await Type("Escaliers", 45);
        var prestation = await Prestation("Parties communes", hall.Id);

        await _admin.PutAsJsonAsync($"/api/prestations/{prestation.Id}", Corps("Parties communes", escaliers.Id));
        Assert.Equal(escaliers.Id, (await _admin.GetFromJsonAsync<PrestationDto>($"/api/prestations/{prestation.Id}"))!.TypePrestationId);

        await _admin.PutAsJsonAsync($"/api/prestations/{prestation.Id}", Corps("Parties communes", null));
        Assert.Null((await _admin.GetFromJsonAsync<PrestationDto>($"/api/prestations/{prestation.Id}"))!.TypePrestationId);
    }

    [Fact]
    public async Task Renommer_un_type_ne_reecrit_pas_le_libelle_des_prestations()
    {
        var type = await Type("Hall d'entrée", 30);
        var prestation = await Prestation("Hall d'entrée", type.Id);

        await Modifier(type.Id, "Hall et sas", 30, actif: true);

        var relue = await _admin.GetFromJsonAsync<PrestationDto>($"/api/prestations/{prestation.Id}");
        Assert.Equal("Hall d'entrée", relue!.Libelle);
    }

    [Fact]
    public async Task Le_type_d_une_autre_entreprise_est_inconnu()
    {
        var beta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);
        var chezBeta = await (await beta.PostAsJsonAsync(Route, new TypePrestationCreateDto
        {
            Libelle = "Hall de Beta", DureeParDefautMinutes = 30
        })).Content.ReadFromJsonAsync<TypePrestationDto>();

        var reponse = await _admin.PostAsJsonAsync("/api/prestations", Corps("Hall", chezBeta!.Id));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("inconnu", await reponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Un_type_desactive_ne_se_choisit_plus()
    {
        var type = await Type("Vitres", 60);
        await Modifier(type.Id, "Vitres", 60, actif: false);

        var creation = await _admin.PostAsJsonAsync("/api/prestations", Corps("Vitres", type.Id));
        Assert.Equal(HttpStatusCode.BadRequest, creation.StatusCode);
        Assert.Contains("désactivé", await creation.Content.ReadAsStringAsync());

        // Ni sur une prestation existante qui ne le portait pas.
        var autre = await Prestation("Vitres du hall", null);
        var modification = await _admin.PutAsJsonAsync($"/api/prestations/{autre.Id}", Corps("Vitres du hall", type.Id));
        Assert.Equal(HttpStatusCode.BadRequest, modification.StatusCode);
    }

    [Fact]
    public async Task Une_prestation_garde_son_type_desactive_a_l_enregistrement()
    {
        // ⚠️ LE cas de la désactivation. Refuser ici obligerait à retirer son type à une
        // prestation pour en changer l'heure — et le stock comme le devis perdraient le
        // lien que la désactivation existe pour garder.
        var type = await Type("Vitres", 60);
        var prestation = await Prestation("Vitres", type.Id);
        await Modifier(type.Id, "Vitres", 60, actif: false);

        var reponse = await _admin.PutAsJsonAsync($"/api/prestations/{prestation.Id}", Corps("Vitres, côté cour", type.Id));

        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);
        Assert.Equal(type.Id, (await _admin.GetFromJsonAsync<PrestationDto>($"/api/prestations/{prestation.Id}"))!.TypePrestationId);
    }

    // ------------------------------------------------------------- le retrait

    [Fact]
    public async Task Un_type_jamais_utilise_se_supprime()
    {
        var type = await Type("Vitres", 60);

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"{Route}/{type.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"{Route}/{type.Id}")).StatusCode);
    }

    [Fact]
    public async Task Un_type_utilise_ne_se_supprime_pas_et_le_refus_nomme_la_desactivation()
    {
        var type = await Type("Hall d'entrée", 30);
        var prestation = await Prestation("Hall", type.Id);

        var reponse = await _admin.DeleteAsync($"{Route}/{type.Id}");

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        var message = await reponse.Content.ReadAsStringAsync();
        Assert.Contains("1 prestation(s)", message);
        Assert.Contains("désactiver", message, StringComparison.OrdinalIgnoreCase);

        // La sortie nommée est franchissable, et la prestation garde son lien.
        Assert.Equal(HttpStatusCode.NoContent, (await Modifier(type.Id, "Hall d'entrée", 30, actif: false)).StatusCode);
        Assert.Equal(type.Id, (await _admin.GetFromJsonAsync<PrestationDto>($"/api/prestations/{prestation.Id}"))!.TypePrestationId);
    }

    [Fact]
    public async Task Un_libelle_ecrit_entre_la_verification_et_l_insertion_rend_409_et_non_500()
    {
        // ⚠️ La course est provoquée, pas espérée. Huit créations simultanées ne se
        // chevauchaient qu'à froid : dans la série, le processus chaud, la première finissait
        // avant que les autres aient lu, et le test restait vert la conversion retirée
        // (CLAUDE.md §10). Ici, « Parking » est inséré dans une transaction laissée ouverte :
        // la vérification du contrôleur ne le voit pas, son insertion attend sur l'index, et
        // la validation la fait tomber sur la violation d'unicité.
        using var portee = _fixture.Fabrique.Services.CreateScope();
        var db = portee.ServiceProvider.GetRequiredService<KleanDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "TypesPrestation" ("Id", "EntrepriseId", "Libelle", "DureeParDefautMinutes", "Actif")
            VALUES ({Guid.NewGuid()}, {_fixture.EntrepriseAlpha}, 'PARKING', 60, true)
            """);

        var creation = _admin.PostAsJsonAsync(Route, new TypePrestationCreateDto
        {
            Libelle = "Parking", DureeParDefautMinutes = 60
        });

        await AttendreUneAttenteDeVerrouAsync(creation);
        await transaction.CommitAsync();

        var reponse = await creation;
        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        Assert.Contains("Recharger", await reponse.Content.ReadAsStringAsync());
        Assert.Equal(1, await CompterEnBaseAsync("parking"));
    }

    [Fact]
    public async Task L_index_refuse_un_doublon_ecrit_hors_de_l_api()
    {
        // La garde de dernier ressort, éprouvée sous le contrôleur : sans l'index, ce
        // doublon passerait, et le test de concurrence ci-dessus ne tiendrait plus.
        await Type("Hall d'entrée", 30);

        var refus = await Assert.ThrowsAsync<PostgresException>(() =>
            InsererEnBaseAsync(_fixture.EntrepriseAlpha, "HALL D'ENTRÉE"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, refus.SqlState);

        // Par entreprise : le même libellé reste libre ailleurs.
        await InsererEnBaseAsync(_fixture.EntrepriseBeta, "HALL D'ENTRÉE");
    }

    // ------------------------------------------------------------- l'import de la liste type

    [Fact]
    public async Task L_import_cree_toute_la_liste_type()
    {
        var compteRendu = await Importer(_admin);

        Assert.Equal(ListeTypePrestations.Elements.Select(e => e.Libelle), compteRendu.Crees);
        Assert.Empty(compteRendu.DejaPresents);

        var catalogue = await _admin.ListerAsync<TypePrestationDto>(Route);
        Assert.Equal(ListeTypePrestations.Elements.Count, catalogue.Count);
        Assert.All(catalogue, t => Assert.True(t.Actif));
        Assert.Equal(180, catalogue.Single(t => t.Libelle == "Remise en état").DureeParDefautMinutes);
    }

    [Fact]
    public async Task Rejouer_l_import_ne_recree_rien()
    {
        await Importer(_admin);

        var second = await Importer(_admin);

        Assert.Empty(second.Crees);
        Assert.Equal(ListeTypePrestations.Elements.Select(e => e.Libelle), second.DejaPresents);
        Assert.Equal(ListeTypePrestations.Elements.Count, (await _admin.ListerAsync<TypePrestationDto>(Route)).Count);
    }

    [Fact]
    public async Task L_import_n_ecrase_ni_une_saisie_ni_une_correction_ni_une_desactivation()
    {
        // Saisi avant l'import, sous une autre casse et une autre durée.
        var saisi = await Type("hall d'entrée", 45);

        var premier = await Importer(_admin);
        Assert.Contains("Hall d'entrée", premier.DejaPresents);
        Assert.DoesNotContain("Hall d'entrée", premier.Crees);

        var catalogue = await _admin.ListerAsync<TypePrestationDto>(Route);
        var parking = catalogue.Single(t => t.Libelle == "Parking");
        var ascenseur = catalogue.Single(t => t.Libelle == "Ascenseur");
        await Modifier(parking.Id, "Parking", 90, actif: true);
        await Modifier(ascenseur.Id, "Ascenseur", 10, actif: false);

        var second = await Importer(_admin);
        Assert.Empty(second.Crees);

        var relu = await _admin.ListerAsync<TypePrestationDto>(Route);
        Assert.Equal(ListeTypePrestations.Elements.Count, relu.Count);
        Assert.Equal(("hall d'entrée", 45), Resume(relu.Single(t => t.Id == saisi.Id)));
        Assert.Equal(90, relu.Single(t => t.Id == parking.Id).DureeParDefautMinutes);
        Assert.False(relu.Single(t => t.Id == ascenseur.Id).Actif);
    }

    [Fact]
    public async Task L_import_est_propre_a_chaque_entreprise()
    {
        await Importer(_admin);

        var beta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);
        Assert.Empty(await beta.ListerAsync<TypePrestationDto>(Route));

        // Alpha a déjà tout : Beta crée quand même toute la liste chez elle.
        Assert.Equal(ListeTypePrestations.Elements.Count, (await Importer(beta)).Crees.Count);
    }

    [Fact]
    public async Task Deux_imports_simultanes_ne_dupliquent_rien_et_n_echouent_pas()
    {
        // Le verrou sérialise : chaque import lit ce que le précédent a écrit. Sans lui,
        // l'index empêcherait le doublon mais l'un des imports échouerait en 500. Six
        // requêtes : à deux, la première peut finir avant que la seconde ait lu.
        var reponses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => _admin.PostAsync(RouteImport, null)));

        var crees = 0;
        foreach (var reponse in reponses)
        {
            Assert.True(reponse.StatusCode == HttpStatusCode.OK, await reponse.Content.ReadAsStringAsync());
            crees += (await reponse.Content.ReadFromJsonAsync<ImportListeTypeDto>())!.Crees.Count;
        }

        Assert.Equal(ListeTypePrestations.Elements.Count, crees);
        Assert.Equal(1, await CompterEnBaseAsync("parking"));
    }

    [Theory]
    [InlineData(RolesKlean.Responsable, "resp@test.local")]
    [InlineData(RolesKlean.ChefEquipe, "chef@test.local")]
    public async Task Seul_l_administrateur_importe(string role, string email)
    {
        var client = await _fixture.ClientDeRoleAsync(_admin, role, email);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(RouteImport, null)).StatusCode);
        Assert.Empty(await _admin.ListerAsync<TypePrestationDto>(Route));
    }

    // ------------------------------------------------------------- outils

    private static async Task<ImportListeTypeDto> Importer(HttpClient client)
    {
        var reponse = await client.PostAsync(RouteImport, null);

        Assert.True(reponse.StatusCode == HttpStatusCode.OK, await reponse.Content.ReadAsStringAsync());

        return (await reponse.Content.ReadFromJsonAsync<ImportListeTypeDto>())!;
    }

    private static (string, int) Resume(TypePrestationDto t) => (t.Libelle, t.DureeParDefautMinutes);

    /// <summary>Lu en base, toutes entreprises : la vérité, pas ce que l'API veut bien montrer.</summary>
    private async Task<int> CompterEnBaseAsync(string libelleEnMinuscules)
    {
        using var portee = _fixture.Fabrique.Services.CreateScope();
        var db = portee.ServiceProvider.GetRequiredService<KleanDbContext>();

        return await db.TypesPrestation.CountAsync(t => t.Libelle.ToLower() == libelleEnMinuscules);
    }

    /// <summary>
    /// Attend qu'une session de la base soit bloquée sur un verrou — l'insertion de la
    /// requête en cours. Échoue si la requête a fini avant : elle n'a donc pas attendu, et
    /// le test n'éprouverait rien.
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

    /// <summary>Écrit sous le contrôleur et sous <c>SaveChangesAsync</c> : seule la base répond.</summary>
    private async Task InsererEnBaseAsync(Guid entrepriseId, string libelle)
    {
        using var portee = _fixture.Fabrique.Services.CreateScope();
        var db = portee.ServiceProvider.GetRequiredService<KleanDbContext>();

        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "TypesPrestation" ("Id", "EntrepriseId", "Libelle", "DureeParDefautMinutes", "Actif")
            VALUES ({Guid.NewGuid()}, {entrepriseId}, {libelle}, 30, true)
            """);
    }

    private async Task<T> Creer<T>(string route, object corps)
    {
        var reponse = await _admin.PostAsJsonAsync(route, corps);

        if (!reponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{route} : {await reponse.Content.ReadAsStringAsync()}");
        }

        return (await reponse.Content.ReadFromJsonAsync<T>())!;
    }

    private Task<TypePrestationDto> Type(string libelle, int duree) =>
        Creer<TypePrestationDto>(Route, new TypePrestationCreateDto { Libelle = libelle, DureeParDefautMinutes = duree });

    private Task<HttpResponseMessage> Modifier(Guid id, string libelle, int duree, bool actif) =>
        _admin.PutAsJsonAsync($"{Route}/{id}", new TypePrestationCreateDto
        {
            Libelle = libelle, DureeParDefautMinutes = duree, Actif = actif
        });

    private PrestationCreateDto Corps(string libelle, Guid? type) => new()
    {
        ContratId = _contrat, Libelle = libelle, DureeEstimeeMinutes = 30, TypePrestationId = type
    };

    private Task<PrestationDto> Prestation(string libelle, Guid? type) =>
        Creer<PrestationDto>("/api/prestations", Corps(libelle, type));
}
