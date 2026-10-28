using System.ComponentModel.DataAnnotations;

namespace Klean.Api.DTOs;

public class SiteDto
{
    public Guid Id { get; set; }
    public Guid EntrepriseId { get; set; }
    public Guid ClientId { get; set; }
    public string Nom { get; set; } = string.Empty;
    public string Adresse { get; set; } = string.Empty;
    public string? CodePostal { get; set; }
    public string? Ville { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Consignes { get; set; }
    public DateTime DateCreation { get; set; }
}

/// <summary>
/// Création et mise à jour. <c>DateCreation</c> est absente volontairement :
/// elle est posée par le serveur à la création et jamais modifiée ensuite.
/// </summary>
public class SiteCreateDto
{
    [Required]
    public Guid ClientId { get; set; }

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

    /// <summary>Requise : le moteur d'affectation calcule une distance depuis le site.</summary>
    [Range(-90, 90)]
    public double Latitude { get; set; }

    [Range(-180, 180)]
    public double Longitude { get; set; }

    public string? Consignes { get; set; }
}
