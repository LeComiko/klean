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

    /// <summary>
    /// Compte du jeton quand il porte le rôle ChefEquipe, nul sinon. Quatrième axe
    /// (SPEC.md, étape 19) : le chef ne voit que les agents rattachés à son compte,
    /// leurs absences, leurs interventions — plus celles sans agent dont le titulaire
    /// est l'un d'eux — et les remplacements de ces interventions.
    ///
    /// ⚠️ Lu sur le <b>rôle</b>, pas sur une revendication dédiée : un jeton émis avant
    /// le rattachement d'un agent n'a pas à être renouvelé, puisque l'équipe est lue en
    /// base à chaque requête par les filtres globaux.
    /// </summary>
    Guid? ChefEquipeId { get; }

    bool FiltrageActif { get; }

    /// <summary>
    /// Utilisé par l'amorçage, qui doit créer la première entreprise avant
    /// qu'aucun tenant n'existe.
    /// </summary>
    void DesactiverFiltrage();

    /// <summary>
    /// Tire l'identifiant de l'entreprise que crée l'assistant d'installation (SPEC.md, étape 34,
    /// lot A2) et en fait l'entreprise de cette requête anonyme : la connexion passe sous
    /// <c>klean_app</c> avec ce réglage, et la politique d'<c>Entreprises</c> accepte l'insertion.
    /// Une fois par requête, jamais sur une requête authentifiée, jamais avec un identifiant venu
    /// d'ailleurs. Seul <c>InstallationController</c> l'appelle (un test le vérifie).
    /// </summary>
    Guid PoserEntrepriseInstallation();
}
