// ⚠️ Extensions explicites : ce module est chargé par `node --test` via
// comptes.test.ts (CLAUDE.md §10).
import { Roles } from '../types/auth.ts'
import type { Compte, CreationCompte } from '../types/comptes.ts'

/**
 * Ce que l'écran des comptes décide sans le serveur (SPEC.md, étape 24).
 *
 * Extrait du composant pour la raison de `reference/valeurs.ts` (CLAUDE.md §11) :
 * c'est la partie qui écrirait une donnée fausse au lieu d'afficher quelque chose de
 * faux — un compte Agent sans fiche, ou une fiche agent envoyée pour un client
 * syndic après un changement de rôle.
 */

export const LibellesRole: Readonly<Record<string, string>> = {
  [Roles.Administrateur]: 'Administrateur',
  [Roles.Responsable]: 'Responsable',
  [Roles.ChefEquipe]: 'Chef d’équipe',
  [Roles.Agent]: 'Agent',
  [Roles.ClientSyndic]: 'Client syndic',
}

/** Les rôles, dans l'ordre où les propose la création : du plus large au plus étroit. */
export const RolesProposes: readonly string[] = [
  Roles.Administrateur,
  Roles.Responsable,
  Roles.ChefEquipe,
  Roles.Agent,
  Roles.ClientSyndic,
]

/** Le libellé d'un rôle ; un rôle que l'écran ne connaît pas s'affiche tel quel. */
export function libelleRole(role: string): string {
  return LibellesRole[role] ?? (role === '' ? 'Aucun rôle' : role)
}

export type EtatCompte = 'Actif' | 'PremiereConnexion' | 'Ferme' | 'Pseudonymise'

/**
 * L'état d'une ligne, un seul : la pseudonymisation l'emporte sur la fermeture — un
 * compte pseudonymisé est aussi inactif —, et la fermeture sur la première connexion
 * en attente.
 */
export function etatDe(compte: Compte): EtatCompte {
  if (compte.dateAnonymisation !== null) return 'Pseudonymise'
  if (!compte.actif) return 'Ferme'
  if (compte.motDePasseProvisoire) return 'PremiereConnexion'

  return 'Actif'
}

export const LibellesEtat: Readonly<Record<EtatCompte, string>> = {
  Actif: 'Actif',
  PremiereConnexion: 'Jamais connecté',
  Ferme: 'Fermé',
  Pseudonymise: 'Pseudonymisé',
}

export type Geste = 'Fermer' | 'Rouvrir' | 'Reinitialiser' | 'Pseudonymiser'

/**
 * Les gestes qu'une ligne propose, dans l'ordre d'affichage.
 *
 * - **Aucun sur son propre compte** : le serveur refuse de fermer ou de réinitialiser
 *   le sien, et de s'effacer soi-même. Proposer un bouton qui répond 409 est la faute
 *   d'interface que les descriptions de référence existent pour éviter.
 * - **Aucun sur un compte pseudonymisé** : son identité n'existe plus.
 * - **Pas de pseudonymisation d'un compte rattaché à une fiche agent** : c'est la route
 *   de l'agent qui efface, parce qu'elle seule traite les trois endroits où la personne
 *   est nommée (CLAUDE.md §7 bis). La ligne le dit au lieu d'offrir un 409.
 */
export function gestesDe(compte: Compte, appelantId: string): Geste[] {
  if (compte.id === appelantId || etatDe(compte) === 'Pseudonymise') return []

  const gestes: Geste[] = compte.actif ? ['Fermer'] : ['Rouvrir']

  if (compte.secondFacteurActif) gestes.push('Reinitialiser')
  if (compte.agentId === null) gestes.push('Pseudonymiser')

  return gestes
}

export const LibellesGeste: Readonly<Record<Geste, string>> = {
  Fermer: 'Fermer',
  Rouvrir: 'Rouvrir',
  Reinitialiser: 'Réinitialiser la double authentification',
  Pseudonymiser: 'Pseudonymiser',
}

