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

  /**
   * Changement de mot de passe par son propriétaire.
   *
   * ⚠️ Ne déconnecte **pas** cet appareil, et déconnecte tous les autres. Le
   * serveur rouvre une session dans la même réponse ; cette fonction en recueille
   * le profil et la nouvelle expiration, sans quoi le minuteur de rafraîchissement
   * anticipé viserait l'ancienne.
   *
   * L'erreur remonte : c'est l'écran de compte qui porte les trois états.
   */
  changerMotDePasse: (motDePasseActuel: string, nouveauMotDePasse: string) => Promise<void>

  /**
   * Déconnexion de tous les appareils, celui-ci compris.
   *
   * ⚠️ Contrairement à `deconnecter`, elle ne purge **pas** la session locale si
   * l'appel échoue. Le raisonnement s'inverse : laisser l'écran connecté après une
   * déconnexion ratée est le pire défaut, mais afficher « déconnecté partout »
   * alors que les autres appareils sont restés ouverts est un mensonge sur un
   * geste de sécurité.
   */
  deconnecterPartout: () => Promise<void>
}

export const ContexteAuth = createContext<ValeurAuth | null>(null)

export function useAuth(): ValeurAuth {
  const valeur = useContext(ContexteAuth)

  if (valeur === null) {
    throw new Error('useAuth doit être appelé dans <FournisseurAuth>.')
  }

  return valeur
}
