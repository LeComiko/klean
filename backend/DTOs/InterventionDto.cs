using System.ComponentModel.DataAnnotations;

namespace Klean.Api.DTOs;

public class InterventionDto
{
    public Guid Id { get; set; }
    public Guid EntrepriseId { get; set; }
    public Guid PrestationId { get; set; }

    /// <summary>Null = vacation non pourvue.</summary>
    public Guid? AgentId { get; set; }

    public DateTime DatePrevue { get; set; }

    /// <summary>Sérialisé en « HH:MM:SS », par exemple « 08:30:00 ».</summary>
    public TimeSpan HeurePrevue { get; set; }

    /// <summary>Valeurs : Planifiee, EnCours, Terminee, NonCertifiee, Annulee.</summary>
    public string Statut { get; set; } = string.Empty;
}

public class InterventionCreateDto
{
    [Required]
    public Guid PrestationId { get; set; }

    /// <summary>
    /// Null pour une vacation non pourvue. Attention : en dehors de ce CRUD,
    /// seul <c>RemplacementService.AccepterAsync</c> doit changer cette valeur
    /// (CLAUDE.md §9).
    /// </summary>
    public Guid? AgentId { get; set; }

    public DateTime DatePrevue { get; set; }

    public TimeSpan HeurePrevue { get; set; }

    [Required]
    public string Statut { get; set; } = nameof(Klean.Api.Models.StatutIntervention.Planifiee);
}
