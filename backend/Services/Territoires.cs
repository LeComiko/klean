using Klean.Api.Data;
using Klean.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Services;

/// <summary>
/// Le fuseau de chaque territoire, et le jour qu'il est là-bas.
///
/// ⚠️ « Aujourd'hui » était le jour UTC jusqu'au 4 novembre 2026 : entre minuit et une ou
/// deux heures du matin à Paris, c'était encore la veille, et la génération pouvait créer
/// une vacation du jour qui venait de finir. À La Réunion, l'écart allait jusqu'à quatre
/// heures. Le jour est désormais celui du territoire de l'entreprise.
/// </summary>
public static class Territoires
{
    /// <summary>Identifiants IANA, que .NET résout sur Linux comme sur macOS.</summary>
    private static readonly IReadOnlyDictionary<Territoire, string> Fuseaux = new Dictionary<Territoire, string>
    {
        [Territoire.Metropole] = "Europe/Paris",
        [Territoire.AlsaceMoselle] = "Europe/Paris",
        [Territoire.Guadeloupe] = "America/Guadeloupe",
        [Territoire.Martinique] = "America/Martinique",
        [Territoire.Guyane] = "America/Cayenne",
        [Territoire.Reunion] = "Indian/Reunion",
        [Territoire.Mayotte] = "Indian/Mayotte"
    };

    /// <summary>Le territoire de l'entreprise de la requête, cherché par son identifiant.</summary>
    public static Task<Territoire> DeLEntrepriseAsync(KleanDbContext db, Guid? entrepriseId, CancellationToken annulation) =>
        db.Entreprises.AsNoTracking()
            .Where(e => e.Id == entrepriseId)
            .Select(e => e.Territoire)
            .FirstAsync(annulation);

    public static TimeZoneInfo Fuseau(Territoire territoire) =>
        TimeZoneInfo.FindSystemTimeZoneById(Fuseaux[territoire]);

    /// <summary>Le jour qu'il est sur ce territoire, à cet instant.</summary>
    public static DateOnly Aujourdhui(TimeProvider horloge, Territoire territoire) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(horloge.GetUtcNow(), Fuseau(territoire)).DateTime);

    /// <summary>
    /// Résout chaque fuseau au démarrage. ⚠️ Un serveur sans base de fuseaux — une image
    /// de conteneur réduite, sans <c>tzdata</c> — ferait échouer la génération en 500, au
    /// premier clic. Mieux vaut un démarrage refusé qui dit pourquoi (CLAUDE.md §2 :
    /// « configuré à moitié est une erreur »).
    /// </summary>
    public static void VerifierFuseaux()
    {
        foreach (var (territoire, identifiant) in Fuseaux)
        {
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(identifiant);
            }
            catch (TimeZoneNotFoundException)
            {
                throw new InvalidOperationException(
                    $"Fuseau « {identifiant} » introuvable sur ce serveur (territoire {territoire}). "
                    + "Installer la base des fuseaux horaires — le paquet tzdata.");
            }
        }
    }
}