/**
 * La question qui précède un geste, ou `null` s'il part sans confirmation. Elle nomme
 * la personne et dit l'effet : une confirmation qui ne dit pas ce qu'elle fait ne
 * confirme rien. Rouvrir rend un accès qui existait, sans rien couper.
 */
export function questionDe(geste: Geste, nom: string): string | null {
  switch (geste) {
    case 'Fermer':
      return `Fermer le compte de ${nom} ? Ses sessions tombent tout de suite ; le compte pourra être rouvert.`
    case 'Reinitialiser':
      return `Réinitialiser la double authentification de ${nom} ? Ses sessions tombent, et sa prochaine connexion équipera un nouveau téléphone.`
    case 'Pseudonymiser':
      return `Pseudonymiser ${nom} ? Irréversible : le nom, le prénom et l’adresse sont effacés, et le compte est fermé pour toujours.`
    case 'Rouvrir':
      return null
  }
}

/** Le nom d'une ligne : prénom et nom, ou l'adresse si les deux sont vides. */
export function nomDe(compte: Compte): string {
  const nom = `${compte.prenom} ${compte.nom}`.trim()

  return nom === '' ? compte.email : nom
}

/** Ce que saisit le formulaire de création, en chaînes, comme tout champ. */
export interface SaisieCompte {
  email: string
  nom: string
  prenom: string
  role: string
  agentId: string
  clientId: string
}

export const SaisieVide: SaisieCompte = { email: '', nom: '', prenom: '', role: '', agentId: '', clientId: '' }

/** Vrai si le rôle propose de rattacher une fiche agent : exigée pour `Agent`. */
export function proposeFicheAgent(role: string): boolean {
  return role !== '' && role !== Roles.ClientSyndic
}

/**
 * Le corps à envoyer, ou le refus à afficher.
 *
 * Les refus locaux sont ceux que le serveur prononcerait **en anglais**, la validation
 * de modèle d'ASP.NET passant avant le contrôleur — l'adresse et le nom vides, l'adresse
 * mal formée —, et ceux qu'il dirait en termes d'identifiants (« un compte de rôle
 * Agent doit porter un AgentId »).
 *
 * ⚠️ **Un champ masqué part à `null`**, et c'est la seconde moitié du masquage
 * (CLAUDE.md §10) : choisir une fiche agent, puis passer le rôle à « Client syndic »,
 * enverrait sinon cette fiche avec un compte qui ne doit pas en porter.
 *
 * Une fiche agent reste **facultative** pour les rôles internes : un administrateur
 * qui fait aussi le ménage porte la sienne, et c'est le cas normal d'une entreprise de
 * cinq personnes (CLAUDE.md §7 bis).
 */
export function corpsDeCreation(saisie: SaisieCompte): { corps: CreationCompte } | { refus: string } {
  const email = saisie.email.trim()
  const nom = saisie.nom.trim()

  if (email === '') return { refus: 'L’adresse électronique est obligatoire.' }
  if (!/^[^@\s]+@[^@\s]+$/.test(email)) return { refus: 'Cette adresse électronique n’est pas valide.' }
  if (nom === '') return { refus: 'Le nom est obligatoire.' }
  if (!RolesProposes.includes(saisie.role)) return { refus: 'Choisir un rôle.' }

  if (saisie.role === Roles.Agent && saisie.agentId === '') {
    return { refus: 'Un compte Agent se rattache à sa fiche : la choisir.' }
  }

  if (saisie.role === Roles.ClientSyndic && saisie.clientId === '') {
    return { refus: 'Un compte de client syndic se rattache à un client : le choisir.' }
  }

  return {
    corps: {
      email,
      nom,
      prenom: saisie.prenom.trim(),
      role: saisie.role,
      agentId: proposeFicheAgent(saisie.role) && saisie.agentId !== '' ? saisie.agentId : null,
      clientId: saisie.role === Roles.ClientSyndic ? saisie.clientId : null,
    },
  }
}

/** Les comptes d'un rôle, ou tous pour un filtre vide. L'ordre du serveur est gardé. */
export function filtrerParRole(comptes: readonly Compte[], role: string): Compte[] {
  return role === '' ? [...comptes] : comptes.filter((compte) => compte.role === role)
}
