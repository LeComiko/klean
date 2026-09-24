using System.ComponentModel.DataAnnotations;

namespace Klean.Api.Models;

public enum TypeEmplacement
{
    Depot,
    Vehicule,
    Site
}

/// <summary>
/// Quantité d'un produit à un emplacement. <see cref="EntrepriseId"/> dénormalisé,
/// sans clé étrangère.
/// </summary>
public class Stock
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid EntrepriseId { get; set; }

    [Required]
    public Guid ProduitId { get; set; }
    public Produit? Produit { get; set; }

    [Required]
    public TypeEmplacement TypeEmplacement { get; set; }

    /// <summary>
    /// Volontairement sans clé étrangère : selon <see cref="TypeEmplacement"/>,
    /// pointe vers un dépôt, un véhicule ou un site (CLAUDE.md §5).
    /// </summary>
    [Required]
    public Guid EmplacementId { get; set; }

    public decimal Quantite { get; set; }
}
