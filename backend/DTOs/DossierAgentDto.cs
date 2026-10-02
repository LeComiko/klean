namespace Klean.Api.DTOs;

/// <summary>
/// Tout ce que KLEAN détient sur un agent, en une réponse (RGPD.md §9.4).
///
/// Sert les deux droits que le produit ne servait pas : l'<b>accès</b> de l'article 15,
/// qui exige une forme intelligible, et la <b>portabilité</b> de l'article 20, qui exige
/// un format structuré et lisible par machine. Avant, un administrateur devait composer
/// la réponse à partir de cinq appels, et rien ne garantissait qu'il n'en oubliait pas un.
///
/// ⚠️ <b>Ce dossier est une divulgation massive de données personnelles, par
/// construction.</b> C'est sa raison d'être, et c'est pourquoi il est réservé à
/// l'administrateur et journalisé en <see cref="Models.NatureAcces.Export"/> — une
/// nature distincte de la lecture, parce que sortir le dossier complet d'une personne
/// n'est pas le même geste que consulter sa fiche.
/// </summary>
public class DossierAgentDto
{
    /// <summary>Horodatage de la composition. Une réponse à une demande d'accès est datée.</summary>
    public DateTime Genere { get; set; }

    public AgentDto Agent { get; set; } = new();

    /// <summary>
    /// Le compte de connexion rattaché, s'il en existe un. Nul pour un agent sans accès
    /// au produit — le cas le plus fréquent.
    ///
    /// ⚠️ Ne porte <b>jamais</b> l'empreinte du mot de passe. Une empreinte n'est pas
    /// communicable au titre de l'article 15 : elle n'apporte rien à la personne et sa
    /// divulgation affaiblirait le secret qu'elle protège.
    /// </summary>
    public CompteDuDossierDto? Compte { get; set; }

    public List<AbsenceDto> Absences { get; set; } = [];

    public List<InterventionDuDossierDto> Interventions { get; set; } = [];

    /// <summary>Remplacements où cet agent a été proposé ou retenu comme remplaçant.</summary>
    public List<RemplacementDuDossierDto> Remplacements { get; set; } = [];

    /// <summary>
    /// Qui a consulté les données de cet agent, <b>en nombre et par nature</b>.
    ///
    /// ⚠️ Des comptes, et non la liste des lecteurs, et ce n'est pas de la pudeur : les
    /// lignes du journal nomment des <b>collègues</b>. Les livrer identifierait des tiers
    /// dans une réponse destinée à une seule personne, ce que l'article 15(4) écarte
    /// expressément — le droit d'accès ne porte pas atteinte aux droits d'autrui. Le
    /// nombre dit que la traçabilité existe ; les identités appartiennent à la direction,
    /// par <c>GET /api/journal-acces</c>.
    /// </summary>
    public List<DecompteAccesDto> AccesALaFiche { get; set; } = [];

    /// <summary>
    /// Ce que le dossier ne contient pas, et pourquoi. Même raison d'être que
    /// <see cref="AnonymisationDto.Subsiste"/> : qui répond à une demande d'accès doit
    /// savoir ce qu'il transmet et ce qu'il retient, sans avoir à relire le code.
    /// </summary>
    public List<string> NonInclus { get; set; } = [];
}

public class CompteDuDossierDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Nom { get; set; } = string.Empty;
    public string Prenom { get; set; } = string.Empty;
    public string? Telephone { get; set; }
    public DateTime DateCreation { get; set; }
    public bool Actif { get; set; }
    public IEnumerable<string> Roles { get; set; } = [];
}

/// <summary>
/// Une vacation, telle qu'elle intéresse la personne concernée.
///
/// ⚠️ Porte le nom et la ville du site, là où <see cref="InterventionDto"/> ne porte que
/// des identifiants. L'article 15 exige une forme <b>intelligible</b> : une liste de
/// <c>PrestationId</c> en UUID ne dit rien à un salarié qui demande où il a travaillé.
/// </summary>
public class InterventionDuDossierDto
{
    public Guid Id { get; set; }
    public DateTime DatePrevue { get; set; }
    public TimeSpan HeurePrevue { get; set; }
    public string Statut { get; set; } = string.Empty;
    public string Prestation { get; set; } = string.Empty;
    public string Site { get; set; } = string.Empty;
    public string? Ville { get; set; }
}

public class RemplacementDuDossierDto
{
    public Guid Id { get; set; }
    public DateTime DateCreation { get; set; }
    public string Statut { get; set; } = string.Empty;

    /// <summary>
    /// Le score qui lui a été attribué, sur 100.
    ///
    /// ⚠️ Communiqué exprès. C'est une <b>évaluation</b> de la personne — un des trois
    /// critères de l'AIPD de RGPD.md §9.6 — et la lui cacher dans sa propre réponse
    /// d'accès serait exactement ce que l'article 15 interdit.
    /// </summary>
    public double? Score { get; set; }
}

public class DecompteAccesDto
{
    public string Nature { get; set; } = string.Empty;
    public int Acces { get; set; }
    public DateTime Premier { get; set; }
    public DateTime Dernier { get; set; }
}
