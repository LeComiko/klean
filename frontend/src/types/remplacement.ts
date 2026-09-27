/**
 * Miroir de `backend/DTOs/RemplacementDto.cs` et `CandidatDto.cs`.
 */

/** Horodatage de l'API : minuit **UTC** pour un jour, instant précis sinon. */
type HorodatageApi = string

export interface Remplacement {
  id: string
  entrepriseId: string
  interventionId: string
  /** Null quand la vacation n'a jamais eu d'agent — donc aucune absence à l'origine. */
  absenceId: string | null
  /** Null tant qu'aucun agent n'a été proposé. */
  agentRemplacantId: string | null
  /** `Recherche`, `Propose`, `Accepte`, `Refuse` ou `Annule`. */
  statut: string
  /** Score du candidat au moment de la proposition, sur 100. */
  score: number | null
  dateCreation: HorodatageApi
}

/**
 * Un agent candidat, avec son classement.
 *
 * ⚠️ Les trois notes **et** les données brutes qui les produisent sont exposées,
 * et l'écran doit afficher les brutes (CLAUDE.md §8) : un responsable qui ne voit
 * qu'un score ne peut ni le contester ni apprendre à s'en méfier. « 12 km » se
 * discute, « note 60 » ne se discute pas.
 */
export interface Candidat {
  agentId: string
  nom: string
  prenom: string
  /** Moyenne pondérée des trois notes, sur 100. */
  score: number
  noteExperience: number
  noteDistance: number
  noteCharge: number
  /** Passages déjà effectués par cet agent sur ce site. */
  passages: number
  /** Null quand l'agent n'a pas de coordonnées : la note vaut alors 50, neutre. */
  distanceKm: number | null
  /** Heures déjà engagées sur la semaine de l'intervention. */
  heuresSemaine: number
}

/** Statuts qui occupent le terrain, miroir de `RemplacementService.StatutsActifs`. */
export const StatutsActifs: readonly string[] = ['Recherche', 'Propose', 'Accepte']
