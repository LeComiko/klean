using System.ComponentModel.DataAnnotations;

namespace Klean.Api.DTOs;

public class StockDto
{
    public Guid Id { get; set; }
    public Guid EntrepriseId { get; set; }
    public Guid ProduitId { get; set; }

    /// <summary>Valeurs : Depot, Vehicule, Site.</summary>
    public string TypeEmplacement { get; set; } = string.Empty;

    /// <summary>Sans clé étrangère : dépôt, véhicule ou site selon le type.</summary>
    public Guid EmplacementId { get; set; }

    public decimal Quantite { get; set; }
}

public class StockCreateDto
{
    public Guid EntrepriseId { get; set; }

    [Required]
    public Guid ProduitId { get; set; }

    [Required]
    public string TypeEmplacement { get; set; } = string.Empty;

    [Required]
    public Guid EmplacementId { get; set; }

    [Range(0, 999999.99)]
    public decimal Quantite { get; set; }
}
