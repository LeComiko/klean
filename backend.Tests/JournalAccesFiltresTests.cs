using System.Net;
using Klean.Api.Data;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Klean.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Klean.Api.Tests;

/// <summary>
/// Les filtres de <c>GET /api/journal-acces</c> (SPEC.md, étape 27, lot A).
///
/// Ils existaient depuis l'étape 17 sans qu'aucun test les exerce : <see cref="JournalAccesTests"/>
/// lit la route sans paramètre. L'écran du journal en devient le premier lecteur.
///
/// Les lignes sont posées <b>en base</b>, sous un auteur fictif propre à chaque test et à des
/// instants choisis : aucune route n'écrit le journal à une date donnée, et les lectures du
/// test lui-même — l'administrateur qui lit le journal — tombent sous un autre auteur.
/// </summary>
[Collection(CollectionApi.Nom)]
public class JournalAccesFiltresTests : IAsyncLifetime
{
    private readonly FixtureApi _fixture;
    private HttpClient _client = null!;

    public JournalAccesFiltresTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly DateTime Instant = new(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Le_filtre_d_auteur_entre_avant_le_comptage()
    {
        var auteur = Guid.NewGuid();
        var autre = Guid.NewGuid();
        await PoserAsync(
            Ligne(auteur, RessourceAccedee.Agent, NatureAcces.Liste),
            Ligne(auteur, RessourceAccedee.Agent, NatureAcces.Lecture),
            Ligne(auteur, RessourceAccedee.Absence, NatureAcces.Liste),
            Ligne(autre, RessourceAccedee.Agent, NatureAcces.Liste));

        var page = await _client.PageAsync<JournalAccesDto>(
            $"/api/journal-acces?utilisateurId={auteur}&taille=2");

        // Le total est celui du filtre, pas de la table ni de la page.
        Assert.Equal(3, page.Total);
        Assert.Equal(2, page.Elements.Count());
        Assert.All(page.Elements, a => Assert.Equal(auteur, a.UtilisateurId));
    }

    [Fact]
    public async Task Le_filtre_de_ressource_ne_rend_que_la_sienne()
    {
        var auteur = Guid.NewGuid();
        await PoserAsync(
            Ligne(auteur, RessourceAccedee.Agent, NatureAcces.Lecture),
            Ligne(auteur, RessourceAccedee.Absence, NatureAcces.Liste),
            Ligne(auteur, RessourceAccedee.Absence, NatureAcces.Lecture));

        var page = await _client.PageAsync<JournalAccesDto>(
            $"/api/journal-acces?utilisateurId={auteur}&ressource=Absence");

        Assert.Equal(2, page.Total);
        Assert.All(page.Elements, a => Assert.Equal("Absence", a.Ressource));
    }

    /// <summary>
    /// La question d'un contrôle : qui a exporté, pseudonymisé, fermé — des gestes rares, noyés
    /// dans les listes que chaque écran produit à son ouverture.
    /// </summary>
    [Fact]
    public async Task Le_filtre_de_nature_isole_un_geste_et_se_combine_a_la_ressource()
    {
        var auteur = Guid.NewGuid();
        await PoserAsync(
            Ligne(auteur, RessourceAccedee.Agent, NatureAcces.Export),
            Ligne(auteur, RessourceAccedee.Agent, NatureAcces.Lecture),
            Ligne(auteur, RessourceAccedee.Agent, NatureAcces.Liste),
            Ligne(auteur, RessourceAccedee.Compte, NatureAcces.Lecture),
            Ligne(auteur, RessourceAccedee.Compte, NatureAcces.Desactivation));

        var exports = await _client.PageAsync<JournalAccesDto>(
            $"/api/journal-acces?utilisateurId={auteur}&nature=Export");
        var lecturesDeFiches = await _client.PageAsync<JournalAccesDto>(
            $"/api/journal-acces?utilisateurId={auteur}&ressource=Agent&nature=Lecture");

        Assert.Equal("Export", Assert.Single(exports.Elements).Nature);
        Assert.Equal(1, exports.Total);

        var lecture = Assert.Single(lecturesDeFiches.Elements);
        Assert.Equal(("Agent", "Lecture"), (lecture.Ressource, lecture.Nature));
    }

    /// <summary>
    /// ⚠️ « 0 » est le cas qui compte : <c>Enum.TryParse</c> l'accepte et filtre sur la première
    /// valeur, <c>Agent</c> ou <c>Liste</c>, qu'aucun administrateur n'a choisie. La casse et
    /// une liste séparée par une virgule — que <c>TryParse</c> lit comme deux drapeaux — sont
    /// refusées pour la même raison.
    /// </summary>
    [Theory]
    [InlineData("ressource=0")]
    [InlineData("ressource=agent")]
    [InlineData("nature=0")]
    [InlineData("nature=export")]
    [InlineData("nature=Liste,Lecture")]
    [InlineData("nature=Inconnue")]
    public async Task Une_ressource_ou_une_nature_illisible_est_refusee_en_nommant_les_valeurs(string parametre)
    {
        var reponse = await _client.GetAsync($"/api/journal-acces?{parametre}");

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("Valeurs acceptées", await reponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Une_ressource_ou_une_nature_vide_laisse_le_filtre_ouvert()
    {
        var auteur = Guid.NewGuid();
        await PoserAsync(
            Ligne(auteur, RessourceAccedee.Agent, NatureAcces.Lecture),
            Ligne(auteur, RessourceAccedee.Absence, NatureAcces.Liste));

        var page = await _client.PageAsync<JournalAccesDto>(
            $"/api/journal-acces?utilisateurId={auteur}&ressource=&nature=");

        Assert.Equal(2, page.Total);
    }

    /// <summary>
    /// Le 12 novembre à Paris, du début inclus à la fin exclue : de 23:00 UTC la veille à
    /// 23:00 UTC le jour même. Les deux lignes à une seconde de chaque borne départagent.
    ///
    /// La seconde forme, avec un décalage, est celle qu'un écran à Paris pourrait envoyer : le
    /// lieur de dates d'ASP.NET la ramène lui-même en UTC, et ce test le fige. Il tourne en
    /// heure de Paris sur la machine de développement, là où une conversion vers l'heure
    /// locale se serait vue.
    /// </summary>
    [Theory]
    [InlineData("2026-11-11T23:00:00Z", "2026-11-12T23:00:00Z")]
    [InlineData("2026-11-12T00:00:00%2B01:00", "2026-11-13T00:00:00%2B01:00")]
    public async Task La_periode_est_un_instant_dont_le_debut_est_inclus_et_la_fin_exclue(
        string depuis, string jusqua)
    {
        var auteur = Guid.NewGuid();
        await PoserAsync(
            Ligne(auteur, RessourceAccedee.Agent, NatureAcces.Lecture, Utc(2026, 11, 11, 22, 59, 59)),
            Ligne(auteur, RessourceAccedee.Agent, NatureAcces.Lecture, Utc(2026, 11, 11, 23, 0, 0)),
            Ligne(auteur, RessourceAccedee.Agent, NatureAcces.Lecture, Utc(2026, 11, 12, 22, 59, 59)),
            Ligne(auteur, RessourceAccedee.Agent, NatureAcces.Lecture, Utc(2026, 11, 12, 23, 0, 0)));

        var page = await _client.PageAsync<JournalAccesDto>(
            $"/api/journal-acces?utilisateurId={auteur}&depuis={depuis}&jusqua={jusqua}");

        Assert.Equal(
            [Utc(2026, 11, 12, 22, 59, 59), Utc(2026, 11, 11, 23, 0, 0)],
            page.Elements.Select(a => a.Horodatage.ToUniversalTime()).ToArray());
        Assert.Equal(2, page.Total);
    }

    // ---------------------------------------------------------------------------

    private static DateTime Utc(int a, int m, int j, int h, int min, int s) =>
        new(a, m, j, h, min, s, DateTimeKind.Utc);

    private AccesJournalise Ligne(
        Guid auteur, RessourceAccedee ressource, NatureAcces nature, DateTime? horodatage = null) => new()
    {
        EntrepriseId = _fixture.EntrepriseAlpha,
        UtilisateurId = auteur,
        Ressource = ressource,
        Nature = nature,
        CibleId = nature == NatureAcces.Liste ? null : Guid.NewGuid(),
        Lignes = nature == NatureAcces.Liste ? 12 : 1,
        Horodatage = horodatage ?? Instant
    };

    /// <summary>Sous l'API : aucune route n'écrit le journal à un instant choisi.</summary>
    private Task PoserAsync(params AccesJournalise[] lignes) =>
        _fixture.AvecServicesAsync(async services =>
        {
            var db = services.GetRequiredService<KleanDbContext>();
            db.AccesJournalises.AddRange(lignes);
            await db.SaveChangesAsync();
        });
}
