import assert from 'node:assert/strict'
import { describe, test } from 'node:test'

import type {
  ChampRessource,
  ColonneRessource,
  DescriptionRessource,
} from '../types/reference.ts'
import {
  accorder,
  colonnesVisibles,
  corpsDe,
  departCreation,
  departOuverture,
  designer,
  etiquetteDe,
  filtreDe,
  formaterNombre,
  majuscule,
  masque,
  nouveau,
  optionsProposees,
  preremplir,
  rendu,
  texteDe,
  tousLes,
  Vide,
} from './valeurs.ts'

/**
 * Les conversions de `Reference.tsx`, éprouvées à part.
 *
 * ⚠️ Cette série est **rejouée dans cinq fuseaux** (`scripts/tests.mjs`), et ce
 * n'est pas du zèle : `rendu` compose trois fonctions de date, et la mauvaise
 * composition décale une colonne d'un jour entier sans rien casser. Le sabotage a
 * été joué — voir le dernier bloc.
 */

const Champs: readonly ChampRessource[] = [
  { nom: 'nom', libelle: 'Nom', type: 'texte', requis: true, longueurMax: 200 },
  { nom: 'ville', libelle: 'Ville', type: 'texte', longueurMax: 100 },
  { nom: 'latitude', libelle: 'Latitude', type: 'nombre', requis: true, min: -90, max: 90 },
  { nom: 'altitude', libelle: 'Altitude', type: 'nombre' },
  { nom: 'dateEmbauche', libelle: 'Embauche', type: 'date', requis: true },
  { nom: 'actif', libelle: 'Actif', type: 'booleen', defaut: true },
  { nom: 'archive', libelle: 'Archivé', type: 'booleen' },
  { nom: 'clientId', libelle: 'Client', type: 'reference', reference: 'clients', requis: true },
  { nom: 'consignes', libelle: 'Consignes', type: 'texteLong' },
  {
    nom: 'type',
    libelle: 'Type',
    type: 'choix',
    choix: ['Conge', 'Indisponibilite', 'Nouveau'],
    // `Nouveau` sans libellé à dessein : la valeur doit passer telle quelle, pas vide.
    libelles: { Conge: 'Congé', Indisponibilite: 'Indisponibilité' },
  },
]

/**
 * Un champ masquable, calqué sur le motif d'une absence : il disparaît quand le type
 * vaut `Maladie`, parce qu'un motif d'arrêt maladie est une donnée de santé au sens
 * de l'article 9 du RGPD (SPEC.md étape 16 bis).
 */
const ChampsAvecMasque: readonly ChampRessource[] = [
  { nom: 'agentId', libelle: 'Agent', type: 'reference', reference: 'agents', requis: true },
  { nom: 'type', libelle: 'Type', type: 'choix', requis: true, choix: ['Maladie', 'Conge'] },
  {
    nom: 'motif',
    libelle: 'Motif',
    type: 'texteLong',
    longueurMax: 500,
    masquerSi: { champ: 'type', vaut: ['Maladie'] },
  },
]

describe('departCreation', () => {
  test('un champ sans défaut part vide, une case sans défaut part décochée', () => {
    const depart = departCreation(Champs)

    assert.equal(depart.nom, '')
    assert.equal(depart.ville, '')
    assert.equal(depart.latitude, '')
    assert.equal(depart.archive, false)
  })

  test('le défaut déclaré est respecté', () => {
    // ⚠️ `actif` en est la raison d'être : un agent créé décoché n'apparaît dans
    // aucun classement de remplaçants, et rien à l'écran ne dirait pourquoi.
    assert.equal(departCreation(Champs).actif, true)
  })

  test('chaque champ de la description est présent', () => {
    // Une clé absente rendrait le contrôle non contrôlé côté React, et React
    // basculerait en avertissement de console sans que l'écran change.
    const depart = departCreation(Champs)

    for (const champ of Champs) {
      assert.ok(champ.nom in depart, champ.nom)
    }
  })
})

