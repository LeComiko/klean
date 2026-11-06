using System.ComponentModel.DataAnnotations;

namespace Klean.Api.DTOs;

public class PointControleDto
{
    public Guid Id { get; set; }
    public Guid EntrepriseId { get; set; }
    public Guid TypePrestationId { get; set; }
    public string Libelle { get; set; } = string.Empty;
    public int Ordre { get; set; }
    public bool Actif { get; set; }
}

public class PointControleCreateDto
{
    public Guid TypePrestationId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Libelle { get; set; } = string.Empty;

    [Range(0, 999)]
    public int Ordre { get; set; }

    public bool Actif { get; set; } = true;
}
