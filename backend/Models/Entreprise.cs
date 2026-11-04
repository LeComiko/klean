using System.ComponentModel.DataAnnotations;

namespace Klean.Api.Models;

/// <summary>
/// Où travaille l'entreprise : il fixe le fuseau d'« aujourd'hui » et le calendrier des
/// jours fériés (<c>Territoires</c>, <c>JoursFeries</c>). Un par entreprise en V1 : une
/// entreprise présente sur deux territoires choisit le sien et saisit l'autre à la main.
/// </summary>
public enum Territoire
{
    Metropole,
    AlsaceMoselle,
    Guadeloupe,
    Martinique,
    Guyane,
    Reunion,
    Mayotte
}

/// <summary>
/// Le tenant. Son <see cref="Id"/> est la valeur portée par la revendication
/// « entrepriseId » du jeton JWT. Elle ne porte donc pas de colonne
/// EntrepriseId : ce serait une duplication de sa propre clé.
/// </summary>
public class Entreprise
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(200)]
    public string RaisonSociale { get; set; } = string.Empty;

    [Required]
    [MaxLength(14)]
    public string Siret { get; set; } = string.Empty;

    public DateTime DateCreation { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Métropole par défaut, et pour toute entreprise antérieure à la colonne : c'était
    /// le comportement d'avant, fuseau mis à part.
    /// </summary>
    public Territoire Territoire { get; set; } = Territoire.Metropole;

    public ICollection<Client> Clients { get; set; } = new List<Client>();
    public ICollection<Agent> Agents { get; set; } = new List<Agent>();
    public ICollection<Produit> Produits { get; set; } = new List<Produit>();
}
