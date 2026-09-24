using System.ComponentModel.DataAnnotations;

namespace Klean.Api.DTOs;

public class RemplacementDto
{
    public Guid Id { get; set; }
    public Guid EntrepriseId { get; set; }
    public Guid InterventionId { get; set; }

    /// <summary>Null quand la vacation n'a jamais eu d'agent affecté.</summary>
    public Guid? AbsenceId { get; set; }

    /// <summary>Null tant qu'aucun agent n'a été proposé.</summary>
    public Guid? AgentRemplacantId { get; set; }

    /// <summary>Valeurs : Recherche, Propose, Accepte, Refuse, Annule.</summary>
    public string Statut { get; set; } = string.Empty;

    /// <summary>Score du candidat au moment de la proposition, sur 100.</summary>
    public double? Score { get; set; }

    public DateTime DateCreation { get; set; }
}

/// <summary>
/// CRUD brut, destiné à l'administration et au diagnostic. Les transitions
/// métier gardées — proposer, accepter, refuser — arrivent à l'étape 8 dans
/// <c>RemplacementService</c> : c'est par là que passe le cycle normal,
/// pas par ce PUT (CLAUDE.md §9).
/// </summary>
public class RemplacementCreateDto
{
    public Guid EntrepriseId { get; set; }

    [Required]
    public Guid InterventionId { get; set; }

    public Guid? AbsenceId { get; set; }

    public Guid? AgentRemplacantId { get; set; }

    [Required]
    public string Statut { get; set; } = nameof(Klean.Api.Models.StatutRemplacement.Recherche);

    [Range(0, 100)]
    public double? Score { get; set; }
}
