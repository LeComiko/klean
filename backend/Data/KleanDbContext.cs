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

    /// <summary>
    /// Vrai quand <see cref="BasculeRoleApplicatif"/> doit mettre la connexion sous la RLS
    /// (SPEC.md, étape 26) : tout contexte injecté, tant que le filtrage n'a pas été désactivé.
    /// Faux pour un contexte construit hors injection — les migrations — et sous
    /// <c>DesactiverFiltrage</c> : la connexion reste alors au propriétaire.
    /// </summary>
    internal bool SousLaSecuriteDesLignes => _tenant?.FiltrageActif == true;

    /// <summary>
    /// L'entreprise que les politiques comparent : celle du jeton, ou, sur la requête anonyme de
    /// l'assistant d'installation, celle que <c>PoserEntrepriseInstallation</c> a tirée. Nulle : aucune ligne.
    /// </summary>
    internal Guid? EntrepriseDuJeton => _tenant?.EntrepriseId;

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

    // Quatrième axe, l'équipe d'un chef (SPEC.md, étape 19). Nul hors du rôle
    // ChefEquipe, auquel cas les conditions qui en dépendent valent vrai.
    private Guid? TenantChefEquipeId => _tenant?.ChefEquipeId;

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

    /// <summary>Le catalogue des prestations de l'entreprise (SPEC.md, étape 21).</summary>
    public DbSet<TypePrestation> TypesPrestation => Set<TypePrestation>();
    public DbSet<Stock> Stocks => Set<Stock>();

    /// <summary>Dépôts et véhicules (SPEC.md, étape 22). Le site est l'autre lieu d'un stock.</summary>
    public DbSet<Emplacement> Emplacements => Set<Emplacement>();

    /// <summary>La seule écriture qui change une quantité de stock (SPEC.md, étape 22).</summary>
    public DbSet<MouvementStock> MouvementsStock => Set<MouvementStock>();

    /// <summary>Ce qu'un passage de chaque type consomme (SPEC.md, étape 22, lot B).</summary>
    public DbSet<ConsommationType> ConsommationsType => Set<ConsommationType>();

    /// <summary>Le début et la fin d'un passage, pointés par l'agent (SPEC.md, étape 25).</summary>
    public DbSet<Pointage> Pointages => Set<Pointage>();

    /// <summary>La checklist de chaque type de prestation (SPEC.md, étape 25, lot B).</summary>
    public DbSet<PointControle> PointsControle => Set<PointControle>();

    /// <summary>Ce qui a été coché, ou non, en terminant une vacation.</summary>
    public DbSet<ControleEffectue> ControlesEffectues => Set<ControleEffectue>();

    public DbSet<Anomalie> Anomalies => Set<Anomalie>();
    public DbSet<PhotoAnomalie> PhotosAnomalie => Set<PhotoAnomalie>();

    /// <summary>
    /// Journal d'accès aux données personnelles (SPEC.md étape 17). La seule
    /// entité qui n'existe pas pour le métier : elle existe pour rendre compte de
    /// l'usage des autres.
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

        // Les six énumérations sont stockées en texte (CLAUDE.md §5).
        // Pas de HasMaxLength ici : ce serait varchar(n), on veut text.
        modelBuilder.Entity<Client>().Property(e => e.Type).HasConversion<string>();
        modelBuilder.Entity<Intervention>().Property(e => e.Statut).HasConversion<string>();
        modelBuilder.Entity<Absence>().Property(e => e.Type).HasConversion<string>();
        modelBuilder.Entity<Remplacement>().Property(e => e.Statut).HasConversion<string>();
        modelBuilder.Entity<Emplacement>().Property(e => e.Type).HasConversion<string>();
        modelBuilder.Entity<MouvementStock>().Property(e => e.Nature).HasConversion<string>();
        modelBuilder.Entity<Pointage>().Property(e => e.Moment).HasConversion<string>();
        modelBuilder.Entity<Pointage>().Property(e => e.Preuve).HasConversion<string>();
        modelBuilder.Entity<Anomalie>().Property(e => e.Statut).HasConversion<string>();
        modelBuilder.Entity<AccesJournalise>().Property(e => e.Ressource).HasConversion<string>();
        modelBuilder.Entity<AccesJournalise>().Property(e => e.Nature).HasConversion<string>();

        // Énumération à drapeaux : « Lundi, Mercredi » en base, relu tel quel.
        //
        // ⚠️ Le défaut en base est pour les prestations antérieures à la colonne.
        // Sans lui, la migration leur donne la chaîne vide — une valeur qu'aucun
        // code n'écrit, et que seule une indulgence d'EF rend lisible : sa
        // conversion d'énumération ne lève pas sur une valeur illisible, elle
        // rend `Aucun` en silence. Mesuré : un sabotage du défaut laissait la
        // lecture verte. « Aucun » écrit en base les laisse planifiées à la main,
        // comme avant, et lisibles telles quelles par un script SQL.
        //
        // La sentinelle -1 n'est jamais portée par une ligne : EF envoie donc
        // toujours la valeur de l'entité, et le défaut ne sert qu'aux lignes que la
        // migration trouve en place. Sans elle, « Aucun » étant aussi le défaut de
        // l'énumération, EF écrit un avertissement à chaque démarrage — dans le
        // journal de production, où un exploitant chercherait une panne.
        modelBuilder.Entity<Prestation>().Property(e => e.Jours)
            .HasConversion<string>()
            .HasDefaultValueSql("'Aucun'")
            .HasSentinel((JoursSemaine)(-1));

        modelBuilder.Entity<Produit>().Property(e => e.SeuilAlerte).HasPrecision(10, 2);
        modelBuilder.Entity<Stock>().Property(e => e.Quantite).HasPrecision(10, 2);
        modelBuilder.Entity<MouvementStock>().Property(e => e.Quantite).HasPrecision(10, 2);
        modelBuilder.Entity<MouvementStock>().Property(e => e.Ecart).HasPrecision(10, 2);
        modelBuilder.Entity<ConsommationType>().Property(e => e.QuantiteParPassage).HasPrecision(10, 2);

        // ⚠️ Le défaut sert les produits antérieurs à la colonne : sans lui, la migration
        // les créerait tous désactivés. La sentinelle `true` fait qu'EF envoie toujours
        // `false` quand l'application le pose, et laisse le défaut — vrai lui aussi —
        // couvrir le cas où elle pose `true`. Même raisonnement que `Prestation.Jours`.
        modelBuilder.Entity<Produit>().Property(e => e.Actif).HasDefaultValue(true).HasSentinel(true);

        // Le territoire, en texte comme les autres énumérations. Le défaut sert les
        // entreprises antérieures à la colonne — sans lui, la chaîne vide, relue en
        // silence comme `Metropole` par la conversion d'EF (CLAUDE.md §5). La sentinelle
        // fait envoyer la valeur de l'entité, même `Metropole` : même raisonnement que
        // `Prestation.Jours`.
        modelBuilder.Entity<Entreprise>().Property(e => e.Territoire)
            .HasConversion<string>()
            .HasDefaultValueSql("'Metropole'")
            .HasSentinel((Territoire)(-1));

        modelBuilder.Entity<Entreprise>().HasIndex(e => e.Siret).IsUnique();

        // ⚠️ Le défaut en base n'est pas là pour l'application, qui pose la valeur
        // elle-même : il est là pour les lignes qui existaient avant la colonne.
        // Sans lui, la migration les daterait du 1er janvier de l'an 1, et
        // `conservation-donnees-metier.sql` les verrait toutes hors délai depuis
        // deux mille ans. `now()` les date du jour de la migration — une borne
        // haute de leur vraie création, donc une purge retardée, jamais avancée.
        modelBuilder.Entity<Site>().Property(e => e.DateCreation).HasDefaultValueSql("now()");
        modelBuilder.Entity<Client>().Property(e => e.DateCreation).HasDefaultValueSql("now()");

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

        modelBuilder.Entity<TypePrestation>()
            .HasOne(e => e.Entreprise).WithMany()
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

        // Le stock et ses mouvements (SPEC.md, étape 22). Tout en **Restrict** : un
        // mouvement est l'histoire d'une quantité, et un produit, un lieu ou un site qui
        // en porte ne part pas en emportant l'explication de ce qu'il reste. On les
        // désactive ; un site, lui, garde son stock tant qu'on ne l'a pas vidé.
        modelBuilder.Entity<Emplacement>()
            .HasOne(e => e.Entreprise).WithMany()
            .HasForeignKey(e => e.EntrepriseId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Stock>()
            .HasOne(e => e.Emplacement).WithMany()
            .HasForeignKey(e => e.EmplacementId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Stock>()
            .HasOne(e => e.Site).WithMany()
            .HasForeignKey(e => e.SiteId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<MouvementStock>()
            .HasOne(e => e.Produit).WithMany()
            .HasForeignKey(e => e.ProduitId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MouvementStock>()
            .HasOne(e => e.OrigineEmplacement).WithMany()
            .HasForeignKey(e => e.OrigineEmplacementId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MouvementStock>()
            .HasOne(e => e.OrigineSite).WithMany()
            .HasForeignKey(e => e.OrigineSiteId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MouvementStock>()
            .HasOne(e => e.DestinationEmplacement).WithMany()
            .HasForeignKey(e => e.DestinationEmplacementId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MouvementStock>()
            .HasOne(e => e.DestinationSite).WithMany()
            .HasForeignKey(e => e.DestinationSiteId).OnDelete(DeleteBehavior.Restrict);

        // ⚠️ Les gardes de base, sous le service : elles tranchent ce qu'une erreur de code
        // ou un script SQL laisserait passer. Un stock est en un lieu et un seul, jamais
        // négatif ; un côté de mouvement désigne au plus un lieu.
        modelBuilder.Entity<Stock>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_Stocks_UnLieu", "(\"EmplacementId\" IS NULL) <> (\"SiteId\" IS NULL)");
            t.HasCheckConstraint("CK_Stocks_QuantitePositive", "\"Quantite\" >= 0");
        });
        modelBuilder.Entity<MouvementStock>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_MouvementsStock_UneOrigine",
                "\"OrigineEmplacementId\" IS NULL OR \"OrigineSiteId\" IS NULL");
            t.HasCheckConstraint("CK_MouvementsStock_UneDestination",
                "\"DestinationEmplacementId\" IS NULL OR \"DestinationSiteId\" IS NULL");
        });

        // La consommation par type (lot B) : **Restrict** des deux côtés. Elle a son écran,
        // et un type ou un produit qui part ne l'emporte pas en silence — ce que la
        // prévision compterait en moins sans que personne l'ait décidé.
        modelBuilder.Entity<ConsommationType>()
            .HasOne(e => e.TypePrestation).WithMany()
            .HasForeignKey(e => e.TypePrestationId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ConsommationType>()
            .HasOne(e => e.Produit).WithMany()
            .HasForeignKey(e => e.ProduitId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ConsommationType>().ToTable(t =>
            t.HasCheckConstraint("CK_ConsommationsType_QuantitePositive", "\"QuantiteParPassage\" > 0"));

        // Une consommation par type et par produit : deux lignes donneraient deux réponses
        // à « combien ce passage consomme-t-il », et la prévision les additionnerait.
        modelBuilder.Entity<ConsommationType>()
            .HasIndex(e => new { e.TypePrestationId, e.ProduitId }).IsUnique();

        // Une ligne par produit et par lieu : deux lignes rendraient la quantité d'un lieu
        // ambiguë. Partiels, parce qu'une seule des deux colonnes est renseignée.
        modelBuilder.Entity<Stock>()
            .HasIndex(e => new { e.ProduitId, e.EmplacementId }).IsUnique()
            .HasFilter("\"EmplacementId\" IS NOT NULL");
        modelBuilder.Entity<Stock>()
            .HasIndex(e => new { e.ProduitId, e.SiteId }).IsUnique()
            .HasFilter("\"SiteId\" IS NOT NULL");

        // Le pointage (SPEC.md, étape 25) : **Restrict**. Il est la preuve d'un passage, et
        // une vacation pointée ne se supprime pas — le contrôleur le refuse avant la base, en
        // disant pourquoi ; la base le refuse sous lui.
        modelBuilder.Entity<Pointage>()
            .HasOne(e => e.Intervention).WithMany()
            .HasForeignKey(e => e.InterventionId).OnDelete(DeleteBehavior.Restrict);

        // Un début et une fin par vacation, pas davantage. ⚠️ C'est aussi la garde d'un double
        // appui sur « Démarrer » : la seconde insertion tombe sur l'index, et le service
        // répond 409 au lieu d'écrire deux débuts.
        modelBuilder.Entity<Pointage>()
            .HasIndex(e => new { e.InterventionId, e.Moment }).IsUnique();

        // La checklist et les anomalies (SPEC.md, étape 25, lot B).
        //
        // Un point de contrôle tient à son type par **Restrict**, comme la consommation : un
        // type qui part n'emporte pas en silence la checklist qu'on a saisie pour lui.
        modelBuilder.Entity<PointControle>()
            .HasOne(e => e.TypePrestation).WithMany()
            .HasForeignKey(e => e.TypePrestationId).OnDelete(DeleteBehavior.Restrict);

        // Ce qui a été coché tient à sa vacation par **Restrict** — c'est son compte rendu —,
        // et à son point par **SetNull** : le libellé est copié, la ligne se lit sans le point,
        // et supprimer un point de la checklist ne doit pas être bloqué par l'historique.
        // Même raisonnement que le titulaire d'une prestation.
        modelBuilder.Entity<ControleEffectue>()
            .HasOne(e => e.Intervention).WithMany()
            .HasForeignKey(e => e.InterventionId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ControleEffectue>()
            .HasOne(e => e.PointControle).WithMany()
            .HasForeignKey(e => e.PointControleId).OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Anomalie>()
            .HasOne(e => e.Intervention).WithMany()
            .HasForeignKey(e => e.InterventionId).OnDelete(DeleteBehavior.Restrict);

        // Une photo n'existe que pour son anomalie : **Cascade**. Aucune route ne supprime une
        // anomalie ; c'est la purge de conservation qui le fera, et elle doit emporter les
        // photos plutôt que de buter dessus.
        modelBuilder.Entity<PhotoAnomalie>()
            .HasOne(e => e.Anomalie).WithMany(e => e.Photos)
            .HasForeignKey(e => e.AnomalieId).OnDelete(DeleteBehavior.Cascade);

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

        // Titulaire d'une prestation : **SetNull**, et ce n'est pas une entorse au
        // Restrict de `Intervention.AgentId` juste au-dessus. Celui-là protège un
        // fait — qui a travaillé où —, que rien ne doit effacer. Celui-ci n'est
        // qu'une préférence pour les vacations à venir : l'agent supprimé, la
        // préférence n'a plus d'objet, et aucune ligne ne disparaît. Restrict ici
        // rendrait insupprimable un agent qui n'a jamais travaillé, pour un simple
        // rattachement.
        modelBuilder.Entity<Prestation>()
            .HasOne(e => e.AgentTitulaire).WithMany()
            .HasForeignKey(e => e.AgentTitulaireId).OnDelete(DeleteBehavior.SetNull);

        // Type du catalogue d'une prestation : **Restrict**, et non SetNull comme le
        // titulaire juste au-dessus. Le titulaire est une préférence que son départ rend
        // sans objet ; le type est la clé dont le stock et le devis auront besoin, et le
        // perdre en silence ferait mentir leurs calculs. Un type utilisé se retire par
        // désactivation, ce que dit le 409 de sa suppression (SPEC.md, étape 21).
        modelBuilder.Entity<Prestation>()
            .HasOne(e => e.TypePrestation).WithMany()
            .HasForeignKey(e => e.TypePrestationId).OnDelete(DeleteBehavior.Restrict);

        // Chef d'équipe d'un agent : **SetNull**, même raisonnement. Le lien vise un
        // compte d'Identity, et supprimer ce compte ne doit ni être bloqué par son
        // équipe, ni emporter les agents qu'il encadrait.
        modelBuilder.Entity<Agent>()
            .HasOne(e => e.ChefEquipe).WithMany()
            .HasForeignKey(e => e.ChefEquipeId).OnDelete(DeleteBehavior.SetNull);

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
        // ---- L'équipe d'un chef : agents, absences, interventions, remplacements ----
        //
        // Une frontière, du même ordre que celle du syndic, et non un filtre de confort :
        // avec plusieurs chefs, chacun verrait sinon les équipes des autres.
        //
        // ⚠️ Les conditions passent par des sous-requêtes `Any`, jamais par des
        // navigations. Une navigation devient une jointure dans le FROM, que tout le
        // monde paierait ; une sous-requête reste dans le WHERE, derrière un paramètre
        // « pas de chef » en tête d'un OR. EF ne la retire pas du SQL — il émet
        // `(@p OR EXISTS …)` pour tous —, mais PostgreSQL ne l'évalue pas quand le
        // paramètre est vrai. Mesuré le 28 octobre 2026 sur 109 200 vacations, plan
        // générique forcé, requête de la semaine : 0,30 ms avant, 0,34 à 0,45 ms pour le
        // patron, 0,49 à 0,56 ms pour un chef — ses sous-requêtes sont hachées une fois,
        // pas rejouées ligne à ligne.
        //
        // ⚠️ Le moteur d'affectation, le planning et le cycle de remplacement lisent ces
        // ensembles filtrés : pour un chef, les candidats sont son équipe par
        // construction. Les filtres durs n'y perdent rien, toutes les vacations et les
        // absences d'un agent de l'équipe restant visibles à son chef.
        modelBuilder.Entity<Agent>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null
            || (e.EntrepriseId == TenantEntrepriseId
                && (TenantChefEquipeId == null || e.ChefEquipeId == TenantChefEquipeId)));

        // Une vacation affectée appartient à l'équipe de son agent, quelle que soit sa
        // prestation. Une vacation sans agent, à l'équipe du titulaire de sa prestation ;
        // sans titulaire, à aucune — seuls le patron et les responsables la voient.
        modelBuilder.Entity<Intervention>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null
            || (e.EntrepriseId == TenantEntrepriseId
                && (TenantAgentId == null || e.AgentId == TenantAgentId)
                && (TenantChefEquipeId == null
                    || (e.AgentId != null
                        && Agents.Any(a => a.Id == e.AgentId && a.ChefEquipeId == TenantChefEquipeId))
                    || (e.AgentId == null
                        && Prestations.Any(p => p.Id == e.PrestationId
                            && Agents.Any(a => a.Id == p.AgentTitulaireId
                                && a.ChefEquipeId == TenantChefEquipeId))))));
        modelBuilder.Entity<Absence>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null
            || (e.EntrepriseId == TenantEntrepriseId
                && (TenantChefEquipeId == null
                    || Agents.Any(a => a.Id == e.AgentId && a.ChefEquipeId == TenantChefEquipeId))));

        // Les remplacements des vacations que le chef voit. La règle n'est pas recopiée :
        // le filtre des interventions s'applique à la sous-requête, et une seule
        // définition de l'équipe ne peut pas diverger d'elle-même.
        modelBuilder.Entity<Remplacement>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null
            || (e.EntrepriseId == TenantEntrepriseId
                && (TenantChefEquipeId == null || Interventions.Any(i => i.Id == e.InterventionId))));
        modelBuilder.Entity<Produit>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null || e.EntrepriseId == TenantEntrepriseId);
        modelBuilder.Entity<TypePrestation>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null || e.EntrepriseId == TenantEntrepriseId);
        modelBuilder.Entity<Stock>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null || e.EntrepriseId == TenantEntrepriseId);
        modelBuilder.Entity<Emplacement>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null || e.EntrepriseId == TenantEntrepriseId);
        modelBuilder.Entity<MouvementStock>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null || e.EntrepriseId == TenantEntrepriseId);
        modelBuilder.Entity<ConsommationType>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null || e.EntrepriseId == TenantEntrepriseId);

        // Les pointages des vacations que l'appelant voit : l'agent les siens, le chef ceux de
        // son équipe. La règle n'est pas recopiée, comme pour les remplacements : le filtre
        // des interventions s'applique à la sous-requête. ⚠️ Rien pour le syndic, dont le
        // filtre des interventions ne restreint pas le client : il attend le portail.
        modelBuilder.Entity<Pointage>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null
            || (e.EntrepriseId == TenantEntrepriseId
                && TenantClientId == null
                && ((TenantAgentId == null && TenantChefEquipeId == null)
                    || Interventions.Any(i => i.Id == e.InterventionId))));

        // La checklist est un catalogue : celle de l'entreprise, comme les types.
        modelBuilder.Entity<PointControle>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null || e.EntrepriseId == TenantEntrepriseId);

        // Ce qui a été coché et ce qui a été signalé suivent la vacation, exactement comme les
        // pointages : l'agent les siens, le chef ceux de son équipe, rien pour le syndic.
        modelBuilder.Entity<ControleEffectue>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null
            || (e.EntrepriseId == TenantEntrepriseId
                && TenantClientId == null
                && ((TenantAgentId == null && TenantChefEquipeId == null)
                    || Interventions.Any(i => i.Id == e.InterventionId))));
        modelBuilder.Entity<Anomalie>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null
            || (e.EntrepriseId == TenantEntrepriseId
                && TenantClientId == null
                && ((TenantAgentId == null && TenantChefEquipeId == null)
                    || Interventions.Any(i => i.Id == e.InterventionId))));

        // Les photos des anomalies que l'appelant voit : le filtre des anomalies s'applique à
        // la sous-requête, qui porte à son tour celui des vacations.
        modelBuilder.Entity<PhotoAnomalie>().HasQueryFilter(e =>
            !TenantFiltrageActif || TenantEntrepriseId == null
            || (e.EntrepriseId == TenantEntrepriseId
                && TenantClientId == null
                && ((TenantAgentId == null && TenantChefEquipeId == null)
                    || Anomalies.Any(a => a.Id == e.AnomalieId))));

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

        // L'équipe d'un chef se lit par cette colonne : c'est le filtre du lot B de
        // l'étape 19, appliqué à chaque lecture d'agent, d'absence et d'intervention.
        modelBuilder.Entity<Agent>().HasIndex(e => e.ChefEquipeId);
        modelBuilder.Entity<Absence>().HasIndex(e => e.EntrepriseId);
        modelBuilder.Entity<Remplacement>().HasIndex(e => e.EntrepriseId);
        modelBuilder.Entity<Produit>().HasIndex(e => e.EntrepriseId);
        modelBuilder.Entity<TypePrestation>().HasIndex(e => e.EntrepriseId);
        modelBuilder.Entity<Stock>().HasIndex(e => e.EntrepriseId);
        modelBuilder.Entity<Emplacement>().HasIndex(e => e.EntrepriseId);
        modelBuilder.Entity<ConsommationType>().HasIndex(e => e.EntrepriseId);

        // L'historique se lit du plus récent au plus ancien, comme le journal d'accès.
        modelBuilder.Entity<MouvementStock>()
            .HasIndex(e => new { e.EntrepriseId, e.DateMouvement })
            .IsDescending(false, true);

        // Les pointages se purgent par date, à un an (RGPD.md §5) : même forme d'index.
        modelBuilder.Entity<Pointage>()
            .HasIndex(e => new { e.EntrepriseId, e.Horodatage })
            .IsDescending(false, true);

        // La liste des anomalies se lit des plus récentes aux plus anciennes, et se purgera
        // par date ; la checklist par type, dans son ordre.
        modelBuilder.Entity<Anomalie>()
            .HasIndex(e => new { e.EntrepriseId, e.DateSignalement })
            .IsDescending(false, true);
        modelBuilder.Entity<PointControle>().HasIndex(e => e.EntrepriseId);
        modelBuilder.Entity<ControleEffectue>().HasIndex(e => e.EntrepriseId);
        modelBuilder.Entity<PhotoAnomalie>().HasIndex(e => e.EntrepriseId);

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
