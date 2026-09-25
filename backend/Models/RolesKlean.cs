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

    public static readonly string[] Tous =
    [
        Administrateur,
        Responsable,
        ChefEquipe,
        Agent,
        ClientSyndic
    ];
}
