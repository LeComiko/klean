using System.Security.Cryptography;
using System.Text;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Net.Codecrete.QrCodeGenerator;

namespace Klean.Api.Services;

/// <summary>Ce que le cookie de défi autorise : saisir un code, ou s'équiper d'abord.</summary>
public enum EtapeDefi
{
    SecondFacteur,
    Equipement,

    /// <summary>
    /// Le mot de passe provisoire est à remplacer (SPEC.md, étape 24). Elle passe
    /// <b>avant</b> les deux autres : un compte obligé équipe son téléphone après avoir
    /// choisi son mot de passe, puisque ce changement régénère l'empreinte que le défi
    /// porte.
    /// </summary>
    ChangementMotDePasse
}

/// <summary>
/// Le second facteur : la clé, les codes de récupération, le défi entre les deux temps
/// de la connexion (SPEC.md, étape 23).
///
/// Identity fournit la colonne <c>TwoFactorEnabled</c> et la table
/// <c>AspNetUserTokens</c>, et c'est tout ce qui en est repris. Ses fournisseurs de
/// codes et ses codes de récupération ne servent pas, pour deux défauts mesurés dans
/// son code source :
/// <list type="bullet">
/// <item>la clé et les codes y sont rangés <b>en clair</b> — une sauvegarde de la base
/// livrerait le second facteur de tous les comptes ;</item>
/// <item>un code y est accepté pendant deux minutes et demie, autant de fois qu'on veut
/// (voir <see cref="Totp"/>).</item>
/// </list>
///
/// Ici, la clé est <b>chiffrée</b> par la Data Protection, dont le trousseau vit hors de
/// la base (volume <c>cles-api</c>) ; les codes de récupération sont tirés sur 80 bits et
/// ne sont gardés que par leur <b>empreinte</b> ; et le dernier pas accepté est mémorisé.
/// Tout est rangé sous le fournisseur <see cref="Fournisseur"/>, donc aucune migration.
/// </summary>
public class ServiceSecondFacteur
{
    public const string CookieDefi = "klean_defi";

    /// <summary>Cinq minutes entre le mot de passe et le code : de quoi sortir son téléphone.</summary>
    public static TimeSpan DureeDefi => TimeSpan.FromMinutes(5);

    public const int NombreCodesRecuperation = 10;

    /// <summary>Le nom sous lequel l'application d'authentification range le compte.</summary>
    public const string Emetteur = "KLEAN";

    /// <summary>Le fournisseur des jetons de <c>AspNetUserTokens</c> : celui de KLEAN, pas d'Identity.</summary>
    public const string Fournisseur = "[KleanSecondFacteur]";

    public const string JetonCle = "Cle";
    public const string JetonCodes = "CodesRecuperation";
    public const string JetonDernierPas = "DernierPas";

    /// <summary>Le défi ne sert qu'aux routes d'authentification, comme le rafraîchissement.</summary>
    private const string CheminDefi = "/api/auth";

    /// <summary>
    /// Un pas de part et d'autre de l'instant : 90 secondes de tolérance au décalage
    /// d'horloge d'un téléphone, la fenêtre que recommande la RFC 6238 §5.2. Identity en
    /// prend deux.
    /// </summary>
    private const int PasTolere = 1;

    /// <summary>
    /// Alphabet de Crockford : ni I, ni L, ni O, ni U. Un code recopié d'une feuille
    /// imprimée ne doit pas dépendre de la différence entre un 0 et un O.
    /// </summary>
    private const string AlphabetCodes = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>Seize caractères de cinq bits : 80 bits, hors de portée d'un essai exhaustif de l'empreinte.</summary>
    private const int LongueurCode = 16;

    private readonly UserManager<Utilisateur> _utilisateurs;
    private readonly IDataProtector _protecteurCle;
    private readonly IDataProtector _protecteurDefi;
    private readonly TimeProvider _horloge;

