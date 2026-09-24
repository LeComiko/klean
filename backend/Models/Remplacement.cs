using System.ComponentModel.DataAnnotations;

namespace Klean.Api.Models;

public enum StatutRemplacement
{
    Recherche,
    Propose,
    Accepte,
    Refuse,
    Annule
}

/// <summary>
/// Recherche de remplaçant pour une intervention. Machine à états stricte,
/// gardée par <c>RemplacementService</c> (CLAUDE.md §9).
/// <see cref="EntrepriseId"/> dénormalisé, sans clé étrangère.
/// </summary>
public class Remplacement
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid EntrepriseId { get; set; }

    [Required]
    public Guid InterventionId { get; set; }
    public Intervention? Intervention { get; set; }

    /// <summary>
    /// Null quand la vacation n'a jamais eu d'agent affecté : il y a un trou à
    /// combler sans absence pour l'expliquer.
    /// </summary>
    public Guid? AbsenceId { get; set; }
    public Absence? Absence { get; set; }

    /// <summary>Null tant qu'aucun agent n'a été proposé.</summary>
    public Guid? AgentRemplacantId { get; set; }
    public Agent? AgentRemplacant { get; set; }

    [Required]
    public StatutRemplacement Statut { get; set; } = StatutRemplacement.Recherche;

    /// <summary>
    /// Score du candidat au moment de la proposition, sur 100. Null en statut
    /// <see cref="StatutRemplacement.Recherche"/>.
    /// </summary>
    public double? Score { get; set; }

    public DateTime DateCreation { get; set; } = DateTime.UtcNow;
}
