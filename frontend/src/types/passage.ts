/**
 * Le passage de l'agent, miroir de `backend/DTOs/PassageDto.cs` et de
 * `backend/DTOs/AnomalieDto.cs` (SPEC.md, étape 25).
 *
 * ⚠️ Aucune coordonnée dans ce qui revient du serveur, et il ne doit jamais en
 * apparaître : la position part avec le geste, sert au calcul de la distance, et n'est
 * pas enregistrée. Si un champ `latitude` apparaissait dans `Pointage`, c'est que le
 * backend aurait régressé.
 */

/** `StatutIntervention`, miroir de `backend/Models/Intervention.cs`. */
export type StatutVacation = 'Planifiee' | 'EnCours' | 'Terminee' | 'NonCertifiee' | 'Annulee'

/** `PreuvePointage`, miroir de `backend/Models/Pointage.cs`. */
export type Preuve = 'SurSite' | 'HorsRayon' | 'Imprecise' | 'NonLocalisee'

export interface Pointage {
  /** Horodatage UTC du serveur : un instant, pas un jour à minuit. */
  horodatage: string
  preuve: Preuve
  distanceMetres: number | null
  precisionMetres: number | null
}

export interface PointChecklist {
  /** Nul si le point a été supprimé depuis : la ligne déclarée, elle, reste. */
  pointControleId: string | null
  libelle: string
  /** Nul tant que la vacation n'est pas terminée. */
  fait: boolean | null
}

export interface AnomalieAgent {
  id: string
  description: string
  /** Le nombre de photos ; l'agent ne les relit pas. */
  photos: number
}

/** Réponse de `GET /api/interventions/ma-journee`, de `demarrer` et de `terminer`. */
export interface VacationAgent {
  id: string
  /** Minuit UTC : un jour, à lire par `jourIso` (CLAUDE.md §10). */
  datePrevue: string
  /** « HH:MM:SS ». */
  heurePrevue: string
  dureeEstimeeMinutes: number
  statut: StatutVacation
  prestation: string
  site: string
  adresse: string
  codePostal: string | null
  ville: string | null
  consignes: string | null
  debut: Pointage | null
  fin: Pointage | null
  checklist: PointChecklist[]
  anomalies: AnomalieAgent[]
}

/** Ce que rend l'API de géolocalisation du navigateur, et rien de plus. */
export interface Position {
  latitude: number
  longitude: number
  precisionMetres: number
}

/** `StatutAnomalie`, miroir de `backend/Models/Anomalie.cs`. */
export type StatutAnomalie = 'Ouverte' | 'Traitee'

/** Une anomalie, telle que la lisent le chef et le responsable. */
export interface Anomalie {
  id: string
  interventionId: string
  siteId: string
  site: string
  /** Minuit UTC. */
  datePrevue: string
  description: string
  statut: StatutAnomalie
  /** Instant UTC. */
  dateSignalement: string
  auteurId: string
  dateTraitement: string | null
  traiteeParId: string | null
  /** Les identifiants des photos, dans l'ordre de leur ajout. */
  photos: string[]
}
