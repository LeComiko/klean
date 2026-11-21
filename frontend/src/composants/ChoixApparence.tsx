import { useState } from 'react'

import {
  apparenceEnregistree,
  Apparences,
  appliquerApparence,
  enregistrerApparence,
  LibellesApparence,
  stockageLocal,
} from '../apparence/apparence'
import type { Apparence } from '../apparence/apparence'
import './ChoixApparence.css'

interface Proprietes {
  /** L'identifiant du titre de la carte qui l'entoure, qui nomme le groupe. */
  idTitre: string
}

/**
 * Automatique, Clair ou Sombre (SPEC.md, étape 32). Trois boutons radio natifs : le
 * clavier, le lecteur d'écran et le focus y sont déjà justes.
 *
 * Le choix s'applique au clic, sans bouton d'enregistrement : c'est une préférence
 * d'affichage, réversible d'un clic, et l'effet se voit aussitôt.
 */
export function ChoixApparence({ idTitre }: Proprietes) {
  const [choix, setChoix] = useState<Apparence>(() => apparenceEnregistree(stockageLocal()))
  const [nonRetenu, setNonRetenu] = useState(false)

  function choisir(apparence: Apparence) {
    setChoix(apparence)
    appliquerApparence(document.documentElement, apparence)
    setNonRetenu(!enregistrerApparence(stockageLocal(), apparence))
  }

  return (
    <div className="choix-apparence">
      <div className="choix-apparence__options" role="radiogroup" aria-labelledby={idTitre}>
        {Apparences.map((apparence) => (
          <label key={apparence} className="choix-apparence__option">
            <input
              type="radio"
              name="apparence"
              value={apparence}
              checked={choix === apparence}
              onChange={() => {
                choisir(apparence)
              }}
            />
            {LibellesApparence[apparence]}
          </label>
        ))}
      </div>

      <p className="choix-apparence__note">
        <strong>Automatique</strong> suit le réglage de votre appareil. Le choix est retenu
        dans ce navigateur seulement : un autre appareil garde le sien.
      </p>

      {nonRetenu && (
        <output className="choix-apparence__note">
          Ce navigateur ne permet pas de retenir le choix : il s’applique jusqu’au prochain
          chargement de la page.
        </output>
      )}
    </div>
  )
}
