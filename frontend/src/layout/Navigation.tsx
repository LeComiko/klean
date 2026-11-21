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

      {/* Les sections en mots, sur une ligne (étape 33). Sous la largeur qu'il faut,
          la liste défile à l'horizontale : un nom n'est jamais tronqué (§12). */}
      <ul className="navigation__liste">
        {visibles.map((section) => (
          <li key={section.chemin}>
            {/* NavLink pose aria-current="page" : l'état actif est annoncé, il ne
                repose donc pas sur la seule couleur de fond. La graisse en est la
                redondance visuelle. */}
            <NavLink
              to={section.chemin}
              className={({ isActive }) =>
                isActive ? 'navigation__lien navigation__lien--actif' : 'navigation__lien'
              }
            >
              {section.libelle}
            </NavLink>
          </li>
        ))}
      </ul>

      {/* Le compte, à droite et hors de la liste des sections : ce n'est pas une
          fonction du produit mais le compte de celui qui l'utilise, il est ouvert à
          tout rôle, et il ne doit jamais devenir la destination d'arrivée d'une
          connexion. Voir `CheminCompte` dans sections.ts. */}
      <div className="navigation__session">
        <NavLink
          to={CheminCompte}
          className={({ isActive }) =>
            isActive ? 'navigation__lien navigation__lien--actif' : 'navigation__lien'
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
          <span className="navigation__texte">Mon compte</span>
        </NavLink>

        <button
          className="navigation__lien navigation__sortie"
          type="button"
          onClick={() => void quitter()}
          disabled={sortie}
          title="Déconnexion"
        >
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
          <span className="navigation__texte">Déconnexion</span>
        </button>
      </div>
    </nav>
  )
}
