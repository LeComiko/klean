using Klean.Api.Data;
using Klean.Api.Models;

namespace Klean.Api.Services;

/// <summary>
/// Écrit les lignes du journal d'accès aux données personnelles (SPEC.md étape 17).
///
/// Une classe concrète, sans interface : rien ne demande de la substituer, et §11
/// n'autorise une interface de service que lorsque la substitution est nécessaire.
/// </summary>
public class ServiceJournalAcces
{
    private readonly KleanDbContext _db;

    public ServiceJournalAcces(KleanDbContext db) => _db = db;

    /// <summary>
    /// Consigne un accès. Une ligne par requête, écrite immédiatement.
    ///
    /// ⚠️ <c>EntrepriseId</c> n'est pas passé en paramètre, et c'est volontaire :
    /// <c>SaveChangesAsync</c> le force inconditionnellement depuis le jeton sur toute
    /// entité ajoutée (CLAUDE.md §7). Le journal obéit donc à la même règle que les
    /// données qu'il surveille, par le même mécanisme — un journal cadré autrement que
    /// ses données serait précisément le genre d'écart qu'on découvre trop tard.
    ///
    /// ⚠️ Pas de file d'attente, pas d'écriture différée : une insertion synchrone par
    /// lecture. C'est le prix du mécanisme, et il est explicite. Une file rendrait
    /// l'échec d'écriture invisible, ce qui viderait le journal de sa valeur de preuve.
    /// </summary>
    public async Task EnregistrerAsync(
        Guid utilisateurId,
        RessourceAccedee ressource,
        NatureAcces nature,
        Guid? cibleId,
        int lignes)
    {
        _db.AccesJournalises.Add(new AccesJournalise
        {
            UtilisateurId = utilisateurId,
            Ressource = ressource,
            Nature = nature,
            CibleId = cibleId,
            Lignes = lignes,
            Horodatage = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
    }
}
