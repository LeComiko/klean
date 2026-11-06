using System.ComponentModel.DataAnnotations;

namespace Klean.Api.Models;

/// <summary>
/// Un point de la checklist d'un type de prestation — « Vider les corbeilles », « Laver le
/// sol du hall » (SPEC.md, étape 25, lot B). <see cref="EntrepriseId"/> dénormalisé.
///
/// Par type, comme la consommation : ce qu'on fait dans un hall ne dépend pas du site. Une
/// vacation sans type n'a donc pas de checklist.
/// </summary>
public class PointControle
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid EntrepriseId { get; set; }

    [Required]
    public Guid TypePrestationId { get; set; }
    public TypePrestation? TypePrestation { get; set; }

    [Required]
    [MaxLength(200)]
    public string Libelle { get; set; } = string.Empty;

    /// <summary>L'ordre d'affichage dans la checklist : celui du parcours sur place.</summary>
    public int Ordre { get; set; }

    /// <summary>
    /// Un point désactivé ne se propose plus. Ce qui a déjà été coché garde son libellé, copié
    /// dans <see cref="ControleEffectue"/>.
    /// </summary>
    public bool Actif { get; set; } = true;
}

/// <summary>
/// Ce que l'agent a coché, ou non, en terminant une vacation. <see cref="EntrepriseId"/>
/// dénormalisé.
///
/// ⚠️ <b>Le libellé est copié</b>, pas seulement référencé : un point renommé plus tard ne
/// doit pas réécrire ce qui a été déclaré fait. Le lien vers le point survit à sa
/// suppression en passant à nul, comme le titulaire d'une prestation : la ligne, elle, ne
/// disparaît pas.
///
/// Un compte rendu, jamais une condition : un point non coché ne retient pas l'agent, et ne
/// touche pas la certification (SPEC.md, étape 25). La certification dit la présence, la
/// checklist dit ce qui a été fait.
/// </summary>
public class ControleEffectue
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid EntrepriseId { get; set; }

    [Required]
    public Guid InterventionId { get; set; }
    public Intervention? Intervention { get; set; }

    public Guid? PointControleId { get; set; }
    public PointControle? PointControle { get; set; }

    [Required]
    [MaxLength(200)]
    public string Libelle { get; set; } = string.Empty;

    public int Ordre { get; set; }

    public bool Fait { get; set; }
}
