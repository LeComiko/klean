using System.ComponentModel.DataAnnotations;

namespace Klean.Api.DTOs;

public class PrestationDto
{
    public Guid Id { get; set; }
    public Guid EntrepriseId { get; set; }
    public Guid ContratId { get; set; }
    public string Libelle { get; set; } = string.Empty;
    public int DureeEstimeeMinutes { get; set; }
}

public class PrestationCreateDto
{
    [Required]
    public Guid ContratId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Libelle { get; set; } = string.Empty;

    /// <summary>Alimente le calcul de charge hebdomadaire du moteur d'affectation.</summary>
    [Range(1, 1440)]
    public int DureeEstimeeMinutes { get; set; }
}
