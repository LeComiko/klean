using System.Net;
using System.Net.Http.Json;
using System.Text;
using Klean.Api.Data;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Klean.Api.Services;
using Klean.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Klean.Api.Tests;

/// <summary>
/// La double authentification (SPEC.md, étape 23).
///
/// Ce que cette série garde : <b>aucune session sans second facteur pour un compte que
/// son rôle oblige</b>, et un second facteur qui ne se contourne ni par le défi, ni par
/// un code rejoué, ni par une copie de la base.
///
/// ⚠️ Elle ne passe jamais par <c>FixtureApi.ConnexionCompleteAsync</c> pour le compte
/// qu'elle éprouve : cette aide oublie le dernier pas accepté, donc elle masquerait la
/// garde du rejeu. Les codes sont produits par <see cref="TotpDeTest"/>, qui ne reprend
/// rien du calcul du serveur.
/// </summary>
[Collection(CollectionApi.Nom)]
public class SecondFacteurTests : IAsyncLifetime
{
    private const string Responsable = "responsable.2fa@test.local";
    private const string Chef = "chef.2fa@test.local";

    /// <summary>Un instant quelconque, au début d'un pas : les décalages se comptent sans reste.</summary>
    private static readonly DateTimeOffset Instant = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    private readonly FixtureApi _fixture;

