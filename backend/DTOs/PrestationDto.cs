using System.ComponentModel.DataAnnotations;

namespace Klean.Api.DTOs;

public class PrestationDto
{
    public Guid Id { get; set; }
    public Guid EntrepriseId { get; set; }
    public Guid ContratId { get; set; }
    public string Libelle { get; set; } = string.Empty;
    public int DureeEstimeeMinutes { get; set; }

    /// <summary>Du lundi au dimanche, par exemple <c>["Lundi", "Jeudi"]</c>. Vide : jamais générée.</summary>
    public string[] Jours { get; set; } = [];

    public TimeSpan? HeureDebut { get; set; }
    public Guid? AgentTitulaireId { get; set; }
}

public class PrestationCreateDto
{
    [Required]
    public Guid ContratId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Libelle { get; set; } = string.Empty;

    /// <summary>Alimente le calcul de charge hebdomadaire du moteur d'affectation.</summary>
    [Range(1, 1440)]
    public int DureeEstimeeMinutes { get; set; }

    /// <summary>
    /// Valeurs : Lundi, Mardi, Mercredi, Jeudi, Vendredi, Samedi, Dimanche. Absent ou
    /// vide : la prestation n'est pas générée.
    ///
    /// ⚠️ Un <c>PUT</c> qui omet ce champ le remet à vide, comme tout champ de ce DTO :
    /// c'est le contrat de remplacement complet des cinq endpoints CRUD.
    /// </summary>
    public string[] Jours { get; set; } = [];

    /// <summary>Requise dès qu'un jour est coché.</summary>
    public TimeSpan? HeureDebut { get; set; }

    public Guid? AgentTitulaireId { get; set; }
}
