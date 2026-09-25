using Klean.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Data;

public class KleanDbContext : IdentityDbContext<Utilisateur, IdentityRole<Guid>, Guid>
{
    public KleanDbContext(DbContextOptions<KleanDbContext> options) : base(options)
    {
    }

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
}
