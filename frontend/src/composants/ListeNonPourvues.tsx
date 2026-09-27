import type { PlanningAgent, PlanningNonPourvue } from '../types/planning'
import {
  depuisChaineIso, formaterDuree, formaterPlage, jourDeLaSemaine, jourIso,
} from '../utils/dates'
import './ListeNonPourvues.css'

interface Proprietes {
  nonPourvues: PlanningNonPourvue[]
  /** Pour nommer l'agent absent : le serveur ne donne que son identifiant. */
  agents: PlanningAgent[]
  /** Identifiant de l'intervention dont le panneau est ouvert, s'il y en a un. */
  selectionnee: string | null
  onChoisir: (vacation: PlanningNonPourvue) => void
}

/**
 * Sous la grille, une ligne par vacation à pourvoir.
 *
 * Composant de présentation : il ne fait aucun appel, c'est la page qui
 * orchestre. Il reçoit les agents uniquement pour nommer l'absent — le serveur
 * envoie `agentId`, et « Absence de Élodie Moreau » est infiniment plus utile
 * qu'« Absence de 9ce2632b ».
 */
export function ListeNonPourvues({
  nonPourvues, agents, selectionnee, onChoisir,
}: Proprietes) {
  if (nonPourvues.length === 0) {
    return (
      <section className="non-pourvues">
        <h2 className="non-pourvues__titre">À pourvoir</h2>
        <p className="non-pourvues__vide">
          Toutes les vacations de la semaine ont un agent. Rien à pourvoir.
        </p>
      </section>
    )
  }

  return (
    <section className="non-pourvues">
      <h2 className="non-pourvues__titre">
        À pourvoir
        <span className="non-pourvues__compte chiffres">{nonPourvues.length}</span>
      </h2>

      <ul className="non-pourvues__liste">
        {nonPourvues.map((vacation) => {
          const choisie = vacation.interventionId === selectionnee
          const jour = depuisChaineIso(jourIso(vacation.date))

          return (
            <li
              key={vacation.interventionId}
              className={choisie ? 'non-pourvue non-pourvue--choisie' : 'non-pourvue'}
            >
              <p className="non-pourvue__quand">
                <span className="non-pourvue__jour">
                  {jourDeLaSemaine(jour)} {jour.getDate()}
                </span>
                <span className="non-pourvue__heure chiffres">
                  {formaterPlage(vacation.heure, vacation.dureeMinutes)}
                  {' · '}
                  {formaterDuree(vacation.dureeMinutes)}
                </span>
              </p>

              <p className="non-pourvue__ou">
                <span className="non-pourvue__site">{vacation.siteNom}</span>
                <span className="non-pourvue__prestation">{vacation.prestationLibelle}</span>
              </p>

              {/* La couleur ne porte pas seule l'information : la raison est
                  écrite, et la pastille n'est qu'une redondance. */}
              <p className="non-pourvue__raison">
                <span className="non-pourvue__pastille" aria-hidden="true" />
                {raison(vacation, agents)}
              </p>

              <button
                className="non-pourvue__bouton"
                type="button"
                onClick={() => onChoisir(vacation)}
                aria-pressed={choisie}
              >
                {choisie ? 'Candidats ouverts' : 'Pourvoir'}
              </button>
            </li>
          )
        })}
      </ul>
    </section>
  )
}

/**
 * « Absence de Élodie Moreau » ou « Aucun agent affecté ».
 *
 * Le champ `cause` du serveur sert de repli : si l'agent n'est pas dans la liste
 * — ce que le service de planning ne devrait jamais produire, puisqu'il inclut
 * les agents affectés même inactifs — mieux vaut sa formulation que rien.
 */
function raison(vacation: PlanningNonPourvue, agents: PlanningAgent[]): string {
  if (vacation.agentId === null) return 'Aucun agent affecté'

  const absent = agents.find((agent) => agent.agentId === vacation.agentId)

  return absent === undefined
    ? vacation.cause
    : `Absence de ${absent.prenom} ${absent.nom}`
}
