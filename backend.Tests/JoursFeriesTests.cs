using Klean.Api.Services;

namespace Klean.Api.Tests;

/// <summary>
/// Les onze jours fériés nationaux. Pur calcul, sans base : la série d'API éprouve ce que
/// la génération en fait.
///
/// Les dates de Pâques attendues sont des faits publiés, pas recalculées ici — dont les
/// deux extrêmes du calendrier grégorien, le 22 mars et le 25 avril, où un algorithme
/// approché se trompe le premier.
/// </summary>
public class JoursFeriesTests
{
    [Theory]
    [InlineData(2008, 3, 23)]
    [InlineData(2019, 4, 21)]
    [InlineData(2024, 3, 31)]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 5)]
    [InlineData(2027, 3, 28)]
    [InlineData(2028, 4, 16)]
    [InlineData(2038, 4, 25)]
    [InlineData(2285, 3, 22)]
    public void Paques_tombe_a_la_date_publiee(int annee, int mois, int jour)
    {
        Assert.Equal(new DateOnly(annee, mois, jour), JoursFeries.Paques(annee));
    }

    [Fact]
    public void Onze_jours_feries_en_2026_aux_dates_legales()
    {
        var feries = JoursFeries.De(2026);

        Assert.Equal(
        [
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 4, 6),
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 8),
            new DateOnly(2026, 5, 14),
            new DateOnly(2026, 5, 25),
            new DateOnly(2026, 7, 14),
            new DateOnly(2026, 8, 15),
            new DateOnly(2026, 11, 1),
            new DateOnly(2026, 11, 11),
            new DateOnly(2026, 12, 25)
        ], feries.Keys.Order());
    }

    [Theory]
    [InlineData(2027, 3, 29, "Lundi de Pâques")]
    [InlineData(2027, 5, 6, "Ascension")]
    [InlineData(2027, 5, 17, "Lundi de Pentecôte")]
    [InlineData(2027, 5, 8, "Victoire 1945")]
    public void Les_feries_mobiles_suivent_Paques(int annee, int mois, int jour, string nom)
    {
        Assert.Equal(nom, JoursFeries.Nom(new DateOnly(annee, mois, jour)));
    }

    /// <summary>
    /// Une table qui recense aussi les fêtes régionales ferait sauter ces jours partout.
    /// L'Alsace-Moselle est hors périmètre de la V1 : ses entreprises marquent leurs
    /// contrats et saisissent ces jours à la main.
    /// </summary>
    [Theory]
    [InlineData(2026, 4, 3)]
    [InlineData(2026, 12, 26)]
    [InlineData(2026, 4, 5)]
    [InlineData(2026, 5, 24)]
    public void Vendredi_saint_Saint_Etienne_et_dimanches_ne_sont_pas_feries(int annee, int mois, int jour)
    {
        Assert.Null(JoursFeries.Nom(new DateOnly(annee, mois, jour)));
    }
}
