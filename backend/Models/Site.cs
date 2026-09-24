using System.ComponentModel.DataAnnotations;

namespace Klean.Api.Models;

/// <summary>
/// Lieu d'intervention. <see cref="EntrepriseId"/> est dénormalisé,
/// volontairement sans clé étrangère (CLAUDE.md §7).
/// </summary>
public class Site
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid EntrepriseId { get; set; }

    [Required]
    public Guid ClientId { get; set; }
    public Client? Client { get; set; }

    [Required]
    [MaxLength(200)]
    public string Nom { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Adresse { get; set; } = string.Empty;

    [MaxLength(10)]
    public string? CodePostal { get; set; }

    [MaxLength(100)]
    public string? Ville { get; set; }

    /// <summary>Requise : le moteur d'affectation calcule une distance Haversine.</summary>
    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public string? Consignes { get; set; }

    public ICollection<Contrat> Contrats { get; set; } = new List<Contrat>();
}
