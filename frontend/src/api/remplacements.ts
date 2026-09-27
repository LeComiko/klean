import { api } from './client'
import type { Candidat, Remplacement } from '../types/remplacement'

/**
 * Ouvre — ou retrouve — le remplacement d'une vacation.
 *
 * **Un seul appel, et c'est délibéré.** L'écran part d'une vacation non pourvue et
 * ne sait pas si un remplacement existe : celui du mercredi d'un agent absent a
 * été posé automatiquement à la saisie de l'absence, celui d'une vacation jamais
 * affectée n'existe pas. L'endpoint est idempotent, il rend 200 dans le premier
 * cas et 201 dans le second, avec le même corps — l'appelant n'a pas à distinguer.
 *
 * L'alternative aurait été un `GET` puis un `POST`. Elle ouvre une fenêtre pendant
 * laquelle un second poste crée le doublon, et `GET /api/remplacements` n'a aucun
 * filtre : il faudrait balayer les pages.
 */
export function ouvrirRemplacement(
  interventionId: string,
  signal?: AbortSignal,
): Promise<Remplacement> {
  return api.post<Remplacement>(`/api/interventions/${interventionId}/remplacement`, undefined, signal)
}

/**
 * Candidats classés par score décroissant.
 *
 * Non paginé côté serveur, contrairement aux onze listes : la taille est bornée
 * par le nombre d'agents actifs de l'entreprise, et tronquer un classement le rend
 * faux.
 */
export function getCandidats(remplacementId: string, signal?: AbortSignal): Promise<Candidat[]> {
  return api.get<Candidat[]>(`/api/remplacements/${remplacementId}/candidats`, { signal })
}

/**
 * Propose un agent. Le serveur **refuse** un agent écarté par un filtre dur, au
 * lieu de le proposer avec un score nul, et calcule le score lui-même — celui que
 * le client enverrait serait ignoré.
 */
export function proposer(remplacementId: string, agentId: string): Promise<Remplacement> {
  return api.post<Remplacement>(`/api/remplacements/${remplacementId}/proposer`, { agentId })
}

/**
 * Accepte la proposition : c'est cet appel qui écrit `AgentId` sur l'intervention.
 * Le serveur **revérifie** l'éligibilité avant d'écrire — entre la proposition et
 * l'acceptation, une absence a pu être saisie.
 */
export function accepter(remplacementId: string): Promise<Remplacement> {
  return api.post<Remplacement>(`/api/remplacements/${remplacementId}/accepter`)
}

/** Ramène le remplacement en `Recherche` et efface l'agent proposé. */
export function refuser(remplacementId: string): Promise<Remplacement> {
  return api.post<Remplacement>(`/api/remplacements/${remplacementId}/refuser`)
}
