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

        modelBuilder.Entity<Produit>().Property(e => e.SeuilAlerte).HasPrecision(10, 2);
        modelBuilder.Entity<Stock>().Property(e => e.Quantite).HasPrecision(10, 2);

        modelBuilder.Entity<Entreprise>().HasIndex(e => e.Siret).IsUnique();

        // Chaîne de rattachement : la suppression d'un parent emporte ses enfants.
        modelBuilder.Entity<Client>()
            .HasOne(e => e.Entreprise).WithMany(e => e.Clients)
            .HasForeignKey(e => e.EntrepriseId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Agent>()
            .HasOne(e => e.Entreprise).WithMany(e => e.Agents)
            .HasForeignKey(e => e.EntrepriseId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Produit>()
            .HasOne(e => e.Entreprise).WithMany(e => e.Produits)
            .HasForeignKey(e => e.EntrepriseId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Site>()
            .HasOne(e => e.Client).WithMany(e => e.Sites)
            .HasForeignKey(e => e.ClientId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Contrat>()
            .HasOne(e => e.Site).WithMany(e => e.Contrats)
            .HasForeignKey(e => e.SiteId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Prestation>()
            .HasOne(e => e.Contrat).WithMany(e => e.Prestations)
            .HasForeignKey(e => e.ContratId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Intervention>()
            .HasOne(e => e.Prestation).WithMany(e => e.Interventions)
            .HasForeignKey(e => e.PrestationId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Absence>()
            .HasOne(e => e.Agent).WithMany(e => e.Absences)
            .HasForeignKey(e => e.AgentId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Stock>()
            .HasOne(e => e.Produit).WithMany(e => e.Stocks)
            .HasForeignKey(e => e.ProduitId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Remplacement>()
            .HasOne(e => e.Intervention).WithMany(e => e.Remplacements)
            .HasForeignKey(e => e.InterventionId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Remplacement>()
            .HasOne(e => e.Absence).WithMany(e => e.Remplacements)
            .HasForeignKey(e => e.AbsenceId).OnDelete(DeleteBehavior.Cascade);

        // Supprimer un agent ne doit pas effacer l'historique du planning :
        // restriction plutôt que cascade ou mise à null silencieuse.
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

        // ⚠️ AspNetUsers n'est PAS filtrée. Identity interroge les comptes hors
        // de toute requête authentifiée — la connexion, par définition — et sa
        // validation d'unicité d'adresse repose sur des requêtes non filtrées.
        // Conséquence : tout futur endpoint listant des comptes doit filtrer à
        // la main sur EntrepriseId. À traiter à l'étape 8.

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

        // La requête de planning filtre sur l'entreprise et une semaine de dates.
        modelBuilder.Entity<Intervention>().HasIndex(e => new { e.EntrepriseId, e.DatePrevue });
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
