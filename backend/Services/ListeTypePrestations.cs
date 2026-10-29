namespace Klean.Api.Services;

/// <summary>
/// La liste type du catalogue, que l'administrateur importe d'un geste
/// (SPEC.md, étape 21, lot B).
///
/// Les libellés viennent des cahiers des charges de copropriété et de bureaux. Les
/// durées sont des <b>hypothèses</b> pour un immeuble moyen, validées le 29 octobre 2026 :
/// aucune source ne publie de durée par type, la profession chiffre au m² ou au forfait.
/// Seules « Bureaux » et « Sanitaires » s'appuient sur une cadence publiée, de 250 à
/// 400 m²/h et de 100 à 150 m²/h.
///
/// ⚠️ Elles ne font que <b>préremplir</b> : chaque type se corrige dans le catalogue,
/// chaque prestation garde la sienne, et rejouer l'import n'écrase rien. Mais une durée
/// laissée telle quelle devient celle de la charge de la semaine, donc du classement des
/// remplaçants (CLAUDE.md §8).
///
/// Sortie et rentrée des conteneurs sont deux types, non une paire : la veille au soir et
/// le lendemain matin, selon le calendrier de collecte de chaque commune.
/// </summary>
public static class ListeTypePrestations
{
    public static readonly IReadOnlyList<(string Libelle, int DureeMinutes)> Elements =
    [
        // Parties communes de copropriété
        ("Hall d'entrée", 20),
        ("Escaliers et paliers", 30),
        ("Ascenseur", 10),
        ("Boîtes aux lettres", 10),
        ("Local poubelles", 20),
        ("Sortie des conteneurs", 15),
        ("Rentrée des conteneurs", 15),
        ("Lavage des conteneurs", 30),
        ("Encombrants", 15),
        ("Caves et couloirs de caves", 30),
        ("Parking", 60),
        ("Vitrerie des parties communes", 60),
        ("Abords extérieurs et cour", 30),

        // Bureaux
        ("Bureaux", 60),
        ("Sanitaires", 15),
        ("Espace pause et cuisine", 20),

        // Ponctuel
        ("Remise en état", 180)
    ];
}
