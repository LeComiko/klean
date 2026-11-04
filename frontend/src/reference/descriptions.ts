// ⚠️ Extensions explicites, et elles sont nécessaires : ce module est chargé par
// `node --test` via descriptions.test.ts, et Node n'applique pas la résolution
// sans extension de Vite. Voir le piège des extensions d'import en CLAUDE.md §10.
import { Roles, RolesInternes } from '../types/auth.ts'
import type { DescriptionRessource } from '../types/reference.ts'

/**
 * Les descriptions des ressources de référence, **source unique** de leurs
 * écrans — comme `layout/sections.ts` l'est pour la navigation.
 *
 * Ce qui est décrit ici est ce que `pages/Reference.tsx` rend : libellé, route
 * d'API, colonnes de liste, champs de formulaire, rôles. Aucun écran de référence
 * n'est écrit à la main, et c'est tout l'objet de l'étape : six formulaires
 * copiés-collés auraient six fois l'occasion de mentir sur les droits.
 *
 * ⚠️ Les valeurs de `choix` sont les noms exacts des énumérations C#. Le serveur
 * fait un `Enum.TryParse` et refuse en 400 en nommant les valeurs acceptées : une
 * faute de frappe ici se voit à la première tentative d'enregistrement, pas à la
 * compilation.
 */

const InterneEtClientSyndic: readonly string[] = [...RolesInternes, Roles.ClientSyndic]

/** Seul l'administrateur écrit les données de référence (SPEC.md étape 8). */
const EcritureReference: readonly string[] = [Roles.Administrateur]

/**
 * Écriture des données d'**exploitation**, par l'administrateur et le responsable.
 *
 * ⚠️ Les absences ne sont pas des données de référence, et c'est pourquoi elles ne
 * portent pas `EcritureReference` : un responsable saisit l'absence d'un agent, c'est
 * son travail quotidien, et le serveur l'y autorise — `[Authorize(Roles =
 * RolesKlean.Exploitation)]` sur `AbsencesController`. Leur donner les droits des
 * données de référence présenterait à un responsable un formulaire en lecture seule
 * sur la seule chose que l'étape 15 lui demande de saisir.
 */
const EcritureExploitation: readonly string[] = [Roles.Administrateur, Roles.Responsable]

/** `TypeClient`, miroir de `backend/Models/TypeClient.cs`. */
const TypesDeClient = [
  'EntrepriseCliente',
  'Syndic',
  'GestionnaireImmobilier',
  'Copropriete',
  'Autre',
] as const

/** `TypeAbsence`, miroir de `backend/Models/TypeAbsence.cs`. */
const TypesDAbsence = ['Maladie', 'Conge', 'Indisponibilite', 'Retard', 'Autre'] as const

/** Les mots affichés pour `TypeClient` ; la valeur envoyée reste le nom de l'énumération. */
const LibellesTypeClient: Readonly<Record<(typeof TypesDeClient)[number], string>> = {
  EntrepriseCliente: 'Entreprise cliente',
  Syndic: 'Syndic',
  GestionnaireImmobilier: 'Gestionnaire immobilier',
  Copropriete: 'Copropriété',
  Autre: 'Autre',
}

const LibellesTypeAbsence: Readonly<Record<(typeof TypesDAbsence)[number], string>> = {
  Maladie: 'Maladie',
  Conge: 'Congé',
  Indisponibilite: 'Indisponibilité',
  Retard: 'Retard',
  Autre: 'Autre',
}

/**
 * Les types d'absence dont le motif ne doit **jamais** être recueilli.
 *
 * Un motif d'arrêt maladie en texte libre est une donnée de santé au sens de
 * l'article 9 du RGPD, et le planning n'en fait rien : il lui suffit de savoir qu'un
 * agent est absent. `AbsencesController` refuse déjà en 400 ; cette liste fait
 * disparaître le champ, pour que l'écran n'invite pas à saisir ce que l'API rejette.
 *
 * ⚠️ `AccidentTravail` rejoindra cette liste à l'étape 17, en même temps que
 * l'énumération du serveur. Les deux listes doivent bouger ensemble.
 */
const SansMotif = ['Maladie'] as const

