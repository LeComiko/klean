import { ajouterJours, formaterPlageDeSemaine, lundiDeLaSemaine, numeroSemaine } from '../utils/dates'
import './SelecteurSemaine.css'

interface Proprietes {
  lundi: Date
  onChanger: (lundi: Date) => void
  chargement: boolean
}

/**
 * Composant de présentation : il ne connaît ni l'API ni l'écran, il reçoit le
 * lundi courant et rend celui que l'on demande.
 */
export function SelecteurSemaine({ lundi, onChanger, chargement }: Proprietes) {
  const dimanche = ajouterJours(lundi, 6)
  const semaineCourante = lundiDeLaSemaine(new Date())
  const estLaSemaineCourante = lundi.getTime() === semaineCourante.getTime()

  return (
    <div className="selecteur-semaine">
      <button
        className="selecteur-semaine__fleche"
        type="button"
        onClick={() => onChanger(ajouterJours(lundi, -7))}
        disabled={chargement}
        aria-label="Semaine précédente"
      >
        <span aria-hidden="true">‹</span>
      </button>

      <p className="selecteur-semaine__libelle">
        <span className="selecteur-semaine__numero chiffres">
          Semaine {numeroSemaine(lundi)}
        </span>
        <span className="selecteur-semaine__plage">
          {formaterPlageDeSemaine(lundi, dimanche)}
        </span>
      </p>

      <button
        className="selecteur-semaine__fleche"
        type="button"
        onClick={() => onChanger(ajouterJours(lundi, 7))}
        disabled={chargement}
        aria-label="Semaine suivante"
      >
        <span aria-hidden="true">›</span>
      </button>

      <button
        className="selecteur-semaine__courante"
        type="button"
        onClick={() => onChanger(semaineCourante)}
        disabled={chargement || estLaSemaineCourante}
      >
        Cette semaine
      </button>
    </div>
  )
}
