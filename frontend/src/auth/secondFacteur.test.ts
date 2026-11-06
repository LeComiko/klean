import assert from 'node:assert/strict'
import { describe, test } from 'node:test'

// ⚠️ Extension explicite : `node --test` n'applique pas la résolution sans
// extension de Vite (CLAUDE.md §10).
import { coteQr, estEtape, lireCode, lireCodeRecuperation, MargeQr, saisieDe, traceQr } from './secondFacteur.ts'

/**
 * Le second facteur côté écran (SPEC.md, étape 23).
 *
 * Les refus locaux épargnent des essais au verrou : côté serveur, une saisie mal
 * formée compte comme un échec, dans les cinq partagés avec le mot de passe.
 */
describe('lireCode — six chiffres', () => {
  test('six chiffres passent, espaces retirés', () => {
    assert.equal(lireCode('123456'), '123456')
    assert.equal(lireCode('123 456'), '123456')
    assert.equal(lireCode(' 012345 '), '012345')
  })

  test('le zéro de tête est gardé : « 012345 » n’est pas « 12345 »', () => {
    assert.equal(lireCode('012345'), '012345')
  })

  test('cinq ou sept chiffres, des lettres, du vide : refusés', () => {
    assert.equal(lireCode('12345'), null)
    assert.equal(lireCode('1234567'), null)
    assert.equal(lireCode('12345a'), null)
    assert.equal(lireCode(''), null)
  })
})

describe('lireCodeRecuperation — seize caractères de Crockford', () => {
  test('la forme remise passe, ramenée à seize caractères', () => {
    assert.equal(lireCodeRecuperation('ABCD-EFGH-JKMN-PQRS'), 'ABCDEFGHJKMNPQRS')
  })

  test('minuscules, espaces et sans tirets : la même chose', () => {
    assert.equal(lireCodeRecuperation('abcd efgh jkmn pqrs'), 'ABCDEFGHJKMNPQRS')
    assert.equal(lireCodeRecuperation('abcdefghjkmnpqrs'), 'ABCDEFGHJKMNPQRS')
  })

  test('O se lit 0, I et L se lisent 1, comme le serveur', () => {
    assert.equal(lireCodeRecuperation('OOOO-IIII-LLLL-2345'), '0000111111112345')
  })

  test('un U, une longueur fausse : refusés', () => {
    assert.equal(lireCodeRecuperation('UBCD-EFGH-JKMN-PQRS'), null)
    assert.equal(lireCodeRecuperation('ABCD-EFGH-JKMN'), null)
  })
})

describe('saisieDe — une seule des deux clés part', () => {
  test('un code part seul', () => {
    assert.deepEqual(saisieDe('123 456', false), { corps: { code: '123456' } })
  })

  test('un code de récupération part seul, normalisé', () => {
    assert.deepEqual(saisieDe('abcd-efgh-jkmn-pqrs', true), { corps: { codeRecuperation: 'ABCDEFGHJKMNPQRS' } })
  })

  test('une saisie mal formée est refusée avant l’appel, avec la forme attendue', () => {
    const refus = saisieDe('12345', false)

    assert.ok('refus' in refus)
    assert.match(refus.refus, /six chiffres/)
    assert.ok('refus' in saisieDe('123456', true))
  })
})

describe('traceQr — le dessin des modules', () => {
  const modules = [
    [true, false],
    [false, true],
  ]

  test('un carré par module sombre, décalé de la marge', () => {
    assert.equal(
      traceQr(modules),
      `M${String(MargeQr)} ${String(MargeQr)}h1v1h-1zM${String(MargeQr + 1)} ${String(MargeQr + 1)}h1v1h-1z`,
    )
  })

  test('x est la colonne, y la ligne : une matrice transposée ne se lit plus', () => {
    const asymetrique = [[false, true], [false, false]]

    assert.equal(traceQr(asymetrique), `M${String(MargeQr + 1)} ${String(MargeQr)}h1v1h-1z`)
  })

  test('le côté compte la marge des deux bords', () => {
    assert.equal(coteQr(modules), 2 + 2 * MargeQr)
  })
})

describe('estEtape — 202 ou session', () => {
  test('une étape se reconnaît, une session non', () => {
    assert.equal(estEtape({ etape: 'SecondFacteur' }), true)
    assert.equal(
      estEtape({
        expiration: '2026-11-06T10:00:00Z',
        profil: {
          id: 'a', email: 'a@b.c', nom: 'N', prenom: '', entrepriseId: 'e',
          agentId: null, clientId: null, actif: true, roles: [],
        },
      }),
      false,
    )
  })
})
