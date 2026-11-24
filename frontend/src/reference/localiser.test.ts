import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { describe, test } from 'node:test'

// ⚠️ Extensions explicites : `node --test` n'applique pas la résolution de Vite (CLAUDE.md §10).
import { descriptionDe } from './descriptions.ts'
import {
  AideLocaliser,
  MotAVerifier,
  Refus,
  bornesDe,
  chaineCoordonnee,
  corpsLocalisation,
  messageRefus,
  phraseTrouvee,
  reglageActif,
  refusLocal,
  routeLocalisation,
} from './localiser.ts'

const Controleur = readFileSync(
  new URL('../../../backend/Controllers/SitesController.cs', import.meta.url), 'utf8')

const sites = descriptionDe('sites')!
const bornes = bornesDe(sites.champs, sites.localiser!)

describe('localiser — refus locaux', () => {
  test('les bornes viennent des longueurMax de la description', () => {
    assert.deepEqual(bornes, { adresse: 200, codePostal: 10, ville: 100 })
  })

  test('chaque refus local est mot pour mot celui du serveur', () => {
    const attendus = [
      refusLocal('', '', '', bornes),
      refusLocal('a'.repeat(201), '', '', bornes),
      refusLocal('20 rue', '1'.repeat(11), '', bornes),
      refusLocal('20 rue', '', 'v'.repeat(101), bornes),
      refusLocal('20 rue\u0007', '', '', bornes),
      refusLocal('a'.repeat(150), '', 'v'.repeat(60), bornes),
      refusLocal('ab', '', '', bornes),
    ]
    for (const message of attendus) {
      assert.ok(message !== null)
      assert.ok(Controleur.includes(`"${message}"`), `absent de SitesController.cs : ${message}`)
    }
  })

  test('un caractère invisible (Control ou Format) est refusé, comme par le serveur', () => {
    assert.equal(refusLocal('20 rue\u202E', '', '', bornes), Refus.invisible)
    assert.equal(refusLocal('20\u200B rue', '', '', bornes), Refus.invisible)
    assert.equal(refusLocal('20 rue', '\u00AD75007', '', bornes), Refus.invisible)
    assert.equal(refusLocal('20 rue \u{E0041}', '', '', bornes), Refus.invisible)
  })

  test('une adresse vide ou blanche est refusée, une adresse correcte passe', () => {
    assert.equal(refusLocal('   ', '', '', bornes), Refus.adresseVide)
    assert.equal(refusLocal('20 avenue de Ségur', '75007', 'Paris', bornes), null)
    assert.equal(refusLocal('« 20 rue »', '', '', bornes), Refus.debut)
  })
})

describe('localiser — corps, coordonnées, phrases', () => {
  test('le corps est rogné, les champs facultatifs vides partent à null', () => {
    assert.deepEqual(corpsLocalisation(' 20 rue ', ' ', ''), { adresse: '20 rue', codePostal: null, ville: null })
    assert.deepEqual(corpsLocalisation('20 rue', '75007', 'Paris'), { adresse: '20 rue', codePostal: '75007', ville: 'Paris' })
  })

  test('une coordonnée part en chaîne, arrondie à six décimales, sans exposant ni -0', () => {
    assert.equal(chaineCoordonnee(48.8566141), '48.856614')
    assert.equal(chaineCoordonnee(2.308628), '2.308628')
    assert.equal(chaineCoordonnee(-0.0000001), '0')
    assert.equal(chaineCoordonnee(0), '0')
    assert.equal(chaineCoordonnee(-61.5), '-61.5')
    assert.equal(chaineCoordonnee(0.000001), '0.000001')
  })

  test('la phrase trouvée nomme l’adresse, et dit « à vérifier » en mots', () => {
    const trouvee = { latitude: 1, longitude: 2, adresseTrouvee: '20 Avenue de Ségur 75007 Paris' }
    assert.equal(phraseTrouvee({ ...trouvee, aVerifier: false }),
      'Latitude et longitude renseignées. Trouvé : 20 Avenue de Ségur 75007 Paris')
    assert.equal(MotAVerifier, 'à vérifier : le point est approximatif')
  })

  test('un refus du serveur s’affiche tel quel, une annulation n’affiche rien', () => {
    const erreur = Object.assign(new Error('Adresse introuvable : vérifiez-la.'), { name: 'ErreurApi' })
    assert.equal(messageRefus(erreur), 'Adresse introuvable : vérifiez-la.')
    assert.equal(messageRefus(Object.assign(new Error('x'), { name: 'AbortError' })), null)
    assert.equal(messageRefus('?'), 'La localisation a échoué.')
  })

  test('un serveur injoignable (TypeError de fetch) dit de réessayer ou de saisir à la main', () => {
    const attendu = 'Le serveur ne répond pas : réessayez, ou saisissez les coordonnées à la main.'
    assert.equal(messageRefus(new TypeError('Failed to fetch')), attendu)
    assert.equal(messageRefus(new TypeError('Load failed')), attendu)
  })

  test('le réglage se lit sur la première entreprise, faux par défaut', () => {
    assert.equal(reglageActif({ elements: [{ geocodageActif: true }] }), true)
    assert.equal(reglageActif({ elements: [{ geocodageActif: false }] }), false)
    assert.equal(reglageActif({ elements: [{}] }), false)
    assert.equal(reglageActif({ elements: [] }), false)
    assert.equal(reglageActif(null), false)
  })

  test('la route se déduit de celle de la description', () => {
    assert.equal(routeLocalisation(sites.route), '/api/sites/localiser')
  })

  test('l’aide du bouton dit ce qui part et le cas du domicile', () => {
    for (const mot of ['IGN', 'domicile', 'donnée personnelle']) assert.ok(AideLocaliser.includes(mot), mot)
  })
})
