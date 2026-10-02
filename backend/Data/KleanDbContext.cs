using Klean.Api.Models;
using Klean.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Data;

public class KleanDbContext : IdentityDbContext<Utilisateur, IdentityRole<Guid>, Guid>
{
    private readonly ITenantContext? _tenant;

    public KleanDbContext(DbContextOptions<KleanDbContext> options, ITenantContext tenant)
        : base(options) => _tenant = tenant;

    /// <summary>
    /// Sans tenant : le filtrage est inactif. Réservé à la migration de la base
    /// de test, qui doit créer les tables avant que l'application ne démarre.
    /// Volontairement <c>internal</c> — un contexte construit hors injection
    /// contourne l'isolation, ce n'est pas un chemin à ouvrir.
    /// </summary>
    internal KleanDbContext(DbContextOptions<KleanDbContext> options) : base(options)
    {
    }

    // Le filtre global doit traverser des propriétés d'INSTANCE du DbContext.
    // OnModelCreating ne s'exécute qu'une fois par type de contexte, puis le
    // modèle est mis en cache : un filtre qui capturerait une variable locale
    // figerait l'instance du premier contexte créé — celui de l'amorçage, sur
    // lequel le filtrage est désactivé — et laisserait tout passer en silence
    // (CLAUDE.md §10).
    private bool TenantFiltrageActif => _tenant?.FiltrageActif ?? false;
    private Guid? TenantEntrepriseId => _tenant?.EntrepriseId;

    // Deuxième et troisième axes d'isolation : nuls pour un compte interne,
    // auquel cas aucune restriction supplémentaire ne s'applique.
    private Guid? TenantClientId => _tenant?.ClientId;
    private Guid? TenantAgentId => _tenant?.AgentId;

    public DbSet<Entreprise> Entreprises => Set<Entreprise>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Contrat> Contrats => Set<Contrat>();
    public DbSet<Prestation> Prestations => Set<Prestation>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<Intervention> Interventions => Set<Intervention>();
    public DbSet<Absence> Absences => Set<Absence>();
    public DbSet<Remplacement> Remplacements => Set<Remplacement>();
    public DbSet<Produit> Produits => Set<Produit>();
    public DbSet<Stock> Stocks => Set<Stock>();

    /// <summary>
    /// Journal d'accès aux données personnelles (SPEC.md étape 17). Douzième
    /// entité, et la seule qui n'existe pas pour le métier : elle existe pour
    /// rendre compte de l'usage des onze autres.
    /// </summary>
    public DbSet<AccesJournalise> AccesJournalises => Set<AccesJournalise>();

    /// <summary>
    /// Sessions révocables. Volontairement hors du filtrage multi-tenant : le
    /// rafraîchissement s'exécute sans requête authentifiée. Voir le commentaire
    /// de l'entité.
    /// </summary>
    public DbSet<JetonRafraichissement> JetonsRafraichissement => Set<JetonRafraichissement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Les clés des entités métier sont générées côté application (CLAUDE.md §5).
        // La boucle exclut délibérément les entités d'Identity, Utilisateur compris :
        // ni UserManager ni RoleManager ne posent l'Id, c'est le générateur d'EF qui
        // le fait. Les basculer en Never leur donnerait toutes Guid.Empty, donc une
        // collision de clé primaire au deuxième enregistrement créé.
        foreach (var type in modelBuilder.Model.GetEntityTypes())
        {
            if (type.ClrType.Namespace != "Klean.Api.Models") continue;
            if (type.ClrType == typeof(Utilisateur)) continue;

            var cle = type.FindPrimaryKey()?.Properties;
            if (cle is { Count: 1 } && cle[0].ClrType == typeof(Guid))
            {
                cle[0].ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
            }
        }

        // Les cinq énumérations sont stockées en texte (CLAUDE.md §5).
        // Pas de HasMaxLength ici : ce serait varchar(n), on veut text.
        modelBuilder.Entity<Client>().Property(e => e.Type).HasConversion<string>();
        modelBuilder.Entity<Intervention>().Property(e => e.Statut).HasConversion<string>();
        modelBuilder.Entity<Absence>().Property(e => e.Type).HasConversion<string>();
        modelBuilder.Entity<Remplacement>().Property(e => e.Statut).HasConversion<string>();
        modelBuilder.Entity<Stock>().Property(e => e.TypeEmplacement).HasConversion<string>();
        modelBuilder.Entity<AccesJournalise>().Property(e => e.Ressource).HasConversion<string>();
        modelBuilder.Entity<AccesJournalise>().Property(e => e.Nature).HasConversion<string>();