describe('departOuverture', () => {
  const ligne = {
    id: 'abc',
    nom: 'Résidence Bellevue',
    ville: null,
    latitude: 48.8566,
    altitude: 0,
    dateEmbauche: '2024-02-29T00:00:00Z',
    actif: true,
    archive: false,
    clientId: 'client-1',
    consignes: 'Code 1234',
  }

  test('un null devient une chaîne vide, pas « null »', () => {
    // `String(null)` rend « null », qui se serait affiché dans le champ Ville et
    // serait parti tel quel à l'enregistrement.
    assert.equal(departOuverture(Champs, ligne).ville, '')
  })

  test('un nombre devient sa représentation décimale', () => {
    assert.equal(departOuverture(Champs, ligne).latitude, '48.8566')
  })

  test('un zéro est conservé, il n’est pas traité comme absent', () => {
    // Le piège du `if (!valeur)` : une altitude de 0 est une valeur, pas un vide.
    assert.equal(departOuverture(Champs, ligne).altitude, '0')
  })

  test('une date est ramenée à AAAA-MM-JJ, seul format qu’accepte input type=date', () => {
    assert.equal(departOuverture(Champs, ligne).dateEmbauche, '2024-02-29')
  })

  test('les booléens traversent en booléens', () => {
    const depart = departOuverture(Champs, ligne)

    assert.equal(depart.actif, true)
    assert.equal(depart.archive, false)
  })

  test('une propriété absente de la ligne part vide', () => {
    assert.equal(departOuverture(Champs, { id: 'x' }).nom, '')
  })
})

describe('corpsDe', () => {
  test('un facultatif vide part à null, pas en chaîne vide', () => {
    // Une chaîne vide enregistrerait une ville nommée « », que rien ne distingue
    // d'une ville absente et qu'aucune recherche ne retrouve.
    const corps = corpsDe(Champs, { ...departCreation(Champs), nom: 'X', ville: '' })

    assert.equal(corps.ville, null)
  })

  test('un facultatif fait d’espaces part aussi à null', () => {
    assert.equal(corpsDe(Champs, { ...departCreation(Champs), ville: '   ' }).ville, null)
  })

  test('un texte est élagué de ses espaces', () => {
    assert.equal(corpsDe(Champs, { ...departCreation(Champs), nom: '  Bellevue  ' }).nom, 'Bellevue')
  })

  test('un nombre part en nombre JSON, pas en chaîne', () => {
    // ⚠️ Envoyer « 48.8566 » à une propriété `double` fait échouer la liaison de
    // modèle d'ASP.NET, avec un message en anglais que l'écran ne peut pas traduire.
    const corps = corpsDe(Champs, { ...departCreation(Champs), latitude: '48.8566' })

    assert.equal(corps.latitude, 48.8566)
    assert.equal(typeof corps.latitude, 'number')
  })

  test('un nombre négatif et un zéro traversent', () => {
    const corps = corpsDe(Champs, {
      ...departCreation(Champs),
      latitude: '-33.8688',
      altitude: '0',
    })

    assert.equal(corps.latitude, -33.8688)
    assert.equal(corps.altitude, 0)
  })

  test('un obligatoire vide part en chaîne vide, pour être refusé bruyamment', () => {
    // ⚠️ Le choix assumé : un `null` ou une clé absente sur une latitude donnerait
    // zéro côté serveur, qui passe le [Range(-90, 90)] et place le site dans le
    // golfe de Guinée. Un refus vaut mieux qu'une coordonnée inventée.
    const corps = corpsDe(Champs, { ...departCreation(Champs), latitude: '' })

    assert.equal(corps.latitude, '')
  })

  test('une case décochée part en false, jamais en null', () => {
    const corps = corpsDe(Champs, { ...departCreation(Champs), actif: false })

    assert.equal(corps.actif, false)
  })

  test('une case dont la valeur a été perdue part en false', () => {
    // Une valeur manquante ne doit pas devenir `true` par accident : un agent activé
    // sans qu'on l'ait demandé entre dans les classements de remplaçants.
    assert.equal(corpsDe(Champs, {}).actif, false)
  })

  test('aucune clé étrangère au formulaire n’est envoyée', () => {
    // Le corps est construit depuis la description, pas depuis les valeurs : un
    // champ parasite — `id`, `entrepriseId` — ne peut pas s'y glisser. C'est la même
    // garde que l'absence d'EntrepriseId des CreateDto (CLAUDE.md §7).
    const corps = corpsDe(Champs, {
      ...departCreation(Champs),
      id: 'abc',
      entrepriseId: 'autre-entreprise',
    })

    assert.equal('id' in corps, false)
    assert.equal('entrepriseId' in corps, false)
  })

  test('un aller-retour ouverture puis enregistrement ne change rien', () => {
    // Le parcours réel : ouvrir une ligne, ne rien toucher, enregistrer. Une
    // conversion asymétrique y perdrait une valeur en silence.
    const ligne = {
      id: 'abc',
      nom: 'Bellevue',
      ville: 'Paris',
      latitude: -0.5,
      altitude: null,
      dateEmbauche: '2023-03-15T00:00:00Z',
      actif: true,
      archive: false,
      clientId: 'client-1',
      consignes: null,
    }

    const corps = corpsDe(Champs, departOuverture(Champs, ligne))

    assert.equal(corps.nom, 'Bellevue')
    assert.equal(corps.ville, 'Paris')
    assert.equal(corps.latitude, -0.5)
    assert.equal(corps.altitude, null)
    assert.equal(corps.dateEmbauche, '2023-03-15')
    assert.equal(corps.actif, true)
    assert.equal(corps.consignes, null)
  })
})

