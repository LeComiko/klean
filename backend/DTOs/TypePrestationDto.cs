using System.ComponentModel.DataAnnotations;

namespace Klean.Api.DTOs;

public class TypePrestationDto
{
    public Guid Id { get; set; }
    public Guid EntrepriseId { get; set; }
    public string Libelle { get; set; } = string.Empty;
    public int DureeParDefautMinutes { get; set; }
    public bool Actif { get; set; }
}

/// <summary>
/// Compte rendu de <c>POST /api/types-prestation/importer-liste-type</c>. Les deux
/// listes, et pas seulement leur nombre : qui rejoue l'import doit voir que rien n'a été
/// recréé, et lesquels étaient déjà là.
/// </summary>
public class ImportListeTypeDto
{
    public List<string> Crees { get; set; } = [];

    /// <summary>
    /// Laissés tels quels, durée et état compris, parce qu'un type de même libellé existait
    /// déjà, actif ou non. C'est ce qui rend l'import rejouable sans écraser une correction.
    /// </summary>
    public List<string> DejaPresents { get; set; } = [];
}

public class TypePrestationCreateDto
{
    [Required]
    [MaxLength(200)]
    public string Libelle { get; set; } = string.Empty;

    /// <summary>Mêmes bornes que <c>PrestationCreateDto.DureeEstimeeMinutes</c>, qu'elle préremplit.</summary>
    [Range(1, 1440)]
    public int DureeParDefautMinutes { get; set; }

    public bool Actif { get; set; } = true;
}
