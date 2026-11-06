import { api } from './client'
import type { AnomalieAgent, Position, VacationAgent } from '../types/passage'

/**
 * Le passage de l'agent (SPEC.md, étape 25) : sa journée, les deux pointages, et ce
 * qu'il signale sur place. Tout est cadré côté serveur sur l'agent du compte — une
 * vacation qui n'est pas la sienne y est inconnue.
 */

export function lireMaJournee(signal?: AbortSignal): Promise<VacationAgent[]> {
  return api.get<VacationAgent[]>('/api/interventions/ma-journee', { signal })
}

/** `position` à `null` quand l'agent refuse la localisation : le passage démarre quand même. */
export function demarrer(id: string, position: Position | null): Promise<VacationAgent> {
  return api.post<VacationAgent>(`/api/interventions/${id}/demarrer`, { position })
}

/** Les points cochés ; un point actif absent de la liste est enregistré non fait. */
export function terminer(id: string, position: Position | null, pointsFaits: string[]): Promise<VacationAgent> {
  return api.post<VacationAgent>(`/api/interventions/${id}/terminer`, { position, pointsFaits })
}

export function signalerAnomalie(interventionId: string, description: string): Promise<AnomalieAgent> {
  return api.post<AnomalieAgent>('/api/anomalies', { interventionId, description })
}

/**
 * La photo, déjà réduite en JPEG par `passage/photo.ts`. Le serveur en retire les
 * métadonnées quoi qu'il en soit : c'est sa garde, pas celle de l'écran.
 */
export function envoyerPhoto(anomalieId: string, photo: Blob): Promise<unknown> {
  return api.postBrut<unknown>(`/api/anomalies/${anomalieId}/photos`, photo)
}
