// ⚠️ Extension explicite : ce module est chargé par `node --test` via
// ressources.test.ts, et Node n'applique pas la résolution sans extension de Vite
// (CLAUDE.md §10).
import { api } from './client.ts'

/**
 * Enveloppe de pagination des onze listes, en place depuis l'étape 8.
 *
 * ⚠️ `total` est le nombre de lignes **visibles par l'appelant**, filtres
 * multi-tenant et axes d'isolation appliqués — pas le nombre de lignes de la
 * table. Un `ClientSyndic` et un administrateur de la même entreprise lisent donc
 * deux totaux différents sur la même route, et c'est correct.
 */
export interface Page<T> {
  elements: T[]
  total: number
  page: number
  taille: number
}

/** Toute ressource de référence porte au moins un identifiant. */
export interface Ressource {
  id: string
  [propriete: string]: unknown
}

/**
 * Plafond du serveur. Demander davantage ne fait pas échouer la requête : le DTO
 * de pagination borne la valeur silencieusement, donc une page de 500 en rendrait
 * 200 et l'écran afficherait « 1 sur 1 » à tort.
 */
export const TailleMaximale = 200

export const TailleParDefaut = 25

/**
 * Une page d'une ressource, éventuellement restreinte à un parent.
 *
 * `filtre` est la descente de niveau : `{ champ: 'clientId', valeur: '…' }` devient
 * `?clientId=…`, le paramètre que les quatre endpoints de la chaîne d'exploitation
 * acceptent depuis le lot B. Le nom du champ est celui du DTO **et** celui du
 * paramètre de requête : il n'y en a qu'un, donc ils ne peuvent pas diverger.
 *
 * ⚠️ `encodeURIComponent` sur la valeur. Elle vient de la chaîne de requête de
 * l'écran, donc de l'extérieur : un identifiant forgé contenant un `&` ajouterait
 * sinon un paramètre à la requête.
 */
export function listerRessource<T>(
  route: string,
  page: number,
  taille: number,
  signal?: AbortSignal,
  filtre?: { champ: string; valeur: string },
): Promise<Page<T>> {
  const restriction = filtre === undefined
    ? ''
    : `&${filtre.champ}=${encodeURIComponent(filtre.valeur)}`

  return api.get<Page<T>>(
    `${route}?page=${String(page)}&taille=${String(Math.min(taille, TailleMaximale))}${restriction}`,
    { signal },
  )
}

/**
 * Toutes les lignes d'une ressource, pour alimenter une liste déroulante de champ
 * de référence.
 *
 * ⚠️ Une seule page, plafonnée à 200. Au-delà, la liste déroulante est de toute
 * façon le mauvais contrôle — mais l'écran ne le **dit** pas, et c'est une limite
 * assumée : `Reference.tsx` compare `total` au nombre reçu et prévient quand le
 * choix est tronqué, plutôt que de laisser croire qu'un client absent n'existe pas.
 */
export function listerToutPourChoix<T>(route: string, signal?: AbortSignal): Promise<Page<T>> {
  return listerRessource<T>(route, 1, TailleMaximale, signal)
}

export function creerRessource<T>(route: string, corps: unknown): Promise<T> {
  return api.post<T>(route, corps)
}

/**
 * Modification. Le serveur répond 204 sans corps sur les onze ressources : le
 * `PUT` ne rend donc rien, et l'appelant recharge la liste.
 */
export function modifierRessource(route: string, id: string, corps: unknown): Promise<void> {
  return api.put<void>(`${route}/${id}`, corps)
}

export function supprimerRessource(route: string, id: string): Promise<void> {
  return api.supprimer<void>(`${route}/${id}`)
}
