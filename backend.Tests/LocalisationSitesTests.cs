using System.Net;
using System.Net.Http.Json;
using Klean.Api.Controllers;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Klean.Api.Services;
using Klean.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Klean.Api.Tests;

/// <summary>La route de localisation (0.3.0), par l'API, le gestionnaire primaire du client substitué.</summary>
[Collection(CollectionApi.Nom)]
public class LocalisationSitesTests : IAsyncLifetime
{
    private readonly FixtureApi _fixture;
    private HttpClient _admin = null!;

    public LocalisationSitesTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly LocaliserSiteDto Segur = new() { Adresse = "20 avenue de Ségur", CodePostal = "75007", Ville = "Paris" };

    private Task<HttpResponseMessage> Localiser(HttpClient client, LocaliserSiteDto? dto = null) =>
        client.PostAsJsonAsync("/api/sites/localiser", dto ?? Segur);

    private async Task Activer(HttpClient admin, Guid entreprise, string raisonSociale, string siret)
    {
        var reponse = await admin.PutAsJsonAsync($"/api/entreprises/{entreprise}", new EntrepriseCreateDto
        {
            RaisonSociale = raisonSociale, Siret = siret, Territoire = "Metropole", GeocodageActif = true
        });
        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);
    }

    private Task ActiverAlpha() => Activer(_admin, _fixture.EntrepriseAlpha, "Alpha Propreté", "10000000000001");

    [Fact]
    public async Task Reglage_coche_une_adresse_trouvee_rend_ses_coordonnees()
    {
        await ActiverAlpha();

        var reponse = await Localiser(_admin);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var localisation = await reponse.Content.ReadFromJsonAsync<LocalisationDto>();
        Assert.Equal(48.850699, localisation!.Latitude);
        Assert.Equal(2.308628, localisation.Longitude);
        Assert.Equal("20 Avenue de Ségur 75007 Paris", localisation.AdresseTrouvee);
        Assert.False(localisation.AVerifier);

        var vue = Assert.Single(_fixture.Fabrique.Geocodage.Requetes);
        Assert.Contains("q=20%20avenue%20de%20S%C3%A9gur%20Paris", vue.Uri.Query);
        Assert.Contains("postcode=75007", vue.Uri.Query);
    }

    [Fact]
    public async Task Reglage_desactive_409_et_aucun_appel_sortant()
    {
        var reponse = await Localiser(_admin);

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        Assert.Contains(SitesController.RefusDesactive, await reponse.Content.ReadAsStringAsync());
        Assert.Empty(_fixture.Fabrique.Geocodage.Requetes);
    }

    /// <summary>Le test seul du réglage désactivé ne distingue pas une lecture mal cadrée : A coche, B non.</summary>
    [Fact]
    public async Task Le_reglage_lu_est_celui_de_l_entreprise_du_jeton()
    {
        await ActiverAlpha();
        var adminBeta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);

        var reponse = await Localiser(adminBeta);

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        Assert.Empty(_fixture.Fabrique.Geocodage.Requetes);
    }

    [Fact]
    public async Task Une_adresse_introuvable_400_en_francais()
    {
        await ActiverAlpha();
        _fixture.Fabrique.Geocodage.Repondre = (_, _) => Task.FromResult(GeocodageDeTest.Json(GeocodageDeTest.Vide));

        var reponse = await Localiser(_admin);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains(SitesController.RefusIntrouvable, await reponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Un_service_injoignable_503_en_francais()
    {
        await ActiverAlpha();
        _fixture.Fabrique.Geocodage.Repondre = (_, _) => throw new HttpRequestException("refusée");

        var reponse = await Localiser(_admin);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, reponse.StatusCode);
        Assert.Contains(SitesController.RefusInjoignable, await reponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Apres_un_429_du_service_la_localisation_suivante_503_sans_appel()
    {
        await ActiverAlpha();
        _fixture.Fabrique.Geocodage.Repondre = (_, _) =>
        {
            var reponse = GeocodageDeTest.Json("{}", HttpStatusCode.TooManyRequests);
            reponse.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return Task.FromResult(reponse);
        };

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Localiser(_admin)).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Localiser(_admin)).StatusCode);
        Assert.Single(_fixture.Fabrique.Geocodage.Requetes);
    }

    public static TheoryData<string?, string?, string?, string> ChampsRefuses => new()
    {
        { null, "75007", "Paris", SitesController.RefusAdresseVide },
        { "   ", null, null, SitesController.RefusAdresseVide },
        { new string('a', 201), null, null, "L’adresse dépasse 200 caractères." },
        { "20 rue", new string('1', 11), null, "Le code postal dépasse 10 caractères." },
        { "20 rue", null, new string('v', 101), "La ville dépasse 100 caractères." },
        { "20 rue\u0000", null, null, SitesController.RefusInvisible },
        { new string('a', 150), null, new string('v', 60), SitesController.RefusTropLongue },
        { "ab", null, null, SitesController.RefusDebut },
        { "« 20 rue »", null, null, SitesController.RefusDebut },
    };

    [Theory]
    [MemberData(nameof(ChampsRefuses))]
    public async Task Des_champs_refuses_400_en_francais_sans_appel_sortant(
        string? adresse, string? codePostal, string? ville, string message)
    {
        await ActiverAlpha();

        var reponse = await Localiser(_admin, new LocaliserSiteDto { Adresse = adresse, CodePostal = codePostal, Ville = ville });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains(message, await reponse.Content.ReadAsStringAsync());
        Assert.Empty(_fixture.Fabrique.Geocodage.Requetes);
    }

    [Theory]
    [InlineData(RolesKlean.Responsable)]
    [InlineData(RolesKlean.ChefEquipe)]
    public async Task Un_role_interne_autre_que_l_administrateur_est_refuse(string role)
    {
        await ActiverAlpha();
        var client = await _fixture.ClientDeRoleAsync(_admin, role, $"{role.ToLowerInvariant()}@test.local");

        Assert.Equal(HttpStatusCode.Forbidden, (await Localiser(client)).StatusCode);
        Assert.Empty(_fixture.Fabrique.Geocodage.Requetes);
    }

    [Fact]
    public async Task L_agent_et_le_client_syndic_sont_refuses()
    {
        await ActiverAlpha();

        var agent = await (await _admin.PostAsJsonAsync("/api/agents", new AgentCreateDto
        {
            Nom = "Agent", Prenom = "Un", DateEmbauche = new DateTime(2024, 1, 1)
        })).Content.ReadFromJsonAsync<AgentDto>();
        var clientSyndic = await (await _admin.PostAsJsonAsync("/api/clients", new ClientCreateDto
        {
            Nom = "Syndic", Type = "Syndic"
        })).Content.ReadFromJsonAsync<ClientDto>();

        var compteAgent = await _fixture.ClientDeRoleAsync(_admin, RolesKlean.Agent, "agent@test.local", agentId: agent!.Id);
        var compteSyndic = await _fixture.ClientDeRoleAsync(_admin, RolesKlean.ClientSyndic, "syndic@test.local", clientId: clientSyndic!.Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await Localiser(compteAgent)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Localiser(compteSyndic)).StatusCode);
        Assert.Empty(_fixture.Fabrique.Geocodage.Requetes);
    }

    /// <summary>
    /// Espacées de 110 ms, les requêtes restent sous le plafond global (dix par seconde) : c'est le plafond
    /// par compte qui est éprouvé.
    /// </summary>
    [Fact]
    public async Task Le_plafond_par_compte_429_avec_Retry_After()
    {
        await ActiverAlpha();

        for (var essai = 1; essai <= PlafondsDebit.LocalisationsParCompte; essai++)
        {
            Assert.Equal(HttpStatusCode.OK, (await Localiser(_admin)).StatusCode);
            await Task.Delay(110);
        }

        var refusee = await Localiser(_admin);

        Assert.Equal(HttpStatusCode.TooManyRequests, refusee.StatusCode);
        Assert.True(refusee.Headers.RetryAfter?.Delta > TimeSpan.FromSeconds(60));
        var corps = await refusee.Content.ReadAsStringAsync();
        Assert.Contains(SitesController.RefusPlafondCompte, corps);
        Assert.DoesNotContain(SitesController.RefusSurcharge, corps);
    }

    /// <summary>
    /// Le message du plafond par compte écrit le nombre et la fenêtre en toutes lettres : changer l'un des
    /// deux plafonds sans le message le fait rougir.
    /// </summary>
    [Fact]
    public void Le_message_du_plafond_par_compte_dit_le_nombre_et_la_fenetre_de_PlafondsDebit()
    {
        var nombres = new Dictionary<int, string> { [30] = "Trente", [15] = "quinze" };

        Assert.Contains(nombres[PlafondsDebit.LocalisationsParCompte], SitesController.RefusPlafondCompte);
        Assert.Contains(nombres[(int)PlafondsDebit.FenetreLocalisations.TotalMinutes], SitesController.RefusPlafondCompte);
    }

    /// <summary>
    /// Le premier caractère est jugé par rune, comme l'écran (lettre ou chiffre décimal) : « \u00BD » et
    /// « \u2163 » sont des nombres sans être des chiffres, et partent en 400 sans appel à l'IGN.
    /// </summary>
    [Theory]
    [InlineData("\u00BD rue de Paris")]
    [InlineData("\u2163 rue de Paris")]
    public async Task Un_debut_qui_n_est_ni_lettre_ni_chiffre_400_sans_appel_au_faux(string adresse)
    {
        await ActiverAlpha();

        var reponse = await Localiser(_admin, new LocaliserSiteDto { Adresse = adresse });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(SitesController.RefusDebut, await reponse.Content.ReadAsStringAsync());
        Assert.Empty(_fixture.Fabrique.Geocodage.Requetes);
    }

    [Fact]
    public async Task Le_plafond_global_429_au_dela_de_dix_par_seconde()
    {
        await ActiverAlpha();

        // Marge large : MemoryCache.GetOrCreate n'est pas atomique, une première rafale peut perdre des comptes.
        var reponses = await Task.WhenAll(Enumerable.Range(0, 2 * PlafondsDebit.LocalisationsGlobales + 5)
            .Select(_ => Localiser(_admin)));

        var refusees = reponses.Where(r => r.StatusCode == HttpStatusCode.TooManyRequests).ToArray();
        Assert.NotEmpty(refusees);
        Assert.All(refusees, r => Assert.Equal(TimeSpan.FromSeconds(1), r.Headers.RetryAfter?.Delta));
        // Borne haute : au plus le double du plafond passe, la marge de la course de GetOrCreate.
        Assert.InRange(reponses.Count(r => r.StatusCode == HttpStatusCode.OK), 1, 2 * PlafondsDebit.LocalisationsGlobales);
        Assert.Contains(SitesController.RefusSurcharge, await refusees[0].Content.ReadAsStringAsync());
    }

    /// <summary>Un 409 « désactivé » ne part pas vers l'IGN : il ne consomme pas le plafond global.</summary>
    [Fact]
    public async Task Un_409_desactive_ne_consomme_pas_le_plafond_global()
    {
        await ActiverAlpha();
        var adminBeta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);

        var refus = await Task.WhenAll(Enumerable.Range(0, 2 * PlafondsDebit.LocalisationsGlobales + 5)
            .Select(_ => Localiser(adminBeta)));
        Assert.All(refus, r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Contains(SitesController.RefusDesactive, await refus[0].Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, (await Localiser(_admin)).StatusCode);
    }

    public static TheoryData<string?, string?, string?> ChampsInvisibles => new()
    {
        { "20 avenue de S\u202Egur", null, null },
        { "20 avenue de Ségur", null, "Pa\u200Bris" },
        { "20 avenue de Ségur", "75\u00AD007", null },
        { "20 avenue de S\U000E0041gur", null, null },
    };

    /// <summary>Control et Format, par runes : l'étiquette U+E0041 est hors du plan de base, une paire de substituts en UTF-16.</summary>
    [Theory]
    [MemberData(nameof(ChampsInvisibles))]
    public async Task Un_caractere_invisible_400_sans_appel_sortant(string? adresse, string? codePostal, string? ville)
    {
        await ActiverAlpha();

        var reponse = await Localiser(_admin, new LocaliserSiteDto { Adresse = adresse, CodePostal = codePostal, Ville = ville });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains(SitesController.RefusInvisible, await reponse.Content.ReadAsStringAsync());
        Assert.Empty(_fixture.Fabrique.Geocodage.Requetes);
    }

    /// <summary>
    /// Le plafond global ne compte que les requêtes qui partent vers l'IGN. Vingt-cinq refus de champ,
    /// d'un second administrateur (le plafond par compte, 30, reste hors de portée), ne doivent pas le
    /// remplir : la requête valide qui suit, du premier compte, rend 200.
    /// </summary>
    [Fact]
    public async Task Un_400_de_champ_ne_consomme_pas_le_plafond_global()
    {
        await ActiverAlpha();
        var second = await _fixture.ClientDeRoleAsync(_admin, RolesKlean.Administrateur, "admin2.alpha@test.local");

        var refus = await Task.WhenAll(Enumerable.Range(0, 2 * PlafondsDebit.LocalisationsGlobales + 5)
            .Select(_ => Localiser(second, new LocaliserSiteDto { Adresse = "" })));
        Assert.All(refus, r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));

        Assert.Equal(HttpStatusCode.OK, (await Localiser(_admin)).StatusCode);
    }

    /// <summary>
    /// Sans ce test, un ordre d'enregistrement inversé ferait appeler la vraie Géoplateforme par la série
    /// sans que rien ne rougisse : le gestionnaire primaire du client nommé doit être le faux.
    /// </summary>
    [Fact]
    public void Le_gestionnaire_primaire_du_client_de_geocodage_est_le_faux()
    {
        var fabrique = _fixture.Fabrique.Services.GetRequiredService<IHttpMessageHandlerFactory>();
        var gestionnaire = fabrique.CreateHandler(ServiceGeocodage.NomClient);

        while (gestionnaire is DelegatingHandler delegue) gestionnaire = delegue.InnerHandler!;

        Assert.Same(_fixture.Fabrique.Geocodage, gestionnaire);
    }
}
