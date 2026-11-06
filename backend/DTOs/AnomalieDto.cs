using System.ComponentModel.DataAnnotations;

namespace Klean.Api.DTOs;

/// <summary>
/// Une anomalie, telle que la lisent le chef et le responsable. Ni nom ni prénom : l'auteur
/// n'y est qu'un identifiant, comme sur un mouvement de stock, et c'est ce qui la laisse hors
/// du journal d'accès (SPEC.md, étape 25).
/// </summary>
public class AnomalieDto
{
    public Guid Id { get; set; }
    public Guid InterventionId { get; set; }
    public Guid SiteId { get; set; }

    /// <summary>Le nom du site, pour que la liste se lise sans aller chercher la vacation.</summary>
    public string Site { get; set; } = string.Empty;

    public DateTime DatePrevue { get; set; }
    public string Description { get; set; } = string.Empty;

    /// <summary>Valeurs : Ouverte, Traitee.</summary>
    public string Statut { get; set; } = string.Empty;

    public DateTime DateSignalement { get; set; }
    public Guid AuteurId { get; set; }
    public DateTime? DateTraitement { get; set; }
    public Guid? TraiteeParId { get; set; }

    /// <summary>Les identifiants des photos, sans leur contenu : chacune se lit par sa route.</summary>
    public List<Guid> Photos { get; set; } = [];
}

/// <summary>
/// Le signalement par l'agent. ⚠️ Aucun <c>[Required]</c> ni <c>[MaxLength]</c> : la
/// validation de modèle répondrait en anglais (CLAUDE.md §10). Le contrôleur refuse en
/// français.
/// </summary>
public class AnomalieCreateDto
{
    public Guid InterventionId { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class PhotoAnomalieDto
{
    public Guid Id { get; set; }
    public Guid AnomalieId { get; set; }
    public int Taille { get; set; }
    public DateTime DateAjout { get; set; }
}
