import { useCallback, useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'

import { ErreurApi, api, surSessionPerdue } from '../api/client'
import type { EtapeConnexion, Profil, ReponseEtape, SaisieSecondFacteur, Session } from '../types/auth'
import { estEtape } from './secondFacteur'
import { ContexteAuth } from './contexte'
import type { ValeurAuth } from './contexte'

/** Marge avant l'expiration du jeton d'accès : rafraîchir avant, pas après. */
const MargeAvantExpiration = 60_000

/**
 * Détient la session courante.
 *
 * Il ne détient **aucun jeton** : les trois cookies sont posés par le serveur,
 * deux sont `HttpOnly`. Ce composant ne peut donc pas savoir tout seul qui est
 * connecté — c'est le serveur qui le lui dit, par `GET /api/auth/moi`. C'est
 * exactement ce qui fait tenir la persistance après F5 : rien n'est stocké côté
 * navigateur, donc rien n'est à restaurer d'un stockage local.
 */
export function FournisseurAuth({ children }: { children: ReactNode }) {
  const [profil, setProfil] = useState<Profil | null>(null)
  const [expiration, setExpiration] = useState<string | null>(null)
  const [restauration, setRestauration] = useState(true)

  // ---- Restauration au chargement ----
  // Si le cookie d'accès a expiré, le client tente un rafraîchissement et
  // rejoue l'appel : la session survit donc aussi à une nuit d'inactivité, dans
  // la limite des quatorze jours de la session absolue.
  useEffect(() => {
    let vivant = true

    api
      .get<Profil>('/api/auth/moi')
      .then((recu) => {
        if (vivant) setProfil(recu)
      })
      .catch(() => {
        if (vivant) setProfil(null)
      })
      .finally(() => {
        if (vivant) setRestauration(false)
      })

    return () => {
      vivant = false
    }
  }, [])

  // ---- Purge quand le client constate que la session est morte ----
  useEffect(
    () =>
      surSessionPerdue(() => {
        setProfil(null)
        setExpiration(null)
      }),
    [],
  )

  // ---- Rafraîchissement anticipé ----
  // `expiration` n'est connue qu'après une connexion ou un rafraîchissement :
  // `GET /api/auth/moi` renvoie un profil, pas une session. Après un F5 ce
  // minuteur est donc absent, et c'est le rafraîchissement sur 401 du client qui
  // assure la continuité. Les deux chemins existent, le second suffit seul.
  useEffect(() => {
    if (expiration === null) return

    const delai = Math.max(0, Date.parse(expiration) - Date.now() - MargeAvantExpiration)

    const minuteur = window.setTimeout(() => {
      api
        .post<Session>('/api/auth/rafraichir')
        .then((session) => {
          setProfil(session.profil)
          setExpiration(session.expiration)
        })
        .catch((erreur: unknown) => {
          // 401 : la session absolue de quatorze jours est écoulée, ou le jeton
          // a été révoqué. Inutile d'attendre le prochain écran pour le dire.
          if (erreur instanceof ErreurApi && erreur.statut === 401) {
            setProfil(null)
            setExpiration(null)
          }
        })
    }, delai)

    return () => {
      window.clearTimeout(minuteur)
    }
  }, [expiration])

  const adopterSession = useCallback((session: Session) => {
    setProfil(session.profil)
    setExpiration(session.expiration)
  }, [])

  const connecter = useCallback(
    async (email: string, motDePasse: string): Promise<EtapeConnexion | null> => {
      // L'erreur remonte volontairement : c'est la page de connexion qui possède
      // les trois états — chargement, erreur, données.
      const reponse = await api.post<Session | ReponseEtape>('/api/auth/connexion', { email, motDePasse })

      // 202 : aucune session, seulement un cookie de défi. Rien à adopter.
      if (estEtape(reponse)) return reponse.etape

      adopterSession(reponse)
      return null
    },
    [adopterSession],
  )

  const saisirCode = useCallback(
    async (saisie: SaisieSecondFacteur) => {
      adopterSession(await api.post<Session>('/api/auth/connexion/code', saisie))
    },
    [adopterSession],
  )

  const desactiverSecondFacteur = useCallback(
    async (code: string) => {
      const session = await api.post<Session | undefined>('/api/auth/second-facteur/desactiver', { code })

      // 204 : un compte obligé change de téléphone, et ses sessions sont fermées,
      // celle-ci comprise. Comme pour `deconnecterPartout`, on ne purge qu'au succès.
      if (session === undefined) {
        setProfil(null)
        setExpiration(null)
        return
      }

      adopterSession(session)
    },
    [adopterSession],
  )

  const deconnecter = useCallback(async () => {
    try {
      await api.post<void>('/api/auth/deconnexion')
    } finally {
      // Purge locale même si l'appel échoue : rien n'est pire qu'un bouton
      // « Déconnexion » qui laisse l'écran connecté.
      setProfil(null)
      setExpiration(null)
    }
  }, [])

  const changerMotDePasse = useCallback(
    async (motDePasseActuel: string, nouveauMotDePasse: string) => {
      // Le serveur révoque les sessions du compte puis rouvre celle-ci : la réponse
      // porte donc une session neuve, exactement comme une connexion. Ne pas la
      // recueillir laisserait le minuteur de rafraîchissement sur l'ancienne
      // expiration — sans rien casser, les cookies étant à jour, mais en
      // rafraîchissant au mauvais moment.
      const session = await api.post<Session>('/api/auth/changer-mot-de-passe', {
        motDePasseActuel,
        nouveauMotDePasse,
      })

      setProfil(session.profil)
      setExpiration(session.expiration)
    },
    [],
  )

  const deconnecterPartout = useCallback(async () => {
    // Pas de `finally` ici, et c'est le contraire de `deconnecter` : si l'appel
    // échoue, les autres appareils sont restés ouverts et il ne faut pas le taire.
    // L'écran affiche l'erreur. Un vrai 401 est de toute façon purgé par le client,
    // qui prévient tous les abonnés.
    await api.post<void>('/api/auth/deconnexion-totale')

    setProfil(null)
    setExpiration(null)
  }, [])

  const valeur = useMemo<ValeurAuth>(
    () => ({
      profil,
      restauration,
      connecter,
      saisirCode,
      adopterSession,
      desactiverSecondFacteur,
      deconnecter,
      changerMotDePasse,
      deconnecterPartout,
    }),
    [
      profil,
      restauration,
      connecter,
      saisirCode,
      adopterSession,
      desactiverSecondFacteur,
      deconnecter,
      changerMotDePasse,
      deconnecterPartout,
    ],
  )

  return <ContexteAuth.Provider value={valeur}>{children}</ContexteAuth.Provider>
}