/**
 * Les noms exacts de `JoursSemaine`, du lundi au dimanche. L'ordre est celui de
 * l'envoi : `corpsDe` les range ainsi, quel que soit l'ordre des clics.
 */
export const JoursSemaine = [
  'Lundi', 'Mardi', 'Mercredi', 'Jeudi', 'Vendredi', 'Samedi', 'Dimanche',
] as const

const Clients: DescriptionRessource = {
  cle: 'clients',
  chemin: '/clients',
  route: '/api/clients',
  libelle: 'Clients',
  singulier: 'client',
  genre: 'masculin',
  sousTitre: 'Entreprises, syndics et copropriétés desservis',
  etiquette: 'nom',

  colonnes: [
    { nom: 'nom', libelle: 'Nom' },
    { nom: 'type', libelle: 'Type' },
    { nom: 'ville', libelle: 'Ville' },
    { nom: 'email', libelle: 'Courriel' },
    { nom: 'telephone', libelle: 'Téléphone' },
  ],

  champs: [
    { nom: 'nom', libelle: 'Nom', type: 'texte', requis: true, longueurMax: 200 },
    { nom: 'type', libelle: 'Type', type: 'choix', requis: true, choix: TypesDeClient, libelles: LibellesTypeClient },
    { nom: 'adresse', libelle: 'Adresse', type: 'texte', longueurMax: 200 },
    { nom: 'codePostal', libelle: 'Code postal', type: 'texte', longueurMax: 10 },
    { nom: 'ville', libelle: 'Ville', type: 'texte', longueurMax: 100 },
    { nom: 'email', libelle: 'Courriel', type: 'courriel', longueurMax: 200 },
    { nom: 'telephone', libelle: 'Téléphone', type: 'telephone', longueurMax: 30 },
  ],

  // Le syndic lit son propre client, cadré par le deuxième axe d'isolation.
  rolesLecture: InterneEtClientSyndic,
  rolesEcriture: EcritureReference,
}

const Sites: DescriptionRessource = {
  cle: 'sites',
  chemin: '/sites',
  route: '/api/sites',
  libelle: 'Sites',
  singulier: 'site',
  genre: 'masculin',
  sousTitre: 'Lieux d’intervention, géolocalisés pour le calcul de distance',
  etiquette: 'nom',

  colonnes: [
    { nom: 'nom', libelle: 'Nom' },
    { nom: 'clientId', libelle: 'Client', reference: 'clients' },
    { nom: 'ville', libelle: 'Ville' },
    { nom: 'latitude', libelle: 'Latitude', chiffres: true },
    { nom: 'longitude', libelle: 'Longitude', chiffres: true },
  ],

  champs: [
    {
      nom: 'clientId',
      libelle: 'Client',
      type: 'reference',
      reference: 'clients',
      requis: true,
    },
    { nom: 'nom', libelle: 'Nom', type: 'texte', requis: true, longueurMax: 200 },
    { nom: 'adresse', libelle: 'Adresse', type: 'texte', requis: true, longueurMax: 200 },
    { nom: 'codePostal', libelle: 'Code postal', type: 'texte', longueurMax: 10 },
    { nom: 'ville', libelle: 'Ville', type: 'texte', longueurMax: 100 },
    {
      nom: 'latitude',
      libelle: 'Latitude',
      type: 'nombre',
      requis: true,
      min: -90,
      max: 90,
      pas: 0.000001,
      // Ce n'est pas du zèle : le moteur d'affectation classe les candidats par
      // distance au site (CLAUDE.md §8). Des coordonnées absentes ou fausses ne
      // cassent rien de visible, elles rendent le classement faux.
      aide: 'Obligatoire : le classement des remplaçants se calcule depuis ce point.',
    },
    {
      nom: 'longitude',
      libelle: 'Longitude',
      type: 'nombre',
      requis: true,
      min: -180,
      max: 180,
      pas: 0.000001,
    },
    {
      nom: 'consignes',
      libelle: 'Consignes',
      type: 'texteLong',
      // ⚠️ La seconde phrase est une mesure de minimisation, pas du conseil
      // rédactionnel. Ce champ est le seul texte libre de la chaîne client, et rien
      // n'empêche d'y écrire « sonner chez Mme Durand, deuxième étage » — ce qui
      // ferait entrer une donnée personnelle dans une table que `RGPD.md` §8.3
      // exempte de journalisation parce qu'un site est un lieu.
      //
      // La parade proportionnée est d'empêcher la donnée d'entrer, pas de journaliser
      // toutes les lectures de sites : elle agit sur la cause, et elle coûte une
      // phrase. Voir `RGPD.md` §9.5.
      aide:
        'Accès, codes, matériel sur place. Lues par l’agent avant son passage. '
        + 'Ne pas y nommer de personne : préférer une fonction — « le gardien », '
        + '« l’accueil ».',
    },
  ],

  rolesLecture: InterneEtClientSyndic,
  rolesEcriture: EcritureReference,

  parent: { ressource: 'clients', champ: 'clientId' },
}

