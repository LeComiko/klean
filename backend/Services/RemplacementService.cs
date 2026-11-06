using Klean.Api.Data;
using Klean.Api.DTOs;
using Klean.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Klean.Api.Services;

/// <summary>Issue d'une transition, traduite en code HTTP par le contrôleur.</summary>
public enum IssueRemplacement
{
    Ok,
    Introuvable,
    Refusee
}

/// <summary>
/// Résultat d'une transition. <c>Message</c> n'est renseigné que sur
/// <see cref="IssueRemplacement.Refusee"/>, et il est destiné à être lu par un
/// humain : une transition refusée sans explication renvoie l'utilisateur à
/// deviner l'état de la machine.
/// </summary>
public readonly record struct ResultatRemplacement(
    IssueRemplacement Issue, RemplacementDto? Valeur, string? Message, bool Cree = false)
{
    public static ResultatRemplacement Ok(RemplacementDto valeur, bool cree = false) =>
        new(IssueRemplacement.Ok, valeur, null, cree);

    public static ResultatRemplacement Introuvable() => new(IssueRemplacement.Introuvable, null, null);
    public static ResultatRemplacement Refusee(string message) => new(IssueRemplacement.Refusee, null, message);
}

/// <summary>
/// Cycle de remplacement (CLAUDE.md §9). Machine à états stricte :
///
/// <code>
/// Recherche ──proposer──► Propose ──accepter──► Accepte
///     ▲                      │
///     └──────refuser─────────┘
/// Refuse ──proposer──► Propose
/// </code>
///
/// Toute transition impossible est refusée avec un message, jamais silencieuse.
/// Le service ne produit lui-même jamais le statut <c>Refuse</c> — un refus
/// ramène à <c>Recherche</c> pour qu'un autre candidat puisse être proposé — mais
/// il l'accepte en entrée, parce que le CRUD brut peut l'avoir posé.
///
/// Pas de mode automatique : sans notifications ni application agent, il n'y a
/// personne à contacter. Chaque proposition est une action humaine explicite.
/// </summary>
public class RemplacementService
{
    private readonly KleanDbContext _db;
    private readonly AffectationService _affectation;

    public RemplacementService(KleanDbContext db, AffectationService affectation)
    {
        _db = db;
        _affectation = affectation;
    }

    /// <summary>Statuts qui occupent déjà le terrain : la détection n'en recrée pas.</summary>
    private static readonly StatutRemplacement[] StatutsActifs =
    [
        StatutRemplacement.Recherche,
        StatutRemplacement.Propose,
        StatutRemplacement.Accepte
    ];

