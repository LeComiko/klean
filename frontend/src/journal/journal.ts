// ⚠️ Extensions explicites : ce module est chargé par `node --test` via journal.test.ts
// (CLAUDE.md §10).
import type { AccesJournalise } from '../types/journal.ts'
import { ajouterJours, depuisChaineIso } from '../utils/dates.ts'

/**
 * Ce que l'écran du journal d'accès décide sans rendre (SPEC.md, étape 27) : les libellés,
 * les noms de l'auteur et de la cible, la période et la requête.
 *
 * Extrait du composant pour la raison de `comptes.ts` (CLAUDE.md §11) : c'est la partie
 * qui écrirait une chose **fausse** au lieu d'afficher mal — un journal qui nomme la
 * mauvaise personne, ou une période décalée d'un jour, ressemble à une réponse.
 */

/** Les noms de `RessourceAccedee`, dans l'ordre du serveur. `journal.test.ts` les compare. */
export const Ressources = [
  'Agent', 'Absence', 'Candidats', 'Planning', 'JournalAcces', 'Compte', 'ChoixAgents',
] as const

/** Les noms de `NatureAcces`, dans l'ordre du serveur. */
export const Natures = [
  'Liste', 'Lecture', 'Anonymisation', 'Export', 'ReinitialisationSecondFacteur',
  'Desactivation', 'Reactivation',
] as const

export type RessourceAccedee = (typeof Ressources)[number]
export type NatureAcces = (typeof Natures)[number]

export const LibellesRessource: Record<RessourceAccedee, string> = {
  Agent: 'Fiches agents',
  Absence: 'Absences',
  Candidats: 'Candidats à une vacation',
  Planning: 'Planning',
  JournalAcces: 'Journal d’accès',
  Compte: 'Comptes',
  ChoixAgents: 'Noms des agents, pour un choix',
}

/**
 * ⚠️ `Anonymisation` s'affiche « Pseudonymisation » : c'est ce que le geste est, l'identifiant
 * survivant (CLAUDE.md §7 bis). Le nom du serveur garde celui de la route.
 */
export const LibellesNature: Record<NatureAcces, string> = {
  Liste: 'Liste',
  Lecture: 'Lecture',
  Anonymisation: 'Pseudonymisation',
  Export: 'Export du dossier',
  ReinitialisationSecondFacteur: 'Double authentification réinitialisée',
  Desactivation: 'Fermeture du compte',
  Reactivation: 'Réouverture du compte',
}

/** Le libellé d'un nom du serveur ; un nom inconnu reste lisible tel quel, sans blanc. */
export function libelleRessource(ressource: string): string {
  return (LibellesRessource as Record<string, string | undefined>)[ressource] ?? ressource
}

export function libelleNature(nature: string): string {
  return (LibellesNature as Record<string, string | undefined>)[nature] ?? nature
}

/** Les huit premiers caractères : de quoi recouper, sans une colonne d'UUID. */
export function identifiantCourt(id: string): string {
  return id.slice(0, 8)
}

/**
 * Les noms que l'écran connaît : les comptes et les fiches agents de l'entreprise.
 *
 * ⚠️ `incomplet` dit que l'une des deux listes a été tronquée à 200, ou n'a pas pu être
 * chargée : un identifiant introuvable n'est alors pas la preuve qu'une personne n'existe pas.
 */
export interface Annuaire {
  comptes: ReadonlyMap<string, string>
  agents: ReadonlyMap<string, string>
  incomplet: boolean
}

export const AnnuaireVide: Annuaire = { comptes: new Map(), agents: new Map(), incomplet: true }

/** Un compte, nommé si l'annuaire le connaît ; sinon son identifiant court, jamais un blanc. */
export function nomDeCompte(id: string, annuaire: Annuaire): string {
  return annuaire.comptes.get(id) ?? `Compte ${identifiantCourt(id)}`
}

/** L'auteur d'un accès. */
export function auteurDe(acces: AccesJournalise, annuaire: Annuaire): string {
  return nomDeCompte(acces.utilisateurId, annuaire)
}

function pluriel(nombre: number, mot: string): string {
  return `${String(nombre)} ${mot}${nombre > 1 ? 's' : ''}`
}

