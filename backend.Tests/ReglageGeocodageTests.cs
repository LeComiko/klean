using System.Net;
using System.Net.Http.Json;
using Klean.Api.Data;
using Klean.Api.DTOs;
using Klean.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Klean.Api.Tests;

/// <summary>
/// Le réglage de localisation de l'entreprise (0.3.0). ⚠️ Le contrôleur projette le DTO à la main en
/// deux endroits et recopie le PUT champ par champ : un oubli rend la case toujours décochée, ou
/// désactive la localisation à chaque enregistrement, sans erreur. D'où la relecture par GET
/// <b>et</b> en base.
/// </summary>
[Collection(CollectionApi.Nom)]
public class ReglageGeocodageTests : IAsyncLifetime
{
    private readonly FixtureApi _fixture;

    public ReglageGeocodageTests(FixtureApi fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ReinitialiserAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<HttpResponseMessage> Poser(HttpClient admin, bool actif, string raisonSociale = "Alpha Propreté") =>
        admin.PutAsJsonAsync($"/api/entreprises/{_fixture.EntrepriseAlpha}", new EntrepriseCreateDto
        {
            RaisonSociale = raisonSociale,
            Siret = "10000000000001",
            Territoire = "Metropole",
            GeocodageActif = actif
        });

    private async Task<Klean.Api.Models.Entreprise> EnBase()
    {
        using var portee = _fixture.PorteeAuProprietaire();
        var db = portee.ServiceProvider.GetRequiredService<KleanDbContext>();
        return await db.Entreprises.AsNoTracking().SingleAsync(e => e.Id == _fixture.EntrepriseAlpha);
    }

    [Fact]
    public async Task Une_entreprise_neuve_se_lit_desactivee_et_sans_date()
    {
        var admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        var lue = await admin.GetFromJsonAsync<EntrepriseDto>($"/api/entreprises/{_fixture.EntrepriseAlpha}");

        Assert.False(lue!.GeocodageActif);
        Assert.Null(lue.GeocodageActiveLe);
        Assert.Null(lue.GeocodageDesactiveLe);
    }

    [Fact]
    public async Task Coche_le_reglage_se_relit_par_GET_dans_la_liste_et_en_base_avec_sa_date()
    {
        var admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
        var avant = DateTime.UtcNow;

        Assert.Equal(HttpStatusCode.NoContent, (await Poser(admin, true)).StatusCode);

        var lue = await admin.GetFromJsonAsync<EntrepriseDto>($"/api/entreprises/{_fixture.EntrepriseAlpha}");
        Assert.True(lue!.GeocodageActif);
        Assert.True(lue.GeocodageActiveLe >= avant && lue.GeocodageActiveLe <= DateTime.UtcNow);
        Assert.Null(lue.GeocodageDesactiveLe);

        var liste = await admin.ListerAsync<EntrepriseDto>("/api/entreprises");
        Assert.True(Assert.Single(liste).GeocodageActif);

        var enBase = await EnBase();
        Assert.True(enBase.GeocodageActif);
        Assert.NotNull(enBase.GeocodageActiveLe);
    }

    [Fact]
    public async Task Decoche_le_reglage_pose_sa_date_de_desactivation_et_garde_celle_d_activation()
    {
        var admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
        Assert.Equal(HttpStatusCode.NoContent, (await Poser(admin, true)).StatusCode);
        var activeLe = (await EnBase()).GeocodageActiveLe;
        Assert.NotNull(activeLe);

        Assert.Equal(HttpStatusCode.NoContent, (await Poser(admin, false)).StatusCode);

        var enBase = await EnBase();
        Assert.False(enBase.GeocodageActif);
        Assert.Equal(activeLe, enBase.GeocodageActiveLe);
        Assert.NotNull(enBase.GeocodageDesactiveLe);
        Assert.True(enBase.GeocodageDesactiveLe >= activeLe);
    }

    [Fact]
    public async Task Un_PUT_qui_ne_change_pas_le_reglage_ne_touche_aucune_date()
    {
        var admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
        Assert.Equal(HttpStatusCode.NoContent, (await Poser(admin, true)).StatusCode);
        var activeLe = (await EnBase()).GeocodageActiveLe;
        Assert.NotNull(activeLe);

        Assert.Equal(HttpStatusCode.NoContent,
            (await Poser(admin, true, raisonSociale: "Alpha Propreté et fils")).StatusCode);

        var enBase = await EnBase();
        Assert.Equal("Alpha Propreté et fils", enBase.RaisonSociale);
        Assert.Equal(activeLe, enBase.GeocodageActiveLe);
        Assert.Null(enBase.GeocodageDesactiveLe);
    }

    [Fact]
    public async Task Decocher_une_localisation_jamais_activee_ne_date_rien()
    {
        var admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        Assert.Equal(HttpStatusCode.NoContent, (await Poser(admin, false)).StatusCode);

        var enBase = await EnBase();
        Assert.False(enBase.GeocodageActif);
        Assert.Null(enBase.GeocodageActiveLe);
        Assert.Null(enBase.GeocodageDesactiveLe);
    }

    [Fact]
    public async Task Reactiver_renouvelle_la_date_d_activation_et_garde_celle_de_desactivation()
    {
        var admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        Assert.Equal(HttpStatusCode.NoContent, (await Poser(admin, true)).StatusCode);
        var premiereActivation = (await EnBase()).GeocodageActiveLe;
        Assert.NotNull(premiereActivation);

        Assert.Equal(HttpStatusCode.NoContent, (await Poser(admin, false)).StatusCode);
        var desactiveLe = (await EnBase()).GeocodageDesactiveLe;
        Assert.NotNull(desactiveLe);

        Assert.Equal(HttpStatusCode.NoContent, (await Poser(admin, true)).StatusCode);

        var enBase = await EnBase();
        Assert.True(enBase.GeocodageActif);
        Assert.True(enBase.GeocodageActiveLe > premiereActivation);
        Assert.Equal(desactiveLe, enBase.GeocodageDesactiveLe);
    }

    /// <summary>
    /// Le défaut de la colonne, exercé : une ligne écrite <b>sans</b> les colonnes, comme une
    /// entreprise d'avant la migration. Une entreprise créée par EF ne prouverait rien — EF écrit
    /// explicitement le <c>false</c> du CLR.
    /// </summary>
    [Fact]
    public async Task Une_entreprise_d_avant_la_migration_porte_false_et_aucune_date()
    {
        using var portee = _fixture.PorteeAuProprietaire();
        var db = portee.ServiceProvider.GetRequiredService<KleanDbContext>();
        var id = Guid.NewGuid();

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""INSERT INTO "Entreprises" ("Id", "RaisonSociale", "Siret", "DateCreation") VALUES ({id}, 'Gamma', '30000000000003', now())""");

        var ligne = await db.Database.SqlQuery<EtatEnBase>(
                $"""SELECT "GeocodageActif" AS "Actif", "GeocodageActiveLe" AS "ActiveLe", "GeocodageDesactiveLe" AS "DesactiveLe" FROM "Entreprises" WHERE "Id" = {id}""")
            .SingleAsync();

        Assert.False(ligne.Actif);
        Assert.Null(ligne.ActiveLe);
        Assert.Null(ligne.DesactiveLe);
    }

    private sealed class EtatEnBase
    {
        public bool Actif { get; set; }
        public DateTime? ActiveLe { get; set; }
        public DateTime? DesactiveLe { get; set; }
    }
}
