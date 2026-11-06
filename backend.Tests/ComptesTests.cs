using System.Net;
using System.Net.Http.Json;
using Klean.Api.Data;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Klean.Api.Services;
using Klean.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Klean.Api.Tests;

/// <summary>
/// Les comptes de l'entreprise (SPEC.md, étape 24) : la liste de l'administrateur, le
/// mot de passe provisoire de la création, la fermeture et la réouverture d'un compte.
///
/// ⚠️ Comme pour la pseudonymisation, les effets se relisent <b>en base</b>, champ par
/// champ : <c>Actif</c>, l'empreinte de sécurité et la révocation des sessions se
/// couvrent l'une l'autre vues de l'API, et un sabotage de l'une resterait vert.
/// </summary>
[Collection(CollectionApi.Nom)]
public class ComptesTests : IAsyncLifetime
{
    private const string Responsable = "responsable.comptes@test.local";
    private const string Chef = "chef.comptes@test.local";
    private const string Choisi = "ChoisiParMoi2026";

    private readonly FixtureApi _fixture;
    private HttpClient _admin = null!;

    /// <summary>
    /// Les lignes du journal présentes au début du test : la base n'est pas purgée du
    /// journal entre deux tests, et une série lancée après une autre en hériterait.
    /// </summary>
    private HashSet<Guid> _connues = [];

    public ComptesTests(FixtureApi fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await _fixture.ReinitialiserAsync();
        _admin = await _fixture.ClientConnecteAsync(FixtureApi.AdminAlpha);

        await _fixture.AvecServicesAsync(async services =>
            _connues = (await services.GetRequiredService<KleanDbContext>().AccesJournalises
                .Select(l => l.Id).ToListAsync()).ToHashSet());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------------- la liste

    [Fact]
    public async Task La_liste_ne_rend_que_les_comptes_de_l_entreprise()
    {
        await Creer(RolesKlean.Responsable, Responsable);

        var comptes = await Liste();

        // L'administrateur de Beta est en base, créé par la fixture sous toute
        // validation de l'API : c'est l'intrus que le filtre écrit à la main doit écarter.
        Assert.Contains(comptes, c => c.Email == FixtureApi.AdminAlpha);
        Assert.Contains(comptes, c => c.Email == Responsable);
        Assert.DoesNotContain(comptes, c => c.Email == FixtureApi.AdminBeta);
    }

    [Fact]
    public async Task La_liste_dit_le_role_et_l_etat_de_chaque_compte()
    {
        await Creer(RolesKlean.ChefEquipe, Chef);

        var avant = (await Liste()).Single(c => c.Email == Chef);
        Assert.Equal(RolesKlean.ChefEquipe, avant.Role);
        Assert.True(avant.Actif);
        Assert.True(avant.MotDePasseProvisoire);
        Assert.False(avant.SecondFacteurActif);

        await _fixture.ClientConnecteAsync(Chef);

        Assert.False((await Liste()).Single(c => c.Email == Chef).MotDePasseProvisoire);
        Assert.True((await Liste()).Single(c => c.Email == FixtureApi.AdminAlpha).SecondFacteurActif);
    }

    [Fact]
    public async Task Les_comptes_pseudonymises_viennent_en_fin_de_liste()
    {
        var id = await Creer(RolesKlean.Responsable, Responsable);

        // Un nom qui se range APRÈS « Compte anonymisé » : sans lui, l'ordre alphabétique
        // seul mettait déjà le compte pseudonymisé en dernier, et un sabotage du rangement
        // restait vert.
        (await _fixture.CreerCompteAsync(_admin, new CompteCreateDto
        {
            Email = Chef,
            Nom = "Zoé",
            Role = RolesKlean.ChefEquipe
        })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await _admin.PostAsync($"/api/auth/comptes/{id}/anonymiser", null)).StatusCode);

        var comptes = await Liste();

        Assert.Equal(id, comptes[^1].Id);
        Assert.NotNull(comptes[^1].DateAnonymisation);
        Assert.All(comptes.Take(comptes.Count - 1), c => Assert.Null(c.DateAnonymisation));
    }

