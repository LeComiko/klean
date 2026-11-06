namespace Klean.Api.DTOs;

/// <summary>
/// Une vacation telle que la lit son agent (SPEC.md, étape 25) : ce qu'il lui faut sur
/// place, et rien de ce que ses collègues font. Elle ne nomme personne d'autre que lui.
/// </summary>
public class VacationAgentDto
{
    public Guid Id { get; set; }
    public DateTime DatePrevue { get; set; }

    /// <summary>Sérialisé en « HH:MM:SS ».</summary>
    public TimeSpan HeurePrevue { get; set; }

    public int DureeEstimeeMinutes { get; set; }

    /// <summary>Valeurs : Planifiee, EnCours, Terminee, NonCertifiee, Annulee.</summary>
    public string Statut { get; set; } = string.Empty;

    public string Prestation { get; set; } = string.Empty;
    public string Site { get; set; } = string.Empty;
    public string Adresse { get; set; } = string.Empty;
    public string? CodePostal { get; set; }
    public string? Ville { get; set; }
    public string? Consignes { get; set; }

    public PointageDto? Debut { get; set; }
    public PointageDto? Fin { get; set; }
}

/// <summary>Un pointage, sans coordonnées : il n'en existe pas à rendre.</summary>
public class PointageDto
{
    public DateTime Horodatage { get; set; }

    /// <summary>Valeurs : SurSite, HorsRayon, Imprecise, NonLocalisee.</summary>
    public string Preuve { get; set; } = string.Empty;

    public int? DistanceMetres { get; set; }
    public int? PrecisionMetres { get; set; }
}

/// <summary>
/// Le corps de <c>demarrer</c> et de <c>terminer</c> : la position du geste, ou rien.
///
/// ⚠️ Aucun <c>[Range]</c> : la validation de modèle répondrait en anglais (CLAUDE.md §10).
/// Le service vérifie les bornes, et refuse en français.
/// </summary>
public class PointageCreateDto
{
    /// <summary>Nulle quand l'agent a refusé la localisation, ou que le téléphone n'en a pas.</summary>
    public PositionDto? Position { get; set; }
}

/// <summary>
/// Ce que rend l'API de géolocalisation du navigateur. Ces trois valeurs ne servent qu'au
/// calcul de la distance ; aucune n'est enregistrée.
/// </summary>
public class PositionDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    /// <summary>La marge d'erreur annoncée par le téléphone, en mètres.</summary>
    public double PrecisionMetres { get; set; }
}
