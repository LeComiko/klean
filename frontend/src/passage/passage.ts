// ⚠️ Extensions explicites : ce module est chargé par `node --test` via
// passage.test.ts (CLAUDE.md §10).
import type {
  AnomalieAgent,
  PointChecklist,
  Pointage,
  Position,
  Preuve,
  StatutAnomalie,
  StatutVacation,
  VacationAgent,
} from '../types/passage.ts'

/**
 * Ce que les écrans du passage décident sans le serveur (SPEC.md, étape 25).
 *
 * Extrait des composants pour la raison de `reference/valeurs.ts` (CLAUDE.md §11) :
 * c'est ici qu'une position pourrait partir fausse, un point de contrôle inconnu être
 * coché, ou une phrase annoncer « certifié » sur un passage qui ne l'est pas.
 */

/** Miroir de `ServicePassage.RayonMetres` : il ne sert qu'aux phrases, le serveur tranche. */
export const RayonMetres = 200

/** Miroir de `AnomaliesController.LongueurMaxDescription`. */
export const LongueurMaxDescription = 1000

/** Miroir de `AnomaliesController.PhotosParAnomalie`. */
export const PhotosParAnomalie = 3

/**
 * Le poids maximal d'une photo réduite. ⚠️ Sous le plafond de corps de l'API, un
 * mébioctet (`Http:TailleMaxCorpsOctets`), avec une marge : au-delà, la requête est
 * refusée en 413 avant d'atteindre le contrôleur, et sans message en français.
 */
export const TailleMaxPhoto = 900 * 1024

/** Les côtés essayés, du plus grand au plus petit, tant que la photo pèse trop. */
export const CotesPhoto: readonly number[] = [1600, 1200, 900, 640]

/** Les qualités JPEG essayées à chaque côté. */
export const QualitesJpeg: readonly number[] = [0.82, 0.7, 0.55]

/**
 * Ce que l'écran dit avant de demander la position, et à chaque fois : le navigateur ne
 * la demande qu'une fois, la personne doit pouvoir relire ce qu'elle a accepté.
 */
export const NoticeLocalisation =
  'Démarrer et Terminer relèvent ta position une seule fois chacun, au moment du geste, '
  + 'pour mesurer la distance au site. Seules la distance et l’heure sont gardées, jamais '
  + 'la position. Sans position, le passage compte quand même, mais n’est pas certifié.'

/**
 * Ce que l'écran dit sous « Ajouter une photo » (RGPD.md §9.9). Une photo de couloir peut
 * montrer l'occupant qui y passe, et c'est la seule donnée du passage qui peut identifier
 * un tiers : la description a sa consigne, la photo a la sienne. C'est la condition de la
 * décision de ne pas journaliser les photos (RGPD.md §8.4).
 */
export const ConsignePhoto = 'Photographie le lieu ou le dégât, jamais une personne.'

/**
 * Le nom d'un statut de vacation, sur *Ma journée* et dans les blocs du planning : un seul
 * jeu, pour qu'une vacation porte le même nom sur les deux écrans (SPEC.md, étape 29).
 */
export const LibellesStatut: Readonly<Record<StatutVacation, string>> = {
  Planifiee: 'À faire',
  EnCours: 'En cours',
  Terminee: 'Certifié',
  NonCertifiee: 'Terminé, non certifié',
  Annulee: 'Annulée',
}

export type Geste = 'Demarrer' | 'Terminer' | 'Aucun'

/**
 * Le geste qu'une vacation propose. ⚠️ Aucune comparaison de date ici : « aujourd'hui »
 * est celui du territoire de l'entreprise, que le serveur connaît et l'écran non. Il
 * refuse en français une vacation qui n'est pas du jour.
 */
export function gesteDe(statut: StatutVacation): Geste {
  if (statut === 'Planifiee') return 'Demarrer'
  if (statut === 'EnCours') return 'Terminer'

  return 'Aucun'
}

