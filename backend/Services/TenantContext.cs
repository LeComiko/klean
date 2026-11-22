using System.Security.Claims;
using Klean.Api.Models;

namespace Klean.Api.Services;

public class TenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accesseur;

    public TenantContext(IHttpContextAccessor accesseur) => _accesseur = accesseur;

    public bool FiltrageActif { get; private set; } = true;

    private Guid? _entrepriseInstallation;

    /// <summary>
    /// ⚠️ <b>Jamais dans une requête HTTP</b> (SPEC.md, étape 34, lot A2) : une route ne tourne
    /// jamais en propriétaire des tables. Hors requête seulement : l'amorçage, la commande de
    /// démonstration, la fixture de test. C'est un fil-piège contre l'appel involontaire, pas une
    /// garantie : un travail lancé hors du flux de la requête n'a pas de <c>HttpContext</c>.
    /// </summary>
    public void DesactiverFiltrage()
    {
        if (_accesseur.HttpContext is not null)
        {
            throw new InvalidOperationException(
                "DesactiverFiltrage dans une requête HTTP : refusé. Une route ne tourne jamais en "
                + "propriétaire des tables (CLAUDE.md §7).");
        }

        FiltrageActif = false;
    }

    public Guid PoserEntrepriseInstallation()
    {
        if (_accesseur.HttpContext?.User?.Identity?.IsAuthenticated == true)
            throw new InvalidOperationException("L'entreprise d'installation ne se pose pas sur une requête authentifiée.");
        if (_entrepriseInstallation is not null)
            throw new InvalidOperationException("L'entreprise d'installation est déjà posée pour cette requête.");

        _entrepriseInstallation = Guid.NewGuid();
        return _entrepriseInstallation.Value;
    }

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
            // série de tests et à /api/sante — sauf l'assistant d'installation,
            // qui pose la sienne.
            if (utilisateur?.Identity?.IsAuthenticated != true)
            {
                return _entrepriseInstallation;
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
