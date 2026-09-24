using System.ComponentModel.DataAnnotations;

namespace Klean.Api.DTOs;

public class EntrepriseDto
{
    public Guid Id { get; set; }
    public string RaisonSociale { get; set; } = string.Empty;
    public string Siret { get; set; } = string.Empty;
    public DateTime DateCreation { get; set; }
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
}
