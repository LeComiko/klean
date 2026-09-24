using System.ComponentModel.DataAnnotations;

namespace Klean.Api.DTOs;

public class ProduitDto
{
    public Guid Id { get; set; }
    public Guid EntrepriseId { get; set; }
    public string Nom { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string Unite { get; set; } = string.Empty;
    public decimal SeuilAlerte { get; set; }
}

public class ProduitCreateDto
{
    public Guid EntrepriseId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Nom { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Reference { get; set; }

    /// <summary>Unité de compte : litre, pièce, rouleau…</summary>
    [Required]
    [MaxLength(30)]
    public string Unite { get; set; } = string.Empty;

    [Range(0, 999999.99)]
    public decimal SeuilAlerte { get; set; }
}