const Agents: DescriptionRessource = {
  cle: 'agents',
  chemin: '/agents',
  route: '/api/agents',
  // Ce que chargent les écrans des prestations et des absences pour leurs listes
  // déroulantes : le nom, sans l'adresse ni les coordonnées du domicile.
  routeChoix: '/api/agents/choix',
  libelle: 'Agents',
  singulier: 'agent',
  genre: 'masculin',
  sousTitre: 'Les personnes que le planning affecte',
  etiquette: 'nom',

  colonnes: [
    { nom: 'nom', libelle: 'Nom' },
    { nom: 'prenom', libelle: 'Prénom' },
    { nom: 'adresse', libelle: 'Adresse' },
    { nom: 'telephone', libelle: 'Téléphone' },
    { nom: 'dateEmbauche', libelle: 'Embauché le' },
    { nom: 'actif', libelle: 'Actif' },
    {
      // L'administrateur seul, qui charge déjà la liste des chefs pour le formulaire.
      // Pour un chef d'équipe elle vaudrait toujours lui-même ; l'ouvrir au responsable
      // demanderait de lui ouvrir `GET /api/agents/chefs-equipe` — écarté à l'étape 19,
      // confirmé le 4 novembre 2026.
      nom: 'chefEquipeId',
      libelle: 'Chef d’équipe',
      comptes: '/api/agents/chefs-equipe',
      roles: EcritureReference,
    },
  ],

  champs: [
    { nom: 'nom', libelle: 'Nom', type: 'texte', requis: true, longueurMax: 100 },
    { nom: 'prenom', libelle: 'Prénom', type: 'texte', requis: true, longueurMax: 100 },
    { nom: 'email', libelle: 'Courriel', type: 'courriel', longueurMax: 200 },
    { nom: 'telephone', libelle: 'Téléphone', type: 'telephone', longueurMax: 30 },
    { nom: 'adresse', libelle: 'Adresse', type: 'texte', longueurMax: 200 },
    {
      nom: 'latitude',
      libelle: 'Latitude',
      type: 'nombre',
      min: -90,
      max: 90,
      pas: 0.000001,
      // Facultatives ici, contrairement au site : sans coordonnées la note de
      // distance vaut 50 sur 100, l'agent reste candidat.
      aide: 'Facultatif. Sans coordonnées, la note de distance vaut 50 sur 100.',
    },
    { nom: 'longitude', libelle: 'Longitude', type: 'nombre', min: -180, max: 180, pas: 0.000001 },
    { nom: 'dateEmbauche', libelle: 'Date d’embauche', type: 'date', requis: true },
    {
      nom: 'actif',
      libelle: 'Actif',
      type: 'booleen',
      defaut: true,
      // Décrire l'effet, pas le champ : « actif » ne dit pas ce que décocher
      // provoque, et le moteur d'affectation l'écarte par un filtre dur.
      aide: 'Décoché, l’agent n’apparaît plus dans aucun classement de remplaçants.',
    },
    {
      // ⚠️ Décrit pour que le PUT le renvoie : la fiche est remplacée entière, et un
      // champ absent du formulaire détacherait l'agent de son chef à chaque
      // enregistrement (CLAUDE.md §11).
      nom: 'chefEquipeId',
      libelle: 'Chef d’équipe',
      type: 'compte',
      comptes: '/api/agents/chefs-equipe',
      aide: 'Facultatif. Un agent appartient à une seule équipe.',
    },
  ],

  rolesLecture: RolesInternes,
  rolesEcriture: EcritureReference,
}

