import assert from 'node:assert/strict'
import { describe, test } from 'node:test'

import {
  corpsMouvement,
  lieuDe,
  lieuxRequis,
  lireLieu,
  lireQuantite,
  Natures,
  refusLocal,
  saisieVide,
  valeurLieu,
} from './mouvements.ts'
import type { SaisieMouvement } from './mouvements.ts'

const Depot = valeurLieu('emplacement', 'e-depot')
const Kangoo = valeurLieu('emplacement', 'e-kangoo')
const Residence = valeurLieu('site', 's-residence')

function saisie(partielle: Partial<SaisieMouvement>): SaisieMouvement {
  return { ...saisieVide(), produitId: 'p-detergent', quantite: '2', ...partielle }
}

describe('mouvements — les lieux qu’exige la nature', () => {
  test('miroir de ServiceStock : entrée et inventaire visent, sortie part, transfert relie', () => {
    assert.deepEqual(
      Natures.map((nature) => [nature, lieuxRequis(nature)]),
      [
        ['Entree', { origine: false, destination: true }],
        ['Transfert', { origine: true, destination: true }],
        ['Sortie', { origine: true, destination: false }],
        ['Inventaire', { origine: false, destination: true }],
      ],
    )
  })

  test('les noms des natures sont ceux de l’énumération du serveur', () => {
    // Le serveur les lit par leurs noms exacts : « Entrée » avec accent serait refusé.
    assert.deepEqual([...Natures].sort(), ['Entree', 'Inventaire', 'Sortie', 'Transfert'])
  })
})

describe('mouvements — un lieu dans une liste', () => {
  test('le préfixe choisit la clé du serveur', () => {
    assert.deepEqual(lireLieu(Depot), { emplacementId: 'e-depot', siteId: null })
    assert.deepEqual(lireLieu(Residence), { emplacementId: null, siteId: 's-residence' })
  })

  test('une valeur vide ou inconnue ne désigne rien', () => {
    for (const valeur of ['', 'site:', 'garage:x', 'e-depot']) {
      assert.deepEqual(lireLieu(valeur), { emplacementId: null, siteId: null }, valeur)
    }
  })

  test('aller et retour', () => {
    assert.equal(lieuDe('e-depot', null), Depot)
    assert.equal(lieuDe(null, 's-residence'), Residence)
    assert.equal(lieuDe(null, null), '')
  })
})

describe('mouvements — le corps envoyé', () => {
  test('une entrée vers un site', () => {
    assert.deepEqual(corpsMouvement(saisie({ nature: 'Entree', destination: Residence, quantite: '2,5' })), {
      produitId: 'p-detergent',
      nature: 'Entree',
      origineEmplacementId: null,
      origineSiteId: null,
      destinationEmplacementId: null,
      destinationSiteId: 's-residence',
      quantite: 2.5,
    })
  })

  test('un transfert du dépôt au véhicule', () => {
    const corps = corpsMouvement(saisie({ nature: 'Transfert', origine: Depot, destination: Kangoo }))

    assert.equal(corps.origineEmplacementId, 'e-depot')
    assert.equal(corps.destinationEmplacementId, 'e-kangoo')
    assert.equal(corps.quantite, 2)
  })

  test('un côté que la nature n’exige pas part à null, même saisi', () => {
    // Le cas réel : choisir un départ pour un transfert, puis basculer sur « Entrée ».
    // Le départ reste dans l'état du formulaire ; l'envoyer ferait refuser le mouvement
    // pour un champ que l'écran n'affiche plus.
    const entree = corpsMouvement(saisie({ nature: 'Entree', origine: Depot, destination: Kangoo }))
    assert.equal(entree.origineEmplacementId, null)
    assert.equal(entree.origineSiteId, null)

    const sortie = corpsMouvement(saisie({ nature: 'Sortie', origine: Kangoo, destination: Residence }))
    assert.equal(sortie.destinationEmplacementId, null)
    assert.equal(sortie.destinationSiteId, null)
    assert.equal(sortie.origineEmplacementId, 'e-kangoo')
  })

  test('la quantité part en nombre, la virgule lue', () => {
    assert.equal(lireQuantite('1,25'), 1.25)
    assert.equal(lireQuantite(' 3 '), 3)
    assert.ok(Number.isNaN(lireQuantite('')))
    assert.ok(Number.isNaN(lireQuantite('deux')))
  })
})

describe('mouvements — les refus prononcés avant d’appeler', () => {
  test('rien à choisir de plus : aucun refus', () => {
    assert.equal(refusLocal(saisie({ nature: 'Entree', destination: Depot })), null)
    assert.equal(refusLocal(saisie({ nature: 'Inventaire', destination: Depot, quantite: '0' })), null)
  })

  test('chaque choix manquant est nommé', () => {
    assert.equal(refusLocal(saisie({ produitId: '', destination: Depot })), 'Choisir un produit.')
    assert.equal(refusLocal(saisie({ nature: 'Sortie' })), 'Choisir le lieu de départ.')
    assert.equal(refusLocal(saisie({ nature: 'Entree' })), 'Choisir le lieu d’arrivée.')
    assert.equal(refusLocal(saisie({ nature: 'Inventaire' })), 'Choisir le lieu compté.')
  })

  test('un transfert vers son propre lieu est refusé', () => {
    assert.equal(
      refusLocal(saisie({ nature: 'Transfert', origine: Depot, destination: Depot })),
      'Un transfert relie deux lieux différents.',
    )
  })

  test('les bornes de la quantité, que le DTO refuserait en anglais', () => {
    assert.equal(refusLocal(saisie({ destination: Depot, quantite: '' })), 'Saisir une quantité.')
    assert.match(refusLocal(saisie({ destination: Depot, quantite: '0' })) ?? '', /^La quantité doit être positive/)
    assert.match(refusLocal(saisie({ destination: Depot, quantite: '-1' })) ?? '', /^La quantité doit être positive/)
    assert.equal(refusLocal(saisie({ destination: Depot, quantite: '100000000' })), 'Quantité trop grande.')
    assert.equal(
      refusLocal(saisie({ nature: 'Inventaire', destination: Depot, quantite: '-1' })),
      'Une quantité comptée n’est jamais négative.',
    )
  })
})
