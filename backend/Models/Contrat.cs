using System.ComponentModel.DataAnnotations;

namespace Klean.Api.Models;

/// <summary>
/// Engagement de prestations sur un site. <see cref="EntrepriseId"/> dénormalisé,
/// sans clé étrangère.
/// </summary>
public class Contrat
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid EntrepriseId { get; set; }

    [Required]
    public Guid SiteId { get; set; }
    public Site? Site { get; set; }

    public DateTime DateDebut { get; set; }

    /// <summary>Null tant que le contrat court sans échéance connue.</summary>
    public DateTime? DateFin { get; set; }

    /// <summary>
    /// Récurrence en clair, par exemple « Lundi, mercredi, vendredi ».
    /// Pas un enum : aucune des cinq énumérations du modèle ne la couvre.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Frequence { get; set; } = string.Empty;

    public bool Actif { get; set; } = true;

    /// <summary>
    /// La génération crée-t-elle des vacations les jours fériés ? Non par défaut ;
    /// oui pour un site ouvert en continu — hôpital, résidence services.
    /// </summary>
    public bool InclutJoursFeries { get; set; }

    public ICollection<Prestation> Prestations { get; set; } = new List<Prestation>();
}
