namespace Klean.Api.DTOs;

/// <summary>
/// Réponse de <c>GET /api/stocks/prevision</c> : le besoin des semaines à venir, d'après le
/// planning, et ce que le stock en couvre (SPEC.md, étape 22, lot B).
///
/// Des identifiants, jamais un nom : l'écran a déjà les produits et les sites, et le DTO
/// reste hors du journal d'accès sans exemption à écrire.
/// </summary>
public class PrevisionStockDto
{
    /// <summary>Premier jour compté : aujourd'hui.</summary>
    public DateTime Du { get; set; }

    /// <summary>Dernier jour compté, inclus.</summary>
    public DateTime Au { get; set; }

    public int Semaines { get; set; }

    /// <summary>
    /// Vacations de la période dont la prestation n'a pas de type : elles ne consomment
    /// rien, faute de consommation à leur attacher. Le nombre est là pour que l'écran le
    /// dise — un besoin sous-estimé en silence ressemblerait à un stock suffisant.
    /// </summary>
    public int VacationsSansType { get; set; }

    /// <summary>Les produits actifs, ceux en alerte d'abord.</summary>
    public List<PrevisionProduitDto> Produits { get; set; } = [];
}

public class PrevisionProduitDto
{
    public Guid ProduitId { get; set; }

    public decimal SeuilAlerte { get; set; }

    /// <summary>Tous lieux confondus : dépôts, véhicules et sites.</summary>
    public decimal StockTotal { get; set; }

    /// <summary>Dépôts et véhicules : ce qui peut partir vers n'importe quel site.</summary>
    public decimal StockReserve { get; set; }

    /// <summary>Vacations planifiées de la période × consommation de leur type.</summary>
    public decimal Besoin { get; set; }

    /// <summary>
    /// Ce que la réserve ne couvre pas, une fois que chaque site a puisé dans son propre
    /// stock. ⚠️ L'excédent d'un site ne compte pas pour un autre : il n'y est pas.
    /// </summary>
    public decimal Manque { get; set; }

    /// <summary>Stock total strictement sous <see cref="SeuilAlerte"/>.</summary>
    public bool SousLeSeuil { get; set; }

    /// <summary><see cref="Manque"/> positif : le stock ne tient pas la période.</summary>
    public bool CouvertureCourte { get; set; }

    /// <summary>Les sites qui ont un besoin sur la période, avec ce qu'ils ont sur place.</summary>
    public List<PrevisionSiteDto> Sites { get; set; } = [];
}

public class PrevisionSiteDto
{
    public Guid SiteId { get; set; }
    public decimal Besoin { get; set; }
    public decimal Stock { get; set; }

    /// <summary>Besoin moins stock du site, jamais négatif : ce qu'il faut lui apporter.</summary>
    public decimal AApporter { get; set; }
}
