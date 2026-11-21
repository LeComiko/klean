using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Klean.Api.Controllers;
using Klean.Api.Data;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Klean.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Demonstration;

/// <summary>Un refus qui se dit à l'exploitant, sans pile d'appels.</summary>
public sealed class RefusDemonstration(string message) : Exception(message);

/// <summary>Ce que la commande a installé, relu en base sous l'identité de l'administrateur.</summary>
public sealed record RapportDemonstration(
    string Entreprise,
    DateOnly Aujourdhui,
    int Clients,
    int Sites,
    int Agents,
    int TypesPrestation,
    int Vacations,
    int NonPourvues,
    int RemplacementsEnRecherche,
    int RemplacementsPourvus);

/// <summary>
/// Le jeu de démonstration installable (SPEC.md, étape 28) : <c>dotnet run -- demonstration</c>,
/// ou <c>docker compose run --rm api demonstration</c>.
///
/// ⚠️ <b>Il traverse les contrôleurs, pas seulement les services.</b> Une partie des règles
/// n'existe que là — l'unicité d'un libellé, l'éligibilité d'un agent au <c>POST</c> d'une
/// vacation, le refus d'un motif sur un arrêt maladie. Le jeu construit donc chaque
/// contrôleur par l'injection, sous l'identité de l'administrateur, et valide chaque DTO par
/// ses attributs comme le ferait la liaison. Ce qui n'y passe pas, et c'est voulu : le CSRF,
/// l'autorisation par attribut, la journalisation. Le jeu ne laisse aucune ligne au journal.
///
/// ⚠️ <b>Le passé se rejoue avec une horloge posée ce jour-là</b> (<see cref="HorlogeDecalee"/>) :
/// la génération part de deux semaines en arrière, et ne contourne aucune règle pour autant.
///
/// ⚠️ <b>L'installation n'est pas atomique.</b> La génération et le stock ouvrent leur propre
/// transaction, qu'une transaction englobante ferait échouer. Un refus d'un contrôleur arrête
/// tout et se dit, mais ce qui précède reste écrit. <c>JeuDeDemonstrationTests</c> joue la
/// commande à chaque série : c'est là que ce refus doit apparaître, pas chez un exploitant.
/// </summary>
public sealed class JeuDeDemonstration
{
    /// <summary>L'argument qui lance la commande au lieu du serveur.</summary>
    public const string Commande = "demonstration";

    /// <summary>Jours d'historique : la génération part d'aujourd'hui moins celui-ci.</summary>
    public const int JoursHistorique = 14;

    /// <summary>Jours à venir, comptés depuis aujourd'hui.</summary>
    public const int JoursAVenir = 28;

    public const string RaisonSociale = "Démonstration — Propreté";

    private readonly IServiceProvider _racine;

    public JeuDeDemonstration(IServiceProvider racine) => _racine = racine;

