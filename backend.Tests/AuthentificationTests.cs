using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Klean.Api.Tests.Infrastructure;

namespace Klean.Api.Tests;

[Collection(CollectionApi.Nom)]
public class AuthentificationTests : IAsyncLifetime
{
    private readonly FixtureApi _fixture;

    public AuthentificationTests(FixtureApi fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ReinitialiserAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Connexion_retourne_un_jeton_portant_entrepriseId()
    {
        var client = _fixture.ClientAnonyme();

        var reponse = await client.PostAsJsonAsync("/api/auth/connexion",
            new ConnexionDto { Email = FixtureApi.AdminAlpha, MotDePasse = FixtureApi.MotDePasse });

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);

        var jeton = await reponse.Content.ReadFromJsonAsync<JetonDto>();
        Assert.NotNull(jeton);
        Assert.NotEmpty(jeton.Jeton);
        Assert.Equal(_fixture.EntrepriseAlpha, jeton.Profil.EntrepriseId);
        Assert.Contains(RolesKlean.Administrateur, jeton.Profil.Roles);

        // La revendication entrepriseId est ce qui alimente le multi-tenant :
        // elle doit être dans le jeton, pas seulement dans le profil renvoyé.
        var revendications = RevendicationsDe(jeton.Jeton);
        Assert.Equal(_fixture.EntrepriseAlpha.ToString(), revendications["entrepriseId"]);
    }

    [Fact]
    public async Task Jeton_expire_dans_environ_huit_heures()
    {
        var client = _fixture.ClientAnonyme();
        var reponse = await client.PostAsJsonAsync("/api/auth/connexion",
            new ConnexionDto { Email = FixtureApi.AdminAlpha, MotDePasse = FixtureApi.MotDePasse });

        var jeton = await reponse.Content.ReadFromJsonAsync<JetonDto>();
        var restant = jeton!.Expiration - DateTime.UtcNow;

        Assert.InRange(restant.TotalHours, 7.9, 8.1);
    }

    [Fact]
    public async Task Moi_avec_jeton_retourne_le_profil()
    {
        var client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        var profil = await client.GetFromJsonAsync<ProfilDto>("/api/auth/moi");

        Assert.NotNull(profil);
        Assert.Equal(FixtureApi.AdminAlpha, profil.Email);
        Assert.Equal(_fixture.EntrepriseAlpha, profil.EntrepriseId);
        Assert.True(profil.Actif);
    }

    [Fact]
    public async Task Moi_sans_jeton_renvoie_401_sans_redirection()
    {
        // AddIdentity pose des schémas cookie qui redirigent vers /Account/Login.
        // Ce test est la garde contre leur retour (CLAUDE.md §10).
        var client = _fixture.ClientAnonymeSansRedirection();

        var reponse = await client.GetAsync("/api/auth/moi");

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
        Assert.Null(reponse.Headers.Location);
        Assert.Contains("Bearer", reponse.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Jeton_altere_renvoie_401()
    {
        var client = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
        var original = client.DefaultRequestHeaders.Authorization!.Parameter;

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", original + "xyz");

        var reponse = await client.GetAsync("/api/auth/moi");

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task Compte_inexistant_et_mot_de_passe_faux_donnent_la_meme_reponse()
    {
        var client = _fixture.ClientAnonyme();

        var inexistant = await client.PostAsJsonAsync("/api/auth/connexion",
            new ConnexionDto { Email = "fantome@test.local", MotDePasse = FixtureApi.MotDePasse });

        var mauvaisMotDePasse = await client.PostAsJsonAsync("/api/auth/connexion",
            new ConnexionDto { Email = FixtureApi.AdminAlpha, MotDePasse = "MauvaisMotDePasse1" });

        Assert.Equal(HttpStatusCode.Unauthorized, inexistant.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, mauvaisMotDePasse.StatusCode);

        // Pas d'énumération des comptes : le message ne doit pas trahir
        // lequel des deux cas s'est produit.
        Assert.Equal(
            await inexistant.Content.ReadAsStringAsync(),
            await mauvaisMotDePasse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Responsable_interdit_sur_la_creation_de_compte()
    {
        var admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        var creation = await admin.PostAsJsonAsync("/api/auth/comptes", new CompteCreateDto
        {
            Email = "resp@test.local",
            MotDePasse = FixtureApi.MotDePasse,
            Nom = "Martin",
            Prenom = "Claire",
            Role = RolesKlean.Responsable
        });
        Assert.Equal(HttpStatusCode.OK, creation.StatusCode);

        var responsable = await _fixture.ClientConnecteAsync("resp@test.local");

        var moi = await responsable.GetAsync("/api/auth/moi");
        Assert.Equal(HttpStatusCode.OK, moi.StatusCode);

        var tentative = await responsable.PostAsJsonAsync("/api/auth/comptes", new CompteCreateDto
        {
            Email = "autre@test.local",
            MotDePasse = FixtureApi.MotDePasse,
            Nom = "X",
            Role = RolesKlean.Responsable
        });
        Assert.Equal(HttpStatusCode.Forbidden, tentative.StatusCode);
    }

    [Fact]
    public async Task Compte_verrouille_apres_cinq_echecs_meme_avec_le_bon_mot_de_passe()
    {
        // Sur un compte dédié : verrouiller un administrateur de la fixture
        // casserait les autres tests de la série.
        var admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);
        const string cible = "verrou@test.local";

        await admin.PostAsJsonAsync("/api/auth/comptes", new CompteCreateDto
        {
            Email = cible,
            MotDePasse = FixtureApi.MotDePasse,
            Nom = "Verrou",
            Role = RolesKlean.Responsable
        });

        var client = _fixture.ClientAnonyme();

        for (var essai = 0; essai < 5; essai++)
        {
            await client.PostAsJsonAsync("/api/auth/connexion",
                new ConnexionDto { Email = cible, MotDePasse = "MauvaisMotDePasse1" });
        }

        var avecLeBon = await client.PostAsJsonAsync("/api/auth/connexion",
            new ConnexionDto { Email = cible, MotDePasse = FixtureApi.MotDePasse });

        Assert.Equal(HttpStatusCode.Unauthorized, avecLeBon.StatusCode);
        Assert.Contains("verrouillé", await avecLeBon.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Mot_de_passe_sans_chiffre_refuse()
    {
        var admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        var reponse = await admin.PostAsJsonAsync("/api/auth/comptes", new CompteCreateDto
        {
            Email = "faible@test.local",
            MotDePasse = "SansChiffreIci",
            Nom = "X",
            Role = RolesKlean.Responsable
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Role_inconnu_refuse_avec_la_liste_des_valeurs()
    {
        var admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        var reponse = await admin.PostAsJsonAsync("/api/auth/comptes", new CompteCreateDto
        {
            Email = "compta@test.local",
            MotDePasse = FixtureApi.MotDePasse,
            Nom = "X",
            Role = "Comptable"
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        var corps = await reponse.Content.ReadAsStringAsync();
        Assert.Contains(RolesKlean.ChefEquipe, corps);
    }

    private static Dictionary<string, string> RevendicationsDe(string jeton)
    {
        var corps = jeton.Split('.')[1];
        corps = corps.PadRight(corps.Length + (4 - corps.Length % 4) % 4, '=')
                     .Replace('-', '+').Replace('_', '/');

        var json = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(corps));

        return json.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.ToString());
    }
}
