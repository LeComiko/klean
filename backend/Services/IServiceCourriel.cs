namespace Klean.Api.Services;

/// <summary>
/// Envoi de courriel.
///
/// ⚠️ <b>Seule exception à la règle « services : classes concrètes, pas
/// d'interface »</b> de <c>CLAUDE.md</c> §11, avec <c>ITenantContext</c>. La raison
/// est la même dans les deux cas : la substitution est nécessaire, pas
/// décorative. Ici, aucun test ne doit envoyer de courriel — et une classe
/// concrète qui « n'envoie pas vraiment quand elle n'est pas configurée » ferait
/// porter au code de production la responsabilité de se taire en test, ce qui est
/// exactement la confusion qu'on veut éviter sur un chemin d'authentification.
/// </summary>
public interface IServiceCourriel
{
    /// <summary>
    /// Faux quand aucun hôte SMTP n'est configuré. Le parcours de réinitialisation
    /// est alors refusé avec un message explicite, plutôt que de prétendre avoir
    /// envoyé un courriel qui n'arrivera jamais.
    /// </summary>
    bool Configure { get; }

    Task EnvoyerAsync(string destinataire, string sujet, string corps);
}