const Contrats: DescriptionRessource = {
  cle: 'contrats',
  chemin: '/contrats',
  route: '/api/contrats',
  libelle: 'Contrats',
  singulier: 'contrat',
  genre: 'masculin',
  sousTitre: 'Engagements de passage sur un site, et leur récurrence',
  // ⚠️ Pas `siteId` : l'étiquette libelle aussi les options du choix des contrats sur
  // l'écran des prestations, qui n'a pas chargé les sites et n'y lirait qu'un
  // identifiant. La fréquence est imparfaite — deux contrats peuvent la partager —
  // mais elle est lisible, et la descente de niveau évite ce choix dans le parcours
  // normal.
  etiquette: 'frequence',

  colonnes: [
    { nom: 'frequence', libelle: 'Fréquence' },
    { nom: 'siteId', libelle: 'Site', reference: 'sites' },
    { nom: 'dateDebut', libelle: 'Début' },
    { nom: 'dateFin', libelle: 'Fin' },
    { nom: 'actif', libelle: 'Actif' },
  ],

  champs: [
    { nom: 'siteId', libelle: 'Site', type: 'reference', reference: 'sites', requis: true },
    { nom: 'dateDebut', libelle: 'Date de début', type: 'date', requis: true },
    {
      nom: 'dateFin',
      libelle: 'Date de fin',
      type: 'date',
      aide: 'À laisser vide tant que le contrat court sans échéance connue.',
    },
    {
      nom: 'frequence',
      libelle: 'Fréquence',
      type: 'texte',
      requis: true,
      longueurMax: 100,
      // Texte libre côté serveur, et c'est assumé : la génération ne le lit pas, elle
      // lit les jours de chaque prestation (SPEC.md, étape 20). Le dire évite de
      // croire qu'écrire « lundi » ici planifie quoi que ce soit.
      aide: 'En clair, par exemple « Lundi, mercredi, vendredi ». Elle désigne le contrat dans les listes ; les jours générés sont ceux de ses prestations.',
    },
    { nom: 'actif', libelle: 'Actif', type: 'booleen', defaut: true },
    {
      nom: 'inclutJoursFeries',
      libelle: 'Travaillé les jours fériés',
      type: 'booleen',
      aide: 'Cochée, la génération crée aussi les vacations des jours fériés — un hôpital, une résidence services. Décochée, elle les saute.',
    },
  ],

  rolesLecture: RolesInternes,
  rolesEcriture: EcritureReference,

  parent: { ressource: 'sites', champ: 'siteId' },
}

