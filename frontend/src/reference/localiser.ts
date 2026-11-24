/**
 * La localisation d'un site (0.3.0) : ce qui se décide hors du composant — le refus local, le corps
 * envoyé, la chaîne d'une coordonnée, les phrases du résultat et d'un refus, la lecture du réglage.
 *
 * ⚠️ Pur, et sans import : chargé par `node --test` (CLAUDE.md §10). Les refus recopient ceux de
 * `SitesController.cs`, que la série lit : un refus local épargne un appel que le serveur refuserait,
 * et il doit le refuser dans les mêmes mots.
 */

export interface CorpsLocalisation {
  adresse: string
  codePostal: string | null
  ville: string | null
}

export interface ResultatLocalisation {
  latitude: number
  longitude: number
  adresseTrouvee: string
  aVerifier: boolean
}

export interface Bornes {
  adresse: number
  codePostal: number
  ville: number
}

/** Ce qu'il faut de la description : le nom des champs et leurs `longueurMax`. */
interface ChampBorne {
  nom: string
  longueurMax?: number
}

interface NomsLocalisation {
  adresse: string
  codePostal: string
  ville: string
}

export const AideLocaliser =
  'Envoie la voie, le code postal et la commune à l’IGN. Pour le domicile d’un particulier, c’est une '
  + 'donnée personnelle : mentionnez-le dans l’information que vous lui donnez.'

export const MotAVerifier = 'à vérifier : le point est approximatif'

/** La borne de la recherche envoyée, mesurée sur le service : de 3 à 200 caractères. */
const LongueurRecherche = 200

export const Refus = {
  adresseVide: 'Saisissez l’adresse du site avant de le localiser.',
  invisible: 'L’adresse, le code postal ou la ville contient un caractère invisible : retapez-les.',
  tropLongue: 'L’adresse et la ville dépassent ensemble 200 caractères : abrégez-les.',
  debut: 'L’adresse doit commencer par un numéro ou une lettre, et compter au moins trois caractères.',
} as const

/**
 * Le même filtre que `ContientInvisible` du serveur : catégories Unicode Control et Format, par point
 * de code (le drapeau `u`) — un caractère de bidirectionnalité ou d'étiquette ne se voit pas.
 */
const Invisible = /[\p{Cc}\p{Cf}]/u

export function bornesDe(champs: readonly ChampBorne[], noms: NomsLocalisation): Bornes {
  const borne = (nom: string): number => {
    const longueur = champs.find((champ) => champ.nom === nom)?.longueurMax
    if (longueur === undefined) throw new Error(`Le champ ${nom} n’a pas de longueurMax.`)
    return longueur
  }

  return { adresse: borne(noms.adresse), codePostal: borne(noms.codePostal), ville: borne(noms.ville) }
}

function recherche(adresse: string, ville: string): string {
  return ville === '' ? adresse : `${adresse} ${ville}`
}

export function refusLocal(adresse: string, codePostal: string, ville: string, bornes: Bornes): string | null {
  const a = adresse.trim()
  const c = codePostal.trim()
  const v = ville.trim()

  if (a === '') return Refus.adresseVide
  if (a.length > bornes.adresse) return `L’adresse dépasse ${bornes.adresse} caractères.`
  if (c.length > bornes.codePostal) return `Le code postal dépasse ${bornes.codePostal} caractères.`
  if (v.length > bornes.ville) return `La ville dépasse ${bornes.ville} caractères.`
  if ([a, c, v].some((texte) => Invisible.test(texte))) return Refus.invisible

  const envoyee = recherche(a, v)
  if (envoyee.length > LongueurRecherche) return Refus.tropLongue
  if (envoyee.length < 3 || !/^[\p{L}\p{N}]/u.test(envoyee)) return Refus.debut

  return null
}

export function corpsLocalisation(adresse: string, codePostal: string, ville: string): CorpsLocalisation {
  const c = codePostal.trim()
  const v = ville.trim()
  return { adresse: adresse.trim(), codePostal: c === '' ? null : c, ville: v === '' ? null : v }
}

/**
 * En chaîne, arrondie à six décimales — le `pas` des champs : les champs nombre et `corpsDe` ne lisent
 * que des chaînes, et un double non arrondi ferait refuser l'enregistrement par le navigateur.
 */
export function chaineCoordonnee(valeur: number): string {
  const arrondie = Number(valeur.toFixed(6))
  return arrondie === 0 ? '0' : String(arrondie)
}

export function phraseTrouvee(resultat: ResultatLocalisation): string {
  return `Latitude et longitude renseignées. Trouvé : ${resultat.adresseTrouvee}`
}

/** Le texte d'un refus ; `null` pour une annulation, qui n'est pas un échec mais le clic suivant. */
export function messageRefus(echec: unknown): string | null {
  if (echec instanceof Error) {
    if (echec.name === 'AbortError') return null
    // `fetch` sans réponse : le message du navigateur est en anglais et varie (« Failed to fetch », « Load failed »).
    if (echec.name === 'TypeError') return 'Le serveur ne répond pas : réessayez, ou saisissez les coordonnées à la main.'
    if (echec.message !== '') return echec.message
  }
  return 'La localisation a échoué.'
}

export function reglageActif(page: unknown): boolean {
  if (typeof page !== 'object' || page === null) return false
  const elements = (page as { elements?: unknown }).elements
  if (!Array.isArray(elements) || elements.length === 0) return false
  const premiere = elements[0] as { geocodageActif?: unknown } | null
  return premiere?.geocodageActif === true
}

export function routeLocalisation(route: string): string {
  return `${route}/localiser`
}
