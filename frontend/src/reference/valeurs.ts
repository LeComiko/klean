// ⚠️ Extensions explicites : ce module est chargé par `node --test`, qui
// n'applique pas la résolution sans extension de Vite (CLAUDE.md §10).
import type { ChampRessource, ColonneRessource } from '../types/reference.ts'
import { depuisChaineIso, formaterDateLongue, jourIso } from '../utils/dates.ts'

/**
 * Conversions entre une ligne du serveur, le formulaire et le corps JSON.
 *
 * Extrait de `pages/Reference.tsx` pour être **éprouvable**, et pas par principe :
 * c'est la seule partie de l'étape où une erreur écrit des données fausses au lieu
 * d'afficher quelque chose de faux. Une chaîne vide enregistrée à la place d'un
 * `null`, une latitude convertie en zéro, un jour décalé de vingt-quatre heures —
 * aucun de ces trois défauts ne produit d'erreur, et les deux derniers ne se voient
 * pas dans le fuseau du développeur.
 */

/** Valeurs d'un formulaire, telles qu'elles partiront dans le corps JSON. */
export type ValeursFormulaire = Record<string, string | boolean>

/** Une ligne telle que le serveur la rend. */
export type Ligne = Record<string, unknown>

/** Marque d'une valeur absente dans une cellule. */
export const Vide = '—'

/** Valeur d'affichage d'une propriété dont le type n'est pas connu à la compilation. */
export function texteDe(valeur: unknown): string | undefined {
  if (typeof valeur === 'string' && valeur !== '') return valeur
  if (typeof valeur === 'number') return String(valeur)

  return undefined
}

export function departCreation(champs: readonly ChampRessource[]): ValeursFormulaire {
  const valeurs: ValeursFormulaire = {}

  for (const champ of champs) {
    valeurs[champ.nom] = champ.defaut ?? (champ.type === 'booleen' ? false : '')
  }

  return valeurs
}

export function departOuverture(
  champs: readonly ChampRessource[],
  ligne: Ligne,
): ValeursFormulaire {
  const valeurs: ValeursFormulaire = {}

  for (const champ of champs) {
    const brut = ligne[champ.nom]

    if (champ.type === 'booleen') {
      valeurs[champ.nom] = brut === true
      continue
    }

    if (brut === null || brut === undefined) {
      valeurs[champ.nom] = ''
      continue
    }

    // ⚠️ Un `<input type="date">` n'accepte que AAAA-MM-JJ, et le serveur envoie un
    // horodatage complet. `jourIso` en extrait la sous-chaîne **sans** construire de
    // Date : passer par `new Date(brut)` puis `getDate()` rendrait la veille à tout
    // décalage négatif, et rien ne le montrerait à Paris (CLAUDE.md §10).
    valeurs[champ.nom]
      = champ.type === 'date' && typeof brut === 'string' ? jourIso(brut) : String(brut)
  }

  return valeurs
}

/**
 * Corps de la requête, construit champ par champ depuis la description.
 *
 * ⚠️ Une chaîne vide devient `null` sur un champ facultatif, et pas `""`. Les DTOs
 * déclarent ces propriétés nullables ; envoyer une chaîne vide enregistrerait une
 * ville nommée « », qu'aucune recherche ne retrouve et que rien ne distingue d'une
 * ville absente.
 *
 * ⚠️ Sur un champ **obligatoire** laissé vide, la chaîne vide part telle quelle et
 * le serveur refuse. C'est voulu : l'attribut `required` du navigateur rend le cas
 * inatteignable en usage normal, et un refus bruyant vaut mieux qu'une latitude
 * convertie en zéro — qui passerait le `[Range(-90, 90)]` et placerait le site dans
 * le golfe de Guinée, à 600 km au sud d'Accra.
 */
export function corpsDe(
  champs: readonly ChampRessource[],
  valeurs: ValeursFormulaire,
): Record<string, unknown> {
  const corps: Record<string, unknown> = {}

  for (const champ of champs) {
    const brut = valeurs[champ.nom]

    if (champ.type === 'booleen') {
      corps[champ.nom] = brut === true
      continue
    }

    const texte = typeof brut === 'string' ? brut.trim() : ''

    if (texte === '') {
      corps[champ.nom] = champ.requis === true ? '' : null
      continue
    }

    corps[champ.nom] = champ.type === 'nombre' ? Number(texte) : texte
  }

  return corps
}

/**
 * Contenu d'une cellule.
 *
 * `resoudre` traduit l'identifiant d'une ressource parente en son libellé ; il est
 * passé plutôt que résolu ici pour que cette fonction reste pure — c'est la page
 * qui détient les listes chargées.
 */
export function rendu(
  ligne: Ligne,
  colonne: ColonneRessource,
  champs: readonly ChampRessource[],
  resoudre: (ressource: string, identifiant: string) => string,
): string {
  const valeur = ligne[colonne.nom]

  if (valeur === null || valeur === undefined || valeur === '') return Vide

  if (colonne.reference !== undefined) {
    return resoudre(colonne.reference, String(valeur))
  }

  if (typeof valeur === 'boolean') return valeur ? 'Oui' : 'Non'

  const champ = champs.find((candidat) => candidat.nom === colonne.nom)

  // ⚠️ Trois fonctions éprouvées, et dans cet ordre. Le serveur envoie les jours à
  // minuit UTC : `jourIso` extrait la sous-chaîne, `depuisChaineIso` construit une
  // date **locale**. Composer autrement — `new Date(valeur)` puis `getDate()` —
  // décale la colonne d'un jour à tout décalage négatif, et le montre nulle part
  // en Europe (CLAUDE.md §10). C'est pourquoi cette série est rejouée dans cinq
  // fuseaux.
  if (champ?.type === 'date' && typeof valeur === 'string') {
    return formaterDateLongue(depuisChaineIso(jourIso(valeur)))
  }

  return String(valeur)
}