    /// <summary>
    /// La commande : trouve l'entreprise et son administrateur, installe, rend compte. Rend le
    /// code de sortie du processus.
    /// </summary>
    public static async Task<int> ExecuterCommandeAsync(
        IServiceProvider racine, TextWriter sortie, CancellationToken annulation = default)
    {
        try
        {
            var (entreprise, administrateur) = await CibleAsync(racine, annulation);
            var rapport = await new JeuDeDemonstration(racine).InstallerAsync(entreprise, administrateur, annulation);

            Ecrire(sortie, rapport);
            return 0;
        }
        catch (RefusDemonstration refus)
        {
            sortie.WriteLine($"✗ {refus.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Installe le jeu dans cette entreprise, sous l'identité de cet administrateur.
    ///
    /// ⚠️ Sous cette identité, les filtres d'EF et la sécurité au niveau des lignes
    /// s'appliquent comme à une requête (étape 26) : le jeu ne peut rien écrire ailleurs.
    /// </summary>
    public async Task<RapportDemonstration> InstallerAsync(
        Guid entrepriseId, Guid administrateurId, CancellationToken annulation = default)
    {
        using var portee = _racine.CreateScope();
        var services = portee.ServiceProvider;
        var accesseur = services.GetRequiredService<IHttpContextAccessor>();

        var contexte = new DefaultHttpContext
        {
            RequestServices = services,
            User = Identite(entrepriseId, administrateurId, RolesKlean.Administrateur)
        };

        accesseur.HttpContext = contexte;

        try
        {
            return await new Installation(services, contexte, entrepriseId, annulation).ExecuterAsync();
        }
        finally
        {
            accesseur.HttpContext = null;
        }
    }

    /// <summary>Les revendications qu'un jeton de ce compte porterait (<see cref="JwtService"/>).</summary>
    internal static ClaimsPrincipal Identite(Guid entrepriseId, Guid utilisateurId, string role) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, utilisateurId.ToString()),
                new Claim(ClaimTypes.Role, role),
                new Claim(JwtService.RevendicationEntrepriseId, entrepriseId.ToString())
            ],
            authenticationType: "Demonstration"));

    /// <summary>
    /// L'entreprise unique de l'instance, et son premier administrateur actif. Lus au
    /// propriétaire, hors de tout tenant : c'est l'amorçage qui les a posés.
    /// </summary>
    private static async Task<(Guid Entreprise, Guid Administrateur)> CibleAsync(
        IServiceProvider racine, CancellationToken annulation)
    {
        using var portee = racine.CreateScope();
        portee.ServiceProvider.GetRequiredService<ITenantContext>().DesactiverFiltrage();

        var db = portee.ServiceProvider.GetRequiredService<KleanDbContext>();
        var entreprises = await db.Entreprises.Select(e => e.Id).ToListAsync(annulation);

        if (entreprises.Count != 1)
        {
            throw new RefusDemonstration(
                $"L'instance compte {entreprises.Count} entreprises. Le jeu de démonstration "
                + "s'installe dans l'entreprise unique d'une instance neuve, celle de l'amorçage.");
        }

        var utilisateurs = portee.ServiceProvider.GetRequiredService<UserManager<Utilisateur>>();
        var administrateur = (await utilisateurs.GetUsersInRoleAsync(RolesKlean.Administrateur))
            .Where(u => u.EntrepriseId == entreprises[0] && u.Actif && u.DateAnonymisation is null)
            .OrderBy(u => u.DateCreation)
            .FirstOrDefault()
            ?? throw new RefusDemonstration(
                "Aucun administrateur actif dans l'entreprise : le jeu s'installe sous son identité.");

        return (entreprises[0], administrateur.Id);
    }

    private static void Ecrire(TextWriter sortie, RapportDemonstration r)
    {
        sortie.WriteLine($"✓ Jeu de démonstration installé dans « {r.Entreprise} », autour du {r.Aujourdhui:dd/MM/yyyy}.");
        sortie.WriteLine($"  {r.Clients} clients, {r.Sites} sites, {r.TypesPrestation} types de prestation, {r.Agents} agents.");
        sortie.WriteLine($"  {r.Vacations} vacations, de {JoursHistorique} jours en arrière à {JoursAVenir} jours devant, "
            + $"dont {r.NonPourvues} sans agent.");
        sortie.WriteLine($"  {r.RemplacementsEnRecherche} remplacement en recherche, {r.RemplacementsPourvus} pourvu.");
    }

    /// <summary>Une installation : une portée, une identité, une suite de gestes.</summary>
    private sealed class Installation
    {
        private readonly IServiceProvider _services;
        private readonly HttpContext _contexte;
        private readonly Guid _entrepriseId;
        private readonly CancellationToken _annulation;
        private readonly KleanDbContext _db;

        public Installation(IServiceProvider services, HttpContext contexte, Guid entrepriseId, CancellationToken annulation)
        {
            _services = services;
            _contexte = contexte;
            _entrepriseId = entrepriseId;
            _annulation = annulation;
            _db = services.GetRequiredService<KleanDbContext>();
        }

        public async Task<RapportDemonstration> ExecuterAsync()
        {
            await GarderAsync();

            var aujourdhui = await _services.GetRequiredService<ServiceGeneration>().AujourdhuiAsync(_annulation);

            await RenommerEntrepriseAsync();
            var types = await CreerTypesAsync();
            var clients = await CreerClientsAsync();
            var agents = await CreerAgentsAsync();
            await CreerSitesEtPrestationsAsync(clients, types, agents, aujourdhui);
            await GenererAsync(aujourdhui);
            await PoserAbsencesAsync(agents, aujourdhui);

            return await RapportAsync(aujourdhui);
        }

        // ------------------------------------------------------------------ la garde

        /// <summary>
        /// Une entreprise vide, ou rien : le jeu n'écrase et ne complète jamais des données
        /// qu'il n'a pas posées — un second passage compris.
        /// </summary>
        private async Task GarderAsync()
        {
            var clients = await _db.Clients.CountAsync(_annulation);
            var agents = await _db.Agents.CountAsync(_annulation);

            if (clients > 0 || agents > 0)
            {
                throw new RefusDemonstration(
                    $"L'entreprise porte déjà {clients} client(s) et {agents} agent(s). Le jeu de "
                    + "démonstration ne s'installe que dans une entreprise vide, et ne complète "
                    + "jamais des données existantes : partir d'une base neuve.");
            }
        }

        // ------------------------------------------------------------------ le référentiel

        private async Task RenommerEntrepriseAsync()
        {
            var controleur = Controleur<EntreprisesController>();
            var actuelle = Valeur(await controleur.Obtenir(_entrepriseId), "l'entreprise");

            Succes(await controleur.Modifier(_entrepriseId, Valide(new EntrepriseCreateDto
            {
                RaisonSociale = RaisonSociale,
                Siret = actuelle.Siret,
                Territoire = nameof(Territoire.Metropole)
            })), "le renommage de l'entreprise");
        }

        private async Task<Dictionary<string, Guid>> CreerTypesAsync()
        {
            var typesControleur = Controleur<TypesPrestationController>();
            var pointsControleur = Controleur<PointsControleController>();
            var types = new Dictionary<string, Guid>();

            foreach (var (cle, libelle, duree, points) in Types)
            {
                var type = Valeur(await typesControleur.Creer(Valide(new TypePrestationCreateDto
                {
                    Libelle = libelle,
                    DureeParDefautMinutes = duree
                })), $"le type « {libelle} »");

                for (var ordre = 0; ordre < points.Length; ordre++)
                {
                    Valeur(await pointsControleur.Creer(Valide(new PointControleCreateDto
                    {
                        TypePrestationId = type.Id,
                        Libelle = points[ordre],
                        Ordre = ordre + 1
                    })), $"le point « {points[ordre]} »");
                }

                types[cle] = type.Id;
            }

            return types;
        }

        private async Task<Dictionary<string, Guid>> CreerClientsAsync()
        {
            var controleur = Controleur<ClientsController>();
            var clients = new Dictionary<string, Guid>();

            foreach (var (cle, dto) in Clients)
            {
                clients[cle] = Valeur(await controleur.Creer(Valide(dto)), $"le client « {dto.Nom} »").Id;
            }

            return clients;
        }

        private async Task<Dictionary<string, Guid>> CreerAgentsAsync()
        {
            var controleur = Controleur<AgentsController>();
            var agents = new Dictionary<string, Guid>();

            foreach (var (cle, dto) in Agents)
            {
                agents[cle] = Valeur(await controleur.Creer(Valide(dto)), $"l'agent {dto.Prenom} {dto.Nom}").Id;
            }

            return agents;
        }

        private async Task CreerSitesEtPrestationsAsync(
            Dictionary<string, Guid> clients,
            Dictionary<string, Guid> types,
            Dictionary<string, Guid> agents,
            DateOnly aujourdhui)
        {
            var sites = Controleur<SitesController>();
            var contrats = Controleur<ContratsController>();
            var prestations = Controleur<PrestationsController>();

            foreach (var (client, site, frequence, lignes) in Sites)
            {
                site.ClientId = clients[client];
                var creeSite = Valeur(await sites.Creer(Valide(site)), $"le site « {site.Nom} »");

                // Un contrat en cours depuis un an : la génération ne lit que les contrats actifs
                // à la date de chaque vacation.
                var contrat = Valeur(await contrats.Creer(Valide(new ContratCreateDto
                {
                    SiteId = creeSite.Id,
                    DateDebut = Utc(aujourdhui.AddYears(-1)),
                    Frequence = frequence,
                    Actif = true
                })), $"le contrat de « {site.Nom} »");

                foreach (var (type, libelle, duree, jours, heure, titulaire) in lignes)
                {
                    Valeur(await prestations.Creer(Valide(new PrestationCreateDto
                    {
                        ContratId = contrat.Id,
                        Libelle = libelle,
                        DureeEstimeeMinutes = duree,
                        Jours = jours,
                        HeureDebut = heure,
                        AgentTitulaireId = titulaire is null ? null : agents[titulaire],
                        TypePrestationId = types[type]
                    })), $"la prestation « {libelle} »");
                }
            }
        }

        // ------------------------------------------------------------------ le planning

        /// <summary>
        /// Par <c>POST /api/planning/generer</c>, avec une horloge posée deux semaines en
        /// arrière : la route refuse une date passée, et c'est le jour qui a changé, pas la règle.
        /// Le titulaire n'est affecté que s'il est éligible, comme toujours (CLAUDE.md §8).
        /// </summary>
        private async Task GenererAsync(DateOnly aujourdhui)
        {
            var horloge = new HorlogeDecalee(
                _services.GetRequiredService<TimeProvider>(), TimeSpan.FromDays(-JoursHistorique));
            var generation = ActivatorUtilities.CreateInstance<ServiceGeneration>(_services, horloge);
            var planning = Controleur<PlanningController>(generation);

            Valeur(await planning.Generer(
                new GenerationDemandeDto { JusquAu = Utc(aujourdhui.AddDays(JoursAVenir)) }, _annulation),
                "la génération");
        }

        /// <summary>
        /// Deux absences à venir, posées <b>après</b> la génération : la détection d'impacts
        /// ouvre alors un remplacement par vacation touchée. Le premier reste en recherche ; le
        /// second est pourvu par le premier candidat du moteur, proposé puis accepté.
        /// </summary>
        private async Task PoserAbsencesAsync(Dictionary<string, Guid> agents, DateOnly aujourdhui)
        {
            var absences = Controleur<AbsencesController>();

            var maladie = ProchainJour(aujourdhui.AddDays(2), DayOfWeek.Tuesday, DayOfWeek.Thursday);
            Valeur(await absences.Creer(Valide(new AbsenceCreateDto
            {
                AgentId = agents[AgentEnArret],
                Type = nameof(TypeAbsence.Maladie),
                DateDebut = Utc(maladie),
                DateFin = Utc(maladie)
                // Aucun motif : un arrêt maladie qui en porte un est refusé (RGPD.md §8.2).
            })), "l'arrêt maladie");

            var conge = ProchainJour(aujourdhui.AddDays(3), DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday);
            var absence = Valeur(await absences.Creer(Valide(new AbsenceCreateDto
            {
                AgentId = agents[AgentEnConge],
                Type = nameof(TypeAbsence.Conge),
                DateDebut = Utc(conge),
                DateFin = Utc(conge),
                Motif = "Congé posé"
            })), "le congé");

            var remplacementId = await _db.Remplacements
                .Where(r => r.AbsenceId == absence.Id)
                .Select(r => (Guid?)r.Id)
                .FirstOrDefaultAsync(_annulation)
                ?? throw new InvalidOperationException(
                    "Le congé n'a ouvert aucun remplacement : la détection d'impacts n'a rien vu.");

            var remplacements = Controleur<RemplacementsController>();
            var candidat = Valeur(await remplacements.Candidats(remplacementId), "les candidats").FirstOrDefault()
                ?? throw new InvalidOperationException("Aucun candidat pour le remplacement du congé.");

            Valeur(await remplacements.Proposer(remplacementId, Valide(new ProposerDto { AgentId = candidat.AgentId })),
                "la proposition du remplaçant");
            Valeur(await remplacements.Accepter(remplacementId), "l'acceptation du remplaçant");
        }

        // ------------------------------------------------------------------ le compte rendu

        private async Task<RapportDemonstration> RapportAsync(DateOnly aujourdhui)
        {
            var nonPourvues = await _db.Interventions.CountAsync(
                i => i.AgentId == null && i.Statut == StatutIntervention.Planifiee, _annulation);

            return new RapportDemonstration(
                RaisonSociale,
                aujourdhui,
                await _db.Clients.CountAsync(_annulation),
                await _db.Sites.CountAsync(_annulation),
                await _db.Agents.CountAsync(_annulation),
                await _db.TypesPrestation.CountAsync(_annulation),
                await _db.Interventions.CountAsync(_annulation),
                nonPourvues,
                await _db.Remplacements.CountAsync(r => r.Statut == StatutRemplacement.Recherche, _annulation),
                await _db.Remplacements.CountAsync(r => r.Statut == StatutRemplacement.Accepte, _annulation));
        }

        // ------------------------------------------------------------------ l'appel d'un contrôleur

        /// <summary>
        /// Un contrôleur construit par l'injection, avec l'identité de l'installation. Les
        /// arguments remplacent une dépendance de même type — une génération à l'horloge
        /// décalée, par exemple.
        /// </summary>
        private T Controleur<T>(params object[] arguments) where T : ControllerBase
        {
            var controleur = ActivatorUtilities.CreateInstance<T>(_services, arguments);
            controleur.ControllerContext = new ControllerContext { HttpContext = _contexte };
            return controleur;
        }

        /// <summary>
        /// La valeur d'une réponse réussie. ⚠️ Un refus lève : c'est la dérive que ce jeu existe
        /// pour rendre visible, et la taire installerait un jeu à trous.
        /// </summary>
        private static T Valeur<T>(ActionResult<T> resultat, string quoi)
        {
            if (resultat.Value is not null) return resultat.Value;

            if (resultat.Result is ObjectResult { Value: T valeur } objet && (objet.StatusCode ?? 200) < 300)
            {
                return valeur;
            }

            throw new InvalidOperationException($"Échec sur {quoi} : {Decrire(resultat.Result)}");
        }

        private static void Succes(IActionResult resultat, string quoi)
        {
            var statut = resultat switch
            {
                ObjectResult objet => objet.StatusCode ?? 200,
                StatusCodeResult code => code.StatusCode,
                _ => 200
            };

            if (statut >= 300) throw new InvalidOperationException($"Échec sur {quoi} : {Decrire(resultat)}");
        }

        private static string Decrire(IActionResult? resultat) => resultat switch
        {
            ObjectResult objet => $"{objet.StatusCode} — {objet.Value}",
            StatusCodeResult code => code.StatusCode.ToString(),
            null => "aucune réponse",
            _ => resultat.GetType().Name
        };
    }

    /// <summary>
    /// La validation de la liaison : les attributs du DTO, avant le contrôleur. Sans elle,
    /// un libellé trop long passerait ici et serait refusé par l'API. ⚠️ Aucun contrôleur ne
    /// revérifie un format de courriel : retirée, une adresse illisible passait sans bruit.
    /// D'où le test qui l'éprouve directement, sur une donnée fautive.
    /// </summary>
    internal static T Valide<T>(T dto) where T : notnull
    {
        var erreurs = new List<ValidationResult>();

        if (!Validator.TryValidateObject(dto, new ValidationContext(dto), erreurs, validateAllProperties: true))
        {
            throw new InvalidOperationException(
                $"{typeof(T).Name} refusé par ses attributs : "
                + string.Join(" ; ", erreurs.Select(e => e.ErrorMessage)));
        }

        return dto;
    }

    private static DateTime Utc(DateOnly jour) =>
        DateTime.SpecifyKind(jour.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

    /// <summary>Le premier de ces jours de la semaine, à partir de <paramref name="depuis"/> inclus.</summary>
    private static DateOnly ProchainJour(DateOnly depuis, params DayOfWeek[] jours)
    {
        var jour = depuis;
        while (!jours.Contains(jour.DayOfWeek)) jour = jour.AddDays(1);
        return jour;
    }

    // ---------------------------------------------------------------------------
    // Les données. Séparées du code pour qu'elles se relisent comme un planning.
    //
    // ⚠️ Les personnes sont fictives, leurs adresses en `.example`, et aucune consigne ne nomme
    // quelqu'un (RGPD.md §9.5). Les coordonnées d'un agent sont celles d'un quartier, pas d'une
    // adresse : elles servent à la note de distance du moteur, et à rien d'autre.
    // ---------------------------------------------------------------------------

    private static readonly (string Cle, string Libelle, int Duree, string[] Points)[] Types =
    [
        ("Communes", "Entretien des parties communes", 120,
            ["Halls et paliers lavés", "Rampes et poignées désinfectées", "Ascenseur nettoyé", "Local poubelles rangé"]),
        ("Bureaux", "Entretien de bureaux", 90,
            ["Corbeilles vidées", "Postes de travail dépoussiérés", "Sanitaires nettoyés et réapprovisionnés", "Sols aspirés"]),
        ("Vitrerie", "Vitrerie", 60,
            ["Vitres intérieures", "Vitres extérieures accessibles", "Encadrements essuyés"]),
    ];

    // ⚠️ Des propriétés, pas des champs : chaque installation reçoit des DTO neufs. Le jeu pose
    // le `ClientId` d'un site ; partagés, ils garderaient celui d'une installation précédente.
    private static (string Cle, ClientCreateDto Dto)[] Clients =>
    [
        ("Bellevue", new ClientCreateDto
        {
            Nom = "Bellevue Gestion", Type = nameof(TypeClient.Syndic),
            Email = "contact@bellevue-gestion.example", Telephone = "01 45 67 89 10",
            Adresse = "18 rue de la Convention", CodePostal = "75015", Ville = "Paris"
        }),
        ("Atelier", new ClientCreateDto
        {
            Nom = "Atelier Numérique", Type = nameof(TypeClient.EntrepriseCliente),
            Email = "accueil@atelier-numerique.example", Telephone = "01 43 55 20 30",
            Adresse = "9 rue Oberkampf", CodePostal = "75011", Ville = "Paris"
        }),
    ];

    /// <summary>Celui dont l'arrêt maladie laisse un remplacement en recherche.</summary>
    private const string AgentEnArret = "Moreau";

    /// <summary>Celui dont le congé est pourvu par le premier candidat du moteur.</summary>
    private const string AgentEnConge = "Costa";

    private static (string Cle, AgentCreateDto Dto)[] Agents =>
    [
        ("Diallo", Agent("Diallo", "Aminata", "06 12 34 56 78", new DateOnly(2023, 3, 15), 48.8566, 2.3522)),
        ("Nguyen", Agent("Nguyen", "Linh", "06 23 45 67 89", new DateOnly(2024, 9, 2), 48.8760, 2.3590)),
        ("Costa", Agent("Costa", "Rui", "06 34 56 78 90", new DateOnly(2022, 1, 10), 48.8156, 2.3600)),
        ("Moreau", Agent("Moreau", "Élodie", "06 45 67 89 01", new DateOnly(2025, 6, 1), 48.9360, 2.3550)),
        ("Bernard", Agent("Bernard", "Karim", "06 56 78 90 12", new DateOnly(2021, 11, 8), 48.8480, 2.4000)),
        ("Lefevre", Agent("Lefèvre", "Inès", "06 67 89 01 23", new DateOnly(2025, 2, 17), 48.8300, 2.3300)),
    ];

    private static AgentCreateDto Agent(
        string nom, string prenom, string telephone, DateOnly embauche, double latitude, double longitude) => new()
    {
        Nom = nom,
        Prenom = prenom,
        Email = $"{prenom[0]}.{nom}@demo-proprete.example".ToLowerInvariant().Replace("è", "e").Replace("é", "e"),
        Telephone = telephone,
        DateEmbauche = Utc(embauche),
        Latitude = latitude,
        Longitude = longitude,
        Actif = true
    };

    private static readonly string[] Semaine = ["Lundi", "Mardi", "Mercredi", "Jeudi", "Vendredi"];
    private static readonly string[] TousLesJours = [.. Semaine, "Samedi", "Dimanche"];

    /// <summary>
    /// (client, site, fréquence du contrat, prestations : type, libellé, durée, jours, heure,
    /// titulaire). Les horaires sont vérifiés contre le repos de onze heures entre journées :
    /// le plus serré est Nguyen, de 06:30 à 08:00 chaque jour, soit vingt-deux heures et demie.
    ///
    /// ⚠️ <i>Bureaux République</i> est passé <b>tous les jours</b>, par l'agent dont le compte
    /// ouvre <i>Ma journée</i> : elle n'est jamais vide, même un dimanche. Et la vitrerie du
    /// samedi à <i>Plateau Bastille</i> n'a pas de titulaire : une vacation non pourvue chaque
    /// semaine, que le planning signale.
    /// </summary>
    private static (string Client, SiteCreateDto Site, string Frequence,
        (string Type, string Libelle, int Duree, string[] Jours, TimeSpan Heure, string? Titulaire)[] Prestations)[] Sites =>
    [
        ("Bellevue",
            new SiteCreateDto
            {
                Nom = "Résidence Bellevue", Adresse = "12 rue de Vaugirard", CodePostal = "75015", Ville = "Paris",
                Latitude = 48.8421, Longitude = 2.3219,
                Consignes = "Badge à récupérer à la loge. Local poubelles au sous-sol."
            },
            "Du lundi au vendredi, le matin",
            [("Communes", "Parties communes", 120, Semaine, new TimeSpan(8, 0, 0), "Diallo")]),

        ("Bellevue",
            new SiteCreateDto
            {
                Nom = "Immeuble Haussmann", Adresse = "40 boulevard Haussmann", CodePostal = "75009", Ville = "Paris",
                Latitude = 48.8735, Longitude = 2.3320,
                Consignes = "Accès par la cour. Monte-charge réservé de 8 h à 10 h."
            },
            "Lundi, mercredi, vendredi",
            [("Communes", "Parties communes et monte-charge", 120, ["Lundi", "Mercredi", "Vendredi"], new TimeSpan(10, 30, 0), "Costa")]),

        ("Bellevue",
            new SiteCreateDto
            {
                Nom = "Copropriété Daumesnil", Adresse = "210 avenue Daumesnil", CodePostal = "75012", Ville = "Paris",
                Latitude = 48.8402, Longitude = 2.3950,
                Consignes = "Deux halls, A et B. Vitrerie extérieure au rez-de-chaussée."
            },
            "Mardi et jeudi",
            [("Vitrerie", "Vitrerie des halls", 60, ["Mardi", "Jeudi"], new TimeSpan(14, 0, 0), "Moreau")]),

        ("Atelier",
            new SiteCreateDto
            {
                Nom = "Bureaux République", Adresse = "9 rue Oberkampf", CodePostal = "75011", Ville = "Paris",
                Latitude = 48.8645, Longitude = 2.3720,
                Consignes = "Code d'entrée au secrétariat. Alarme à désactiver en arrivant : consignes dans la boîte à clés."
            },
            "Tous les jours, avant l'ouverture",
            [("Bureaux", "Bureaux", 90, TousLesJours, new TimeSpan(6, 30, 0), "Nguyen")]),

        ("Atelier",
            new SiteCreateDto
            {
                Nom = "Plateau Bastille", Adresse = "3 rue de la Roquette", CodePostal = "75011", Ville = "Paris",
                Latitude = 48.8540, Longitude = 2.3720,
                Consignes = "Open space au deuxième étage. Ne pas déplacer les documents laissés sur les bureaux."
            },
            "Du lundi au vendredi le soir, vitrerie le samedi",
            [
                ("Bureaux", "Bureaux du soir", 90, Semaine, new TimeSpan(18, 30, 0), "Bernard"),
                ("Vitrerie", "Vitrerie", 60, ["Samedi"], new TimeSpan(9, 0, 0), null),
            ]),
    ];
}