        modelBuilder.Entity<Produit>().Property(e => e.SeuilAlerte).HasPrecision(10, 2);
        modelBuilder.Entity<Stock>().Property(e => e.Quantite).HasPrecision(10, 2);

        modelBuilder.Entity<Entreprise>().HasIndex(e => e.Siret).IsUnique();

        // ---- Comportements de suppression ----
        //
        // Une seule règle, et elle se lit sans exception à retenir :
        //
        //   **Aucune suppression n'efface en silence une ligne que l'utilisateur
        //   gère sur son propre écran.**
        //
        // ⚠️ La version précédente cascadait toute la chaîne du modèle. Mesuré
        // contre le serveur avant correction : un `DELETE /api/clients/{id}` sur un
        // client portant un site a effacé le client, le site, son contrat, sa
        // prestation **et** son intervention, en un appel, avec un 204 et sans
        // avertissement. Tant que ce chemin n'existait que dans Swagger, le risque
        // était théorique ; l'étape 16 bis en a fait un bouton dans une liste.
        //
        // Ce que `Restrict` coûte : il faut vider de bas en haut. C'est plus
        // pénible, et c'est le but — la friction est ici la fonctionnalité. Le
        // serveur répond 409 avec « Suppression refusée par la base : des données
        // rattachées l'empêchent », que l'écran affiche tel quel.

