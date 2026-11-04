using System.ComponentModel.DataAnnotations;

namespace Klean.Api.Models;

/// <summary>
/// Quantité d'un produit en un lieu : un <see cref="Emplacement"/> — dépôt ou véhicule —
/// <b>ou</b> un <see cref="Site"/>, jamais les deux ni aucun, ce qu'une contrainte de
/// base garde. Une seule ligne par produit et par lieu, gardée par deux index uniques
/// partiels. <see cref="EntrepriseId"/> dénormalisé, sans clé étrangère.
///
/// ⚠️ <see cref="Quantite"/> n'est écrite que par <c>ServiceStock</c>, dans la
/// transaction d'un <see cref="MouvementStock"/> (SPEC.md, étape 22) : aucune route ne la
/// saisit. Une quantité qu'on tape n'a pas d'histoire, et « pourquoi a-t-elle baissé de
/// douze litres ? » n'a alors pas de réponse.
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

    public Guid? EmplacementId { get; set; }
    public Emplacement? Emplacement { get; set; }

    public Guid? SiteId { get; set; }
    public Site? Site { get; set; }

    /// <summary>Jamais négative : une contrainte de base le garde, sous le service.</summary>
    public decimal Quantite { get; set; }
}
