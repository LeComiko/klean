using System.Net;
using System.Net.Http.Json;
using Klean.Api.Data;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Klean.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Klean.Api.Tests.Infrastructure;

/// <summary>
/// Partagée par toute la série. Migre la base de test une fois, puis fournit
/// une réinitialisation entre chaque test.
///
/// Deux entreprises, et c'est le point essentiel : tout test d'isolation a
/// besoin d'un tenant témoin. Une seule entreprise ne prouve rien.
/// </summary>
public class FixtureApi : IAsyncLifetime
{
    public const string MotDePasse = "MotDePasseTest2026";

    public FabriqueApi Fabrique { get; private set; } = null!;

    public Guid EntrepriseAlpha { get; private set; }
    public Guid EntrepriseBeta { get; private set; }

    public const string AdminAlpha = "admin.alpha@test.local";
    public const string AdminBeta = "admin.beta@test.local";

    /// <summary>
    /// La clé de chaque compte équipé par la fixture, telle que l'écran l'a affichée :
    /// ce que le téléphone aurait retenu. Vidée avec la base.
    /// </summary>
    private readonly Dictionary<string, string> _cles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>> _codes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Le mot de passe provisoire de chaque compte créé par l'API et pas encore connecté
    /// (SPEC.md, étape 24) : ce que l'administrateur aurait transmis. Consommé par la
    /// première connexion, vidé avec la base.
    /// </summary>
    private readonly Dictionary<string, string> _provisoires = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>La seule base que la série a le droit de migrer, de vider et de remplir.</summary>
    public const string BaseDeTest = "klean_test";

    public async Task InitializeAsync()
    {
        // ⚠️ Avant toute écriture — la migration en est une, le vidage plus encore. Lue dans la
        // chaîne, sans connexion : une KLEAN_TEST_CONNECTION qui viserait la base de
        // développement la viderait à la ligne du TRUNCATE (CLAUDE.md §10).
        var baseVisee = new NpgsqlConnectionStringBuilder(FabriqueApi.ChaineTest).Database;
        if (baseVisee != BaseDeTest) throw BaseRefusee(baseVisee);

        // La migration passe par un contexte autonome, avant que l'application
        // ne démarre : AmorcageService s'exécute au démarrage et échouerait sur
        // une base sans tables.
        var options = new DbContextOptionsBuilder<KleanDbContext>()
            .UseNpgsql(FabriqueApi.ChaineTest)
            .Options;

        await using (var db = new KleanDbContext(options))
        {
            // La migration crée la base si elle manque — le premier lancement sur un poste neuf :
            // la base atteinte ne se lit donc qu'après elle. Elle ne touche que la base dont le nom
            // vient d'être vérifié.
            await db.Database.MigrateAsync();

            // Seconde lecture, celle du serveur : ce que la connexion atteint vraiment, avant le vidage.
            var baseAtteinte = await db.Database
                .SqlQueryRaw<string>("SELECT current_database() AS \"Value\"").SingleAsync();
            if (baseAtteinte != BaseDeTest) throw BaseRefusee(baseAtteinte);

            // ⚠️ Avant de démarrer l'hôte, donc l'amorçage. Une série précédente a pu laisser la
            // base vide ou avec une entreprise sans compte (AmorcageTests, InstallationTests) :
            // l'amorçage refuserait alors de démarrer, et toute la série échouerait d'un coup.
            // Vide, l'amorçage par les variables de la fabrique la remplit, puis
            // ReinitialiserAsync la remet à la forme de la série.
            await db.Database.ExecuteSqlRawAsync(SqlVidage);
        }

        Fabrique = new FabriqueApi();

        // Garde : la configuration effective de l'application doit viser la base
        // de test, pas celle de développement. Vérifiée avant toute écriture.
        Fabrique.VerifierCiblageBaseDeTest();

        await ReinitialiserAsync();
    }

    private static InvalidOperationException BaseRefusee(string? nom) => new(
        $"La série de tests vise la base « {nom} » au lieu de « {BaseDeTest} ». Arrêt : continuer "
        + "la viderait. Voir KLEAN_TEST_CONNECTION.");

