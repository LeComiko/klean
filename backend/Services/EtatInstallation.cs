namespace Klean.Api.Services;

/// <summary>
/// Ce que l'API sait de son installation, partagé entre l'amorçage et l'assistant.
/// </summary>
public sealed class EtatInstallation
{
    // Singleton lu par les requêtes et écrit par l'amorçage ou par l'assistant, d'un fil à
    // l'autre : volatile, pour qu'une écriture soit vue sans attendre une barrière de hasard.
    private volatile bool _entrepriseOrpheline;
    private volatile bool _installee;

    /// <summary>
    /// Une entreprise sans aucun compte, vue au démarrage au propriétaire — reste d'un amorçage
    /// interrompu d'avant le lot A2, qui n'était pas transactionnel. Sous la RLS, la route ne voit
    /// aucune autre entreprise que la sienne : elle ne peut pas la découvrir elle-même.
    /// </summary>
    public bool EntrepriseOrpheline
    {
        get => _entrepriseOrpheline;
        set => _entrepriseOrpheline = value;
    }

    /// <summary>
    /// Vrai dès qu'un compte a été vu. Ne revient jamais en arrière dans la vie du processus :
    /// GET /api/installation n'interroge plus la base ensuite. Une restauration redémarre l'API.
    /// </summary>
    public bool Installee
    {
        get => _installee;
        set => _installee = value;
    }

    public const string MessageOrpheline =
        "Une entreprise existe déjà sans aucun compte : le reste d'une installation interrompue. "
        + "Retirez-la, puis redémarrez KLEAN — DEPLOIEMENT.md, « Une entreprise sans compte ».";
}
