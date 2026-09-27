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

/**
 * Écrans réels, par chemin. Ce qui n'y figure pas tombe sur `Chantier`.
 *
 * C'est la couture des étapes suivantes : l'étape 14 ajoute ici
 * `'/planning': <Planning />`, et rien d'autre ne bouge.
 */
const Ecrans: Record<string, ReactNode> = {
  '/planning': <Planning />,
}

function ecranDe(section: Section): ReactNode {
  return Ecrans[section.chemin] ?? <Chantier titre={section.libelle} />
}

/**
 * Destination d'arrivée, **dérivée** de la liste des sections plutôt que codée en
 * dur : la première ouverte au profil. Un rôle interne atterrit sur Planning, un
 * `ClientSyndic` sur Sites.
 *
 * La destination était `/tableau-de-bord`, retiré avec sa pastille. La coder en
 * dur avait un défaut qui ne se voyait pas tant que TB accueillait tout le monde :
 * elle pouvait désigner une section fermée au profil qui arrive.
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

            {Sections.map((section) => (
              <Route
                key={section.chemin}
                path={section.chemin}
                element={<RouteProtegee roles={section.roles}>{ecranDe(section)}</RouteProtegee>}
              />
            ))}
          </Route>

          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </FournisseurAuth>
    </BrowserRouter>
  )
}
