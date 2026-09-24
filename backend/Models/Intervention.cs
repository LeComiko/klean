using System.ComponentModel.DataAnnotations;

namespace Klean.Api.Models;

public enum StatutIntervention
{
    Planifiee,
    EnCours,
    Terminee,
    NonCertifiee,
    Annulee
}

/// <summary>
/// Passage daté d'un agent sur un site. Pivot du planning et du remplacement.
/// <see cref="EntrepriseId"/> dénormalisé, sans clé étrangère.
/// </summary>
public class Intervention
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid EntrepriseId { get; set; }

    [Required]
    public Guid PrestationId { get; set; }
    public Prestation? Prestation { get; set; }

    /// <summary>
    /// Null = vacation non pourvue. Seul <c>RemplacementService.AccepterAsync</c>
    /// est autorisé à changer cette valeur (CLAUDE.md §9).
    /// </summary>
    public Guid? AgentId { get; set; }
    public Agent? Agent { get; set; }

    public DateTime DatePrevue { get; set; }

    public TimeSpan HeurePrevue { get; set; }

    [Required]
    public StatutIntervention Statut { get; set; } = StatutIntervention.Planifiee;

    public ICollection<Remplacement> Remplacements { get; set; } = new List<Remplacement>();
}
