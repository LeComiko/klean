import { api } from './client'
import type { Page } from './ressources'
import { routeDuJournal } from '../journal/journal'
import type { FiltresJournal } from '../journal/journal'
import type { AccesJournalise } from '../types/journal'

/**
 * Le journal d'accès, administrateur seul (SPEC.md, étape 27).
 *
 * ⚠️ Chaque appel laisse lui-même une ligne `JournalAcces / Liste` : lire le journal est
 * un accès comme un autre (RGPD.md, T5).
 */
export function lireJournal(
  filtres: FiltresJournal,
  page: number,
  taille: number,
  signal?: AbortSignal,
): Promise<Page<AccesJournalise>> {
  return api.get<Page<AccesJournalise>>(routeDuJournal(filtres, page, taille), { signal })
}
