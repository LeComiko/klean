import { api } from './client'
import { TailleMaximale } from './ressources'
import type { Page } from './ressources'
import type { Anomalie, StatutAnomalie } from '../types/passage'

/**
 * Les anomalies signalées par les agents, lues et traitées par les trois rôles internes
 * (SPEC.md, étape 25). Le chef d'équipe n'y voit que celles de son équipe : le filtre
 * global suit celui des vacations.
 */

/** Les plus récentes d'abord. Sans `statut`, toutes. */
export function listerAnomalies(
  page: number,
  taille: number,
  statut: StatutAnomalie | null,
  signal?: AbortSignal,
): Promise<Page<Anomalie>> {
  const filtre = statut === null ? '' : `&statut=${statut}`

  return api.get<Page<Anomalie>>(
    `/api/anomalies?page=${String(page)}&taille=${String(Math.min(taille, TailleMaximale))}${filtre}`,
    { signal },
  )
}

export function traiterAnomalie(id: string): Promise<Anomalie> {
  return api.post<Anomalie>(`/api/anomalies/${id}/traiter`)
}

/** Les octets d'une photo, pour une URL `blob:` — jamais l'URL de l'API dans un `<img>`. */
export function lirePhoto(anomalieId: string, photoId: string, signal?: AbortSignal): Promise<Blob> {
  return api.getBlob(`/api/anomalies/${anomalieId}/photos/${photoId}`, signal)
}
