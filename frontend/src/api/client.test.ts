import assert from 'node:assert/strict'
import { beforeEach, describe, test } from 'node:test'

import { ErreurApi, api, surSessionPerdue } from './client.ts'

/**
 * Série sur l'enveloppe `fetch`. Aucun DOM : `client.ts` ne touche que
 * `globalThis.fetch` et `document.cookie`, tous deux remplaçables en trois
 * lignes. C'est délibéré — un module de session qui exigerait un navigateur pour
 * être éprouvé ne le serait jamais.
 *
 * Ce qui est gardé ici a été établi empiriquement à l'étape 13, contre le vrai
 * serveur :
 *
 * · le cookie `klean_csrf` **tourne à chaque rafraîchissement**. Le mémoriser au
 *   démarrage fait échouer toute écriture en 403, avec un message qui accuse le
 *   client de ne pas envoyer l'en-tête qu'il envoie ;
 * · deux rafraîchissements concurrents avec la même valeur de cookie ne perdent
 *   pas une requête, ils **détruisent la session** : l'un répond 200, l'autre
 *   déclenche la détection de rejeu, et le jeton tout neuf du gagnant meurt avec
 *   lui. La garde « un seul vol » est donc structurelle, pas une optimisation.
 */

interface Appel {
  url: string
  methode: string
  entetes: Record<string, string>
  corps: string | undefined
  identifiants: string | undefined
}

const appels: Appel[] = []
let repondre: (appel: Appel) => Response

const environnement = globalThis as unknown as {
  document: { cookie: string }
  fetch: (entree: string, options?: RequestInit) => Promise<Response>
}

environnement.document = { cookie: '' }

environnement.fetch = (entree, options) => {
  const appel: Appel = {
    url: entree,
    methode: options?.method ?? 'GET',
    entetes: { ...(options?.headers as Record<string, string> | undefined) },
    corps: typeof options?.body === 'string' ? options.body : undefined,
    identifiants: options?.credentials,
  }

  appels.push(appel)

  return Promise.resolve(repondre(appel))
}

function json(charge: unknown, statut = 200): Response {
  return new Response(JSON.stringify(charge), {
    status: statut,
    headers: { 'Content-Type': 'application/json; charset=utf-8' },
  })
}

/** Ce que renvoie le backend pour un `Unauthorized("…")` ou le refus CSRF. */
function texte(corps: string, statut: number): Response {
  return new Response(corps, {
    status: statut,
    headers: { 'Content-Type': 'text/plain; charset=utf-8' },
  })
}

function vide(statut: number): Response {
  return new Response(null, { status: statut })
}

function poserCookies(valeur: string): void {
  environnement.document.cookie = valeur
}

function trace(): string[] {
  return appels.map((appel) => `${appel.methode} ${appel.url}`)
}

beforeEach(() => {
  appels.length = 0
  poserCookies('')
  repondre = () => json({})
})

describe('client — cookies et CSRF', () => {
  test('chaque appel part avec les cookies de session', () => {
    // Sans credentials: 'include', le navigateur ne renvoie rien et tout répond
    // 401 — sans que rien n'indique pourquoi.
    return api.get('/api/planning?lundi=2026-09-07').then(() => {
      assert.equal(appels.length, 1)
      assert.equal(appels[0].identifiants, 'include')
    })
  })

  test('une lecture n’envoie pas d’en-tête CSRF', async () => {
    poserCookies('klean_csrf=JETON')

    await api.get('/api/agents')

    assert.equal(appels[0].entetes['X-Klean-CSRF'], undefined)
  })

  test('une écriture recopie le cookie klean_csrf dans l’en-tête', async () => {
    poserCookies('autre=1; klean_csrf=JETON; encore=2')

    await api.post('/api/interventions', { prestationId: 'x' })

    assert.equal(appels[0].methode, 'POST')
    assert.equal(appels[0].entetes['X-Klean-CSRF'], 'JETON')
    assert.equal(appels[0].entetes['Content-Type'], 'application/json')
    assert.equal(appels[0].corps, '{"prestationId":"x"}')
  })

  test('PUT et DELETE aussi', async () => {
    poserCookies('klean_csrf=JETON')

    await api.put('/api/interventions/1', { a: 1 })
    await api.supprimer('/api/interventions/1')

    assert.equal(appels[0].entetes['X-Klean-CSRF'], 'JETON')
    assert.equal(appels[1].entetes['X-Klean-CSRF'], 'JETON')
    assert.equal(appels[1].corps, undefined)
  })

  test('sans cookie, l’en-tête est absent plutôt que vide', async () => {
    // Un en-tête vide serait comparé au cookie par le middleware et donnerait un
    // 403 identique, mais un en-tête absent dit la vérité sur l’état du client.
    await api.post('/api/interventions', {})

    assert.equal(appels[0].entetes['X-Klean-CSRF'], undefined)
  })

  test('le cookie est relu à CHAQUE écriture, jamais mémorisé', async () => {
    // ⚠️ La garde centrale. Le serveur régénère klean_csrf à chaque
    // rafraîchissement ; une valeur capturée au démarrage de l’application serait
    // périmée dès le premier renouvellement.
    poserCookies('klean_csrf=PREMIER')
    await api.post('/api/interventions', {})

    poserCookies('klean_csrf=SECOND')
    await api.post('/api/absences', {})

    assert.equal(appels[0].entetes['X-Klean-CSRF'], 'PREMIER')
    assert.equal(appels[1].entetes['X-Klean-CSRF'], 'SECOND')
  })
})