const Prestations: DescriptionRessource = {
  cle: 'prestations',
  chemin: '/prestations',
  route: '/api/prestations',
  libelle: 'Prestations',
  singulier: 'prestation',
  genre: 'feminin',
  sousTitre: 'Ce qui est fait sur place, et le temps que cela prend',
  etiquette: 'libelle',

  colonnes: [
    { nom: 'libelle', libelle: 'Libellé' },
    { nom: 'contratId', libelle: 'Contrat', reference: 'contrats' },
    { nom: 'dureeEstimeeMinutes', libelle: 'Durée (min)', chiffres: true },
    { nom: 'jours', libelle: 'Jours' },
    { nom: 'heureDebut', libelle: 'Heure', chiffres: true },
    { nom: 'agentTitulaireId', libelle: 'Titulaire', reference: 'agents' },
  ],

  champs: [
    {
      nom: 'contratId',
      libelle: 'Contrat',
      type: 'reference',
      reference: 'contrats',
      requis: true,
      aide: 'Les contrats sont désignés par leur fréquence. Partir du contrat lui-même évite ce choix.',
    },
    {
      // Avant le libellé et la durée, qu'il remplit : on choisit le type, puis on lit
      // et on corrige ce qu'il a proposé. ⚠️ Décrit même vide, pour que le PUT le
      // renvoie : sans lui, chaque enregistrement retirerait son type (CLAUDE.md §11).
      nom: 'typePrestationId',
      libelle: 'Type',
      type: 'reference',
      reference: 'typesPrestation',
      preremplit: { libelle: 'libelle', dureeEstimeeMinutes: 'dureeParDefautMinutes' },
      // Le serveur refuse un type désactivé qu'on choisit (SPEC.md, étape 21) : ne
      // pas le proposer évite de faire saisir ce qu'il rejette.
      seulementActifs: true,
      aide: 'Facultatif. Remplit le libellé et la durée, qui restent modifiables pour ce site.',
    },
    { nom: 'libelle', libelle: 'Libellé', type: 'texte', requis: true, longueurMax: 200 },
    {
      nom: 'dureeEstimeeMinutes',
      libelle: 'Durée estimée (minutes)',
      type: 'nombre',
      requis: true,
      min: 1,
      max: 1440,
      // Décrire l'effet : cette durée n'est pas documentaire, elle pèse un tiers du
      // score de remplacement par la charge hebdomadaire (CLAUDE.md §8).
      aide: 'Alimente la charge de la semaine, qui compte pour 35 points sur 100 dans le classement des remplaçants.',
    },
    {
      nom: 'jours',
      libelle: 'Jours de passage',
      type: 'jours',
      choix: JoursSemaine,
      aide: 'Les jours cochés font créer une vacation par semaine, depuis le planning. Aucun jour coché : la prestation se planifie à la main.',
    },
    {
      nom: 'heureDebut',
      libelle: 'Heure de début',
      type: 'heure',
      // Pas `requis` : l'heure ne l'est que si un jour est coché, et le serveur le dit
      // en français. La rendre obligatoire partout bloquerait les prestations
      // planifiées à la main, qui n'en ont pas.
      aide: 'Obligatoire dès qu’un jour est coché.',
    },
    {
      nom: 'agentTitulaireId',
      libelle: 'Titulaire',
      type: 'reference',
      reference: 'agents',
      aide: 'Affecté par la génération s’il est disponible ce jour-là. Sinon la vacation reste à pourvoir.',
    },
  ],

  rolesLecture: RolesInternes,
  rolesEcriture: EcritureReference,

  parent: { ressource: 'contrats', champ: 'contratId' },
}

/**
 * Le catalogue des prestations de l'entreprise (SPEC.md, étape 21). Un type porte ce
 * qui est vrai partout — libellé et durée par défaut ; les jours et l'heure, qui
 * dépendent du site, restent sur la prestation.
 */
const TypesPrestation: DescriptionRessource = {
  cle: 'typesPrestation',
  chemin: '/catalogue',
  route: '/api/types-prestation',
  libelle: 'Catalogue',
  singulier: 'type de prestation',
  genre: 'masculin',
  sousTitre: 'Les prestations types, à choisir plutôt qu’à retaper',
  etiquette: 'libelle',

  colonnes: [
    { nom: 'libelle', libelle: 'Libellé' },
    { nom: 'dureeParDefautMinutes', libelle: 'Durée par défaut (min)', chiffres: true },
    { nom: 'actif', libelle: 'Actif' },
  ],

  champs: [
    {
      nom: 'libelle',
      libelle: 'Libellé',
      type: 'texte',
      requis: true,
      longueurMax: 200,
      aide: 'Unique dans le catalogue. Le renommer ne change pas les prestations déjà créées.',
    },
    {
      nom: 'dureeParDefautMinutes',
      libelle: 'Durée par défaut (minutes)',
      type: 'nombre',
      requis: true,
      min: 1,
      max: 1440,
      aide: 'Proposée à la création d’une prestation, qui reste libre de la changer.',
    },
    {
      nom: 'actif',
      libelle: 'Actif',
      type: 'booleen',
      defaut: true,
      // Décrire l'effet : c'est la seule façon de retirer un type utilisé, sa
      // suppression étant refusée.
      aide: 'Décoché, le type n’est plus proposé ; les prestations qui le portent le gardent.',
    },
  ],

  rolesLecture: RolesInternes,
  rolesEcriture: EcritureReference,

  action: {
    libelle: 'Importer la liste type',
    enCours: 'Import…',
    route: '/api/types-prestation/importer-liste-type',
    suggestion: '« Importer la liste type » y ajoute d’un geste dix-sept prestations courantes, à corriger ensuite.',
    compteRendu: compteRenduImport,
  },
}

