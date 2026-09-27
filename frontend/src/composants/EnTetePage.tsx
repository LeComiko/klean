import type { ReactNode } from 'react'

import './EnTetePage.css'

interface Proprietes {
  titre: string
  sousTitre?: string
  /** Boutons ou sélecteurs propres à l'écran, alignés à droite du titre. */
  actions?: ReactNode
}

/**
 * En-tête commun à tous les écrans : composant de présentation pur, il ne fait
 * aucun appel réseau — c'est la page qui orchestre.
 */
export function EnTetePage({ titre, sousTitre, actions }: Proprietes) {
  return (
    <header className="en-tete-page">
      <div className="en-tete-page__textes">
        <h1 className="en-tete-page__titre">{titre}</h1>
        {sousTitre !== undefined && <p className="en-tete-page__sous-titre">{sousTitre}</p>}
      </div>
      {actions !== undefined && <div className="en-tete-page__actions">{actions}</div>}
    </header>
  )
}