/**
 * Ce que vise un accès : une personne nommée, un identifiant, ou un nombre de lignes.
 *
 * ⚠️ **Seuls une fiche agent et un compte se nomment.** Une absence reste un identifiant,
 * délibérément : la nommer par son agent mettrait à l'écran « l'absence de Marie » à côté
 * de « qui l'a lue », ce qu'aucun choix n'a demandé (SPEC.md, étape 27). Une vacation ou
 * un remplacement, cible d'une liste de candidats, ne nomme personne non plus.
 */
export function cibleDe(acces: AccesJournalise, annuaire: Annuaire): string {
  const lignes = pluriel(acces.lignes, 'ligne')

  if (acces.cibleId === null) return acces.nature === 'Liste' ? lignes : '—'

  const court = identifiantCourt(acces.cibleId)

  switch (acces.ressource) {
    case 'Agent':
      return annuaire.agents.get(acces.cibleId) ?? `Fiche ${court}`
    case 'Compte':
      return nomDeCompte(acces.cibleId, annuaire)
    case 'Absence':
      return `Absence ${court}`
    case 'Candidats':
      return `${lignes}, pour ${court}`
    default:
      return acces.nature === 'Liste' ? `${lignes}, pour ${court}` : court
  }
}

/** Les filtres de l'écran, en chaînes, comme toute saisie. Vide : le filtre est ouvert. */
export interface FiltresJournal {
  utilisateurId: string
  ressource: string
  nature: string
  /** `AAAA-MM-JJ`, jour local, inclus. */
  du: string
  /** `AAAA-MM-JJ`, jour local, inclus. */
  au: string
}

export const FiltresVides: FiltresJournal = { utilisateurId: '', ressource: '', nature: '', du: '', au: '' }

/**
 * Deux jours locaux, inclus, vers les deux instants de la route : le début du premier jour,
 * et le début du lendemain du second — `jusqua` est exclu côté serveur (SPEC.md, étape 27).
 *
 * ⚠️ `toISOString()` est juste ici, et c'est l'inverse du piège de `versChaineIso`
 * (CLAUDE.md §10) : on veut l'**instant** du minuit local, pas le jour qu'il porte. Le
 * minuit vient de `depuisChaineIso`, jamais de `new Date('AAAA-MM-JJ')`, lu en UTC.
 */
export function periodeDe(du: string, au: string): { depuis?: string; jusqua?: string } {
  return {
    ...(du === '' ? {} : { depuis: depuisChaineIso(du).toISOString() }),
    ...(au === '' ? {} : { jusqua: ajouterJours(depuisChaineIso(au), 1).toISOString() }),
  }
}

/** Le refus local d'une période, ou `null`. Les chaînes `AAAA-MM-JJ` se comparent comme du texte. */
export function refusDePeriode(du: string, au: string): string | null {
  return du !== '' && au !== '' && au < du ? 'La fin de la période précède son début.' : null
}

/**
 * La requête d'une page. Seuls les filtres posés partent : un paramètre vide vaudrait
 * aussi absence de filtre, mais une requête qui ne dit que ce qu'elle demande se relit.
 *
 * ⚠️ `encodeURIComponent` sur chaque valeur : l'instant porte des `:`, et un `+` de décalage
 * deviendrait une espace, comme dans `api/ressources.ts`.
 */
export function routeDuJournal(filtres: FiltresJournal, page: number, taille: number): string {
  const parametres: [string, string][] = [
    ['page', String(page)],
    ['taille', String(taille)],
  ]

  if (filtres.utilisateurId !== '') parametres.push(['utilisateurId', filtres.utilisateurId])
  if (filtres.ressource !== '') parametres.push(['ressource', filtres.ressource])
  if (filtres.nature !== '') parametres.push(['nature', filtres.nature])

  const { depuis, jusqua } = periodeDe(filtres.du, filtres.au)
  if (depuis !== undefined) parametres.push(['depuis', depuis])
  if (jusqua !== undefined) parametres.push(['jusqua', jusqua])

  return `/api/journal-acces?${parametres.map(([cle, valeur]) => `${cle}=${encodeURIComponent(valeur)}`).join('&')}`
}

function deuxChiffres(valeur: number): string {
  return valeur < 10 ? `0${String(valeur)}` : String(valeur)
}

/** `12/11/2026 10:05`, en heure locale : un horodatage est un instant, pas un jour. */
export function formaterInstant(instant: string): string {
  const date = new Date(instant)

  return `${deuxChiffres(date.getDate())}/${deuxChiffres(date.getMonth() + 1)}/${String(date.getFullYear())} `
    + `${deuxChiffres(date.getHours())}:${deuxChiffres(date.getMinutes())}`
}
