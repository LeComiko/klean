using System.ComponentModel.DataAnnotations;

namespace Klean.Api.DTOs;

public class TypePrestationDto
{
    public Guid Id { get; set; }
    public Guid EntrepriseId { get; set; }
    public string Libelle { get; set; } = string.Empty;
    public int DureeParDefautMinutes { get; set; }
    public bool Actif { get; set; }
}

public class TypePrestationCreateDto
{
    [Required]
    [MaxLength(200)]
    public string Libelle { get; set; } = string.Empty;

    /// <summary>Mêmes bornes que <c>PrestationCreateDto.DureeEstimeeMinutes</c>, qu'elle préremplit.</summary>
    [Range(1, 1440)]
    public int DureeParDefautMinutes { get; set; }

    public bool Actif { get; set; } = true;
}
