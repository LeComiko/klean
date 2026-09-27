using System.Net;
using System.Net.Http.Json;
using Klean.Api.Data;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Klean.Api.Services;
using Klean.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Klean.Api.Tests;

/// <summary>
/// Session révocable : cookies, rotation, révocation, CSRF.
///
/// C'est le lot où la sécurité ne se lit pas dans le code métier mais dans des
/// attributs de cookie et un ordre de middleware — donc exactement le lot où un
/// test qui passe pour la mauvaise raison est le plus probable. Plusieurs tests
/// portent un <b>témoin</b> : la version qui doit réussir, à côté de celle qui
/// doit échouer.
/// </summary>
[Collection(CollectionApi.Nom)]
public class SessionTests : IAsyncLifetime
{
    private readonly FixtureApi _fixture;

    public SessionTests(FixtureApi fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ReinitialiserAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------------------------------------------------------- cookies

    [Fact]
    public async Task Les_cookies_de_session_sont_HttpOnly_Secure_et_SameSite_Strict()
    {
        var reponse = await Connexion();

        foreach (var nom in new[] { ServiceSession.CookieAcces, ServiceSession.CookieRafraichissement })
        {
            var entete = FixtureApi.EnteteDuCookie(reponse, nom);
            Assert.NotNull(entete);
            Assert.Contains("httponly", entete.ToLowerInvariant());
            Assert.Contains("secure", entete.ToLowerInvariant());
            Assert.Contains("samesite=strict", entete.ToLowerInvariant());
        }
    }

    [Fact]
    public async Task Le_cookie_anti_CSRF_est_lisible_par_le_script_et_lui_seul()
    {
        // C'est la moitié visible du double-submit : le script doit pouvoir la
        // recopier dans un en-tête. Elle n'authentifie rien à elle seule.
        var entete = FixtureApi.EnteteDuCookie(await Connexion(), ServiceSession.CookieCsrf);

        Assert.NotNull(entete);
        Assert.DoesNotContain("httponly", entete.ToLowerInvariant());
        Assert.Contains("secure", entete.ToLowerInvariant());
        Assert.Contains("samesite=strict", entete.ToLowerInvariant());
    }

    [Fact]
    public async Task Le_cookie_de_rafraichissement_est_limite_aux_routes_d_authentification()
    {
        // Il n'a aucune raison d'accompagner un POST sur /api/clients : ne pas
        // l'y envoyer réduit d'autant sa surface d'exposition.
        var entete = FixtureApi.EnteteDuCookie(await Connexion(), ServiceSession.CookieRafraichissement);

        Assert.NotNull(entete);
        Assert.Contains("path=/api/auth", entete.ToLowerInvariant());
    }

    [Fact]
    public async Task L_entete_Authorization_n_est_plus_accepte()
    {
        // Aucun jeton n'est remis au client : garder ce chemin serait garder un
        // second mécanisme d'authentification à auditer, sans utilisateur.
        var jeton = FixtureApi.ValeurDuCookie(await Connexion(), ServiceSession.CookieAcces);
        Assert.NotNull(jeton);

        var client = _fixture.ClientSansCookies();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jeton);

        var reponse = await client.GetAsync("/api/auth/moi");

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    // -------------------------------------------------------------- révocation

    [Fact]
    public async Task Desactiver_un_compte_coupe_l_acces_a_la_requete_suivante()
    {
        // La validation centrale de l'étape. Avant, le jeton restait pleinement
        // valable huit heures après la désactivation du compte.
        var client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/moi")).StatusCode);

        await _fixture.DesactiverCompteAsync(FixtureApi.AdminAlpha);

        // Même jeton, même cookie, requête suivante : coupé.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/moi")).StatusCode);
    }

    [Fact]
    public async Task Changer_le_mot_de_passe_invalide_les_jetons_deja_emis()
    {
        var client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/moi")).StatusCode);

        await _fixture.AvecServicesAsync(async services =>
        {
            var utilisateurs = services.GetRequiredService<UserManager<Utilisateur>>();
            var compte = await utilisateurs.FindByEmailAsync(FixtureApi.AdminAlpha);

            var resultat = await utilisateurs.ChangePasswordAsync(
                compte!, FixtureApi.MotDePasse, "AutreMotDePasse2026");

            Assert.True(resultat.Succeeded, string.Join(" ; ", resultat.Errors.Select(e => e.Description)));
        });

        // Identity régénère le SecurityStamp : l'empreinte du jeton est périmée.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/moi")).StatusCode);
    }

    [Fact]
    public async Task Un_compte_desactive_ne_peut_plus_rafraichir()
    {
        var client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        await _fixture.DesactiverCompteAsync(FixtureApi.AdminAlpha);

        var reponse = await client.PostAsync("/api/auth/rafraichir", null);

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
        Assert.Contains("désactivé", await reponse.Content.ReadAsStringAsync());
    }

    // ----------------------------------------------------------------- rotation

    [Fact]
    public async Task Rafraichir_renouvelle_la_session_et_fait_tourner_le_jeton()
    {
        var client = _fixture.ClientAnonyme();
        var connexion = await Connexion(client);
        FixtureApi.ArmerCsrf(client, connexion);

        var premier = FixtureApi.ValeurDuCookie(connexion, ServiceSession.CookieRafraichissement);

        var reponse = await client.PostAsync("/api/auth/rafraichir", null);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);

        var session = await reponse.Content.ReadFromJsonAsync<SessionDto>();
        Assert.NotNull(session);
        Assert.Equal(FixtureApi.AdminAlpha, session.Profil.Email);

        var second = FixtureApi.ValeurDuCookie(reponse, ServiceSession.CookieRafraichissement);
        Assert.NotNull(second);
        Assert.NotEqual(premier, second);

        // Et la session reste utilisable avec les nouveaux cookies.
        FixtureApi.ArmerCsrf(client, reponse);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/moi")).StatusCode);
    }