    public Task DisposeAsync()
    {
        Fabrique.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Les tables métier, le journal d'accès et les comptes. Les rôles restent : l'amorçage les
    /// crée au démarrage, et ils ne changent jamais.
    ///
    /// ⚠️ <c>AccesJournalises</c> ne porte aucune clé étrangère : la cascade ne l'atteint pas, elle
    /// doit être nommée. <c>AmorcageTests</c> vérifie qu'aucune table n'est oubliée.
    /// </summary>
    private const string SqlVidage = """
        TRUNCATE TABLE
            "Remplacements", "Absences", "Interventions", "Prestations",
            "Contrats", "ConsommationsType", "MouvementsStock", "Stocks", "Emplacements", "Sites", "Clients", "Produits", "TypesPrestation",
            "Agents", "Entreprises",
            "JetonsRafraichissement", "AccesJournalises",
            "AspNetUserRoles", "AspNetUserClaims", "AspNetUserLogins",
            "AspNetUserTokens", "AspNetRoleClaims", "AspNetUsers"
        RESTART IDENTITY CASCADE;
        """;

    /// <summary>
    /// Laisse la base sans entreprise ni compte — l'instance neuve que l'amorçage et
    /// l'assistant d'installation trouvent —, et oublie ce que l'API en savait. Une classe qui
    /// l'appelle remet la base en forme en sortant : <c>DisposeAsync =&gt; ReinitialiserAsync</c>.
    /// </summary>
    public async Task ViderAsync()
    {
        using var portee = PorteeAuProprietaire();
        var db = portee.ServiceProvider.GetRequiredService<KleanDbContext>();

        await db.Database.ExecuteSqlRawAsync(SqlVidage);

        var etat = Fabrique.Services.GetRequiredService<EtatInstallation>();
        etat.EntrepriseOrpheline = false;
        etat.Installee = false;
    }

    /// <summary>
    /// Vide les tables métier et les comptes, puis recrée les deux entreprises
    /// et leur administrateur. Les cinq rôles sont conservés : ils sont créés
    /// par l'amorçage au démarrage et ne changent jamais.
    /// </summary>
    public async Task ReinitialiserAsync()
    {
        // Le vidage et la recréation des deux entreprises se font au propriétaire, hors de la
        // sécurité au niveau des lignes (SPEC.md, étape 26) : `klean_app` n'a ni `TRUNCATE`, ni
        // le droit de créer une entreprise qui n'est pas la sienne.
        using var portee = PorteeAuProprietaire();

        // La série partage une instance d'application : sans remise à zéro, les
        // tentatives de connexion s'accumulent d'un test à l'autre et la série
        // finit par se bloquer elle-même sur sa propre limitation de débit. Les
        // tests exercent ainsi les vrais plafonds de production.
        portee.ServiceProvider.GetRequiredService<LimiteurDebit>().Vider();
        _cles.Clear();
        _codes.Clear();
        _provisoires.Clear();
        Fabrique.Courriels.Reinitialiser();
        Fabrique.Horloge.Liberer();

        await ViderAsync();

        EntrepriseAlpha = await CreerEntrepriseEtAdmin(
            portee.ServiceProvider, "Alpha Propreté", "10000000000001", AdminAlpha);

        EntrepriseBeta = await CreerEntrepriseEtAdmin(
            portee.ServiceProvider, "Beta Nettoyage", "20000000000002", AdminBeta);

        // Deux comptes existent : ce que l'amorçage noterait au démarrage en production.
        Fabrique.Services.GetRequiredService<EtatInstallation>().Installee = true;
    }

    private static async Task<Guid> CreerEntrepriseEtAdmin(
        IServiceProvider services, string raisonSociale, string siret, string email)
    {
        var db = services.GetRequiredService<KleanDbContext>();
        var utilisateurs = services.GetRequiredService<UserManager<Utilisateur>>();

        var entreprise = new Entreprise
        {
            RaisonSociale = raisonSociale,
            Siret = siret,
            DateCreation = DateTime.UtcNow
        };

        db.Entreprises.Add(entreprise);
        await db.SaveChangesAsync();

        var admin = new Utilisateur
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            EntrepriseId = entreprise.Id,
            Nom = "Admin",
            Prenom = raisonSociale,
            DateCreation = DateTime.UtcNow,
            Actif = true
        };

        var creation = await utilisateurs.CreateAsync(admin, MotDePasse);
        if (!creation.Succeeded)
        {
            throw new InvalidOperationException(
                "Amorçage de fixture impossible : "
                + string.Join(" ; ", creation.Errors.Select(e => e.Description)));
        }

        await utilisateurs.AddToRoleAsync(admin, RolesKlean.Administrateur);
        return entreprise.Id;
    }

