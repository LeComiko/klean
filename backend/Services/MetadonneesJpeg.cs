namespace Klean.Api.Services;

/// <summary>
/// Retire les métadonnées d'un JPEG, sans dépendance (SPEC.md, étape 25, lot B).
///
/// ⚠️ <b>Une photo de téléphone porte souvent la position GPS de sa prise</b>, dans son
/// segment EXIF. L'écran réduit la photo par un <c>canvas</c>, ce qui en retire déjà les
/// métadonnées ; le serveur ne s'y fie pas, puisqu'une requête forgée n'est pas passée par
/// l'écran.
///
/// Un JPEG est une suite de segments, chacun annoncé par un marqueur <c>FF xx</c> et, sauf
/// exceptions, une longueur sur deux octets. On recopie tout, sauf :
/// <list type="bullet">
/// <item><c>APP1</c> à <c>APP15</c> — EXIF, XMP, profils de couleur, données de fabricant ;</item>
/// <item><c>COM</c> — un commentaire libre.</item>
/// </list>
/// <c>APP0</c>, l'en-tête JFIF, reste : il ne porte que la densité de l'image.
///
/// ⚠️ <b>Les données de l'image sont lues, pas recopiées d'un bloc.</b> Après le début d'un
/// balayage (<c>SOS</c>), un JPEG progressif reprend des segments entre ses balayages ; un
/// fichier forgé pourrait y glisser un <c>APP1</c>. Dans les données compressées, un
/// <c>FF</c> est suivi de <c>00</c> ou d'un marqueur de reprise ; tout autre marqueur clôt le
/// balayage et repasse par la même lecture que l'en-tête.
/// </summary>
public static class MetadonneesJpeg
{
    private const byte Debut = 0xD8;          // SOI
    private const byte Fin = 0xD9;            // EOI
    private const byte Balayage = 0xDA;       // SOS
    private const byte Commentaire = 0xFE;    // COM

    /// <summary>
    /// Le JPEG sans ses métadonnées, ou nul si les octets ne forment pas un JPEG lisible :
    /// signature absente, segment tronqué, aucune image, ou pas de marqueur de fin.
    /// </summary>
    public static byte[]? Nettoyer(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != Debut) return null;

        using var sortie = new MemoryStream(jpeg.Length);
        sortie.Write([0xFF, Debut]);

        var vuUneImage = false;
        var i = 2;

        while (i < jpeg.Length)
        {
            if (jpeg[i] != 0xFF) return null;

            // Des octets de remplissage peuvent précéder un marqueur.
            while (i < jpeg.Length && jpeg[i] == 0xFF) i++;
            if (i >= jpeg.Length) return null;

            var marqueur = jpeg[i++];

            if (marqueur == Fin)
            {
                if (!vuUneImage) return null;

                sortie.Write([0xFF, Fin]);
                return sortie.ToArray();
            }

            if (marqueur is 0x00 or Debut) return null;

            // Marqueurs sans longueur : TEM, et les reprises RST0 à RST7.
            if (marqueur is 0x01 or >= 0xD0 and <= 0xD7)
            {
                sortie.Write([0xFF, marqueur]);
                continue;
            }

            if (i + 2 > jpeg.Length) return null;

            // La longueur compte ses deux propres octets, pas le marqueur.
            var longueur = (jpeg[i] << 8) | jpeg[i + 1];
            if (longueur < 2 || i + longueur > jpeg.Length) return null;

            if (!EstUneMetadonnee(marqueur))
            {
                sortie.Write([0xFF, marqueur]);
                sortie.Write(jpeg.Slice(i, longueur));
            }

            i += longueur;

            if (marqueur != Balayage) continue;

            vuUneImage = true;
            var debutDesDonnees = i;

            while (i < jpeg.Length)
            {
                if (jpeg[i] == 0xFF && i + 1 < jpeg.Length)
                {
                    var suivant = jpeg[i + 1];

                    // Un FF des données, échappé, ou une reprise : on continue le balayage.
                    if (suivant is 0x00 or >= 0xD0 and <= 0xD7)
                    {
                        i += 2;
                        continue;
                    }

                    break;
                }

                i++;
            }

            sortie.Write(jpeg[debutDesDonnees..i]);
        }

        return null;
    }

    private static bool EstUneMetadonnee(byte marqueur) => marqueur is >= 0xE1 and <= 0xEF or Commentaire;
}
