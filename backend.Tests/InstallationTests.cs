using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Klean.Api.Controllers;
using Klean.Api.Data;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Klean.Api.Services;
using Klean.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Klean.Api.Tests;

/// <summary>
/// L'assistant d'installation, côté API (SPEC.md, étape 34, lot A2) : <c>GET</c> et
/// <c>POST /api/installation</c>, anonymes, gardés par le code d'installation, et sous la RLS.
///
/// ⚠️ <b>Les tests d'une instance vierge commencent par <see cref="FixtureApi.ViderAsync"/></b> :
/// la base de la fixture a des comptes, et un POST y rend 409. Un 409 inattendu ne se
/// « corrige » jamais en déplaçant le contrôle du code. La classe remet la base en forme en
/// sortant, sinon la suivante trouverait une base vide.
///
/// Le code ne passe jamais dans une URL : il voyage dans le corps, et sa valeur est tirée à
/// chaque exécution (<see cref="FabriqueApi.CodeDInstallation"/>).
/// </summary>
[Collection(CollectionApi.Nom)]
public class InstallationTests : IAsyncLifetime
{
    private const string Route = "/api/installation";

    /// <summary>Dans <see cref="FabriqueApi.ReseauProxy"/> : ses X-Forwarded-For sont crus.</summary>
    private static readonly IPAddress Proxy = IPAddress.Parse("10.0.0.2");

    private readonly FixtureApi _fixture;

