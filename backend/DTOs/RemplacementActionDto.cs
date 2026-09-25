using System.ComponentModel.DataAnnotations;

namespace Klean.Api.DTOs;

/// <summary>
/// Corps de <c>POST /api/remplacements/{id}/proposer</c>. Un seul champ : le
/// score n'est pas fourni par l'appelant, il est recalculé par le serveur au
/// moment de la proposition. Un score envoyé depuis le client serait un score
/// invérifiable.
/// </summary>
public class ProposerDto
{
    [Required]
    public Guid AgentId { get; set; }
}

/// <summary>
/// Réponse de <c>POST /api/absences/{id}/detecter-impacts</c>. Le compte est
/// la seule information utile : zéro au second appel prouve l'idempotence.
/// </summary>
public class DetectionDto
{
    public int RemplacementsCrees { get; set; }
}
