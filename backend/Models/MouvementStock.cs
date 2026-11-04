using System.ComponentModel.DataAnnotations;

namespace Klean.Api.Models;

/// <summary>
/// Ce qu'un mouvement fait, et donc quels lieux il exige.
///
/// | Nature | Origine | Destination |
/// |---|---|---|
/// | Entree | — | requise |
/// | Sortie | requise | — |
/// | Transfert | requise | requise, distincte |
/// | Inventaire | — | requise : le lieu compté |
/// </summary>
public enum NatureMouvement
{
    /// <summary>Livraison d'un fournisseur.</summary>
    Entree,

    /// <summary>Consommé, perdu, cassé : le produit quitte l'entreprise.</summary>
    Sortie,

    /// <summary>Du dépôt au véhicule, du véhicule au site.</summary>
    Transfert,

    /// <summary>
    /// Le comptage d'un lieu : la quantité devient celle qu'on a comptée, et l'écart est
    /// conservé. C'est par lui que le réel entre dans KLEAN, la consommation n'étant que
    /// prévisionnelle (SPEC.md, étape 22).
    /// </summary>
    Inventaire
}

/// <summary>
/// Un mouvement de stock, et la seule écriture qui change une quantité (SPEC.md,
/// étape 22). Ni modifiable ni supprimable : une erreur se corrige par un mouvement de
/// plus, et l'historique reste vrai. <see cref="EntrepriseId"/> dénormalisé.
///
/// Chaque côté désigne un emplacement <b>ou</b> un site, au plus l'un des deux ; quels
/// côtés sont requis dépend de <see cref="Nature"/>, ce que le service vérifie.
/// </summary>
public class MouvementStock
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid EntrepriseId { get; set; }

    [Required]
    public Guid ProduitId { get; set; }
    public Produit? Produit { get; set; }

    [Required]
    public NatureMouvement Nature { get; set; }

    public Guid? OrigineEmplacementId { get; set; }
    public Emplacement? OrigineEmplacement { get; set; }
    public Guid? OrigineSiteId { get; set; }
    public Site? OrigineSite { get; set; }

    public Guid? DestinationEmplacementId { get; set; }
    public Emplacement? DestinationEmplacement { get; set; }
    public Guid? DestinationSiteId { get; set; }
    public Site? DestinationSite { get; set; }

    /// <summary>La quantité déplacée ; pour un inventaire, la quantité comptée.</summary>
    public decimal Quantite { get; set; }

    /// <summary>
    /// Inventaire seulement : comptée moins attendue. Négatif, il manque du produit. C'est
    /// la ligne qui répond à « pourquoi a-t-il baissé ? » quand aucun mouvement ne l'explique.
    /// </summary>
    public decimal? Ecart { get; set; }

    /// <summary>Posée par le serveur, jamais par le client.</summary>
    public DateTime DateMouvement { get; set; }

    /// <summary>
    /// Le compte qui a enregistré le mouvement. Donnée personnelle (RGPD.md §3) : elle
    /// désigne un collègue. Sans clé étrangère, comme <c>AccesJournalise.UtilisateurId</c> :
    /// un compte n'est jamais supprimé, il est pseudonymisé, et l'identifiant survit.
    /// </summary>
    public Guid AuteurId { get; set; }
}
