using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Klean.Api.Middleware;
using Klean.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Klean.Api.Tests;

/// <summary>
/// Surface HTTP : en-têtes, CORS, HTTPS, limites, sonde de disponibilité.
///
/// C'est le lot où <b>rien ne se lit dans le code métier</b> : tout tient dans un
/// ordre de middleware et des valeurs de configuration. Un en-tête absent ne
/// casse aucun parcours, une politique CORS trop large ne fait échouer aucun
/// test fonctionnel, et une redirection HTTPS qui ne se déclenche pas laisse
/// l'application parfaitement utilisable. Ces défauts n'ont donc <b>aucun</b>
/// symptôme : seuls des tests écrits exprès pour eux les voient.
///
/// D'où deux exigences dans ce fichier. Chaque interdiction porte un
/// <b>témoin</b> — la requête voisine qui doit réussir — sinon un middleware qui
/// refuse tout passerait au vert. Et les comportements désactivés en
/// développement sont éprouvés sur un hôte en <c>Production</c>, sinon ils
/// seraient constatés absents et déclarés conformes.
/// </summary>
[Collection(CollectionApi.Nom)]
public class SurfaceHttpTests
{
    private readonly FixtureApi _fixture;

    public SurfaceHttpTests(FixtureApi fixture) => _fixture = fixture;

    // -----------------------------------------------------------------------
    // En-têtes de sécurité
    // -----------------------------------------------------------------------

    /// <summary>
    /// Les quatre en-têtes de §16 sur une réponse authentifiée ordinaire.
    /// </summary>
    [Fact]
    public async Task Les_en_tetes_de_securite_sont_sur_une_reponse_ordinaire()
    {
        await _fixture.ReinitialiserAsync();
        var client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        var reponse = await client.GetAsync("/api/auth/moi");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        AsserterEnTetes(reponse);
    }

    /// <summary>
    /// Et sur un 401, ce qui est le cas qui compte.
    ///
    /// Un en-tête posé par un filtre de contrôleur manquerait toutes les réponses
    /// produites avant que le contrôleur soit atteint — c'est-à-dire exactement
    /// celles qu'obtient quelqu'un qui n'a rien à faire là. D'où la place du
    /// middleware, avant l'authentification.
    /// </summary>
    [Fact]
    public async Task Les_en_tetes_de_securite_sont_aussi_sur_un_refus()
    {
        var client = _fixture.ClientAnonymeSansRedirection();

        var reponse = await client.GetAsync("/api/auth/moi");

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
        AsserterEnTetes(reponse);
    }

    /// <summary>
    /// Et sur la sonde, qui est anonyme et n'est pas un contrôleur.
    /// </summary>
    [Fact]
    public async Task Les_en_tetes_de_securite_sont_aussi_sur_la_sonde()
    {
        var client = _fixture.ClientAnonyme();

        AsserterEnTetes(await client.GetAsync("/api/sante"));
    }

    /// <summary>
    /// La politique de l'API n'autorise aucune ressource.
    ///
    /// ⚠️ Ce n'est pas la politique du frontend, qui doit au contraire ouvrir
    /// <c>style-src</c> et <c>font-src</c> sur Google Fonts — mais l'API ne sert
    /// aucun document. Ce test garde la distinction : y ajouter une origine de
    /// police le ferait rougir, ce qui est l'effet voulu.
    /// </summary>
    [Fact]
    public void La_politique_de_contenu_de_l_api_n_autorise_rien()
    {
        Assert.Contains("default-src 'none'", EnTetesSecurite.Politique);
        Assert.Contains("frame-ancestors 'none'", EnTetesSecurite.Politique);

        Assert.DoesNotContain("fonts.googleapis.com", EnTetesSecurite.Politique);
        Assert.DoesNotContain("unsafe-inline", EnTetesSecurite.Politique);
    }

    private static void AsserterEnTetes(HttpResponseMessage reponse)
    {
        Assert.Equal(EnTetesSecurite.Politique, Entete(reponse, "Content-Security-Policy"));
        Assert.Equal("nosniff", Entete(reponse, "X-Content-Type-Options"));
        Assert.Equal("no-referrer", Entete(reponse, "Referrer-Policy"));
        Assert.Equal("DENY", Entete(reponse, "X-Frame-Options"));
    }

    private static string? Entete(HttpResponseMessage reponse, string nom) =>
        reponse.Headers.TryGetValues(nom, out var valeurs) ? string.Join(", ", valeurs) : null;

    // -----------------------------------------------------------------------
    // CORS
    // -----------------------------------------------------------------------

