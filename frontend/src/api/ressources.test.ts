import assert from 'node:assert/strict'
import { beforeEach, describe, test } from 'node:test'

import { listerRessource, listerToutPourChoix, TailleMaximale } from './ressources.ts'

/**
 * Série sur la **construction des URL** de liste, et sur rien d'autre.
 *
 * Elle naît du lot B de l'étape 16 bis, qui a ajouté le filtre de descente de niveau.
 * Ce que fait ce module tient en une concaténation, et c'est précisément le genre de
 * code qu'on relit sans voir : un `?` au lieu d'un `&` donne
 * `?page=1&taille=25?clientId=…`, que le serveur lie en silence — `taille` devient
 * « 25?clientId=… », donc invalide, donc ramené à la valeur par défaut, et le filtre
 * n'est jamais appliqué. L'écran afficherait alors toute la liste sous un bandeau
 * annonçant un filtre, sans qu'aucune erreur ne soit levée nulle part.
 *
 * Aucun DOM, aucun serveur : seul `globalThis.fetch` est remplacé, comme dans
 * `client.test.ts`.
 */

const urls: string[] = []

const environnement = globalThis as unknown as {
  document: { cookie: string }
  fetch: (entree: string, options?: RequestInit) => Promise<Response>
}

environnement.document = { cookie: '' }

environnement.fetch = (entree) => {
  urls.push(entree)

  return Promise.resolve(
    new Response(JSON.stringify({ elements: [], total: 0, page: 1, taille: 25 }), {
      status: 200,
      headers: { 'Content-Type': 'application/json; charset=utf-8' },
    }),
  )
}

beforeEach(() => {
  urls.length = 0
})

describe('listerRessource — sans filtre', () => {
  test('la pagination part dans la chaîne de requête', async () => {
    await listerRessource('/api/sites', 2, 25)

    assert.equal(urls[0], '/api/sites?page=2&taille=25')
  })

  test('une taille au-delà du plafond du serveur est ramenée avant l’envoi', async () => {
    // Le serveur borne silencieusement : demander 500 en rendrait 200, et l'écran
    // afficherait « 1 sur 1 » à tort. Mieux vaut envoyer ce qu'on obtiendra.
    await listerRessource('/api/sites', 1, 5000)

    assert.equal(urls[0], `/api/sites?page=1&taille=${String(TailleMaximale)}`)
  })

  test('aucun paramètre parasite n’est ajouté', async () => {
    await listerRessource('/api/sites', 1, 25)

    assert.equal(urls[0].includes('undefined'), false)
    assert.equal(urls[0].split('?').length, 2)
  })
})

describe('listerRessource — avec filtre de descente de niveau', () => {
  test('le filtre s’ajoute en troisième paramètre, avec une esperluette', async () => {
    // ⚠️ LE test de ce fichier. Un `?` ici au lieu d'un `&` ne lève aucune erreur :
    // le serveur lirait `taille` comme « 25?clientId=… », le ramènerait à sa valeur
    // par défaut, et ignorerait le filtre.
    await listerRessource('/api/sites', 1, 25, undefined, {
      champ: 'clientId',
      valeur: '0f8b1c2d-0000-4000-8000-000000000001',
    })

    assert.equal(
      urls[0],
      '/api/sites?page=1&taille=25&clientId=0f8b1c2d-0000-4000-8000-000000000001',
    )
  })

  test('le nom du champ est celui du paramètre : un seul nom, aucune traduction', async () => {
    await listerRessource('/api/absences', 1, 25, undefined, {
      champ: 'agentId',
      valeur: 'agent-1',
    })

    assert.ok(urls[0].endsWith('&agentId=agent-1'), urls[0])
  })

  test('la valeur est encodée', async () => {
    // Elle vient de la chaîne de requête de l'écran, donc de l'extérieur. Sans
    // encodage, un identifiant forgé contenant une esperluette ajouterait un
    // paramètre à la requête — « &taille=5000 », par exemple.
    await listerRessource('/api/sites', 1, 25, undefined, {
      champ: 'clientId',
      valeur: 'x&taille=5000',
    })

    assert.equal(urls[0], '/api/sites?page=1&taille=25&clientId=x%26taille%3D5000')
    assert.equal(urls[0].split('taille=').length, 2)
  })

  test('la pagination survit au filtre', async () => {
    await listerRessource('/api/contrats', 3, 10, undefined, {
      champ: 'siteId',
      valeur: 'site-1',
    })

    assert.equal(urls[0], '/api/contrats?page=3&taille=10&siteId=site-1')
  })
})

describe('listerToutPourChoix', () => {
  test('demande une seule page, au plafond', async () => {
    // C'est la limite assumée des listes déroulantes : au-delà de 200 lignes,
    // `Reference.tsx` prévient que le choix est tronqué plutôt que de laisser croire
    // qu'une ligne absente n'existe pas.
    await listerToutPourChoix('/api/clients')

    assert.equal(urls[0], `/api/clients?page=1&taille=${String(TailleMaximale)}`)
  })

  test('ne porte jamais de filtre', async () => {
    // Le choix d'un parent doit rester complet : le restreindre au filtre courant
    // empêcherait de rattacher une ligne à un autre parent.
    await listerToutPourChoix('/api/clients')

    assert.equal(urls[0].split('&').length, 2)
  })
})
