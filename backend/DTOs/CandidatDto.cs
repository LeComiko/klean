namespace Klean.Api.DTOs;

/// <summary>
/// Un agent candidat à une intervention, avec son classement.
///
/// Les trois notes <b>et</b> les données brutes qui les produisent sont
/// exposées (CLAUDE.md §8) : un responsable qui ne voit qu'un score ne peut
/// ni le contester ni apprendre à s'en méfier. « 12 km » se discute,
/// « note 60 » ne se discute pas.
/// </summary>
public class CandidatDto
{
    public Guid AgentId { get; set; }
    public string Nom { get; set; } = string.Empty;
    public string Prenom { get; set; } = string.Empty;

    /// <summary>Moyenne pondérée des trois notes, sur 100.</summary>
    public double Score { get; set; }

    public double NoteExperience { get; set; }
    public double NoteDistance { get; set; }
    public double NoteCharge { get; set; }

    /// <summary>Passages déjà effectués par cet agent sur ce site.</summary>
    public int Passages { get; set; }

    /// <summary>
    /// Distance à vol d'oiseau entre l'agent et le site. Null quand l'agent
    /// n'a pas de coordonnées : la note vaut alors 50, la valeur neutre.
    /// </summary>
    public double? DistanceKm { get; set; }

    /// <summary>Heures déjà engagées sur la semaine de l'intervention.</summary>
    public double HeuresSemaine { get; set; }
}