describe('texteDe', () => {
  test('rend une chaîne non vide, un nombre, et rien d’autre', () => {
    assert.equal(texteDe('Bellevue'), 'Bellevue')
    assert.equal(texteDe(42), '42')
    assert.equal(texteDe(0), '0')
    assert.equal(texteDe(''), undefined)
    assert.equal(texteDe(null), undefined)
    assert.equal(texteDe(undefined), undefined)
    assert.equal(texteDe(true), undefined)
  })
})

describe('rendu', () => {
  const resoudre = (ressource: string, identifiant: string): string =>
    ressource === 'clients' && identifiant === 'client-1' ? 'Syndic Bellevue' : identifiant

  function cellule(ligne: Record<string, unknown>, colonne: ColonneRessource): string {
    return rendu(ligne, colonne, Champs, resoudre)
  }

  test('une valeur absente rend le tiret cadratin', () => {
    const colonne: ColonneRessource = { nom: 'ville', libelle: 'Ville' }

    assert.equal(cellule({ ville: null }, colonne), Vide)
    assert.equal(cellule({ ville: '' }, colonne), Vide)
    assert.equal(cellule({}, colonne), Vide)
  })

  test('un zéro n’est pas un vide', () => {
    assert.equal(cellule({ altitude: 0 }, { nom: 'altitude', libelle: 'Altitude' }), '0')
  })

  test('un choix s’affiche par son mot, et la valeur du serveur ne se voit pas', () => {
    // « Conge » à l'écran, sous un formulaire qui propose « Congé » : la même absence,
    // deux orthographes. Le libellé est appliqué à l'affichage seulement — `corpsDe`
    // envoie toujours le nom exact, que le serveur exige.
    const colonne: ColonneRessource = { nom: 'type', libelle: 'Type' }

    assert.equal(cellule({ type: 'Conge' }, colonne), 'Congé')
    assert.equal(cellule({ type: 'Indisponibilite' }, colonne), 'Indisponibilité')
    assert.equal(cellule({ type: 'Nouveau' }, colonne), 'Nouveau')
    assert.deepEqual(corpsDe(Champs.filter((champ) => champ.nom === 'type'), { type: 'Conge' }), { type: 'Conge' })
  })

  test('un nombre s’écrit à la virgule', () => {
    assert.equal(cellule({ latitude: 48.85 }, { nom: 'latitude', libelle: 'Latitude' }), '48,85')
    assert.equal(cellule({ altitude: -0.5 }, { nom: 'altitude', libelle: 'Altitude' }), '-0,5')
    assert.equal(cellule({ altitude: 120 }, { nom: 'altitude', libelle: 'Altitude' }), '120')
    assert.equal(formaterNombre(0.25), '0,25')
  })

  test('un booléen se dit en français', () => {
    assert.equal(cellule({ actif: true }, { nom: 'actif', libelle: 'Actif' }), 'Oui')
    assert.equal(cellule({ actif: false }, { nom: 'actif', libelle: 'Actif' }), 'Non')
  })

  test('une référence est résolue en libellé', () => {
    const colonne: ColonneRessource = { nom: 'clientId', libelle: 'Client', reference: 'clients' }

    assert.equal(cellule({ clientId: 'client-1' }, colonne), 'Syndic Bellevue')
  })

  test('un compte est résolu par sa route, comme une référence par sa clé', () => {
    const colonne: ColonneRessource = { nom: 'chefEquipeId', libelle: 'Chef', comptes: '/api/agents/chefs-equipe' }
    const parRoute = (source: string, identifiant: string): string =>
      source === '/api/agents/chefs-equipe' && identifiant === 'compte-1' ? 'Léa Martin' : identifiant

    assert.equal(rendu({ chefEquipeId: 'compte-1' }, colonne, Champs, parRoute), 'Léa Martin')
    assert.equal(rendu({ chefEquipeId: null }, colonne, Champs, parRoute), Vide)
  })

  test('une référence introuvable rend son identifiant plutôt que rien', () => {
    // Le cas d'une liste déroulante tronquée à 200 : afficher l'identifiant est
    // laid, mais une cellule vide laisserait croire que le site n'a pas de client.
    const colonne: ColonneRessource = { nom: 'clientId', libelle: 'Client', reference: 'clients' }

    assert.equal(cellule({ clientId: 'client-9' }, colonne), 'client-9')
  })

  describe('les dates, dans le fuseau de cette exécution', () => {
    const colonne: ColonneRessource = { nom: 'dateEmbauche', libelle: 'Embauche' }

    test('minuit UTC rend bien le jour que le serveur a écrit', () => {
      // ⚠️ LE test de ce fichier. `new Date('2023-03-15T00:00:00Z').getDate()` rend
      // **14** à tout décalage négatif — à Los Angeles la colonne entière reculerait
      // d'un jour — et rend 15 à Paris comme en UTC. Une intégration continue en UTC
      // est aveugle au défaut, c'est pourquoi cette série tourne dans cinq fuseaux.
      assert.equal(cellule({ dateEmbauche: '2023-03-15T00:00:00Z' }, colonne), '15 mars 2023')
    })

    test('le premier du mois ne recule pas sur le mois précédent', () => {
      assert.equal(cellule({ dateEmbauche: '2025-06-01T00:00:00Z' }, colonne), '1 juin 2025')
    })

    test('le premier janvier ne recule pas sur l’année précédente', () => {
      assert.equal(cellule({ dateEmbauche: '2026-01-01T00:00:00Z' }, colonne), '1 janvier 2026')
    })

    test('le 29 février d’une année bissextile est conservé', () => {
      assert.equal(cellule({ dateEmbauche: '2024-02-29T00:00:00Z' }, colonne), '29 février 2024')
    })

    test('un jour d’été ne dérive pas avec l’heure d’été', () => {
      // Le passage à l'heure d'été change le décalage en cours d'année : une date
      // d'août et une date de janvier ne se comportent pas pareil sous une
      // conversion fautive.
      assert.equal(cellule({ dateEmbauche: '2026-08-15T00:00:00Z' }, colonne), '15 août 2026')
    })
  })
})

