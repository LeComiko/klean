using System.ComponentModel.DataAnnotations;

namespace Klean.Api.DTOs;

public class AbsenceDto
{
    public Guid Id { get; set; }
    public Guid EntrepriseId { get; set; }
    public Guid AgentId { get; set; }

    /// <summary>Valeurs : Maladie, Conge, Indisponibilite, Retard, Autre.</summary>
    public string Type { get; set; } = string.Empty;

    public DateTime DateDebut { get; set; }
    public DateTime DateFin { get; set; }
    public string? Motif { get; set; }
}

public class AbsenceCreateDto
{
    [Required]
    public Guid AgentId { get; set; }

    [Required]
    public string Type { get; set; } = string.Empty;

    public DateTime DateDebut { get; set; }
    public DateTime DateFin { get; set; }

    [MaxLength(500)]
    public string? Motif { get; set; }
}
