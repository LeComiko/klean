import { createContext, useContext } from 'react'

import type { EtapeConnexion, Profil, SaisieSecondFacteur, Session } from '../types/auth'

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

  /**
   * Premier temps de la connexion. Rend `null` si la session est ouverte, sinon
   * l'étape qui suit : le mot de passe ne suffit pas pour un compte équipé, ou pour un
   * compte que son rôle oblige à s'équiper (SPEC.md, étape 23).
   */
  connecter: (email: string, motDePasse: string) => Promise<EtapeConnexion | null>

  /** Second temps : le code ouvre la session. L'erreur remonte, l'écran la dit. */
  saisirCode: (saisie: SaisieSecondFacteur) => Promise<void>

  /**
   * Adopte une session que le serveur a déjà ouverte — celle que rend l'activation du
   * second facteur. Appelée **après** que l'utilisateur a vu ses codes de
   * récupération : adoptée plus tôt, elle ferait quitter l'écran de connexion avant.
   */
  adopterSession: (session: Session) => void

  /**
   * Retire le second facteur, sur un code de l'application ou un code de récupération
   * — le seul que garde qui a perdu son téléphone. Pour un compte que son rôle oblige,
   * le serveur ferme toutes ses sessions (204) : la session locale est alors purgée, et
   * la connexion suivante impose l'équipement du nouveau téléphone.
   */
  desactiverSecondFacteur: (saisie: SaisieSecondFacteur) => Promise<void>

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