describe('client — rafraîchissement sur 401', () => {
  test('un 401 déclenche un rafraîchissement puis rejoue la requête', async () => {
    poserCookies('klean_csrf=JETON')
    let premierPassage = true

    repondre = (appel) => {
      if (appel.url === '/api/auth/rafraichir') return json({ expiration: '2026-09-07T12:00:00Z' })
      if (premierPassage) {
        premierPassage = false
        return texte('Jeton expiré.', 401)
      }
      return json({ totalVacations: 17 })
    }

    const recu = await api.get<{ totalVacations: number }>('/api/planning')

    assert.deepEqual(recu, { totalVacations: 17 })
    assert.deepEqual(trace(), [
      'GET /api/planning',
      'POST /api/auth/rafraichir',
      'GET /api/planning',
    ])
  })

  test('le rejeu porte le NOUVEAU jeton CSRF, pas celui d’avant', async () => {
    // Le cas qui réunit les deux pièges : le rafraîchissement fait tourner le
    // cookie, donc la requête rejouée doit repartir avec la nouvelle valeur.
    // Mémoriser le jeton ferait échouer ce rejeu en 403 — un 401 transformé en
    // 403, le pire diagnostic possible.
    poserCookies('klean_csrf=AVANT')
    let premierPassage = true

    repondre = (appel) => {
      if (appel.url === '/api/auth/rafraichir') {
        poserCookies('klean_csrf=APRES')
        return json({ expiration: '2026-09-07T12:00:00Z' })
      }
      if (premierPassage) {
        premierPassage = false
        return texte('Jeton expiré.', 401)
      }
      return vide(204)
    }

    await api.post('/api/interventions', { agentId: null })

    assert.equal(appels[0].entetes['X-Klean-CSRF'], 'AVANT', 'la première tentative')
    assert.equal(appels[1].entetes['X-Klean-CSRF'], 'AVANT', 'le rafraîchissement lui-même')
    assert.equal(appels[2].entetes['X-Klean-CSRF'], 'APRES', 'le rejeu')
  })

  test('deux 401 concurrents ne déclenchent qu’UN rafraîchissement', async () => {
    // ⚠️ Vérifié contre le vrai serveur : deux rotations avec la même valeur de
    // cookie font que l’une réussit, l’autre est lue comme un rejeu, et TOUTES
    // les sessions du compte sont révoquées — y compris le jeton tout neuf de
    // celle qui avait réussi. C’est aussi ce qui protège du double montage des
    // effets de StrictMode en développement.
    poserCookies('klean_csrf=JETON')
    let rafraichissements = 0
    const dejaVues = new Set<string>()

    repondre = (appel) => {
      if (appel.url === '/api/auth/rafraichir') {
        rafraichissements++
        return json({ expiration: '2026-09-07T12:00:00Z' })
      }
      if (!dejaVues.has(appel.url)) {
        dejaVues.add(appel.url)
        return texte('Jeton expiré.', 401)
      }
      return json({ url: appel.url })
    }

    const [planning, agents] = await Promise.all([
      api.get<{ url: string }>('/api/planning'),
      api.get<{ url: string }>('/api/agents'),
    ])

    assert.equal(rafraichissements, 1, 'un second rafraîchissement couperait toutes les sessions')
    assert.deepEqual([planning.url, agents.url], ['/api/planning', '/api/agents'])
    assert.equal(appels.length, 5, '2 × 401, 1 rafraîchissement, 2 rejeux')
  })

  test('un seul rafraîchissement par requête : pas de seconde tentative', async () => {
    // Si le rejeu répond 401 à son tour, on abandonne. Boucler produirait une
    // rotation par tour, donc une détection de rejeu au deuxième.
    poserCookies('klean_csrf=JETON')

    repondre = (appel) =>
      appel.url === '/api/auth/rafraichir'
        ? json({ expiration: '2026-09-07T12:00:00Z' })
        : texte('Jeton expiré.', 401)

    await assert.rejects(
      () => api.get('/api/planning'),
      (echec: unknown) => echec instanceof ErreurApi && echec.statut === 401,
    )

    assert.deepEqual(trace(), [
      'GET /api/planning',
      'POST /api/auth/rafraichir',
      'GET /api/planning',
    ])
  })

  test('rafraîchissement en échec : la session est purgée, une seule fois', async () => {
    let purges = 0
    const desabonner = surSessionPerdue(() => {
      purges++
    })

    repondre = () => texte('Session révoquée.', 401)

    await assert.rejects(
      () => api.get('/api/planning'),
      (echec: unknown) => echec instanceof ErreurApi && echec.statut === 401,
    )

    assert.equal(purges, 1)
    assert.deepEqual(trace(), ['GET /api/planning', 'POST /api/auth/rafraichir'])

    desabonner()
  })

  test('un abonné désabonné n’est plus prévenu', async () => {
    let purges = 0
    surSessionPerdue(() => {
      purges++
    })()

    repondre = () => texte('Session révoquée.', 401)

    await assert.rejects(() => api.get('/api/planning'), ErreurApi)

    assert.equal(purges, 0)
  })

  test('un réseau coupé pendant le rafraîchissement ne boucle pas', async () => {
    repondre = (appel) => {
      if (appel.url === '/api/auth/rafraichir') throw new TypeError('Failed to fetch')
      return texte('Jeton expiré.', 401)
    }

    await assert.rejects(
      () => api.get('/api/planning'),
      (echec: unknown) => echec instanceof ErreurApi && echec.statut === 401,
    )

    assert.deepEqual(trace(), ['GET /api/planning', 'POST /api/auth/rafraichir'])
  })

  test('un mot de passe faux ne déclenche aucun rafraîchissement', async () => {
    // Sans cette exception, un identifiant erroné partirait en rafraîchissement,
    // échouerait, et l’écran afficherait « Session expirée » au lieu du message
    // du serveur. Le rafraîchissement masquerait la vraie cause.
    repondre = () => texte('Identifiants invalides.', 401)

    await assert.rejects(
      () => api.post('/api/auth/connexion', { email: 'x@y.z', motDePasse: 'faux' }),
      (echec: unknown) =>
        echec instanceof ErreurApi
        && echec.statut === 401
        && echec.message === 'Identifiants invalides.',
    )

    assert.deepEqual(trace(), ['POST /api/auth/connexion'])
  })

  test('rafraichir et deconnexion ne se rafraîchissent pas eux-mêmes', async () => {
    repondre = () => texte('Session révoquée.', 401)

    await assert.rejects(() => api.post('/api/auth/rafraichir'), ErreurApi)
    await assert.rejects(() => api.post('/api/auth/deconnexion'), ErreurApi)

    assert.deepEqual(trace(), ['POST /api/auth/rafraichir', 'POST /api/auth/deconnexion'])
  })

  test('un défi échu au second temps de la connexion ne déclenche aucun rafraîchissement', async () => {
    // Avant le code, aucune session n'existe (étape 23). Rafraîchir échouerait, et
    // l'écran afficherait « Session expirée » au lieu de « La connexion a expiré ».
    repondre = () => texte('La connexion a expiré ou n’est plus valable.', 401)

    await assert.rejects(
      () => api.post('/api/auth/connexion/code', { code: '123456' }),
      (echec: unknown) =>
        echec instanceof ErreurApi && echec.message === 'La connexion a expiré ou n’est plus valable.',
    )

    assert.deepEqual(trace(), ['POST /api/auth/connexion/code'])
  })
})

