import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'

import App from './App'
import { apparenceEnregistree, appliquerApparence, stockageLocal } from './apparence/apparence'
import './styles/tokens.css'

// Le thème choisi dans *Mon compte*, avant le premier rendu (SPEC.md, étape 32). Pas
// plus tôt : un script en ligne dans index.html le poserait avant toute peinture,
// mais la politique de sécurité du contenu n'en autorise aucun. Qui a choisi le
// contraire de son système peut donc voir un instant le fond du système au
// chargement.
appliquerApparence(document.documentElement, apparenceEnregistree(stockageLocal()))

const racine = document.getElementById('root')

if (racine === null) throw new Error("L'élément #root est absent de index.html.")

createRoot(racine).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
