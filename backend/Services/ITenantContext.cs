namespace Klean.Api.Services;

/// <summary>
/// Source unique de vérité du tenant courant. C'est la seule interface de
/// service du projet : elle existe parce que l'amorçage doit pouvoir désactiver
/// le filtrage, et parce que le DbContext en dépend par injection.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// Entreprise du jeton, ou null hors requête authentifiée — amorçage,
    /// série de tests, endpoints anonymes.
    /// </summary>
    Guid? EntrepriseId { get; }

    /// <summary>
    /// Client du jeton, renseigné pour un compte ClientSyndic. Deuxième axe
    /// d'isolation : les filtres globaux s'en servent pour restreindre le
    /// syndic à son propre client et à ses sites.
    /// </summary>
    Guid? ClientId { get; }

    /// <summary>
    /// Agent du jeton, renseigné pour un compte Agent. Troisième axe : il ne
    /// voit que les interventions dont il est l'affectataire.
    /// </summary>
    Guid? AgentId { get; }

    bool FiltrageActif { get; }

    /// <summary>
    /// Utilisé par l'amorçage, qui doit créer la première entreprise avant
    /// qu'aucun tenant n'existe.
    /// </summary>
    void DesactiverFiltrage();
}
