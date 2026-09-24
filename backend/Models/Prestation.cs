using System.ComponentModel.DataAnnotations;

namespace Klean.Api.Models;

/// <summary>
/// Tâche récurrente prévue par un contrat. <see cref="EntrepriseId"/> dénormalisé,
/// sans clé étrangère.
/// </summary>
public class Prestation
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid EntrepriseId { get; set; }

    [Required]
    public Guid ContratId { get; set; }
    public Contrat? Contrat { get; set; }

    [Required]
    [MaxLength(200)]
    public string Libelle { get; set; } = string.Empty;

    /// <summary>Alimente le calcul de charge hebdomadaire du moteur d'affectation.</summary>
    public int DureeEstimeeMinutes { get; set; }

    public ICollection<Intervention> Interventions { get; set; } = new List<Intervention>();
}