    /// <summary>
    /// Client HTTP porteur d'une session valide pour le compte demandé.
    ///
    /// Depuis l'étape 12, plus aucun jeton ne traverse le corps de la réponse : la
    /// session vit dans trois cookies, que <c>CookieContainer</c> renvoie tout
    /// seul. Reste à faire ce que fera le frontend — recopier le cookie anti-CSRF
    /// dans un en-tête, puisque le navigateur ne le fait pas de lui-même.
    ///
    /// Depuis l'étape 23, l'administrateur et le responsable ne reçoivent pas de session
    /// sur leur seul mot de passe : la fixture les équipe à leur première connexion, par
    /// les routes de l'écran, puis produit un code à chacune des suivantes.
    /// </summary>
    public async Task<HttpClient> ClientConnecteAsync(string email)
    {
        var client = Fabrique.CreerClient();

        var reponse = await ConnexionCompleteAsync(client, email);

        reponse.EnsureSuccessStatusCode();

        ArmerCsrf(client, reponse);

        return client;
    }

    /// <summary>
    /// La connexion jusqu'à la session : le mot de passe, puis, sur un 202, l'équipement
    /// ou le code. Rend la réponse qui pose les cookies de session — ou la première, telle
    /// quelle, si elle n'est pas un 202 : un refus de mot de passe reste observable. Le
    /// corps d'une réussite est toujours une <c>SessionDto</c>.
    ///
    /// Pour les séries qui éprouvent la session et non le second facteur : les cookies de
    /// session qu'elles inspectent viennent désormais de la dernière réponse.
    /// </summary>
    public async Task<HttpResponseMessage> ConnexionCompleteAsync(
        HttpClient client, string email, string motDePasse = MotDePasse)
    {
        if (_provisoires.Remove(email, out var provisoire)) await PremierMotDePasseAsync(email, provisoire, motDePasse);

        var reponse = await client.PostAsJsonAsync("/api/auth/connexion",
            new ConnexionDto { Email = email, MotDePasse = motDePasse });

        if (reponse.StatusCode != HttpStatusCode.Accepted) return reponse;

        var etape = await reponse.Content.ReadFromJsonAsync<EtapeConnexionDto>();

        if (etape!.Etape == nameof(EtapeDefi.SecondFacteur)) return await SaisirCodeAsync(client, email);

        // Premier passage : l'activation ouvre bien une session, mais son corps porte les
        // codes de récupération. L'équipement se fait donc sur un client à part, comme sur
        // un autre appareil, puis la connexion reprend sur celui de l'appelant : il reçoit
        // une réponse de connexion ordinaire, et ne garde aucun cookie de l'activation.
        var appareil = Fabrique.CreerClient();
        await appareil.PostAsJsonAsync("/api/auth/connexion",
            new ConnexionDto { Email = email, MotDePasse = motDePasse });
        var activation = await EquiperAsync(appareil, email);

        // Puis cet appareil se déconnecte : une session de plus fausserait les séries qui
        // comptent les sessions actives d'un compte.
        ArmerCsrf(appareil, activation);
        (await appareil.PostAsync("/api/auth/deconnexion", null)).EnsureSuccessStatusCode();

        return await ConnexionCompleteAsync(client, email, motDePasse);
    }

