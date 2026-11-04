import assert from 'node:assert/strict'
import { describe, test } from 'node:test'

import {
  etatsDe,
  formaterQuantite,
  HorizonParDefaut,
  Horizons,
  libelleHorizon,
  phraseSansType,
  resumeAlertes,
} from './prevision.ts'
import type { PrevisionProduit } from '../types/stock.ts'

function ligne(partielle: Partial<PrevisionProduit>): PrevisionProduit {
  return {
    produitId: 'p',
    seuilAlerte: 0,
    stockTotal: 0,
    stockReserve: 0,
    besoin: 0,
    manque: 0,
    sousLeSeuil: false,
    couvertureCourte: false,
    sites: [],
    ...partielle,
  }
}

describe('prévision — les états d’un produit', () => {
  test('la couverture d’abord, puis le seuil, et les deux se disent', () => {
    assert.deepEqual(
      etatsDe(ligne({ couvertureCourte: true, sousLeSeuil: true })).map((etat) => etat.mot),
      ['Couverture courte', 'Sous le seuil'],
    )
  })

  test('chaque alerte seule porte son mot', () => {
    assert.deepEqual(etatsDe(ligne({ sousLeSeuil: true })), [{ mot: 'Sous le seuil', ton: 'alerte' }])
    assert.deepEqual(etatsDe(ligne({ couvertureCourte: true })), [{ mot: 'Couverture courte', ton: 'alerte' }])
  })

  test('sans alerte, un mot quand même : la couleur ne porte jamais seule l’information', () => {
    assert.deepEqual(etatsDe(ligne({})), [{ mot: 'Sans alerte', ton: 'calme' }])
  })
})

describe('prévision — les phrases', () => {
  test('les quantités à la virgule, au centième', () => {
    assert.equal(formaterQuantite(0.5), '0,5')
    assert.equal(formaterQuantite(12), '12')
    assert.equal(formaterQuantite(1.256), '1,26')
    assert.equal(formaterQuantite(0), '0')
  })

  test('les vacations sans type : rien à dire sous zéro, accordé au-dessus', () => {
    assert.equal(phraseSansType(0), null)
    assert.match(phraseSansType(1) ?? '', /^1 vacation de la période n’a pas de type/)
    assert.match(phraseSansType(3) ?? '', /^3 vacations de la période n’ont pas de type/)
    assert.match(phraseSansType(3) ?? '', /Choisir un type dans le catalogue/)
  })

  test('le résumé compte les deux alertes séparément', () => {
    assert.equal(resumeAlertes([ligne({})]), 'Aucune alerte sur la période.')
    assert.equal(
      resumeAlertes([ligne({ couvertureCourte: true }), ligne({ couvertureCourte: true, sousLeSeuil: true })]),
      '2 produits en couverture courte, 1 sous le seuil.',
    )
    assert.equal(resumeAlertes([ligne({ sousLeSeuil: true })]), '1 sous le seuil.')
  })

  test('l’horizon de la génération, quatre semaines par défaut', () => {
    assert.deepEqual([Horizons[0], Horizons.at(-1), Horizons.length], [1, 12, 12])
    assert.equal(HorizonParDefaut, 4)
    assert.equal(libelleHorizon(1), '1 semaine')
    assert.equal(libelleHorizon(4), '4 semaines')
  })
})
