using System.ComponentModel.DataAnnotations;

namespace Klean.Api.DTOs;

public class MouvementStockDto
{
    public Guid Id { get; set; }
    public Guid EntrepriseId { get; set; }
    public Guid ProduitId { get; set; }

    /// <summary>Valeurs : Entree, Sortie, Transfert, Inventaire.</summary>
    public string Nature { get; set; } = string.Empty;

    public Guid? OrigineEmplacementId { get; set; }
    public Guid? OrigineSiteId { get; set; }
    public Guid? DestinationEmplacementId { get; set; }
    public Guid? DestinationSiteId { get; set; }

    /// <summary>La quantité déplacée ; pour un inventaire, la quantité comptée.</summary>
    public decimal Quantite { get; set; }

    /// <summary>Inventaire seulement : comptée moins attendue.</summary>
    public decimal? Ecart { get; set; }

    public DateTime DateMouvement { get; set; }

    /// <summary>L'identifiant du compte, jamais son nom : le DTO reste hors du journal d'accès.</summary>
    public Guid AuteurId { get; set; }
}

/// <summary>
/// Un mouvement à enregistrer. Les lieux requis dépendent de la nature — voir
/// <c>NatureMouvement</c> ; chaque côté désigne un emplacement <b>ou</b> un site.
/// Ni date ni auteur : le serveur les pose.
/// </summary>
public class MouvementStockCreateDto
{
    [Required]
    public Guid ProduitId { get; set; }

    [Required]
    public string Nature { get; set; } = string.Empty;

    public Guid? OrigineEmplacementId { get; set; }
    public Guid? OrigineSiteId { get; set; }
    public Guid? DestinationEmplacementId { get; set; }
    public Guid? DestinationSiteId { get; set; }

    /// <summary>Mêmes bornes que la colonne. Zéro n'est admis que pour un inventaire.</summary>
    [Range(0, 99999999.99)]
    public decimal Quantite { get; set; }
}
