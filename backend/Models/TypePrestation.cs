using System.ComponentModel.DataAnnotations;

namespace Klean.Api.Models;

/// <summary>
/// Un type du catalogue des prestations de l'entreprise — « Hall d'entrée »,
/// « Sortie des conteneurs » (SPEC.md, étape 21). Porte naturellement son
/// <see cref="EntrepriseId"/>, avec clé étrangère : chaque entreprise a son catalogue.
///
/// Il porte ce qui est vrai partout, et rien d'autre : un libellé et une durée par
/// défaut. Les jours et l'heure dépendent du site, ils restent sur la prestation.
/// </summary>
public class TypePrestation
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid EntrepriseId { get; set; }
    public Entreprise? Entreprise { get; set; }

    /// <summary>
    /// Unique dans l'entreprise, à la casse près : l'import de la liste type s'y fie pour
    /// ne rien recréer, et deux « Hall d'entrée » rendraient le choix illisible.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Libelle { get; set; } = string.Empty;

    /// <summary>Proposée à la création d'une prestation, qui reste libre de la changer.</summary>
    public int DureeParDefautMinutes { get; set; }

    /// <summary>
    /// Un type désactivé n'est plus proposé à la saisie, et les prestations qui le portent
    /// le gardent. C'est la façon de retirer un type utilisé : sa suppression est refusée.
    /// </summary>
    public bool Actif { get; set; } = true;
}
