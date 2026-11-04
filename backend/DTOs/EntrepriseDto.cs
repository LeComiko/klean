using System.ComponentModel.DataAnnotations;

namespace Klean.Api.DTOs;

public class EntrepriseDto
{
    public Guid Id { get; set; }
    public string RaisonSociale { get; set; } = string.Empty;
    public string Siret { get; set; } = string.Empty;
    public DateTime DateCreation { get; set; }

    /// <summary>Valeurs : Metropole, AlsaceMoselle, Guadeloupe, Martinique, Guyane, Reunion, Mayotte.</summary>
    public string Territoire { get; set; } = string.Empty;
}

/// <summary>
/// Création et mise à jour. <c>DateCreation</c> est absente volontairement :
/// elle est posée par le serveur à la création et jamais modifiée ensuite.
/// </summary>
public class EntrepriseCreateDto
{
    [Required]
    [MaxLength(200)]
    public string RaisonSociale { get; set; } = string.Empty;

    [Required]
    [MaxLength(14)]
    public string Siret { get; set; } = string.Empty;

    /// <summary>
    /// Le nom exact du territoire. ⚠️ Pas de défaut : un <c>PUT</c> qui l'omet est refusé,
    /// plutôt que de ramener en silence une entreprise d'Alsace-Moselle en Métropole — et
    /// de lui faire travailler son Vendredi saint.
    /// </summary>
    public string Territoire { get; set; } = string.Empty;
}
