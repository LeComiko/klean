namespace Klean.Api.DTOs;

/// <summary>
/// Planning d'une semaine : les agents en lignes, leurs vacations par jour, et
/// la liste séparée de ce qui n'est pas pourvu.
///
/// Les agrégats sont calculés par le serveur et non par l'écran : deux clients
/// qui additionnent eux-mêmes finissent par ne pas afficher le même total.
/// </summary>
public class PlanningSemaineDto
{
    public DateTime Lundi { get; set; }

    /// <summary>Dernier jour inclus dans la semaine, pour lever toute ambiguïté.</summary>
    public DateTime Dimanche { get; set; }

    public List<PlanningAgentDto> Agents { get; set; } = [];

    /// <summary>
    /// Vacations à pourvoir. Celles qui sont affectées à un agent absent y
    /// figurent **en plus** de la ligne de cet agent, où elles portent
    /// <see cref="PlanningVacationDto.NonPourvue"/>. La duplication est voulue :
    /// le planning doit montrer le trou à sa place dans la journée de l'agent, et
    /// le panneau de remplacement doit pouvoir lister les trous sans parcourir
    /// toutes les lignes.
    /// </summary>
    public List<PlanningNonPourvueDto> NonPourvues { get; set; } = [];

    /// <summary>Toutes les vacations de la semaine, pourvues ou non. Les annulées sont exclues.</summary>
    public int TotalVacations { get; set; }

    public double TotalHeures { get; set; }

    public int TotalNonPourvues { get; set; }
}

public class PlanningAgentDto
{
    public Guid AgentId { get; set; }
    public string Nom { get; set; } = string.Empty;
    public string Prenom { get; set; } = string.Empty;

    /// <summary>Triées par date puis heure.</summary>
    public List<PlanningVacationDto> Vacations { get; set; } = [];

    public int NombreVacations { get; set; }
    public double Heures { get; set; }
}

public class PlanningVacationDto
{
    public Guid InterventionId { get; set; }
    public DateTime Date { get; set; }
    public TimeSpan Heure { get; set; }
    public int DureeMinutes { get; set; }

    public Guid SiteId { get; set; }
    public string SiteNom { get; set; } = string.Empty;
    public string PrestationLibelle { get; set; } = string.Empty;

    /// <summary>Valeurs : Planifiee, EnCours, Terminee, NonCertifiee.</summary>
    public string Statut { get; set; } = string.Empty;

    /// <summary>
    /// Vrai quand l'agent de cette ligne est absent ce jour-là. La vacation reste
    /// affichée à sa place — c'est le trou qu'il faut voir, pas son absence de la
    /// grille.
    /// </summary>
    public bool NonPourvue { get; set; }
}

public class PlanningNonPourvueDto
{
    public Guid InterventionId { get; set; }
    public DateTime Date { get; set; }
    public TimeSpan Heure { get; set; }
    public int DureeMinutes { get; set; }

    public Guid SiteId { get; set; }
    public string SiteNom { get; set; } = string.Empty;
    public string PrestationLibelle { get; set; } = string.Empty;

    /// <summary>Null quand la vacation n'a jamais été affectée.</summary>
    public Guid? AgentId { get; set; }

    /// <summary>« Aucun agent affecté » ou « Agent absent ».</summary>
    public string Cause { get; set; } = string.Empty;
}
