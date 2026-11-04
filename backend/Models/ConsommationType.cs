using System.ComponentModel.DataAnnotations;

namespace Klean.Api.Models;

/// <summary>
/// Ce qu'un passage d'un type de prestation consomme d'un produit — « Hall d'entrée :
/// 0,25 litre de détergent sols » (SPEC.md, étape 22, lot B). <see cref="EntrepriseId"/>
/// dénormalisé, sans clé étrangère, comme le stock.
///
/// Par type seulement, en V1 : une surcharge par prestation donnerait deux sources à la
/// même quantité. Une prestation sans type ne consomme donc rien, et la prévision le dit.
///
/// ⚠️ Elle sert au <b>besoin prévu</b>, jamais à une déduction : rien ne certifie encore
/// qu'un passage a eu lieu, et le réel s'établit par l'inventaire.
/// </summary>
public class ConsommationType
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid EntrepriseId { get; set; }

    /// <summary>Une ligne par type et par produit, gardée par un index unique.</summary>
    [Required]
    public Guid TypePrestationId { get; set; }
    public TypePrestation? TypePrestation { get; set; }

    [Required]
    public Guid ProduitId { get; set; }
    public Produit? Produit { get; set; }

    /// <summary>Dans l'unité du produit, toujours positive.</summary>
    public decimal QuantiteParPassage { get; set; }
}
