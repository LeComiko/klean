import { useCallback, useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'

import { ErreurApi, api, surSessionPerdue } from '../api/client'
import type { Profil, Session } from '../types/auth'
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

  const connecter = useCallback(async (email: string, motDePasse: string) => {
    // L'erreur remonte volontairement : c'est la page de connexion qui possède
    // les trois états — chargement, erreur, données.
    const session = await api.post<Session>('/api/auth/connexion', { email, motDePasse })

    setProfil(session.profil)
    setExpiration(session.expiration)
  }, [])

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

  const valeur = useMemo<ValeurAuth>(
    () => ({ profil, restauration, connecter, deconnecter }),
    [profil, restauration, connecter, deconnecter],
  )

  return <ContexteAuth.Provider value={valeur}>{children}</ContexteAuth.Provider>
}
