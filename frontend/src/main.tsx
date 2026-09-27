import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'

import App from './App'
import './styles/tokens.css'

const racine = document.getElementById('root')

if (racine === null) throw new Error("L'élément #root est absent de index.html.")

createRoot(racine).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
