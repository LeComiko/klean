using Microsoft.Extensions.Caching.Memory;

namespace Klean.Api.Services;

/// <summary>
/// Limitation de débit à fenêtre glissante, par clé arbitraire.
///
/// Le verrouillage d'Identity est <b>par compte</b> : cinq échecs, quinze minutes.
/// Il ne ralentit donc en rien le scénario inverse — pulvériser un seul mot de
/// passe probable sur cent comptes, un essai par compte. C'est ce que couvre la
/// dimension <b>par adresse</b>, et c'est pour cela que les deux sont appliquées
/// ensemble.
///
/// Fenêtre glissante et non fixe : une fenêtre fixe laisse passer deux fois le
/// quota à cheval sur sa frontière, ce qui est exactement le moment que choisit
/// un script.
///
/// ⚠️ <b>Limite connue :</b> le compteur vit en mémoire du processus. Derrière
/// plusieurs instances, la limite est par instance. La cible de déploiement est
/// une instance unique ; un compteur partagé supposerait un service tiers, ce que
/// <c>CLAUDE.md</c> §2 refuse d'imposer.
/// </summary>
public class LimiteurDebit : IDisposable
{
    /// <summary>Clé d'adresse quand elle est inconnue — en test, ou derrière un proxy mal configuré.</summary>
    public const string AdresseInconnue = "adresse-inconnue";

    private MemoryCache _cache = Neuf();

    /// <summary>
    /// Enregistre une tentative et dit si elle est autorisée. Une tentative
    /// refusée n'est <b>pas</b> comptée : sinon un client bloqué se prolongerait
    /// indéfiniment sa propre punition en réessayant, ce qui transforme la
    /// limitation en verrouillage définitif.
    /// </summary>
    public bool Autoriser(string cle, int maximum, TimeSpan fenetre)
    {
        var maintenant = DateTime.UtcNow;

        var horodatages = _cache.GetOrCreate(cle, entree =>
        {
            // Marge sur l'expiration : une entrée ne doit pas disparaître tant
            // qu'elle peut encore contenir un horodatage dans la fenêtre.
            entree.SlidingExpiration = fenetre + TimeSpan.FromMinutes(1);
            return new List<DateTime>();
        })!;

        lock (horodatages)
        {
            var debut = maintenant - fenetre;
            horodatages.RemoveAll(h => h < debut);

            if (horodatages.Count >= maximum) return false;

            horodatages.Add(maintenant);
            return true;
        }
    }

    /// <summary>
    /// Remet tous les compteurs à zéro. Réservé à la série de tests, qui partage
    /// une instance d'application entre les tests : sans cela les tentatives
    /// s'accumuleraient d'un test à l'autre et la série se bloquerait elle-même.
    /// Les tests exercent ainsi les vraies limites de production.
    /// </summary>
    public void Vider()
    {
        var ancien = Interlocked.Exchange(ref _cache, Neuf());
        ancien.Dispose();
    }

    public void Dispose()
    {
        _cache.Dispose();
        GC.SuppressFinalize(this);
    }

    private static MemoryCache Neuf() => new(new MemoryCacheOptions());
}

/// <summary>
/// Les plafonds, rassemblés pour être lisibles d'un coup d'œil plutôt que
/// dispersés dans les contrôleurs.
/// </summary>
public static class PlafondsDebit
{
    /// <summary>Connexion, par adresse. Un humain n'en fait pas vingt par minute.</summary>
    public const int ConnexionsParAdresse = 20;
    public static TimeSpan FenetreConnexionAdresse => TimeSpan.FromMinutes(1);

    /// <summary>Connexion, par compte. Aligné sur le verrouillage d'Identity.</summary>
    public const int ConnexionsParCompte = 10;
    public static TimeSpan FenetreConnexionCompte => TimeSpan.FromMinutes(15);

    /// <summary>
    /// Mot de passe oublié. Serré : chaque appel réussi envoie un courriel, donc
    /// un plafond large transforme l'endpoint en outil de harcèlement par boîte
    /// de réception.
    /// </summary>
    public const int OublisParAdresse = 3;
    public const int OublisParCompte = 3;
    public static TimeSpan FenetreOublis => TimeSpan.FromMinutes(15);

    /// <summary>
    /// Réinitialisation. Plus permissif que l'oubli — un utilisateur peut s'y
    /// reprendre à plusieurs fois sur les règles de mot de passe — mais borné :
    /// c'est aussi la surface d'un essai de jetons au hasard.
    /// </summary>
    public const int ReinitialisationsParAdresse = 10;
    public static TimeSpan FenetreReinitialisations => TimeSpan.FromMinutes(15);

    /// <summary>
    /// Changement de mot de passe par son propriétaire.
    ///
    /// ⚠️ L'endpoint est authentifié, et il a pourtant besoin d'être borné : il
    /// vérifie le mot de passe <b>actuel</b>, donc c'est un oracle. Le verrouillage
    /// d'Identity ne s'y applique pas — <c>ChangePasswordAsync</c> ne compte pas
    /// d'échec — et sans plafond, qui tient une session volée peut y éprouver des
    /// mots de passe sans limite. Non pas pour entrer, il est déjà entré, mais pour
    /// apprendre un mot de passe sans doute réemployé ailleurs.
    ///
    /// Par compte et non par adresse : l'attaquant est à l'intérieur d'une session,
    /// pas en train d'en essayer cent.
    /// </summary>
    public const int ChangementsParAdresse = 20;
    public const int ChangementsParCompte = 10;
    public static TimeSpan FenetreChangements => TimeSpan.FromMinutes(15);

    /// <summary>
    /// Saisie d'un code du second facteur — à la connexion, à l'activation, à la
    /// régénération des codes et à la désactivation.
    ///
    /// Le verrouillage d'Identity, partagé avec le mot de passe, borne déjà les essais
    /// de la connexion à cinq. Ce plafond couvre les trois autres gestes, où un échec
    /// ne verrouille pas le compte : s'en servir comme oracle depuis une session volée
    /// reste ainsi aussi lent qu'au premier pas.
    /// </summary>
    public const int CodesParAdresse = 20;
    public const int CodesParCompte = 10;
    public static TimeSpan FenetreCodes => TimeSpan.FromMinutes(15);
}
