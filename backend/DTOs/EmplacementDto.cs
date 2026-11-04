using System.ComponentModel.DataAnnotations;

namespace Klean.Api.DTOs;

public class EmplacementDto
{
    public Guid Id { get; set; }
    public Guid EntrepriseId { get; set; }
    public string Nom { get; set; } = string.Empty;

    /// <summary>Valeurs : Depot, Vehicule.</summary>
    public string Type { get; set; } = string.Empty;

    public bool Actif { get; set; }
}

public class EmplacementCreateDto
{
    [Required]
    [MaxLength(200)]
    public string Nom { get; set; } = string.Empty;

    /// <summary>Valeurs : Depot, Vehicule. Un site n'est pas un emplacement : il existe déjà.</summary>
    [Required]
    public string Type { get; set; } = string.Empty;

    public bool Actif { get; set; } = true;
}
