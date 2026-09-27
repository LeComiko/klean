import { Outlet } from 'react-router-dom'

import { Navigation } from './Navigation'
import './Disposition.css'

/**
 * Charpente des écrans authentifiés : la barre latérale de 64 px, puis la zone
 * de contenu. Les écrans se rendent dans l'`Outlet`.
 */
export function Disposition() {
  return (
    <div className="disposition">
      <Navigation />
      <main className="disposition__contenu">
        <Outlet />
      </main>
    </div>
  )
}
