using System.ComponentModel.DataAnnotations;

namespace Klean.Api.Models;

/// <summary>
/// Jours de passage d'une prestation. Énumération à drapeaux, stockée en texte
/// comme les cinq autres (CLAUDE.md §5) : « Lundi, Mercredi, Vendredi ».
/// </summary>
[Flags]
public enum JoursSemaine
{
    /// <summary>Prestation jamais générée : ses vacations se saisissent à la main.</summary>
    Aucun = 0,
    Lundi = 1,
    Mardi = 2,
    Mercredi = 4,
    Jeudi = 8,
    Vendredi = 16,
    Samedi = 32,
    Dimanche = 64
}

/// <summary>Passage entre <see cref="JoursSemaine"/>, sa forme d'API et le calendrier.</summary>
public static class JoursDeLaSemaine
{
    /// <summary>Du lundi au dimanche — l'ordre d'affichage, pas celui de <see cref="DayOfWeek"/>.</summary>
    public static readonly JoursSemaine[] Ordre =
    [
        JoursSemaine.Lundi, JoursSemaine.Mardi, JoursSemaine.Mercredi, JoursSemaine.Jeudi,
        JoursSemaine.Vendredi, JoursSemaine.Samedi, JoursSemaine.Dimanche
    ];

    public static string[] EnListe(JoursSemaine jours) =>
        Ordre.Where(jour => jours.HasFlag(jour)).Select(jour => jour.ToString()).ToArray();

    /// <summary>
    /// Lit la liste reçue par l'API. Rend le premier nom inconnu, pour que le refus
    /// le cite. ⚠️ <c>Enum.TryParse</c> seul ne suffit pas : il accepte « 3 », « Aucun »
    /// et « Lundi, Mardi » en une seule chaîne — trois formes que l'écran n'envoie
    /// jamais et qui produiraient une valeur que personne n'a cochée.
    /// </summary>
    public static bool TryLire(IEnumerable<string> noms, out JoursSemaine jours, out string? inconnu)
    {
        jours = JoursSemaine.Aucun;
        inconnu = null;

        foreach (var nom in noms)
        {
            var jour = Ordre.FirstOrDefault(j => j.ToString() == nom);
            if (jour == JoursSemaine.Aucun)
            {
                inconnu = nom;
                return false;
            }

            jours |= jour;
        }

        return true;
    }

    public static JoursSemaine De(DayOfWeek jour) => jour switch
    {
        DayOfWeek.Monday => JoursSemaine.Lundi,
        DayOfWeek.Tuesday => JoursSemaine.Mardi,
        DayOfWeek.Wednesday => JoursSemaine.Mercredi,
        DayOfWeek.Thursday => JoursSemaine.Jeudi,
        DayOfWeek.Friday => JoursSemaine.Vendredi,
        DayOfWeek.Saturday => JoursSemaine.Samedi,
        _ => JoursSemaine.Dimanche
    };
}

/// <summary>
/// Tâche récurrente prévue par un contrat. <see cref="EntrepriseId"/> dénormalisé,
/// sans clé étrangère.
/// </summary>
public class Prestation
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid EntrepriseId { get; set; }

    [Required]
    public Guid ContratId { get; set; }
    public Contrat? Contrat { get; set; }

    [Required]
    [MaxLength(200)]
    public string Libelle { get; set; } = string.Empty;

    /// <summary>Alimente le calcul de charge hebdomadaire du moteur d'affectation.</summary>
    public int DureeEstimeeMinutes { get; set; }

    /// <summary>
    /// Jours où la génération crée une vacation. <see cref="JoursSemaine.Aucun"/> pour
    /// toute prestation antérieure à la génération : elle continue de se planifier à
    /// la main, exactement comme avant.
    /// </summary>
    public JoursSemaine Jours { get; set; } = JoursSemaine.Aucun;

    /// <summary>Heure de passage. Requise dès qu'un jour est coché.</summary>
    public TimeSpan? HeureDebut { get; set; }

    /// <summary>
    /// Agent affecté par la génération, s'il est éligible ce jour-là — sinon la vacation
    /// reste non pourvue. Aucun chemin n'affecte un agent inéligible (CLAUDE.md §9).
    /// </summary>
    public Guid? AgentTitulaireId { get; set; }
    public Agent? AgentTitulaire { get; set; }

    /// <summary>
    /// Le type du catalogue dont la prestation est issue, nul pour une prestation saisie
    /// librement ou antérieure au catalogue (SPEC.md, étape 21).
    ///
    /// ⚠️ Le <see cref="Libelle"/> est une <b>copie</b>, pas un renvoi : renommer un type ne
    /// réécrit ni les prestations ni l'historique de leurs vacations, et un site garde son
    /// « Hall + escalier B ». La clé, elle, sert au stock et au devis.
    /// </summary>
    public Guid? TypePrestationId { get; set; }
    public TypePrestation? TypePrestation { get; set; }

    public ICollection<Intervention> Interventions { get; set; } = new List<Intervention>();
}
