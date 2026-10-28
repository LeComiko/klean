namespace Klean.Api.DTOs;

/// <summary>Corps de <c>POST /api/planning/generer</c>.</summary>
public class GenerationDemandeDto
{
    /// <summary>
    /// Dernier jour généré, inclus. La génération part toujours d'aujourd'hui : elle ne
    /// crée jamais de vacation dans le passé, et au plus douze semaines à l'avance.
    /// </summary>
    public DateTime? JusquAu { get; set; }
}

/// <summary>
/// Ce que la génération a fait, et surtout ce qui demande un regard humain.
///
/// ⚠️ <b>Aucun nom d'agent</b>, ni ici ni dans les lignes. Le rapport part vers un écran
/// qui n'est pas journalisé (CLAUDE.md §7 bis) : il dit qu'une vacation n'a pas trouvé
/// son titulaire, pas qui il est ni pourquoi il manque — la cause mêle les quatre filtres
/// durs, comme le refus de l'affectation, et ne divulgue donc pas une absence.
/// </summary>
public class RapportGenerationDto
{
    /// <summary>Premier jour considéré : aujourd'hui.</summary>
    public DateTime Du { get; set; }

    /// <summary>Dernier jour considéré, inclus.</summary>
    public DateTime Au { get; set; }

    /// <summary>Vacations créées, pourvues ou non.</summary>
    public int Creees { get; set; }

    /// <summary>Parmi les créées, celles qui ont reçu le titulaire de leur prestation.</summary>
    public int Pourvues { get; set; }

    /// <summary>
    /// Créneaux déjà occupés par une vacation de la même prestation ce jour-là, quel que
    /// soit son statut — une vacation annulée n'est pas recréée. C'est l'idempotence.
    /// </summary>
    public int DejaPresentes { get; set; }

    /// <summary>Vacations créées sans agent, avec la raison.</summary>
    public List<LigneRapportGenerationDto> NonPourvues { get; set; } = [];

    /// <summary>Créneaux non générés parce qu'ils tombent un jour férié que le contrat n'inclut pas.</summary>
    public List<LigneRapportGenerationDto> Feries { get; set; } = [];

    /// <summary>
    /// Vacations planifiées de la période qui ne correspondent plus à leur prestation ou
    /// à leur contrat. Signalées, jamais modifiées ni supprimées : la génération n'ajoute
    /// que, et une suppression automatique est l'effet silencieux à éviter.
    /// </summary>
    public List<LigneRapportGenerationDto> Orphelines { get; set; } = [];
}

public class LigneRapportGenerationDto
{
    /// <summary>La vacation concernée. Nul pour un jour férié : rien n'a été créé.</summary>
    public Guid? InterventionId { get; set; }

    public DateTime Date { get; set; }
    public TimeSpan Heure { get; set; }
    public Guid PrestationId { get; set; }
    public string Prestation { get; set; } = string.Empty;
    public string Site { get; set; } = string.Empty;
    public string Cause { get; set; } = string.Empty;
}
