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

    /// <summary>
    /// La localisation des sites par la Géoplateforme de l'IGN (0.3.0) : désactivée par défaut,
    /// activée par l'administrateur dans l'écran Entreprise. Elle envoie la voie, le code postal et
    /// la commune d'un site, à chaque clic sur « Localiser » (RGPD.md, T6).
    /// </summary>
    public bool GeocodageActif { get; set; }

    /// <summary>
    /// Posées par le serveur au changement d'état, jamais reçues : elles répondent à « mon adresse
    /// est-elle partie, et quand ? », que le clic, qui n'enregistre rien, laisse sans trace
    /// (RGPD.md §6). Le dernier passage dans chaque sens, pas l'historique.
    /// </summary>
    public DateTime? GeocodageActiveLe { get; set; }
    public DateTime? GeocodageDesactiveLe { get; set; }

    public ICollection<Client> Clients { get; set; } = new List<Client>();
    public ICollection<Agent> Agents { get; set; } = new List<Agent>();
    public ICollection<Produit> Produits { get; set; } = new List<Produit>();
}
