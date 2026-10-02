namespace Klean.Api.DTOs;

/// <summary>
/// Compte rendu d'une pseudonymisation d'agent (SPEC.md étape 17).
///
/// ⚠️ Il dit ce qui a été fait et ce qui <b>reste</b>. La seconde moitié est la plus
/// utile : un compte rendu qui n'annonce que des succès laisse croire que l'effacement
/// est total, alors qu'il ne peut pas l'être — les interventions passées sont conservées
/// à dessein, et l'identifiant de l'agent avec elles.
///
/// Aucune donnée personnelle ici, seulement des comptes.
/// </summary>
public class AnonymisationDto
{
    public Guid AgentId { get; set; }

    public DateTime DateAnonymisation { get; set; }

    /// <summary>Pseudonyme substitué, pour que l'appelant sache quoi chercher ensuite.</summary>
    public string Pseudonyme { get; set; } = string.Empty;

    /// <summary>
    /// Interventions conservées, et c'est le cœur du compromis : <c>Restrict</c> sur
    /// <c>Intervention.AgentId</c> rend l'agent insupprimable dès qu'il a travaillé, ce
    /// qui est le bon choix pour l'intégrité du planning et entre en conflit direct avec
    /// une demande d'effacement. La pseudonymisation est la réponse à ce conflit.
    /// </summary>
    public int InterventionsConservees { get; set; }

    /// <summary>Absences conservées, dont les motifs en texte libre ont été effacés.</summary>
    public int AbsencesConservees { get; set; }

    public int MotifsEffaces { get; set; }

    /// <summary>
    /// Vrai si un compte de connexion était rattaché à cet agent et a été neutralisé :
    /// identité remplacée, compte désactivé, sessions révoquées.
    /// </summary>
    public bool CompteNeutralise { get; set; }

    public int SessionsRevoquees { get; set; }

    /// <summary>
    /// ⚠️ Ce qui subsiste, en clair. Lu par l'écran et destiné à être relu par qui
    /// répondra à une demande d'effacement : il doit savoir ce qu'il peut promettre.
    /// </summary>
    public List<string> Subsiste { get; set; } = [];
}
