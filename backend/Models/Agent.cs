using System.ComponentModel.DataAnnotations;

namespace Klean.Api.Models;

/// <summary>
/// Agent de propreté. Porte naturellement son <see cref="EntrepriseId"/>,
/// avec clé étrangère.
/// </summary>
public class Agent
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid EntrepriseId { get; set; }
    public Entreprise? Entreprise { get; set; }

    [Required]
    [MaxLength(100)]
    public string Nom { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Prenom { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Email { get; set; }

    [MaxLength(30)]
    public string? Telephone { get; set; }

    [MaxLength(200)]
    public string? Adresse { get; set; }

    /// <summary>
    /// Nullable : un agent sans coordonnées reçoit la note de distance neutre
    /// de 50 sur 100 (CLAUDE.md §8).
    /// </summary>
    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public DateTime DateEmbauche { get; set; }

    /// <summary>Filtre dur du moteur d'affectation : un agent inactif n'est jamais candidat.</summary>
    public bool Actif { get; set; } = true;

    public ICollection<Absence> Absences { get; set; } = new List<Absence>();
    public ICollection<Intervention> Interventions { get; set; } = new List<Intervention>();
}
