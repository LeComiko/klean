using System.Net;
using System.Net.Http.Json;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Klean.Api.Services;
using Klean.Api.Tests.Infrastructure;

namespace Klean.Api.Tests;

/// <summary>
/// Changement de mot de passe par son propriétaire — <c>POST /api/auth/changer-mot-de-passe</c>.
///
/// Il comble le dernier trou des parcours de mot de passe : la réinitialisation de
/// l'étape 17 exige un serveur SMTP, optionnel sur une instance KLEAN, et
/// l'administrateur ne doit pas poser lui-même le mot de passe d'un collègue — il le
/// connaîtrait. Restait donc un responsable connecté sans aucun moyen de changer son
/// mot de passe.
///
/// Ce que cette série garde tient en deux phrases : <b>l'appareil qui change le mot de
/// passe reste connecté</b>, et <b>tous les autres tombent</b>. Les deux moitiés se
/// cassent indépendamment, et chacune se casse en silence.
/// </summary>
[Collection(CollectionApi.Nom)]
public class ChangementMotDePasseTests : IAsyncLifetime
{
    private const string Nouveau = "ChangementTest2026";

    private readonly FixtureApi _fixture;

    public ChangementMotDePasseTests(FixtureApi fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ReinitialiserAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ----------------------------------------------------------- le changement

    [Fact]
    public async Task Le_mot_de_passe_est_remplace()
    {
        var client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        var reponse = await Changer(client, FixtureApi.MotDePasse, Nouveau);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);

        // L'ancien ne vaut plus rien, le nouveau ouvre une session.
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Connexion(FixtureApi.AdminAlpha, FixtureApi.MotDePasse)).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await Connexion(FixtureApi.AdminAlpha, Nouveau)).StatusCode);
    }

    [Fact]
    public async Task La_reponse_rend_une_session_neuve_et_aucun_jeton()
    {
        var client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        var reponse = await Changer(client, FixtureApi.MotDePasse, Nouveau);

        var session = await reponse.Content.ReadFromJsonAsync<SessionDto>();
        Assert.NotNull(session);
        Assert.True(session.Expiration > DateTime.UtcNow, "L'expiration rendue est déjà passée.");
        Assert.Equal(FixtureApi.AdminAlpha, session.Profil.Email);

        // Le contrat de l'étape 12 : aucun jeton ne traverse le corps d'une réponse.
        var corps = await reponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("eyJ", corps);

        // Les trois cookies sont reposés, dont le cookie anti-CSRF qui a tourné.
        Assert.NotNull(FixtureApi.ValeurDuCookie(reponse, ServiceSession.CookieAcces));
        Assert.NotNull(FixtureApi.ValeurDuCookie(reponse, ServiceSession.CookieRafraichissement));
        Assert.NotNull(FixtureApi.ValeurDuCookie(reponse, ServiceSession.CookieCsrf));
    }

    // ------------------------------------- l'appareil qui change reste connecté

    [Fact]
    public async Task L_appareil_qui_change_son_mot_de_passe_reste_connecte()
    {
        var client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        await Changer(client, FixtureApi.MotDePasse, Nouveau);

        // ⚠️ Le test qui attrape l'empreinte de sécurité périmée. Identity régénère
        // le SecurityStamp, et le cookie d'accès reposé dans la même réponse doit
        // porter la NOUVELLE empreinte : sinon OnTokenValidated refuse dès la requête
        // suivante, et l'utilisateur est déconnecté sans que rien ne l'explique.
        var profil = await client.GetAsync("/api/auth/moi");
        Assert.Equal(HttpStatusCode.OK, profil.StatusCode);
    }

    [Fact]
    public async Task Le_jeton_de_rafraichissement_rendu_est_utilisable()
    {
        var client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        var reponse = await Changer(client, FixtureApi.MotDePasse, Nouveau);

        // Le cookie anti-CSRF a tourné : le réarmer est exactement ce que fait le
        // frontend, qui relit le cookie à chaque écriture.
        FixtureApi.ArmerCsrf(client, reponse);

        // ⚠️ Le test qui attrape l'ordre inversé. Le contrôleur révoque toutes les
        // sessions PUIS rouvre la sienne ; dans l'autre sens, le jeton tout juste
        // émis serait révoqué par le geste suivant et l'utilisateur tomberait au
        // premier renouvellement — quinze minutes plus tard, très loin de la cause.
        var rafraichissement = await client.PostAsync("/api/auth/rafraichir", null);
        Assert.Equal(HttpStatusCode.OK, rafraichissement.StatusCode);
    }

    // --------------------------------------------- les autres appareils tombent

    [Fact]
    public async Task Les_autres_appareils_du_compte_sont_deconnectes()
    {
        var premier = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
        var autre = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        Assert.Equal(HttpStatusCode.OK, (await autre.GetAsync("/api/auth/moi")).StatusCode);

        await Changer(premier, FixtureApi.MotDePasse, Nouveau);

        // Deux assertions, parce que deux mécanismes distincts doivent agir et que
        // chacun se retire sans que l'autre le signale (CLAUDE.md §10) : le
        // SecurityStamp périme le jeton d'accès, la révocation tue la session de
        // rafraîchissement. Tester le seul `moi` laisserait retirer la révocation.
        Assert.Equal(HttpStatusCode.Unauthorized, (await autre.GetAsync("/api/auth/moi")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await autre.PostAsync("/api/auth/rafraichir", null)).StatusCode);
    }

    [Fact]
    public async Task Les_sessions_d_un_autre_compte_ne_sont_pas_touchees()
    {
        var client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
        var temoin = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);

        await Changer(client, FixtureApi.MotDePasse, Nouveau);

        // ⚠️ Le témoin porte sur `rafraichir` et non sur `moi` : un jeton d'accès
        // est signé et vaut quinze minutes, donc `moi` resterait vert même si les
        // sessions du témoin avaient été coupées (CLAUDE.md §10).
        Assert.Equal(HttpStatusCode.OK,
            (await temoin.PostAsync("/api/auth/rafraichir", null)).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await Connexion(FixtureApi.AdminBeta, FixtureApi.MotDePasse)).StatusCode);
    }

    // ------------------------------------------------------------------- refus

    [Fact]
    public async Task Un_mot_de_passe_actuel_faux_est_refuse_en_400_et_non_en_401()
    {
        var client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        var reponse = await Changer(client, "PasLeBonMotDePasse2026", Nouveau);

        // ⚠️ 400, jamais 401, et c'est un couplage avec le frontend : `api/client.ts`
        // traite tout 401 comme une session morte — rafraîchissement, rejeu, puis
        // purge. Un 401 ici déconnecterait l'utilisateur sur une faute de frappe.
        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("Mot de passe incorrect", await reponse.Content.ReadAsStringAsync());

        // La session de l'appelant survit, et le mot de passe n'a pas changé.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/moi")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await Connexion(FixtureApi.AdminAlpha, FixtureApi.MotDePasse)).StatusCode);
    }

    [Fact]
    public async Task Un_nouveau_mot_de_passe_trop_court_est_refuse_en_francais()
    {
        var client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        var reponse = await Changer(client, FixtureApi.MotDePasse, "Court1");
        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);

        // Identity est l'autorité sur la règle, et il parle français depuis
        // l'étape 12. Un `[MinLength]` sur le DTO aurait répondu avant lui, en
        // anglais, hors d'atteinte du descripteur (CLAUDE.md §6).
        var message = await reponse.Content.ReadAsStringAsync();
        Assert.Contains("au moins 10 caractères", message);
        Assert.DoesNotContain("must", message);
    }

    [Fact]
    public async Task Un_nouveau_mot_de_passe_sans_chiffre_est_refuse_en_francais()
    {
        var client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        var reponse = await Changer(client, FixtureApi.MotDePasse, "SansAucunChiffre");
        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("au moins un chiffre", await reponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Le_changement_exige_une_session()
    {
        var anonyme = _fixture.ClientAnonymeSansRedirection();

        var reponse = await anonyme.PostAsJsonAsync("/api/auth/changer-mot-de-passe",
            new ChangementMotDePasseDto
            {
                MotDePasseActuel = FixtureApi.MotDePasse,
                NouveauMotDePasse = Nouveau
            });

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);

        // ⚠️ Le code seul ne prouve rien ici, et un sabotage l'a montré : remplacer
        // `[Authorize]` par `[AllowAnonymous]` laissait ce test vert, parce que
        // l'action porte de toute façon un repli qui rend 401 quand le jeton n'a pas
        // d'identifiant exploitable. Le témoin qui distingue les deux est l'en-tête
        // `WWW-Authenticate`, que le gestionnaire JWT pose en répondant à un défi
        // d'autorisation et que `Unauthorized(...)` dans un contrôleur ne pose pas.
        Assert.Contains("Bearer", reponse.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Le_changement_exige_l_en_tete_anti_CSRF()
    {
        var client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        // Sans l'en-tête, un formulaire hébergé ailleurs pourrait changer le mot de
        // passe d'un utilisateur connecté — le pire des cas de CSRF, puisqu'il
        // emporte le compte.
        client.DefaultRequestHeaders.Remove(ServiceSession.EnteteCsrf);

        var reponse = await Changer(client, FixtureApi.MotDePasse, Nouveau);
        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);

        // Et le mot de passe n'a pas bougé.
        Assert.Equal(HttpStatusCode.OK,
            (await Connexion(FixtureApi.AdminAlpha, FixtureApi.MotDePasse)).StatusCode);
    }

    [Fact]
    public async Task Les_essais_de_mot_de_passe_actuel_sont_bornes()
    {
        var client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        // L'endpoint vérifie le mot de passe actuel, donc c'est un oracle — et le
        // verrouillage d'Identity ne s'y applique pas, ChangePasswordAsync ne
        // comptant aucun échec. Qui tient une session volée pourrait sinon y
        // éprouver sans limite un mot de passe sans doute réemployé ailleurs.
        for (var essai = 0; essai < PlafondsDebit.ChangementsParCompte; essai++)
        {
            var refus = await Changer(client, $"Faux{essai}MotDePasse2026", Nouveau);
            Assert.Equal(HttpStatusCode.BadRequest, refus.StatusCode);
        }

        var bloque = await Changer(client, FixtureApi.MotDePasse, Nouveau);
        Assert.Equal(HttpStatusCode.TooManyRequests, bloque.StatusCode);

        // Le plafond borne bien les essais : le bon mot de passe lui-même est refusé.
        Assert.Equal(HttpStatusCode.OK,
            (await Connexion(FixtureApi.AdminAlpha, FixtureApi.MotDePasse)).StatusCode);
    }

    [Fact]
    public async Task Un_role_non_administrateur_peut_changer_son_mot_de_passe()
    {
        var admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
        var responsable = await _fixture.ClientDeRoleAsync(
            admin, RolesKlean.Responsable, "responsable.compte@test.local");

        // C'est tout l'objet de l'écran de compte : le parcours doit être ouvert à
        // tout compte connecté, pas aux seuls administrateurs.
        var reponse = await Changer(responsable, FixtureApi.MotDePasse, Nouveau);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await Connexion("responsable.compte@test.local", Nouveau)).StatusCode);
    }

    // ------------------------------------------------------------------ outils

    private static Task<HttpResponseMessage> Changer(
        HttpClient client, string actuel, string nouveau) =>
        client.PostAsJsonAsync("/api/auth/changer-mot-de-passe",
            new ChangementMotDePasseDto
            {
                MotDePasseActuel = actuel,
                NouveauMotDePasse = nouveau
            });

    private Task<HttpResponseMessage> Connexion(string email, string motDePasse) =>
        _fixture.ClientAnonyme().PostAsJsonAsync("/api/auth/connexion",
            new ConnexionDto { Email = email, MotDePasse = motDePasse });
}
