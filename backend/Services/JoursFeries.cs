namespace Klean.Api.Services;

/// <summary>
/// Les onze jours fériés nationaux français (article L3133-1 du Code du travail).
///
/// Huit sont à date fixe ; trois dépendent de Pâques — le lundi de Pâques, l'Ascension
/// et le lundi de Pentecôte —, d'où le calcul de la date de Pâques plutôt qu'une table
/// recopiée, qui s'arrêterait à l'année où quelqu'un a cessé de la tenir.
///
/// ⚠️ <b>Hors périmètre de la V1 : l'Alsace-Moselle</b>, qui ajoute le Vendredi saint et
/// la Saint-Étienne (26 décembre), et les jours fériés propres aux départements
/// d'outre-mer, dont l'abolition de l'esclavage. Une entreprise concernée marque ses
/// contrats <c>InclutJoursFeries</c> et saisit ces jours à la main.
///
/// Le lundi de Pentecôte reste férié ici même là où il sert de journée de solidarité :
/// c'est la loi qui le dit férié, et un contrat qui y travaille se marque comme les autres.
/// </summary>
public static class JoursFeries
{
    /// <summary>Le nom du jour férié tombant ce jour-là, ou <c>null</c>.</summary>
    public static string? Nom(DateOnly jour) =>
        De(jour.Year).TryGetValue(jour, out var nom) ? nom : null;

    /// <summary>Les onze jours fériés d'une année, avec leur nom.</summary>
    public static IReadOnlyDictionary<DateOnly, string> De(int annee)
    {
        var paques = Paques(annee);

        return new Dictionary<DateOnly, string>
        {
            [new DateOnly(annee, 1, 1)] = "Jour de l'an",
            [paques.AddDays(1)] = "Lundi de Pâques",
            [new DateOnly(annee, 5, 1)] = "Fête du Travail",
            [new DateOnly(annee, 5, 8)] = "Victoire 1945",
            [paques.AddDays(39)] = "Ascension",
            [paques.AddDays(50)] = "Lundi de Pentecôte",
            [new DateOnly(annee, 7, 14)] = "Fête nationale",
            [new DateOnly(annee, 8, 15)] = "Assomption",
            [new DateOnly(annee, 11, 1)] = "Toussaint",
            [new DateOnly(annee, 11, 11)] = "Armistice 1918",
            [new DateOnly(annee, 12, 25)] = "Noël"
        };
    }

    /// <summary>
    /// Dimanche de Pâques du calendrier grégorien, par l'algorithme anonyme dit de
    /// Meeus, Jones et Butcher. Exact pour toute année grégorienne, sans table.
    /// </summary>
    public static DateOnly Paques(int annee)
    {
        var a = annee % 19;
        var b = annee / 100;
        var c = annee % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var mois = (h + l - 7 * m + 114) / 31;
        var jour = (h + l - 7 * m + 114) % 31 + 1;

        return new DateOnly(annee, mois, jour);
    }
}
