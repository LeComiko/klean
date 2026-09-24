using System.ComponentModel.DataAnnotations;

namespace Klean.Api.DTOs;

public class AgentDto
{
    public Guid Id { get; set; }
    public Guid EntrepriseId { get; set; }
    public string Nom { get; set; } = string.Empty;
    public string Prenom { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Telephone { get; set; }
    public string? Adresse { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTime DateEmbauche { get; set; }
    public bool Actif { get; set; }
}

public class AgentCreateDto
{
    public Guid EntrepriseId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Nom { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Prenom { get; set; } = string.Empty;

    [MaxLength(200)]
    [EmailAddress]
    public string? Email { get; set; }

    [MaxLength(30)]
    public string? Telephone { get; set; }

    [MaxLength(200)]
    public string? Adresse { get; set; }

    /// <summary>Nullable : sans coordonnées, la note de distance vaut 50 sur 100.</summary>
    [Range(-90, 90)]
    public double? Latitude { get; set; }

    [Range(-180, 180)]
    public double? Longitude { get; set; }

    public DateTime DateEmbauche { get; set; }

    public bool Actif { get; set; } = true;
}
