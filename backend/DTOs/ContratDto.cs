using System.ComponentModel.DataAnnotations;

namespace Klean.Api.DTOs;

public class ContratDto
{
    public Guid Id { get; set; }
    public Guid EntrepriseId { get; set; }
    public Guid SiteId { get; set; }
    public DateTime DateDebut { get; set; }
    public DateTime? DateFin { get; set; }
    public string Frequence { get; set; } = string.Empty;
    public bool Actif { get; set; }
    public bool InclutJoursFeries { get; set; }
}

public class ContratCreateDto
{
    [Required]
    public Guid SiteId { get; set; }

    public DateTime DateDebut { get; set; }

    /// <summary>Null tant que le contrat court sans échéance connue.</summary>
    public DateTime? DateFin { get; set; }

    /// <summary>Récurrence en clair, par exemple « Lundi, mercredi, vendredi ».</summary>
    [Required]
    [MaxLength(100)]
    public string Frequence { get; set; } = string.Empty;

    public bool Actif { get; set; } = true;

    /// <summary>La génération crée-t-elle des vacations les jours fériés ? Non par défaut.</summary>
    public bool InclutJoursFeries { get; set; }
}
