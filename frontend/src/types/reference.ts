/**
 * Types de la description d'une ressource de référence.
 *
 * Six écrans écrits à la main divergeraient, et la divergence prend une forme
 * précise : un bouton d'enregistrement qui répond 403. Une description par
 * ressource, un seul motif qui les rend toutes.
 *
 * ⚠️ Ce fichier ne dépend de rien : il est chargé par `node --test` à travers
 * `descriptions.ts`, et chaque import qu'il ajouterait devrait porter son
 * extension explicite (CLAUDE.md §10).
 */

/**
 * Nature d'un champ. Elle décide du contrôle affiché **et** de la façon dont la
 * valeur est convertie avant l'envoi — les deux vont ensemble, et les séparer
 * ferait qu'un champ « nombre » rendu par un `<input type="number">` partirait
 * quand même en chaîne.
 */
export type TypeChamp =
  | 'texte'
  | 'texteLong'
  | 'courriel'
  | 'telephone'
  | 'nombre'
  | 'date'
  | 'choix'
  | 'booleen'
  /** Clé étrangère : rendue par une liste déroulante alimentée par l'API. */
  | 'reference'

export interface ChampRessource {
  /** Nom de la propriété dans le DTO, en camelCase comme le JSON du serveur. */
  nom: string
  libelle: string
  type: TypeChamp

  /**
   * Requis côté serveur. Reproduit `[Required]`, ne le remplace pas : l'écran
   * évite un aller-retour, l'API refuse.
   */
  requis?: boolean

  /**
   * Valeur de départ à la création.
   *
   * ⚠️ Nécessaire pour `actif` : un agent créé décoché n'apparaît dans aucun
   * classement de remplaçants, et rien à l'écran ne dirait pourquoi. Le DTO du
   * serveur porte le même défaut ; le formulaire ne doit pas le contredire en
   * envoyant `false` explicitement.
   */
  defaut?: string | boolean

  /** Pour `choix` : les valeurs exactes que l'énumération du serveur accepte. */
  choix?: readonly string[]

  /** Pour `reference` : la clé de la description visée. */
  reference?: string

  /** Texte d'aide sous le champ. Expliquer, pas décorer. */
  aide?: string

  /** Pour `nombre` : bornes et pas, reproduisant le `[Range]` du DTO. */
  min?: number
  max?: number
  pas?: number

  /**
   * Longueur maximale, reproduisant le `[MaxLength]` du DTO.
   *
   * ⚠️ Ce n'est pas une redondance de confort. La validation de modèle d'ASP.NET
   * s'exécute **avant** le contrôleur et répond **en anglais** — « The field Nom
   * must be a string or array type with a maximum length of '200' » — un texte que
   * rien côté écran ne peut traduire, puisqu'il traverse l'API tel quel. C'est le
   * même piège que les DTOs de mot de passe (CLAUDE.md §6). L'attribut `maxLength`
   * du navigateur empêche la saisie, donc rend ce message inatteignable, et il
   * refuse dans la langue du navigateur.
   */
  longueurMax?: number
}

export interface ColonneRessource {
  /** Nom de la propriété du DTO, ou `nom` composé rendu par la page. */
  nom: string
  libelle: string

  /**
   * Chiffre comparé en colonne : rendu en chasse fixe, aligné à droite
   * (CLAUDE.md §12). Coordonnées, quantités, durées.
   */
  chiffres?: boolean

  /** Rendu par la valeur d'une autre ressource, résolue par la page. */
  reference?: string
}

/**
 * Lien de parenté dans la chaîne du modèle.
 *
 * Sert deux choses, dont une seule est en place : il alimente la liste déroulante
 * du champ de référence, et il portera la descente de niveau — « les sites de ce
 * client » — le jour où `GET /api/sites` acceptera un `?clientId=`. Aucun endpoint
 * de liste n'accepte de filtre aujourd'hui.
 */
export interface ParenteRessource {
  /** Clé de la description parente. */
  ressource: string
  /** Champ de cette ressource qui porte la clé étrangère. */
  champ: string
}

export interface DescriptionRessource {
  /** Identifiant interne, et clé de parenté. */
  cle: string

  /** Route de l'écran, montée par `App.tsx`. */
  chemin: string

  /** Route de l'API, sans identifiant ni chaîne de requête. */
  route: string

  libelle: string

  /**
   * Au singulier et sans article : sert à composer « Nouveau client », « Modifier
   * le client », « Aucun client ». Écrit à la main parce qu'aucune règle ne
   * dérive un singulier français d'un pluriel de façon fiable.
   */
  singulier: string

  /** Genre grammatical, pour accorder ces mêmes phrases. */
  genre: 'masculin' | 'feminin'

  sousTitre: string

  /** Colonne portant le libellé d'une ligne dans un message ou une confirmation. */
  etiquette: string

  colonnes: readonly ColonneRessource[]
  champs: readonly ChampRessource[]

  /**
   * Rôles autorisés en lecture et en écriture, reflet de la matrice des droits de
   * `SPEC.md` étape 8.
   *
   * ⚠️ Ces listes n'appliquent rien : l'autorité est le serveur, qui répond 403.
   * Elles évitent d'afficher une porte fermée — et pour l'écriture, c'est la
   * décision de l'étape : présenter un formulaire en lecture seule plutôt qu'un
   * bouton qui échoue. Afficher un bouton qui répond 403 est une faute
   * d'interface, pas une protection.
   */
  rolesLecture: readonly string[]
  rolesEcriture: readonly string[]

  parent?: ParenteRessource

  /**
   * Ce que la suppression d'une ligne emporte avec elle, en toutes lettres.
   *
   * ⚠️ Mesuré, pas supposé : toute la chaîne du modèle est en
   * `DeleteBehavior.Cascade` (`Data/KleanDbContext.cs`). Supprimer un client rend
   * **204** et efface au passage ses sites, leurs contrats, leurs prestations et
   * toutes les interventions qui en découlent — vérifié contre le serveur : un
   * client, un site, un contrat, une prestation et une intervention disparus en un
   * appel.
   *
   * Jusqu'à cette étape ce chemin n'existait que dans Swagger. En faire un bouton
   * de liste sans le dire serait la faute d'interface la plus coûteuse du produit :
   * l'historique d'exploitation d'un client n'est pas restaurable.
   */
  cascade?: string
}
