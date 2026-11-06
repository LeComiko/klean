using System.ComponentModel.DataAnnotations;

namespace Klean.Api.Models;

public enum StatutAnomalie
{
    Ouverte,
    Traitee
}

/// <summary>
/// Ce que l'agent a constaté sur place pendant un passage — une fuite, une porte cassée, un
/// produit manquant (SPEC.md, étape 25, lot B). <see cref="EntrepriseId"/> dénormalisé.
///
/// Traitée par un chef d'équipe ou un responsable, chacun dans son périmètre. Le syndic n'y
/// a pas accès avant le portail.
/// </summary>
public class Anomalie
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid EntrepriseId { get; set; }

    [Required]
    public Guid InterventionId { get; set; }
    public Intervention? Intervention { get; set; }

    /// <summary>
    /// ⚠️ Un texte libre, comme les consignes d'un site (RGPD.md §3) : rien n'empêche d'y
    /// nommer un résident. L'écran dissuade de le faire ; le modèle ne peut pas l'interdire.
    /// </summary>
    [Required]
    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    [Required]
    public StatutAnomalie Statut { get; set; } = StatutAnomalie.Ouverte;

    /// <summary>Posée par le serveur.</summary>
    public DateTime DateSignalement { get; set; }

    /// <summary>
    /// Le compte de l'agent qui l'a signalée. Sans clé étrangère, comme les autres auteurs :
    /// un compte n'est jamais supprimé, il est pseudonymisé.
    /// </summary>
    public Guid AuteurId { get; set; }

    public DateTime? DateTraitement { get; set; }

    /// <summary>Le compte qui l'a marquée traitée, sans clé étrangère.</summary>
    public Guid? TraiteeParId { get; set; }

    public ICollection<PhotoAnomalie> Photos { get; set; } = new List<PhotoAnomalie>();
}

/// <summary>
/// Une photo d'anomalie, rangée en base (SPEC.md, étape 25) : sous le filtre multi-tenant
/// comme toute entité, une photo d'une autre entreprise est inatteignable par construction.
/// <see cref="EntrepriseId"/> dénormalisé.
///
/// ⚠️ <b>Le contenu est un JPEG débarrassé de ses métadonnées</b> par
/// <c>MetadonneesJpeg</c>, à l'arrivée : une photo de téléphone porte souvent la position
/// GPS de sa prise, et la garder ferait entrer par la photo la localisation brute que le
/// pointage refuse d'enregistrer.
///
/// ⚠️ Toute lecture de liste <b>projette</b> sans <see cref="Contenu"/> : la colonne pèse
/// jusqu'à un mégaoctet par ligne.
/// </summary>
public class PhotoAnomalie
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid EntrepriseId { get; set; }

    [Required]
    public Guid AnomalieId { get; set; }
    public Anomalie? Anomalie { get; set; }

    [Required]
    public byte[] Contenu { get; set; } = [];

    /// <summary>En octets, après le retrait des métadonnées.</summary>
    public int Taille { get; set; }

    public DateTime DateAjout { get; set; }
}
