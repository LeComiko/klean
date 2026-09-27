import { NavLink } from 'react-router-dom'

import type { DescriptionRessource } from '../types/reference'
import './SousNavigation.css'

interface Proprietes {
  /** Les ressources de la section, dans l'ordre de la chaîne du modèle. */
  ressources: readonly DescriptionRessource[]
}

/**
 * Navigation à l'intérieur d'une section, le long de la **chaîne du modèle** :
 * Clients → Sites → Contrats → Prestations pour `SI`, Agents → Absences pour `AG`.
 *
 * L'alternative écartée était d'aligner huit pastilles dans la barre latérale de
 * 64 px. Elle aurait fait de la mise en route une chasse au trésor : rien n'y
 * indique qu'un site suppose un client, alors que l'ordre de cette barre le dit.
 *
 * Ne s'affiche pas quand la section n'ouvre qu'une ressource : un seul onglet
 * n'est pas une navigation, c'est un titre en double.
 */
export function SousNavigation({ ressources }: Proprietes) {
  if (ressources.length < 2) return null

  return (
    <nav className="sous-navigation" aria-label="Ressources de la section">
      <ul className="sous-navigation__liste">
        {ressources.map((ressource, index) => (
          <li key={ressource.cle} className="sous-navigation__element">
            {/* Le chevron dit la dépendance, pas seulement la suite : un site
                appartient à un client. Décoratif pour un lecteur d'écran, qui
                entend déjà l'ordre de la liste. */}
            {index > 0 && (
              <span className="sous-navigation__fleche" aria-hidden="true">
                ›
              </span>
            )}
            <NavLink
              to={ressource.chemin}
              className={({ isActive }) =>
                isActive ? 'sous-navigation__onglet sous-navigation__onglet--actif' : 'sous-navigation__onglet'
              }
              end
            >
              {ressource.libelle}
            </NavLink>
          </li>
        ))}
      </ul>
    </nav>
  )
}
