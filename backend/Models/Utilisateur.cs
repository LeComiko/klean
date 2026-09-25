using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace Klean.Api.Models;

/// <summary>
/// Compte de connexion. <see cref="EntrepriseId"/> est obligatoire : c'est elle
/// qui alimente la revendication « entrepriseId » du jeton, donc le multi-tenant.
/// </summary>
public class Utilisateur : IdentityUser<Guid>
{
    /// <summary>Obligatoire, quel que soit le rôle.</summary>
    public Guid EntrepriseId { get; set; }

    /// <summary>Renseigné pour un compte de rôle Agent, nul sinon.</summary>
    public Guid? AgentId { get; set; }

    /// <summary>Renseigné pour un compte de rôle ClientSyndic, nul sinon.</summary>
    public Guid? ClientId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Nom { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Prenom { get; set; } = string.Empty;

    public DateTime DateCreation { get; set; } = DateTime.UtcNow;

    public bool Actif { get; set; } = true;
}
