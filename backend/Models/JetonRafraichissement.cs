using System.ComponentModel.DataAnnotations;

namespace Klean.Api.Models;

/// <summary>
/// Jeton de rafraîchissement, persisté pour être <b>révocable</b>. C'est ce que
/// le jeton d'accès de quinze minutes ne peut pas être : un JWT signé vaut
/// jusqu'à son expiration, quoi qu'il arrive côté serveur.
///
/// Seule l'empreinte est stockée, jamais la valeur. Une fuite de la table ne
/// permet donc pas de rejouer les sessions — c'est le même raisonnement que pour
/// un mot de passe, et la table est tout aussi sensible.
///
/// ⚠️ Pas de colonne <c>EntrepriseId</c>, et donc <b>aucun filtre global</b>.
/// C'est délibéré, et la raison est la même que pour <c>AspNetUsers</c> : le
/// rafraîchissement s'exécute <b>sans requête authentifiée</b> — le jeton d'accès
/// vient justement d'expirer — donc aucun tenant n'est disponible pour filtrer.
/// L'entreprise se lit sur l'utilisateur rattaché. La recherche se fait sur une
/// empreinte de 256 bits, qui n'est pas devinable : c'est elle qui tient lieu de
/// cadrage.
/// </summary>
public class JetonRafraichissement
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid UtilisateurId { get; set; }
    public Utilisateur? Utilisateur { get; set; }

    /// <summary>SHA-256 de la valeur remise au client, en base64. Index unique.</summary>
    [Required]
    [MaxLength(64)]
    public string Empreinte { get; set; } = string.Empty;

    public DateTime DateCreation { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Fin de session absolue. La rotation la <b>reporte telle quelle</b> sur le
    /// jeton suivant : une session ne se prolonge donc pas indéfiniment par
    /// simple activité, elle dure au plus quatorze jours.
    /// </summary>
    public DateTime DateExpiration { get; set; }

    /// <summary>Renseignée dès que le jeton a servi, été remplacé, ou été révoqué.</summary>
    public DateTime? DateRevocation { get; set; }

    /// <summary>
    /// Jeton émis en remplacement lors de la rotation. Rend la chaîne d'une
    /// session lisible, ce qui sert au diagnostic d'un rejeu.
    /// </summary>
    public Guid? RemplacePar { get; set; }

    /// <summary>Pourquoi le jeton a été révoqué : rotation, déconnexion, rejeu détecté…</summary>
    [MaxLength(100)]
    public string? MotifRevocation { get; set; }

    [MaxLength(64)]
    public string? AdresseIp { get; set; }

    public bool EstActif(DateTime maintenant) =>
        DateRevocation is null && DateExpiration > maintenant;
}
