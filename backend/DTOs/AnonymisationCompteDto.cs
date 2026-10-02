namespace Klean.Api.DTOs;

/// <summary>
/// Compte rendu de la pseudonymisation d'un <b>compte de connexion</b> (RGPD.md §9.3).
///
/// Distinct de <see cref="AnonymisationDto"/>, qui traite un agent et, avec lui, le
/// compte qui lui serait rattaché. Celui-ci sert les comptes <b>sans</b> fiche agent —
/// `Responsable`, `Administrateur`, `ClientSyndic` — qui n'avaient aucun chemin
/// d'effacement : leur nom, leur prénom et leur courriel restaient en base
/// indéfiniment, et il n'existait même pas de route pour les désactiver.
///
/// ⚠️ Ce n'est pas un oubli du lot B de l'étape 17, qui répondait à la demande
/// d'effacement d'un <b>agent</b>. C'était un trou du modèle de droits, et il concernait
/// les personnes les mieux placées pour s'en apercevoir.
///
/// Aucune donnée personnelle ici : des comptes, une date, et ce qui subsiste.
/// </summary>
public class AnonymisationCompteDto
{
    public Guid UtilisateurId { get; set; }

    public DateTime DateAnonymisation { get; set; }

    /// <summary>
    /// L'adresse substituée, sur un domaine non routable. Rendue pour que l'appelant
    /// sache quoi chercher ensuite — elle n'identifie plus personne.
    /// </summary>
    public string AdresseSubstituee { get; set; } = string.Empty;

    public int SessionsRevoquees { get; set; }

    /// <summary>
    /// Ce qui subsiste, en clair. Même raison d'être que sur la pseudonymisation d'un
    /// agent : qui répond à une demande d'effacement doit savoir ce qu'il peut promettre.
    /// </summary>
    public List<string> Subsiste { get; set; } = [];
}
