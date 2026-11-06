import { api } from './client'
import type { CodesRecuperation, EtatSecondFacteur, PreparationSecondFacteur } from '../types/auth'

/**
 * Les routes du second facteur qui ne changent pas la session tenue par l'écran
 * (SPEC.md, étape 23). Celles qui la changent — le code de la connexion, la
 * désactivation — passent par `AuthContext`, qui doit recueillir la session rouverte.
 *
 * ⚠️ `activer` rouvre bien une session côté serveur, mais l'écran ne doit pas
 * l'adopter tout de suite : à la connexion, adopter le profil ferait quitter l'écran
 * **avant** d'avoir montré les codes de récupération, qui ne se reverront jamais.
 * L'appelant l'adopte quand l'utilisateur a confirmé les avoir rangés.
 */

/** Ouvert à une session, ou au défi d'équipement de la connexion. */
export function preparerSecondFacteur(): Promise<PreparationSecondFacteur> {
  return api.post<PreparationSecondFacteur>('/api/auth/second-facteur/preparer')
}

export function activerSecondFacteur(code: string): Promise<CodesRecuperation> {
  return api.post<CodesRecuperation>('/api/auth/second-facteur/activer', { code })
}

export function lireEtatSecondFacteur(signal?: AbortSignal): Promise<EtatSecondFacteur> {
  return api.get<EtatSecondFacteur>('/api/auth/second-facteur', { signal })
}

/** Exige un code de l'application : une session volée ne suffit pas à s'en faire remettre. */
export function regenererCodes(code: string): Promise<CodesRecuperation> {
  return api.post<CodesRecuperation>('/api/auth/second-facteur/codes', { code })
}
