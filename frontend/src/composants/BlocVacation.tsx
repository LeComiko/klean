import { LibellesStatut } from '../passage/passage'
import type { StatutVacation } from '../types/passage'
import { formaterDuree, formaterPlage } from '../utils/dates'
import './BlocVacation.css'

interface Proprietes {
  heure: string
  dureeMinutes: number
  siteNom: string
  prestationLibelle: string
  /**
   * Écrit par son libellé, celui de *Ma journée* : une vacation porte le même nom sur
   * les deux écrans. `Planifiee` ne s'affiche pas — c'est l'état ordinaire du planning.
   */
  statut?: StatutVacation
  /**
   * Mot à écrire quand la vacation n'est pas pourvue : « Absent » pour un agent
   * en absence, « À pourvoir » pour une vacation jamais affectée. Le mot est
   * obligatoire — la couleur ne porte jamais seule l'information.
   */
  alerte?: string
}

/**
 * Un bloc de planning. Composant de présentation : il reçoit des champs, pas un
 * DTO, ce qui lui permet de rendre aussi bien une vacation d'agent qu'une
 * vacation jamais affectée — deux formes différentes côté serveur.
 */
export function BlocVacation({
  heure, dureeMinutes, siteNom, prestationLibelle, statut, alerte,
}: Proprietes) {
  const enAlerte = alerte !== undefined
  const statutVisible = statut !== undefined && statut !== 'Planifiee'

  return (
    <article className={enAlerte ? 'bloc-vacation bloc-vacation--alerte' : 'bloc-vacation'}>
      <p className="bloc-vacation__heure chiffres">{formaterPlage(heure, dureeMinutes)}</p>

      {/* Le nom du site n'est jamais tronqué : deux résidences d'un même syndic
          se distinguent souvent par leur fin, et « Résidence Bellevue… » se
          confondrait avec « Résidence Bellevue Sud ». Il passe à la ligne. */}
      <p className="bloc-vacation__site">{siteNom}</p>

      <p className="bloc-vacation__prestation">{prestationLibelle}</p>

      <p className="bloc-vacation__pied">
        <span className="bloc-vacation__duree chiffres">{formaterDuree(dureeMinutes)}</span>
        {statutVisible && <span className="bloc-vacation__statut">{LibellesStatut[statut]}</span>}
      </p>

      {enAlerte && (
        <p className="bloc-vacation__alerte">
          <span className="bloc-vacation__pastille" aria-hidden="true" />
          {alerte}
        </p>
      )}
    </article>
  )
}
