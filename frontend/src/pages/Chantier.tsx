import { EnTetePage } from '../composants/EnTetePage'
import './Chantier.css'

/**
 * Écran d'attente des sections que la feuille de route n'a pas encore
 * atteintes. Une seule page pour les cinq, plutôt que cinq fichiers vides :
 * chacune sera remplacée par son écran réel à son étape.
 */
export function Chantier({ titre }: { titre: string }) {
  return (
    <>
      <EnTetePage titre={titre} />
      <p className="chantier">
        Cette section n'est pas encore construite. La navigation, la session et
        le système d'interface le sont — c'est l'objet de l'étape 13.
      </p>
    </>
  )
}
