using System.Security.Claims;
using Klean.Api.Models;

namespace Klean.Api.Services;

public class TenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accesseur;

    public TenantContext(IHttpContextAccessor accesseur) => _accesseur = accesseur;

    public bool FiltrageActif { get; private set; } = true;

    public void DesactiverFiltrage() => FiltrageActif = false;

    /// <summary>
    /// Nuls pour un compte interne : aucune restriction supplémentaire ne
    /// s'applique alors, les filtres globaux traitent null comme « pas de
    /// cadrage ».
    /// </summary>
    public Guid? ClientId => Revendication(JwtService.RevendicationClientId);

    public Guid? AgentId => Revendication(JwtService.RevendicationAgentId);

    /// <summary>
    /// ⚠️ Le rôle suffit à restreindre, même cumulé avec un autre : sur une frontière,
    /// le doute se résout vers moins de données. Aucune route ne crée un compte à deux
    /// rôles aujourd'hui.
    /// </summary>
    public Guid? ChefEquipeId
    {
        get
        {
            var utilisateur = _accesseur.HttpContext?.User;

            if (utilisateur?.IsInRole(RolesKlean.ChefEquipe) != true) return null;

            return Guid.TryParse(utilisateur.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)
                ? id
                : throw new InvalidOperationException(
                    "Jeton de chef d'équipe sans identifiant exploitable. Refus de poursuivre : "
                    + "le cadrage de son équipe serait inopérant.");
        }
    }

    private Guid? Revendication(string nom)
    {
        var valeur = _accesseur.HttpContext?.User?.FindFirst(nom)?.Value;
        return Guid.TryParse(valeur, out var id) ? id : null;
    }

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
