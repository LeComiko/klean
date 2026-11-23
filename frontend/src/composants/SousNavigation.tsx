import { NavLink } from 'react-router-dom'

import type { Onglet } from '../layout/sections'
import './SousNavigation.css'

interface Proprietes {
  /**
   * Les onglets de la section, dans l'ordre de la chaîne du modèle, puis ses écrans
   * propres — `ongletsDe` dans `sections.ts`.
   */
  onglets: readonly Onglet[]
}

/**
 * Navigation à l'intérieur d'une section, le long de la **chaîne du modèle** :
 * Clients → Sites → Contrats → Prestations pour *Clients et sites*, Agents → Absences
 * pour *Agents*.
 *
 * L'alternative écartée était d'aligner huit liens dans la barre de navigation.
 * Elle aurait fait de la mise en route une chasse au trésor : rien n'y
 * indique qu'un site suppose un client, alors que l'ordre de cette barre le dit.
 *
 * Ne s'affiche pas quand la section n'ouvre qu'une ressource : un seul onglet
 * n'est pas une navigation, c'est un titre en double.
 */
export function SousNavigation({ onglets }: Proprietes) {
  if (onglets.length < 2) return null

  return (
    <nav className="sous-navigation" aria-label="Ressources de la section">
      <ul className="sous-navigation__liste">
        {onglets.map((onglet, index) => (
          <li key={onglet.chemin} className="sous-navigation__element">
            <NavLink
              to={onglet.chemin}
              className={({ isActive }) =>
                isActive ? 'sous-navigation__onglet sous-navigation__onglet--actif' : 'sous-navigation__onglet'
              }
              end
            >
              {onglet.libelle}
            </NavLink>
            {/* Le chevron dit la dépendance, pas seulement la suite : un site
                appartient à un client. Placé **après** son onglet : quand la liste
                revient à la ligne, une ligne finit par « › » au lieu d'en commencer
                une. Décoratif pour un lecteur d'écran, qui entend déjà l'ordre. */}
            {index < onglets.length - 1 && (
              <span className="sous-navigation__fleche" aria-hidden="true">
                ›
              </span>
            )}
          </li>
        ))}
      </ul>
    </nav>
  )
}
