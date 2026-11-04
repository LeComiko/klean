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

/**
 * Valeurs d'un formulaire, telles qu'elles partiront dans le corps JSON. Un tableau
 * pour les jours cochés : une chaîne jointe serait un format de plus à découper.
 */
export type ValeursFormulaire = Record<string, string | boolean | readonly string[]>

/** Une ligne telle que le serveur la rend. */
export type Ligne = Record<string, unknown>

/** Marque d'une valeur absente dans une cellule. */
export const Vide = '—'

/**
 * Un nombre à la virgule : `0.5` → `0,5`. Le domaine est francophone, et un point
 * décimal se lit comme un séparateur de milliers. Aucun regroupement : les colonnes de
 * chiffres sont en chasse fixe, et une espace insécable de plus les désalignerait.
 */
export function formaterNombre(nombre: number): string {
  return String(nombre).replace('.', ',')
}

/** Le mot affiché pour une valeur de choix, ou la valeur elle-même faute de libellé. */
export function libelleDeChoix(champ: ChampRessource | undefined, valeur: string): string {
  return champ?.libelles?.[valeur] ?? valeur
}

/**
 * Le nom d'une ligne : dans une confirmation, une annonce, et dans la liste déroulante
 * d'une ressource enfant. Passé par les mêmes libellés que le tableau — sans quoi on
 * lirait « Supprimer « Conge » ? » sous une colonne qui affiche « Congé ».
 */
export function etiquetteDe(description: DescriptionRessource, ligne: Ligne): string | undefined {
  const valeur = ligne[description.etiquette]
  const champ = description.champs.find((candidat) => candidat.nom === description.etiquette)

  if (typeof valeur === 'number') return formaterNombre(valeur)
  if (champ?.type === 'choix' && typeof valeur === 'string' && valeur !== '') {
    return libelleDeChoix(champ, valeur)
  }

  return texteDe(valeur)
}

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
      : champ.defaut ?? videDe(champ)
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

