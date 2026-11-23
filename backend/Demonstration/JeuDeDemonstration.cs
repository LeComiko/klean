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
    int RemplacementsPourvus,
    int Terminees,
    int NonCertifiees,
    int AnomaliesOuvertes,
    int AnomaliesTraitees,
    int Produits,
    int ProduitsSousSeuil,
    IReadOnlyList<CompteDemonstration> Comptes);

/// <summary>
/// Un compte créé par le jeu, avec le mot de passe que le serveur a tiré. ⚠️ Il n'existe en
/// clair que dans ce compte rendu, comme dans la réponse de <c>POST /api/auth/comptes</c>.
/// </summary>
public sealed record CompteDemonstration(string Role, string Nom, string Email, string MotDePasseProvisoire);

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
    internal static ClaimsPrincipal Identite(Guid entrepriseId, Guid utilisateurId, string role, Guid? agentId = null)
    {
        List<Claim> revendications =
        [
            new(ClaimTypes.NameIdentifier, utilisateurId.ToString()),
            new(ClaimTypes.Role, role),
            new(JwtService.RevendicationEntrepriseId, entrepriseId.ToString())
        ];

        if (agentId is not null) revendications.Add(new(JwtService.RevendicationAgentId, agentId.Value.ToString()));

        return new ClaimsPrincipal(new ClaimsIdentity(revendications, authenticationType: "Demonstration"));
    }

    /// <summary>
    /// L'entreprise unique de l'instance, et son premier administrateur actif. Lus au
    /// propriétaire, hors de tout tenant : c'est l'amorçage ou l'assistant d'installation qui les a posés.
    /// </summary>
    private static async Task<(Guid Entreprise, Guid Administrateur)> CibleAsync(
        IServiceProvider racine, CancellationToken annulation)
    {
        using var portee = racine.CreateScope();
        portee.ServiceProvider.GetRequiredService<ITenantContext>().DesactiverFiltrage();

        var db = portee.ServiceProvider.GetRequiredService<KleanDbContext>();
        var entreprises = await db.Entreprises.Select(e => e.Id).ToListAsync(annulation);

        if (entreprises.Count == 0)
        {
            // Depuis le lot A2 de l'étape 34, une instance démarre sans entreprise quand le code
            // d'installation est configuré : elle attend l'assistant.
            throw new RefusDemonstration(
                "L'instance ne compte aucune entreprise. Créez d'abord l'entreprise : l'assistant "
                + "d'installation dans le navigateur, ou les variables d'amorçage.");
        }

        if (entreprises.Count != 1)
        {
            throw new RefusDemonstration(
                $"L'instance compte {entreprises.Count} entreprises. Le jeu de démonstration "
                + "s'installe dans l'entreprise unique d'une instance neuve.");
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
        sortie.WriteLine($"  {r.Terminees} passages certifiés, {r.NonCertifiees} sans preuve ; "
            + $"{r.AnomaliesOuvertes} anomalies ouvertes, {r.AnomaliesTraitees} traitée.");
        sortie.WriteLine($"  {r.Produits} produits en stock, dont {r.ProduitsSousSeuil} sous son seuil d'alerte.");
        sortie.WriteLine();
        sortie.WriteLine("  Comptes créés. Chaque mot de passe ne s'affiche qu'ici : le transmettre, puis l'oublier.");
        sortie.WriteLine("  La première connexion le fait remplacer, et le responsable équipera un téléphone.");
        sortie.WriteLine();

        foreach (var compte in r.Comptes)
        {
            sortie.WriteLine($"    {compte.Role,-14} {compte.Email,-38} {compte.MotDePasseProvisoire}");
        }
    }

    /// <summary>Une installation : une portée, une identité, une suite de gestes.</summary>
    private sealed class Installation
    {
        private readonly IServiceProvider _services;
        private readonly HttpContext _contexte;
        private readonly Guid _entrepriseId;
        private readonly CancellationToken _annulation;
        private readonly KleanDbContext _db;

        /// <summary>Les points de contrôle de chaque type, dans l'ordre de la checklist.</summary>
        private readonly Dictionary<string, List<Guid>> _points = [];

        /// <summary>Les sites par leur nom, pour les mouvements de stock qui les visent.</summary>
        private readonly Dictionary<string, Guid> _sites = [];

        /// <summary>Le dépôt et le véhicule, par leur clé.</summary>
        private readonly Dictionary<string, Guid> _emplacements = [];

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
            var comptes = await CreerComptesAsync(agents, clients);
            await CreerStockAsync(types);
            await PointerPassagesAsync(agents[AgentAuCompte], comptes.AgentUtilisateurId, aujourdhui);
            await ConstaterSansPreuveAsync(aujourdhui);

            return await RapportAsync(aujourdhui, comptes.Crees);
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

                _points[cle] = [];

                for (var ordre = 0; ordre < points.Length; ordre++)
                {
                    _points[cle].Add(Valeur(await pointsControleur.Creer(Valide(new PointControleCreateDto
                    {
                        TypePrestationId = type.Id,
                        Libelle = points[ordre],
                        Ordre = ordre + 1
                    })), $"le point « {points[ordre]} »").Id);
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
                _sites[site.Nom] = creeSite.Id;

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

        // ------------------------------------------------------------------ les comptes

        /// <summary>
        /// Un compte par rôle, par <c>POST /api/auth/comptes</c> : le serveur tire le mot de passe,
        /// le marque provisoire, et le rend une fois. Puis l'équipe du chef, par le <c>PUT</c> de
        /// chaque fiche, qui vérifie le rattachement — même entreprise, rôle <c>ChefEquipe</c>.
        /// </summary>
        private async Task<(List<CompteDemonstration> Crees, Guid AgentUtilisateurId)> CreerComptesAsync(
            Dictionary<string, Guid> agents, Dictionary<string, Guid> clients)
        {
            var auth = Controleur<AuthController>();
            var crees = new List<CompteDemonstration>();
            var agentUtilisateur = Guid.Empty;
            var chef = Guid.Empty;

            foreach (var (role, nom, prenom, email) in Comptes)
            {
                var cree = Valeur(await auth.CreerCompte(Valide(new CompteCreateDto
                {
                    Email = email,
                    Nom = nom,
                    Prenom = prenom,
                    Role = role,
                    AgentId = role == RolesKlean.Agent ? agents[AgentAuCompte] : null,
                    ClientId = role == RolesKlean.ClientSyndic ? clients[ClientDuSyndic] : null
                })), $"le compte {email}");

                crees.Add(new CompteDemonstration(role, $"{prenom} {nom}", email, cree.MotDePasseProvisoire));

                if (role == RolesKlean.Agent) agentUtilisateur = cree.Compte.Id;
                if (role == RolesKlean.ChefEquipe) chef = cree.Compte.Id;
            }

            var fiches = Controleur<AgentsController>();

            foreach (var (cle, dto) in Agents.Where(a => EquipeDuChef.Contains(a.Cle)))
            {
                dto.ChefEquipeId = chef;
                Succes(await fiches.Modifier(agents[cle], Valide(dto)), $"le rattachement de {dto.Prenom} {dto.Nom}");
            }

            return (crees, agentUtilisateur);
        }

        // ------------------------------------------------------------------ le stock

        /// <summary>
        /// Produits, lieux, consommations par type, puis des mouvements par le service du stock,
        /// qui refuse une sortie au-delà du disponible. ⚠️ Datés du jour de l'installation :
        /// l'horloge du stock n'est pas substituable (SPEC.md, étape 28).
        /// </summary>
        private async Task CreerStockAsync(Dictionary<string, Guid> types)
        {
            var produits = new Dictionary<string, Guid>();
            var produitsControleur = Controleur<ProduitsController>();

            foreach (var (cle, dto) in Produits)
            {
                produits[cle] = Valeur(await produitsControleur.Creer(Valide(dto)), $"le produit « {dto.Nom} »").Id;
            }

            var emplacements = Controleur<EmplacementsController>();

            foreach (var (cle, nom, type) in Emplacements)
            {
                _emplacements[cle] = Valeur(await emplacements.Creer(Valide(new EmplacementCreateDto
                {
                    Nom = nom,
                    Type = type
                })), $"l'emplacement « {nom} »").Id;
            }

            var consommations = Controleur<ConsommationsTypeController>();

            foreach (var (type, produit, quantite) in Consommations)
            {
                Valeur(await consommations.Creer(Valide(new ConsommationTypeCreateDto
                {
                    TypePrestationId = types[type],
                    ProduitId = produits[produit],
                    QuantiteParPassage = quantite
                })), $"la consommation de {produit} par {type}");
            }

            var mouvements = Controleur<MouvementsStockController>();

            foreach (var (nature, produit, origine, destination, quantite) in Mouvements)
            {
                var dto = new MouvementStockCreateDto { ProduitId = produits[produit], Nature = nature, Quantite = quantite };

                if (origine is not null)
                {
                    if (_emplacements.TryGetValue(origine, out var id)) dto.OrigineEmplacementId = id;
                    else dto.OrigineSiteId = _sites[origine];
                }

                if (destination is not null)
                {
                    if (_emplacements.TryGetValue(destination, out var id)) dto.DestinationEmplacementId = id;
                    else dto.DestinationSiteId = _sites[destination];
                }

                Valeur(await mouvements.Enregistrer(Valide(dto), _annulation), $"le mouvement {nature} de {produit}");
            }
        }

        // ------------------------------------------------------------------ les passages

        private enum Preuve { SurSite, SansPosition, HorsRayon }

        /// <summary>
        /// Les passages des jours passés de l'agent qui a un compte, par <c>demarrer</c> et
        /// <c>terminer</c>, sous son identité, avec une horloge posée à l'heure du geste. Le
        /// serveur tranche la preuve, la checklist et le statut ; le jeu ne donne qu'une position.
        ///
        /// ⚠️ <b>Seul cet agent pointe</b>, parce que seul l'agent affecté le peut, par son compte.
        /// Inventer une identité aux autres fabriquerait des auteurs qui n'existent pas : leurs
        /// vacations passées sont constatées sans preuve, par <see cref="ConstaterSansPreuveAsync"/>.
        ///
        /// ⚠️ Une position près du site, jamais celle d'un domicile : elle n'est d'ailleurs pas
        /// gardée, le serveur n'en retient que la distance (CLAUDE.md §9 ter).
        /// </summary>
        private async Task PointerPassagesAsync(Guid agentId, Guid utilisateurId, DateOnly aujourdhui)
        {
            var avant = Utc(aujourdhui);
            var vacations = await _db.Interventions.AsNoTracking()
                .Where(i => i.AgentId == agentId && i.DatePrevue < avant && i.Statut == StatutIntervention.Planifiee)
                .OrderBy(i => i.DatePrevue)
                .Select(i => new { i.Id, i.DatePrevue, i.HeurePrevue })
                .ToListAsync(_annulation);

            var site = Sites.Single(s => s.Site.Nom == SitePointe).Site;
            var fuseau = Territoires.Fuseau(Territoire.Metropole);
            var agent = Identite(_entrepriseId, utilisateurId, RolesKlean.Agent, agentId);

            for (var rang = 0; rang < vacations.Count; rang++)
            {
                var vacation = vacations[rang];
                var jour = DateOnly.FromDateTime(vacation.DatePrevue);
                var debut = Instant(jour, vacation.HeurePrevue + TimeSpan.FromMinutes(2), fuseau);
                var fin = Instant(jour, vacation.HeurePrevue + TimeSpan.FromMinutes(86), fuseau);
                var preuve = rang == RangSansPosition ? Preuve.SansPosition
                    : rang == RangHorsRayon ? Preuve.HorsRayon
                    : Preuve.SurSite;
                var aTraiter = new List<Guid>();

                using (Comme(agent))
                {
                    var demarrage = Controleur<InterventionsController>(ServicePassageA(debut));
                    Valeur(await demarrage.Demarrer(vacation.Id, new PointageCreateDto
                    {
                        Position = preuve == Preuve.SansPosition ? null : Pres(site)
                    }, _annulation), $"le début du passage du {jour:dd/MM}");

                    foreach (var (_, description, traitee) in AnomaliesSignalees.Where(a => a.Rang == rang))
                    {
                        var anomalies = Controleur<AnomaliesController>(HorlogeA(debut.AddMinutes(25)));
                        var signalee = Valeur(await anomalies.Signaler(Valide(new AnomalieCreateDto
                        {
                            InterventionId = vacation.Id,
                            Description = description
                        })), $"l'anomalie du {jour:dd/MM}");

                        if (traitee) aTraiter.Add(signalee.Id);
                    }

                    var points = rang == RangPointOublie ? _points[TypePointe].SkipLast(1).ToList() : _points[TypePointe];
                    var terminaison = Controleur<InterventionsController>(ServicePassageA(fin));
                    Valeur(await terminaison.Terminer(vacation.Id, new TerminaisonDto
                    {
                        Position = preuve == Preuve.HorsRayon ? Loin(site) : Pres(site),
                        PointsFaits = points
                    }, _annulation), $"la fin du passage du {jour:dd/MM}");
                }

                // Traitée le lendemain, par l'administrateur : c'est un rôle interne qui traite.
                foreach (var id in aTraiter)
                {
                    var traitement = Controleur<AnomaliesController>(HorlogeA(fin.AddDays(1).AddHours(2)));
                    Valeur(await traitement.Traiter(id), "le traitement d'une anomalie");
                }
            }
        }

        /// <summary>
        /// Les vacations passées des agents sans compte : « fait, sans preuve », le recours que
        /// l'API laisse à un chef dont l'agent n'a pas de téléphone (CLAUDE.md §9 ter), par le
        /// <c>PUT</c>, qui revérifie l'éligibilité. Une vacation passée sans agent reste ce
        /// qu'elle est : jamais pourvue.
        /// </summary>
        private async Task ConstaterSansPreuveAsync(DateOnly aujourdhui)
        {
            var avant = Utc(aujourdhui);
            var passees = await _db.Interventions.AsNoTracking()
                .Where(i => i.DatePrevue < avant && i.Statut == StatutIntervention.Planifiee && i.AgentId != null)
                .Select(i => new { i.Id, i.PrestationId, i.AgentId, i.DatePrevue, i.HeurePrevue })
                .ToListAsync(_annulation);

            var controleur = Controleur<InterventionsController>();

            foreach (var vacation in passees)
            {
                Succes(await controleur.Modifier(vacation.Id, Valide(new InterventionCreateDto
                {
                    PrestationId = vacation.PrestationId,
                    AgentId = vacation.AgentId,
                    DatePrevue = vacation.DatePrevue,
                    HeurePrevue = vacation.HeurePrevue,
                    Statut = nameof(StatutIntervention.NonCertifiee)
                })), $"la vacation du {vacation.DatePrevue:dd/MM}");
            }
        }

        private ServicePassage ServicePassageA(DateTimeOffset instant) =>
            ActivatorUtilities.CreateInstance<ServicePassage>(_services, HorlogeA(instant));

        // ------------------------------------------------------------------ le compte rendu

        private async Task<RapportDemonstration> RapportAsync(DateOnly aujourdhui, IReadOnlyList<CompteDemonstration> comptes)
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
                await _db.Remplacements.CountAsync(r => r.Statut == StatutRemplacement.Accepte, _annulation),
                await _db.Interventions.CountAsync(i => i.Statut == StatutIntervention.Terminee, _annulation),
                await _db.Interventions.CountAsync(i => i.Statut == StatutIntervention.NonCertifiee, _annulation),
                await _db.Anomalies.CountAsync(a => a.Statut == StatutAnomalie.Ouverte, _annulation),
                await _db.Anomalies.CountAsync(a => a.Statut == StatutAnomalie.Traitee, _annulation),
                await _db.Produits.CountAsync(_annulation),
                // Tous lieux confondus, comme le seuil se lit (CLAUDE.md §9 bis).
                await _db.Produits.CountAsync(
                    p => _db.Stocks.Where(s => s.ProduitId == p.Id).Sum(s => (decimal?)s.Quantite) < p.SeuilAlerte,
                    _annulation),
                comptes);
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
        /// Des gestes sous une autre identité — celle de l'agent qui pointe —, jusqu'à la fin du
        /// <c>using</c>. Le tenant relit <c>HttpContext.User</c> à chaque requête : c'est lui qui
        /// change, sur le même <c>HttpContext</c>, que l'accesseur garde d'un bout à l'autre.
        ///
        /// ⚠️ <b>Jamais en remplaçant le <c>HttpContext</c> de l'accesseur.</b> Il le range dans
        /// un <c>AsyncLocal</c>, et <b>vide</b> le porteur du précédent avant d'en poser un autre.
        /// Remplacé dans une méthode <c>async</c>, le nouveau ne survit pas au retour, et l'ancien
        /// a été vidé en passant : l'appelant se retrouve sans identité, donc sans entreprise, et
        /// <c>klean_app</c> ne voit plus rien. Le symptôme a d'abord été une mise à jour qui ne
        /// touchait aucune ligne, puis un compte rendu à zéro, un étage plus haut.
        /// </summary>
        private Bascule Comme(ClaimsPrincipal identite)
        {
            var avant = _contexte.User;
            _contexte.User = identite;
            return new Bascule(_contexte, avant);
        }

        private sealed class Bascule(HttpContext contexte, ClaimsPrincipal retour) : IDisposable
        {
            public void Dispose() => contexte.User = retour;
        }

        /// <summary>Une horloge posée à cet instant, qui avance avec la vraie.</summary>
        private HorlogeDecalee HorlogeA(DateTimeOffset instant)
        {
            var reelle = _services.GetRequiredService<TimeProvider>();
            return new HorlogeDecalee(reelle, instant - reelle.GetUtcNow());
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

    /// <summary>Une heure locale du territoire, en instant.</summary>
    private static DateTimeOffset Instant(DateOnly jour, TimeSpan heure, TimeZoneInfo fuseau) =>
        new(TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(jour.ToDateTime(TimeOnly.FromTimeSpan(heure)), DateTimeKind.Unspecified), fuseau));

    /// <summary>Une trentaine de mètres du site, à une précision de téléphone en ville.</summary>
    private static PositionDto Pres(SiteCreateDto site) =>
        new() { Latitude = site.Latitude + 0.0003, Longitude = site.Longitude, PrecisionMetres = 12 };

    /// <summary>Six à sept cents mètres : hors du rayon de deux cents, même en accordant la marge.</summary>
    private static PositionDto Loin(SiteCreateDto site) =>
        new() { Latitude = site.Latitude + 0.006, Longitude = site.Longitude, PrecisionMetres = 15 };

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

    // ---------------------------------------------------------------------------
    // Lot B : comptes, stock, passages.
    // ---------------------------------------------------------------------------

    /// <summary>L'agent dont le compte ouvre <i>Ma journée</i>, passé tous les jours à République.</summary>
    private const string AgentAuCompte = "Nguyen";

    private const string SitePointe = "Bureaux République";
    private const string TypePointe = "Bureaux";
    private const string ClientDuSyndic = "Bellevue";

    /// <summary>L'équipe du chef : la vue d'un chef d'équipe n'en montre pas d'autre (CLAUDE.md §7).</summary>
    private static readonly string[] EquipeDuChef = ["Nguyen", "Bernard", "Lefevre"];

    /// <summary>Rang, dans les passages passés de l'agent, de ceux qui ne sont pas certifiés.</summary>
    private const int RangSansPosition = 3;
    private const int RangHorsRayon = 8;

    /// <summary>Le passage dont un point de checklist reste décoché : un compte rendu, pas une condition.</summary>
    private const int RangPointOublie = 6;

    /// <summary>(rang du passage, description, traitée le lendemain). Aucune ne nomme personne.</summary>
    private static readonly (int Rang, string Description, bool Traitee)[] AnomaliesSignalees =
    [
        (5, "Fuite sous l'évier de la kitchenette, flaque au sol.", true),
        (10, "Néon grillé dans le couloir du premier étage.", false),
        (11, "Distributeur de savon cassé dans les sanitaires.", false),
    ];

    /// <summary>(rôle, nom, prénom, courriel). Le compte de l'agent reprend le courriel de sa fiche.</summary>
    private static readonly (string Role, string Nom, string Prenom, string Email)[] Comptes =
    [
        (RolesKlean.Responsable, "Laurent", "Sophie", "s.laurent@demo-proprete.example"),
        (RolesKlean.ChefEquipe, "Petit", "Marc", "m.petit@demo-proprete.example"),
        (RolesKlean.Agent, "Nguyen", "Linh", "l.nguyen@demo-proprete.example"),
        (RolesKlean.ClientSyndic, "Garnier", "Hélène", "h.garnier@bellevue-gestion.example"),
    ];

    private static (string Cle, ProduitCreateDto Dto)[] Produits =>
    [
        ("Detergent", new ProduitCreateDto { Nom = "Détergent sols", Reference = "DS-5L", Unite = "L", SeuilAlerte = 10 }),
        ("Desinfectant", new ProduitCreateDto { Nom = "Désinfectant surfaces", Reference = "DF-1L", Unite = "L", SeuilAlerte = 5 }),
        ("Vitres", new ProduitCreateDto { Nom = "Nettoyant vitres", Reference = "NV-750", Unite = "L", SeuilAlerte = 4 }),
        ("Sacs", new ProduitCreateDto { Nom = "Sacs poubelle 100 L", Unite = "rouleau", SeuilAlerte = 20 }),
        ("Papier", new ProduitCreateDto { Nom = "Papier toilette", Unite = "colis", SeuilAlerte = 6 }),
    ];

    /// <summary>Un véhicule se nomme par sa plaque, jamais par son conducteur (CLAUDE.md §7 bis).</summary>
    private static readonly (string Cle, string Nom, string Type)[] Emplacements =
    [
        ("Depot", "Dépôt central", nameof(TypeEmplacement.Depot)),
        ("Vehicule", "Utilitaire AB-123-CD", nameof(TypeEmplacement.Vehicule)),
    ];

    private static readonly (string Type, string Produit, decimal Quantite)[] Consommations =
    [
        ("Communes", "Detergent", 0.5m),
        ("Communes", "Desinfectant", 0.2m),
        ("Communes", "Sacs", 1m),
        ("Bureaux", "Desinfectant", 0.3m),
        ("Bureaux", "Sacs", 0.5m),
        ("Bureaux", "Papier", 0.25m),
        ("Vitrerie", "Vitres", 0.4m),
    ];

    /// <summary>
    /// (nature, produit, origine, destination, quantité) ; un lieu est la clé d'un emplacement ou
    /// le nom d'un site. ⚠️ Le nettoyant vitres entre à 3 L pour un seuil de 4 : le produit sous
    /// son seuil que l'écran du stock doit signaler. L'inventaire du détergent laisse un écart.
    /// </summary>
    private static readonly (string Nature, string Produit, string? Origine, string? Destination, decimal Quantite)[] Mouvements =
    [
        (nameof(NatureMouvement.Entree), "Detergent", null, "Depot", 40m),
        (nameof(NatureMouvement.Entree), "Desinfectant", null, "Depot", 20m),
        (nameof(NatureMouvement.Entree), "Vitres", null, "Depot", 3m),
        (nameof(NatureMouvement.Entree), "Sacs", null, "Depot", 100m),
        (nameof(NatureMouvement.Entree), "Papier", null, "Depot", 30m),
        (nameof(NatureMouvement.Transfert), "Desinfectant", "Depot", "Vehicule", 6m),
        (nameof(NatureMouvement.Transfert), "Sacs", "Depot", "Vehicule", 20m),
        (nameof(NatureMouvement.Transfert), "Papier", "Depot", "Bureaux République", 8m),
        (nameof(NatureMouvement.Transfert), "Sacs", "Depot", "Bureaux République", 10m),
        (nameof(NatureMouvement.Sortie), "Papier", "Bureaux République", null, 2m),
        (nameof(NatureMouvement.Inventaire), "Detergent", null, "Depot", 38m),
    ];
}
