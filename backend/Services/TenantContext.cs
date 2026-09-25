using System.Security.Claims;

namespace Klean.Api.Services;

public class TenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accesseur;

    public TenantContext(IHttpContextAccessor accesseur) => _accesseur = accesseur;

    public bool FiltrageActif { get; private set; } = true;

    public void DesactiverFiltrage() => FiltrageActif = false;

    public Guid? EntrepriseId
    {
        get
        {
            var utilisateur = _accesseur.HttpContext?.User;

            // Hors requête, ou requête anonyme : pas de tenant. Les onze
            // contrôleurs métier portant [Authorize], ce cas ne correspond
            // jamais à un accès aux données — seulement à l'amorçage, à la
            // série de tests et à /api/sante.
            if (utilisateur?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            var valeur = utilisateur.FindFirst(JwtService.RevendicationEntrepriseId)?.Value;

            // Un jeton authentifié sans cette revendication est un état
            // impossible : JwtService la pose toujours, et la signature empêche
            // de forger. Échouer bruyamment plutôt que de laisser passer un
            // tenant nul, qui désactiverait les filtres.
            if (!Guid.TryParse(valeur, out var entrepriseId))
            {
                throw new InvalidOperationException(
                    "Jeton authentifié sans revendication « entrepriseId » exploitable. "
                    + "Refus de poursuivre : le filtrage multi-tenant serait inopérant.");
            }

            return entrepriseId;
        }
    }
}
