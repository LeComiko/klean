import type { ReactNode } from 'react'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'

import { FournisseurAuth } from './auth/AuthContext'
import { useAuth } from './auth/contexte'
import { RouteProtegee } from './auth/RouteProtegee'
import { Disposition } from './layout/Disposition'
import { Sections, premiereSectionOuverte } from './layout/sections'
import type { Section } from './layout/sections'
import { Chantier } from './pages/Chantier'
import { Connexion } from './pages/Connexion'
import { Planning } from './pages/Planning'
import { Reference } from './pages/Reference'
import { descriptionDe } from './reference/descriptions'

/**
 * Écrans réels, par chemin. Ce qui n'y figure pas tombe sur `Chantier`.
 *
 * Ne concerne plus que les sections **sans** ressources : celles qui en portent
 * sont rendues par `Reference`, piloté par sa description.
 */
const Ecrans: Record<string, ReactNode> = {
  '/planning': <Planning />,
}

function ecranDe(section: Section): ReactNode {
  return Ecrans[section.chemin] ?? <Chantier titre={section.libelle} />
}

/** Une route montée sous la disposition. */
interface RouteEcran {
  chemin: string
  roles?: readonly string[]
  element: ReactNode
}

/**
 * Les routes, **dérivées** des sections et de leurs ressources.
 *
 * Une section sans ressources donne une route ; une section qui en porte donne une
 * route par ressource, gardée par les rôles de **lecture de cette ressource** et
 * non par ceux de la section. C'est plus fin, et c'est la seule façon de ne pas
 * ouvrir une liste à qui la section laisse entrer.
 *
 * ⚠️ Aucune route n'est créée pour le `chemin` d'une section à ressources : il vaut
 * celui de sa première, qui la crée déjà. Deux `<Route>` sur le même chemin
 * laisseraient la seconde inatteignable, sans erreur.
 */
function routesDesEcrans(): RouteEcran[] {
  const routes: RouteEcran[] = []

  for (const section of Sections) {
    if (section.ressources === undefined) {
      routes.push({ chemin: section.chemin, roles: section.roles, element: ecranDe(section) })
      continue
    }

    for (const cle of section.ressources) {
      const description = descriptionDe(cle)

      if (description === undefined) continue

      routes.push({
        chemin: description.chemin,
        roles: description.rolesLecture,
        element: (
          <Reference
            // La `key` remonte la page quand on passe de Clients à Sites : l'état
            // de pagination, de formulaire et de confirmation appartient à une
            // ressource, et le traîner d'une liste à l'autre afficherait « page 3 »
            // sur une liste qui n'en a qu'une.
            key={description.cle}
            description={description}
            fratrie={section.ressources}
          />
        ),
      })
    }
  }

  return routes
}

/**
 * Destination d'arrivée, **dérivée** de la liste des sections plutôt que codée en
 * dur : la première ouverte au profil. Un rôle interne atterrit sur Planning, un
 * `ClientSyndic` sur Clients.
 *
 * Repli sur `/planning` quand **aucune** section n'est ouverte, c'est-à-dire pour
 * le rôle `Agent` : `RouteProtegee` y affiche un refus qui nomme le rôle. C'est
 * l'état documenté du produit — l'étape 8 de `SPEC.md` dit que ce rôle est
 * « sécurisé, pas servi », son parcours suppose une application mobile et des
 * endpoints conçus pour elle.
 */
function Accueil() {
  const { profil } = useAuth()

  if (profil === null) return <Navigate to="/connexion" replace />

  const premiere = premiereSectionOuverte(profil.roles)

  return <Navigate to={premiere?.chemin ?? '/planning'} replace />
}

export default function App() {
  return (
    <BrowserRouter>
      {/* Le fournisseur est à l'intérieur du routeur : RouteProtegee redirige
          par <Navigate>, ce qui exige un routeur au-dessus de lui. */}
      <FournisseurAuth>
        <Routes>
          <Route path="/connexion" element={<Connexion />} />

          {/* Route de disposition sans chemin : elle garde l'accès une seule
              fois pour toutes les sections, et pose la charpente. */}
          <Route
            element={
              <RouteProtegee>
                <Disposition />
              </RouteProtegee>
            }
          >
            <Route index element={<Accueil />} />

            {routesDesEcrans().map((route) => (
              <Route
                key={route.chemin}
                path={route.chemin}
                element={<RouteProtegee roles={route.roles}>{route.element}</RouteProtegee>}
              />
            ))}
          </Route>

          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </FournisseurAuth>
    </BrowserRouter>
  )
}
