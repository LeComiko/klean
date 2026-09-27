import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router-dom'

import { aUnRole } from '../types/auth'
import { useAuth } from './contexte'
import './RouteProtegee.css'

interface Proprietes {
  /**
   * Rôles autorisés. Absent : tout compte connecté passe.
   *
   * Cette garde est un confort d'affichage, pas une sécurité : l'autorité reste
   * le serveur, qui répond 403. Un écran qui refuse poliment vaut mieux qu'un
   * écran vide, mais retirer cette garde n'ouvre aucun accès.
   */
  roles?: readonly string[]
  children: ReactNode
}

export function RouteProtegee({ roles, children }: Proprietes) {
  const { profil, restauration } = useAuth()
  const emplacement = useLocation()

  // Ni connecté ni déconnecté : le serveur n'a pas encore répondu. Rediriger
  // maintenant renverrait à la connexion quelqu'un dont la session est valide.
  if (restauration) {
    return (
      <output className="route-attente">Vérification de la session…</output>
    )
  }

  if (profil === null) {
    // `depuis` permet de revenir sur l'écran demandé après la connexion, ce qui
    // rend un F5 sur une page profonde indolore.
    return <Navigate to="/connexion" replace state={{ depuis: emplacement.pathname }} />
  }

  if (roles !== undefined && !aUnRole(profil, roles)) {
    return (
      <div className="route-refus">
        <h1 className="route-refus__titre">Accès refusé</h1>
        <p className="route-refus__texte">
          Cette section n'est pas ouverte à ton rôle&nbsp;
          {profil.roles.join(', ')}.
        </p>
      </div>
    )
  }

  return children
}