describe('departCreation avec des valeurs imposées', () => {
  test('la valeur imposée l’emporte sur le vide', () => {
    // La descente de niveau : arrivé sur « les sites de ce client », le formulaire
    // part avec ce client choisi.
    const depart = departCreation(Champs, { clientId: 'client-1' })

    assert.equal(depart.clientId, 'client-1')
  })

  test('la valeur imposée l’emporte sur le défaut déclaré', () => {
    // Y compris quand elle est « fausse » au sens de JavaScript : un `false` imposé
    // sur un champ dont le défaut vaut `true` doit rester `false`, sinon l'agent
    // serait créé actif alors que l'appelant demandait l'inverse.
    //
    // ⚠️ Ce test ne distingue **pas** les deux écritures possibles de `departCreation`,
    // et c'est dit ici pour que personne ne le croie : `??` ne retombe que sur `null`
    // et `undefined`, qu'une `ValeursFormulaire` ne peut pas porter. Le sabotage qui
    // remplace le `in` par un `??` reste donc vert. Aucune entrée accessible aux types
    // du projet ne les sépare.
    assert.equal(departCreation(Champs, { actif: false }).actif, false)
    assert.equal(departCreation(Champs, { nom: '' }).nom, '')
  })

  test('une clé imposée qui ne correspond à aucun champ est ignorée', () => {
    // Le corps est construit depuis la description : une clé parasite ne doit pas
    // s'introduire par cette porte non plus.
    const depart = departCreation(Champs, { entrepriseId: 'autre-entreprise' })

    assert.equal('entrepriseId' in depart, false)
  })

  test('sans valeurs imposées, le comportement d’avant est inchangé', () => {
    assert.deepEqual(departCreation(Champs), departCreation(Champs, {}))
    assert.equal(departCreation(Champs).actif, true)
  })
})