/** 35 → `35 m` · 1240 → `1,2 km`. */
export function formaterDistance(metres: number): string {
  if (metres < 1000) return `${String(Math.round(metres))} m`

  return `${(metres / 1000).toFixed(1).replace('.', ',')} km`
}

/** L'heure locale d'un horodatage du serveur — un instant, donc `new Date` est juste ici. */
export function heureDe(horodatage: string): string {
  const date = new Date(horodatage)

  return `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`
}

/** Ce qu'un pointage établit, en une phrase. */
export function phrasePreuve(pointage: Pointage): string {
  const distance = pointage.distanceMetres === null ? '' : formaterDistance(pointage.distanceMetres)

  switch (pointage.preuve) {
    case 'SurSite':
      return `Sur place, à ${distance} du site`
    case 'HorsRayon':
      return `Hors du rayon de ${formaterDistance(RayonMetres)}, à ${distance} du site`
    case 'Imprecise':
      return pointage.precisionMetres === null
        ? 'Position trop imprécise pour certifier'
        : `Position trop imprécise pour certifier (± ${formaterDistance(pointage.precisionMetres)})`
    case 'NonLocalisee':
      return 'Sans position'
  }
}

const CausesParPreuve: Readonly<Record<Exclude<Preuve, 'SurSite'>, string>> = {
  HorsRayon: 'hors du rayon',
  Imprecise: 'avec une position imprécise',
  NonLocalisee: 'sans position',
}

/**
 * Le bilan d'un passage terminé, ou `null` s'il ne l'est pas. Il dit **pourquoi** un
 * passage n'est pas certifié : « non certifié » seul laisserait l'agent croire à une
 * panne, alors qu'il a peut-être refusé la localisation.
 */
export function bilanDe(vacation: VacationAgent): string | null {
  if (vacation.statut === 'Terminee') return 'Passage certifié : les deux pointages ont été faits sur place.'

  if (vacation.statut !== 'NonCertifiee') return null

  // Saisi « fait, sans preuve » par l'exploitation, sans aucun pointage.
  if (vacation.debut === null && vacation.fin === null) {
    return 'Passage enregistré sans pointage : il n’est pas certifié.'
  }

  const causes = [
    ['le début', vacation.debut],
    ['la fin', vacation.fin],
  ] as const

  const raisons = causes.flatMap(([moment, pointage]) =>
    pointage === null || pointage.preuve === 'SurSite' ? [] : [`${moment} ${CausesParPreuve[pointage.preuve]}`])

  return raisons.length === 0
    ? 'Passage terminé, non certifié.'
    : `Passage terminé, non certifié : ${raisons.join(', ')}.`
}

/**
 * Les points cochés, à envoyer à `terminer`. ⚠️ Seulement ceux de la checklist reçue : le
 * serveur refuse **toute** la terminaison sur un identifiant qu'il ne connaît pas, et une
 * coche restée d'une autre vacation suffirait.
 */
export function pointsFaits(checklist: readonly PointChecklist[], coches: ReadonlySet<string>): string[] {
  return checklist
    .map((point) => point.pointControleId)
    .filter((id): id is string => id !== null && coches.has(id))
}

/**
 * Les points que la fin déclarerait non faits. Plus de zéro, « Terminer » se confirme :
 * la checklist est un compte rendu, jamais un blocage, mais une fin à moitié cochée par
 * un doigt qui a glissé ne se rattrape pas — une vacation pointée est figée.
 */
export function pointsNonCoches(checklist: readonly PointChecklist[], coches: ReadonlySet<string>): number {
  const cochables = checklist.filter((point) => point.pointControleId !== null).length

  return cochables - pointsFaits(checklist, coches).length
}

/** `3 points sur 5 cochés`, ou la raison d'une checklist vide. */
export function resumeChecklist(checklist: readonly PointChecklist[], coches: ReadonlySet<string>): string {
  if (checklist.length === 0) return 'Aucun point de contrôle pour cette prestation.'

  const faits = pointsFaits(checklist, coches).length

  return `${String(faits)} point${faits > 1 ? 's' : ''} sur ${String(checklist.length)} coché${faits > 1 ? 's' : ''}`
}