/** Valeur d'un champ que rien n'a encore rempli. */
function videDe(champ: ChampRessource): string | boolean | readonly string[] {
  if (champ.type === 'booleen') return false
  if (champ.type === 'jours') return []

  return ''
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

    if (champ.type === 'jours') {
      valeurs[champ.nom] = Array.isArray(brut)
        ? brut.filter((jour): jour is string => typeof jour === 'string')
        : []
      continue
    }

    if (brut === null || brut === undefined) {
      valeurs[champ.nom] = ''
      continue
    }

    // `<input type="time">` attend `hh:mm`, le serveur envoie `hh:mm:ss`. Une valeur
    // avec secondes y est acceptée par certains navigateurs et affichée « 07:00:00 »,
    // par d'autres ignorée — le champ paraît alors vide sur une prestation qui a une
    // heure, et l'enregistrer l'effacerait.
    if (champ.type === 'heure' && typeof brut === 'string') {
      valeurs[champ.nom] = brut.slice(0, 5)
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

    // ⚠️ Un tableau, toujours, et jamais `null` : aucun jour coché s'écrit `[]`, ce
    // que le serveur lit « planifiée à la main ». Dans l'ordre de `choix`, du lundi au
    // dimanche, quel que soit l'ordre des clics — et seulement des valeurs connues,
    // pour qu'une valeur égarée soit retirée ici plutôt que refusée là-bas.
    if (champ.type === 'jours') {
      const coches = Array.isArray(brut) ? brut : []
      corps[champ.nom] = (champ.choix ?? []).filter((jour) => coches.includes(jour))
      continue
    }

    const texte = typeof brut === 'string' ? brut.trim() : ''

    if (texte === '') {
      corps[champ.nom] = champ.requis === true ? '' : null
      continue
    }

    corps[champ.nom] = champ.type === 'nombre'
      ? Number(texte)
      : champ.type === 'heure' && /^\d{2}:\d{2}$/.test(texte) ? `${texte}:00` : texte
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

  if (Array.isArray(valeur)) {
    return valeur.length === 0 ? Vide : valeur.map((jour) => String(jour).slice(0, 3)).join(' ')
  }

  const champ = champs.find((candidat) => candidat.nom === colonne.nom)

  if (champ?.type === 'heure' && typeof valeur === 'string') return valeur.slice(0, 5)

  // ⚠️ Trois fonctions éprouvées, et dans cet ordre. Le serveur envoie les jours à
  // minuit UTC : `jourIso` extrait la sous-chaîne, `depuisChaineIso` construit une
  // date **locale**. Composer autrement — `new Date(valeur)` puis `getDate()` —
  // décale la colonne d'un jour à tout décalage négatif, et le montre nulle part
  // en Europe (CLAUDE.md §10). C'est pourquoi cette série est rejouée dans cinq
  // fuseaux.
  if (champ?.type === 'date' && typeof valeur === 'string') {
    return formaterDateLongue(depuisChaineIso(jourIso(valeur)))
  }

  if (champ?.type === 'choix' && typeof valeur === 'string') return libelleDeChoix(champ, valeur)

  if (typeof valeur === 'number') return formaterNombre(valeur)

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

/**
 * Les valeurs après le choix d'une ligne dans un champ qui `preremplit`.
 *
 * Un champ cible n'est réécrit que s'il est **vide**, ou s'il porte encore la valeur
 * que le choix **précédent** y avait mise. C'est ce qui permet de se raviser —
 * « Hall d'entrée » puis « Escaliers » remplace bien le libellé — sans jamais écraser
 * une saisie : un libellé propre au site, tapé ou déjà enregistré, n'est plus égal au
 * libellé du type, donc il reste.
 *
 * ⚠️ Revenir à « — choisir — » ne vide rien : la saisie reste, seul le lien part.
 *
 * Les valeurs sont comparées en texte, comme le formulaire les tient : une durée de
 * 30 y est la chaîne « 30 ».
 */
export function preremplir(
  champ: ChampRessource,
  valeurs: ValeursFormulaire,
  precedente: Ligne | undefined,
  choisie: Ligne | undefined,
): ValeursFormulaire {
  if (champ.preremplit === undefined || choisie === undefined) return valeurs

  const suivantes: ValeursFormulaire = { ...valeurs }

  for (const [cible, source] of Object.entries(champ.preremplit)) {
    const proposee = texteDe(choisie[source])

    if (proposee === undefined) continue

    const actuelle = valeurs[cible]
    const laissee = precedente === undefined ? undefined : texteDe(precedente[source])

    if (actuelle === '' || actuelle === undefined || actuelle === laissee) {
      suivantes[cible] = proposee
    }
  }

  return suivantes
}

/** Ce qu'une option doit porter pour être filtrée par `optionsProposees`. */
export interface OptionFiltrable {
  valeur: string
  libelle: string
  actif?: boolean
}

/**
 * Les options d'une liste déroulante, sans les lignes désactivées si le champ le
 * demande — **sauf** `gardee`, la valeur enregistrée, rendue et dite « (désactivé) ».
 *
 * `gardee` est la valeur **à l'ouverture**, pas la valeur courante : qui choisit un
 * autre type puis se ravise doit pouvoir revenir à celui que la prestation portait.
 */
export function optionsProposees<O extends OptionFiltrable>(
  champ: ChampRessource,
  options: readonly O[],
  gardee: ValeursFormulaire[string] | undefined,
): readonly O[] {
  if (champ.seulementActifs !== true) return options

  return options
    .filter((option) => option.actif !== false || option.valeur === gardee)
    .map((option) => (option.actif === false
      ? { ...option, libelle: `${option.libelle} (désactivé)` }
      : option))
}

const Voyelles = /^[aeiouyâàäéèêëîïôöùûü]/i

/** « le client », « la prestation », « l’absence ». */
export function designer(singulier: string, genre: Genre): string {
  // L'apostrophe typographique, comme partout ailleurs dans l'interface.
  if (Voyelles.test(singulier)) return `l’${singulier}`

  return genre === 'feminin' ? `la ${singulier}` : `le ${singulier}`
}

/**
 * « Nouveau client », « Nouvelle prestation », « Nouvel agent ».
 *
 * La forme unique « Nouveau » a tenu jusqu'à l'étape 20, et deux boutons sur sept
 * disaient « Nouveau agent » et « Nouveau prestation » : constaté dans le navigateur,
 * jamais en relisant, puisque le code compose le libellé.
 */
export function nouveau(singulier: string, genre: Genre): string {
  if (genre === 'feminin') return `Nouvelle ${singulier}`

  return Voyelles.test(singulier) ? `Nouvel ${singulier}` : `Nouveau ${singulier}`
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
