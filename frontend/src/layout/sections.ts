// ⚠️ Extension explicite, et elle est nécessaire : ce module est chargé par
// `node --test` via sections.test.ts, et Node n'applique pas la résolution sans
// extension de Vite. Voir le piège des extensions d'import en CLAUDE.md §10.
import { descriptionsLisibles } from '../reference/descriptions.ts'
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

  /**
   * Écrans écrits à la main, rangés **après** les ressources dans la sous-navigation.
   *
   * Le cas qui les a fait naître : le stock et la prévision de l'étape 22, qui ne sont
   * pas des listes à formulaire — une quantité ne se saisit pas, elle résulte d'un
   * mouvement. Chacun porte ses rôles, que `roles` de la section doit couvrir.
   */
  ecrans?: readonly EcranSection[]
}

/** Un écran propre d'une section : son chemin, son onglet, ses lecteurs. */
export interface EcranSection {
  chemin: string
  libelle: string
  roles: readonly string[]
}

/** Un onglet de sous-navigation, ressource décrite ou écran propre. */
export interface Onglet {
  chemin: string
  libelle: string
}

/** Lecture et écriture du stock pour l'exploitation : `RolesKlean.Exploitation`. */
const Exploitation: readonly string[] = [Roles.Administrateur, Roles.Responsable]

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

  // Ce que les agents ont constaté sur place (SPEC.md, étape 25). Juste après le
  // planning : c'est de l'exploitation du jour, pas de la donnée de référence. Le chef
  // d'équipe y lit celles de son équipe, par le filtre global des vacations.
  { chemin: '/anomalies', pastille: 'AN', libelle: 'Anomalies', roles: RolesInternes },

  // La chaîne client, entière : Clients → Sites → Contrats → Prestations. Le syndic
  // y lit son propre client et ses sites, cadré par le deuxième axe d'isolation —
  // les contrats et les prestations lui sont fermés, et les rôles de la section
  // valent l'union des lectures de ses quatre ressources.
  //
  // La pastille dit la première ressource, celle où l'on arrive : `CL`. Elle a valu
  // `SI` jusqu'à l'étape 22, où les libellés ont été revus d'un seul geste.
  {
    chemin: '/clients',
    pastille: 'CL',
    libelle: 'Clients et sites',
    roles: InternesEtClientSyndic,
    // Le catalogue clôt la chaîne : il ne descend d'aucune ressource, il sert les
    // prestations (SPEC.md, étape 21).
    //
    // La checklist d'un type le suit (étape 25) : ce qu'un passage de ce type vérifie.
    ressources: ['clients', 'sites', 'contrats', 'prestations', 'typesPrestation', 'pointsControle'],
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

  // Le stock, étape 22 : ce qui se stocke, où, ce que chaque passage consomme, puis
  // les quantités et le besoin. L'ordre est celui de la mise en route — on ne compte
  // pas un stock avant d'avoir un produit et un lieu.
  //
  // ⚠️ Le chemin reste `/produits` : celui d'une section est celui de sa première
  // ressource, faute de quoi la destination d'arrivée pointerait vers une route
  // qu'`App.tsx` ne monte pas.
  {
    chemin: '/produits',
    pastille: 'ST',
    libelle: 'Stock',
    roles: RolesInternes,
    ressources: ['produits', 'emplacements', 'consommationsType'],
    ecrans: [
      { chemin: '/stock', libelle: 'Stock', roles: RolesInternes },
      // ⚠️ L'exploitation seule : les vacations d'un chef sont filtrées sur son équipe,
      // le stock non, et sa prévision serait fausse (SPEC.md, étape 22, lot B).
      { chemin: '/prevision', libelle: 'Prévision', roles: Exploitation },
    ],
  },

  // L'entreprise elle-même : son identité et son territoire. Dernière, parce qu'on y
  // vient rarement — une fois à la mise en route, puis presque jamais.
  {
    chemin: '/entreprise',
    pastille: 'EN',
    libelle: 'Entreprise',
    roles: RolesInternes,
    ressources: ['entreprises'],
    // Les comptes (SPEC.md, étape 24) : qui a accès, administrateur seul. Rangés ici
    // plutôt qu'en pastille : on y vient à l'arrivée d'une personne et à son départ.
    ecrans: [{ chemin: '/comptes', libelle: 'Comptes', roles: [Roles.Administrateur] }],
  },

  // La journée de l'agent, sur son téléphone (SPEC.md, étape 25) : la seule section qui
  // lui soit ouverte, donc sa destination d'arrivée. Jusqu'à l'étape 25, ce rôle n'en
  // avait aucune et tombait sur un refus en se connectant.
  //
  // ⚠️ Dernière, et ce n'est pas un rang d'importance : la première section ouverte est
  // l'arrivée, et un compte qui porterait aussi un rôle interne doit arriver sur le
  // planning. Pour l'agent seul, le rang ne change rien.
  { chemin: '/ma-journee', pastille: 'MJ', libelle: 'Ma journée', roles: [Roles.Agent] },
]

/**
 * Les onglets de la sous-navigation d'une section, pour ces rôles : ses ressources
 * lisibles, dans l'ordre de la chaîne, puis ses écrans ouverts.
 *
 * ⚠️ Filtrés par rôle un à un, comme les liens de descente : un chef d'équipe ne voit
 * pas l'onglet de la prévision, qui lui répondrait 403.
 */
export function ongletsDe(section: Section, roles: readonly string[]): readonly Onglet[] {
  return [
    ...descriptionsLisibles(section.ressources ?? [], roles),
    ...(section.ecrans ?? []).filter((ecran) => ecran.roles.some((role) => roles.includes(role))),
  ]
}

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