/** Le refus local d'une description, dans les mots du serveur ; `null` si elle passe. */
export function refusDescription(texte: string): string | null {
  const description = texte.trim()

  if (description === '') return 'Décrire l’anomalie : ce qui a été constaté, et où.'

  if (description.length > LongueurMaxDescription) {
    return `La description tient en ${String(LongueurMaxDescription)} caractères au plus.`
  }

  return null
}

/** Les photos qu'une anomalie peut encore recevoir. */
export function photosRestantes(anomalie: AnomalieAgent): number {
  return Math.max(0, PhotosParAnomalie - anomalie.photos)
}

/** Le refus local d'un fichier choisi ; `null` s'il est une image. */
export function refusFichier(fichier: { type: string }): string | null {
  return fichier.type.startsWith('image/') ? null : 'Choisir une photo.'
}

/** Les dimensions d'une image ramenée à `coteMax` sur son plus grand côté — jamais agrandie. */
export function dimensionsReduites(
  largeur: number,
  hauteur: number,
  coteMax: number,
): { largeur: number; hauteur: number } {
  const echelle = Math.min(1, coteMax / Math.max(largeur, hauteur))

  return {
    largeur: Math.max(1, Math.round(largeur * echelle)),
    hauteur: Math.max(1, Math.round(hauteur * echelle)),
  }
}

/** Ce que l'écran garde d'une position du navigateur : trois nombres, rien de plus. */
export function positionDe(coordonnees: { latitude: number; longitude: number; accuracy: number }): Position {
  return {
    latitude: coordonnees.latitude,
    longitude: coordonnees.longitude,
    precisionMetres: coordonnees.accuracy,
  }
}

/** Le délai accordé au téléphone ; au-delà, le geste part sans position. */
export const DelaiLocalisationMs = 15_000

/**
 * Une position, au moment du geste, ou `null` — refus, absence de capteur, délai
 * dépassé. ⚠️ Jamais d'échec : un passage sans position est un passage non certifié, pas
 * un passage impossible (SPEC.md, étape 25).
 *
 * `maximumAge: 0` : une position mise en cache par le téléphone serait celle d'avant le
 * trajet, et certifierait un passage depuis le dépôt.
 */
export function localiser(geolocalisation: Pick<Geolocation, 'getCurrentPosition'> | undefined): Promise<Position | null> {
  if (geolocalisation === undefined) return Promise.resolve(null)

  return new Promise((resoudre) => {
    geolocalisation.getCurrentPosition(
      (position) => {
        resoudre(positionDe(position.coords))
      },
      () => {
        resoudre(null)
      },
      { enableHighAccuracy: true, timeout: DelaiLocalisationMs, maximumAge: 0 },
    )
  })
}

// ---------------------------------------------------------------------------
// Les anomalies, côté exploitation
// ---------------------------------------------------------------------------

export type FiltreAnomalies = StatutAnomalie | 'Toutes'

/** Ouvertes d'abord : c'est ce qui reste à faire. */
export const FiltresAnomalies: readonly FiltreAnomalies[] = ['Ouverte', 'Traitee', 'Toutes']

export const LibellesFiltreAnomalies: Readonly<Record<FiltreAnomalies, string>> = {
  Ouverte: 'Ouvertes',
  Traitee: 'Traitées',
  Toutes: 'Toutes',
}

export const LibellesStatutAnomalie: Readonly<Record<StatutAnomalie, string>> = {
  Ouverte: 'Ouverte',
  Traitee: 'Traitée',
}

/** Le paramètre `statut` de la liste : `null` pour toutes, sinon le nom exact. */
export function statutDuFiltre(filtre: FiltreAnomalies): StatutAnomalie | null {
  return filtre === 'Toutes' ? null : filtre
}
