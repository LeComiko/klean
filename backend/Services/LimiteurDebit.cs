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
    public bool Autoriser(string cle, int maximum, TimeSpan fenetre) =>
        Autoriser(cle, maximum, fenetre, out _);

    /// <summary>
    /// La même, qui dit aussi, sur un refus, combien de temps attendre avant qu'un essai repasse :
    /// la sortie du plus ancien horodatage de la fenêtre. <see cref="TimeSpan.Zero"/> sur une
    /// autorisation.
    /// </summary>
    public bool Autoriser(string cle, int maximum, TimeSpan fenetre, out TimeSpan attente)
    {
        var maintenant = DateTime.UtcNow;
        attente = TimeSpan.Zero;

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

            if (horodatages.Count >= maximum)
            {
                // À défaut d'horodatage (maximum nul), la fenêtre entière.
                attente = horodatages.Count > 0 ? horodatages.Min() + fenetre - maintenant : fenetre;
                return false;
            }

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

    /// <summary>
    /// La clé d'une adresse : IPv4 telle quelle (une IPv4 encapsulée en IPv6 ramenée à IPv4),
    /// IPv6 regroupée par son /64 — un seul hôte en possède des milliards, et tournerait dedans.
    /// </summary>
    public static string CleDAdresse(System.Net.IPAddress? adresse)
    {
        if (adresse is null) return AdresseInconnue;
        if (adresse.IsIPv4MappedToIPv6) adresse = adresse.MapToIPv4();
        if (adresse.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return adresse.ToString();

        var octets = adresse.GetAddressBytes();
        Array.Clear(octets, 8, 8);
        return new System.Net.IPAddress(octets) + "/64";
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

    /// <summary>
    /// Assistant d'installation (étape 34, lot A2). Le code porte environ 100 bits : ces plafonds
    /// bornent le bruit, ils ne le protègent pas. Par adresse sur tout POST, plus large pour un bon
    /// code — le patron s'y reprend sur un SIRET ou un mot de passe refusé. Le global ne compte
    /// <b>que les codes faux</b> : compté sur tous, un inconnu le garderait plein indéfiniment et
    /// bloquerait l'installation légitime (relecture du 22 novembre 2026).
    /// </summary>
    public const int InstallationsParAdresse = 10;
    public const int InstallationsBonCodeParAdresse = 20;
    public const int CodesFauxGlobaux = 30;
    public static TimeSpan FenetreInstallations => TimeSpan.FromMinutes(15);

    /// <summary>L'état de l'installation, lu à chaque ouverture de l'écran de connexion. Par adresse seulement.</summary>
    public const int LecturesInstallationParAdresse = 60;
    public static TimeSpan FenetreLecturesInstallation => TimeSpan.FromMinutes(1);

    /// <summary>
    /// La localisation d'un site (0.3.0). Le service compte par adresse IP, donc par instance : toutes
    /// les entreprises d'un serveur partagent ses 50 requêtes par seconde. Le plafond par compte borne un
    /// compte, et ne protège pas ce quota seul — trente requêtes peuvent partir dans la même seconde, et
    /// un administrateur peut en créer d'autres. Le global, dix par seconde, le protège.
    /// </summary>
    public const int LocalisationsParCompte = 30;
    public static TimeSpan FenetreLocalisations => TimeSpan.FromMinutes(15);
    public const int LocalisationsGlobales = 10;
    public static TimeSpan FenetreLocalisationsGlobales => TimeSpan.FromSeconds(1);
}
