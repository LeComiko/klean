namespace Klean.Api.Models;

/// <summary>
/// Les cinq rôles du système. ClientSyndic est le seul accès externe : son
/// périmètre doit être cadré avec une attention particulière, une erreur
/// exposerait les données internes d'une entreprise à ses clients.
/// </summary>
public static class RolesKlean
{
    public const string Administrateur = "Administrateur";
    public const string Responsable = "Responsable";
    public const string ChefEquipe = "ChefEquipe";
    public const string Agent = "Agent";
    public const string ClientSyndic = "ClientSyndic";

    // ---- Groupes utilisés par [Authorize(Roles = ...)] ----
    // La sémantique de Roles est « l'un de », pas « tous ».
    // Voir la matrice des droits dans SPEC.md, étape 8.

    /// <summary>Les trois rôles internes : lecture sur toute l'entreprise.</summary>
    public const string Interne = $"{Administrateur},{Responsable},{ChefEquipe}";

    /// <summary>Écriture sur les données d'exploitation, hors données de référence.</summary>
    public const string Exploitation = $"{Administrateur},{Responsable}";

    /// <summary>Lecture interne, plus le syndic cadré sur son propre client.</summary>
    public const string InterneEtClientSyndic = $"{Interne},{ClientSyndic}";

    /// <summary>Lecture interne, plus l'agent cadré sur ses propres interventions.</summary>
    public const string InterneEtAgent = $"{Interne},{Agent}";

    public static readonly string[] Tous =
    [
        Administrateur,
        Responsable,
        ChefEquipe,
        Agent,
        ClientSyndic
    ];
}
