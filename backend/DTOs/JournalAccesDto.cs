namespace Klean.Api.DTOs;

/// <summary>
/// Une ligne du journal d'accès, telle qu'un administrateur la lit.
///
/// ⚠️ Rien de ce qui a été lu n'y figure, parce que rien n'en est stocké. Le journal
/// répond « qui, quelle ressource, quand, combien de lignes » — jamais « quoi ».
/// Voir <see cref="Klean.Api.Models.AccesJournalise"/>.
/// </summary>
public class JournalAccesDto
{
    public Guid Id { get; set; }
    public Guid EntrepriseId { get; set; }

    /// <summary>
    /// L'auteur, par son identifiant seul.
    ///
    /// ⚠️ Volontairement non résolu en nom ni en adresse : l'écran qui lira ce journal
    /// fera la correspondance s'il en a le droit, et un compte effacé laisse ici un
    /// identifiant orphelin — ce qui est le comportement voulu. Résoudre côté serveur
    /// ferait du journal un second annuaire des comptes.
    /// </summary>
    public Guid UtilisateurId { get; set; }

    /// <summary>Valeurs : Agent, Absence, Candidats, Planning, JournalAcces.</summary>
    public string Ressource { get; set; } = string.Empty;

    /// <summary>Valeurs : Liste, Lecture, Anonymisation.</summary>
    public string Nature { get; set; } = string.Empty;

    /// <summary>La ligne visée, ou la vacation dont les candidats ont été listés.</summary>
    public Guid? CibleId { get; set; }

    public int Lignes { get; set; }

    public DateTime Horodatage { get; set; }
}
