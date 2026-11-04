import { api } from './client'
import { TailleMaximale } from './ressources'
import type { Page } from './ressources'
import type { LigneStock, Mouvement, MouvementACreer, Prevision } from '../types/stock'

/**
 * Le lieu d'une liste de stock : un emplacement **ou** un site, jamais les deux. Les
 * deux paramètres existent côté serveur et filtrent avant le comptage.
 */
export interface FiltreLieu {
  emplacementId?: string
  siteId?: string
}

export function listerStocks(
  page: number,
  taille: number,
  filtre: FiltreLieu,
  signal?: AbortSignal,
): Promise<Page<LigneStock>> {
  const lieu = filtre.emplacementId !== undefined
    ? `&emplacementId=${encodeURIComponent(filtre.emplacementId)}`
    : filtre.siteId !== undefined
      ? `&siteId=${encodeURIComponent(filtre.siteId)}`
      : ''

  return api.get<Page<LigneStock>>(
    `/api/stocks?page=${String(page)}&taille=${String(Math.min(taille, TailleMaximale))}${lieu}`,
    { signal },
  )
}

/** Du plus récent au plus ancien. */
export function listerMouvements(page: number, taille: number, signal?: AbortSignal): Promise<Page<Mouvement>> {
  return api.get<Page<Mouvement>>(
    `/api/mouvements-stock?page=${String(page)}&taille=${String(Math.min(taille, TailleMaximale))}`,
    { signal },
  )
}

/**
 * Un refus — stock insuffisant, lieu désactivé — revient en 400 avec la phrase du
 * serveur, qui nomme la sortie : l'écran l'affiche telle quelle.
 */
export function enregistrerMouvement(corps: MouvementACreer): Promise<Mouvement> {
  return api.post<Mouvement>('/api/mouvements-stock', corps)
}

/** Le besoin des `semaines` à venir, de 1 à 12, et les deux alertes. */
export function lirePrevision(semaines: number, signal?: AbortSignal): Promise<Prevision> {
  return api.get<Prevision>(`/api/stocks/prevision?semaines=${String(semaines)}`, { signal })
}
