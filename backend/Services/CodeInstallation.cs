using System.Security.Cryptography;
using System.Text;

namespace Klean.Api.Services;

/// <summary>
/// Le code d'installation (SPEC.md, étape 34, lot A2) : la seule garde de
/// <c>POST /api/installation</c> entre le démarrage et la première visite. Les journaux de
/// transparence des certificats publient un domaine en quelques minutes : sans code, quiconque
/// le lirait pourrait s'emparer de l'entreprise.
///
/// Vingt symboles de <see cref="Alphabet"/> — celui de <c>generer_code</c> dans
/// <c>deploiement/fonctions.sh</c>, sans I, L, O, 0 ni 1 —, environ 100 bits. La saisie est
/// normalisée (casse, espaces, tirets) puis comparée par empreinte, en temps constant.
///
/// ⚠️ <b>Un code configuré hors de cette forme fait refuser le démarrage</b> : configuré à
/// moitié, comme un SMTP incomplet (CLAUDE.md §2) — et un code recopié d'un exemple serait
/// connu de tous. Absent, il n'est simplement pas configuré.
///
/// ⚠️ Seule l'empreinte est gardée <b>par cette classe</b> ; la valeur brute reste dans
/// <c>IConfiguration</c> et dans l'environnement du conteneur tant que le script ne l'a pas
/// retirée de <c>.env</c>, ce qu'il fait dès qu'un compte existe.
/// </summary>
public sealed class CodeInstallation
{
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int Longueur = 20;

    /// <summary>Au-delà, une saisie n'est même pas hachée : aucun code n'est si long.</summary>
    private const int LongueurMaximaleSaisie = 100;

    private readonly byte[]? _empreinte;

    private CodeInstallation(byte[]? empreinte) => _empreinte = empreinte;

    public bool Configure => _empreinte is not null;

    /// <summary>Retire espaces et tirets — y compris ceux qu'un copier-coller apporte — et met en majuscules.</summary>
    public static string Normaliser(string saisie) =>
        new(saisie.Where(c => !char.IsWhiteSpace(c) && c is not ('-' or '‐' or '‑' or '‒' or '–' or '—'))
            .Select(char.ToUpperInvariant).ToArray());

    /// <summary>Un code neuf. Pour la série de tests : le script tire le sien.</summary>
    public static string Tirer() =>
        new(Enumerable.Range(0, Longueur).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray());

    public static CodeInstallation Lire(IConfiguration configuration)
    {
        var brut = configuration["Installation:Code"];

        if (string.IsNullOrWhiteSpace(brut)) return new CodeInstallation(null);

        var code = Normaliser(brut);

        if (code.Length != Longueur || code.Any(c => !Alphabet.Contains(c)))
        {
            throw new InvalidOperationException(
                $"Installation:Code doit compter {Longueur} symboles parmi {Alphabet} : configuré à moitié. "
                + "Le script en tire un (KLEAN_CODE_INSTALLATION dans .env). À la main : "
                + "tr -dc 'ABCDEFGHJKMNPQRSTUVWXYZ23456789' < /dev/urandom | head -c 20 ; "
                + "sans assistant, retirer la variable.");
        }

        return new CodeInstallation(Empreinte(code));
    }

    public bool Correspond(string? saisie)
    {
        if (_empreinte is null || string.IsNullOrEmpty(saisie) || saisie.Length > LongueurMaximaleSaisie)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Empreinte(Normaliser(saisie)), _empreinte);
    }

    private static byte[] Empreinte(string code) => SHA256.HashData(Encoding.UTF8.GetBytes(code));
}
