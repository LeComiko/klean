namespace Klean.Api.DTOs;

/// <summary>Réponse de GET /api/installation : rien d'autre.</summary>
public sealed record EtatInstallationDto(bool Installee);

/// <summary>
/// Corps de POST /api/installation (SPEC.md, étape 34, lot A2).
/// ⚠️ <c>string?</c> partout, et aucune annotation : avec Nullable activé, un <c>string</c> non
/// nullable rend un <c>null</c> JSON en 400 anglais avant le contrôleur (mesuré, CLAUDE.md §10).
/// Tout est vérifié par le contrôleur, en français.
/// </summary>
public sealed class InstallationDto
{
    public string? Code { get; set; }
    public string? RaisonSociale { get; set; }
    public string? Siret { get; set; }
    public string? Territoire { get; set; }
    public string? Email { get; set; }
    public string? Nom { get; set; }
    public string? Prenom { get; set; }
    public string? MotDePasse { get; set; }
}