describe('masque', () => {
  const motif = ChampsAvecMasque[2]

  test('un champ sans condition n’est jamais masqué', () => {
    assert.equal(masque(Champs[0], { nom: 'X' }), false)
  })

  test('le champ disparaît quand le pivot porte une valeur interdite', () => {
    assert.equal(masque(motif, { type: 'Maladie' }), true)
  })

  test('il reste affiché sur les autres valeurs, et sur aucune valeur', () => {
    assert.equal(masque(motif, { type: 'Conge' }), false)
    assert.equal(masque(motif, { type: '' }), false)
    assert.equal(masque(motif, {}), false)
  })

  test('la comparaison est exacte, pas une inclusion de texte', () => {
    // « Maladie professionnelle » n'est pas « Maladie » : une comparaison par
    // `includes` sur la chaîne masquerait des types que l'énumération ne nomme pas.
    assert.equal(masque(motif, { type: 'MaladieProfessionnelle' }), false)
  })
})

describe('corpsDe et les champs masqués', () => {
  test('un champ masqué part à null, même s’il portait du texte', () => {
    // ⚠️ LE test du masquage. Le parcours réel : saisir un motif, puis basculer le
    // type sur « Maladie ». Sans cette règle, le texte part quand même — le serveur
    // refuse en 400 en parlant de donnée de santé, l'écran n'affiche plus le champ
    // dont le refus parle, et la donnée a quitté le navigateur pour rien.
    const corps = corpsDe(ChampsAvecMasque, {
      agentId: 'agent-1',
      type: 'Maladie',
      motif: 'Lombalgie',
    })

    assert.equal(corps.motif, null)
    assert.equal(corps.type, 'Maladie')
    assert.equal(corps.agentId, 'agent-1')
  })

  test('le même champ part normalement quand il n’est pas masqué', () => {
    const corps = corpsDe(ChampsAvecMasque, {
      agentId: 'agent-1',
      type: 'Conge',
      motif: 'Congé payé',
    })

    assert.equal(corps.motif, 'Congé payé')
  })

  test('la clé du champ masqué reste présente, à null', () => {
    // Un `PUT` construit depuis ce corps doit **effacer** un motif déjà enregistré,
    // pas le laisser en place. Omettre la clé laisserait la valeur précédente.
    const corps = corpsDe(ChampsAvecMasque, { type: 'Maladie', motif: 'à effacer' })

    assert.ok('motif' in corps)
    assert.equal(corps.motif, null)
  })
})

describe('filtreDe', () => {
  const sites: DescriptionRessource = {
    cle: 'sites',
    chemin: '/sites',
    route: '/api/sites',
    libelle: 'Sites',
    singulier: 'site',
    genre: 'masculin',
    sousTitre: 'Lieux',
    etiquette: 'nom',
    colonnes: [],
    champs: Champs,
    rolesLecture: [],
    rolesEcriture: [],
    parent: { ressource: 'clients', champ: 'clientId' },
  }

  const clients: DescriptionRessource = { ...sites, cle: 'clients', parent: undefined }

  function avec(parametres: Record<string, string>) {
    return (nom: string): string | null => parametres[nom] ?? null
  }

  test('le paramètre de la parenté devient le filtre', () => {
    assert.deepEqual(filtreDe(sites, avec({ clientId: 'client-1' })), {
      champ: 'clientId',
      valeur: 'client-1',
    })
  })

  test('une ressource sans parent n’a jamais de filtre', () => {
    assert.equal(filtreDe(clients, avec({ clientId: 'client-1' })), undefined)
  })

  test('un paramètre absent ne filtre rien', () => {
    assert.equal(filtreDe(sites, avec({})), undefined)
  })

  test('un paramètre vide ne filtre rien non plus', () => {
    // ⚠️ Mesuré côté serveur, et c'est la raison d'être de cette assertion : un
    // `Guid?` recevant `?clientId=` est lié à `null` par ASP.NET, qui rend donc la
    // liste entière (FiltresListeTests). Si l'écran y voyait un filtre, il afficherait
    // « Sites de … » au-dessus de tous les sites de l'entreprise.
    assert.equal(filtreDe(sites, avec({ clientId: '' })), undefined)
    assert.equal(filtreDe(sites, avec({ clientId: '   ' })), undefined)
  })

  test('un autre paramètre de la requête est ignoré', () => {
    // Seul le champ de la parenté compte : un `?siteId=` sur l'écran des sites ne
    // doit pas filtrer les sites par eux-mêmes.
    assert.equal(filtreDe(sites, avec({ siteId: 'site-1', page: '3' })), undefined)
  })
})

