using Klean.Api.Services;
using Klean.Api.Tests.Infrastructure;

namespace Klean.Api.Tests;

/// <summary>
/// Le retrait des métadonnées d'un JPEG — étape 25, lot B. ⚠️ C'est la garde qui empêche une
/// photo d'anomalie de faire entrer en base la position GPS de sa prise : le pointage refuse
/// d'enregistrer une coordonnée, la photo ne doit pas en apporter une par la bande.
/// </summary>
public class MetadonneesJpegTests
{
    [Fact]
    public void Une_photo_perd_son_exif_son_xmp_et_son_commentaire()
    {
        var nettoyee = MetadonneesJpeg.Nettoyer(JpegDeTest.AvecPosition());

        Assert.NotNull(nettoyee);
        Assert.False(JpegDeTest.Contient(nettoyee, JpegDeTest.Position));
        Assert.False(JpegDeTest.Contient(nettoyee, "Exif"));
        Assert.False(JpegDeTest.Contient(nettoyee, "rue de Paul"));
    }

    [Fact]
    public void Tout_le_reste_est_recopie_octet_pour_octet()
    {
        // L'en-tête JFIF, les tables, la trame, les deux balayages et leurs données — avec
        // l'octet FF échappé et la reprise qu'elles contiennent — et la fin.
        Assert.Equal(JpegDeTest.SansPosition(), MetadonneesJpeg.Nettoyer(JpegDeTest.AvecPosition()));
    }

    [Fact]
    public void Un_exif_glisse_entre_deux_balayages_disparait_aussi()
    {
        // ⚠️ Recopier d'un bloc tout ce qui suit le premier balayage l'aurait laissé passer.
        byte[] progressif =
        [
            0xFF, 0xD8, .. JpegDeTest.Trame,
            .. JpegDeTest.Balayage, .. JpegDeTest.DonneesImage,
            .. JpegDeTest.Exif,
            .. JpegDeTest.Balayage, .. JpegDeTest.DonneesImage,
            0xFF, 0xD9
        ];

        Assert.False(JpegDeTest.Contient(MetadonneesJpeg.Nettoyer(progressif)!, JpegDeTest.Position));
    }

    [Fact]
    public void Nettoyer_deux_fois_ne_change_plus_rien()
    {
        var une = MetadonneesJpeg.Nettoyer(JpegDeTest.AvecPosition())!;

        Assert.Equal(une, MetadonneesJpeg.Nettoyer(une));
    }

    public static TheoryData<string, byte[]> Illisibles => new()
    {
        { "un PNG", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A] },
        { "trop court", [0xFF, 0xD8] },
        { "sans fin", [0xFF, 0xD8, .. JpegDeTest.Trame, .. JpegDeTest.Balayage, .. JpegDeTest.DonneesImage] },
        { "sans image", [0xFF, 0xD8, .. JpegDeTest.Jfif, .. JpegDeTest.Trame, 0xFF, 0xD9] },
        { "un segment qui déborde", [0xFF, 0xD8, 0xFF, 0xE1, 0x40, 0x00, 0x45, 0x78, 0xFF, 0xD9] },
        // Un segment recopié, et non sauté : c'est lui que la borne protège d'une lecture hors du tableau.
        { "une table qui déborde", [0xFF, 0xD8, 0xFF, 0xDB, 0x40, 0x00, 0x00, 0x01, 0xFF, 0xD9] },
        { "un octet hors segment", [0xFF, 0xD8, 0x00, .. JpegDeTest.Balayage, 0xFF, 0xD9] },
        { "une longueur nulle", [0xFF, 0xD8, 0xFF, 0xE1, 0x00, 0x00, .. JpegDeTest.Balayage, 0xFF, 0xD9] }
    };

    [Theory]
    [MemberData(nameof(Illisibles))]
    public void Ce_qui_n_est_pas_un_jpeg_lisible_est_refuse(string cas, byte[] octets)
    {
        Assert.True(MetadonneesJpeg.Nettoyer(octets) is null, cas);
    }
}