    public ServiceSecondFacteur(
        UserManager<Utilisateur> utilisateurs, IDataProtectionProvider protection, TimeProvider horloge)
    {
        _utilisateurs = utilisateurs;
        _protecteurCle = protection.CreateProtector("Klean.SecondFacteur.Cle");
        _protecteurDefi = protection.CreateProtector("Klean.SecondFacteur.Defi");
        _horloge = horloge;
    }

    public static bool EstOblige(IEnumerable<string> roles) =>
        roles.Any(RolesKlean.ObligesAuSecondFacteur.Contains);

    public async Task<bool> EstObligeAsync(Utilisateur utilisateur) =>
        EstOblige(await _utilisateurs.GetRolesAsync(utilisateur));

    // ---- Le défi, entre le mot de passe et le code -------------------------------------

    /// <summary>
    /// Pose le cookie de défi. Il porte l'identifiant du compte, son empreinte de
    /// sécurité, l'étape et l'échéance, chiffrés : le client ne peut ni le lire ni le
    /// forger, et un changement de mot de passe entre les deux temps le rend caduc.
    /// </summary>
    public void PoserDefi(HttpContext contexte, Utilisateur utilisateur, EtapeDefi etape)
    {
        var echeance = _horloge.GetUtcNow().Add(DureeDefi).ToUnixTimeSeconds();
        var contenu = $"{utilisateur.Id:N}|{utilisateur.SecurityStamp}|{etape}|{echeance}";

        contexte.Response.Cookies.Append(CookieDefi, _protecteurDefi.Protect(contenu), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = CheminDefi,
            MaxAge = DureeDefi,
            IsEssential = true
        });
    }

    public static void EffacerDefi(HttpContext contexte) =>
        contexte.Response.Cookies.Delete(CookieDefi, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = CheminDefi
        });

    /// <summary>
    /// Le compte du défi présenté, s'il est lisible, à l'étape attendue, non échu, et si
    /// le compte est actif et porte encore la même empreinte. <c>null</c> sinon : tous ces
    /// cas se règlent de la même façon, en recommençant la connexion.
    /// </summary>
    public async Task<Utilisateur?> CompteDuDefiAsync(HttpContext contexte, EtapeDefi attendue)
    {
        if (!contexte.Request.Cookies.TryGetValue(CookieDefi, out var brut) || string.IsNullOrEmpty(brut))
        {
            return null;
        }

        string contenu;
        try
        {
            contenu = _protecteurDefi.Unprotect(brut);
        }
        catch (CryptographicException)
        {
            return null;
        }

        var morceaux = contenu.Split('|');
        if (morceaux.Length != 4
            || !Guid.TryParseExact(morceaux[0], "N", out var id)
            || morceaux[2] != attendue.ToString()
            || !long.TryParse(morceaux[3], out var echeance)
            || _horloge.GetUtcNow().ToUnixTimeSeconds() >= echeance)
        {
            return null;
        }

        var utilisateur = await _utilisateurs.FindByIdAsync(id.ToString());

        return utilisateur is { Actif: true } && utilisateur.SecurityStamp == morceaux[1]
            ? utilisateur
            : null;
    }

    // ---- Équipement -------------------------------------------------------------------

    /// <summary>
    /// Tire une clé neuve et la range chiffrée, sans activer le second facteur : il ne
    /// l'est qu'une fois un code reconnu (<see cref="ActiverAsync"/>). Rappeler la
    /// méthode remplace la clé — un QR code mal scanné se recommence.
    /// </summary>
    public async Task<PreparationSecondFacteurDto?> PreparerAsync(Utilisateur utilisateur)
    {
        var cle = Totp.VersBase32(RandomNumberGenerator.GetBytes(Totp.OctetsCle));

        if (!await EcrireAsync(utilisateur, JetonCle, _protecteurCle.Protect(cle))) return null;
        if (!await EffacerAsync(utilisateur, JetonDernierPas)) return null;

        var compte = utilisateur.Email ?? utilisateur.Id.ToString();
        var uri = $"otpauth://totp/{Uri.EscapeDataString(Emetteur)}:{Uri.EscapeDataString(compte)}"
                  + $"?secret={cle}&issuer={Uri.EscapeDataString(Emetteur)}"
                  + "&algorithm=SHA1&digits=6&period=30";

        var qr = QrCode.EncodeText(uri, QrCode.Ecc.Medium);

        return new PreparationSecondFacteurDto
        {
            Cle = string.Join(' ', cle.Chunk(4).Select(groupe => new string(groupe))),
            Uri = uri,
            Modules = Enumerable.Range(0, qr.Size)
                .Select(y => Enumerable.Range(0, qr.Size).Select(x => qr.GetModule(x, y)).ToArray())
                .ToArray()
        };
    }

    /// <summary>
    /// Active le second facteur si le code prouve que le téléphone produit les bons
    /// codes, et rend les codes de récupération. <c>null</c> sur un code refusé.
    ///
    /// ⚠️ <c>SetTwoFactorEnabledAsync</c> régénère l'empreinte de sécurité : toutes les
    /// sessions du compte meurent, celle de l'appelant comprise. Au contrôleur de la
    /// rouvrir, avec <b>cette</b> instance du compte.
    /// </summary>
    public async Task<IReadOnlyList<string>?> ActiverAsync(Utilisateur utilisateur, string? saisie)
    {
        if (!await VerifierCodeAsync(utilisateur, saisie)) return null;

        var activation = await _utilisateurs.SetTwoFactorEnabledAsync(utilisateur, true);
        if (!activation.Succeeded) return null;

        return await NouveauxCodesAsync(utilisateur);
    }

    /// <summary>
    /// Retire le second facteur : la clé, les codes, le dernier pas, et l'indicateur —
    /// qui régénère l'empreinte de sécurité, donc périme les jetons d'accès du compte.
    /// </summary>
    public async Task<bool> RetirerAsync(Utilisateur utilisateur)
    {
        var retrait = await _utilisateurs.SetTwoFactorEnabledAsync(utilisateur, false);

        return retrait.Succeeded
               && await EffacerAsync(utilisateur, JetonCle)
               && await EffacerAsync(utilisateur, JetonCodes)
               && await EffacerAsync(utilisateur, JetonDernierPas);
    }

    // ---- Vérification -----------------------------------------------------------------

    /// <summary>
    /// Vrai si la saisie est le code d'un pas de la fenêtre <b>postérieur</b> au dernier
    /// accepté — un code ne sert qu'une fois —, et ce pas est alors mémorisé.
    ///
    /// ⚠️ L'écriture du pas passe par <c>UpdateAsync</c> d'Identity, qui compare le
    /// <c>ConcurrencyStamp</c> du compte : de deux saisies simultanées du même code, la
    /// seconde échoue à l'écriture et le code est refusé.
    /// </summary>
    public async Task<bool> VerifierCodeAsync(Utilisateur utilisateur, string? saisie)
    {
        if (Totp.LireCode(saisie) is not { } code) return false;
        if (await CleAsync(utilisateur) is not { } cle) return false;

        var dernier = long.TryParse(await LireAsync(utilisateur, JetonDernierPas), out var lu) ? lu : long.MinValue;
        var maintenant = Totp.Pas(_horloge.GetUtcNow());
        var attendu = Encoding.ASCII.GetBytes(code.ToString("D6"));

        for (var pas = maintenant - PasTolere; pas <= maintenant + PasTolere; pas++)
        {
            if (pas <= dernier) continue;

            var calcule = Encoding.ASCII.GetBytes(Totp.Code(cle, pas).ToString("D6"));

            if (CryptographicOperations.FixedTimeEquals(calcule, attendu))
            {
                return await EcrireAsync(utilisateur, JetonDernierPas, pas.ToString());
            }
        }

        return false;
    }

    /// <summary>
    /// Consomme un code de récupération : vrai s'il figurait parmi ceux qui restent, et
    /// il n'y figure plus. Même garde de concurrence que <see cref="VerifierCodeAsync"/> :
    /// deux usages simultanés du même code n'en valident qu'un.
    /// </summary>
    public async Task<bool> ConsommerCodeRecuperationAsync(Utilisateur utilisateur, string? saisie)
    {
        if (Normaliser(saisie) is not { } normalise) return false;

        var empreintes = Empreintes(await LireAsync(utilisateur, JetonCodes));
        var cherchee = Encoding.ASCII.GetBytes(Empreinte(normalise));

        var trouvee = empreintes.FindIndex(e =>
            CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(e), cherchee));

        if (trouvee < 0) return false;

        empreintes.RemoveAt(trouvee);

        return await EcrireAsync(utilisateur, JetonCodes, string.Join(';', empreintes));
    }

    /// <summary>Dix codes neufs, qui remplacent tous les précédents.</summary>
    public async Task<IReadOnlyList<string>?> NouveauxCodesAsync(Utilisateur utilisateur)
    {
        var codes = Enumerable.Range(0, NombreCodesRecuperation)
            .Select(_ => new string(RandomNumberGenerator.GetItems<char>(AlphabetCodes, LongueurCode)))
            .ToList();

        var ecrit = await EcrireAsync(
            utilisateur, JetonCodes, string.Join(';', codes.Select(Empreinte)));

        return ecrit
            ? codes.Select(code => string.Join('-', code.Chunk(4).Select(groupe => new string(groupe)))).ToList()
            : null;
    }

    public async Task<int> CodesRestantsAsync(Utilisateur utilisateur) =>
        Empreintes(await LireAsync(utilisateur, JetonCodes)).Count;

    // ---- Rangement --------------------------------------------------------------------

    private async Task<byte[]?> CleAsync(Utilisateur utilisateur)
    {
        var protegee = await LireAsync(utilisateur, JetonCle);
        if (protegee is null) return null;

        try
        {
            return Totp.DepuisBase32(_protecteurCle.Unprotect(protegee));
        }
        catch (CryptographicException erreur)
        {
            // Une erreur, pas un code refusé : répondre « code faux » enverrait
            // l'utilisateur réessayer un code juste, jusqu'au verrouillage. La cause est
            // chez l'exploitant, et c'est dans son journal qu'elle doit apparaître.
            throw new InvalidOperationException(
                "La clé du second facteur ne se déchiffre plus : le trousseau de la Data "
                + "Protection a été perdu ou remplacé (volume cles-api, DEPLOIEMENT.md). "
                + "Réinitialiser le second facteur des comptes concernés.", erreur);
        }
    }

    private Task<string?> LireAsync(Utilisateur utilisateur, string nom) =>
        _utilisateurs.GetAuthenticationTokenAsync(utilisateur, Fournisseur, nom);

    private async Task<bool> EcrireAsync(Utilisateur utilisateur, string nom, string valeur) =>
        (await _utilisateurs.SetAuthenticationTokenAsync(utilisateur, Fournisseur, nom, valeur)).Succeeded;

    private async Task<bool> EffacerAsync(Utilisateur utilisateur, string nom) =>
        (await _utilisateurs.RemoveAuthenticationTokenAsync(utilisateur, Fournisseur, nom)).Succeeded;

    private static List<string> Empreintes(string? rangees) =>
        string.IsNullOrEmpty(rangees) ? [] : [.. rangees.Split(';')];

    /// <summary>
    /// Majuscules, sans tirets ni espaces, et les confusions de Crockford ramenées :
    /// O vaut 0, I et L valent 1. <c>null</c> si la saisie n'a pas la forme d'un code.
    /// </summary>
    private static string? Normaliser(string? saisie)
    {
        if (saisie is null) return null;

        var normalise = new string(saisie.ToUpperInvariant()
            .Where(c => c is not ('-' or ' '))
            .Select(c => c switch { 'O' => '0', 'I' or 'L' => '1', _ => c })
            .ToArray());

        return normalise.Length == LongueurCode && normalise.All(AlphabetCodes.Contains)
            ? normalise
            : null;
    }

    private static string Empreinte(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(code)));
}
