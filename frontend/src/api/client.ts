/**
 * Enveloppe `fetch` unique du projet. Aucun composant n'appelle `fetch`
 * directement : tout ce qui suit serait alors à réécrire à chaque appel.
 *
 * Elle porte quatre responsabilités, toutes imposées par le durcissement de
 * l'étape 12 côté serveur :
 *
 * 1. `credentials: 'include'` sur chaque requête. Les trois cookies de session
 *    sont posés par le serveur, deux sont `HttpOnly` — sans cette option, le
 *    navigateur ne les renvoie pas et tout répond 401.
 * 2. Recopie du cookie `klean_csrf` dans l'en-tête `X-Klean-CSRF` sur toute
 *    écriture. Le serveur exige la double soumission dès qu'un cookie de
 *    session accompagne une méthode non sûre, et répond 403 sinon.
 * 3. Rafraîchissement transparent sur 401, une seule fois, puis rejeu de la
 *    requête d'origine.
 * 4. Purge de la session si le rafraîchissement échoue.
 *
 * Aucun jeton ne transite par ce fichier. Il n'y a rien à stocker, rien à
 * mettre dans un en-tête `Authorization` : le serveur ne l'accepte plus.
 */

const CookieCsrf = 'klean_csrf'
const EnteteCsrf = 'X-Klean-CSRF'

/** Les méthodes que le serveur considère sans effet, donc dispensées de CSRF. */
const MethodesSansEffet = new Set(['GET', 'HEAD', 'OPTIONS', 'TRACE'])

/**
 * Chemins sur lesquels un 401 ne doit jamais déclencher de rafraîchissement.
 * `rafraichir` s'appellerait lui-même sans fin ; `connexion` répond 401 sur un
 * mot de passe faux, et tenter un rafraîchissement là dessus masquerait le
 * message d'erreur derrière une purge de session.
 */
const SansRafraichissement = new Set([
  '/api/auth/rafraichir',
  '/api/auth/connexion',
  // Le second temps de la connexion (étape 23) : il n'y a pas encore de session à
  // rafraîchir, et son 401 dit « recommencer la connexion », pas « session morte ».
  '/api/auth/connexion/code',
  // La première connexion (étape 24), pour la même raison : un défi, pas une session.
  '/api/auth/connexion/mot-de-passe',
  '/api/auth/deconnexion',
])

/** Erreur portant le code HTTP, pour que l'appelant puisse distinguer les cas. */
export class ErreurApi extends Error {
  readonly statut: number

  constructor(statut: number, message: string) {
    super(message)
    this.name = 'ErreurApi'
    this.statut = statut
  }
}

// ---------------------------------------------------------------------------
// Cookie CSRF
// ---------------------------------------------------------------------------

/**
 * ⚠️ Relu à chaque écriture, jamais mémorisé.
 *
 * Le serveur **régénère** `klean_csrf` à chaque rafraîchissement. Une valeur
 * capturée au démarrage de l'application serait périmée dès le premier
 * renouvellement, et toutes les écritures partiraient en 403 — avec un message
 * qui accuse le client de ne pas envoyer l'en-tête alors qu'il l'envoie.
 */
function jetonCsrf(): string | null {
  const prefixe = `${CookieCsrf}=`

  for (const morceau of document.cookie.split(';')) {
    const candidat = morceau.trimStart()

    if (candidat.startsWith(prefixe)) {
      return decodeURIComponent(candidat.slice(prefixe.length))
    }
  }

  return null
}

// ---------------------------------------------------------------------------
// Session perdue
// ---------------------------------------------------------------------------

type Abonne = () => void

const abonnes = new Set<Abonne>()

/**
 * Abonne un observateur à la perte de session. `AuthContext` s'en sert pour
 * vider le profil ; la redirection vers la connexion est alors rendue par
 * `RouteProtegee`, sans rechargement de page — un `window.location` perdrait
 * l'état de l'écran et ferait clignoter l'application.
 *
 * Retourne la fonction de désabonnement.
 */
export function surSessionPerdue(abonne: Abonne): () => void {
  abonnes.add(abonne)

  return () => {
    abonnes.delete(abonne)
  }
}

function signalerSessionPerdue(): void {
  for (const abonne of abonnes) abonne()
}