    /// <summary>
    /// Une origine déclarée obtient l'autorisation, identifiants compris.
    ///
    /// Le témoin de la paire : sans lui, une politique qui refuse absolument tout
    /// ferait passer le test suivant.
    /// </summary>
    [Fact]
    public async Task Une_origine_declaree_est_autorisee_avec_les_identifiants()
    {
        var client = _fixture.ClientAnonyme();
        var demande = new HttpRequestMessage(HttpMethod.Get, "/api/sante");
        demande.Headers.Add("Origin", FabriqueApi.OrigineDeclaree);

        var reponse = await client.SendAsync(demande);

        Assert.Equal(FabriqueApi.OrigineDeclaree, Entete(reponse, "Access-Control-Allow-Origin"));

        // Sans cet en-tête le navigateur jette la réponse, et l'authentification
        // par cookie ne peut pas fonctionner entre origines.
        Assert.Equal("true", Entete(reponse, "Access-Control-Allow-Credentials"));
    }

    /// <summary>Une origine non déclarée n'obtient rien.</summary>
    [Fact]
    public async Task Une_origine_non_declaree_n_obtient_aucune_autorisation()
    {
        var client = _fixture.ClientAnonyme();
        var demande = new HttpRequestMessage(HttpMethod.Get, "/api/sante");
        demande.Headers.Add("Origin", FabriqueApi.OrigineInconnue);

        var reponse = await client.SendAsync(demande);

        Assert.Null(Entete(reponse, "Access-Control-Allow-Origin"));
        Assert.Null(Entete(reponse, "Access-Control-Allow-Credentials"));
    }

    /// <summary>
    /// Et son préambule non plus, ce qui est le vrai verrou.
    ///
    /// Le navigateur interroge avant d'écrire : un <c>OPTIONS</c> sans
    /// autorisation en retour empêche la requête réelle de partir. C'est là que
    /// l'écriture inter-origine est arrêtée, pas sur la réponse.
    /// </summary>
    [Fact]
    public async Task Le_preambule_d_une_origine_non_declaree_est_refuse()
    {
        var client = _fixture.ClientAnonyme();

        var demande = new HttpRequestMessage(HttpMethod.Options, "/api/agents");
        demande.Headers.Add("Origin", FabriqueApi.OrigineInconnue);
        demande.Headers.Add("Access-Control-Request-Method", "POST");

        var reponse = await client.SendAsync(demande);

        Assert.Null(Entete(reponse, "Access-Control-Allow-Origin"));
        Assert.Null(Entete(reponse, "Access-Control-Allow-Methods"));
    }

    /// <summary>Témoin : le préambule d'une origine déclarée, lui, répond.</summary>
    [Fact]
    public async Task Le_preambule_d_une_origine_declaree_est_accepte()
    {
        var client = _fixture.ClientAnonyme();

        var demande = new HttpRequestMessage(HttpMethod.Options, "/api/agents");
        demande.Headers.Add("Origin", FabriqueApi.OrigineDeclaree);
        demande.Headers.Add("Access-Control-Request-Method", "POST");

        var reponse = await client.SendAsync(demande);

        Assert.Equal(FabriqueApi.OrigineDeclaree, Entete(reponse, "Access-Control-Allow-Origin"));
        Assert.Contains("POST", Entete(reponse, "Access-Control-Allow-Methods") ?? string.Empty);
    }

    // -----------------------------------------------------------------------
    // HTTPS, hors développement
    // -----------------------------------------------------------------------

    /// <summary>
    /// Hors développement, une requête en clair est redirigée.
    ///
    /// ⚠️ Éprouvé sur un hôte en <c>Production</c>. Sur l'hôte de développement de
    /// la série, la redirection est volontairement désactivée : le test y
    /// constaterait son absence et n'aurait rien prouvé.
    /// </summary>
    [Fact]
    public async Task Hors_developpement_une_requete_en_clair_est_redirigee()
    {
        using var fabrique = new FabriqueApi("Production");
        var client = fabrique.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://klean-production.test/"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        var reponse = await client.GetAsync("/api/sante");

        Assert.Equal(HttpStatusCode.TemporaryRedirect, reponse.StatusCode);
        Assert.StartsWith("https://", reponse.Headers.Location?.ToString());
    }

