import { useState } from 'react'
import { NavLink } from 'react-router-dom'

import { useAuth } from '../auth/contexte'
import { aUnRole } from '../types/auth'
import { CheminCompte, Sections } from './sections'
import './Navigation.css'

export function Navigation() {
  const { profil, deconnecter } = useAuth()
  const [sortie, setSortie] = useState(false)

  if (profil === null) return null

  const visibles = Sections.filter(
    (section) => section.roles === undefined || aUnRole(profil, section.roles),
  )

  async function quitter() {
    setSortie(true)

    try {
      await deconnecter()
    } finally {
      setSortie(false)
    }
  }

  return (
    <nav className="navigation" aria-label="Sections">
      <p className="navigation__marque" aria-hidden="true">
        K
      </p>

      <ul className="navigation__liste">
        {visibles.map((section) => (
          <li key={section.chemin}>
            {/* NavLink pose aria-current="page" : l'état actif est annoncé, il
                ne repose donc pas sur la seule couleur de fond. Le liséré
                gauche en est la redondance visuelle. */}
            <NavLink
              to={section.chemin}
              className={({ isActive }) =>
                isActive ? 'navigation__pastille navigation__pastille--active' : 'navigation__pastille'
              }
              title={section.libelle}
            >
              <span className="navigation__code" aria-hidden="true">{section.pastille}</span>
              {/* Lu par le lecteur d'écran à toute largeur, affiché à côté du code
                  quand la place le permet (Navigation.css, étape 31). */}
              <span className="navigation__libelle">{section.libelle}</span>
            </NavLink>
          </li>
        ))}
      </ul>

      {/* Le compte, au pied de la barre et non dans la liste des pastilles : ce
          n'est pas une fonction du produit mais le compte de celui qui l'utilise,
          il est ouvert à tout rôle, et il ne doit jamais devenir la destination
          d'arrivée d'une connexion. Voir `CheminCompte` dans sections.ts.

          Une icône et non deux initiales : le prénom est facultatif à la création
          d'un compte, et « A. » pour tout un service de nettoyage ne distingue
          rien. */}
      <NavLink
        to={CheminCompte}
        className={({ isActive }) =>
          isActive
            ? 'navigation__pastille navigation__compte navigation__pastille--active'
            : 'navigation__pastille navigation__compte'
        }
        title="Mon compte"
      >
        {/* SVG écrit à la main : aucune bibliothèque d'icônes n'est autorisée. */}
        <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true" focusable="false">
          <circle cx="12" cy="8" r="3.5" fill="none" stroke="currentColor" strokeWidth="2" />
          <path
            d="M5 20c0-3.3 3.1-5.5 7-5.5s7 2.2 7 5.5"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
          />
        </svg>
        <span className="navigation__libelle">Mon compte</span>
      </NavLink>

      <button
        className="navigation__pastille navigation__sortie"
        type="button"
        onClick={() => void quitter()}
        disabled={sortie}
        title="Déconnexion"
      >
        {/* SVG écrit à la main : aucune bibliothèque d'icônes n'est autorisée. */}
        <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true" focusable="false">
          <path
            d="M10 4H6.5A2.5 2.5 0 0 0 4 6.5v11A2.5 2.5 0 0 0 6.5 20H10"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
          />
          <path
            d="M15.5 8.5 19 12l-3.5 3.5M19 12H9.5"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
        </svg>
        <span className="navigation__libelle">Déconnexion</span>
      </button>
    </nav>
  )
}
