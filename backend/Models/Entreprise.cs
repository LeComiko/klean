using System.ComponentModel.DataAnnotations;

namespace Klean.Api.Models;

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

    public ICollection<Client> Clients { get; set; } = new List<Client>();
    public ICollection<Agent> Agents { get; set; } = new List<Agent>();
    public ICollection<Produit> Produits { get; set; } = new List<Produit>();
}