/**
 * Le compte rendu de l'import, en une phrase.
 *
 * ⚠️ Il nomme les types **laissés tels quels**, et ce n'est pas du remplissage : qui
 * rejoue l'import après avoir corrigé une durée doit lire que sa correction a tenu.
 * « 0 type ajouté » seul ne le lui dirait pas.
 */
export function compteRenduImport(reponse: unknown): string {
  const crees = listeDeTextes(reponse, 'crees')
  const presents = listeDeTextes(reponse, 'dejaPresents')

  if (crees === undefined || presents === undefined) return 'Liste type importée.'

  const n = presents.length

  if (crees.length === 0) {
    return `Aucun type ajouté : ${n > 1 ? `les ${String(n)}` : 'celui'} de la liste type `
      + `${n > 1 ? 'sont' : 'est'} déjà au catalogue, laissé${pluriel(n)} tel${pluriel(n)} quel${pluriel(n)}.`
  }

  const ajoutes = `${String(crees.length)} type${pluriel(crees.length)} ajouté${pluriel(crees.length)} au catalogue.`

  if (n === 0) return ajoutes

  return `${ajoutes} ${String(n)} déjà présent${pluriel(n)}, laissé${pluriel(n)} `
    + `tel${pluriel(n)} quel${pluriel(n)} : ${presents.join(', ')}.`
}

function pluriel(nombre: number): string {
  return nombre > 1 ? 's' : ''
}

function listeDeTextes(reponse: unknown, cle: string): string[] | undefined {
  if (typeof reponse !== 'object' || reponse === null) return undefined

  const valeur = (reponse as Record<string, unknown>)[cle]

  return Array.isArray(valeur) && valeur.every((element) => typeof element === 'string')
    ? valeur
    : undefined
}

const Absences: DescriptionRessource = {
  cle: 'absences',
  chemin: '/absences',
  route: '/api/absences',
  libelle: 'Absences',
  singulier: 'absence',
  genre: 'feminin',
  sousTitre: 'Ce qui déclenche la recherche d’un remplaçant',
  etiquette: 'type',

  colonnes: [
    { nom: 'agentId', libelle: 'Agent', reference: 'agents' },
    { nom: 'type', libelle: 'Type' },
    { nom: 'dateDebut', libelle: 'Du' },
    { nom: 'dateFin', libelle: 'Au' },
    { nom: 'motif', libelle: 'Motif' },
  ],

  champs: [
    { nom: 'agentId', libelle: 'Agent', type: 'reference', reference: 'agents', requis: true },
    { nom: 'type', libelle: 'Type', type: 'choix', requis: true, choix: TypesDAbsence, libelles: LibellesTypeAbsence },
    {
      nom: 'dateDebut',
      libelle: 'Du',
      type: 'date',
      requis: true,
      // L'enregistrement crée les remplacements dans la foulée : le dire évite de
      // chercher ailleurs ce que l'écran a déjà fait.
      aide: 'À l’enregistrement, les vacations découvertes sur cette période ouvrent un remplacement.',
    },
    { nom: 'dateFin', libelle: 'Au', type: 'date', requis: true },
    {
      nom: 'motif',
      libelle: 'Motif',
      type: 'texteLong',
      longueurMax: 500,
      masquerSi: { champ: 'type', vaut: SansMotif },
      aide: 'Facultatif. Le champ disparaît sur un arrêt maladie : le planning n’a pas besoin d’une donnée de santé.',
    },
  ],

  rolesLecture: RolesInternes,
  // ⚠️ Donnée d'exploitation, pas de référence : voir EcritureExploitation.
  rolesEcriture: EcritureExploitation,

  parent: { ressource: 'agents', champ: 'agentId' },
}