// ---------------------------------------------------------------------------
// Rafraîchissement, en un seul vol
// ---------------------------------------------------------------------------

let rafraichissementEnCours: Promise<boolean> | null = null

async function executerRafraichissement(): Promise<boolean> {
  try {
    const entetes: Record<string, string> = {}
    const jeton = jetonCsrf()

    if (jeton !== null) entetes[EnteteCsrf] = jeton

    const reponse = await fetch('/api/auth/rafraichir', {
      method: 'POST',
      credentials: 'include',
      headers: entetes,
    })

    return reponse.ok
  } catch {
    // Réseau coupé : ce n'est pas une session perdue, mais on ne peut pas
    // distinguer ici. L'appelant remontera l'échec.
    return false
  }
}

/**
 * ⚠️ Un seul rafraîchissement à la fois, et c'est structurel, pas une
 * optimisation.
 *
 * Le serveur fait **tourner** le jeton de rafraîchissement à chaque usage et
 * traite un jeton déjà consommé comme un rejeu : il révoque alors **toutes** les
 * sessions du compte. Deux requêtes qui expirent en même temps enverraient donc
 * deux fois la même valeur de cookie — la seconde serait lue comme un vol, et
 * déconnecterait l'utilisateur partout.
 *
 * C'est aussi ce qui protège du double montage des effets de `StrictMode` en
 * développement : les deux appels de restauration se replient sur la même
 * promesse, donc sur une seule rotation.
 */
function rafraichir(): Promise<boolean> {
  rafraichissementEnCours ??= executerRafraichissement().then((abouti) => {
    rafraichissementEnCours = null
    return abouti
  })

  return rafraichissementEnCours
}

// ---------------------------------------------------------------------------
// Messages d'erreur
// ---------------------------------------------------------------------------

/**
 * Extrait un message lisible. Le backend parle trois dialectes :
 * une chaîne JSON nue (`Unauthorized("Identifiants invalides.")`), un
 * `ProblemDetails` de la validation de modèle, et du `text/plain` pour le
 * refus CSRF du middleware.
 *
 * Les messages d'Identity traversent l'API en texte libre — et en français,
 * grâce au descripteur de l'étape 12. Il n'y a rien à traduire ici, et rien à
 * reformuler : réécrire le message côté écran le ferait diverger du serveur.
 */
async function messageDErreur(reponse: Response): Promise<string> {
  const brut = await reponse.text().catch(() => '')

  if (brut === '') return `Erreur ${String(reponse.status)}.`

  if (!(reponse.headers.get('Content-Type') ?? '').includes('json')) return brut

  let charge: unknown

  try {
    charge = JSON.parse(brut)
  } catch {
    return brut
  }

  if (typeof charge === 'string') return charge

  if (typeof charge !== 'object' || charge === null) return brut

  const details = charge as {
    detail?: unknown
    title?: unknown
    errors?: unknown
    correlation?: unknown
  }

  const message = messageDuProbleme(details) ?? brut

  // Une erreur serveur demande à l'utilisateur de communiquer l'identifiant de
  // corrélation à l'exploitant (SPEC.md étape 18) : encore faut-il qu'il le voie.
  // Sur un 4xx, non — le serveur le joint aussi, mais l'utilisateur corrige sa
  // saisie, il n'appelle personne.
  if (
    reponse.status >= 500
    && typeof details.correlation === 'string'
    && details.correlation !== ''
  ) {
    return `${message} Identifiant : ${details.correlation}`
  }

  return message
}

function messageDuProbleme(details: {
  detail?: unknown
  title?: unknown
  errors?: unknown
}): string | undefined {
  if (typeof details.detail === 'string' && details.detail !== '') {
    return details.detail
  }

  // ProblemDetails de validation : { errors: { Champ: ["message", …] } }
  if (typeof details.errors === 'object' && details.errors !== null) {
    const messages = Object.values(details.errors as Record<string, unknown>)
      .flat()
      .filter((valeur): valeur is string => typeof valeur === 'string')

    if (messages.length > 0) return messages.join(' ')
  }

  if (typeof details.title === 'string' && details.title !== '') {
    return details.title
  }

  return undefined
}

