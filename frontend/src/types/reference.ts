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
  /**
   * Heure de la journée. Saisie `hh:mm`, envoyée `hh:mm:ss` : la sérialisation d'un
   * `TimeSpan` par le serveur, qui ne lit pas la forme courte.
   */
  | 'heure'
  /**
   * Jours de la semaine, en cases à cocher. Envoyés en **tableau** de noms, du lundi
   * au dimanche — `JoursDeLaSemaine.TryLire` côté serveur, qui refuse toute autre
   * forme. Les valeurs possibles sont dans `choix`.
   */
  | 'jours'
  /**
   * Un compte de connexion, choisi dans une liste déroulante alimentée par la route
   * `comptes`. Les comptes n'ont pas d'écran de référence, donc pas de description à
   * viser : c'est la seule différence avec `reference`.
   */
  | 'compte'

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

  /**
   * Pour `choix` et `jours` : les valeurs exactes que le serveur accepte. Pour `jours`,
   * leur ordre est celui de l'envoi.
   */
  choix?: readonly string[]

  /**
   * Pour `choix` : le mot affiché pour chaque valeur, dans la liste comme dans le tableau.
   * La valeur envoyée reste le nom exact de l'énumération du serveur, qui refuse tout
   * autre texte ; « Congé » s'affiche, `Conge` part.
   *
   * ⚠️ Une clé par valeur de `choix`, ni plus ni moins : une valeur sans libellé
   * s'afficherait en nom brut, sans erreur. `descriptions.test.ts` le garde.
   */
  libelles?: Readonly<Record<string, string>>

  /** Pour `reference` : la clé de la description visée. */
  reference?: string

  /**
   * Pour `compte` : la route qui rend la liste des comptes possibles, en tableau de
   * `{ id, nom, prenom, actif }`.
   *
   * ⚠️ Chargée seulement quand le formulaire est éditable. Les routes de comptes sont
   * réservées à qui écrit, et un lecteur n'en a pas l'usage : le charger pour lui
   * rendrait un 403 à chaque ouverture d'écran.
   */
  comptes?: string

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
   * Pour `reference` : les champs de **ce** formulaire que le choix d'une ligne
   * remplit, en `{ champCible: proprieteDeLaLigneChoisie }`.
   *
   * Le cas qui l'a fait naître : choisir « Hall d'entrée » dans le catalogue remplit le
   * libellé et la durée de la prestation (SPEC.md, étape 21). Un champ n'est rempli
   * que s'il est **vide ou encore à la valeur du choix précédent** — voir `preremplir`
   * dans `valeurs.ts` : un libellé propre au site n'est jamais écrasé.
   */
  preremplit?: Readonly<Record<string, string>>

  /**
   * Pour `reference` : ne proposer que les lignes dont `actif` n'est pas `false`.
   *
   * ⚠️ Sauf la valeur **déjà enregistrée**, gardée et dite « (désactivé) ». La
   * retirer aussi afficherait « — choisir — » sur une prestation qui porte un type :
   * l'écran mentirait, et le serveur, lui, garde le lien.
   */
  seulementActifs?: boolean

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

  /**
   * Rendu par le nom d'un compte, lu sur cette route — celle du champ `compte` du même
   * nom. Les comptes n'ont pas de description, d'où cette propriété à côté de `reference`.
   */
  comptes?: string

  /**
   * Rôles qui voient la colonne ; absent, tous les lecteurs.
   *
   * ⚠️ Une colonne de `comptes` doit y être restreinte à des rôles d'**écriture** : la
   * liste des comptes n'est chargée que pour eux, et un lecteur lirait « Hors de votre
   * périmètre » sur chaque ligne. `descriptions.test.ts` le garde.
   */
  roles?: readonly string[]
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

  /**
   * Route qui remplit les listes déroulantes des ressources **enfants**, si elle
   * diffère de `route`. Elle doit rendre l'`id` et l'`etiquette`, paginée comme `route`.
   *
   * ⚠️ Le cas qui l'a fait naître : choisir le titulaire d'une prestation chargeait
   * `/api/agents`, donc l'adresse et les coordonnées du domicile de chaque agent, pour
   * n'en afficher que le nom (SPEC.md, étape 20). Une ressource qui porte des données
   * personnelles au-delà de son étiquette devrait en déclarer une.
   */
  routeChoix?: string

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

  /** Un geste de liste au-delà du CRUD, offert à qui écrit. */
  action?: ActionRessource
}

/**
 * Un `POST` sans corps sur une route de la ressource, dont la réponse se résume en une
 * phrase. Aujourd'hui l'import de la liste type du catalogue, seul de son espèce.
 *
 * ⚠️ Offert aux rôles `rolesEcriture` de la description, sans liste propre : une
 * action ouverte à d'autres rôles que l'écriture devrait le dire, et ce champ ne le
 * permet pas à dessein.
 */
export interface ActionRessource {
  libelle: string
  /** Libellé du bouton pendant l'appel. */
  enCours: string
  route: string
  /** Phrase ajoutée au message d'une liste vide, qui nomme le geste. */
  suggestion: string
  /** La réponse du serveur, en une phrase lisible. */
  compteRendu: (reponse: unknown) => string
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