describe('client — corps de réponse', () => {
  test('204 ne tente pas d’analyser un corps', async () => {
    repondre = () => vide(204)

    assert.equal(await api.post<void>('/api/auth/deconnexion'), undefined)
  })

  test('200 avec un corps vide ne jette pas', async () => {
    repondre = () => new Response('', { status: 200 })

    assert.equal(await api.get<void>('/api/quelque-chose'), undefined)
  })
})

describe('client — messages d’erreur', () => {
  test('text/plain est rendu tel quel : le refus CSRF du middleware', async () => {
    repondre = () => texte('Jeton anti-CSRF absent ou invalide.', 403)

    await assert.rejects(
      () => api.post('/api/interventions', {}),
      (echec: unknown) =>
        echec instanceof ErreurApi
        && echec.statut === 403
        && echec.message === 'Jeton anti-CSRF absent ou invalide.',
    )
  })

  test('une chaîne JSON nue est rendue telle quelle', async () => {
    // Ce que produit un BadRequest("…") sérialisé en JSON.
    repondre = () => json('Prestation inconnue.', 400)

    await assert.rejects(
      () => api.post('/api/interventions', {}),
      (echec: unknown) => echec instanceof ErreurApi && echec.message === 'Prestation inconnue.',
    )
  })

  test('ProblemDetails : detail est préféré', async () => {
    repondre = () => json({ title: 'Bad Request', detail: 'La date de fin précède le début.' }, 400)

    await assert.rejects(
      () => api.post('/api/contrats', {}),
      (echec: unknown) =>
        echec instanceof ErreurApi && echec.message === 'La date de fin précède le début.',
    )
  })

  test('ProblemDetails : les erreurs de validation sont réunies', async () => {
    repondre = () =>
      json(
        {
          title: 'One or more validation errors occurred.',
          errors: { Email: ['Le champ Email est requis.'], Nom: ['Le champ Nom est requis.'] },
        },
        400,
      )

    await assert.rejects(
      () => api.post('/api/auth/comptes', {}),
      (echec: unknown) =>
        echec instanceof ErreurApi
        && echec.message === 'Le champ Email est requis. Le champ Nom est requis.',
    )
  })

  test('ProblemDetails : title en dernier recours', async () => {
    repondre = () => json({ title: 'Conflit.', status: 409 }, 409)

    await assert.rejects(
      () => api.supprimer('/api/agents/1'),
      (echec: unknown) =>
        echec instanceof ErreurApi && echec.statut === 409 && echec.message === 'Conflit.',
    )
  })

  test('une erreur serveur montre son identifiant de corrélation', async () => {
    // Le corps du 500 dit « communiquez l'identifiant de corrélation » : sans
    // l'identifiant à l'écran, la consigne est impossible à suivre.
    repondre = () =>
      json(
        {
          title: 'Erreur interne',
          detail: 'Une erreur inattendue a interrompu la requête.',
          status: 500,
          correlation: '8636344d0a3c1c0a01ae7cf0af64da27',
        },
        500,
      )

    await assert.rejects(
      () => api.get('/api/planning'),
      (echec: unknown) =>
        echec instanceof ErreurApi
        && echec.statut === 500
        && echec.message
          === 'Une erreur inattendue a interrompu la requête. '
            + 'Identifiant : 8636344d0a3c1c0a01ae7cf0af64da27',
    )
  })

  test('une erreur de saisie ne montre pas l’identifiant de corrélation', async () => {
    // Le serveur le joint à tout corps d'erreur ; sous « le champ est requis »,
    // ce ne serait que du bruit. L'utilisateur corrige sa saisie, il n'appelle
    // pas l'exploitant.
    repondre = () =>
      json(
        {
          title: 'One or more validation errors occurred.',
          errors: { Nom: ['Le champ Nom est requis.'] },
          correlation: '8636344d0a3c1c0a01ae7cf0af64da27',
        },
        400,
      )

    await assert.rejects(
      () => api.post('/api/clients', {}),
      (echec: unknown) =>
        echec instanceof ErreurApi && echec.message === 'Le champ Nom est requis.',
    )
  })

  test('un corps vide donne au moins le code', async () => {
    repondre = () => vide(500)

    await assert.rejects(
      () => api.get('/api/planning'),
      (echec: unknown) =>
        echec instanceof ErreurApi && echec.statut === 500 && echec.message === 'Erreur 500.',
    )
  })

  test('un JSON illisible n’écrase pas le corps brut', async () => {
    repondre = () =>
      new Response('{ceci n’est pas du json', {
        status: 400,
        headers: { 'Content-Type': 'application/json' },
      })

    await assert.rejects(
      () => api.get('/api/planning'),
      (echec: unknown) =>
        echec instanceof ErreurApi && echec.message === '{ceci n’est pas du json',
    )
  })
})