        // Rattachement au tenant : **Cascade**, et c'est délibéré. Supprimer une
        // entreprise est du provisionnement d'instance, pas de la gestion de
        // tenant — `POST` et `DELETE /api/entreprises` ont été retirés à l'étape 8
        // précisément pour cela. Aucun endpoint n'expose donc ce chemin, et le jour
        // où un script de désinscription existera, c'est exactement la sémantique
        // qu'il voudra. Y mettre `Restrict` protégerait une porte qui n'existe pas.
        modelBuilder.Entity<Client>()
            .HasOne(e => e.Entreprise).WithMany(e => e.Clients)
            .HasForeignKey(e => e.EntrepriseId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Agent>()
            .HasOne(e => e.Entreprise).WithMany(e => e.Agents)
            .HasForeignKey(e => e.EntrepriseId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Produit>()
            .HasOne(e => e.Entreprise).WithMany(e => e.Produits)
            .HasForeignKey(e => e.EntrepriseId).OnDelete(DeleteBehavior.Cascade);

        // Chaîne d'exploitation : **Restrict**. Chacune de ces lignes a son écran,
        // son formulaire et son bouton de suppression depuis l'étape 16 bis. Perdre
        // l'historique d'exploitation d'un client sur un clic n'est pas réparable
        // sans sauvegarde.
        modelBuilder.Entity<Site>()
            .HasOne(e => e.Client).WithMany(e => e.Sites)
            .HasForeignKey(e => e.ClientId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Contrat>()
            .HasOne(e => e.Site).WithMany(e => e.Contrats)
            .HasForeignKey(e => e.SiteId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Prestation>()
            .HasOne(e => e.Contrat).WithMany(e => e.Prestations)
            .HasForeignKey(e => e.ContratId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Intervention>()
            .HasOne(e => e.Prestation).WithMany(e => e.Interventions)
            .HasForeignKey(e => e.PrestationId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Stock>()
            .HasOne(e => e.Produit).WithMany(e => e.Stocks)
            .HasForeignKey(e => e.ProduitId).OnDelete(DeleteBehavior.Restrict);

        // Une absence porte des dates à valeur légale, et l'étape 16 bis lot B lui
        // donne son écran. Elle ne part donc pas avec son agent sans qu'on le
        // demande — d'autant qu'un agent déjà planifié est de toute façon
        // insupprimable, par la restriction sur `Intervention.AgentId` ci-dessous.
        modelBuilder.Entity<Absence>()
            .HasOne(e => e.Agent).WithMany(e => e.Absences)
            .HasForeignKey(e => e.AgentId).OnDelete(DeleteBehavior.Restrict);

        // Remplacement : **Cascade**, seule exception à la règle, et c'est la règle
        // elle-même qui la donne. Un remplacement est de l'état **dérivé** : il est
        // posé par `DetecterImpactsAsync` à partir d'une absence, ou ouvert sur une
        // vacation, il n'a aucune existence propre et **aucun écran** ne le liste.
        //
        // `Restrict` produirait ici le pire résultat d'interface du produit : un 409
        // nommant une ligne que l'utilisateur ne peut ni voir ni supprimer. Or
        // supprimer une absence saisie par erreur est un geste normal, et l'écran
        // des absences arrive au lot B.
        modelBuilder.Entity<Remplacement>()
            .HasOne(e => e.Intervention).WithMany(e => e.Remplacements)
            .HasForeignKey(e => e.InterventionId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Remplacement>()
            .HasOne(e => e.Absence).WithMany(e => e.Remplacements)
            .HasForeignKey(e => e.AbsenceId).OnDelete(DeleteBehavior.Cascade);

        // Déjà en Restrict avant cette passe : supprimer un agent ne doit pas
        // effacer l'historique du planning, ni mettre son affectation à null en
        // silence. L'étape 17 y ajoutera un chemin d'anonymisation, qui conserve la
        // ligne — la bonne réponse à une demande d'effacement quand l'intégrité du
        // planning interdit la suppression.
        modelBuilder.Entity<Intervention>()
            .HasOne(e => e.Agent).WithMany(e => e.Interventions)
            .HasForeignKey(e => e.AgentId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Remplacement>()
            .HasOne(e => e.AgentRemplacant).WithMany()
            .HasForeignKey(e => e.AgentRemplacantId).OnDelete(DeleteBehavior.Restrict);

        // ---- Filtres globaux multi-tenant ----
        //
        // Entreprise est filtrée sur son PROPRE Id : elle ne porte pas de
        // colonne EntrepriseId, son identifiant EST le tenant. Sans ce filtre,
        // GET /api/entreprises listerait la raison sociale et le SIRET de toutes
        // les entreprises de l'instance.
        modelBuilder.Entity<Entreprise>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null || e.Id == TenantEntrepriseId);

        modelBuilder.Entity<Client>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null
            || (e.EntrepriseId == TenantEntrepriseId
                && (TenantClientId == null || e.Id == TenantClientId)));
        modelBuilder.Entity<Site>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null
            || (e.EntrepriseId == TenantEntrepriseId
                && (TenantClientId == null || e.ClientId == TenantClientId)));
        modelBuilder.Entity<Contrat>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null || e.EntrepriseId == TenantEntrepriseId);
        modelBuilder.Entity<Prestation>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null || e.EntrepriseId == TenantEntrepriseId);
        modelBuilder.Entity<Agent>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null || e.EntrepriseId == TenantEntrepriseId);
        modelBuilder.Entity<Intervention>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null
            || (e.EntrepriseId == TenantEntrepriseId
                && (TenantAgentId == null || e.AgentId == TenantAgentId)));
        modelBuilder.Entity<Absence>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null || e.EntrepriseId == TenantEntrepriseId);
        modelBuilder.Entity<Remplacement>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null || e.EntrepriseId == TenantEntrepriseId);
        modelBuilder.Entity<Produit>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null || e.EntrepriseId == TenantEntrepriseId);
        modelBuilder.Entity<Stock>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null || e.EntrepriseId == TenantEntrepriseId);

        // ⚠️ Le journal d'accès est filtré comme le reste, et l'oublier serait pire
        // qu'ailleurs : GET /api/journal-acces est réservé à l'administrateur, donc un
        // administrateur non filtré lirait les habitudes de consultation des autres
        // entreprises de l'instance — qui a regardé combien d'agents, et quand.
        modelBuilder.Entity<AccesJournalise>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null || e.EntrepriseId == TenantEntrepriseId);

        // ⚠️ AspNetUsers n'est PAS filtrée. Identity interroge les comptes hors
        // de toute requête authentifiée — la connexion, par définition — et sa
        // validation d'unicité d'adresse repose sur des requêtes non filtrées.
        // Conséquence : tout futur endpoint listant des comptes doit filtrer à
        // la main sur EntrepriseId. Aucun n'existe à ce jour.
        //
        // JetonsRafraichissement n'est pas filtrée non plus, et pour une raison
        // voisine : le rafraîchissement s'exécute sans requête authentifiée,
        // puisque le jeton d'accès vient d'expirer. La recherche porte sur une
        // empreinte de 256 bits, qui tient lieu de cadrage.

        // EntrepriseId est dénormalisé pour éviter les jointures de la vue planning
        // (CLAUDE.md §7) : il est indexé partout, c'est le prédicat de tous les filtres.
        modelBuilder.Entity<Client>().HasIndex(e => e.EntrepriseId);
        modelBuilder.Entity<Site>().HasIndex(e => e.EntrepriseId);
        modelBuilder.Entity<Contrat>().HasIndex(e => e.EntrepriseId);
        modelBuilder.Entity<Prestation>().HasIndex(e => e.EntrepriseId);
        modelBuilder.Entity<Agent>().HasIndex(e => e.EntrepriseId);
        modelBuilder.Entity<Absence>().HasIndex(e => e.EntrepriseId);
        modelBuilder.Entity<Remplacement>().HasIndex(e => e.EntrepriseId);
        modelBuilder.Entity<Produit>().HasIndex(e => e.EntrepriseId);
        modelBuilder.Entity<Stock>().HasIndex(e => e.EntrepriseId);

        // ⚠️ Le journal se lit toujours du plus récent au plus ancien, et se purge
        // par date : l'index est composite et l'horodatage descendant. Un index sur
        // EntrepriseId seul obligerait à trier une table qui, elle, ne cesse de
        // croître — c'est la seule du modèle dont la taille n'est bornée par aucun
        // effectif, seulement par l'usage et par la purge à douze mois.
        modelBuilder.Entity<AccesJournalise>()
            .HasIndex(e => new { e.EntrepriseId, e.Horodatage })
            .IsDescending(false, true);

        // La requête de planning filtre sur l'entreprise et une semaine de dates.
        modelBuilder.Entity<Intervention>().HasIndex(e => new { e.EntrepriseId, e.DatePrevue });

        // Le rafraîchissement cherche par empreinte, et la révocation en masse
        // par utilisateur. Unique : deux sessions ne peuvent pas partager une
        // empreinte, et l'index le garantit au lieu de l'espérer.
        modelBuilder.Entity<JetonRafraichissement>().HasIndex(e => e.Empreinte).IsUnique();
        modelBuilder.Entity<JetonRafraichissement>().HasIndex(e => e.UtilisateurId);

        // Supprimer un compte emporte ses sessions : une session orpheline
        // n'authentifie plus personne, la garder n'a aucun sens.
        modelBuilder.Entity<JetonRafraichissement>()
            .HasOne(e => e.Utilisateur).WithMany()
            .HasForeignKey(e => e.UtilisateurId).OnDelete(DeleteBehavior.Cascade);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AppliquerTenant();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AppliquerTenant();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Impose le tenant du jeton sur toute écriture. Le forçage est
    /// <b>inconditionnel</b> : n'écraser que <c>Guid.Empty</c> laisserait passer
    /// un identifiant réel d'une autre entreprise fourni dans la requête, et la
    /// ligne serait créée chez elle. Voir l'avertissement de CLAUDE.md §7.
    /// </summary>
    private void AppliquerTenant()
    {
        var entrepriseId = TenantEntrepriseId;

        // Pas de tenant : amorçage, série de tests, ou requête anonyme. Les onze
        // contrôleurs métier portant [Authorize], ce dernier cas n'atteint
        // jamais les données.
        if (!TenantFiltrageActif || entrepriseId is null)
        {
            return;
        }

        foreach (var entree in ChangeTracker.Entries())
        {
            var propriete = entree.Metadata.FindProperty("EntrepriseId");
            if (propriete is null || propriete.ClrType != typeof(Guid))
            {
                continue;
            }

            var valeur = entree.Property(propriete.Name);

            if (entree.State == EntityState.Added)
            {
                valeur.CurrentValue = entrepriseId.Value;
            }
            else if (entree.State == EntityState.Modified && valeur.IsModified)
            {
                // La ligne a été chargée à travers le filtre : sa valeur
                // d'origine est celle du tenant. On la restaure plutôt que de
                // laisser une mise à jour déplacer une ligne d'un tenant à
                // l'autre.
                valeur.CurrentValue = valeur.OriginalValue;
                valeur.IsModified = false;
            }
        }
    }
}