const Produits: DescriptionRessource = {
  cle: 'produits',
  chemin: '/produits',
  route: '/api/produits',
  libelle: 'Produits',
  singulier: 'produit',
  genre: 'masculin',
  sousTitre: 'Consommables suivis en stock',
  etiquette: 'nom',

  colonnes: [
    { nom: 'nom', libelle: 'Nom' },
    { nom: 'reference', libelle: 'Référence' },
    { nom: 'unite', libelle: 'Unité' },
    { nom: 'seuilAlerte', libelle: 'Seuil d’alerte', chiffres: true },
    { nom: 'actif', libelle: 'Actif' },
  ],

  champs: [
    { nom: 'nom', libelle: 'Nom', type: 'texte', requis: true, longueurMax: 200 },
    {
      // ⚠️ `nom: 'reference'` est la **propriété du DTO**, la référence fournisseur du
      // produit. Aucun rapport avec `type: 'reference'`, qui désigne une clé
      // étrangère : la collision de vocabulaire est malheureuse, elle n'a pas d'effet.
      nom: 'reference',
      libelle: 'Référence',
      type: 'texte',
      longueurMax: 50,
      aide: 'Facultatif. Référence du fournisseur, pour commander sans se tromper d’article.',
    },
    {
      nom: 'unite',
      libelle: 'Unité',
      type: 'texte',
      requis: true,
      longueurMax: 30,
      aide: 'Unité de compte : litre, pièce, rouleau…',
    },
    {
      nom: 'seuilAlerte',
      libelle: 'Seuil d’alerte',
      type: 'nombre',
      requis: true,
      min: 0,
      max: 999999.99,
      pas: 0.01,
      aide: 'Quantité en dessous de laquelle le stock est signalé comme bas.',
    },
    {
      nom: 'actif',
      libelle: 'Actif',
      type: 'booleen',
      defaut: true,
      // Décrire l'effet : c'est la seule façon de retirer un produit qui a servi, sa
      // suppression étant refusée dès qu'un mouvement de stock le cite (SPEC.md, étape 22).
      aide: 'Décoché, le produit ne rentre plus en stock ; ce qui en reste peut encore sortir.',
    },
  ],

  rolesLecture: RolesInternes,
  rolesEcriture: EcritureReference,
}

/** `TypeEmplacement`, miroir de `backend/Models/Emplacement.cs`. Le site n'en est pas un. */
const TypesDEmplacement = ['Depot', 'Vehicule'] as const

const LibellesTypeEmplacement: Readonly<Record<(typeof TypesDEmplacement)[number], string>> = {
  Depot: 'Dépôt',
  Vehicule: 'Véhicule',
}

/**
 * Dépôts et véhicules (SPEC.md, étape 22). Écrits par l'exploitation, comme les
 * mouvements qui les visent.
 */
const Emplacements: DescriptionRessource = {
  cle: 'emplacements',
  chemin: '/emplacements',
  route: '/api/emplacements',
  libelle: 'Emplacements',
  singulier: 'emplacement',
  genre: 'masculin',
  sousTitre: 'Les dépôts et les véhicules qui portent du stock',
  etiquette: 'nom',

  colonnes: [
    { nom: 'nom', libelle: 'Nom' },
    { nom: 'type', libelle: 'Type' },
    { nom: 'actif', libelle: 'Actif' },
  ],

  champs: [
    {
      nom: 'nom',
      libelle: 'Nom',
      type: 'texte',
      requis: true,
      longueurMax: 200,
      // ⚠️ La condition de l'exemption d'`EmplacementDto` au journal d'accès
      // (CLAUDE.md §7 bis) : un véhicule nommé « Kangoo de Paul » ferait de cette liste
      // une donnée personnelle que personne ne journalise. L'écran le dit au moment
      // où le nom se choisit.
      aide: 'Un véhicule se nomme par sa plaque ou son modèle, jamais par son conducteur.',
    },
    { nom: 'type', libelle: 'Type', type: 'choix', requis: true, choix: TypesDEmplacement, libelles: LibellesTypeEmplacement },
    {
      nom: 'actif',
      libelle: 'Actif',
      type: 'booleen',
      defaut: true,
      aide: 'Décoché, l’emplacement ne reçoit plus rien ; ce qui s’y trouve peut encore en sortir.',
    },
  ],

  rolesLecture: RolesInternes,
  rolesEcriture: EcritureExploitation,
}

