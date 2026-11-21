/**
 * Une ligne du journal d'accès, miroir de `JournalAccesDto` (SPEC.md, étapes 17 et 27).
 *
 * ⚠️ Rien de ce qui a été lu n'y figure : qui, quelle ressource, quand, combien — jamais
 * quoi. Les noms de l'auteur et de la cible sont résolus par l'écran, jamais stockés.
 */
export interface AccesJournalise {
  id: string
  entrepriseId: string
  utilisateurId: string
  /** Un nom de `RessourceAccedee`, côté serveur. */
  ressource: string
  /** Un nom de `NatureAcces`, côté serveur. */
  nature: string
  /** La ligne visée ; sur une liste de candidats, la vacation ou le remplacement. */
  cibleId: string | null
  lignes: number
  /** Un instant UTC. */
  horodatage: string
}
