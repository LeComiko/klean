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

/** `TypeClient`, miroir de `backend/Models/TypeClient.cs`. */
const TypesDeClient = [
  'EntrepriseCliente',
  'Syndic',
  'GestionnaireImmobilier',
  'Copropriete',
  'Autre',
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
    { nom: 'type', libelle: 'Type', type: 'choix', requis: true, choix: TypesDeClient },
    { nom: 'adresse', libelle: 'Adresse', type: 'texte', longueurMax: 200 },
    { nom: 'codePostal', libelle: 'Code postal', type: 'texte', longueurMax: 10 },
    { nom: 'ville', libelle: 'Ville', type: 'texte', longueurMax: 100 },
    { nom: 'email', libelle: 'Courriel', type: 'courriel', longueurMax: 200 },
    { nom: 'telephone', libelle: 'Téléphone', type: 'telephone', longueurMax: 30 },
  ],

  // Le syndic lit son propre client, cadré par le deuxième axe d'isolation.
  rolesLecture: InterneEtClientSyndic,
  rolesEcriture: EcritureReference,

  cascade:
    'ses sites, leurs contrats, leurs prestations et toutes les interventions qui '
    + 'en découlent',
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
      aide: 'Accès, codes, matériel sur place. Lues par l’agent avant son passage.',
    },
  ],

  rolesLecture: InterneEtClientSyndic,
  rolesEcriture: EcritureReference,

  parent: { ressource: 'clients', champ: 'clientId' },

  cascade: 'ses contrats, leurs prestations et toutes les interventions qui en découlent',
}

const Agents: DescriptionRessource = {
  cle: 'agents',
  chemin: '/agents',
  route: '/api/agents',
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
  ],

  rolesLecture: RolesInternes,
  rolesEcriture: EcritureReference,

  // Pas de `cascade`, et ce n'est pas un oubli : `Intervention.AgentId` et
  // `Remplacement.AgentRemplacantId` sont les deux seules clés étrangères du
  // modèle en `DeleteBehavior.Restrict`. Un agent qui porte une intervention ne se
  // supprime pas — le serveur répond 409 et le message est affiché tel quel. C'est
  // le bon choix pour l'intégrité du planning, et l'étape 17 y ajoutera un chemin
  // d'anonymisation pour la demande d'effacement.
}

/**
 * ⚠️ L'ordre suit la **chaîne du modèle**, pas l'alphabet : un site appartient à
 * un client, on ne peut donc pas créer le second avant le premier. C'est cet ordre
 * que la sous-navigation affiche, et c'est lui qui rend la mise en route d'une
 * entreprise possible de haut en bas sans revenir en arrière.
 */
export const Descriptions: readonly DescriptionRessource[] = [Clients, Sites, Agents]

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