describe('les formules de langue', () => {
  test('l’article s’accorde, et s’élide devant une voyelle', () => {
    assert.equal(designer('client', 'masculin'), 'le client')
    assert.equal(designer('prestation', 'feminin'), 'la prestation')
    // ⚠️ Le cas qui impose l'élision : « la absence » se lit dans la confirmation de
    // suppression, et l'écran des absences est celui du lot B.
    assert.equal(designer('absence', 'feminin'), 'l’absence')
    assert.equal(designer('agent', 'masculin'), 'l’agent')
  })

  test('l’apostrophe est typographique, comme partout ailleurs', () => {
    assert.ok(designer('absence', 'feminin').includes('’'))
    assert.equal(designer('absence', 'feminin').includes("'"), false)
  })

  test('l’adjectif « nouveau » s’accorde et s’élide', () => {
    assert.equal(nouveau('client', 'masculin'), 'Nouveau client')
    assert.equal(nouveau('agent', 'masculin'), 'Nouvel agent')
    assert.equal(nouveau('prestation', 'feminin'), 'Nouvelle prestation')
    assert.equal(nouveau('absence', 'feminin'), 'Nouvelle absence')
  })

  test('le participe passé s’accorde', () => {
    assert.equal(accorder('masculin'), '')
    assert.equal(accorder('feminin'), 'e')
  })

  test('le déterminant pluriel s’accorde, et le pluriel n’est pas fabriqué', () => {
    assert.equal(tousLes('masculin'), 'tous les')
    assert.equal(tousLes('feminin'), 'toutes les')
  })

  test('la majuscule ne touche que la première lettre', () => {
    assert.equal(majuscule('le contrat'), 'Le contrat')
    assert.equal(majuscule('l’absence'), 'L’absence')
    assert.equal(majuscule(''), '')
  })
})

/**
 * La récurrence d'une prestation (étape 20) : deux types de champ que le serveur lit
 * à la lettre. `JoursDeLaSemaine.TryLire` refuse toute autre forme qu'un tableau de
 * noms exacts, et un `TimeSpan` ne se lit pas en `hh:mm`.
 */