    /// <summary>
    /// La première connexion d'un compte créé par l'API : le mot de passe provisoire,
    /// puis le mot de passe choisi — celui que le test emploiera ensuite.
    ///
    /// Sur un client à part, comme l'équipement : un compte sans second facteur reçoit
    /// ici une session, qui fausserait les séries comptant les sessions actives. Elle est
    /// fermée aussitôt.
    /// </summary>
    private async Task PremierMotDePasseAsync(string email, string provisoire, string choisi)
    {
        var appareil = Fabrique.CreerClient();

        var connexion = await appareil.PostAsJsonAsync("/api/auth/connexion",
            new ConnexionDto { Email = email, MotDePasse = provisoire });
        var etape = connexion.StatusCode == HttpStatusCode.Accepted
            ? (await connexion.Content.ReadFromJsonAsync<EtapeConnexionDto>())!.Etape
            : null;

        if (etape != nameof(EtapeDefi.ChangementMotDePasse))
        {
            throw new InvalidOperationException(
                $"La première connexion de {email} n'a pas demandé de changer le mot de passe : "
                + $"{(int)connexion.StatusCode} {await connexion.Content.ReadAsStringAsync()}");
        }

        var changement = await appareil.PostAsJsonAsync("/api/auth/connexion/mot-de-passe",
            new NouveauMotDePasseDto { NouveauMotDePasse = choisi });

        if (changement.StatusCode == HttpStatusCode.OK)
        {
            ArmerCsrf(appareil, changement);
            (await appareil.PostAsync("/api/auth/deconnexion", null)).EnsureSuccessStatusCode();
        }
        else if (changement.StatusCode != HttpStatusCode.Accepted)
        {
            throw new InvalidOperationException(
                $"Changement du mot de passe provisoire refusé pour {email} : "
                + await changement.Content.ReadAsStringAsync());
        }
    }

    /// <summary>
    /// Crée un compte par l'API et retient son mot de passe provisoire, comme
    /// l'administrateur qui le transmet. La réponse est rendue intacte, lisible encore.
    /// </summary>
    public async Task<HttpResponseMessage> CreerCompteAsync(HttpClient administrateur, CompteCreateDto dto)
    {
        var reponse = await administrateur.PostAsJsonAsync("/api/auth/comptes", dto);

        if (reponse.IsSuccessStatusCode)
        {
            // Lu une fois, puis remis tel quel : le flux de TestServer ne se relit pas,
            // même mis en mémoire tampon, et l'appelant lit encore la réponse.
            var corps = await reponse.Content.ReadAsStringAsync();
            _provisoires[dto.Email] = System.Text.Json.JsonSerializer.Deserialize<CompteCreeDto>(
                corps, System.Text.Json.JsonSerializerOptions.Web)!.MotDePasseProvisoire;
            reponse.Content = new StringContent(corps, System.Text.Encoding.UTF8, "application/json");
        }

        return reponse;
    }

    /// <summary>
    /// La première connexion seule, sans session à la clé : pour une série qui éprouve la
    /// connexion ordinaire d'un compte créé par l'API. Ensuite, son mot de passe est
    /// <paramref name="choisi"/>.
    /// </summary>
    public async Task LeverProvisoireAsync(string email, string choisi = MotDePasse)
    {
        if (_provisoires.Remove(email, out var provisoire)) await PremierMotDePasseAsync(email, provisoire, choisi);
    }

    /// <summary>Le mot de passe provisoire retenu à la création, s'il n'a pas encore servi.</summary>
    public string ProvisoireDe(string email) => _provisoires[email];

    /// <summary>La clé que la fixture a scannée pour ce compte.</summary>
    public string CleDe(string email) => _cles[email];

    /// <summary>Les codes de récupération remis à l'activation, dans l'ordre.</summary>
    public IReadOnlyList<string> CodesDe(string email) => _codes[email];

    /// <summary>Le code que l'application du téléphone afficherait à l'heure du serveur.</summary>
    public string CodeActuel(string email) => TotpDeTest.Code(_cles[email], Fabrique.Horloge.GetUtcNow());

