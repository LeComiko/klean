using System.ComponentModel.DataAnnotations;

namespace Klean.Api.Models;

/// <summary>
/// Consommable ou matériel. Porte naturellement son <see cref="EntrepriseId"/>,
/// avec clé étrangère.
/// </summary>
public class Produit
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid EntrepriseId { get; set; }
    public Entreprise? Entreprise { get; set; }

    [Required]
    [MaxLength(200)]
    public string Nom { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Reference { get; set; }

    /// <summary>Unité de compte : litre, pièce, rouleau…</summary>
    [Required]
    [MaxLength(30)]
    public string Unite { get; set; } = string.Empty;

    /// <summary>Quantité sous laquelle le produit est signalé en alerte.</summary>
    public decimal SeuilAlerte { get; set; }

    /// <summary>
    /// Un produit désactivé ne reçoit plus d'entrée ; ce qui en reste peut encore sortir,
    /// circuler et se compter. C'est la façon de retirer un produit qui a servi : sa
    /// suppression est refusée dès qu'un mouvement le cite (SPEC.md, étape 22).
    /// </summary>
    public bool Actif { get; set; } = true;

    public ICollection<Stock> Stocks { get; set; } = new List<Stock>();
}
