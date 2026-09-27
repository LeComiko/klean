// ⚠️ Extension explicite, et elle est nécessaire : ce module est chargé par
// `node --test` via sections.test.ts, et Node n'applique pas la résolution sans
// extension de Vite. Voir le piège des extensions d'import en CLAUDE.md §10.
import { Roles, RolesInternes } from '../types/auth.ts'

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

/**
 * ⚠️ L'ordre compte : la **première** section ouverte à un profil est sa
 * destination d'arrivée après connexion (voir `Accueil` dans `App.tsx`).
 *
 * `TB` — tableau de bord — a été retiré. Il accueillait tout le monde mais menait
 * à « en chantier », et aucune étape ne prévoyait de l'écrire : le planning *est*
 * le tableau de bord de ce produit.
 */
export const Sections: readonly Section[] = [
  // `GET /api/planning` est réservé aux trois rôles internes.
  { chemin: '/planning', pastille: 'PL', libelle: 'Planning', roles: RolesInternes },
  // Le syndic lit les sites, cadré sur son propre client par le deuxième axe
  // d'isolation.
  { chemin: '/sites', pastille: 'SI', libelle: 'Sites', roles: InternesEtClientSyndic },
  { chemin: '/agents', pastille: 'AG', libelle: 'Agents', roles: RolesInternes },
  { chemin: '/stocks', pastille: 'ST', libelle: 'Stocks', roles: RolesInternes },
]

/**
 * La première section ouverte à ces rôles, ou `undefined` si aucune.
 *
 * C'est la destination d'arrivée après connexion. Extraite ici plutôt que laissée
 * dans `App.tsx` pour être éprouvable : une mauvaise destination, c'est un
 * utilisateur qui se connecte et tombe sur « Accès refusé » — un défaut visible
 * dès la première seconde d'usage, et invisible en relisant le code.
 */
export function premiereSectionOuverte(roles: readonly string[]): Section | undefined {
  return Sections.find(
    (section) => section.roles === undefined || section.roles.some((role) => roles.includes(role)),
  )
}
