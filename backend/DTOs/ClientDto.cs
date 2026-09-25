using System.ComponentModel.DataAnnotations;

namespace Klean.Api.DTOs;

public class ClientDto
{
    public Guid Id { get; set; }
    public Guid EntrepriseId { get; set; }
    public string Nom { get; set; } = string.Empty;

    /// <summary>Valeurs : EntrepriseCliente, Syndic, GestionnaireImmobilier, Copropriete, Autre.</summary>
    public string Type { get; set; } = string.Empty;

    public string? Adresse { get; set; }
    public string? CodePostal { get; set; }
    public string? Ville { get; set; }
    public string? Email { get; set; }
    public string? Telephone { get; set; }
}

public class ClientCreateDto
{
    [Required]
    [MaxLength(200)]
    public string Nom { get; set; } = string.Empty;

    [Required]
    public string Type { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Adresse { get; set; }

    [MaxLength(10)]
    public string? CodePostal { get; set; }

    [MaxLength(100)]
    public string? Ville { get; set; }

    [MaxLength(200)]
    [EmailAddress]
    public string? Email { get; set; }

    [MaxLength(30)]
    public string? Telephone { get; set; }
}