    /// <summary>
    /// Crée un remplacement en statut <c>Recherche</c> pour chaque intervention
    /// <c>Planifiee</c> de l'agent absent dont le jour tombe dans l'absence.
    /// Retourne le nombre créé, ou <c>null</c> si l'absence n'existe pas.
    ///
    /// Idempotent : une intervention qui porte déjà un remplacement actif est
    /// ignorée. Sans cette garde, un second appel — ou une modification de
    /// l'absence — doublerait les lignes et le panneau de remplacement
    /// afficherait deux fois la même vacation à pourvoir.
    /// </summary>
    public async Task<int?> DetecterImpactsAsync(Guid absenceId)
    {
        var absence = await _db.Absences
            .AsNoTracking()
            .Where(a => a.Id == absenceId)
            .Select(a => new { a.Id, a.AgentId, a.DateDebut, a.DateFin })
            .FirstOrDefaultAsync();

        if (absence is null) return null;

        // Comparaison au jour, comme le filtre dur du moteur d'affectation : une
        // absence saisie « du 7 au 9 » arrive avec les heures à zéro, et un
        // chevauchement strict raterait la vacation du 9 à 8 h 30.
        var premierJour = DateTime.SpecifyKind(absence.DateDebut.Date, DateTimeKind.Utc);
        var jourApres = DateTime.SpecifyKind(absence.DateFin.Date, DateTimeKind.Utc).AddDays(1);

        var interventions = await _db.Interventions
            .AsNoTracking()
            .Where(i => i.AgentId == absence.AgentId
                        && i.Statut == StatutIntervention.Planifiee
                        && i.DatePrevue >= premierJour
                        && i.DatePrevue < jourApres)
            .Select(i => i.Id)
            .ToListAsync();

        if (interventions.Count == 0) return 0;

        var dejaCouvertes = await _db.Remplacements
            .AsNoTracking()
            .Where(r => interventions.Contains(r.InterventionId)
                        && StatutsActifs.Contains(r.Statut))
            .Select(r => r.InterventionId)
            .ToListAsync();

        var aCreer = interventions.Except(dejaCouvertes).ToList();
        if (aCreer.Count == 0) return 0;

        foreach (var interventionId in aCreer)
        {
            _db.Remplacements.Add(new Remplacement
            {
                InterventionId = interventionId,
                AbsenceId = absence.Id,
                Statut = StatutRemplacement.Recherche,
                AgentRemplacantId = null,
                Score = null,
                DateCreation = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        return aCreer.Count;
    }

    /// <summary>Candidats pour l'intervention d'un remplacement, classés par score.</summary>
    /// <summary>
    /// Rend le remplacement actif d'une vacation, ou l'ouvre en <c>Recherche</c>
    /// s'il n'y en a pas. <c>Cree</c> dit lequel des deux, pour que le contrôleur
    /// réponde 201 ou 200.
    ///
    /// **Idempotent, et c'est tout l'objet de cette méthode.** L'écran de planning
    /// arrive avec un identifiant d'intervention et ne sait pas si un remplacement
    /// existe : celui du mercredi d'un agent absent a été posé par
    /// <see cref="DetecterImpactsAsync"/>, celui d'une vacation jamais affectée
    /// n'existe pas. Sans cette opération, l'écran devrait lire puis écrire en deux
    /// appels — donc balayer une liste sans filtre, et laisser une fenêtre pendant
    /// laquelle un second poste crée le doublon. Un doublon en <c>Recherche</c> est
    /// invisible à l'écran, puisque les vacations non pourvues viennent du planning
    /// et non de cette table : personne ne le verrait.
    ///
    /// La règle d'unicité est <see cref="StatutsActifs"/>, la même que la détection
    /// d'impacts. Un remplacement <c>Refuse</c> ou <c>Annule</c> est de l'historique
    /// et n'empêche pas d'en ouvrir un nouveau.
    /// </summary>
    public async Task<ResultatRemplacement> OuvrirAsync(Guid interventionId)
    {
        var intervention = await _db.Interventions
            .AsNoTracking()
            .Where(i => i.Id == interventionId)
            .Select(i => new { i.Id, i.Statut })
            .FirstOrDefaultAsync();

        if (intervention is null) return ResultatRemplacement.Introuvable();

        // L'existant est cherché **avant** le contrôle de statut : un remplacement
        // déjà ouvert doit rester consultable même si la vacation a changé d'état
        // entre-temps. L'inverse cacherait des données au lieu de refuser une
        // création.
        var actif = await _db.Remplacements
            .AsNoTracking()
            .Where(r => r.InterventionId == interventionId && StatutsActifs.Contains(r.Statut))
            .OrderBy(r => r.DateCreation)
            .FirstOrDefaultAsync();

        if (actif is not null) return ResultatRemplacement.Ok(Vers(actif));

        // Même restriction que la détection d'impacts : seule une vacation
        // Planifiee se remplace. Une vacation terminée, non certifiée ou annulée
        // appartient au passé, et lui ouvrir un remplacement ne veut rien dire.
        if (intervention.Statut != StatutIntervention.Planifiee)
        {
            return ResultatRemplacement.Refusee(
                $"Vacation au statut {intervention.Statut} : seule une vacation "
                + $"{StatutIntervention.Planifiee} peut être remplacée.");
        }

        var remplacement = new Remplacement
        {
            InterventionId = interventionId,
            // Null volontairement : une ouverture manuelle ne sait pas de quelle
            // absence elle découle, et la deviner serait une inférence. Seule
            // DetecterImpactsAsync, qui part de l'absence, peut renseigner ce lien.
            AbsenceId = null,
            Statut = StatutRemplacement.Recherche,
            AgentRemplacantId = null,
            Score = null,
            DateCreation = DateTime.UtcNow
        };

        _db.Remplacements.Add(remplacement);
        await _db.SaveChangesAsync();

        return ResultatRemplacement.Ok(Vers(remplacement), cree: true);
    }

    public async Task<List<CandidatDto>?> GetCandidatsAsync(Guid remplacementId)
    {
        var interventionId = await _db.Remplacements
            .AsNoTracking()
            .Where(r => r.Id == remplacementId)
            .Select(r => (Guid?)r.InterventionId)
            .FirstOrDefaultAsync();

        return interventionId is null ? null : await _affectation.GetCandidatsAsync(interventionId.Value);
    }

    /// <summary>
    /// Propose un agent. Le score est celui que le moteur d'affectation calcule
    /// à cet instant.
    ///
    /// Un agent écarté par un filtre dur est <b>refusé</b>, pas proposé avec un
    /// score nul : sans cette garde les filtres durs seraient décoratifs, et un
    /// agent en arrêt maladie pourrait être proposé à la main un jour de tension.
    /// </summary>
    public async Task<ResultatRemplacement> ProposerAsync(Guid remplacementId, Guid agentId)
    {
        var remplacement = await _db.Remplacements.FindAsync(remplacementId);
        if (remplacement is null) return ResultatRemplacement.Introuvable();

        if (remplacement.Statut is not (StatutRemplacement.Recherche or StatutRemplacement.Refuse))
        {
            return ResultatRemplacement.Refusee(Transition("proposer", remplacement.Statut,
                $"{StatutRemplacement.Recherche} ou {StatutRemplacement.Refuse}"));
        }

        if (!await _db.Agents.AnyAsync(a => a.Id == agentId))
        {
            return ResultatRemplacement.Refusee($"Agent {agentId} inconnu.");
        }

        var candidat = await CandidatAsync(remplacement.InterventionId, agentId);
        if (candidat is null) return ResultatRemplacement.Refusee(PasCandidat(agentId, remplacementId));

        remplacement.AgentRemplacantId = agentId;
        remplacement.Score = candidat.Score;
        remplacement.Statut = StatutRemplacement.Propose;

        await _db.SaveChangesAsync();
        return ResultatRemplacement.Ok(Vers(remplacement));
    }

    /// <summary>
    /// Accepte la proposition. <b>Seul endroit du service</b> où une intervention
    /// change d'agent (CLAUDE.md §9).
    ///
    /// L'éligibilité est revérifiée ici, et pas seulement à la proposition : entre
    /// les deux, une absence a pu être saisie ou une autre vacation affectée. Sans
    /// cette seconde vérification, accepter une proposition de la veille peut
    /// poser un agent sur un créneau qu'il ne peut plus tenir.
    /// </summary>
    public async Task<ResultatRemplacement> AccepterAsync(Guid remplacementId)
    {
        var remplacement = await _db.Remplacements.FindAsync(remplacementId);
        if (remplacement is null) return ResultatRemplacement.Introuvable();

        if (remplacement.Statut != StatutRemplacement.Propose)
        {
            return ResultatRemplacement.Refusee(Transition("accepter", remplacement.Statut,
                StatutRemplacement.Propose.ToString()));
        }

        if (remplacement.AgentRemplacantId is null)
        {
            return ResultatRemplacement.Refusee(
                "Remplacement en statut Propose sans agent remplaçant : incohérent, "
                + "proposer un agent avant d'accepter.");
        }

        var agentId = remplacement.AgentRemplacantId.Value;

        var candidat = await CandidatAsync(remplacement.InterventionId, agentId);
        if (candidat is null)
        {
            return ResultatRemplacement.Refusee(
                $"L'agent {agentId} n'est plus candidat depuis la proposition. "
                + "Refuser ce remplacement et en proposer un autre.");
        }

        var intervention = await _db.Interventions.FindAsync(remplacement.InterventionId);
        if (intervention is null)
        {
            return ResultatRemplacement.Refusee(
                $"Intervention {remplacement.InterventionId} introuvable : remplacement orphelin.");
        }

        // ⚠️ Entre la proposition et l'acceptation, l'agent en place a pu démarrer la
        // vacation (SPEC.md, étape 25). Changer d'agent détacherait son pointage de la ligne
        // qu'il certifie.
        if (intervention.Statut != StatutIntervention.Planifiee)
        {
            return ResultatRemplacement.Refusee(
                $"Vacation passée au statut {intervention.Statut} depuis la proposition : "
                + "elle ne change plus d'agent. Refuser ce remplacement.");
        }

        intervention.AgentId = agentId;

        remplacement.Score = candidat.Score;
        remplacement.Statut = StatutRemplacement.Accepte;

        await _db.SaveChangesAsync();
        return ResultatRemplacement.Ok(Vers(remplacement));
    }

    /// <summary>
    /// Refuse la proposition et revient en <c>Recherche</c>. Ne réaffecte rien et
    /// ne déclenche aucune cascade : la vacation reste à pourvoir, un autre
    /// candidat peut être proposé.
    ///
    /// L'agent refusé est effacé de la ligne. Limite assumée de la V1 : sans table
    /// d'historique, le refus ne laisse pas de trace, et rien n'empêche de
    /// reproposer le même agent aussitôt.
    /// </summary>
    public async Task<ResultatRemplacement> RefuserAsync(Guid remplacementId)
    {
        var remplacement = await _db.Remplacements.FindAsync(remplacementId);
        if (remplacement is null) return ResultatRemplacement.Introuvable();

        if (remplacement.Statut != StatutRemplacement.Propose)
        {
            return ResultatRemplacement.Refusee(Transition("refuser", remplacement.Statut,
                StatutRemplacement.Propose.ToString()));
        }

        remplacement.AgentRemplacantId = null;
        remplacement.Score = null;
        remplacement.Statut = StatutRemplacement.Recherche;

        await _db.SaveChangesAsync();
        return ResultatRemplacement.Ok(Vers(remplacement));
    }

    private async Task<CandidatDto?> CandidatAsync(Guid interventionId, Guid agentId)
    {
        var candidats = await _affectation.GetCandidatsAsync(interventionId);

        return candidats?.FirstOrDefault(c => c.AgentId == agentId);
    }

    private static string Transition(string action, StatutRemplacement actuel, string attendus) =>
        $"Transition « {action} » impossible depuis le statut « {actuel} ». "
        + $"Statut attendu : {attendus}.";

    /// <summary>
    /// Même message que celui des écritures directes sur une intervention : une
    /// seule formulation pour une seule règle, quel que soit le chemin.
    /// </summary>
    private static string PasCandidat(Guid agentId, Guid remplacementId) =>
        AffectationService.MessageIneligibilite(agentId,
            $"Voir GET /api/remplacements/{remplacementId}/candidats.");

    private static RemplacementDto Vers(Remplacement r) => new()
    {
        Id = r.Id,
        EntrepriseId = r.EntrepriseId,
        InterventionId = r.InterventionId,
        AbsenceId = r.AbsenceId,
        AgentRemplacantId = r.AgentRemplacantId,
        Statut = r.Statut.ToString(),
        Score = r.Score,
        DateCreation = r.DateCreation
    };
}
