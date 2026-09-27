import { Roles, RolesInternes } from '../types/auth'

/**
 * Les sections de la barre latérale, source unique pour la navigation **et**
 * pour les routes d'`App.tsx`.
 *
 * Deux listes séparées finiraient par diverger, et la divergence a une forme
 * précise : une pastille visible dont la route n'est pas gardée, ou l'inverse.
 *
 * ⚠️ `roles` reflète la matrice des droits de `SPEC.md` étape 8, mais ne
 * l'applique pas : l'autorité est le serveur, qui répond 403. Ce champ évite
 * d'afficher une porte fermée, il n'en ferme aucune.
 */
export interface Section {
  chemin: string
  /** Deux lettres, le format de la pastille de 40 px. */
  pastille: string
  libelle: string
  /** Absent : tout compte connecté. */
  roles?: readonly string[]
}

const InternesEtClientSyndic: readonly string[] = [...RolesInternes, Roles.ClientSyndic]

export const Sections: readonly Section[] = [
  // `GET /api/planning` est réservé aux trois rôles internes.
  { chemin: '/planning', pastille: 'PL', libelle: 'Planning', roles: RolesInternes },
  { chemin: '/tableau-de-bord', pastille: 'TB', libelle: 'Tableau de bord' },
  // Le syndic lit les sites, cadré sur son propre client par le deuxième axe
  // d'isolation.
  { chemin: '/sites', pastille: 'SI', libelle: 'Sites', roles: InternesEtClientSyndic },
  { chemin: '/agents', pastille: 'AG', libelle: 'Agents', roles: RolesInternes },
  { chemin: '/stocks', pastille: 'ST', libelle: 'Stocks', roles: RolesInternes },
]
