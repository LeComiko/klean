using System.ComponentModel.DataAnnotations;

namespace Klean.Api.DTOs;

public class ConnexionDto
{

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string MotDePasse { get; set; } = string.Empty;
}

/// <summary>
/// Réponse d'une connexion ou d'un rafraîchissement.
///
/// ⚠️ <b>Ne contient aucun jeton, et ne doit jamais en contenir.</b> Le jeton
/// d'accès part en cookie <c>HttpOnly</c> : le remettre aussi dans le corps le
/// rendrait lisible par le script, ce qui annulerait exactement ce que le cookie
/// apporte. Le client n'a besoin que de savoir <b>qui</b> il est et <b>quand</b>
/// rafraîchir.
/// </summary>
public class SessionDto
{
    /// <summary>
    /// Expiration du jeton d'accès. Sert au client à rafraîchir avant d'échouer,
    /// plutôt qu'après.
    /// </summary>
    public DateTime Expiration { get; set; }

    public ProfilDto Profil { get; set; } = new();
}

public class ProfilDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Nom { get; set; } = string.Empty;
    public string Prenom { get; set; } = string.Empty;
    public Guid EntrepriseId { get; set; }
    public Guid? AgentId { get; set; }
    public Guid? ClientId { get; set; }
    public bool Actif { get; set; }
    public IEnumerable<string> Roles { get; set; } = [];
}

public class CompteCreateDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>10 caractères minimum, une majuscule, une minuscule, un chiffre.</summary>
    [Required]
    [MinLength(10)]
    public string MotDePasse { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Nom { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Prenom { get; set; } = string.Empty;

    /// <summary>Un des cinq rôles de RolesKlean.</summary>
    [Required]
    public string Role { get; set; } = string.Empty;

    /// <summary>Obligatoire pour un compte de rôle Agent.</summary>
    public Guid? AgentId { get; set; }

    /// <summary>Obligatoire pour un compte de rôle ClientSyndic.</summary>
    public Guid? ClientId { get; set; }
}
