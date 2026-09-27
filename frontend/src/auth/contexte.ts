import { createContext, useContext } from 'react'

import type { Profil } from '../types/auth'

/**
 * Le contexte et son crochet vivent dans un `.ts` séparé du fournisseur pour
 * que `AuthContext.tsx` n'exporte que des composants — la règle
 * `react/only-export-components` d'oxlint, qui garde la compatibilité du
 * rafraîchissement à chaud.
 */
export interface ValeurAuth {
  /** `null` quand personne n'est connecté. Aucun jeton : il n'y en a pas à avoir. */
  profil: Profil | null

  /**
   * Vrai le temps de la restauration initiale. État distinct de « déconnecté » :
   * sans lui, chaque rechargement afficherait l'écran de connexion pendant un
   * aller-retour réseau, puis l'application par-dessus.
   */
  restauration: boolean

  connecter: (email: string, motDePasse: string) => Promise<void>
  deconnecter: () => Promise<void>
}

export const ContexteAuth = createContext<ValeurAuth | null>(null)

export function useAuth(): ValeurAuth {
  const valeur = useContext(ContexteAuth)

  if (valeur === null) {
    throw new Error('useAuth doit être appelé dans <FournisseurAuth>.')
  }

  return valeur
}
