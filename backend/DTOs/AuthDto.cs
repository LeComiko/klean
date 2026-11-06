using System.ComponentModel.DataAnnotations;

namespace Klean.Api.DTOs;

public class ConnexionDto
{

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string MotDePasse { get; set; } = string.Empty;
}

/// <summary>
/// Réponse d'une connexion ou d'un rafraîchissement.
///
/// ⚠️ <b>Ne contient aucun jeton, et ne doit jamais en contenir.</b> Le jeton
/// d'accès part en cookie <c>HttpOnly</c> : le remettre aussi dans le corps le
/// rendrait lisible par le script, ce qui annulerait exactement ce que le cookie
/// apporte. Le client n'a besoin que de savoir <b>qui</b> il est et <b>quand</b>
/// rafraîchir.
/// </summary>
public class SessionDto
{
    /// <summary>
    /// Expiration du jeton d'accès. Sert au client à rafraîchir avant d'échouer,
    /// plutôt qu'après.
    /// </summary>
    public DateTime Expiration { get; set; }

    public ProfilDto Profil { get; set; } = new();
}

public class ProfilDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Nom { get; set; } = string.Empty;
    public string Prenom { get; set; } = string.Empty;
    public Guid EntrepriseId { get; set; }
    public Guid? AgentId { get; set; }
    public Guid? ClientId { get; set; }
    public bool Actif { get; set; }
    public IEnumerable<string> Roles { get; set; } = [];
}

public class MotDePasseOublieDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}

public class ReinitialiserDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>Jeton reçu par courriel. À usage unique, valable une heure.</summary>
    [Required]
    public string Jeton { get; set; } = string.Empty;

    /// <summary>
    /// 10 caractères minimum, une majuscule, une minuscule, un chiffre. Pas de
    /// <c>[MinLength]</c> : voir <see cref="NouveauMotDePasseDto.NouveauMotDePasse"/>.
    /// </summary>
    [Required]
    public string NouveauMotDePasse { get; set; } = string.Empty;
}

/// <summary>
/// Création d'un compte par un administrateur.
///
/// ⚠️ <b>Aucun mot de passe</b> (SPEC.md, étape 24) : le serveur en tire un, le rend une
/// fois dans <see cref="CompteCreeDto"/>, et la première connexion impose de le changer.
/// L'administrateur ne choisit donc jamais le mot de passe d'un collègue, et ce qu'il lui
/// transmet ne vaut plus rien dès la première connexion.
/// </summary>
public class CompteCreateDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Nom { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Prenom { get; set; } = string.Empty;

    /// <summary>Un des cinq rôles de RolesKlean.</summary>
    [Required]
    public string Role { get; set; } = string.Empty;

    /// <summary>Obligatoire pour un compte de rôle Agent.</summary>
    public Guid? AgentId { get; set; }

    /// <summary>Obligatoire pour un compte de rôle ClientSyndic.</summary>
    public Guid? ClientId { get; set; }
}

/// <summary>
/// Le mot de passe choisi à la première connexion, en remplacement du provisoire —
/// <c>POST /api/auth/connexion/mot-de-passe</c> (SPEC.md, étape 24). Le défi de la
/// connexion tient lieu de preuve : le mot de passe provisoire vient d'y être vérifié.
/// </summary>
public class NouveauMotDePasseDto
{
    /// <summary>
    /// 10 caractères minimum, une majuscule, une minuscule, un chiffre.
    ///
    /// ⚠️ Pas de <c>[MinLength]</c> : la validation de modèle d'ASP.NET s'exécute
    /// <b>avant</b> le contrôleur, donc avant Identity, et renvoie son propre message
    /// <b>en anglais</b> que le descripteur d'erreurs français ne peut pas
    /// atteindre. Elle dupliquerait en plus une règle dont Identity est l'autorité —
    /// deux longueurs minimales finiraient par diverger. Identity valide, et parle
    /// français.
    /// </summary>
    [Required]
    public string NouveauMotDePasse { get; set; } = string.Empty;
}

/// <summary>
/// Un compte de l'entreprise, pour la liste de l'administrateur —
/// <c>GET /api/auth/comptes</c> (SPEC.md, étape 24).
///
/// ⚠️ <b>Aucune date de dernière connexion</b>, et c'est délibéré : elle n'est pas
/// stockée, elle se déduirait des sessions, et ce serait une donnée de surveillance des
/// collègues que personne n'a demandée.
/// </summary>
public class CompteDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Nom { get; set; } = string.Empty;
    public string Prenom { get; set; } = string.Empty;

    /// <summary>Un compte porte un seul rôle ; vide si la base en a perdu la trace.</summary>
    public string Role { get; set; } = string.Empty;

    public bool Actif { get; set; }
    public bool SecondFacteurActif { get; set; }

    /// <summary>Vrai tant que la personne ne s'est pas connectée une première fois.</summary>
    public bool MotDePasseProvisoire { get; set; }

    public Guid? AgentId { get; set; }
    public Guid? ClientId { get; set; }
    public DateTime DateCreation { get; set; }

    /// <summary>
    /// Posée, le compte est pseudonymisé : la liste le garde, en fin, pour que le
    /// registre montre que l'effacement a eu lieu.
    /// </summary>
    public DateTime? DateAnonymisation { get; set; }
}

/// <summary>
/// La réponse d'une création : le compte, et son mot de passe provisoire.
///
/// ⚠️ <b>Le seul endroit où ce mot de passe existe en clair</b>, et une seule fois :
/// la base n'en garde que l'empreinte d'Identity. Perdu avant d'avoir été transmis, il
/// ne se relit pas — le compte est à désactiver, puis à recréer.
/// </summary>
public class CompteCreeDto
{
    public CompteDto Compte { get; set; } = new();
    public string MotDePasseProvisoire { get; set; } = string.Empty;
}

/// <summary>
/// Changement de mot de passe par son propriétaire, depuis l'écran de compte.
///
/// Distinct de <see cref="ReinitialiserDto"/> : celui-ci ne demande aucun jeton
/// reçu par courriel, parce que la preuve d'identité est le mot de passe actuel —
/// donc aucun serveur SMTP n'est requis, et c'est le seul parcours de mot de passe
/// qui fonctionne sur une instance sans courriel (CLAUDE.md §2).
/// </summary>
public class ChangementMotDePasseDto
{
    /// <summary>
    /// La preuve d'identité. Sans elle, un jeton de session volé suffirait à
    /// verrouiller le compte de son propriétaire.
    /// </summary>
    [Required]
    public string MotDePasseActuel { get; set; } = string.Empty;

    /// <summary>
    /// 10 caractères minimum, une majuscule, une minuscule, un chiffre. Pas de
    /// <c>[MinLength]</c> : voir <see cref="NouveauMotDePasseDto.NouveauMotDePasse"/>.
    /// </summary>
    [Required]
    public string NouveauMotDePasse { get; set; } = string.Empty;
}
