using System.ComponentModel.DataAnnotations;

namespace Klean.Api.Models;

public enum TypeAbsence
{
    Maladie,
    Conge,
    Indisponibilite,
    Retard,
    Autre
}

/// <summary>
/// Indisponibilité d'un agent. Sa création déclenche la détection d'impacts
/// (CLAUDE.md §9). <see cref="EntrepriseId"/> dénormalisé, sans clé étrangère.
/// </summary>
public class Absence
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid EntrepriseId { get; set; }

    [Required]
    public Guid AgentId { get; set; }
    public Agent? Agent { get; set; }

    [Required]
    public TypeAbsence Type { get; set; }

    public DateTime DateDebut { get; set; }

    public DateTime DateFin { get; set; }

    [MaxLength(500)]
    public string? Motif { get; set; }

    public ICollection<Remplacement> Remplacements { get; set; } = new List<Remplacement>();
}