// ---------------------------------------------------------------------------
// Appel
// ---------------------------------------------------------------------------

interface Options {
  corps?: unknown
  /**
   * Un corps envoyé tel quel, sous son propre type — la photo d'une anomalie
   * (SPEC.md, étape 25). Exclusif de `corps`, que `JSON.stringify` transformerait en
   * `{}`.
   */
  brut?: Blob
  signal?: AbortSignal
}

function envoyer(methode: string, chemin: string, options: Options): Promise<Response> {
  const entetes: Record<string, string> = {}

  if (options.brut !== undefined) entetes['Content-Type'] = options.brut.type
  else if (options.corps !== undefined) entetes['Content-Type'] = 'application/json'

  if (!MethodesSansEffet.has(methode)) {
    const jeton = jetonCsrf()

    if (jeton !== null) entetes[EnteteCsrf] = jeton
  }

  return fetch(chemin, {
    method: methode,
    credentials: 'include',
    headers: entetes,
    body: options.brut ?? (options.corps === undefined ? undefined : JSON.stringify(options.corps)),
    signal: options.signal,
  })
}

/** Lit le corps d'une réponse réussie. */
type Lecture<T> = (reponse: Response) => Promise<T>

async function lireJson<T>(reponse: Response): Promise<T> {
  // Un corps vide rend `undefined`, ce qui couvre le 204 d'une suppression ou
  // d'une déconnexion sans avoir à tester le code séparément. Une branche
  // `status === 204` explicite a existé ici : le sabotage a montré qu'on pouvait
  // la retirer sans qu'aucune série ne rougisse, parce qu'elle était redondante.
  const brut = await reponse.text()

  return (brut === '' ? undefined : JSON.parse(brut)) as T
}

async function appeler<T>(
  methode: string,
  chemin: string,
  options: Options = {},
  lire: Lecture<T> = lireJson,
): Promise<T> {
  let reponse = await envoyer(methode, chemin, options)

  if (reponse.status === 401 && !SansRafraichissement.has(chemin)) {
    if (await rafraichir()) {
      // Le cookie CSRF a tourné pendant le rafraîchissement : `envoyer` le
      // relit, c'est tout l'objet de ne pas le mémoriser.
      reponse = await envoyer(methode, chemin, options)
    } else {
      signalerSessionPerdue()
      throw new ErreurApi(401, 'Session expirée. Reconnecte-toi.')
    }
  }

  if (reponse.status === 401) {
    signalerSessionPerdue()
    throw new ErreurApi(401, await messageDErreur(reponse))
  }

  if (!reponse.ok) {
    throw new ErreurApi(reponse.status, await messageDErreur(reponse))
  }

  return lire(reponse)
}

export const api = {
  get: <T>(chemin: string, options?: Options): Promise<T> => appeler<T>('GET', chemin, options),

  post: <T>(chemin: string, corps?: unknown, signal?: AbortSignal): Promise<T> =>
    appeler<T>('POST', chemin, { corps, signal }),

  put: <T>(chemin: string, corps?: unknown, signal?: AbortSignal): Promise<T> =>
    appeler<T>('PUT', chemin, { corps, signal }),

  supprimer: <T>(chemin: string, signal?: AbortSignal): Promise<T> =>
    appeler<T>('DELETE', chemin, { signal }),

  /**
   * Un `POST` dont le corps part tel quel, sous le type du `Blob`. La réponse reste du
   * JSON. Le CSRF et le rafraîchissement sont ceux de toute écriture : c'est la raison
   * de passer par ici plutôt que par `fetch`.
   */
  postBrut: <T>(chemin: string, brut: Blob, signal?: AbortSignal): Promise<T> =>
    appeler<T>('POST', chemin, { brut, signal }),

  /**
   * Une lecture binaire — une photo. ⚠️ Pas une balise `<img src="/api/…">` : elle
   * enverrait bien les cookies, mais un jeton d'accès expiré y donnerait une image
   * cassée, sans rafraîchissement ni message. L'écran en fait une URL `blob:`.
   */
  getBlob: (chemin: string, signal?: AbortSignal): Promise<Blob> =>
    appeler<Blob>('GET', chemin, { signal }, (reponse) => reponse.blob()),
}
