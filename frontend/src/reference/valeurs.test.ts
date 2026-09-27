import assert from 'node:assert/strict'
import { describe, test } from 'node:test'

import type { ChampRessource, ColonneRessource } from '../types/reference.ts'
import { corpsDe, departCreation, departOuverture, rendu, texteDe, Vide } from './valeurs.ts'

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

  test('un booléen se dit en français', () => {
    assert.equal(cellule({ actif: true }, { nom: 'actif', libelle: 'Actif' }), 'Oui')
    assert.equal(cellule({ actif: false }, { nom: 'actif', libelle: 'Actif' }), 'Non')
  })

  test('une référence est résolue en libellé', () => {
    const colonne: ColonneRessource = { nom: 'clientId', libelle: 'Client', reference: 'clients' }

    assert.equal(cellule({ clientId: 'client-1' }, colonne), 'Syndic Bellevue')
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