    /// <summary>
    /// Le premier temps de l'équipement imposé : préparer, lire la clé, activer. Le
    /// cookie de défi est dans le <c>CookieContainer</c> du client.
    /// </summary>
    public async Task<HttpResponseMessage> EquiperAsync(HttpClient client, string email)
    {
        var preparation = await client.PostAsync("/api/auth/second-facteur/preparer", null);
        preparation.EnsureSuccessStatusCode();

        var cle = (await preparation.Content.ReadFromJsonAsync<PreparationSecondFacteurDto>())!.Cle;
        _cles[email] = cle;

        var activation = await client.PostAsJsonAsync("/api/auth/second-facteur/activer",
            new CodeSecondFacteurDto { Code = CodeActuel(email) });

        if (!activation.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Activation du second facteur impossible pour {email} : "
                + await activation.Content.ReadAsStringAsync());
        }

        _codes[email] = (await activation.Content.ReadFromJsonAsync<CodesRecuperationDto>())!.Codes;

        return activation;
    }

    /// <summary>
    /// Le second temps d'une connexion ordinaire.
    ///
    /// ⚠️ <b>Oublie d'abord le dernier pas accepté du compte</b>, et c'est un
    /// contournement délibéré, réservé à cette aide : un code ne sert qu'une fois, et un
    /// test qui connecte deux fois le même compte en trente secondes buterait sinon sur
    /// la garde du rejeu. Celle-ci est éprouvée par <c>SecondFacteurTests</c>, qui
    /// n'appelle jamais cette méthode.
    /// </summary>
    private async Task<HttpResponseMessage> SaisirCodeAsync(HttpClient client, string email)
    {
        await AvecServicesAsync(async services =>
        {
            var utilisateurs = services.GetRequiredService<UserManager<Utilisateur>>();
            var compte = await utilisateurs.FindByEmailAsync(email);

            await utilisateurs.RemoveAuthenticationTokenAsync(
                compte!, ServiceSecondFacteur.Fournisseur, ServiceSecondFacteur.JetonDernierPas);
        });

        var reponse = await client.PostAsJsonAsync("/api/auth/connexion/code",
            new CodeSecondFacteurDto { Code = CodeActuel(email) });

        if (!reponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Second temps de la connexion refusé pour {email} : "
                + await reponse.Content.ReadAsStringAsync());
        }

        return reponse;
    }

    /// <summary>
    /// Recopie le cookie anti-CSRF de la réponse dans l'en-tête par défaut du
    /// client. Rejoue exactement ce que le frontend devra faire.
    /// </summary>
    public static void ArmerCsrf(HttpClient client, HttpResponseMessage reponse)
    {
        var valeur = ValeurDuCookie(reponse, ServiceSession.CookieCsrf)
            ?? throw new InvalidOperationException(
                $"La réponse ne pose pas le cookie « {ServiceSession.CookieCsrf} ».");

        client.DefaultRequestHeaders.Remove(ServiceSession.EnteteCsrf);
        client.DefaultRequestHeaders.Add(ServiceSession.EnteteCsrf, valeur);
    }

    /// <summary>Lit une valeur de cookie dans les en-têtes <c>Set-Cookie</c>.</summary>
    public static string? ValeurDuCookie(HttpResponseMessage reponse, string nom)
    {
        if (!reponse.Headers.TryGetValues("Set-Cookie", out var entetes)) return null;

        foreach (var entete in entetes)
        {
            var debut = $"{nom}=";
            if (!entete.StartsWith(debut, StringComparison.Ordinal)) continue;

            var valeur = entete[debut.Length..].Split(';')[0];

            // Un cookie effacé revient vide : ce n'est pas une valeur utilisable.
            return string.IsNullOrEmpty(valeur) ? null : valeur;
        }

        return null;
    }

    /// <summary>
    /// Crée un compte du rôle demandé via l'API — donc en passant par les mêmes
    /// contrôles qu'en production — puis rend un client connecté dessus.
    /// </summary>
    public async Task<HttpClient> ClientDeRoleAsync(
        HttpClient administrateur, string role, string email,
        Guid? agentId = null, Guid? clientId = null)
    {
        var creation = await CreerCompteAsync(administrateur,
            new CompteCreateDto
            {
                Email = email,
                Nom = role,
                Role = role,
                AgentId = agentId,
                ClientId = clientId
            });

        if (!creation.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Création du compte {role} impossible : "
                + await creation.Content.ReadAsStringAsync());
        }

        return await ClientConnecteAsync(email);
    }

    public HttpClient ClientAnonyme() => Fabrique.CreerClient();

    /// <summary>
    /// Client sans gestion automatique des cookies : l'en-tête <c>Cookie</c> est
    /// posé à la main. Indispensable pour rejouer un jeton révoqué ou en altérer
    /// un — ce que <c>CookieContainer</c> ne permet pas, puisqu'il ne rend jamais
    /// une valeur qu'il a remplacée.
    /// </summary>
    public HttpClient ClientSansCookies() => Fabrique.CreerClient(gererCookies: false);

    /// <summary>
    /// En-tête <c>Set-Cookie</c> complet, attributs compris. Sert à vérifier
    /// <c>HttpOnly</c>, <c>Secure</c> et <c>SameSite</c>, qui ne sont pas des
    /// détails : ce sont eux qui font la différence entre un cookie de session et
    /// un jeton que la première XSS emporte.
    /// </summary>
    public static string? EnteteDuCookie(HttpResponseMessage reponse, string nom)
    {
        if (!reponse.Headers.TryGetValues("Set-Cookie", out var entetes)) return null;

        return entetes.FirstOrDefault(e => e.StartsWith($"{nom}=", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ouvre une portée de services de l'application pour manipuler la base
    /// directement. Réservé à ce que l'API n'expose pas : désactiver un compte,
    /// vieillir une session. Aucun test métier ne doit passer par là — il
    /// contournerait précisément ce qu'il prétend vérifier.
    /// </summary>
    public async Task AvecServicesAsync(Func<IServiceProvider, Task> action)
    {
        using var portee = PorteeAuProprietaire();
        await action(portee.ServiceProvider);
    }

    /// <summary>
    /// Une portée dont le contexte reste au propriétaire, hors de la sécurité au niveau des
    /// lignes (SPEC.md, étape 26) — ce que fait l'amorçage.
    ///
    /// ⚠️ C'est la seule façon d'ouvrir une portée dans un test qui touche la base **sous
    /// l'API** : poser une ligne qu'aucune route n'écrit, éprouver une contrainte par du SQL
    /// direct, ou lire <c>pg_stat_activity</c>. Sans tenant, la base n'accepterait ni ne rendrait
    /// aucune ligne, et sous <c>klean_app</c> les sessions des autres ne s'y voient pas. Côté
    /// EF, rien ne change : l'absence de tenant y valait déjà absence de filtre.
    /// </summary>
    public IServiceScope PorteeAuProprietaire()
    {
        var portee = Fabrique.Services.CreateScope();
        portee.ServiceProvider.GetRequiredService<ITenantContext>().DesactiverFiltrage();
        return portee;
    }

    /// <summary>Désactive un compte en base : aucun endpoint ne le fait encore.</summary>
    public async Task DesactiverCompteAsync(string email)
    {
        await AvecServicesAsync(async services =>
        {
            var utilisateurs = services.GetRequiredService<UserManager<Utilisateur>>();
            var utilisateur = await utilisateurs.FindByEmailAsync(email)
                ?? throw new InvalidOperationException($"Compte {email} introuvable.");

            utilisateur.Actif = false;
            await utilisateurs.UpdateAsync(utilisateur);
        });
    }

    /// <summary>
    /// Client qui ne suit pas les redirections. Indispensable pour prouver
    /// qu'une requête sans jeton répond 401 et non 302 : le client par défaut
    /// suivrait la redirection et le test passerait pour la mauvaise raison.
    /// </summary>
    public HttpClient ClientAnonymeSansRedirection() =>
        Fabrique.CreerClient(suivreRedirections: false);
}

[CollectionDefinition(Nom)]
public class CollectionApi : ICollectionFixture<FixtureApi>
{
    public const string Nom = "api";
}
