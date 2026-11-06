import { api } from './client'
import type { Compte, CompteCree, CreationCompte } from '../types/comptes'

/**
 * Les comptes de l'entreprise, administrateur seul (SPEC.md, étape 24). La liste et
 * les quatre gestes de ligne sont journalisés côté serveur : ils portent sur des
 * collègues.
 */

export function lireComptes(signal?: AbortSignal): Promise<Compte[]> {
  return api.get<Compte[]>('/api/auth/comptes', { signal })
}

export function creerCompte(corps: CreationCompte): Promise<CompteCree> {
  return api.post<CompteCree>('/api/auth/comptes', corps)
}

export function fermerCompte(id: string): Promise<Compte> {
  return api.post<Compte>(`/api/auth/comptes/${id}/desactiver`)
}

export function rouvrirCompte(id: string): Promise<Compte> {
  return api.post<Compte>(`/api/auth/comptes/${id}/reactiver`)
}

export function reinitialiserSecondFacteur(id: string): Promise<unknown> {
  return api.post<unknown>(`/api/auth/comptes/${id}/reinitialiser-second-facteur`)
}

export function pseudonymiserCompte(id: string): Promise<unknown> {
  return api.post<unknown>(`/api/auth/comptes/${id}/anonymiser`)
}
