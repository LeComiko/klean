import type { ReactNode } from 'react'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'

import { FournisseurAuth } from './auth/AuthContext'
import { useAuth } from './auth/contexte'
import { RouteProtegee } from './auth/RouteProtegee'
import { Disposition } from './layout/Disposition'
import { CheminCompte, Sections, premiereSectionOuverte } from './layout/sections'
import type { Section } from './layout/sections'
import { Anomalies } from './pages/Anomalies'
import { Chantier } from './pages/Chantier'
import { Compte } from './pages/Compte'
import { Comptes } from './pages/Comptes'
import { Connexion } from './pages/Connexion'
import { MaJournee } from './pages/MaJournee'
import { Planning } from './pages/Planning'
import { Prevision } from './pages/Prevision'
import { Reference } from './pages/Reference'
import { Stock } from './pages/Stock'
import { descriptionDe } from './reference/descriptions'

/**
 * Écrans écrits à la main, par chemin. Ce qui n'y figure pas tombe sur `Chantier`.
 *
 * Deux sortes : une section **sans** ressources — le planning —, et les écrans propres
 * d'une section à ressources (`ecrans` dans `sections.ts`), qui reçoivent leur section
 * pour la sous-navigation. Les ressources, elles, sont rendues par `Reference`.
 */
const Ecrans: Record<string, (section: Section) => ReactNode> = {
  '/planning': () => <Planning />,
  '/anomalies': () => <Anomalies />,
  '/ma-journee': () => <MaJournee />,
  '/stock': (section) => <Stock section={section} />,
  '/prevision': (section) => <Prevision section={section} />,
  '/comptes': (section) => <Comptes section={section} />,
}

function ecranDe(section: Section, chemin: string, libelle: string): ReactNode {
  return Ecrans[chemin]?.(section) ?? <Chantier titre={libelle} />
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
      routes.push({
        chemin: section.chemin,
        roles: section.roles,
        element: ecranDe(section, section.chemin, section.libelle),
      })
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
            section={section}
          />
        ),
      })
    }

    // Les écrans propres, gardés par **leurs** rôles : la prévision est fermée au chef
    // d'équipe, à qui la section reste ouverte.
    for (const ecran of section.ecrans ?? []) {
      routes.push({
        chemin: ecran.chemin,
        roles: ecran.roles,
        element: ecranDe(section, ecran.chemin, ecran.libelle),
      })
    }
  }

  return routes
}

/**
 * Destination d'arrivée, **dérivée** de la liste des sections plutôt que codée en
 * dur : la première ouverte au profil. Un rôle interne atterrit sur Planning, un
 * `ClientSyndic` sur Clients, un `Agent` sur Ma journée depuis l'étape 25.
 *
 * Repli sur `/planning` quand **aucune** section n'est ouverte — un compte sans rôle :
 * `RouteProtegee` y affiche un refus qui nomme le rôle.
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

            {/* Le seul écran qui ne vient pas de `Sections`, et le seul monté sans
                `RouteProtegee` propre : il est ouvert à tout compte connecté, donc la
                garde de la route de disposition suffit — en ajouter une seconde sans
                rôles ne fermerait rien de plus.

                Il est atteignable par tout rôle, l'agent compris, depuis le pied de la
                barre latérale : c'est le compte, pas une fonction du produit. */}
            <Route path={CheminCompte} element={<Compte />} />
          </Route>

          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </FournisseurAuth>
    </BrowserRouter>
  )
}
