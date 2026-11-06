using System.Net;
using System.Net.Http.Json;
using Klean.Api.DTOs;
using Klean.Api.Services;
using Klean.Api.Tests.Infrastructure;

namespace Klean.Api.Tests;

/// <summary>
/// Réinitialisation de mot de passe.
///
/// Avant cette étape, aucun parcours n'existait : un responsable qui oubliait son
/// mot de passe n'avait strictement aucune issue, et un administrateur qui le
/// réinitialisait à la main était pire — il connaissait alors le mot de passe d'un
/// collègue.
/// </summary>
[Collection(CollectionApi.Nom)]
public class MotDePasseTests : IAsyncLifetime
{
    private const string NouveauMotDePasse = "NouveauSecret2026";

    private readonly FixtureApi _fixture;

    public MotDePasseTests(FixtureApi fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ReinitialiserAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------- le parcours

    [Fact]
    public async Task Le_parcours_de_reinitialisation_fonctionne_de_bout_en_bout()
    {
        var demande = await Demander(FixtureApi.AdminAlpha);
        Assert.Equal(HttpStatusCode.Accepted, demande.StatusCode);

        var courriel = _fixture.Fabrique.Courriels.Dernier;
        Assert.Equal(FixtureApi.AdminAlpha, courriel.Destinataire);
        Assert.Contains("KLEAN", courriel.Sujet);

        var jeton = ServiceCourrielDeTest.JetonDuLien(courriel.Corps);

        var reinitialisation = await Reinitialiser(FixtureApi.AdminAlpha, jeton, NouveauMotDePasse);
        Assert.Equal(HttpStatusCode.NoContent, reinitialisation.StatusCode);

        // L'ancien mot de passe ne vaut plus rien, le nouveau ouvre la session.
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Connexion(FixtureApi.AdminAlpha, FixtureApi.MotDePasse)).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await Connexion(FixtureApi.AdminAlpha, NouveauMotDePasse)).StatusCode);
    }

    [Fact]
    public async Task Le_lien_porte_l_adresse_et_le_jeton_encodes()
    {
        // Le jeton d'Identity contient des caractères réservés dans une URL. Sans
        // encodage, le lien se casse silencieusement et le parcours échoue chez
        // l'utilisateur, pas ici.
        await Demander(FixtureApi.AdminAlpha);

        var corps = _fixture.Fabrique.Courriels.Dernier.Corps;

        Assert.Equal(FixtureApi.AdminAlpha, ServiceCourrielDeTest.AdresseDuLien(corps));
        Assert.Contains("/reinitialiser?email=", corps);
        Assert.Contains("valable une heure", corps);

        // Décodé, le jeton doit être accepté : la preuve que l'encodage est correct.
        var jeton = ServiceCourrielDeTest.JetonDuLien(corps);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Reinitialiser(FixtureApi.AdminAlpha, jeton, NouveauMotDePasse)).StatusCode);
    }

    [Fact]
    public async Task Le_jeton_ne_sert_qu_une_fois()
    {
        await Demander(FixtureApi.AdminAlpha);
        var jeton = ServiceCourrielDeTest.JetonDuLien(_fixture.Fabrique.Courriels.Dernier.Corps);

        Assert.Equal(HttpStatusCode.NoContent,
            (await Reinitialiser(FixtureApi.AdminAlpha, jeton, NouveauMotDePasse)).StatusCode);

        // Identity régénère le SecurityStamp à la réussite, ce qui périme le jeton.
        var seconde = await Reinitialiser(FixtureApi.AdminAlpha, jeton, "EncoreAutre2026");

        Assert.Equal(HttpStatusCode.BadRequest, seconde.StatusCode);
        Assert.Contains("invalide ou a expiré", await seconde.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task La_reinitialisation_revoque_toutes_les_sessions_en_cours()
    {
        // Quelqu'un qui réinitialise son mot de passe le fait souvent parce qu'il
        // craint un accès illégitime. Laisser les sessions ouvertes serait répondre
        // à côté de la question posée.
        var session = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/api/auth/moi")).StatusCode);

        await Demander(FixtureApi.AdminAlpha);
        var jeton = ServiceCourrielDeTest.JetonDuLien(_fixture.Fabrique.Courriels.Dernier.Corps);
        await Reinitialiser(FixtureApi.AdminAlpha, jeton, NouveauMotDePasse);

        // Jeton d'accès : périmé par le nouveau SecurityStamp.
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/auth/moi")).StatusCode);

        // Jeton de rafraîchissement : révoqué explicitement.
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await session.PostAsync("/api/auth/rafraichir", null)).StatusCode);
    }

    // ------------------------------------------------------ absence de fuite

    [Fact]
    public async Task Une_adresse_inconnue_recoit_202_et_aucun_courriel()
    {
        // Distinguer ferait de cet endpoint un vérificateur d'adresses ouvert à
        // tous : on y saisit une adresse, la réponse dit si elle a un compte.
        var reponse = await Demander("personne@nulle-part.local");

        Assert.Equal(HttpStatusCode.Accepted, reponse.StatusCode);
        Assert.Empty(_fixture.Fabrique.Courriels.Messages);
    }

    [Fact]
    public async Task Un_compte_desactive_recoit_202_et_aucun_courriel()
    {
        await _fixture.DesactiverCompteAsync(FixtureApi.AdminAlpha);

        var reponse = await Demander(FixtureApi.AdminAlpha);

        Assert.Equal(HttpStatusCode.Accepted, reponse.StatusCode);
        Assert.Empty(_fixture.Fabrique.Courriels.Messages);
    }

    [Fact]
    public async Task Une_adresse_inconnue_et_un_jeton_faux_donnent_le_meme_message()
    {
        var inconnue = await Reinitialiser("personne@nulle-part.local", "jeton-invente", NouveauMotDePasse);
        var fausse = await Reinitialiser(FixtureApi.AdminAlpha, "jeton-invente", NouveauMotDePasse);

        Assert.Equal(HttpStatusCode.BadRequest, inconnue.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, fausse.StatusCode);

        // Le message identique est le point : sinon la réponse dirait « ce compte
        // existe, mais votre jeton est faux ».
        Assert.Equal(
            await inconnue.Content.ReadAsStringAsync(),
            await fausse.Content.ReadAsStringAsync());
    }

    // -------------------------------------------------------------- SMTP absent

    [Fact]
    public async Task Sans_SMTP_configure_la_demande_repond_503_avec_la_marche_a_suivre()
    {
        // CLAUDE.md §2 interdit d'imposer un service tiers : l'absence de SMTP ne
        // doit pas empêcher KLEAN de tourner. Elle désactive ce seul parcours, et
        // le dit — plutôt que de prétendre avoir envoyé un courriel.
        _fixture.Fabrique.Courriels.Configure = false;

        var reponse = await Demander(FixtureApi.AdminAlpha);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, reponse.StatusCode);

        var corps = await reponse.Content.ReadAsStringAsync();
        Assert.Contains("Courriel:Hote", corps);
        Assert.Contains("administrateur", corps);
        Assert.Empty(_fixture.Fabrique.Courriels.Messages);
    }

    [Fact]
    public async Task Le_503_ne_depend_pas_de_l_adresse_saisie()
    {
        // Le contrôle passe avant toute recherche de compte : le 503 ne révèle donc
        // rien, pas même par différence de traitement.
        _fixture.Fabrique.Courriels.Configure = false;

        var connue = await Demander(FixtureApi.AdminAlpha);
        var inconnue = await Demander("personne@nulle-part.local");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, connue.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, inconnue.StatusCode);
    }

    // ----------------------------------------------------- messages en français

    [Fact]
    public async Task Un_mot_de_passe_trop_court_donne_la_regle_en_francais()
    {
        // Les règles de mot de passe doivent être dites, contrairement au jeton :
        // l'utilisateur ne peut pas corriger ce qu'il ne connaît pas.
        await Demander(FixtureApi.AdminAlpha);
        var jeton = ServiceCourrielDeTest.JetonDuLien(_fixture.Fabrique.Courriels.Dernier.Corps);

        var reponse = await Reinitialiser(FixtureApi.AdminAlpha, jeton, "Court1");

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);

        var corps = await reponse.Content.ReadAsStringAsync();
        Assert.Contains("mot de passe", corps);
        Assert.DoesNotContain("Password", corps);
        Assert.DoesNotContain("must", corps);
    }

    [Fact]
    public async Task Un_mot_de_passe_sans_chiffre_donne_la_regle_en_francais()
    {
        await Demander(FixtureApi.AdminAlpha);
        var jeton = ServiceCourrielDeTest.JetonDuLien(_fixture.Fabrique.Courriels.Dernier.Corps);

        var reponse = await Reinitialiser(FixtureApi.AdminAlpha, jeton, "SansAucunChiffre");
        var corps = await reponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("chiffre", corps);
        Assert.DoesNotContain("digit", corps);
    }

    [Fact]
    public async Task Une_adresse_en_doublon_donne_un_message_en_francais()
    {
        // Les descriptions d'Identity traversent l'API telles quelles : sans
        // descripteur, une interface française renvoie de l'anglais.
        var admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        var doublon = await admin.PostAsJsonAsync("/api/auth/comptes", new CompteCreateDto
        {
            Email = FixtureApi.AdminAlpha,
            Nom = "Doublon",
            Role = "Responsable"
        });

        Assert.Equal(HttpStatusCode.BadRequest, doublon.StatusCode);

        var corps = await doublon.Content.ReadAsStringAsync();
        Assert.Contains("déjà utilisée", corps);
        Assert.DoesNotContain("already taken", corps);
    }

    // ------------------------------------------------------------- outillage

    private Task<HttpResponseMessage> Demander(string email) =>
        _fixture.ClientAnonyme().PostAsJsonAsync("/api/auth/mot-de-passe-oublie",
            new MotDePasseOublieDto { Email = email });

    private Task<HttpResponseMessage> Reinitialiser(string email, string jeton, string motDePasse) =>
        _fixture.ClientAnonyme().PostAsJsonAsync("/api/auth/reinitialiser",
            new ReinitialiserDto { Email = email, Jeton = jeton, NouveauMotDePasse = motDePasse });

    private Task<HttpResponseMessage> Connexion(string email, string motDePasse) =>
        _fixture.ConnexionCompleteAsync(_fixture.ClientAnonyme(), email, motDePasse);
}