describe('les jours et l’heure', () => {
  const Semaine = ['Lundi', 'Mardi', 'Mercredi', 'Jeudi', 'Vendredi', 'Samedi', 'Dimanche'] as const

  const Recurrence: readonly ChampRessource[] = [
    { nom: 'jours', libelle: 'Jours', type: 'jours', choix: Semaine },
    { nom: 'heureDebut', libelle: 'Heure', type: 'heure' },
  ]

  test('une création part sans jour coché et sans heure', () => {
    assert.deepEqual(departCreation(Recurrence), { jours: [], heureDebut: '' })
  })

  test('l’ouverture reprend les jours et rend l’heure sans secondes', () => {
    // Un `<input type="time">` qui reçoit « 07:30:00 » l'ignore dans certains
    // navigateurs : le champ paraît vide, et l'enregistrer effacerait l'heure.
    assert.deepEqual(
      departOuverture(Recurrence, { jours: ['Lundi', 'Jeudi'], heureDebut: '07:30:00' }),
      { jours: ['Lundi', 'Jeudi'], heureDebut: '07:30' },
    )
  })

  test('une prestation sans récurrence s’ouvre sans jour et sans heure', () => {
    assert.deepEqual(
      departOuverture(Recurrence, { jours: [], heureDebut: null }),
      { jours: [], heureDebut: '' },
    )
  })

  test('les jours partent du lundi au dimanche, quel que soit l’ordre des clics', () => {
    const corps = corpsDe(Recurrence, { jours: ['Vendredi', 'Lundi', 'Mercredi'], heureDebut: '07:00' })

    assert.deepEqual(corps.jours, ['Lundi', 'Mercredi', 'Vendredi'])
  })

  test('aucun jour coché part en tableau vide, jamais en null', () => {
    // `null` serait lu par le serveur comme « champ absent », que le `PUT` remet à
    // vide de toute façon ; mais le tableau vide dit ce que l'écran veut dire.
    assert.deepEqual(corpsDe(Recurrence, { jours: [], heureDebut: '' }), { jours: [], heureDebut: null })
  })

  test('une valeur inconnue est retirée plutôt qu’envoyée', () => {
    assert.deepEqual(corpsDe(Recurrence, { jours: ['lundi', 'Jeudi'], heureDebut: '' }).jours, ['Jeudi'])
  })

  test('l’heure part avec ses secondes, comme un TimeSpan', () => {
    assert.equal(corpsDe(Recurrence, { jours: ['Lundi'], heureDebut: '07:30' }).heureDebut, '07:30:00')
  })

  test('la colonne montre les jours abrégés et l’heure sans secondes', () => {
    const resoudre = () => ''

    assert.equal(
      rendu({ jours: ['Lundi', 'Jeudi'] }, { nom: 'jours', libelle: 'Jours' }, Recurrence, resoudre),
      'Lun Jeu',
    )
    assert.equal(rendu({ jours: [] }, { nom: 'jours', libelle: 'Jours' }, Recurrence, resoudre), Vide)
    assert.equal(
      rendu({ heureDebut: '07:30:00' }, { nom: 'heureDebut', libelle: 'Heure' }, Recurrence, resoudre),
      '07:30',
    )
  })
})

/**
 * Le préremplissage d'une prestation par son type — étape 21, lot C.
 *
 * La règle tient en une ligne — un champ n'est réécrit que s'il est vide ou encore à
 * la valeur du choix précédent —, et chacun de ses deux bras protège quelque chose :
 * le premier permet de se raviser, le second de ne jamais écraser un libellé propre
 * au site.
 */
describe('preremplir', () => {
  const ChampType: ChampRessource = {
    nom: 'typePrestationId',
    libelle: 'Type',
    type: 'reference',
    reference: 'typesPrestation',
    preremplit: { libelle: 'libelle', dureeEstimeeMinutes: 'dureeParDefautMinutes' },
  }

  const Hall = { id: 'h', libelle: 'Hall d’entrée', dureeParDefautMinutes: 20 }
  const Escaliers = { id: 'e', libelle: 'Escaliers et paliers', dureeParDefautMinutes: 30 }

  const vierge = { typePrestationId: 'h', libelle: '', dureeEstimeeMinutes: '' }

  test('remplit un formulaire vierge, la durée en texte comme le formulaire la tient', () => {
    assert.deepEqual(preremplir(ChampType, vierge, undefined, Hall), {
      typePrestationId: 'h', libelle: 'Hall d’entrée', dureeEstimeeMinutes: '20',
    })
  })

  test('se raviser remplace ce que le premier choix avait mis', () => {
    const apresHall = preremplir(ChampType, vierge, undefined, Hall)

    const apresEscaliers = preremplir(ChampType, { ...apresHall, typePrestationId: 'e' }, Hall, Escaliers)

    assert.equal(apresEscaliers.libelle, 'Escaliers et paliers')
    assert.equal(apresEscaliers.dureeEstimeeMinutes, '30')
  })

  test('une saisie n’est jamais écrasée, champ par champ', () => {
    // Le libellé a été retouché après le premier choix, la durée non.
    const valeurs = { typePrestationId: 'e', libelle: 'Hall + escalier B', dureeEstimeeMinutes: '20' }

    const suivantes = preremplir(ChampType, valeurs, Hall, Escaliers)

    assert.equal(suivantes.libelle, 'Hall + escalier B')
    assert.equal(suivantes.dureeEstimeeMinutes, '30')
  })

  test('une prestation enregistrée garde son libellé et sa durée propres', () => {
    // ⚠️ Le cas de la modification : rien ne vient du type, tout vient du site.
    const ouverte = { typePrestationId: 'e', libelle: 'Hall côté cour', dureeEstimeeMinutes: '25' }

    assert.deepEqual(preremplir(ChampType, ouverte, Hall, Escaliers), ouverte)
  })

  test('revenir à « — choisir — » ne vide rien', () => {
    const remplie = { typePrestationId: '', libelle: 'Hall d’entrée', dureeEstimeeMinutes: '20' }

    assert.deepEqual(preremplir(ChampType, remplie, Hall, undefined), remplie)
  })

  test('un champ sans préremplissage ne touche à rien', () => {
    const sansRemplissage: ChampRessource = { ...ChampType, preremplit: undefined }

    assert.deepEqual(preremplir(sansRemplissage, vierge, undefined, Hall), vierge)
  })
})

