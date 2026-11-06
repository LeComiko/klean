namespace Klean.Api.DTOs;

/// <summary>
/// Réponse 202 de <c>POST /api/auth/connexion</c> quand le mot de passe ne suffit pas
/// (SPEC.md, étape 23). Aucune session n'est ouverte : seul un cookie de défi est posé.
/// </summary>
public class EtapeConnexionDto
{
    /// <summary><c>SecondFacteur</c> : saisir un code. <c>Equipement</c> : activer d'abord.</summary>
    public string Etape { get; set; } = string.Empty;
}

/// <summary>
/// Un code du second facteur : six chiffres de l'application, <b>ou</b> un code de
/// récupération. Exactement l'un des deux, vérifié par le contrôleur.
///
/// ⚠️ Aucun <c>[Required]</c> : la validation de modèle répondrait en anglais avant le
/// contrôleur (CLAUDE.md §10).
/// </summary>
public class CodeSecondFacteurDto
{
    public string? Code { get; set; }
    public string? CodeRecuperation { get; set; }
}

/// <summary>
/// Ce qu'il faut pour équiper un téléphone. La clé est rendue <b>en clair</b> une fois,
/// à qui vient de prouver son mot de passe : c'est ce qui la fait entrer dans
/// l'application. Elle n'est plus jamais relue ensuite.
/// </summary>
public class PreparationSecondFacteurDto
{
    /// <summary>La clé en Base32, par groupes de quatre, pour une saisie à la main.</summary>
    public string Cle { get; set; } = string.Empty;

    /// <summary>L'URI <c>otpauth://totp/…</c> que porte le QR code.</summary>
    public string Uri { get; set; } = string.Empty;

    /// <summary>
    /// Les modules du QR code, ligne par ligne, <c>true</c> pour un module sombre. La
    /// marge blanche n'y est pas : l'écran la dessine.
    /// </summary>
    public bool[][] Modules { get; set; } = [];
}

/// <summary>
/// Les codes de récupération, montrés <b>une seule fois</b> : le serveur n'en garde que
/// l'empreinte. Accompagnés de la session rouverte quand le geste a changé l'empreinte
/// de sécurité du compte.
/// </summary>
public class CodesRecuperationDto
{
    public IReadOnlyList<string> Codes { get; set; } = [];
    public SessionDto? Session { get; set; }
}

/// <summary>L'état du second facteur de l'appelant, pour l'écran du compte.</summary>
public class EtatSecondFacteurDto
{
    public bool Actif { get; set; }

    /// <summary>Le rôle l'impose : la désactivation est alors refusée.</summary>
    public bool Obligatoire { get; set; }

    public int CodesRestants { get; set; }
}

/// <summary>Compte rendu de la réinitialisation par un administrateur.</summary>
public class ReinitialisationSecondFacteurDto
{
    public Guid UtilisateurId { get; set; }
    public int SessionsRevoquees { get; set; }
}
