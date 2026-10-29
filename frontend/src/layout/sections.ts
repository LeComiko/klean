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

  /**
   * Clés de `reference/descriptions.ts`, **dans l'ordre de la chaîne du modèle**.
   *
   * Une section en porte plusieurs plutôt que d'aligner huit pastilles dans une
   * barre de 64 px : l'ordre dit la dépendance — un site suppose un client — et
   * c'est ce que la sous-navigation affiche. `chemin` est celui de la première.
   *
   * ⚠️ `roles` doit valoir l'union des `rolesLecture` de ces ressources, sinon la
   * section garde une porte que son contenu n'a pas, ou l'inverse. C'est une
   * invariante éprouvée par `sections.test.ts`, pas une convention.
   */
  ressources?: readonly string[]
}

/**
 * Le seul écran qui n'est **pas** une section, et le seul chemin monté par
 * `App.tsx` que `Sections` ne produit pas.
 *
 * Il est déclaré ici quand même, pour que ce fichier reste la source unique des
 * chemins : deux listes finiraient par se recouvrir, et une section qui
 * revendiquerait `/compte` donnerait deux `<Route>` sur le même chemin — la
 * seconde inatteignable, sans aucune erreur. `sections.test.ts` garde l'absence.
 *
 * Pourquoi dehors : une section est une **fonction du produit**, ouverte par rôle,
 * et `premiereSectionOuverte` en fait une destination d'arrivée. Le compte n'est ni
 * l'un ni l'autre — il est ouvert à tout compte connecté, et personne ne doit
 * atterrir dessus en se connectant. Sa place dans la navigation est le pied de la
 * barre, auprès de la déconnexion, et non la liste des pastilles.
 */
export const CheminCompte = '/compte'

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

  // La chaîne client, entière : Clients → Sites → Contrats → Prestations. Le syndic
  // y lit son propre client et ses sites, cadré par le deuxième axe d'isolation —
  // les contrats et les prestations lui sont fermés, et les rôles de la section
  // valent l'union des lectures de ses quatre ressources.
  //
  // ⚠️ La pastille reste `SI` alors que la section commence par Clients. Les
  // libellés de pastilles seront revus à l'étape des Stocks : les renommer
  // maintenant, pour les renommer encore à l'étape suivante, ferait deux fois le
  // travail et deux fois la désorientation.
  {
    chemin: '/clients',
    pastille: 'SI',
    libelle: 'Clients et sites',
    roles: InternesEtClientSyndic,
    // Le catalogue clôt la chaîne : il ne descend d'aucune ressource, il sert les
    // prestations (SPEC.md, étape 21).
    ressources: ['clients', 'sites', 'contrats', 'prestations', 'typesPrestation'],
  },

  // Agents → Absences. C'est l'ajout qui rend le panneau de remplacement de
  // l'étape 15 utilisable sans script : jusqu'ici, aucune absence ne pouvait être
  // saisie par l'interface.
  {
    chemin: '/agents',
    pastille: 'AG',
    libelle: 'Agents',
    roles: RolesInternes,
    ressources: ['agents', 'absences'],
  },

  // ⚠️ `ST` mène désormais à Produits, et le chemin a donc changé de `/stocks` à
  // `/produits` : l'invariante veut que le chemin d'une section soit celui de sa
  // première ressource, faute de quoi la destination d'arrivée pointerait vers une
  // route qu'`App.tsx` ne monte pas. L'écran « en chantier » des stocks disparaît
  // avec lui, et c'est préférable à un écran qui ne promet rien : les Stocks sont
  // une des quatre fonctions du cœur de produit et ont leur propre étape, où ils
  // rejoindront cette section en deuxième ressource.
  {
    chemin: '/produits',
    pastille: 'ST',
    libelle: 'Produits et stocks',
    roles: RolesInternes,
    ressources: ['produits'],
  },
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