    [Fact]
    public async Task La_rotation_ne_prolonge_pas_l_echeance_absolue_de_la_session()
    {
        // Une session dure au plus quatorze jours. Si la rotation repoussait
        // l'échéance, une session active ne finirait jamais.
        var client = _fixture.ClientAnonyme();
        FixtureApi.ArmerCsrf(client, await Connexion(client));

        var avant = await EcheanceUniqueAsync();

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/auth/rafraichir", null)).StatusCode);

        var apres = await EcheancesActivesAsync();

        Assert.Equal(avant, Assert.Single(apres));
    }

    [Fact]
    public async Task Rejouer_un_jeton_de_rafraichissement_coupe_toute_la_session()
    {
        var client = _fixture.ClientAnonyme();
        var connexion = await Connexion(client);
        FixtureApi.ArmerCsrf(client, connexion);

        var premier = FixtureApi.ValeurDuCookie(connexion, ServiceSession.CookieRafraichissement);
        Assert.NotNull(premier);

        var rotation = await client.PostAsync("/api/auth/rafraichir", null);
        Assert.Equal(HttpStatusCode.OK, rotation.StatusCode);

        // Rejeu du premier jeton, déjà consommé.
        var rejeu = await RafraichirManuellement(premier);

        Assert.Equal(HttpStatusCode.Unauthorized, rejeu.StatusCode);
        Assert.Contains("déjà servi", await rejeu.Content.ReadAsStringAsync());

        // Et la conséquence qui donne son sens à la rotation : le jeton légitime,
        // celui du porteur, est coupé lui aussi. Sans cela un attaquant ayant
        // copié un jeton s'en servirait tranquillement en parallèle.
        FixtureApi.ArmerCsrf(client, rotation);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsync("/api/auth/rafraichir", null)).StatusCode);
    }

    [Fact]
    public async Task Un_jeton_de_rafraichissement_expire_ne_delivre_plus_rien()
    {
        var client = _fixture.ClientAnonyme();
        FixtureApi.ArmerCsrf(client, await Connexion(client));

        // Vieillir la session plutôt qu'attendre quatorze jours.
        await _fixture.AvecServicesAsync(async services =>
        {
            var db = services.GetRequiredService<KleanDbContext>();
            var jetons = await db.JetonsRafraichissement.ToListAsync();

            foreach (var jeton in jetons)
            {
                jeton.DateExpiration = DateTime.UtcNow.AddMinutes(-1);
            }

            await db.SaveChangesAsync();
        });

        var reponse = await client.PostAsync("/api/auth/rafraichir", null);

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task Rafraichir_sans_cookie_de_session_renvoie_401()
    {
        var reponse = await _fixture.ClientAnonyme().PostAsync("/api/auth/rafraichir", null);

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    // -------------------------------------------------------------- déconnexion

    [Fact]
    public async Task Deconnexion_revoque_le_jeton_et_efface_les_cookies()
    {
        var client = _fixture.ClientAnonyme();
        var connexion = await Connexion(client);
        FixtureApi.ArmerCsrf(client, connexion);

        var reponse = await client.PostAsync("/api/auth/deconnexion", null);
        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);

        // Les trois cookies sont effacés.
        foreach (var nom in new[]
        {
            ServiceSession.CookieAcces, ServiceSession.CookieRafraichissement, ServiceSession.CookieCsrf
        })
        {
            Assert.NotNull(FixtureApi.EnteteDuCookie(reponse, nom));
            Assert.Null(FixtureApi.ValeurDuCookie(reponse, nom));
        }

        // Et le jeton est révoqué en base : rejouer le cookie ne sert à rien.
        var rejeu = await RafraichirManuellement(
            FixtureApi.ValeurDuCookie(connexion, ServiceSession.CookieRafraichissement)!);

        Assert.Equal(HttpStatusCode.Unauthorized, rejeu.StatusCode);
        Assert.Empty(await EcheancesActivesAsync());
    }

    [Fact]
    public async Task Apres_deconnexion_un_jeton_d_acces_deja_copie_vaut_jusqu_a_son_echeance()
    {
        // ⚠️ Propriété <b>assumée</b>, pas un oubli, et ce test l'écrit noir sur
        // blanc pour qu'un changement soit délibéré.
        //
        // Un JWT signé vaut jusqu'à son expiration : rien côté serveur ne peut le
        // rétracter. La déconnexion efface les cookies du navigateur et révoque le
        // jeton de rafraîchissement, donc un porteur normal est bel et bien
        // déconnecté. Mais un jeton d'accès déjà <b>exfiltré</b> reste utilisable au
        // plus quinze minutes — contre huit heures avant cette étape.
        //
        // Le fermer complètement demanderait de lier le jeton d'accès à la session
        // et de vérifier celle-ci à chaque requête, soit une seconde lecture par
        // requête. Arbitrage consigné dans SPEC.md.
        var client = _fixture.ClientAnonyme();
        var connexion = await Connexion(client);
        FixtureApi.ArmerCsrf(client, connexion);

        var acces = FixtureApi.ValeurDuCookie(connexion, ServiceSession.CookieAcces);
        Assert.NotNull(acces);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsync("/api/auth/deconnexion", null)).StatusCode);

        // Le client normal est déconnecté : ses cookies ont été effacés.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/moi")).StatusCode);

        // Le jeton copié avant la déconnexion, lui, répond encore.
        var rejeu = new HttpRequestMessage(HttpMethod.Get, "/api/auth/moi");
        rejeu.Headers.Add("Cookie", $"{ServiceSession.CookieAcces}={acces}");

        Assert.Equal(HttpStatusCode.OK,
            (await _fixture.ClientSansCookies().SendAsync(rejeu)).StatusCode);
    }

    [Fact]
    public async Task Desactiver_le_compte_ferme_meme_un_jeton_d_acces_deja_copie()
    {
        // Le contrepoint du test précédent, et la raison d'être du contrôle par
        // requête : ce que la déconnexion ne peut pas faire, la désactivation du
        // compte le fait, immédiatement et quel que soit le porteur.
        var connexion = await Connexion();
        var acces = FixtureApi.ValeurDuCookie(connexion, ServiceSession.CookieAcces);
        Assert.NotNull(acces);

        await _fixture.DesactiverCompteAsync(FixtureApi.AdminAlpha);

        var rejeu = new HttpRequestMessage(HttpMethod.Get, "/api/auth/moi");
        rejeu.Headers.Add("Cookie", $"{ServiceSession.CookieAcces}={acces}");

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _fixture.ClientSansCookies().SendAsync(rejeu)).StatusCode);
    }

    [Fact]
    public async Task Deconnexion_sans_session_reste_silencieuse()
    {
        // Idempotente : se déconnecter deux fois n'est pas une erreur.
        var reponse = await _fixture.ClientAnonyme().PostAsync("/api/auth/deconnexion", null);

        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);
    }

    // --------------------------------------------------------------------- CSRF

    [Fact]
    public async Task Une_ecriture_sans_entete_anti_CSRF_renvoie_403()
    {
        var client = _fixture.ClientAnonyme();
        var connexion = await Connexion(client);

        // Cookies présents, en-tête volontairement absent : c'est exactement la
        // forme d'une requête déclenchée depuis un site tiers.
        var sans = await client.PostAsJsonAsync("/api/clients",
            new ClientCreateDto { Nom = "Forgé", Type = "Syndic" });

        Assert.Equal(HttpStatusCode.Forbidden, sans.StatusCode);
        Assert.Contains("CSRF", await sans.Content.ReadAsStringAsync());

        // Témoin : la même requête avec l'en-tête passe. Sans lui, le 403
        // pourrait venir d'une autorisation manquante.
        FixtureApi.ArmerCsrf(client, connexion);

        var avec = await client.PostAsJsonAsync("/api/clients",
            new ClientCreateDto { Nom = "Légitime", Type = "Syndic" });

        Assert.Equal(HttpStatusCode.Created, avec.StatusCode);
    }

    [Fact]
    public async Task Un_entete_anti_CSRF_qui_ne_correspond_pas_au_cookie_renvoie_403()
    {
        var client = _fixture.ClientAnonyme();
        await Connexion(client);

        client.DefaultRequestHeaders.Add(ServiceSession.EnteteCsrf, "valeur-inventee");

        var reponse = await client.PostAsJsonAsync("/api/clients",
            new ClientCreateDto { Nom = "Forgé", Type = "Syndic" });

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    [Fact]
    public async Task Une_lecture_ne_demande_pas_d_entete_anti_CSRF()
    {
        // Une requête sans effet de bord n'a rien à protéger, et l'exiger
        // rendrait chaque GET plus lourd sans rien gagner.
        var client = _fixture.ClientAnonyme();
        await Connexion(client);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/moi")).StatusCode);
    }

    [Fact]
    public async Task La_connexion_elle_meme_ne_demande_pas_d_entete_anti_CSRF()
    {
        // Sans cookie de session, aucune autorisation implicite n'est en jeu — et
        // l'exiger rendrait la première connexion impossible.
        var reponse = await Connexion();

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }

    [Fact]
    public async Task Le_rafraichissement_exige_l_entete_anti_CSRF()
    {
        // Le cookie d'accès a pu expirer et disparaître ; c'est la présence du
        // cookie de rafraîchissement qui déclenche la vérification.
        var client = _fixture.ClientAnonyme();
        await Connexion(client);

        var reponse = await client.PostAsync("/api/auth/rafraichir", null);

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    // ------------------------------------------------------------- outillage

    private Task<HttpResponseMessage> Connexion(HttpClient? client = null) =>
        (client ?? _fixture.ClientAnonyme()).PostAsJsonAsync("/api/auth/connexion",
            new ConnexionDto { Email = FixtureApi.AdminAlpha, MotDePasse = FixtureApi.MotDePasse });

    // -----------------------------------------------------------------------
    // Déconnexion de tous les appareils (étape 16)
    // -----------------------------------------------------------------------

    /// <summary>
    /// La déconnexion totale tue l'autre appareil <b>immédiatement</b>.
    ///
    /// C'est la brèche assumée du lot A de l'étape 12 qui se ferme ici. Une
    /// déconnexion ordinaire ne révoque que le jeton de rafraîchissement : le jeton
    /// d'accès déjà émis reste valable jusqu'à quinze minutes, puisqu'il est signé
    /// et que rien ne le rappelle.
    ///
    /// La fenêtre se ferme sans aucune lecture de plus par requête, parce que la
    /// vérification existait déjà : <c>OnTokenValidated</c> compare l'empreinte de
    /// sécurité du jeton à celle du compte à chaque appel. Il manquait le geste qui
    /// change l'empreinte.
    /// </summary>
    [Fact]
    public async Task La_deconnexion_totale_tue_immediatement_les_autres_appareils()
    {
        await _fixture.ReinitialiserAsync();

        var telephone = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
        var ordinateur = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        // Témoin de départ : les deux appareils fonctionnent. Sans cette
        // vérification, un second client mal construit rendrait le test vert pour
        // la mauvaise raison.
        Assert.Equal(HttpStatusCode.OK, (await telephone.GetAsync("/api/auth/moi")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ordinateur.GetAsync("/api/auth/moi")).StatusCode);

        var fermeture = await ordinateur.PostAsync("/api/auth/deconnexion-totale", null);
        Assert.Equal(HttpStatusCode.NoContent, fermeture.StatusCode);

        // Le jeton d'accès du téléphone n'a pas expiré — il vient d'être émis — et
        // il est pourtant refusé.
        Assert.Equal(
            HttpStatusCode.Unauthorized, (await telephone.GetAsync("/api/auth/moi")).StatusCode);
    }

    /// <summary>
    /// Et elle coupe aussi le rafraîchissement de l'autre appareil.
    ///
    /// Sans cela la fermeture ne durerait que quinze minutes à l'envers : le
    /// téléphone, refusé sur son jeton d'accès, en obtiendrait un neuf avec son
    /// cookie de rafraîchissement intact.
    /// </summary>
    [Fact]
    public async Task La_deconnexion_totale_coupe_aussi_le_rafraichissement()
    {
        await _fixture.ReinitialiserAsync();

        var telephone = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
        var ordinateur = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        await ordinateur.PostAsync("/api/auth/deconnexion-totale", null);

        var reprise = await telephone.PostAsync("/api/auth/rafraichir", null);

        Assert.Equal(HttpStatusCode.Unauthorized, reprise.StatusCode);
    }

    /// <summary>
    /// Témoin indispensable : elle ne touche qu'un seul compte.
    ///
    /// <c>RevoquerToutAsync</c> filtre sur l'identifiant d'utilisateur, et
    /// <c>JetonsRafraichissement</c> n'est <b>pas</b> couverte par les filtres
    /// multi-tenant (CLAUDE.md §7) — c'est exactement la table où un oubli de
    /// clause ferme les sessions de toute l'instance sans qu'aucun test métier ne
    /// s'en aperçoive.
    ///
    /// ⚠️ Le témoin porte sur le <b>rafraîchissement</b> de Beta, pas sur une
    /// lecture authentifiée. Une première version vérifiait son
    /// <c>GET /api/auth/moi</c> et ne prouvait rien : révoquer le jeton de
    /// rafraîchissement de Beta ne périme pas son jeton d'accès, qui est signé et
    /// vaut quinze minutes. Le sabotage — retirer la clause de compte — la laissait
    /// verte. C'est précisément la garantie qui manque au JWT, et elle se retourne
    /// contre le test qui l'ignore.
    /// </summary>
    [Fact]
    public async Task La_deconnexion_totale_ne_touche_pas_les_autres_comptes()
    {
        await _fixture.ReinitialiserAsync();

        var alpha = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
        var beta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);

        await alpha.PostAsync("/api/auth/deconnexion-totale", null);

        var reprise = await beta.PostAsync("/api/auth/rafraichir", null);
        Assert.Equal(HttpStatusCode.OK, reprise.StatusCode);

        // Et son empreinte de sécurité n'a pas bougé non plus.
        Assert.Equal(HttpStatusCode.OK, (await beta.GetAsync("/api/auth/moi")).StatusCode);
    }

    /// <summary>
    /// Elle exige un jeton d'accès valide, contrairement à la déconnexion ordinaire.
    ///
    /// La distinction n'est pas cosmétique. Fermer toutes les sessions d'un compte
    /// est un effet de bord global ; l'autoriser sur le seul cookie de
    /// rafraîchissement, c'est l'autoriser à qui aurait volé précisément ce
    /// cookie-là. La déconnexion ordinaire reste tolérante parce qu'elle n'affecte
    /// que la session qui la demande.
    /// </summary>
    [Fact]
    public async Task La_deconnexion_totale_refuse_une_requete_sans_jeton_d_acces()
    {
        var reponse = await _fixture.ClientAnonymeSansRedirection()
            .PostAsync("/api/auth/deconnexion-totale", null);

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    /// <summary>
    /// Rejoue un jeton de rafraîchissement donné, cookies posés à la main. Le
    /// couple anti-CSRF est inventé : il doit seulement être cohérent avec
    /// lui-même, puisqu'il n'authentifie rien.
    /// </summary>
    private async Task<HttpResponseMessage> RafraichirManuellement(string jeton)
    {
        const string csrf = "jeton-csrf-de-test";

        var requete = new HttpRequestMessage(HttpMethod.Post, "/api/auth/rafraichir");
        requete.Headers.Add("Cookie",
            $"{ServiceSession.CookieRafraichissement}={jeton}; {ServiceSession.CookieCsrf}={csrf}");
        requete.Headers.Add(ServiceSession.EnteteCsrf, csrf);

        return await _fixture.ClientSansCookies().SendAsync(requete);
    }

    private async Task<DateTime> EcheanceUniqueAsync()
    {
        var echeances = await EcheancesActivesAsync();
        return Assert.Single(echeances);
    }

    private async Task<List<DateTime>> EcheancesActivesAsync()
    {
        List<DateTime> echeances = [];

        await _fixture.AvecServicesAsync(async services =>
        {
            var db = services.GetRequiredService<KleanDbContext>();

            echeances = await db.JetonsRafraichissement
                .Where(j => j.DateRevocation == null)
                .Select(j => j.DateExpiration)
                .ToListAsync();
        });

        return echeances;
    }
}
