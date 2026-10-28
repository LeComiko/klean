/**
 * Miroir de `backend/DTOs/PlanningDto.cs`.
 *
 * Les agrégats — `totalVacations`, `totalHeures`, `heures` par agent — sont
 * calculés par le serveur et **recopiés** ici, jamais recalculés par l'écran.
 * Deux clients qui additionnent eux-mêmes finissent par ne pas afficher le même
 * total, et c'est alors le serveur qu'on accuse.
 */

/** Horodatage de l'API : minuit **UTC**. Le lire avec `jourIso`, pas avec `Date`. */
type HorodatageApi = string

/** `hh:mm:ss`, sérialisation d'un `TimeSpan`. */
type HeureApi = string

export interface PlanningSemaine {
  lundi: HorodatageApi
  dimanche: HorodatageApi
  agents: PlanningAgent[]
  /**
   * Les vacations à pourvoir. Celle d'un agent absent y figure **en plus** de la
   * ligne de cet agent, où elle porte `nonPourvue`. La duplication est voulue
   * côté serveur : le trou doit se voir à sa place dans la journée.
   */
  nonPourvues: PlanningNonPourvue[]
  totalVacations: number
  totalHeures: number
  totalNonPourvues: number
}

export interface PlanningAgent {
  agentId: string
  nom: string
  prenom: string
  vacations: PlanningVacation[]
  nombreVacations: number
  heures: number
}

export interface PlanningVacation {
  interventionId: string
  date: HorodatageApi
  heure: HeureApi
  dureeMinutes: number
  siteId: string
  siteNom: string
  prestationLibelle: string
  /** `Planifiee`, `EnCours`, `Terminee` ou `NonCertifiee`. */
  statut: string
  /** Vrai quand l'agent de cette ligne est absent ce jour-là. */
  nonPourvue: boolean
}

export interface PlanningNonPourvue {
  interventionId: string
  date: HorodatageApi
  heure: HeureApi
  dureeMinutes: number
  siteId: string
  siteNom: string
  prestationLibelle: string
  /** Null quand la vacation n'a jamais été affectée. */
  agentId: string | null
  /** « Aucun agent affecté » ou « Agent absent ». */
  cause: string
}

/** Au-delà, la charge hebdomadaire d'un agent s'affiche en `--retard`. */
export const SeuilHeuresHebdomadaires = 30

/**
 * Miroir de `backend/DTOs/GenerationDto.cs` : ce que la génération a fait, et ce qui
 * demande un regard.
 *
 * Aucun nom d'agent, ni ici ni dans les lignes — c'est voulu côté serveur, et l'écran
 * ne doit pas le recomposer à partir de la liste des agents.
 */
export interface RapportGeneration {
  du: HorodatageApi
  au: HorodatageApi
  creees: number
  pourvues: number
  dejaPresentes: number
  nonPourvues: LigneRapportGeneration[]
  feries: LigneRapportGeneration[]
  orphelines: LigneRapportGeneration[]
}

export interface LigneRapportGeneration {
  /** Nul pour un jour férié : rien n'a été créé. */
  interventionId: string | null
  date: HorodatageApi
  heure: HeureApi
  prestationId: string
  prestation: string
  site: string
  cause: string
}
