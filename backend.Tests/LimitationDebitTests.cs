using System.Net;
using System.Net.Http.Json;
using Klean.Api.DTOs;
using Klean.Api.Services;
using Klean.Api.Tests.Infrastructure;

namespace Klean.Api.Tests;

/// <summary>
/// Limitation de débit.
///
/// Le verrouillage d'Identity est <b>par compte</b> : cinq échecs, quinze minutes.
/// Il ne ralentit donc en rien le scénario inverse — pulvériser un seul mot de
/// passe probable sur cent comptes, un essai chacun. Les deux dimensions sont
/// testées séparément, parce qu'une seule des deux laisse un trou entier.
/// </summary>
[Collection(CollectionApi.Nom)]
public class LimitationDebitTests : IAsyncLifetime
{
    private readonly FixtureApi _fixture;

    public LimitationDebitTests(FixtureApi fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ReinitialiserAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------- par compte

    [Fact]
    public async Task Trop_de_connexions_sur_le_meme_compte_repond_429()
    {
        var client = _fixture.ClientAnonyme();

        // Le plafond par compte se déclenche avant celui par adresse.
        for (var essai = 1; essai <= PlafondsDebit.ConnexionsParCompte; essai++)
        {
            var reponse = await Connexion(client, FixtureApi.AdminAlpha, "MauvaisMotDePasse1");

            Assert.NotEqual(HttpStatusCode.TooManyRequests, reponse.StatusCode);
        }

        var suivante = await Connexion(client, FixtureApi.AdminAlpha, "MauvaisMotDePasse1");

        Assert.Equal(HttpStatusCode.TooManyRequests, suivante.StatusCode);
    }

    [Fact]
    public async Task Le_plafond_par_compte_bloque_aussi_le_bon_mot_de_passe()
    {
        // Sinon la limitation ne coûte rien à un attaquant qui trouve : il essaie
        // jusqu'au blocage, puis passe malgré tout.
        var client = _fixture.ClientAnonyme();

        for (var essai = 1; essai <= PlafondsDebit.ConnexionsParCompte + 1; essai++)
        {
            await Connexion(client, FixtureApi.AdminAlpha, "MauvaisMotDePasse1");
        }

        var avecLeBon = await Connexion(client, FixtureApi.AdminAlpha, FixtureApi.MotDePasse);

        Assert.Equal(HttpStatusCode.TooManyRequests, avecLeBon.StatusCode);
    }

    [Fact]
    public async Task Un_autre_compte_n_est_pas_bloque_par_le_premier()
    {
        // Le compteur est par compte : bloquer tout le monde parce qu'un compte est
        // attaqué serait un déni de service offert à l'attaquant.
        var client = _fixture.ClientAnonyme();

        for (var essai = 1; essai <= PlafondsDebit.ConnexionsParCompte + 1; essai++)
        {
            await Connexion(client, FixtureApi.AdminAlpha, "MauvaisMotDePasse1");
        }

        var autre = await Connexion(client, FixtureApi.AdminBeta, FixtureApi.MotDePasse);

        Assert.Equal(HttpStatusCode.OK, autre.StatusCode);
    }

    // ------------------------------------------------------------ par adresse

    [Fact]
    public async Task Trop_de_connexions_depuis_la_meme_adresse_repond_429()
    {
        // Une adresse différente par essai, pour ne déclencher que la dimension
        // adresse : c'est exactement la forme du bourrage d'identifiants, que le
        // verrouillage par compte d'Identity ne ralentit pas du tout.
        var client = _fixture.ClientAnonyme();

        for (var essai = 1; essai <= PlafondsDebit.ConnexionsParAdresse; essai++)
        {
            var reponse = await Connexion(client, $"cible{essai}@test.local", "MotDePasseProbable1");

            Assert.NotEqual(HttpStatusCode.TooManyRequests, reponse.StatusCode);
        }

        var suivante = await Connexion(client, "cible-de-trop@test.local", "MotDePasseProbable1");

        Assert.Equal(HttpStatusCode.TooManyRequests, suivante.StatusCode);
    }

    // ------------------------------------------------------ mot de passe oublié

    [Fact]
    public async Task Trop_de_demandes_de_mot_de_passe_oublie_repond_429()
    {
        // Plafond serré : chaque appel réussi envoie un courriel, donc un plafond
        // large transforme l'endpoint en outil de harcèlement par boîte de réception.
        var client = _fixture.ClientAnonyme();

        for (var essai = 1; essai <= PlafondsDebit.OublisParAdresse; essai++)
        {
            var reponse = await Oubli(client, FixtureApi.AdminAlpha);

            Assert.Equal(HttpStatusCode.Accepted, reponse.StatusCode);
        }

        var suivante = await Oubli(client, FixtureApi.AdminAlpha);

        Assert.Equal(HttpStatusCode.TooManyRequests, suivante.StatusCode);
        Assert.Equal(PlafondsDebit.OublisParAdresse, _fixture.Fabrique.Courriels.Messages.Count);
    }

    [Fact]
    public async Task Le_message_de_429_ne_dit_pas_quelle_limite_a_ete_atteinte()
    {
        // Préciser la dimension renseignerait un attaquant sur la forme de la
        // défense, donc sur la façon de la contourner.
        var client = _fixture.ClientAnonyme();

        for (var essai = 1; essai <= PlafondsDebit.ConnexionsParCompte + 1; essai++)
        {
            await Connexion(client, FixtureApi.AdminAlpha, "MauvaisMotDePasse1");
        }

        var corps = await (await Connexion(client, FixtureApi.AdminAlpha, "x"))
            .Content.ReadAsStringAsync();

        Assert.Contains("Trop de tentatives", corps);
        Assert.DoesNotContain("adresse", corps);
        Assert.DoesNotContain("compte", corps);
    }

    // ------------------------------------------------- le compteur lui-même

    [Fact]
    public void Chaque_cle_a_son_propre_compteur()
    {
        var limiteur = new LimiteurDebit();
        var fenetre = TimeSpan.FromMinutes(1);

        Assert.True(limiteur.Autoriser("a", 1, fenetre));
        Assert.False(limiteur.Autoriser("a", 1, fenetre));
        Assert.True(limiteur.Autoriser("b", 1, fenetre));
    }

    [Fact]
    public async Task Une_tentative_refusee_n_est_pas_comptee()
    {
        // Sinon un client bloqué se prolonge indéfiniment sa propre punition en
        // réessayant, et la limitation devient un verrouillage définitif.
        var limiteur = new LimiteurDebit();
        var fenetre = TimeSpan.FromSeconds(1);

        Assert.True(limiteur.Autoriser("k", 1, fenetre));

        await Task.Delay(800);
        Assert.False(limiteur.Autoriser("k", 1, fenetre));

        // 300 ms plus tard, la tentative de t=0 est sortie de la fenêtre. Celle de
        // t=800 ms y serait encore si les refus étaient comptés.
        await Task.Delay(300);
        Assert.True(limiteur.Autoriser("k", 1, fenetre));
    }

    [Fact]
    public void La_fenetre_glisse_au_lieu_d_etre_fixe()
    {
        // Une fenêtre fixe laisse passer deux fois le quota à cheval sur sa
        // frontière, ce qui est exactement le moment que choisit un script.
        var limiteur = new LimiteurDebit();
        var fenetre = TimeSpan.FromMinutes(5);

        Assert.True(limiteur.Autoriser("k", 2, fenetre));
        Assert.True(limiteur.Autoriser("k", 2, fenetre));
        Assert.False(limiteur.Autoriser("k", 2, fenetre));

        limiteur.Dispose();
    }

    // ------------------------------------------------------------- outillage

    private static Task<HttpResponseMessage> Connexion(HttpClient client, string email, string motDePasse) =>
        client.PostAsJsonAsync("/api/auth/connexion",
            new ConnexionDto { Email = email, MotDePasse = motDePasse });

    private static Task<HttpResponseMessage> Oubli(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/auth/mot-de-passe-oublie",
            new MotDePasseOublieDto { Email = email });
}
