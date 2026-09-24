using System.ComponentModel.DataAnnotations;

namespace Klean.Api.Models;

public enum TypeClient
{
    EntrepriseCliente,
    Syndic,
    GestionnaireImmobilier,
    Copropriete,
    Autre
}

/// <summary>
/// Donneur d'ordre. Porte naturellement son <see cref="EntrepriseId"/>,
/// avec clé étrangère.
/// </summary>
public class Client
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid EntrepriseId { get; set; }
    public Entreprise? Entreprise { get; set; }

    [Required]
    [MaxLength(200)]
    public string Nom { get; set; } = string.Empty;

    [Required]
    public TypeClient Type { get; set; }

    [MaxLength(200)]
    public string? Adresse { get; set; }

    [MaxLength(10)]
    public string? CodePostal { get; set; }

    [MaxLength(100)]
    public string? Ville { get; set; }

    [MaxLength(200)]
    public string? Email { get; set; }

    [MaxLength(30)]
    public string? Telephone { get; set; }

    public ICollection<Site> Sites { get; set; } = new List<Site>();
}
