// ⚠️ Extension explicite : ce module est chargé par `node --test` via
// mouvements.test.ts (CLAUDE.md §10).
import type { MouvementACreer, NatureMouvement } from '../types/stock.ts'

/**
 * La saisie d'un mouvement de stock, sortie du composant pour être éprouvée : c'est la
 * partie qui écrit une donnée fausse au lieu d'afficher quelque chose de faux
 * (CLAUDE.md §11). Une origine envoyée sur une entrée, et le serveur refuse ; une
 * quantité en chaîne, et il refuse aussi, en anglais.
 */

/** Dans l'ordre du quotidien : on reçoit, on déplace, on consomme, on compte. */
export const Natures: readonly NatureMouvement[] = ['Entree', 'Transfert', 'Sortie', 'Inventaire']

export const LibellesNature: Readonly<Record<NatureMouvement, string>> = {
  Entree: 'Entrée',
  Sortie: 'Sortie',
  Transfert: 'Transfert',
  Inventaire: 'Inventaire',
}

/** Ce que chaque nature veut dire, sous la liste où on la choisit. */
export const AidesNature: Readonly<Record<NatureMouvement, string>> = {
  Entree: 'Ce qui arrive du fournisseur, vers un dépôt, un véhicule ou un site.',
  Transfert: 'D’un lieu à un autre : du dépôt vers un véhicule, d’un véhicule vers un site.',
  Sortie: 'Ce qui part sans arriver ailleurs : consommé, perdu, jeté.',
  Inventaire: 'La quantité comptée sur place. Elle remplace la quantité connue, et l’écart reste dans l’historique.',
}

/** Miroir de `ServiceStock.RefusDeForme` : quels côtés la nature exige. */
export function lieuxRequis(nature: NatureMouvement): { origine: boolean; destination: boolean } {
  switch (nature) {
    case 'Entree':
      return { origine: false, destination: true }
    case 'Sortie':
      return { origine: true, destination: false }
    case 'Transfert':
      return { origine: true, destination: true }
    case 'Inventaire':
      // Le lieu compté est la destination : c'est sa quantité qui est réécrite.
      return { origine: false, destination: true }
  }
}

/**
 * Un lieu dans une liste déroulante : `emplacement:<id>` ou `site:<id>`. Les deux sortes
 * partagent une liste, parce que l'utilisateur choisit un **endroit**, pas une table ;
 * le préfixe dit laquelle des deux clés du serveur renseigner.
 */
export function valeurLieu(sorte: 'emplacement' | 'site', id: string): string {
  return `${sorte}:${id}`
}

export function lireLieu(valeur: string): { emplacementId: string | null; siteId: string | null } {
  const [sorte, id] = valeur.split(':', 2)

  if (id === undefined || id === '') return { emplacementId: null, siteId: null }
  if (sorte === 'emplacement') return { emplacementId: id, siteId: null }
  if (sorte === 'site') return { emplacementId: null, siteId: id }

  return { emplacementId: null, siteId: null }
}

/** La valeur de liste d'un côté de mouvement ou d'une ligne de stock, vide sans lieu. */
export function lieuDe(emplacementId: string | null, siteId: string | null): string {
  if (emplacementId !== null) return valeurLieu('emplacement', emplacementId)
  if (siteId !== null) return valeurLieu('site', siteId)

  return ''
}

/** Ce que le formulaire tient : des chaînes, comme ses contrôles. */
export interface SaisieMouvement {
  nature: NatureMouvement
  produitId: string
  origine: string
  destination: string
  quantite: string
}

export function saisieVide(): SaisieMouvement {
  return { nature: 'Entree', produitId: '', origine: '', destination: '', quantite: '' }
}

/** Plafond de la colonne, `numeric(10,2)`, et du `[Range]` du DTO. */
export const QuantiteMaximale = 99999999.99

/** Un nombre, la virgule acceptée ; `NaN` sur une saisie illisible ou vide. */
export function lireQuantite(texte: string): number {
  const nettoye = texte.trim().replace(',', '.')

  return nettoye === '' ? Number.NaN : Number(nettoye)
}

/**
 * Le refus prononcé avant d'appeler, ou `null`.
 *
 * Deux raisons, chacune précise. Une quantité hors bornes tomberait sur le `[Range]` du
 * DTO, qui répond **en anglais** (CLAUDE.md §10). Un produit ou un lieu non choisi
 * partirait en identifiant vide, et le serveur répondrait « Produit 00000000-… inconnu »,
 * juste mais illisible.
 */
export function refusLocal(saisie: SaisieMouvement): string | null {
  const requis = lieuxRequis(saisie.nature)

  if (saisie.produitId === '') return 'Choisir un produit.'
  if (requis.origine && saisie.origine === '') return 'Choisir le lieu de départ.'
  if (requis.destination && saisie.destination === '') {
    return saisie.nature === 'Inventaire' ? 'Choisir le lieu compté.' : 'Choisir le lieu d’arrivée.'
  }

  if (saisie.nature === 'Transfert' && saisie.origine === saisie.destination) {
    return 'Un transfert relie deux lieux différents.'
  }

  const quantite = lireQuantite(saisie.quantite)

  if (Number.isNaN(quantite)) return 'Saisir une quantité.'
  if (quantite > QuantiteMaximale) return 'Quantité trop grande.'

  if (saisie.nature === 'Inventaire') {
    return quantite < 0 ? 'Une quantité comptée n’est jamais négative.' : null
  }

  return quantite <= 0 ? 'La quantité doit être positive. Un stock à zéro se constate par un inventaire.' : null
}

/**
 * Le corps envoyé au serveur.
 *
 * ⚠️ Un côté que la nature n'exige pas part à `null`, **même s'il a été saisi**.
 * Choisir une origine, puis passer la nature sur « Entrée », laisse la valeur dans
 * l'état du formulaire ; l'envoyer ferait refuser le mouvement pour un champ que l'écran
 * n'affiche plus — la leçon du motif masqué d'une absence (CLAUDE.md §10).
 */
export function corpsMouvement(saisie: SaisieMouvement): MouvementACreer {
  const requis = lieuxRequis(saisie.nature)
  const origine = lireLieu(requis.origine ? saisie.origine : '')
  const destination = lireLieu(requis.destination ? saisie.destination : '')

  return {
    produitId: saisie.produitId,
    nature: saisie.nature,
    origineEmplacementId: origine.emplacementId,
    origineSiteId: origine.siteId,
    destinationEmplacementId: destination.emplacementId,
    destinationSiteId: destination.siteId,
    quantite: lireQuantite(saisie.quantite),
  }
}