/**
 * Ce qu'un passage de chaque type consomme (SPEC.md, étape 22, lot B). Elle descend du
 * catalogue — « les consommations de ce type » — alors que l'écran vit dans la section
 * du stock : la descente traverse les sections, et c'est voulu.
 */
const ConsommationsType: DescriptionRessource = {
  cle: 'consommationsType',
  chemin: '/consommations',
  route: '/api/consommations-type',
  libelle: 'Consommations',
  singulier: 'consommation',
  genre: 'feminin',
  sousTitre: 'Ce qu’un passage de chaque type consomme, pour prévoir le besoin',
  // Imparfait, mais ni une référence ni un booléen : aucune ressource n'en descend, et
  // la ligne se nomme ainsi dans une confirmation de suppression.
  etiquette: 'quantiteParPassage',

  colonnes: [
    { nom: 'typePrestationId', libelle: 'Type de prestation', reference: 'typesPrestation' },
    { nom: 'produitId', libelle: 'Produit', reference: 'produits' },
    { nom: 'quantiteParPassage', libelle: 'Par passage', chiffres: true },
  ],

  champs: [
    {
      nom: 'typePrestationId',
      libelle: 'Type de prestation',
      type: 'reference',
      reference: 'typesPrestation',
      requis: true,
      seulementActifs: true,
    },
    {
      nom: 'produitId',
      libelle: 'Produit',
      type: 'reference',
      reference: 'produits',
      requis: true,
      // Le serveur refuse d'attacher un produit désactivé : ne pas le proposer.
      seulementActifs: true,
    },
    {
      nom: 'quantiteParPassage',
      libelle: 'Quantité par passage',
      type: 'nombre',
      requis: true,
      min: 0.01,
      max: 99999999.99,
      pas: 0.01,
      aide: 'Dans l’unité du produit. Une prestation sans type ne consomme rien.',
    },
  ],

  rolesLecture: RolesInternes,
  rolesEcriture: EcritureExploitation,

  parent: { ressource: 'typesPrestation', champ: 'typePrestationId' },
}

/**
 * ⚠️ L'ordre suit la **chaîne du modèle**, pas l'alphabet : un site appartient à
 * un client, on ne peut donc pas créer le second avant le premier. C'est cet ordre
 * que la sous-navigation affiche, et c'est lui qui rend la mise en route d'une
 * entreprise possible de haut en bas sans revenir en arrière.
 */
export const Descriptions: readonly DescriptionRessource[] = [
  Clients,
  Sites,
  Contrats,
  Prestations,
  TypesPrestation,
  Agents,
  Absences,
  Produits,
  Emplacements,
  ConsommationsType,
]

export function descriptionDe(cle: string): DescriptionRessource | undefined {
  return Descriptions.find((description) => description.cle === cle)
}

/**
 * Les descriptions lisibles par ces rôles, dans l'ordre de la chaîne.
 *
 * La **première** est la destination d'arrivée d'une section : un `ClientSyndic`
 * atterrit sur Clients, qui lui est ouvert, et jamais sur une liste qu'il ne peut
 * pas lire.
 */
export function descriptionsLisibles(
  cles: readonly string[],
  roles: readonly string[],
): readonly DescriptionRessource[] {
  return cles
    .map((cle) => descriptionDe(cle))
    .filter((description): description is DescriptionRessource => description !== undefined)
    .filter((description) => description.rolesLecture.some((role) => roles.includes(role)))
}

/**
 * Les ressources dont cette ressource est le parent — **dérivé**, jamais déclaré.
 *
 * C'est ce qui porte la descente de niveau : la liste des clients offre « Voir les
 * sites » parce que `Sites` déclare `parent: { ressource: 'clients' }`. Déclarer les
 * enfants en plus des parents donnerait deux listes à tenir d'accord, et la
 * divergence prendrait la forme d'un lien vers un écran qui n'affiche rien.
 */
export function enfantsDe(cle: string): readonly DescriptionRessource[] {
  return Descriptions.filter((description) => description.parent?.ressource === cle)
}
