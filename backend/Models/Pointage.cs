using System.ComponentModel.DataAnnotations;

namespace Klean.Api.Models;

/// <summary>Le geste qui a produit le pointage.</summary>
public enum MomentPointage
{
    Debut,
    Fin
}

/// <summary>
/// Ce que le pointage prouve. Seuls deux <see cref="SurSite"/> certifient un passage ;
/// les trois autres le laissent terminé, mais <c>NonCertifiee</c> (SPEC.md, étape 25).
/// </summary>
public enum PreuvePointage
{
    /// <summary>La position, assez précise, tombe dans le rayon du site.</summary>
    SurSite,

    /// <summary>La position tombe hors du rayon, même en lui accordant sa marge d'erreur.</summary>
    HorsRayon,

    /// <summary>
    /// La précision annoncée dépasse le seuil : fréquent à l'intérieur d'un bâtiment. On ne
    /// sait pas trancher, et on ne tranche pas.
    /// </summary>
    Imprecise,

    /// <summary>Aucune position : refusée par l'agent, ou indisponible sur le téléphone.</summary>
    NonLocalisee
}

/// <summary>
/// Un pointage de l'agent sur sa vacation, au début ou à la fin (SPEC.md, étape 25).
/// <see cref="EntrepriseId"/> dénormalisé.
///
/// ⚠️ <b>Aucune coordonnée n'est conservée, et aucune colonne ne peut en recevoir.</b> La
/// position reçue sert à calculer la distance au site, puis elle est jetée : ce qui prouve
/// le passage est la distance à un lieu déjà connu, pas l'endroit où se tenait le salarié.
/// <c>PassagesTests</c> fige la liste des colonnes, pour qu'une latitude ajoutée demain ne
/// passe pas inaperçue.
///
/// ⚠️ <b>Une table à part, et non des colonnes de l'intervention</b> : une vacation se garde
/// cinq ans, une donnée tirée d'une localisation un an au plus (RGPD.md §5). Le pointage se
/// purge sans toucher la vacation, dont le statut reste.
/// </summary>
public class Pointage
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid EntrepriseId { get; set; }

    [Required]
    public Guid InterventionId { get; set; }
    public Intervention? Intervention { get; set; }

    [Required]
    public MomentPointage Moment { get; set; }

    /// <summary>L'heure du serveur, jamais celle du téléphone, qu'on peut régler.</summary>
    public DateTime Horodatage { get; set; }

    [Required]
    public PreuvePointage Preuve { get; set; }

    /// <summary>Distance au site, au mètre. Nulle sans position.</summary>
    public int? DistanceMetres { get; set; }

    /// <summary>La marge d'erreur que le téléphone annonce, au mètre. Nulle sans position.</summary>
    public int? PrecisionMetres { get; set; }

    /// <summary>
    /// Le compte qui a pointé. Sans clé étrangère, comme <c>MouvementStock.AuteurId</c> : un
    /// compte n'est jamais supprimé, il est pseudonymisé, et l'identifiant survit.
    /// </summary>
    public Guid AuteurId { get; set; }
}
