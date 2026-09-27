/**
 * Contrat de session, miroir exact de `backend/DTOs/AuthDto.cs`.
 *
 * ⚠️ Aucun jeton ici, et il ne doit jamais en apparaître. Les trois cookies de
 * session sont posés par le serveur ; deux sont `HttpOnly`, le JavaScript n'y
 * accède pas. Si un champ « jeton » apparaissait dans ce fichier, c'est que le
 * backend aurait régressé.
 */

/** Réponse de `POST /api/auth/connexion` et de `POST /api/auth/rafraichir`. */
export interface Session {
  /**
   * Expiration du jeton d'accès, en ISO 8601 UTC (suffixe `Z`).
   * Sert à rafraîchir *avant* d'échouer, plutôt qu'après un 401.
   */
  expiration: string
  profil: Profil
}

/** Réponse de `GET /api/auth/moi`. */
export interface Profil {
  id: string
  email: string
  nom: string
  prenom: string
  entrepriseId: string
  /** Renseigné pour un compte de rôle `Agent`, sinon `null`. */
  agentId: string | null
  /** Renseigné pour un compte de rôle `ClientSyndic`, sinon `null`. */
  clientId: string | null
  actif: boolean
  roles: string[]
}

/**
 * Les cinq rôles, miroir de `backend/Models/RolesKlean.cs`.
 *
 * Objet constant et non `enum` : le projet est en `erasableSyntaxOnly`, et un
 * `enum` TypeScript émet du code à l'exécution.
 */
export const Roles = {
  Administrateur: 'Administrateur',
  Responsable: 'Responsable',
  ChefEquipe: 'ChefEquipe',
  Agent: 'Agent',
  ClientSyndic: 'ClientSyndic',
} as const

/** Les trois rôles internes : lecture sur toute l'entreprise. */
export const RolesInternes: string[] = [
  Roles.Administrateur,
  Roles.Responsable,
  Roles.ChefEquipe,
]

/** Vrai si le profil porte au moins un des rôles demandés. */
export function aUnRole(profil: Profil, roles: readonly string[]): boolean {
  return roles.some((role) => profil.roles.includes(role))
}