    [Fact]
    public async Task La_liste_est_journalisee_et_fermee_au_responsable()
    {
        await Creer(RolesKlean.Responsable, Responsable);

        var comptes = await Liste();
        var ligne = (await Journal(NatureAcces.Liste)).Single(l => l.Ressource == RessourceAccedee.Compte);
        Assert.Equal(comptes.Count, ligne.Lignes);

        var responsable = await _fixture.ClientConnecteAsync(Responsable);
        Assert.Equal(HttpStatusCode.Forbidden, (await responsable.GetAsync("/api/auth/comptes")).StatusCode);
    }

    // ----------------------------------------------------- le mot de passe provisoire

    [Fact]
    public async Task La_creation_tire_un_mot_de_passe_qui_impose_d_en_changer()
    {
        await Creer(RolesKlean.Responsable, Responsable);
        var provisoire = _fixture.ProvisoireDe(Responsable);

        // Seize caractères en quatre groupes, et la règle d'Identity tenue.
        Assert.Matches("^[A-Za-z2-9]{4}(-[A-Za-z2-9]{4}){3}$", provisoire);
        Assert.Contains(provisoire, char.IsUpper);
        Assert.Contains(provisoire, char.IsLower);
        Assert.Contains(provisoire, char.IsDigit);
        Assert.True((await CompteEnBase(Responsable)).MotDePasseProvisoire);

        var client = _fixture.ClientAnonyme();
        Assert.Equal(nameof(EtapeDefi.ChangementMotDePasse), await Etape(await Connexion(client, Responsable, provisoire)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/moi")).StatusCode);

        // Un rôle obligé équipe son téléphone ensuite, et seulement ensuite.
        var changement = await PremierMotDePasse(client, Choisi);
        Assert.Equal(nameof(EtapeDefi.Equipement), await Etape(changement));
        Assert.False((await CompteEnBase(Responsable)).MotDePasseProvisoire);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Connexion(_fixture.ClientAnonyme(), Responsable, provisoire)).StatusCode);
        Assert.Equal(nameof(EtapeDefi.Equipement), await Etape(await Connexion(_fixture.ClientAnonyme(), Responsable, Choisi)));
    }

    [Fact]
    public async Task Un_compte_sans_second_facteur_recoit_sa_session_apres_le_premier_mot_de_passe()
    {
        await Creer(RolesKlean.ChefEquipe, Chef);

        var client = _fixture.ClientAnonyme();
        await Connexion(client, Chef, _fixture.ProvisoireDe(Chef));
        var changement = await PremierMotDePasse(client, Choisi);

        Assert.Equal(HttpStatusCode.OK, changement.StatusCode);
        Assert.Equal(Chef, (await changement.Content.ReadFromJsonAsync<SessionDto>())!.Profil.Email);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/moi")).StatusCode);
    }

    [Fact]
    public async Task Le_premier_mot_de_passe_suit_les_regles_et_ne_reprend_pas_le_provisoire()
    {
        await Creer(RolesKlean.ChefEquipe, Chef);
        var provisoire = _fixture.ProvisoireDe(Chef);

        var client = _fixture.ClientAnonyme();
        await Connexion(client, Chef, provisoire);

        var faible = await PremierMotDePasse(client, "SansChiffreIci");
        Assert.Equal(HttpStatusCode.BadRequest, faible.StatusCode);
        Assert.Contains("chiffre", await faible.Content.ReadAsStringAsync());

        var meme = await PremierMotDePasse(client, provisoire);
        Assert.Equal(HttpStatusCode.BadRequest, meme.StatusCode);
        Assert.Contains("différent", await meme.Content.ReadAsStringAsync());

        Assert.True((await CompteEnBase(Chef)).MotDePasseProvisoire);
    }

    /// <summary>
    /// Le défi d'une autre étape ne change aucun mot de passe : sans cette garde, qui
    /// tient le mot de passe d'un compte équipé le remplacerait sans jamais saisir de code.
    /// </summary>
    [Fact]
    public async Task Le_premier_mot_de_passe_exige_le_defi_de_son_etape()
    {
        var empreinte = (await CompteEnBase(FixtureApi.AdminAlpha)).PasswordHash;

        var client = _fixture.ClientAnonyme();
        Assert.Equal(nameof(EtapeDefi.SecondFacteur), await Etape(await Connexion(client, FixtureApi.AdminAlpha, FixtureApi.MotDePasse)));

        Assert.Equal(HttpStatusCode.Unauthorized, (await PremierMotDePasse(client, Choisi)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PremierMotDePasse(_fixture.ClientAnonyme(), Choisi)).StatusCode);
        Assert.Equal(empreinte, (await CompteEnBase(FixtureApi.AdminAlpha)).PasswordHash);
    }

