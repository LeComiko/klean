using System.Security.Cryptography;

namespace Klean.Api.Tests.Infrastructure;

/// <summary>
/// Ce que fait le téléphone : produire le code d'un instant à partir de la clé affichée.
///
/// ⚠️ <b>Écrit sans rien reprendre de <c>Totp</c></b>, le calcul du serveur. Un test qui
/// fabriquerait ses codes avec la fonction qu'il éprouve resterait vert si elle était
/// fausse, puisqu'elle se donnerait raison à elle-même. Les deux implémentations sont
/// ancrées séparément sur les vecteurs de la RFC 6238.
/// </summary>
public static class TotpDeTest
{
    public static string Code(string cleBase32, DateTimeOffset instant)
    {
        var cle = DecoderBase32(cleBase32.Replace(" ", string.Empty));
        var pas = instant.ToUnixTimeSeconds() / 30;

        var compteur = BitConverter.GetBytes(pas);
        if (BitConverter.IsLittleEndian) Array.Reverse(compteur);

        var empreinte = new HMACSHA1(cle).ComputeHash(compteur);
        var decalage = empreinte[^1] & 0x0F;

        var nombre = ((empreinte[decalage] & 0x7F) << 24)
                     | (empreinte[decalage + 1] << 16)
                     | (empreinte[decalage + 2] << 8)
                     | empreinte[decalage + 3];

        return (nombre % 1_000_000).ToString("D6");
    }

    private static byte[] DecoderBase32(string texte)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        // Chaîne de bits, puis octets : lent et évident, ce qu'on attend d'un témoin.
        var bits = string.Concat(texte.ToUpperInvariant()
            .Select(c => Convert.ToString(alphabet.IndexOf(c), 2).PadLeft(5, '0')));

        return Enumerable.Range(0, bits.Length / 8)
            .Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2))
            .ToArray();
    }
}
