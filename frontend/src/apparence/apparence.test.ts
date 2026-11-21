import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { describe, test } from 'node:test'

// ⚠️ Extension explicite : `node --test` n'applique pas la résolution sans
// extension de Vite (CLAUDE.md §10).
import {
  appliquerApparence,
  Apparences,
  apparenceEnregistree,
  attributTheme,
  CleApparence,
  enregistrerApparence,
  LibellesApparence,
  lireApparence,
} from './apparence.ts'
import type { RacineApparence, StockageApparence } from './apparence.ts'

function stockage(initial: Record<string, string> = {}): StockageApparence & { contenu: Record<string, string> } {
  const contenu = { ...initial }

  return {
    contenu,
    getItem: (cle) => contenu[cle] ?? null,
    setItem: (cle, valeur) => {
      contenu[cle] = valeur
    },
  }
}

/** Le stockage d'une navigation privée stricte : chaque accès lève. */
const StockageBloque: StockageApparence = {
  getItem: () => {
    throw new Error('SecurityError')
  },
  setItem: () => {
    throw new Error('QuotaExceededError')
  },
}

describe('lireApparence', () => {
  test('les trois noms se relisent tels quels', () => {
    for (const apparence of Apparences) assert.equal(lireApparence(apparence), apparence)
  })

  test('rien, ou une valeur illisible, vaut automatique', () => {
    for (const brut of [null, '', 'dark', 'Sombre', 'light', '0', ' clair']) {
      assert.equal(lireApparence(brut), 'automatique', `« ${String(brut)} »`)
    }
  })
})

describe('le stockage', () => {
  test('un choix enregistré est relu', () => {
    const local = stockage()

    assert.equal(enregistrerApparence(local, 'clair'), true)
    assert.equal(local.contenu[CleApparence], 'clair')
    assert.equal(apparenceEnregistree(local), 'clair')
  })

  test('un stockage qui lève rend automatique, et l’enregistrement le dit sans lever', () => {
    assert.equal(apparenceEnregistree(StockageBloque), 'automatique')
    assert.equal(enregistrerApparence(StockageBloque, 'sombre'), false)
  })

  test('sans stockage, automatique, et rien n’est retenu', () => {
    assert.equal(apparenceEnregistree(null), 'automatique')
    assert.equal(enregistrerApparence(null, 'sombre'), false)
  })
})

describe('la racine du document', () => {
  test('clair et sombre posent data-theme, automatique le retire', () => {
    const racine: RacineApparence = { dataset: {} }

    appliquerApparence(racine, 'sombre')
    assert.equal(racine.dataset.theme, 'sombre')

    appliquerApparence(racine, 'clair')
    assert.equal(racine.dataset.theme, 'clair')

    appliquerApparence(racine, 'automatique')
    assert.equal('theme' in racine.dataset, false)
  })

  test('chaque valeur posée existe dans tokens.css, et y force le bon color-scheme', () => {
    // Un attribut que la feuille de style ne connaît pas ne change rien, sans erreur :
    // la correspondance entre ce module et tokens.css n'est écrite nulle part ailleurs.
    const jetons = readFileSync(new URL('../styles/tokens.css', import.meta.url), 'utf8')
    const attendu = { clair: 'light', sombre: 'dark' } as const

    for (const apparence of Apparences) {
      const attribut = attributTheme(apparence)
      if (attribut === null) continue

      const regle = new RegExp(`:root\\[data-theme='${attribut}'\\]\\s*\\{\\s*color-scheme:\\s*${attendu[attribut]}\\s*;`)
      assert.match(jetons, regle, `data-theme='${attribut}'`)
    }
  })
})

test('chaque apparence a son libellé, et aucun n’est son nom brut', () => {
  for (const apparence of Apparences) {
    assert.ok(LibellesApparence[apparence], apparence)
    assert.notEqual(LibellesApparence[apparence], apparence)
  }
})
