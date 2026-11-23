using System.ComponentModel.DataAnnotations;

namespace Klean.Api.DTOs;

public class SiteDto
{
    public Guid Id { get; set; }
    public Guid EntrepriseId { get; set; }
    public Guid ClientId { get; set; }
    public string Nom { get; set; } = string.Empty;
    public string Adresse { get; set; } = string.Empty;
    public string? CodePostal { get; set; }
    public string? Ville { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Consignes { get; set; }
    public DateTime DateCreation { get; set; }
}

/// <summary>
/// Création et mise à jour. <c>DateCreation</c> est absente volontairement :
/// elle est posée par le serveur à la création et jamais modifiée ensuite.
/// </summary>
public class SiteCreateDto
{
    [Required]
    public Guid ClientId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Nom { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Adresse { get; set; } = string.Empty;

    [MaxLength(10)]
    public string? CodePostal { get; set; }

    [MaxLength(100)]
    public string? Ville { get; set; }

    /// <summary>Requise : le moteur d'affectation calcule une distance depuis le site.</summary>
    [Range(-90, 90)]
    public double Latitude { get; set; }

    [Range(-180, 180)]
    public double Longitude { get; set; }

    public string? Consignes { get; set; }
}

/// <summary>
/// Ce que « Localiser » envoie (0.3.0) : les champs d'adresse d'un site, jamais une chaîne libre.
/// ⚠️ Des <c>string?</c> sans annotation : avec <c>Nullable</c> activé, un <c>string</c> rendrait un
/// <c>null</c> JSON en 400 anglais, avant le contrôleur (CLAUDE.md §10). Le contrôleur vérifie tout.
/// </summary>
public class LocaliserSiteDto
{
    public string? Adresse { get; set; }
    public string? CodePostal { get; set; }
    public string? Ville { get; set; }
}

/// <summary>
/// La réponse d'une localisation. ⚠️ Porte <c>Latitude</c> et <c>Longitude</c>, que surveille le filet de
/// <c>JournalAccesTests</c> ; la route n'est pas journalisée, et c'est une décision (CLAUDE.md §7 bis) :
/// rien n'est lu en base, et la réponse est une donnée publique de référence sur l'adresse envoyée.
/// </summary>
public class LocalisationDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string AdresseTrouvee { get; set; } = string.Empty;
    public bool AVerifier { get; set; }
}
