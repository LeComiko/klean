using System.ComponentModel.DataAnnotations;

namespace Klean.Api.Models;

/// <summary>
/// Agent de propreté. Porte naturellement son <see cref="EntrepriseId"/>,
/// avec clé étrangère.
/// </summary>
public class Agent
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid EntrepriseId { get; set; }
    public Entreprise? Entreprise { get; set; }

    [Required]
    [MaxLength(100)]
    public string Nom { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Prenom { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Email { get; set; }

    [MaxLength(30)]
    public string? Telephone { get; set; }

    [MaxLength(200)]
    public string? Adresse { get; set; }

    /// <summary>
    /// Nullable : un agent sans coordonnées reçoit la note de distance neutre
    /// de 50 sur 100 (CLAUDE.md §8).
    /// </summary>
    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public DateTime DateEmbauche { get; set; }

    /// <summary>Filtre dur du moteur d'affectation : un agent inactif n'est jamais candidat.</summary>
    public bool Actif { get; set; } = true;

    /// <summary>
    /// Date de la pseudonymisation, nulle tant qu'elle n'a pas eu lieu (étape 17).
    ///
    /// ⚠️ Une <b>date</b> et non un drapeau, pour deux raisons. Elle répond à la
    /// question du registre des traitements — « quand cet effacement a-t-il été
    /// exécuté » — qu'un booléen laisse sans réponse. Et elle rend l'opération
    /// refusable en 409 sur un agent déjà traité : sur un geste irréversible, une
    /// réussite silencieuse masquerait une erreur d'identifiant.
    ///
    /// ⚠️ <b>Pseudonymisation</b>, et le mot est choisi. Cet <c>Id</c> survit, parce que
    /// toutes les interventions passées en dépendent : la ligne reste donc rattachable à
    /// une personne par quiconque détiendrait une correspondance externe. C'est
    /// l'article 4(5) du RGPD, pas l'article 4(1). La route s'appelle
    /// <c>/anonymiser</c> parce que c'est l'intention de l'exploitant ; le registre des
    /// traitements devra, lui, être exact.
    /// </summary>
    public DateTime? DateAnonymisation { get; set; }

    /// <summary>
    /// Le compte du chef d'équipe qui encadre cet agent, nul s'il n'en a pas (SPEC.md,
    /// étape 19). Une équipe par agent, d'où une colonne et non une table de liaison.
    ///
    /// ⚠️ Il vise un <b>compte</b>, pas une fiche agent : le chef pilote ses agents et ne
    /// fait pas de vacations. Or <c>AspNetUsers</c> n'est pas filtrée par le multi-tenant
    /// (CLAUDE.md §7) : l'entreprise et le rôle du compte visé sont vérifiés à la main, à
    /// chaque écriture, par <c>AgentsController</c>.
    ///
    /// <b>SetNull</b> à la suppression du compte, pour la raison qui vaut pour le
    /// titulaire d'une prestation : c'est un rattachement, pas un fait. L'agent perdrait
    /// son chef, rien d'autre.
    /// </summary>
    public Guid? ChefEquipeId { get; set; }
    public Utilisateur? ChefEquipe { get; set; }

    public ICollection<Absence> Absences { get; set; } = new List<Absence>();
    public ICollection<Intervention> Interventions { get; set; } = new List<Intervention>();
}
