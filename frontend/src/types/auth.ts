/**
 * Contrat de session, miroir exact de `backend/DTOs/AuthDto.cs`.
 *
 * ⚠️ Aucun jeton ici, et il ne doit jamais en apparaître. Les trois cookies de
 * session sont posés par le serveur ; deux sont `HttpOnly`, le JavaScript n'y
 * accède pas. Si un champ « jeton » apparaissait dans ce fichier, c'est que le
 * backend aurait régressé.
 */

/**
 * Réponse de `POST /api/auth/rafraichir`, de `POST /api/auth/connexion/code`, et de
 * `POST /api/auth/connexion` quand le mot de passe suffit (200).
 */
export interface Session {
  /**
   * Expiration du jeton d'accès, en ISO 8601 UTC (suffixe `Z`).
   * Sert à rafraîchir *avant* d'échouer, plutôt qu'après un 401.
   */
  expiration: string
  profil: Profil
}

/**
 * L'étape que rend `POST /api/auth/connexion` en **202**, quand le mot de passe ne
 * suffit pas (SPEC.md, étape 23) : saisir un code, ou équiper d'abord un téléphone.
 * Miroir de `backend/DTOs/SecondFacteurDto.cs`, comme les quatre types qui suivent.
 *
 * `ChangementMotDePasse` (étape 24) passe avant les deux autres : le mot de passe est
 * le provisoire que l'administrateur a transmis, et la personne en choisit un.
 */
export type EtapeConnexion = 'SecondFacteur' | 'Equipement' | 'ChangementMotDePasse'

export interface ReponseEtape {
  etape: EtapeConnexion
}

/** Un code de l'application, ou un code de récupération : l'un des deux, jamais les deux. */
export type SaisieSecondFacteur = { code: string } | { codeRecuperation: string }

/** De quoi équiper un téléphone. La clé n'est montrée qu'à cet instant. */
export interface PreparationSecondFacteur {
  /** En Base32, par groupes de quatre. */
  cle: string
  uri: string
  /** Lignes de modules, `true` pour un module sombre, sans la marge blanche. */
  modules: boolean[][]
}

/** Les codes de récupération, montrés une seule fois, et la session rouverte s'il y a lieu. */
export interface CodesRecuperation {
  codes: string[]
  session: Session | null
}

export interface EtatSecondFacteur {
  actif: boolean
  /** Le rôle l'impose : « Désactiver » devient alors « Changer de téléphone ». */
  obligatoire: boolean
  codesRestants: number
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