    public SecondFacteurTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _fixture.Fabrique.Horloge.Fixer(Instant);
    }

    public Task DisposeAsync()
    {
        _fixture.Fabrique.Horloge.Liberer();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------ le calcul

    /// <summary>RFC 6238, annexe B, SHA-1 : les six derniers chiffres des codes à huit.</summary>
    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void Le_calcul_rend_les_vecteurs_de_la_RFC_6238(long secondes, string attendu)
    {
        var cle = Encoding.ASCII.GetBytes("12345678901234567890");
        var instant = DateTimeOffset.FromUnixTimeSeconds(secondes);

        Assert.Equal(attendu, Totp.Code(cle, Totp.Pas(instant)).ToString("D6"));

        // Le témoin des tests est ancré sur les mêmes vecteurs, séparément.
        Assert.Equal(attendu, TotpDeTest.Code(Totp.VersBase32(cle), instant));
    }

    [Fact]
    public void Le_Base32_fait_l_aller_retour()
    {
        var octets = Encoding.ASCII.GetBytes("12345678901234567890");

        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", Totp.VersBase32(octets));
        Assert.Equal(octets, Totp.DepuisBase32("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"));
    }

    // ------------------------------------------------- un compte obligé, pas équipé

    [Fact]
    public async Task Un_compte_oblige_non_equipe_ne_recoit_qu_un_defi()
    {
        var reponse = await Connexion(_fixture.ClientAnonyme(), FixtureApi.AdminAlpha);

        Assert.Equal(HttpStatusCode.Accepted, reponse.StatusCode);
        Assert.Equal(nameof(EtapeDefi.Equipement), (await reponse.Content.ReadFromJsonAsync<EtapeConnexionDto>())!.Etape);

        Assert.Null(FixtureApi.ValeurDuCookie(reponse, ServiceSession.CookieAcces));
        Assert.Null(FixtureApi.ValeurDuCookie(reponse, ServiceSession.CookieRafraichissement));

        var defi = FixtureApi.EnteteDuCookie(reponse, ServiceSecondFacteur.CookieDefi);
        Assert.NotNull(defi);
        Assert.Contains("httponly", defi, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", defi, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", defi, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", defi, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Le_defi_n_ouvre_aucune_route_metier()
    {
        var client = _fixture.ClientAnonyme();
        await Connexion(client, FixtureApi.AdminAlpha);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/moi")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/agents")).StatusCode);
    }

    [Fact]
    public async Task Un_compte_non_oblige_ouvre_sa_session_sur_son_mot_de_passe()
    {
        await CreerCompte(RolesKlean.ChefEquipe, Chef);

        var reponse = await Connexion(_fixture.ClientAnonyme(), Chef);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.NotNull(FixtureApi.ValeurDuCookie(reponse, ServiceSession.CookieAcces));
    }

    [Fact]
    public async Task L_equipement_impose_ouvre_la_session_et_remet_dix_codes()
    {
        var client = _fixture.ClientAnonyme();
        await Connexion(client, FixtureApi.AdminAlpha);

        var preparation = await Preparer(client);
        Assert.Equal(39, preparation.Cle.Length); // 32 caractères en 8 groupes, 7 espaces
        Assert.StartsWith("otpauth://totp/KLEAN:", preparation.Uri);
        Assert.Contains($"secret={preparation.Cle.Replace(" ", "")}", preparation.Uri);
        Assert.True(preparation.Modules.Length >= 21);
        Assert.All(preparation.Modules, ligne => Assert.Equal(preparation.Modules.Length, ligne.Length));

        var activation = await client.PostAsJsonAsync("/api/auth/second-facteur/activer",
            new CodeSecondFacteurDto { Code = TotpDeTest.Code(preparation.Cle, Instant) });

        Assert.Equal(HttpStatusCode.OK, activation.StatusCode);

        var corps = (await activation.Content.ReadFromJsonAsync<CodesRecuperationDto>())!;
        Assert.Equal(10, corps.Codes.Count);
        Assert.Equal(10, corps.Codes.Distinct().Count());
        Assert.All(corps.Codes, code => Assert.Matches("^[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}$", code));
        Assert.NotNull(corps.Session);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/moi")).StatusCode);
        Assert.True((await Compte(FixtureApi.AdminAlpha)).TwoFactorEnabled);
    }

    [Fact]
    public async Task Un_code_faux_a_l_activation_n_active_rien()
    {
        var client = _fixture.ClientAnonyme();
        await Connexion(client, FixtureApi.AdminAlpha);
        var cle = (await Preparer(client)).Cle;

        var activation = await client.PostAsJsonAsync("/api/auth/second-facteur/activer",
            new CodeSecondFacteurDto { Code = TotpDeTest.Code(cle, Instant.AddMinutes(10)) });

        Assert.Equal(HttpStatusCode.BadRequest, activation.StatusCode);
        Assert.False((await Compte(FixtureApi.AdminAlpha)).TwoFactorEnabled);
        Assert.Null(FixtureApi.ValeurDuCookie(activation, ServiceSession.CookieAcces));
    }

    [Fact]
    public async Task Les_essais_de_code_a_l_activation_sont_bornes()
    {
        var client = _fixture.ClientAnonyme();
        await Connexion(client, FixtureApi.AdminAlpha);
        await Preparer(client);

        for (var essai = 1; essai <= PlafondsDebit.CodesParCompte; essai++)
        {
            var reponse = await client.PostAsJsonAsync("/api/auth/second-facteur/activer",
                new CodeSecondFacteurDto { Code = "000000" });

            Assert.NotEqual(HttpStatusCode.TooManyRequests, reponse.StatusCode);
        }

        var deTrop = await client.PostAsJsonAsync("/api/auth/second-facteur/activer",
            new CodeSecondFacteurDto { Code = "000000" });

        Assert.Equal(HttpStatusCode.TooManyRequests, deTrop.StatusCode);
    }

    // ------------------------------------------------------ un compte équipé

    [Fact]
    public async Task Un_compte_equipe_ouvre_sa_session_au_second_temps()
    {
        var cle = await EquiperAdminAsync();
        var client = _fixture.ClientAnonyme();

        var connexion = await Connexion(client, FixtureApi.AdminAlpha);
        Assert.Equal(HttpStatusCode.Accepted, connexion.StatusCode);
        Assert.Equal(nameof(EtapeDefi.SecondFacteur), (await connexion.Content.ReadFromJsonAsync<EtapeConnexionDto>())!.Etape);
        Assert.Null(FixtureApi.ValeurDuCookie(connexion, ServiceSession.CookieAcces));

        // Le pas suivant : celui de l'activation a déjà servi.
        var code = await Code(client, TotpDeTest.Code(cle, Instant.AddSeconds(30)));

        Assert.Equal(HttpStatusCode.OK, code.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/moi")).StatusCode);
    }

    [Fact]
    public async Task Un_code_deja_utilise_ne_sert_pas_deux_fois()
    {
        var cle = await EquiperAdminAsync();
        var suivant = TotpDeTest.Code(cle, Instant.AddSeconds(30));

        var premier = _fixture.ClientAnonyme();
        await Connexion(premier, FixtureApi.AdminAlpha);
        Assert.Equal(HttpStatusCode.OK, (await Code(premier, suivant)).StatusCode);

        // Le même code, toujours dans sa fenêtre, depuis un autre appareil.
        var second = _fixture.ClientAnonyme();
        await Connexion(second, FixtureApi.AdminAlpha);

        Assert.Equal(HttpStatusCode.BadRequest, (await Code(second, suivant)).StatusCode);
    }

    [Fact]
    public async Task La_fenetre_tolere_un_pas_de_decalage_et_pas_deux()
    {
        var cle = await EquiperAdminAsync();

        // Une minute plus tard, le pas de l'activation est dépassé : le pas précédent
        // l'instant est donc encore neuf, et toléré.
        _fixture.Fabrique.Horloge.Fixer(Instant.AddMinutes(1));

        var client = _fixture.ClientAnonyme();
        await Connexion(client, FixtureApi.AdminAlpha);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await Code(client, TotpDeTest.Code(cle, Instant.AddMinutes(2)))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await Code(client, TotpDeTest.Code(cle, Instant.AddSeconds(30)))).StatusCode);
    }

    [Fact]
    public async Task Un_code_faux_rend_400_et_compte_un_echec()
    {
        await EquiperAdminAsync();
        var client = _fixture.ClientAnonyme();
        await Connexion(client, FixtureApi.AdminAlpha);

        var reponse = await Code(client, "000000");

        // 400 et non 401 : la session ne meurt pas d'une faute de frappe (CLAUDE.md §10).
        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(1, (await Compte(FixtureApi.AdminAlpha)).AccessFailedCount);
    }

    [Fact]
    public async Task Les_echecs_du_mot_de_passe_et_du_code_partagent_le_verrou()
    {
        await EquiperAdminAsync();
        var client = _fixture.ClientAnonyme();

        await Connexion(client, FixtureApi.AdminAlpha, "MauvaisMotDePasse1");
        await Connexion(client, FixtureApi.AdminAlpha, "MauvaisMotDePasse1");

        Assert.Equal(HttpStatusCode.Accepted, (await Connexion(client, FixtureApi.AdminAlpha)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await Code(client, "000000")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Code(client, "000000")).StatusCode);

        // Le cinquième échec, le troisième code : verrouillé, et le défi effacé.
        var cinquieme = await Code(client, "000000");
        Assert.Equal(HttpStatusCode.Unauthorized, cinquieme.StatusCode);
        Assert.Contains("verrouillé", await cinquieme.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, (await Connexion(client, FixtureApi.AdminAlpha)).StatusCode);
    }

    [Fact]
    public async Task Le_succes_remet_le_compteur_d_echecs_a_zero()
    {
        var cle = await EquiperAdminAsync();
        var client = _fixture.ClientAnonyme();
        await Connexion(client, FixtureApi.AdminAlpha);

        await Code(client, "000000");
        Assert.Equal(HttpStatusCode.OK, (await Code(client, TotpDeTest.Code(cle, Instant.AddSeconds(30)))).StatusCode);

        Assert.Equal(0, (await Compte(FixtureApi.AdminAlpha)).AccessFailedCount);
    }

    [Fact]
    public async Task Un_defi_obtenu_avant_le_verrouillage_ne_sert_plus_pendant()
    {
        var cle = await EquiperAdminAsync();
        var appareil = _fixture.ClientAnonyme();
        await Connexion(appareil, FixtureApi.AdminAlpha);

        // Un autre appareil épuise les cinq essais pendant ce temps.
        var attaquant = _fixture.ClientAnonyme();
        for (var essai = 1; essai <= 5; essai++)
        {
            await Connexion(attaquant, FixtureApi.AdminAlpha, "MauvaisMotDePasse1");
        }

        var reponse = await Code(appareil, TotpDeTest.Code(cle, Instant.AddSeconds(30)));

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
        Assert.Contains("verrouillé", await reponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Un_compte_desactive_entre_les_deux_temps_n_ouvre_rien()
    {
        var cle = await EquiperAdminAsync();
        var client = _fixture.ClientAnonyme();
        await Connexion(client, FixtureApi.AdminAlpha);

        await _fixture.DesactiverCompteAsync(FixtureApi.AdminAlpha);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Code(client, TotpDeTest.Code(cle, Instant.AddSeconds(30)))).StatusCode);
    }

    [Fact]
    public async Task Le_defi_expire_au_bout_de_cinq_minutes()
    {
        var cle = await EquiperAdminAsync();
        var client = _fixture.ClientAnonyme();
        await Connexion(client, FixtureApi.AdminAlpha);

        var echeance = Instant.Add(ServiceSecondFacteur.DureeDefi);
        _fixture.Fabrique.Horloge.Fixer(echeance);

        var reponse = await Code(client, TotpDeTest.Code(cle, echeance));

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task Le_defi_ne_survit_pas_a_un_changement_d_empreinte()
    {
        var cle = await EquiperAdminAsync();
        var client = _fixture.ClientAnonyme();
        await Connexion(client, FixtureApi.AdminAlpha);

        // Ce que fait un changement de mot de passe entre les deux temps.
        await AvecComptesAsync(async utilisateurs =>
            await utilisateurs.UpdateSecurityStampAsync((await utilisateurs.FindByEmailAsync(FixtureApi.AdminAlpha))!));

        var reponse = await Code(client, TotpDeTest.Code(cle, Instant.AddSeconds(30)));

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task Le_defi_du_second_temps_ne_permet_pas_de_changer_la_cle()
    {
        // Sans cette garde, le mot de passe seul suffirait à remplacer le téléphone d'un
        // compte équipé par celui de l'attaquant.
        var cle = await EquiperAdminAsync();
        var client = _fixture.ClientAnonyme();
        await Connexion(client, FixtureApi.AdminAlpha);

        var preparation = await client.PostAsync("/api/auth/second-facteur/preparer", null);

        Assert.Equal(HttpStatusCode.Unauthorized, preparation.StatusCode);

        // Témoin : la clé d'origine sert toujours.
        Assert.Equal(HttpStatusCode.OK,
            (await Code(client, TotpDeTest.Code(cle, Instant.AddSeconds(30)))).StatusCode);
    }

    [Fact]
    public async Task Une_session_ne_prepare_pas_une_seconde_cle()
    {
        var admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        var preparation = await admin.PostAsync("/api/auth/second-facteur/preparer", null);

        Assert.Equal(HttpStatusCode.Conflict, preparation.StatusCode);
    }

    // ------------------------------------------------------ codes de récupération

    [Fact]
    public async Task Un_code_de_recuperation_ouvre_la_session_une_seule_fois()
    {
        await EquiperAdminAsync();
        var code = _fixture.CodesDe(FixtureApi.AdminAlpha)[0];

        var premier = _fixture.ClientAnonyme();
        await Connexion(premier, FixtureApi.AdminAlpha);
        Assert.Equal(HttpStatusCode.OK, (await Recuperation(premier, code)).StatusCode);

        var second = _fixture.ClientAnonyme();
        await Connexion(second, FixtureApi.AdminAlpha);
        Assert.Equal(HttpStatusCode.BadRequest, (await Recuperation(second, code)).StatusCode);
    }

    [Fact]
    public async Task Un_code_de_recuperation_se_saisit_sans_tirets_ni_majuscules()
    {
        await EquiperAdminAsync();
        var code = _fixture.CodesDe(FixtureApi.AdminAlpha)[1].Replace("-", " ").ToLowerInvariant();

        var client = _fixture.ClientAnonyme();
        await Connexion(client, FixtureApi.AdminAlpha);

        Assert.Equal(HttpStatusCode.OK, (await Recuperation(client, code)).StatusCode);
    }

    [Fact]
    public async Task Ni_la_cle_ni_les_codes_ne_sont_lisibles_en_base()
    {
        await EquiperAdminAsync();
        var cle = _fixture.CleDe(FixtureApi.AdminAlpha).Replace(" ", "");
        var codes = _fixture.CodesDe(FixtureApi.AdminAlpha).Select(c => c.Replace("-", "")).ToList();
        var id = (await Compte(FixtureApi.AdminAlpha)).Id;

        var rangees = string.Join("\n", (await JetonsAsync(id)).Select(j => j.Value));

        Assert.NotEmpty(rangees);
        Assert.DoesNotContain(cle, rangees);
        Assert.All(codes, code => Assert.DoesNotContain(code, rangees));
    }

    [Fact]
    public async Task Regenerer_les_codes_annule_les_anciens()
    {
        var admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
        var anciens = _fixture.CodesDe(FixtureApi.AdminAlpha);

        var sansCode = await admin.PostAsJsonAsync("/api/auth/second-facteur/codes", new CodeSecondFacteurDto());
        Assert.Equal(HttpStatusCode.BadRequest, sansCode.StatusCode);

        var reponse = await admin.PostAsJsonAsync("/api/auth/second-facteur/codes",
            new CodeSecondFacteurDto { Code = TotpDeTest.Code(_fixture.CleDe(FixtureApi.AdminAlpha), Instant.AddSeconds(30)) });
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);

        var nouveaux = (await reponse.Content.ReadFromJsonAsync<CodesRecuperationDto>())!.Codes;
        Assert.Equal(10, nouveaux.Count);
        Assert.Empty(nouveaux.Intersect(anciens));

        var client = _fixture.ClientAnonyme();
        await Connexion(client, FixtureApi.AdminAlpha);
        Assert.Equal(HttpStatusCode.BadRequest, (await Recuperation(client, anciens[0])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Recuperation(client, nouveaux[0])).StatusCode);
    }

    [Fact]
    public async Task L_etat_dit_actif_obligatoire_et_codes_restants()
    {
        await EquiperAdminAsync();

        var client = _fixture.ClientAnonyme();
        await Connexion(client, FixtureApi.AdminAlpha);
        Assert.Equal(HttpStatusCode.OK,
            (await Recuperation(client, _fixture.CodesDe(FixtureApi.AdminAlpha)[0])).StatusCode);

        var etat = (await client.GetFromJsonAsync<EtatSecondFacteurDto>("/api/auth/second-facteur"))!;

        Assert.True(etat.Actif);
        Assert.True(etat.Obligatoire);
        Assert.Equal(9, etat.CodesRestants);
    }

    // ------------------------------------------- aucune session sans second facteur

    [Fact]
    public async Task Un_jeton_d_acces_d_un_compte_oblige_sans_second_facteur_est_refuse()
    {
        // Une session d'avant l'obligation : simulée en retirant l'indicateur en base,
        // sous Identity, pour ne pas toucher à l'empreinte de sécurité.
        var admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
        await RetirerIndicateurEnBaseAsync(FixtureApi.AdminAlpha);

        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/auth/moi")).StatusCode);
    }

    [Fact]
    public async Task Le_rafraichissement_d_un_compte_oblige_sans_second_facteur_est_refuse()
    {
        var admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
        await RetirerIndicateurEnBaseAsync(FixtureApi.AdminAlpha);

        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.PostAsync("/api/auth/rafraichir", null)).StatusCode);
    }

    // ------------------------------------------------- activation et désactivation

    [Fact]
    public async Task L_activation_volontaire_coupe_les_autres_sessions_et_garde_celle_ci()
    {
        await CreerCompte(RolesKlean.ChefEquipe, Chef);
        var autre = await _fixture.ClientConnecteAsync(Chef);
        var chef = await _fixture.ClientConnecteAsync(Chef);

        var cle = (await Preparer(chef)).Cle;
        var activation = await chef.PostAsJsonAsync("/api/auth/second-facteur/activer",
            new CodeSecondFacteurDto { Code = TotpDeTest.Code(cle, Instant) });
        Assert.Equal(HttpStatusCode.OK, activation.StatusCode);
        FixtureApi.ArmerCsrf(chef, activation);

        Assert.Equal(HttpStatusCode.OK, (await chef.GetAsync("/api/auth/moi")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await autre.GetAsync("/api/auth/moi")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await autre.PostAsync("/api/auth/rafraichir", null)).StatusCode);
    }

    [Fact]
    public async Task Un_compte_non_oblige_desactive_et_garde_sa_session()
    {
        await CreerCompte(RolesKlean.ChefEquipe, Chef);
        var chef = await _fixture.ClientConnecteAsync(Chef);
        var cle = (await Preparer(chef)).Cle;
        FixtureApi.ArmerCsrf(chef, await chef.PostAsJsonAsync("/api/auth/second-facteur/activer",
            new CodeSecondFacteurDto { Code = TotpDeTest.Code(cle, Instant) }));

        var faux = await chef.PostAsJsonAsync("/api/auth/second-facteur/desactiver",
            new CodeSecondFacteurDto { Code = "000000" });
        Assert.Equal(HttpStatusCode.BadRequest, faux.StatusCode);

        var reponse = await chef.PostAsJsonAsync("/api/auth/second-facteur/desactiver",
            new CodeSecondFacteurDto { Code = TotpDeTest.Code(cle, Instant.AddSeconds(30)) });
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        FixtureApi.ArmerCsrf(chef, reponse);

        Assert.Equal(HttpStatusCode.OK, (await chef.GetAsync("/api/auth/moi")).StatusCode);

        var compte = await Compte(Chef);
        Assert.False(compte.TwoFactorEnabled);
        Assert.Empty(await JetonsAsync(compte.Id));
        Assert.Equal(HttpStatusCode.OK, (await Connexion(_fixture.ClientAnonyme(), Chef)).StatusCode);
    }

    [Fact]
    public async Task Un_compte_oblige_qui_desactive_est_deconnecte_et_doit_se_reequiper()
    {
        var admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        var reponse = await admin.PostAsJsonAsync("/api/auth/second-facteur/desactiver",
            new CodeSecondFacteurDto { Code = TotpDeTest.Code(_fixture.CleDe(FixtureApi.AdminAlpha), Instant.AddSeconds(30)) });

        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/auth/moi")).StatusCode);

        var connexion = await Connexion(_fixture.ClientAnonyme(), FixtureApi.AdminAlpha);
        Assert.Equal(nameof(EtapeDefi.Equipement), (await connexion.Content.ReadFromJsonAsync<EtapeConnexionDto>())!.Etape);
    }

    /// <summary>
    /// Le téléphone perdu, de bout en bout : connecté par un code de récupération, le
    /// compte change de téléphone avec un autre. Sur le code de l'application seul, il
    /// n'avait aucune sortie que l'administrateur, ou le script pour le dernier d'entre eux.
    /// </summary>
    [Fact]
    public async Task Un_telephone_perdu_se_remplace_avec_les_codes_de_recuperation()
    {
        await EquiperAdminAsync();
        var codes = _fixture.CodesDe(FixtureApi.AdminAlpha);

        var admin = _fixture.ClientAnonyme();
        await Connexion(admin, FixtureApi.AdminAlpha);
        var session = await Recuperation(admin, codes[0]);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        FixtureApi.ArmerCsrf(admin, session);

        // Le code qui a servi à la connexion ne sert pas deux fois.
        var deja = await admin.PostAsJsonAsync("/api/auth/second-facteur/desactiver",
            new CodeSecondFacteurDto { CodeRecuperation = codes[0] });
        Assert.Equal(HttpStatusCode.BadRequest, deja.StatusCode);
        Assert.True((await Compte(FixtureApi.AdminAlpha)).TwoFactorEnabled);

        var reponse = await admin.PostAsJsonAsync("/api/auth/second-facteur/desactiver",
            new CodeSecondFacteurDto { CodeRecuperation = codes[1] });
        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);

        var compte = await Compte(FixtureApi.AdminAlpha);
        Assert.False(compte.TwoFactorEnabled);
        Assert.Empty(await JetonsAsync(compte.Id));

        var connexion = await Connexion(_fixture.ClientAnonyme(), FixtureApi.AdminAlpha);
        Assert.Equal(nameof(EtapeDefi.Equipement), (await connexion.Content.ReadFromJsonAsync<EtapeConnexionDto>())!.Etape);
    }

    /// <summary>
    /// Le trousseau de la Data Protection perdu (DEPLOIEMENT.md §10) : la clé est
    /// remplacée par la même, chiffrée par un autre trousseau — exactement ce que voit
    /// l'API après la perte du volume <c>cles-api</c>.
    ///
    /// Deux promesses du document, éprouvées ensemble : le code de l'application rend une
    /// erreur serveur, sans compter d'échec — « code faux » enverrait réessayer un code
    /// juste jusqu'au verrouillage ; et les codes de récupération, gardés par empreinte,
    /// suffisent à changer de téléphone.
    /// </summary>
    [Fact]
    public async Task Un_trousseau_perdu_se_rattrape_par_les_codes_de_recuperation()
    {
        var cle = await EquiperAdminAsync();
        var codes = _fixture.CodesDe(FixtureApi.AdminAlpha);

        await _fixture.AvecServicesAsync(async services =>
        {
            var utilisateurs = services.GetRequiredService<UserManager<Utilisateur>>();
            var compte = (await utilisateurs.FindByEmailAsync(FixtureApi.AdminAlpha))!;
            var autreTrousseau = new EphemeralDataProtectionProvider().CreateProtector("Klean.SecondFacteur.Cle");

            await utilisateurs.SetAuthenticationTokenAsync(compte, ServiceSecondFacteur.Fournisseur,
                ServiceSecondFacteur.JetonCle, autreTrousseau.Protect(cle));
        });

        var parCode = _fixture.ClientAnonyme();
        await Connexion(parCode, FixtureApi.AdminAlpha);
        var refus = await Code(parCode, TotpDeTest.Code(cle, Instant.AddSeconds(30)));

        Assert.Equal(HttpStatusCode.InternalServerError, refus.StatusCode);
        Assert.Equal(0, (await Compte(FixtureApi.AdminAlpha)).AccessFailedCount);

        var admin = _fixture.ClientAnonyme();
        await Connexion(admin, FixtureApi.AdminAlpha);
        var session = await Recuperation(admin, codes[0]);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        FixtureApi.ArmerCsrf(admin, session);

        var reponse = await admin.PostAsJsonAsync("/api/auth/second-facteur/desactiver",
            new CodeSecondFacteurDto { CodeRecuperation = codes[1] });

        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);
        Assert.False((await Compte(FixtureApi.AdminAlpha)).TwoFactorEnabled);
    }

    [Fact]
    public async Task Changer_de_telephone_refuse_les_deux_saisies_ensemble_ou_aucune()
    {
        var admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
        var code = TotpDeTest.Code(_fixture.CleDe(FixtureApi.AdminAlpha), Instant.AddSeconds(30));

        var deux = await admin.PostAsJsonAsync("/api/auth/second-facteur/desactiver",
            new CodeSecondFacteurDto { Code = code, CodeRecuperation = _fixture.CodesDe(FixtureApi.AdminAlpha)[0] });
        var aucune = await admin.PostAsJsonAsync("/api/auth/second-facteur/desactiver", new CodeSecondFacteurDto());

        Assert.Equal(HttpStatusCode.BadRequest, deux.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, aucune.StatusCode);
        Assert.True((await Compte(FixtureApi.AdminAlpha)).TwoFactorEnabled);
    }

    // ------------------------------------------------ réinitialisation par l'admin

    [Fact]
    public async Task Un_administrateur_reinitialise_le_second_facteur_d_un_collegue()
    {
        var admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
        var id = await CreerCompte(RolesKlean.Responsable, Responsable, admin);
        var responsable = await _fixture.ClientConnecteAsync(Responsable);
        var empreinte = (await Compte(Responsable)).SecurityStamp;

        var reponse = await admin.PostAsync($"/api/auth/comptes/{id}/reinitialiser-second-facteur", null);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.True((await reponse.Content.ReadFromJsonAsync<ReinitialisationSecondFacteurDto>())!.SessionsRevoquees >= 1);

        // Relu en base, champ par champ : un test par l'API ne distingue pas les gardes
        // qui se couvrent l'une l'autre (CLAUDE.md §7 bis).
        var compte = await Compte(Responsable);
        Assert.False(compte.TwoFactorEnabled);
        Assert.NotEqual(empreinte, compte.SecurityStamp);
        Assert.Empty(await JetonsAsync(id));

        Assert.Equal(HttpStatusCode.Unauthorized, (await responsable.GetAsync("/api/auth/moi")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await responsable.PostAsync("/api/auth/rafraichir", null)).StatusCode);

        var connexion = await Connexion(_fixture.ClientAnonyme(), Responsable);
        Assert.Equal(nameof(EtapeDefi.Equipement), (await connexion.Content.ReadFromJsonAsync<EtapeConnexionDto>())!.Etape);
    }

    [Fact]
    public async Task La_reinitialisation_coupe_les_sessions_d_un_compte_non_oblige()
    {
        // Un chef équipé de son plein gré : ici, rien d'autre que la révocation ne coupe
        // son rafraîchissement — le refus réservé aux comptes obligés ne s'applique pas.
        var admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
        var id = await CreerCompte(RolesKlean.ChefEquipe, Chef, admin);
        var chef = await _fixture.ClientConnecteAsync(Chef);
        var cle = (await Preparer(chef)).Cle;
        FixtureApi.ArmerCsrf(chef, await chef.PostAsJsonAsync("/api/auth/second-facteur/activer",
            new CodeSecondFacteurDto { Code = TotpDeTest.Code(cle, Instant) }));

        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostAsync($"/api/auth/comptes/{id}/reinitialiser-second-facteur", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await chef.PostAsync("/api/auth/rafraichir", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Connexion(_fixture.ClientAnonyme(), Chef)).StatusCode);
    }

    [Fact]
    public async Task La_reinitialisation_est_journalisee()
    {
        var admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
        var id = await CreerCompte(RolesKlean.Responsable, Responsable, admin);
        await _fixture.ClientConnecteAsync(Responsable);

        await admin.PostAsync($"/api/auth/comptes/{id}/reinitialiser-second-facteur", null);

        var journal = await admin.PageAsync<JournalAccesDto>("/api/journal-acces");
        var ligne = Assert.Single(journal.Elements, l => l.Nature == nameof(NatureAcces.ReinitialisationSecondFacteur));

        Assert.Equal(nameof(RessourceAccedee.Compte), ligne.Ressource);
        Assert.Equal(id, ligne.CibleId);
    }

    [Fact]
    public async Task La_reinitialisation_refuse_et_nomme_ses_sorties()
    {
        var admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
        var moi = (await Compte(FixtureApi.AdminAlpha)).Id;
        var nonEquipe = await CreerCompte(RolesKlean.ChefEquipe, Chef, admin);

        var surSoi = await admin.PostAsync($"/api/auth/comptes/{moi}/reinitialiser-second-facteur", null);
        Assert.Equal(HttpStatusCode.Conflict, surSoi.StatusCode);
        Assert.Contains("Changer de téléphone", await surSoi.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsync($"/api/auth/comptes/{nonEquipe}/reinitialiser-second-facteur", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PostAsync($"/api/auth/comptes/{Guid.NewGuid()}/reinitialiser-second-facteur", null)).StatusCode);
    }

    [Fact]
    public async Task La_reinitialisation_ne_traverse_pas_les_entreprises()
    {
        var adminBeta = await _fixture.ClientConnecteAsync(FixtureApi.AdminBeta);
        var idBeta = (await Compte(FixtureApi.AdminBeta)).Id;
        var admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        var reponse = await admin.PostAsync($"/api/auth/comptes/{idBeta}/reinitialiser-second-facteur", null);

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
        Assert.True((await Compte(FixtureApi.AdminBeta)).TwoFactorEnabled);
        Assert.Equal(HttpStatusCode.OK, (await adminBeta.GetAsync("/api/auth/moi")).StatusCode);
    }

    [Fact]
    public async Task Seul_l_administrateur_reinitialise()
    {
        var admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
        var moi = (await Compte(FixtureApi.AdminAlpha)).Id;
        await CreerCompte(RolesKlean.Responsable, Responsable, admin);
        var responsable = await _fixture.ClientConnecteAsync(Responsable);

        var reponse = await responsable.PostAsync($"/api/auth/comptes/{moi}/reinitialiser-second-facteur", null);

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    // ------------------------------------------------------------ pseudonymisation

    [Fact]
    public async Task La_pseudonymisation_d_un_compte_efface_son_second_facteur()
    {
        var admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
        var id = await CreerCompte(RolesKlean.Responsable, Responsable, admin);
        await _fixture.ClientConnecteAsync(Responsable);
        Assert.NotEmpty(await JetonsAsync(id));

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/auth/comptes/{id}/anonymiser", null)).StatusCode);

        Assert.Empty(await JetonsAsync(id));
        Assert.False((await CompteParId(id)).TwoFactorEnabled);
    }

    [Fact]
    public async Task La_pseudonymisation_d_un_agent_efface_le_second_facteur_de_son_compte()
    {
        var admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        var agent = await (await admin.PostAsJsonAsync("/api/agents", new AgentCreateDto
        {
            Nom = "Diallo",
            Prenom = "Aminata",
            DateEmbauche = new DateTime(2024, 3, 15)
        })).Content.ReadFromJsonAsync<AgentDto>();

        // Un agent n'y est pas tenu, mais peut l'activer.
        var id = await CreerCompte(RolesKlean.Agent, "agent.2fa@test.local", admin, agent!.Id);
        var compte = await _fixture.ClientConnecteAsync("agent.2fa@test.local");
        var cle = (await Preparer(compte)).Cle;
        Assert.Equal(HttpStatusCode.OK, (await compte.PostAsJsonAsync("/api/auth/second-facteur/activer",
            new CodeSecondFacteurDto { Code = TotpDeTest.Code(cle, Instant) })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/agents/{agent.Id}/anonymiser", null)).StatusCode);

        Assert.Empty(await JetonsAsync(id));
        Assert.False((await CompteParId(id)).TwoFactorEnabled);
    }

    // ------------------------------------------------------------------ outillage

    private static Task<HttpResponseMessage> Connexion(
        HttpClient client, string email, string motDePasse = FixtureApi.MotDePasse) =>
        client.PostAsJsonAsync("/api/auth/connexion", new ConnexionDto { Email = email, MotDePasse = motDePasse });

    private static Task<HttpResponseMessage> Code(HttpClient client, string code) =>
        client.PostAsJsonAsync("/api/auth/connexion/code", new CodeSecondFacteurDto { Code = code });

    private static Task<HttpResponseMessage> Recuperation(HttpClient client, string code) =>
        client.PostAsJsonAsync("/api/auth/connexion/code", new CodeSecondFacteurDto { CodeRecuperation = code });

    private static async Task<PreparationSecondFacteurDto> Preparer(HttpClient client)
    {
        var reponse = await client.PostAsync("/api/auth/second-facteur/preparer", null);
        reponse.EnsureSuccessStatusCode();

        return (await reponse.Content.ReadFromJsonAsync<PreparationSecondFacteurDto>())!;
    }

    /// <summary>
    /// Équipe l'administrateur d'Alpha par le chemin de l'écran, à <see cref="Instant"/> :
    /// le pas de l'instant est donc consommé.
    /// </summary>
    private async Task<string> EquiperAdminAsync()
    {
        var client = _fixture.ClientAnonyme();
        await Connexion(client, FixtureApi.AdminAlpha);
        await _fixture.EquiperAsync(client, FixtureApi.AdminAlpha);

        return _fixture.CleDe(FixtureApi.AdminAlpha);
    }

    private async Task<Guid> CreerCompte(
        string role, string email, HttpClient? admin = null, Guid? agentId = null)
    {
        admin ??= await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        var reponse = await admin.PostAsJsonAsync("/api/auth/comptes", new CompteCreateDto
        {
            Email = email,
            MotDePasse = FixtureApi.MotDePasse,
            Nom = role,
            Role = role,
            AgentId = agentId
        });

        reponse.EnsureSuccessStatusCode();

        return (await reponse.Content.ReadFromJsonAsync<ProfilDto>())!.Id;
    }

    private async Task AvecComptesAsync(Func<UserManager<Utilisateur>, Task> action) =>
        await _fixture.AvecServicesAsync(services =>
            action(services.GetRequiredService<UserManager<Utilisateur>>()));

    /// <summary>Le compte relu dans une portée neuve : ce qui est en base, pas en mémoire.</summary>
    private async Task<Utilisateur> Compte(string email)
    {
        Utilisateur? compte = null;
        await _fixture.AvecServicesAsync(async services =>
            compte = await services.GetRequiredService<KleanDbContext>().Users.AsNoTracking()
                .SingleAsync(u => u.NormalizedEmail == email.ToUpperInvariant()));

        return compte!;
    }

    private async Task<Utilisateur> CompteParId(Guid id)
    {
        Utilisateur? compte = null;
        await _fixture.AvecServicesAsync(async services =>
            compte = await services.GetRequiredService<KleanDbContext>().Users.AsNoTracking()
                .SingleAsync(u => u.Id == id));

        return compte!;
    }

    private async Task<List<IdentityUserToken<Guid>>> JetonsAsync(Guid id)
    {
        List<IdentityUserToken<Guid>> jetons = [];
        await _fixture.AvecServicesAsync(async services =>
            jetons = await services.GetRequiredService<KleanDbContext>().UserTokens.AsNoTracking()
                .Where(t => t.UserId == id).ToListAsync());

        return jetons;
    }

    private Task RetirerIndicateurEnBaseAsync(string email) =>
        _fixture.AvecServicesAsync(services =>
            services.GetRequiredService<KleanDbContext>().Database.ExecuteSqlInterpolatedAsync(
                $"""UPDATE "AspNetUsers" SET "TwoFactorEnabled" = false WHERE "NormalizedEmail" = {email.ToUpperInvariant()}"""));
}