    /// <summary>
    /// Témoin : en développement elle passe.
    ///
    /// Ce n'est pas une tolérance de confort. Le backend local n'écoute qu'en
    /// clair derrière le proxy Vite ; rediriger romprait ce chemin sans rien
    /// protéger, le saut étant en loopback sur la même machine.
    /// </summary>
    [Fact]
    public async Task En_developpement_une_requete_en_clair_passe()
    {
        var client = _fixture.Fabrique.CreerClientHttp();

        var reponse = await client.GetAsync("/api/sante");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }

    /// <summary>
    /// Hors développement, HSTS est annoncé.
    ///
    /// ⚠️ Sur un hôte nommé <c>localhost</c> l'en-tête serait absent : HSTS exclut
    /// par défaut les noms de boucle locale, et c'est une bonne chose — une
    /// politique HSTS posée par erreur sur son propre poste ne se retire pas. D'où
    /// le nom d'hôte de test, qui n'est pas cosmétique.
    /// </summary>
    [Fact]
    public async Task Hors_developpement_hsts_est_annonce()
    {
        using var fabrique = new FabriqueApi("Production");
        var client = fabrique.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://klean-production.test/"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        var reponse = await client.GetAsync("/api/sante");

        var hsts = Entete(reponse, "Strict-Transport-Security");

        Assert.NotNull(hsts);
        Assert.Contains("max-age=", hsts);
    }

    /// <summary>Témoin : en développement, aucun HSTS.</summary>
    [Fact]
    public async Task En_developpement_hsts_n_est_pas_annonce()
    {
        var client = _fixture.ClientAnonyme();

        var reponse = await client.GetAsync("/api/sante");

        Assert.Null(Entete(reponse, "Strict-Transport-Security"));
    }

    // -----------------------------------------------------------------------
    // Sonde de disponibilité
    // -----------------------------------------------------------------------

    /// <summary>
    /// La sonde interrogeait seulement le processus ; elle interroge la base.
    ///
    /// Une API qui répond pendant que PostgreSQL est injoignable est un processus
    /// vivant et un service mort. L'ancienne version renvoyait « ok » dans ce cas,
    /// et un orchestrateur y aurait envoyé du trafic.
    /// </summary>
    [Fact]
    public async Task La_sonde_rend_compte_de_l_etat_de_la_base()
    {
        var client = _fixture.ClientAnonyme();

        var reponse = await client.GetAsync("/api/sante");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);

        using var corps = JsonDocument.Parse(await reponse.Content.ReadAsStringAsync());

        Assert.Equal("ok", corps.RootElement.GetProperty("statut").GetString());
        Assert.Equal("joignable", corps.RootElement.GetProperty("baseDeDonnees").GetString());
    }

    /// <summary>La sonde reste ouverte sans authentification : c'est sa raison d'être.</summary>
    [Fact]
    public async Task La_sonde_reste_anonyme()
    {
        var reponse = await _fixture.ClientAnonymeSansRedirection().GetAsync("/api/sante");

        Assert.NotEqual(HttpStatusCode.Unauthorized, reponse.StatusCode);
        Assert.NotEqual(HttpStatusCode.Found, reponse.StatusCode);
    }

    // -----------------------------------------------------------------------
    // Limite de taille de corps
    // -----------------------------------------------------------------------

    /// <summary>
    /// Un corps démesuré est refusé.
    ///
    /// Sans plafond, n'importe qui peut faire allouer trente mégaoctets par
    /// requête à l'API — le défaut de Kestrel — sur un endpoint dont le plus gros
    /// corps légitime pèse quelques kilo-octets. C'est le pendant, côté taille, de
    /// la pagination de l'étape 8.
    /// </summary>
    [Fact]
    public async Task Un_corps_demesure_est_refuse()
    {
        await _fixture.ReinitialiserAsync();
        var client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        // Deux mégaoctets de JSON valide, au-delà du mégaoctet autorisé.
        var enorme = new string('a', 2 * 1024 * 1024);
        var corps = new StringContent(
            $"{{\"nom\":\"{enorme}\",\"type\":\"Syndic\"}}",
            Encoding.UTF8, "application/json");

        var reponse = await client.PostAsync("/api/clients", corps);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, reponse.StatusCode);
    }

    /// <summary>
    /// Témoin : un corps de taille normale passe.
    ///
    /// Sans lui, un plafond fixé par erreur à zéro octet ferait passer le test
    /// précédent en refusant toutes les écritures de l'application.
    /// </summary>
    [Fact]
    public async Task Un_corps_de_taille_normale_passe()
    {
        await _fixture.ReinitialiserAsync();
        var client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        var reponse = await client.PostAsJsonAsync("/api/clients",
            new { nom = "Syndic Normal", type = "Syndic" });

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
    }
}
