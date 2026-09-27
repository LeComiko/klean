/**
 * Utilitaires de date, écrits à la main : `date-fns` n'est pas autorisé
 * (CLAUDE.md §3), et l'API `Intl` ne l'est pas davantage pour les noms de jours
 * — les données ICU du navigateur ne peuvent pas être supposées présentes, et le
 * backend a déjà tranché dans le même sens avec son `EnFrancais(DayOfWeek)`.
 */

const JoursCourts = ['lun.', 'mar.', 'mer.', 'jeu.', 'ven.', 'sam.', 'dim.'] as const
const JoursLongs = [
  'lundi', 'mardi', 'mercredi', 'jeudi', 'vendredi', 'samedi', 'dimanche',
] as const
const Mois = [
  'janvier', 'février', 'mars', 'avril', 'mai', 'juin',
  'juillet', 'août', 'septembre', 'octobre', 'novembre', 'décembre',
] as const

const MillisecondesParJour = 86_400_000

function deuxChiffres(valeur: number): string {
  return valeur < 10 ? `0${String(valeur)}` : String(valeur)
}

/** Index du jour dans une semaine commençant le lundi : 0 = lundi, 6 = dimanche. */
function indexLundiPremier(date: Date): number {
  return (date.getDay() + 6) % 7
}

/**
 * `AAAA-MM-JJ` d'une date, à partir de ses composantes **locales**.
 *
 * ⚠️ Ne jamais utiliser `toISOString()` ici. Elle convertit en UTC d'abord : sur
 * un minuit local à Paris en septembre (UTC+2), elle rend `…T22:00:00Z` la
 * veille, donc le **mauvais jour**. Le décalage est invisible en test si la
 * machine est en UTC, et le planning se décale d'un jour en production.
 */
export function versChaineIso(date: Date): string {
  return `${String(date.getFullYear())}-${deuxChiffres(date.getMonth() + 1)}-${deuxChiffres(date.getDate())}`
}

/** Le lundi de la semaine contenant `date`, à minuit local. */
export function lundiDeLaSemaine(date: Date): Date {
  return new Date(
    date.getFullYear(),
    date.getMonth(),
    date.getDate() - indexLundiPremier(date),
  )
}

/**
 * Numéro de semaine ISO 8601 : la semaine 1 est celle qui contient le premier
 * jeudi de l'année. Compter les semaines depuis le 1er janvier donnerait un
 * numéro faux une année sur deux, autour du changement d'année.
 */
export function numeroSemaine(date: Date): number {
  const jeudi = new Date(date.getFullYear(), date.getMonth(), date.getDate())
  jeudi.setDate(jeudi.getDate() - indexLundiPremier(jeudi) + 3)

  const premierJeudi = new Date(jeudi.getFullYear(), 0, 4)
  premierJeudi.setDate(premierJeudi.getDate() - indexLundiPremier(premierJeudi) + 3)

  // Math.round et non une division entière : un changement d'heure décale la
  // différence d'une heure, ce qui tronquerait le quotient vers le bas.
  return 1 + Math.round((jeudi.getTime() - premierJeudi.getTime()) / (7 * MillisecondesParJour))
}

/**
 * Date locale à partir d'un `AAAA-MM-JJ`.
 *
 * ⚠️ Pas `new Date('2026-09-07')`. La norme impose de lire une date nue comme de
 * l'**UTC**, donc dans tout fuseau à décalage négatif l'objet obtenu tombe la
 * veille au soir, et `getDate()` rend 6. Le pendant exact du piège de
 * `toISOString` dans `versChaineIso`.
 */
export function depuisChaineIso(iso: string): Date {
  const morceaux = iso.split('-')

  return new Date(Number(morceaux[0]), Number(morceaux[1]) - 1, Number(morceaux[2]))
}

export function ajouterJours(date: Date, nombre: number): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate() + nombre)
}

/**
 * Le jour d'un horodatage de l'API, sous la forme `AAAA-MM-JJ`.
 *
 * ⚠️ Extraction textuelle, et c'est délibéré. Le serveur envoie minuit **UTC**
 * (`2026-09-07T00:00:00Z`). Passer par `new Date(...)` puis `getDate()` rend 7 à
 * Paris mais **6** dans tout fuseau à décalage négatif, et la vacation changerait
 * de colonne. La sous-chaîne, elle, ne dépend d'aucun fuseau.
 */
export function jourIso(horodatage: string): string {
  return horodatage.slice(0, 10)
}

/** `08:00:00` → `08:00`. */
export function formaterHeure(heure: string): string {
  return heure.slice(0, 5)
}

/** `08:00:00` + 120 → `08:00 – 10:00`. */
export function formaterPlage(heure: string, dureeMinutes: number): string {
  const morceaux = heure.split(':')
  const heures = Number(morceaux[0])
  const minutes = Number(morceaux[1])
  const finEnMinutes = heures * 60 + minutes + dureeMinutes
  const fin = `${deuxChiffres(Math.floor(finEnMinutes / 60) % 24)}:${deuxChiffres(finEnMinutes % 60)}`

  // Espace insécable avant le tiret : la plage ne doit pas se couper en fin de
  // ligne dans un bloc de planning étroit.
  return `${formaterHeure(heure)} – ${fin}`
}

/** 120 → `2 h` · 90 → `1 h 30` · 45 → `45 min`. */
export function formaterDuree(minutes: number): string {
  if (minutes < 60) return `${String(minutes)} min`

  const heures = Math.floor(minutes / 60)
  const reste = minutes % 60

  return reste === 0 ? `${String(heures)} h` : `${String(heures)} h ${deuxChiffres(reste)}`
}

/** `9.5` → `9,5 h`. Virgule décimale : le domaine est francophone. */
export function formaterHeures(heures: number): string {
  const arrondi = Math.round(heures * 100) / 100
  return `${arrondi.toString().replace('.', ',')} h`
}

export function jourCourt(index: number): string {
  return JoursCourts[index] ?? ''
}

export function jourLong(index: number): string {
  return JoursLongs[index] ?? ''
}

/** `mercredi` pour le 9 septembre 2026. */
export function jourDeLaSemaine(date: Date): string {
  return jourLong(indexLundiPremier(date))
}

/** `7 septembre 2026`. */
export function formaterDateLongue(date: Date): string {
  return `${String(date.getDate())} ${Mois[date.getMonth()] ?? ''} ${String(date.getFullYear())}`
}

/**
 * `7 – 13 septembre 2026`, et `30 août – 5 septembre 2026` à cheval sur deux
 * mois. Répéter le mois quand il est le même alourdit une ligne lue cinquante
 * fois par jour.
 */
export function formaterPlageDeSemaine(lundi: Date, dimanche: Date): string {
  const memeMois = lundi.getMonth() === dimanche.getMonth()
    && lundi.getFullYear() === dimanche.getFullYear()

  return memeMois
    ? `${String(lundi.getDate())} – ${formaterDateLongue(dimanche)}`
    : `${String(lundi.getDate())} ${Mois[lundi.getMonth()] ?? ''} – ${formaterDateLongue(dimanche)}`
}
