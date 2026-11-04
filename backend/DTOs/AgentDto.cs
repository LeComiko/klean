using System.ComponentModel.DataAnnotations;

namespace Klean.Api.DTOs;

public class AgentDto
{
    public Guid Id { get; set; }
    public Guid EntrepriseId { get; set; }
    public string Nom { get; set; } = string.Empty;
    public string Prenom { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Telephone { get; set; }
    public string? Adresse { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTime DateEmbauche { get; set; }
    public bool Actif { get; set; }

    /// <summary>
    /// Renseignée quand l'agent a été pseudonymisé (étape 17), nulle sinon.
    ///
    /// ⚠️ Exposée exprès : sans elle, l'écran afficherait « Agent anonymisé 3f2a1b0c »
    /// comme un agent ordinaire dont quelqu'un aurait mal saisi le nom. Elle ne révèle
    /// rien — elle dit qu'il n'y a plus rien à révéler.
    /// </summary>
    public DateTime? DateAnonymisation { get; set; }

    /// <summary>Le compte du chef d'équipe, nul si l'agent n'est rattaché à personne.</summary>
    public Guid? ChefEquipeId { get; set; }
}

public class AgentCreateDto
{
    [Required]
    [MaxLength(100)]
    public string Nom { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Prenom { get; set; } = string.Empty;

    [MaxLength(200)]
    [EmailAddress]
    public string? Email { get; set; }

    [MaxLength(30)]
    public string? Telephone { get; set; }

    [MaxLength(200)]
    public string? Adresse { get; set; }

    /// <summary>Nullable : sans coordonnées, la note de distance vaut 50 sur 100.</summary>
    [Range(-90, 90)]
    public double? Latitude { get; set; }

    [Range(-180, 180)]
    public double? Longitude { get; set; }

    public DateTime DateEmbauche { get; set; }

    public bool Actif { get; set; } = true;

    /// <summary>
    /// Le compte d'un chef d'équipe <b>de la même entreprise</b>, ou nul. Refusé en 400
    /// sinon : <c>AspNetUsers</c> n'étant pas filtrée, rien d'autre ne l'empêcherait.
    ///
    /// ⚠️ Le PUT remplace la fiche entière : un corps qui omet ce champ détache l'agent
    /// de son chef. C'est pourquoi l'écran des agents le décrit (CLAUDE.md §11).
    /// </summary>
    public Guid? ChefEquipeId { get; set; }
}

/// <summary>
/// Un chef d'équipe de l'entreprise, pour le choix du rattachement d'un agent —
/// <c>GET /api/agents/chefs-equipe</c>.
///
/// Ni courriel ni téléphone : choisir un chef demande de le reconnaître, pas de le
/// joindre. <see cref="Actif"/> permet à l'écran de signaler un compte désactivé, auquel
/// des agents peuvent rester rattachés.
/// </summary>
public class ChefEquipeDto
{
    public Guid Id { get; set; }
    public string Nom { get; set; } = string.Empty;
    public string Prenom { get; set; } = string.Empty;
    public bool Actif { get; set; }
}

/// <summary>
/// Un agent dans une liste déroulante — <c>GET /api/agents/choix</c>.
///
/// ⚠️ C'est toute la raison de la route : remplir le choix du titulaire d'une prestation
/// envoyait au navigateur l'adresse et les <b>coordonnées du domicile</b> de chaque agent,
/// par <see cref="AgentDto"/>, pour n'en afficher que le nom. Une propriété ajoutée ici
/// sortira à chaque ouverture des écrans de prestations et d'absences : la minimisation
/// se garde par le test qui lit la réponse brute.
/// </summary>
public class AgentChoixDto
{
    public Guid Id { get; set; }
    public string Nom { get; set; } = string.Empty;
    public string Prenom { get; set; } = string.Empty;

    /// <summary>Un titulaire désactivé reste porté par ses prestations : l'écran le dit.</summary>
    public bool Actif { get; set; }
}
