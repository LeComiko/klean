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

/** Genre grammatical, pour accorder « créé/créée » et « le/la ». */
export type Genre = 'masculin' | 'feminin'

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

  /**
   * Condition de **disparition** du champ : il n'est ni affiché ni envoyé quand le
   * champ désigné porte l'une de ces valeurs.
   *
   * ⚠️ Une seule raison d'exister aujourd'hui, et elle n'est pas cosmétique : le
   * motif d'une absence `Maladie` est une donnée de santé au sens de l'article 9 du
   * RGPD. Le serveur refuse déjà en 400 — « un arrêt maladie ne peut pas porter de
   * motif » — mais un formulaire qui laisse saisir ce que l'API rejette invite à
   * écrire un diagnostic, et le recueille dans le navigateur avant de le refuser.
   * L'interface empêche, l'API fait barrage. L'étape 17 ajoutera
   * `AccidentTravail` à la même liste.
   *
   * ⚠️ Masquer n'est pas cacher la valeur : `corpsDe` envoie `null` sur un champ
   * masqué, sinon un motif saisi *avant* de choisir « Maladie » partirait quand
   * même — et le refus du serveur nommerait un champ que l'écran n'affiche plus.
   */
  masquerSi?: {
    /** Champ de la même description dont la valeur décide. */
    champ: string
    /** Valeurs qui font disparaître ce champ. */
    vaut: readonly string[]
  }
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
 * Sert deux choses, et les deux sont en place depuis le lot B : il alimente la liste
 * déroulante du champ de référence, et il porte la **descente de niveau** — « les
 * sites de ce client ». Le nom du champ est celui du paramètre de requête que le
 * serveur accepte : `/sites?clientId=…` côté écran, `?clientId=` côté API. Les deux
 * ne peuvent pas diverger parce qu'il n'y en a qu'un.
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
  genre: Genre

  sousTitre: string

  /**
   * Champ portant le libellé d'une ligne dans un message ou une confirmation.
   *
   * ⚠️ Il sert **deux fois**, et la seconde contraint le choix : c'est aussi lui
   * qui libelle les options de la liste déroulante d'une ressource **enfant**. Une
   * étiquette de type `reference` afficherait donc des identifiants dans ce choix —
   * sur l'écran des prestations, la liste des contrats n'a pas chargé les sites, et
   * n'a aucun moyen de résoudre un `siteId` en nom. D'où `frequence` pour un contrat
   * et `type` pour une absence, qui sont imparfaits mais lisibles. Une invariante de
   * `descriptions.test.ts` l'empêche de redevenir une référence.
   */
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
}

/*
 * ⚠️ Il y a eu ici un champ `cascade`, qui décrivait en toutes lettres ce que la
 * suppression d'une ligne emportait — « ses sites, leurs contrats, leurs
 * prestations et toutes les interventions qui en découlent ». Il était juste au
 * moment où il a été écrit : toute la chaîne du modèle était en
 * `DeleteBehavior.Cascade`, et un `DELETE` rendait 204.
 *
 * La migration `SuppressionRestreinteSurLaChaineExploitation` a supprimé le
 * problème plutôt que l'avertissement : une suppression qui a des enfants est
 * désormais **refusée** en 409, avec un message du serveur qui nomme ce qui bloque
 * et par où sortir. Le champ est donc retiré — laisser une confirmation annoncer une
 * cascade qui n'a plus lieu serait pire que de ne rien annoncer.
 *
 * Ne pas le réintroduire pour « prévenir d'un refus possible » : l'écran ne peut pas
 * savoir si une ligne a des enfants, aucun endpoint de liste n'acceptant de filtre.
 * Un avertissement affiché sur toutes les lignes est du bruit, et le message du 409
 * arrive au bon moment avec le bon compte.
 */
