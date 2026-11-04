using System.ComponentModel.DataAnnotations;

namespace Klean.Api.DTOs;

public class ConsommationTypeDto
{
    public Guid Id { get; set; }
    public Guid EntrepriseId { get; set; }
    public Guid TypePrestationId { get; set; }
    public Guid ProduitId { get; set; }
    public decimal QuantiteParPassage { get; set; }
}

public class ConsommationTypeCreateDto
{
    public Guid TypePrestationId { get; set; }
    public Guid ProduitId { get; set; }

    /// <summary>
    /// Dans l'unité du produit. Mêmes bornes que les mouvements, plancher au centième : une
    /// consommation nulle ne se saisit pas, elle se supprime.
    /// </summary>
    [Range(0.01, 99999999.99)]
    public decimal QuantiteParPassage { get; set; }
}