    [Fact]
    public async Task Le_mot_de_passe_oublie_leve_le_provisoire()
    {
        await Creer(RolesKlean.ChefEquipe, Chef);

        await _fixture.ClientAnonyme().PostAsJsonAsync("/api/auth/mot-de-passe-oublie",
            new MotDePasseOublieDto { Email = Chef });
        var jeton = ServiceCourrielDeTest.JetonDuLien(_fixture.Fabrique.Courriels.Dernier.Corps);
        var reinitialisation = await _fixture.ClientAnonyme().PostAsJsonAsync("/api/auth/reinitialiser",
            new ReinitialiserDto { Email = Chef, Jeton = jeton, NouveauMotDePasse = Choisi });
        Assert.Equal(HttpStatusCode.NoContent, reinitialisation.StatusCode);

        Assert.False((await CompteEnBase(Chef)).MotDePasseProvisoire);
        Assert.Equal(HttpStatusCode.OK, (await Connexion(_fixture.ClientAnonyme(), Chef, Choisi)).StatusCode);
    }

    // ------------------------------------------------- fermer et rouvrir un compte

    [Fact]
    public async Task Desactiver_coupe_l_acces_et_les_sessions()
    {
        var id = await Creer(RolesKlean.ChefEquipe, Chef);
        var chef = await _fixture.ClientConnecteAsync(Chef);
        var empreinte = (await CompteEnBase(Chef)).SecurityStamp;

        var reponse = await _admin.PostAsync($"/api/auth/comptes/{id}/desactiver", null);
        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.False((await reponse.Content.ReadFromJsonAsync<CompteDto>())!.Actif);

        // Relu en base, champ par champ.
        var compte = await CompteEnBase(Chef);
        Assert.False(compte.Actif);
        Assert.NotEqual(empreinte, compte.SecurityStamp);
        Assert.Equal(0, await SessionsVivantes(id));

        Assert.Equal(HttpStatusCode.Unauthorized, (await chef.GetAsync("/api/auth/moi")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await chef.PostAsync("/api/auth/rafraichir", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Connexion(_fixture.ClientAnonyme(), Chef, FixtureApi.MotDePasse)).StatusCode);

        Assert.Equal(id, (await Journal(NatureAcces.Desactivation)).Single().CibleId);
    }

    [Fact]
    public async Task Reactiver_rend_l_acces_avec_le_meme_mot_de_passe()
    {
        var id = await Creer(RolesKlean.ChefEquipe, Chef);
        await _fixture.ClientConnecteAsync(Chef);
        await _admin.PostAsync($"/api/auth/comptes/{id}/desactiver", null);

        var reponse = await _admin.PostAsync($"/api/auth/comptes/{id}/reactiver", null);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.True((await CompteEnBase(Chef)).Actif);
        Assert.Equal(HttpStatusCode.OK, (await Connexion(_fixture.ClientAnonyme(), Chef, FixtureApi.MotDePasse)).StatusCode);
        Assert.Equal(id, (await Journal(NatureAcces.Reactivation)).Single().CibleId);
    }

    [Fact]
    public async Task Fermer_et_rouvrir_refusent_et_nomment_leurs_sorties()
    {
        var soi = (await CompteEnBase(FixtureApi.AdminAlpha)).Id;
        var autreEntreprise = (await CompteEnBase(FixtureApi.AdminBeta)).Id;
        var chef = await Creer(RolesKlean.ChefEquipe, Chef);
        var efface = await Creer(RolesKlean.Responsable, Responsable);
        await _admin.PostAsync($"/api/auth/comptes/{efface}/anonymiser", null);

        await Refus(HttpStatusCode.Conflict, "propre compte", "desactiver", soi);
        await Refus(HttpStatusCode.Conflict, "déjà actif", "reactiver", chef);
        await Refus(HttpStatusCode.Conflict, "pseudonymisé", "desactiver", efface);
        await Refus(HttpStatusCode.Conflict, "Créer un compte neuf", "reactiver", efface);
        await Refus(HttpStatusCode.NotFound, null, "desactiver", autreEntreprise);
        await Refus(HttpStatusCode.NotFound, null, "reactiver", autreEntreprise);

        await _admin.PostAsync($"/api/auth/comptes/{chef}/desactiver", null);
        await Refus(HttpStatusCode.Conflict, "déjà désactivé", "desactiver", chef);

        Assert.True((await CompteEnBase(FixtureApi.AdminAlpha)).Actif);
        Assert.True((await CompteEnBase(FixtureApi.AdminBeta)).Actif);
        Assert.Empty(await Journal(NatureAcces.Reactivation));
    }

    [Fact]
    public async Task Fermer_un_compte_est_reserve_a_l_administrateur()
    {
        var chef = await Creer(RolesKlean.ChefEquipe, Chef);
        await Creer(RolesKlean.Responsable, Responsable);
        var responsable = await _fixture.ClientConnecteAsync(Responsable);

        Assert.Equal(HttpStatusCode.Forbidden, (await responsable.PostAsync($"/api/auth/comptes/{chef}/desactiver", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await responsable.PostAsync($"/api/auth/comptes/{chef}/reactiver", null)).StatusCode);
        Assert.True((await CompteEnBase(Chef)).Actif);
    }

    // ---- Fabrication ----

    private async Task<Guid> Creer(string role, string email)
    {
        var reponse = await _fixture.CreerCompteAsync(_admin, new CompteCreateDto
        {
            Email = email,
            Nom = role,
            Prenom = "Test",
            Role = role
        });

        reponse.EnsureSuccessStatusCode();
        return (await reponse.Content.ReadFromJsonAsync<CompteCreeDto>())!.Compte.Id;
    }

    private async Task<List<CompteDto>> Liste()
    {
        var reponse = await _admin.GetAsync("/api/auth/comptes");
        reponse.EnsureSuccessStatusCode();

        return (await reponse.Content.ReadFromJsonAsync<List<CompteDto>>())!;
    }

    private async Task Refus(HttpStatusCode attendu, string? motif, string geste, Guid id)
    {
        var reponse = await _admin.PostAsync($"/api/auth/comptes/{id}/{geste}", null);

        Assert.Equal(attendu, reponse.StatusCode);
        if (motif is not null) Assert.Contains(motif, await reponse.Content.ReadAsStringAsync());
    }

    private static Task<HttpResponseMessage> Connexion(HttpClient client, string email, string motDePasse) =>
        client.PostAsJsonAsync("/api/auth/connexion", new ConnexionDto { Email = email, MotDePasse = motDePasse });

    private static Task<HttpResponseMessage> PremierMotDePasse(HttpClient client, string motDePasse) =>
        client.PostAsJsonAsync("/api/auth/connexion/mot-de-passe", new NouveauMotDePasseDto { NouveauMotDePasse = motDePasse });

    private static async Task<string?> Etape(HttpResponseMessage reponse) =>
        reponse.StatusCode == HttpStatusCode.Accepted
            ? (await reponse.Content.ReadFromJsonAsync<EtapeConnexionDto>())!.Etape
            : $"{(int)reponse.StatusCode}";

    /// <summary>Le compte relu dans une portée neuve : ce qui est en base, pas en mémoire.</summary>
    private async Task<Utilisateur> CompteEnBase(string email)
    {
        Utilisateur? compte = null;
        await _fixture.AvecServicesAsync(async services =>
            compte = await services.GetRequiredService<KleanDbContext>().Users.AsNoTracking()
                .SingleAsync(u => u.NormalizedEmail == email.ToUpperInvariant()));

        return compte!;
    }

    private async Task<int> SessionsVivantes(Guid id)
    {
        var nombre = 0;
        await _fixture.AvecServicesAsync(async services =>
            nombre = await services.GetRequiredService<KleanDbContext>().JetonsRafraichissement
                .CountAsync(j => j.UtilisateurId == id && j.DateRevocation == null));

        return nombre;
    }

    private async Task<List<AccesJournalise>> Journal(NatureAcces nature)
    {
        List<AccesJournalise> lignes = [];
        await _fixture.AvecServicesAsync(async services =>
            lignes = await services.GetRequiredService<KleanDbContext>().AccesJournalises.AsNoTracking()
                .Where(l => l.Nature == nature)
                .ToListAsync());

        return lignes.Where(l => !_connues.Contains(l.Id)).ToList();
    }
}
