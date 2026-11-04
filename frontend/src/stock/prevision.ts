// ⚠️ Extension explicite : ce module est chargé par `node --test` via
// prevision.test.ts (CLAUDE.md §10).
import type { PrevisionProduit } from '../types/stock.ts'

/**
 * La lecture de la prévision, sortie de la page pour être éprouvée : un état mal lu
 * affiche « Sans alerte » sur un produit qui manque, sans qu'aucune erreur ne se lève.
 */

/** De 1 à 12, comme `ServicePrevisionStock.SemainesMaximum`. */
export const Horizons: readonly number[] = Array.from({ length: 12 }, (_, index) => index + 1)

export const HorizonParDefaut = 4

/**
 * Un état de stock : un mot, et le ton de sa pastille.
 *
 * ⚠️ Jamais la terre cuite : elle est réservée au « non pourvu » (CLAUDE.md §12). Une
 * alerte de stock porte le ton du retard — elle demande d'agir bientôt, pas tout de suite.
 */
export interface Etat {
  mot: string
  ton: 'alerte' | 'calme'
}

/**
 * Les états d'un produit, la couverture d'abord : elle dit qu'un site manquera, le seuil
 * seulement que le total est bas. Les deux peuvent se cumuler, et les deux se disent.
 */
export function etatsDe(ligne: PrevisionProduit): readonly Etat[] {
  const etats: Etat[] = []

  if (ligne.couvertureCourte) etats.push({ mot: 'Couverture courte', ton: 'alerte' })
  if (ligne.sousLeSeuil) etats.push({ mot: 'Sous le seuil', ton: 'alerte' })

  return etats.length > 0 ? etats : [{ mot: 'Sans alerte', ton: 'calme' }]
}

/** `0.5` → `0,5` · `12` → `12` · `1.256` → `1,26`. Virgule : le domaine est francophone. */
export function formaterQuantite(quantite: number): string {
  const arrondi = Math.round(quantite * 100) / 100

  return arrondi.toString().replace('.', ',')
}

/**
 * La phrase des vacations comptées pour rien, ou `null` s'il n'y en a pas.
 *
 * ⚠️ Elle n'est pas facultative : un besoin sous-estimé en silence ressemble à un stock
 * suffisant. La sortie est nommée — donner un type à la prestation.
 */
export function phraseSansType(nombre: number): string | null {
  if (nombre <= 0) return null

  return nombre === 1
    ? '1 vacation de la période n’a pas de type de prestation : elle ne compte dans aucun besoin. '
      + 'Choisir un type dans le catalogue pour sa prestation.'
    : `${String(nombre)} vacations de la période n’ont pas de type de prestation : elles ne comptent `
      + 'dans aucun besoin. Choisir un type dans le catalogue pour leurs prestations.'
}

/** Le résumé des alertes en une phrase, pour l'annonce de l'écran. */
export function resumeAlertes(produits: readonly PrevisionProduit[]): string {
  const courts = produits.filter((ligne) => ligne.couvertureCourte).length
  const bas = produits.filter((ligne) => ligne.sousLeSeuil).length

  if (courts === 0 && bas === 0) return 'Aucune alerte sur la période.'

  const morceaux: string[] = []

  if (courts > 0) morceaux.push(`${String(courts)} produit${courts > 1 ? 's' : ''} en couverture courte`)
  if (bas > 0) morceaux.push(`${String(bas)} sous le seuil`)

  return `${morceaux.join(', ')}.`
}

/** L'horizon en toutes lettres : « 1 semaine », « 4 semaines ». */
export function libelleHorizon(semaines: number): string {
  return `${String(semaines)} semaine${semaines > 1 ? 's' : ''}`
}
