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

    /// <summary>
    /// Date de la pseudonymisation du compte, nulle tant qu'elle n'a pas eu lieu
    /// (RGPD.md §9.3).
    ///
    /// ⚠️ Une <b>date</b> et non un drapeau, pour la même raison que sur
    /// <see cref="Agent.DateAnonymisation"/> : elle répond à la question du registre des
    /// traitements — « quand cet effacement a-t-il été exécuté » — qu'un booléen laisse
    /// sans réponse, et elle rend l'opération refusable en 409 sur un compte déjà traité.
    ///
    /// ⚠️ Un compte rattaché à un agent ne passe <b>pas</b> par ici : il est neutralisé
    /// par <c>POST /api/agents/{id}/anonymiser</c>, qui traite les trois endroits où la
    /// personne est nommée. Cette colonne sert les comptes <b>sans</b> fiche agent —
    /// Responsable, Administrateur, ClientSyndic — qui n'avaient aucun chemin
    /// d'effacement, et ce sont les personnes les mieux placées pour s'en apercevoir.
    /// </summary>
    public DateTime? DateAnonymisation { get; set; }
}
