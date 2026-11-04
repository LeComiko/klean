using System.ComponentModel.DataAnnotations;

namespace Klean.Api.Models;

/// <summary>Où un stock peut se trouver hors d'un site.</summary>
public enum TypeEmplacement
{
    Depot,
    Vehicule
}

/// <summary>
/// Un dépôt ou un véhicule de l'entreprise (SPEC.md, étape 22). Porte naturellement son
/// <see cref="EntrepriseId"/>, avec clé étrangère.
///
/// ⚠️ Le site n'en est pas un : il existe déjà, et un stock posé sur un site le vise par
/// <see cref="Stock.SiteId"/>. Jusqu'à l'étape 22, dépôts et véhicules n'existaient pas du
/// tout — un stock « au dépôt » pointait vers un <c>Guid</c> que rien ne nommait.
/// </summary>
public class Emplacement
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid EntrepriseId { get; set; }
    public Entreprise? Entreprise { get; set; }

    /// <summary>« Dépôt central », « Kangoo AB-123-CD ».</summary>
    [Required]
    [MaxLength(200)]
    public string Nom { get; set; } = string.Empty;

    [Required]
    public TypeEmplacement Type { get; set; }

    /// <summary>
    /// Un emplacement désactivé ne reçoit plus rien ; ce qui s'y trouve peut encore en
    /// sortir. C'est la façon de retirer un véhicule vendu : sa suppression est refusée dès
    /// qu'un mouvement le cite, l'historique en dépendant.
    /// </summary>
    public bool Actif { get; set; } = true;
}
