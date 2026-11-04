using System.ComponentModel.DataAnnotations;

namespace Klean.Api.Models;

/// <summary>
/// Ce qui est accédé, et qui le trace. Une valeur par endpoint divulguant des
/// données personnelles d'agent ou d'absence.
///
/// ⚠️ Les noms sont stockés <b>en texte</b> comme tous les enums du projet, et ils
/// apparaissent donc en base : les renommer demande une migration de données. Les
/// ajouts, en revanche, ne coûtent rien.
/// </summary>
public enum RessourceAccedee
{
    /// <summary>Nom, prénom, adresse, courriel, téléphone, coordonnées du domicile.</summary>
    Agent,

    /// <summary>Type, dates, motif. Un arrêt maladie est une donnée de santé.</summary>
    Absence,

    /// <summary>
    /// Nom, prénom, et <b>distance au site</b> — dérivée des coordonnées du domicile.
    /// Exposée par <c>/api/interventions/{id}/candidats</c> et
    /// <c>/api/remplacements/{id}/candidats</c>.
    /// </summary>
    Candidats,

    /// <summary>
    /// Nom et prénom de tous les agents de l'entreprise, et la cause
    /// « Agent absent » sur une vacation non pourvue : le planning <b>révèle une
    /// absence</b> sans qu'on lise la liste des absences.
    /// </summary>
    Planning,

    /// <summary>Le journal lui-même, qui est l'accès le plus sensible.</summary>
    JournalAcces,

    /// <summary>
    /// Un compte de connexion — nom, prénom, courriel, téléphone (RGPD.md §9.3).
    ///
    /// Deux usages. La pseudonymisation d'un compte, écriture irréversible ; et depuis
    /// l'étape 19, <c>GET /api/agents/chefs-equipe</c>, la seule route qui liste des
    /// comptes — nom et prénom des chefs d'équipe de l'entreprise.
    ///
    /// <c>GET /api/auth/moi</c> n'est pas journalisée, et ce n'est pas un oubli : elle ne
    /// rend que l'identité de l'appelant lui-même, et un journal de « qui a lu son propre
    /// nom » est du bruit.
    /// </summary>
    Compte,

    /// <summary>
    /// Nom et prénom des agents, sans rien d'autre — <c>GET /api/agents/choix</c>, qui
    /// remplit les listes déroulantes des prestations et des absences (étape 20).
    ///
    /// Distinct de <see cref="Agent"/> à dessein. Ouvrir l'écran des prestations n'est
    /// pas lire des fiches, et confondre les deux noierait les vraies lectures dans une
    /// ligne par ouverture d'écran. Distinct, le bruit se <b>filtre</b> ; exempté, il
    /// disparaîtrait avec la trace, alors que la liste des chefs, qui ne rend elle aussi
    /// que des noms, est journalisée.
    /// </summary>
    ChoixAgents
}

/// <summary>Nature de l'accès.</summary>
public enum NatureAcces
{
    /// <summary>Plusieurs lignes d'un coup. <see cref="AccesJournalise.Lignes"/> les compte.</summary>
    Liste,

    /// <summary>Une ligne nommément. <see cref="AccesJournalise.CibleId"/> la désigne.</summary>
    Lecture,

    /// <summary>Remplacement des données identifiantes par des valeurs neutres (étape 17, lot B).</summary>
    Anonymisation,

    /// <summary>
    /// Sortie du dossier complet d'une personne, pour répondre à une demande d'accès
    /// ou de portabilité (RGPD.md §9.4).
    ///
    /// ⚠️ Nature distincte de <see cref="Lecture"/>, et la distinction a un sens pour
    /// qui relit le journal : consulter une fiche est un geste d'exploitation ordinaire,
    /// sortir le dossier entier d'un salarié n'en est pas un. Les confondre ferait
    /// disparaître l'export dans le bruit des consultations.
    /// </summary>
    Export
}

/// <summary>
/// Une ligne du journal d'accès aux données personnelles (SPEC.md étape 17).
///
/// ⚠️ <b>Ce journal ne contient jamais ce qui a été lu.</b> C'est sa règle de
/// conception, pas une économie : un journal qui recopie les données qu'il surveille
/// devient une seconde base de données personnelles, moins protégée que la première,
/// et double le risque qu'il existe pour documenter. Il porte donc <b>qui</b>,
/// <b>quoi</b> au sens de la ressource, <b>quand</b>, et une quantité — jamais un
/// nom, une adresse, un motif d'absence ni une liste d'identifiants.
///
/// ⚠️ Aucune adresse IP non plus. Ce serait une donnée personnelle de plus, dont la
/// conservation demanderait sa propre justification, et elle n'ajoute rien à « qui a
/// lu quoi » dans un produit où chaque compte est nominatif.
///
/// Conservation : <b>douze mois</b>, purge par <c>Scripts/purge-journal-acces.sql</c>.
/// Un journal est lui-même une donnée personnelle, et le conserver sans limite
/// contredirait ce qu'il sert.
/// </summary>
public class AccesJournalise
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Dénormalisé comme ailleurs, sans clé étrangère (CLAUDE.md §7).</summary>
    [Required]
    public Guid EntrepriseId { get; set; }

    /// <summary>
    /// L'auteur, par son identifiant seul.
    ///
    /// ⚠️ Pas son adresse de courriel : elle est déjà dans <c>AspNetUsers</c>, et la
    /// recopier ici en ferait une donnée personnelle de plus à protéger et à purger.
    /// Un compte supprimé laisse donc un identifiant qui ne résout plus — c'est le
    /// comportement voulu, l'effacement d'une personne ne doit pas être empêché par le
    /// journal qui a observé ses lectures. Aucune clé étrangère pour la même raison.
    /// </summary>
    [Required]
    public Guid UtilisateurId { get; set; }

    [Required]
    public RessourceAccedee Ressource { get; set; }

    [Required]
    public NatureAcces Nature { get; set; }

    /// <summary>
    /// La ligne visée, sur une <see cref="NatureAcces.Lecture"/> ou une
    /// <see cref="NatureAcces.Anonymisation"/>. Null sur une liste.
    /// </summary>
    public Guid? CibleId { get; set; }

    /// <summary>
    /// Nombre de lignes divulguées.
    ///
    /// ⚠️ Un <b>compte</b>, pas la liste des identifiants. « A lu 150 agents » répond
    /// à la question d'un contrôle ; « a lu ces 150 identifiants » recopierait la table
    /// dans le journal, une page de liste à la fois.
    /// </summary>
    [Required]
    public int Lignes { get; set; }

    /// <summary>En UTC, comme toutes les dates du projet (CLAUDE.md §10).</summary>
    [Required]
    public DateTime Horodatage { get; set; }
}
