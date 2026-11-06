using System.Text;

namespace Klean.Api.Tests.Infrastructure;

/// <summary>
/// Des JPEG construits segment par segment, pour éprouver le retrait des métadonnées sans
/// bibliothèque d'images. Ils ne se décodent pas en une image utile : seule leur structure
/// compte, et c'est elle que <c>MetadonneesJpeg</c> lit.
/// </summary>
public static class JpegDeTest
{
    /// <summary>Ce que porte l'EXIF d'une photo de téléphone, et qui ne doit jamais survivre.</summary>
    public const string Position = "GPS 48.8530N 2.3499E";

    public static readonly byte[] DonneesImage = [0x12, 0xFF, 0x00, 0x34, 0xFF, 0xD0, 0x56];

    public static byte[] Segment(byte marqueur, byte[] contenu)
    {
        var longueur = contenu.Length + 2;
        return [0xFF, marqueur, (byte)(longueur >> 8), (byte)longueur, .. contenu];
    }

    public static byte[] Jfif => Segment(0xE0, Encoding.ASCII.GetBytes("JFIF\0\x01\x01"));
    public static byte[] Exif => Segment(0xE1, Encoding.ASCII.GetBytes("Exif\0\0" + Position));
    public static byte[] Xmp => Segment(0xE1, Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0" + Position));
    public static byte[] Commentaire => Segment(0xFE, Encoding.ASCII.GetBytes("pris rue de Paul " + Position));
    public static byte[] Quantification => Segment(0xDB, [0x00, 0x01, 0x02, 0x03]);
    public static byte[] Trame => Segment(0xC0, [0x08, 0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x11, 0x00]);
    public static byte[] Balayage => Segment(0xDA, [0x01, 0x01, 0x00, 0x00, 0x3F, 0x00]);

    /// <summary>
    /// Une photo de téléphone, telle qu'une requête forgée l'enverrait : EXIF, XMP, un
    /// commentaire, et un second EXIF glissé entre deux balayages, à la façon d'un JPEG
    /// progressif.
    /// </summary>
    public static byte[] AvecPosition() =>
    [
        0xFF, 0xD8,
        .. Jfif, .. Exif, .. Xmp, .. Commentaire, .. Quantification, .. Trame,
        .. Balayage, .. DonneesImage,
        .. Exif,
        .. Balayage, .. DonneesImage,
        0xFF, 0xD9
    ];

    /// <summary>La même, sans rien de ce qui doit disparaître.</summary>
    public static byte[] SansPosition() =>
    [
        0xFF, 0xD8,
        .. Jfif, .. Quantification, .. Trame,
        .. Balayage, .. DonneesImage,
        .. Balayage, .. DonneesImage,
        0xFF, 0xD9
    ];

    public static bool Contient(byte[] octets, string texte) =>
        octets.AsSpan().IndexOf(Encoding.ASCII.GetBytes(texte)) >= 0;
}
