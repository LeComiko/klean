// ⚠️ Extensions explicites : ce module est chargé par `node --test`, qui
// n'applique pas la résolution sans extension de Vite (CLAUDE.md §10).
import type {
  ChampRessource,
  ColonneRessource,
  DescriptionRessource,
  Genre,
} from '../types/reference.ts'
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

/**
 * Valeurs d'ouverture d'une création.
 *
 * `imposees` sert la descente de niveau : arrivé sur « les sites de ce client », le
 * formulaire part avec ce client déjà choisi. Ce n'est pas du confort — c'est le seul
 * chemin qui évite la liste déroulante des contrats, dont les options sont libellées
 * par une fréquence et peuvent donc se ressembler.
 *
 * `imposees` l'emporte sur `defaut`. Le test porte sur la **présence de la clé** et
 * non sur la vérité de sa valeur, parce que c'est ce qu'on veut dire — mais il faut
 * être exact : `??` donnerait aujourd'hui le même résultat, puisqu'il ne retombe que
 * sur `null` et `undefined`, qu'une `ValeursFormulaire` ne peut pas porter. Un
 * sabotage remplaçant `in` par `??` est resté vert, et c'est normal. La forme retenue
 * ne corrige donc rien : elle dit l'intention, et resterait juste si le type venait à
 * admettre `undefined`.
 */
export function departCreation(
  champs: readonly ChampRessource[],
  imposees: ValeursFormulaire = {},
): ValeursFormulaire {
  const valeurs: ValeursFormulaire = {}

  for (const champ of champs) {
    valeurs[champ.nom] = champ.nom in imposees
      ? imposees[champ.nom]
      : champ.defaut ?? (champ.type === 'booleen' ? false : '')
  }

  return valeurs
}

/**
 * Vrai si ce champ doit **disparaître**, compte tenu de ce qui est saisi.
 *
 * ⚠️ Le cas qui l'a fait naître : le motif d'une absence `Maladie`, donnée de santé
 * au sens de l'article 9 du RGPD. Le serveur refuse déjà ce motif en 400 ; sans cette
 * fonction, le formulaire invite à l'écrire puis le fait refuser, ce qui est la pire
 * des deux situations — la donnée a été saisie, et le champ fautif n'est plus visible
 * quand le refus s'affiche.
 */
export function masque(champ: ChampRessource, valeurs: ValeursFormulaire): boolean {
  const condition = champ.masquerSi

  if (condition === undefined) return false

  const pivot = valeurs[condition.champ]

  return typeof pivot === 'string' && condition.vaut.includes(pivot)
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
    // ⚠️ Un champ masqué part à `null`, quelle que soit la valeur qu'il portait avant
    // que la condition ne devienne vraie. Sans cette ligne, saisir un motif puis
    // basculer le type sur « Maladie » enverrait ce texte : le serveur refuserait en
    // 400 en parlant de donnée de santé, et l'écran n'afficherait plus le champ dont
    // il parle. La donnée aurait de surcroît quitté le navigateur.
    if (masque(champ, valeurs)) {
      corps[champ.nom] = null
      continue
    }

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

/** Filtre de descente de niveau, lu dans la chaîne de requête de l'écran. */
export interface FiltreParent {
  /** Nom du champ porteur de la clé étrangère, et du paramètre de requête. */
  champ: string
  valeur: string
}

/**
 * Le filtre de parenté actif, s'il y en a un.
 *
 * `lire` est passé plutôt qu'une `URLSearchParams` pour que cette fonction reste
 * éprouvable sans environnement de navigateur — c'est la même raison que `resoudre`
 * dans `rendu`.
 *
 * ⚠️ Une valeur **vide** vaut absence de filtre, et c'est mesuré côté serveur : un
 * `Guid?` recevant `?clientId=` est lié à `null` par ASP.NET, qui rend donc la liste
 * entière. Si l'écran y voyait un filtre, il afficherait « Sites de … » au-dessus de
 * tous les sites de l'entreprise. Les deux lectures doivent coïncider, et la série
 * `FiltresListeTests` garde celle du serveur.
 */
export function filtreDe(
  description: DescriptionRessource,
  lire: (nom: string) => string | null,
): FiltreParent | undefined {
  const parent = description.parent

  if (parent === undefined) return undefined

  const valeur = lire(parent.champ)

  if (valeur === null || valeur.trim() === '') return undefined

  return { champ: parent.champ, valeur }
}

/**
 * Formules de langue des écrans de référence.
 *
 * Sorties du composant pour la même raison que les conversions : « Supprimer
 * « Lundi, mercredi, vendredi » ? » ne dit pas ce qui est supprimé, et sur l'écran
 * des contrats c'est le genre de défaut qu'on ne voit qu'en le lisant à voix haute.
 */

const Voyelles = /^[aeiouyâàäéèêëîïôöùûü]/i

/** « le client », « la prestation », « l’absence ». */
export function designer(singulier: string, genre: Genre): string {
  // L'apostrophe typographique, comme partout ailleurs dans l'interface.
  if (Voyelles.test(singulier)) return `l’${singulier}`

  return genre === 'feminin' ? `la ${singulier}` : `le ${singulier}`
}

/** Accord du participe passé : « créé » ou « créée ». */
export function accorder(genre: Genre): string {
  return genre === 'feminin' ? 'e' : ''
}

/**
 * « tous les » ou « toutes les ».
 *
 * ⚠️ Le pluriel du libellé n'est **pas** fabriqué en accolant un `s` au singulier :
 * `libelle` le porte déjà, écrit à la main pour cette raison. Seul le déterminant
 * s'accorde.
 */
export function tousLes(genre: Genre): string {
  return genre === 'feminin' ? 'toutes les' : 'tous les'
}

export function majuscule(mot: string): string {
  return mot.charAt(0).toUpperCase() + mot.slice(1)
}