describe('optionsProposees', () => {
  const Champ: ChampRessource = {
    nom: 'typePrestationId', libelle: 'Type', type: 'reference', reference: 'typesPrestation', seulementActifs: true,
  }

  const Options = [
    { valeur: 'h', libelle: 'Hall d’entrée', actif: true },
    { valeur: 'v', libelle: 'Vitres', actif: false },
    { valeur: 'p', libelle: 'Produit sans état' },
  ]

  test('retire les lignes désactivées, garde celles qui n’ont pas d’état', () => {
    assert.deepEqual(optionsProposees(Champ, Options, '').map((option) => option.valeur), ['h', 'p'])
  })

  test('garde la valeur enregistrée, et dit qu’elle est désactivée', () => {
    // ⚠️ Sans elle, la liste afficherait « — choisir — » sur une prestation qui porte
    // ce type, et le serveur, lui, garde le lien.
    assert.deepEqual(optionsProposees(Champ, Options, 'v').map((option) => option.libelle), [
      'Hall d’entrée', 'Vitres (désactivé)', 'Produit sans état',
    ])
  })

  test('un champ qui ne le demande pas propose tout, tel quel', () => {
    const toutes: ChampRessource = { ...Champ, seulementActifs: undefined }

    assert.deepEqual(optionsProposees(toutes, Options, ''), Options)
  })
})

describe('etiquetteDe', () => {
  function description(etiquette: string): DescriptionRessource {
    return {
      cle: 'essais',
      chemin: '/essais',
      route: '/api/essais',
      libelle: 'Essais',
      singulier: 'essai',
      genre: 'masculin',
      sousTitre: '',
      etiquette,
      colonnes: [],
      champs: Champs,
      rolesLecture: [],
      rolesEcriture: [],
    }
  }

  test('le nom d’une ligne passe par les mêmes libellés que le tableau', () => {
    // Sinon : « Supprimer « Conge » ? » sous une colonne qui affiche « Congé ».
    assert.equal(etiquetteDe(description('type'), { type: 'Conge' }), 'Congé')
  })

  test('un nombre se nomme à la virgule', () => {
    // Le cas réel : une consommation se nomme par sa quantité par passage.
    assert.equal(etiquetteDe(description('altitude'), { altitude: 0.5 }), '0,5')
  })

  test('un texte reste tel quel, une valeur absente ne nomme rien', () => {
    assert.equal(etiquetteDe(description('nom'), { nom: 'Bellevue' }), 'Bellevue')
    assert.equal(etiquetteDe(description('nom'), {}), undefined)
    assert.equal(etiquetteDe(description('type'), { type: '' }), undefined)
  })
})

describe('colonnesVisibles', () => {
  const colonnes: readonly ColonneRessource[] = [
    { nom: 'nom', libelle: 'Nom' },
    { nom: 'chefEquipeId', libelle: 'Chef', comptes: '/api/agents/chefs-equipe', roles: ['Administrateur'] },
  ]

  test('une colonne réservée n’apparaît qu’à ses rôles', () => {
    assert.deepEqual(colonnesVisibles(colonnes, ['Administrateur']).map((c) => c.nom), ['nom', 'chefEquipeId'])
    assert.deepEqual(colonnesVisibles(colonnes, ['Responsable']).map((c) => c.nom), ['nom'])
    assert.deepEqual(colonnesVisibles(colonnes, ['ChefEquipe', 'Administrateur']).map((c) => c.nom), ['nom', 'chefEquipeId'])
    assert.deepEqual(colonnesVisibles(colonnes, []).map((c) => c.nom), ['nom'])
  })
})