    public InstallationTests(FixtureApi fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ReinitialiserAsync();

    public Task DisposeAsync() => _fixture.ReinitialiserAsync();

    // ------------------------------------------------------------------ outils

    /// <summary>En groupes de cinq et en minuscules, comme un patron le recopierait : la normalisation est exercée.</summary>
    private static string Saisi(string code) =>
        string.Join("-", code.Chunk(5).Select(g => new string(g))).ToLowerInvariant();

    private static string CodeFaux()
    {
        string code;
        do code = CodeInstallation.Tirer(); while (code == FabriqueApi.CodeDInstallation);
        return code;
    }

    private static Dictionary<string, object?> Corps(
        string siret = "40000000000004", string email = "patron@delta.test", string? code = null,
        string territoire = "Metropole", string motDePasse = "PatronDelta2026") => new()
    {
        ["code"] = Saisi(code ?? FabriqueApi.CodeDInstallation),
        ["raisonSociale"] = "Delta Propreté",
        ["siret"] = siret,
        ["territoire"] = territoire,
        ["email"] = email,
        ["nom"] = "Durand",
        ["prenom"] = "Claire",
        ["motDePasse"] = motDePasse
    };

    private Task<HttpResponseMessage> Poster(object corps, HttpClient? client = null) =>
        (client ?? _fixture.ClientAnonyme()).PostAsJsonAsync(Route, corps);

    private Task<HttpResponseMessage> PosterBrut(string json) =>
        _fixture.ClientAnonyme().PostAsync(Route, new StringContent(json, Encoding.UTF8, "application/json"));

    /// <summary>
    /// Une requête venue du proxy de confiance, pour le compte de <paramref name="transmise"/>.
    /// Un client de la fabrique n'a pas d'adresse distante : X-Forwarded-For y serait ignoré.
    /// Recopié de <c>ExploitationTests.Envoyer</c>.
    /// </summary>
    private async Task<(int Statut, string Corps)> EnvoyerDepuis(string transmise, object corps)
    {
        var octets = JsonSerializer.SerializeToUtf8Bytes(corps);

        var contexte = await _fixture.Fabrique.Server.SendAsync(requete =>
        {
            requete.Connection.RemoteIpAddress = Proxy;
            requete.Request.Method = "POST";
            requete.Request.Scheme = "http";
            requete.Request.Host = new HostString("klean-production.test");
            requete.Request.Path = Route;
            requete.Request.ContentType = "application/json";
            requete.Request.ContentLength = octets.Length;
            requete.Request.Body = new MemoryStream(octets);
            requete.Request.Headers["X-Forwarded-For"] = transmise;
        });

        return (contexte.Response.StatusCode, await new StreamReader(contexte.Response.Body).ReadToEndAsync());
    }

    private async Task<List<Entreprise>> EntreprisesAsync()
    {
        using var portee = _fixture.PorteeAuProprietaire();
        return await portee.ServiceProvider.GetRequiredService<KleanDbContext>()
            .Entreprises.AsNoTracking().ToListAsync();
    }

    private async Task<int> ComptesAsync()
    {
        using var portee = _fixture.PorteeAuProprietaire();
        return await portee.ServiceProvider.GetRequiredService<KleanDbContext>().Users.CountAsync();
    }

    private EtatInstallation Etat => _fixture.Fabrique.Services.GetRequiredService<EtatInstallation>();

    /// <summary>Une entreprise posée au propriétaire, sans compte : le reste d'un amorçage d'avant le lot A2.</summary>
    private Task PoserOrphelineAsync(string siret) => _fixture.AvecServicesAsync(async services =>
    {
        var db = services.GetRequiredService<KleanDbContext>();
        db.Entreprises.Add(new Entreprise { RaisonSociale = "Orpheline", Siret = siret });
        await db.SaveChangesAsync();
    });

    // --------------------------------------------------------------------- GET

    [Fact]
    public async Task Get_rend_installee_quand_un_compte_existe()
    {
        var reponse = await _fixture.ClientAnonyme().GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Equal("""{"installee":true}""", await reponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Get_rend_non_installee_sans_compte()
    {
        await _fixture.ViderAsync();

        var reponse = await _fixture.ClientAnonyme().GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Equal("""{"installee":false}""", await reponse.Content.ReadAsStringAsync());
    }

    // ----------------------------------------------------------- refus d'abord

    /// <summary>
    /// Le second cas départage le contrôleur de la relecture de <c>ServiceInstallation</c>, qui
    /// rendrait aussi 409 sur un corps valide : le contrôleur répond avant les champs.
    /// </summary>
    [Theory]
    [InlineData("40000000000004")]
    [InlineData("123")]
    public async Task Post_avec_des_comptes_rend_409(string siret)
    {
        var reponse = await Poster(Corps(siret: siret));

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        Assert.DoesNotContain(await EntreprisesAsync(), e => e.RaisonSociale == "Delta Propreté");
        Assert.Equal(2, await ComptesAsync());
    }

    /// <summary>
    /// Une requête authentifiée ne pose jamais d'entreprise d'installation : refusée en 409 par
    /// le contrôleur, et non en 500 par la garde de <c>PoserEntrepriseInstallation</c>.
    /// </summary>
    [Fact]
    public async Task Post_depuis_une_session_rend_409()
    {
        var administrateur = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        var reponse = await Poster(Corps(), administrateur);

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        Assert.Equal(2, (await EntreprisesAsync()).Count);
    }

    [Fact]
    public async Task Post_avec_un_code_faux_rend_400_sans_rien_ecrire()
    {
        await _fixture.ViderAsync();

        var reponse = await Poster(Corps(code: CodeFaux()));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.StartsWith(InstallationController.DebutRefusCode, await reponse.Content.ReadAsStringAsync());
        Assert.Empty(await EntreprisesAsync());
        Assert.Equal(0, await ComptesAsync());
    }

    // ---------------------------------------------------------------- création

    [Fact]
    public async Task Post_cree_l_entreprise_et_l_administrateur()
    {
        await _fixture.ViderAsync();

        // Un territoire autre que celui par défaut : sinon l'assertion passerait sans qu'il soit écrit.
        var reponse = await Poster(Corps(territoire: "Reunion"));

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);

        var entreprise = Assert.Single(await EntreprisesAsync());
        Assert.Equal("Delta Propreté", entreprise.RaisonSociale);
        Assert.Equal("40000000000004", entreprise.Siret);
        Assert.Equal(Territoire.Reunion, entreprise.Territoire);

        await _fixture.AvecServicesAsync(async services =>
        {
            var utilisateurs = services.GetRequiredService<UserManager<Utilisateur>>();
            var compte = Assert.Single(await utilisateurs.Users.AsNoTracking().ToListAsync());

            Assert.Equal("patron@delta.test", compte.Email);
            Assert.Equal(compte.Email, compte.UserName);
            Assert.Equal(entreprise.Id, compte.EntrepriseId);
            Assert.Equal("Durand", compte.Nom);
            Assert.Equal("Claire", compte.Prenom);
            Assert.True(compte.Actif);
            // Choisi par le patron lui-même : rien à remplacer à la première connexion.
            Assert.False(compte.MotDePasseProvisoire);
            Assert.Equal(RolesKlean.Administrateur, Assert.Single(await utilisateurs.GetRolesAsync(compte)));
            Assert.True(await utilisateurs.CheckPasswordAsync(compte, "PatronDelta2026"));
        });
    }

    [Fact]
    public async Task Post_n_ouvre_aucune_session()
    {
        await _fixture.ViderAsync();

        var reponse = await Poster(Corps());

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        Assert.False(reponse.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Apres_l_installation_la_connexion_fait_equiper_le_second_facteur()
    {
        await _fixture.ViderAsync();
        Assert.Equal(HttpStatusCode.Created, (await Poster(Corps())).StatusCode);

        var client = _fixture.ClientAnonyme();
        var connexion = await client.PostAsJsonAsync("/api/auth/connexion",
            new ConnexionDto { Email = "patron@delta.test", MotDePasse = "PatronDelta2026" });

        Assert.Equal(HttpStatusCode.Accepted, connexion.StatusCode);
        Assert.Equal(nameof(EtapeDefi.Equipement), (await connexion.Content.ReadFromJsonAsync<EtapeConnexionDto>())!.Etape);

        Assert.Equal("""{"installee":true}""", await (await client.GetAsync(Route)).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Une_seconde_installation_rend_409()
    {
        await _fixture.ViderAsync();

        var premiere = await Poster(Corps());
        var seconde = await Poster(Corps(siret: "50000000000005", email: "autre@delta.test"));

        Assert.Equal(HttpStatusCode.Created, premiere.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, seconde.StatusCode);
        Assert.Single(await EntreprisesAsync());
        Assert.Equal(1, await ComptesAsync());
    }

    [Fact]
    public async Task Le_mot_de_passe_refuse_par_Identity_ne_laisse_rien()
    {
        await _fixture.ViderAsync();

        var reponse = await Poster(Corps(motDePasse: "court"));
        var message = await reponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Contains("Le mot de passe doit faire au moins", message);
        Assert.DoesNotContain("The ", message);
        Assert.Empty(await EntreprisesAsync());
        Assert.Equal(0, await ComptesAsync());
        Assert.False(Etat.Installee);
    }

    /// <summary>
    /// Des corps écrits en texte brut, pour que les <c>null</c> soient de vrais <c>null</c> JSON :
    /// avec Nullable activé, un <c>string</c> non nullable dans le DTO les ferait refuser en
    /// anglais par la validation de modèle, avant le contrôleur (CLAUDE.md §10).
    /// <c>§LONG§</c> vaut un texte de 101 caractères, <c>§ADRESSE257§</c> une adresse de 257.
    /// Celle-ci départage le contrôleur d'Identity, qui refuse déjà les autres adresses fausses —
    /// en français — mais l'accepterait : elle dépasse la colonne, et finirait en 500 (sabotage).
    /// </summary>
    [Theory]
    [InlineData("raisonSociale", "\"\"")]
    [InlineData("raisonSociale", "\"   \"")]
    [InlineData("siret", "\"123\"")]
    [InlineData("siret", "\"123.456.789.00012\"")]
    [InlineData("territoire", "\"Paris\"")]
    [InlineData("territoire", "\"0\"")]
    [InlineData("email", "\"pas-une-adresse\"")]
    [InlineData("email", "\"Claire <patron@delta.test>\"")]
    [InlineData("email", "§ADRESSE257§")]
    [InlineData("nom", "\"\"")]
    [InlineData("prenom", "\"\"")]
    [InlineData("nom", "§LONG§")]
    [InlineData("code", "null")]
    [InlineData("raisonSociale", "null")]
    [InlineData("siret", "null")]
    [InlineData("territoire", "null")]
    [InlineData("email", "null")]
    [InlineData("nom", "null")]
    [InlineData("prenom", "null")]
    [InlineData("motDePasse", "null")]
    [InlineData(null, null)]
    public async Task Les_champs_sont_refuses_en_francais(string? champ, string? valeur)
    {
        await _fixture.ViderAsync();

        var reponse = await PosterBrut(CorpsBrut(champ, valeur));
        var message = await reponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.DoesNotContain("The ", message);
        Assert.DoesNotContain("field", message);

        // Sans code, le refus est celui du code ; sinon, celui du champ.
        if (champ is null or "code") Assert.StartsWith(InstallationController.DebutRefusCode, message);
        else Assert.DoesNotContain(InstallationController.DebutRefusCode, message);

        Assert.Empty(await EntreprisesAsync());
        Assert.Equal(0, await ComptesAsync());
    }

    private static string CorpsBrut(string? champ, string? valeur)
    {
        if (champ is null) return "{}";

        (string Nom, string Valeur)[] champs =
        [
            ("code", $"\"{Saisi(FabriqueApi.CodeDInstallation)}\""),
            ("raisonSociale", "\"Delta Propreté\""),
            ("siret", "\"40000000000004\""),
            ("territoire", "\"Metropole\""),
            ("email", "\"patron@delta.test\""),
            ("nom", "\"Durand\""),
            ("prenom", "\"Claire\""),
            ("motDePasse", "\"PatronDelta2026\"")
        ];
        Assert.Contains(champs, c => c.Nom == champ);

        var remplacant = valeur switch
        {
            "§LONG§" => $"\"{new string('a', 101)}\"",
            "§ADRESSE257§" => $"\"{new string('a', 257 - "@delta.test".Length)}@delta.test\"",
            _ => valeur
        };
        return "{" + string.Join(",", champs.Select(c => $"\"{c.Nom}\":{(c.Nom == champ ? remplacant : c.Valeur)}")) + "}";
    }

    // --------------------------------------------------------------- sous la RLS

    /// <summary>
    /// Ce que voit la base, et non ce que croit le code : la connexion qui insère l'entreprise
    /// est sous <c>klean_app</c>, avec pour entreprise celle qui a été créée.
    /// </summary>
    [Fact]
    public async Task L_insertion_se_fait_sous_klean_app()
    {
        await _fixture.ViderAsync();
        var sonde = _fixture.Fabrique.Sonde;
        sonde.Vider();

        sonde.Active = true;
        HttpResponseMessage reponse;
        try { reponse = await Poster(Corps()); }
        finally { sonde.Active = false; }

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        var entreprise = Assert.Single(await EntreprisesAsync());

        var insertion = Assert.Single(sonde.Releves, r => r.Commande.Contains("INSERT INTO \"Entreprises\""));
        Assert.Equal(BasculeRoleApplicatif.Role, insertion.Role);
        Assert.Equal(entreprise.Id.ToString(), insertion.Entreprise);

        // Et toute la requête : aucune commande au propriétaire.
        Assert.All(sonde.Releves, r => Assert.Equal(BasculeRoleApplicatif.Role, r.Role));
    }

    [Fact]
    public async Task La_requete_suivante_ne_garde_pas_l_entreprise_de_l_installation()
    {
        await _fixture.ViderAsync();
        Assert.Equal(HttpStatusCode.Created, (await Poster(Corps())).StatusCode);

        var sonde = _fixture.Fabrique.Sonde;
        sonde.Vider();
        var client = _fixture.ClientAnonyme();

        sonde.Active = true;
        try
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/moi")).StatusCode);
            var connexion = await client.PostAsJsonAsync("/api/auth/connexion",
                new ConnexionDto { Email = "patron@delta.test", MotDePasse = "PasLeBonMotDePasse1" });
            Assert.False(connexion.IsSuccessStatusCode);
        }
        finally { sonde.Active = false; }

        Assert.NotEmpty(sonde.Releves);
        Assert.All(sonde.Releves, r => Assert.Equal("", r.Entreprise));
    }

    /// <summary>
    /// Deux assistants validés en même temps : le second attend le verrou, relit les comptes, et
    /// trouve celui du premier. SIRET et adresses différents : sans la relecture, le second
    /// passerait — et non échouerait sur l'adresse en 400, pour la mauvaise raison.
    ///
    /// La course est provoquée, pas espérée (CLAUDE.md §10) : le test tient le verrou, attend que
    /// les deux requêtes y soient bloquées, puis le rend.
    /// </summary>
    [Fact]
    public async Task Deux_installations_simultanees_une_seule_passe()
    {
        await _fixture.ViderAsync();

        Task<HttpResponseMessage> premier, second;

        using (var verrou = _fixture.PorteeAuProprietaire())
        {
            var db = verrou.ServiceProvider.GetRequiredService<KleanDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({ServiceInstallation.EspaceVerrou}, 0)");

                premier = Poster(Corps(siret: "40000000000004", email: "patron@delta.test"));
                second = Poster(Corps(siret: "50000000000005", email: "autre@delta.test"));

                if (!await AttendreAttentesSurVerrouAsync(2))
                {
                    Assert.Fail("Les deux installations n'ont jamais attendu ensemble le verrou consultatif.");
                }
            }
            finally
            {
                await transaction.CommitAsync();
            }
        }

        var statuts = (await Task.WhenAll(premier, second)).Select(r => (int)r.StatusCode).Order().ToArray();

        Assert.Equal([StatusCodes.Status201Created, StatusCodes.Status409Conflict], statuts);
        Assert.Single(await EntreprisesAsync());
        Assert.Equal(1, await ComptesAsync());
    }

    /// <summary>Lu au propriétaire : sous klean_app, pg_stat_activity ne montre pas les autres sessions.</summary>
    private async Task<bool> AttendreAttentesSurVerrouAsync(int attendues)
    {
        using var portee = _fixture.PorteeAuProprietaire();
        var db = portee.ServiceProvider.GetRequiredService<KleanDbContext>();

        for (var essai = 0; essai < 100; essai++)
        {
            var attentes = await db.Database.SqlQueryRaw<int>("""
                SELECT count(*)::int AS "Value" FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event_type = 'Lock'
                  AND wait_event = 'advisory' AND state = 'active'
                """).SingleAsync();
            if (attentes >= attendues) return true;
            await Task.Delay(100);
        }

        return false;
    }

    // --------------------------------------------------------------- orpheline

    [Fact]
    public async Task Une_orpheline_notee_au_demarrage_rend_409()
    {
        await _fixture.ViderAsync();
        await PoserOrphelineAsync("60000000000006");
        Etat.EntrepriseOrpheline = true;

        var reponse = await Poster(Corps());

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        Assert.Contains("DEPLOIEMENT.md", await reponse.Content.ReadAsStringAsync());
        Assert.Equal("Orpheline", Assert.Single(await EntreprisesAsync()).RaisonSociale);
        Assert.Equal(0, await ComptesAsync());
    }

    /// <summary>
    /// Non notée — posée après le démarrage —, l'orpheline est invisible sous la RLS ; elle se
    /// trahit par l'unicité de son SIRET, et la réponse le dit au lieu d'un 500.
    /// </summary>
    [Fact]
    public async Task Une_orpheline_au_meme_SIRET_rend_409_et_non_500()
    {
        await _fixture.ViderAsync();
        await PoserOrphelineAsync("40000000000004");

        var reponse = await Poster(Corps(siret: "40000000000004"));

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        Assert.Contains("DEPLOIEMENT.md", await reponse.Content.ReadAsStringAsync());
        Assert.Equal("Orpheline", Assert.Single(await EntreprisesAsync()).RaisonSociale);
        Assert.Equal(0, await ComptesAsync());
    }

    // ---------------------------------------------------------------- plafonds

    [Fact]
    public async Task La_creation_est_plafonnee_par_adresse()
    {
        await _fixture.ViderAsync();
        var client = _fixture.ClientAnonyme();

        for (var essai = 1; essai <= PlafondsDebit.InstallationsParAdresse; essai++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await Poster(Corps(code: CodeFaux()), client)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await Poster(Corps(code: CodeFaux()), client)).StatusCode);
    }

    /// <summary>
    /// Le bon code a son propre compteur, plus large : le patron s'y reprend sur un SIRET refusé,
    /// et les codes faux d'une même adresse ne l'ont pas épuisé.
    /// </summary>
    [Fact]
    public async Task Le_bon_code_a_son_propre_plafond_par_adresse()
    {
        await _fixture.ViderAsync();
        var client = _fixture.ClientAnonyme();

        for (var essai = 1; essai <= PlafondsDebit.InstallationsParAdresse; essai++)
        {
            await Poster(Corps(code: CodeFaux()), client);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Poster(Corps(code: CodeFaux()), client)).StatusCode);

        for (var essai = 1; essai <= PlafondsDebit.InstallationsBonCodeParAdresse; essai++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await Poster(Corps(siret: "123"), client)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await Poster(Corps(), client)).StatusCode);
        Assert.Empty(await EntreprisesAsync());
    }

    [Fact]
    public async Task La_lecture_de_l_etat_est_plafonnee_par_adresse()
    {
        var client = _fixture.ClientAnonyme();

        for (var essai = 1; essai <= PlafondsDebit.LecturesInstallationParAdresse; essai++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Route)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync(Route)).StatusCode);
    }

    [Fact]
    public async Task Les_codes_faux_sont_plafonnes_globalement()
    {
        await _fixture.ViderAsync();

        for (var i = 1; i <= PlafondsDebit.CodesFauxGlobaux; i++)
        {
            Assert.Equal(StatusCodes.Status400BadRequest, (await EnvoyerDepuis($"198.51.100.{i}", Corps(code: CodeFaux()))).Statut);
        }

        Assert.Equal(StatusCodes.Status429TooManyRequests, (await EnvoyerDepuis("198.51.100.200", Corps(code: CodeFaux()))).Statut);
    }

    /// <summary>Un inconnu qui garde le plafond global plein ne bloque que d'autres inconnus.</summary>
    [Fact]
    public async Task Le_bon_code_passe_le_plafond_global_plein()
    {
        await _fixture.ViderAsync();

        for (var i = 1; i <= PlafondsDebit.CodesFauxGlobaux; i++)
        {
            Assert.Equal(StatusCodes.Status400BadRequest, (await EnvoyerDepuis($"198.51.100.{i}", Corps(code: CodeFaux()))).Statut);
        }

        // Témoin : le plafond global est bien plein.
        Assert.Equal(StatusCodes.Status429TooManyRequests, (await EnvoyerDepuis("198.51.100.200", Corps(code: CodeFaux()))).Statut);

        var (statut, corps) = await EnvoyerDepuis("198.51.100.201", Corps());

        Assert.True(statut == StatusCodes.Status201Created, $"{statut} {corps}");
        Assert.Single(await EntreprisesAsync());
    }

    [Fact]
    public async Task Une_adresse_IPv6_est_plafonnee_par_son_64()
    {
        await _fixture.ViderAsync();

        for (var i = 1; i <= PlafondsDebit.InstallationsParAdresse; i++)
        {
            Assert.Equal(StatusCodes.Status400BadRequest, (await EnvoyerDepuis($"2001:db8:1:2::{i:x}", Corps(code: CodeFaux()))).Statut);
        }

        Assert.Equal(StatusCodes.Status429TooManyRequests,
            (await EnvoyerDepuis("2001:db8:1:2:ffff:ffff:ffff:fffe", Corps(code: CodeFaux()))).Statut);

        // Témoin : le /64 voisin n'est pas touché.
        Assert.Equal(StatusCodes.Status400BadRequest, (await EnvoyerDepuis("2001:db8:1:3::1", Corps(code: CodeFaux()))).Statut);
    }
}
