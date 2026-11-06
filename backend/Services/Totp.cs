using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Klean.Api.Services;

/// <summary>
/// Les codes à six chiffres des applications d'authentification — RFC 6238, sur
/// HMAC-SHA1 et des pas de trente secondes, les réglages que toutes lisent.
///
/// ⚠️ <b>Écrit ici plutôt que pris à Identity</b>, et ce n'est pas par goût.
/// <c>AuthenticatorTokenProvider</c> accepte deux pas de part et d'autre, donc cinq
/// codes à la fois, et ne mémorise rien : un code intercepté resservait pendant deux
/// minutes et demie. Refuser le rejeu demande de savoir <b>quel pas</b> a été reconnu,
/// ce qu'Identity ne dit pas (SPEC.md, étape 23). Le calcul est éprouvé sur les
/// vecteurs de l'annexe B de la RFC.
/// </summary>
public static class Totp
{
    public static readonly TimeSpan DureePas = TimeSpan.FromSeconds(30);

    /// <summary>Clé de 160 bits : la longueur de sortie de SHA-1, que recommande la RFC 4226.</summary>
    public const int OctetsCle = 20;

    /// <summary>Le numéro du pas de temps contenant cet instant.</summary>
    public static long Pas(DateTimeOffset instant) =>
        instant.ToUnixTimeSeconds() / (long)DureePas.TotalSeconds;

    /// <summary>Le code d'un pas, de 0 à 999 999.</summary>
    public static int Code(byte[] cle, long pas)
    {
        Span<byte> compteur = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(compteur, pas);

        Span<byte> empreinte = stackalloc byte[HMACSHA1.HashSizeInBytes];
        HMACSHA1.HashData(cle, compteur, empreinte);

        // Troncature dynamique (RFC 4226 §5.3) : les quatre bits de poids faible du
        // dernier octet désignent où lire, et le bit de signe est écarté.
        var decalage = empreinte[^1] & 0x0F;
        var binaire = ((empreinte[decalage] & 0x7F) << 24)
                      | (empreinte[decalage + 1] << 16)
                      | (empreinte[decalage + 2] << 8)
                      | empreinte[decalage + 3];

        return binaire % 1_000_000;
    }

    /// <summary>
    /// Lit une saisie à six chiffres, espaces tolérés — les applications affichent
    /// souvent « 123 456 ». Rend <c>null</c> sur toute autre forme.
    /// </summary>
    public static int? LireCode(string? saisie)
    {
        if (saisie is null) return null;

        var chiffres = saisie.Replace(" ", string.Empty).Trim();

        if (chiffres.Length != 6 || !chiffres.All(char.IsAsciiDigit)) return null;

        return int.Parse(chiffres);
    }

    private const string AlphabetBase32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Base32 de la RFC 4648, sans remplissage : la forme que lit l'URI <c>otpauth</c>.</summary>
    public static string VersBase32(byte[] octets)
    {
        var texte = new StringBuilder((octets.Length * 8 + 4) / 5);
        int tampon = 0, bits = 0;

        foreach (var octet in octets)
        {
            tampon = (tampon << 8) | octet;
            bits += 8;

            while (bits >= 5)
            {
                texte.Append(AlphabetBase32[(tampon >> (bits - 5)) & 0x1F]);
                bits -= 5;
            }
        }

        if (bits > 0) texte.Append(AlphabetBase32[(tampon << (5 - bits)) & 0x1F]);

        return texte.ToString();
    }

    public static byte[] DepuisBase32(string texte)
    {
        var octets = new List<byte>(texte.Length * 5 / 8);
        int tampon = 0, bits = 0;

        foreach (var caractere in texte)
        {
            var valeur = AlphabetBase32.IndexOf(char.ToUpperInvariant(caractere));
            if (valeur < 0) throw new FormatException($"« {caractere} » n'appartient pas à l'alphabet Base32.");

            tampon = (tampon << 5) | valeur;
            bits += 5;

            if (bits >= 8)
            {
                octets.Add((byte)((tampon >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. octets];
    }
}
