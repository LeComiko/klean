using System.ComponentModel.DataAnnotations;

namespace Klean.Api.DTOs;

/// <summary>
/// Une quantité en un lieu : <see cref="EmplacementId"/> ou <see cref="SiteId"/>, l'un des
/// deux exactement.
///
/// ⚠️ Pas de <c>StockCreateDto</c>, et c'est voulu depuis l'étape 22 : une quantité ne se
/// saisit plus, elle résulte de mouvements (<c>POST /api/mouvements-stock</c>).
/// </summary>
public class StockDto
{
    public Guid Id { get; set; }
    public Guid EntrepriseId { get; set; }
    public Guid ProduitId { get; set; }
    public Guid? EmplacementId { get; set; }
    public Guid? SiteId { get; set; }
    public decimal Quantite { get; set; }
}
