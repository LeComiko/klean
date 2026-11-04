using Klean.Api.Models;
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
    /// En Métropole, le territoire par défaut. Une table qui recenserait les fêtes
    /// régionales sans territoire ferait sauter ces jours partout.
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

    [Fact]
    public void L_Alsace_Moselle_ajoute_le_Vendredi_saint_et_la_Saint_Etienne()
    {
        var feries = JoursFeries.De(2026, Territoire.AlsaceMoselle);

        Assert.Equal(13, feries.Count);
        Assert.Equal("Vendredi saint", feries[new DateOnly(2026, 4, 3)]);
        Assert.Equal("Saint-Étienne", feries[new DateOnly(2026, 12, 26)]);
        Assert.Equal("Vendredi saint", JoursFeries.Nom(new DateOnly(2027, 3, 26), Territoire.AlsaceMoselle));
    }

    /// <summary>Décret n° 83-1003 : une date par département.</summary>
    [Theory]
    [InlineData(Territoire.Guadeloupe, 5, 27)]
    [InlineData(Territoire.Martinique, 5, 22)]
    [InlineData(Territoire.Guyane, 6, 10)]
    [InlineData(Territoire.Reunion, 12, 20)]
    [InlineData(Territoire.Mayotte, 4, 27)]
    public void Chaque_departement_d_outre_mer_commemore_l_abolition_a_sa_date(Territoire territoire, int mois, int jour)
    {
        var feries = JoursFeries.De(2026, territoire);

        Assert.Equal(12, feries.Count);
        Assert.Equal("Abolition de l'esclavage", feries[new DateOnly(2026, mois, jour)]);
        Assert.Null(JoursFeries.Nom(new DateOnly(2026, mois, jour)));
    }

    [Fact]
    public void Chaque_territoire_garde_les_onze_jours_nationaux()
    {
        var nationaux = JoursFeries.De(2026).Keys.ToHashSet();

        foreach (var territoire in Enum.GetValues<Territoire>())
        {
            Assert.Superset(nationaux, JoursFeries.De(2026, territoire).Keys.ToHashSet());
        }
    }

    [Theory]
    [InlineData(Territoire.Metropole, 24)]
    [InlineData(Territoire.AlsaceMoselle, 24)]
    [InlineData(Territoire.Reunion, 24)]
    [InlineData(Territoire.Guadeloupe, 23)]
    [InlineData(Territoire.Guyane, 23)]
    public void Aujourd_hui_est_le_jour_du_territoire(Territoire territoire, int jourAttendu)
    {
        // Dimanche 23 mai 2027, 23 h UTC.
        var horloge = new HorlogeFixe(new DateTimeOffset(2027, 5, 23, 23, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2027, 5, jourAttendu), Territoires.Aujourdhui(horloge, territoire));
    }

    [Fact]
    public void Tous_les_fuseaux_se_resolvent_sur_cette_machine()
    {
        Territoires.VerifierFuseaux();
    }

    private sealed class HorlogeFixe(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
