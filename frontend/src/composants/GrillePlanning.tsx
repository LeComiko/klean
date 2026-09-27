import type { PlanningSemaine, PlanningVacation } from '../types/planning'
import { SeuilHeuresHebdomadaires } from '../types/planning'
import {
  ajouterJours, depuisChaineIso, formaterHeures, jourCourt, jourIso, versChaineIso,
} from '../utils/dates'
import { BlocVacation } from './BlocVacation'
import './GrillePlanning.css'

interface Proprietes {
  semaine: PlanningSemaine
}

interface Colonne {
  iso: string
  nom: string
  numero: number
}

/**
 * La grille : colonne agent de 160 px, puis lundi à vendredi.
 *
 * Un vrai `<table>` et non une grille de `<div>` : c'est un tableau de données,
 * et l'en-tête de colonne comme l'en-tête de ligne doivent être annoncés par un
 * lecteur d'écran. `role="table"` sur des div reconstruirait à la main ce que
 * `<th scope>` donne gratuitement.
 */
export function GrillePlanning({ semaine }: Proprietes) {
  // ⚠️ Le lundi passe par jourIso puis depuisChaineIso, jamais par
  // `new Date(semaine.lundi)`. Le serveur envoie minuit UTC, et la conversion
  // directe décale le jour dans tout fuseau à décalage négatif.
  const lundi = depuisChaineIso(jourIso(semaine.lundi))

  const colonnes: Colonne[] = [0, 1, 2, 3, 4].map((decalage) => {
    const jour = ajouterJours(lundi, decalage)

    return { iso: versChaineIso(jour), nom: jourCourt(decalage), numero: jour.getDate() }
  })

  // Les vacations jamais affectées n'ont pas de ligne d'agent : sans cette
  // dernière ligne, deux des dix-sept vacations seraient invisibles alors que le
  // compteur en annonce dix-sept. Celles d'un agent absent, elles, sont déjà sur
  // la ligne de cet agent — les filtrer ici évite de les compter deux fois.
  const aPourvoir = semaine.nonPourvues.filter((vacation) => vacation.agentId === null)

  if (semaine.totalVacations === 0) {
    return (
      <p className="grille-planning__vide">
        Aucune vacation planifiée cette semaine.
      </p>
    )
  }

  return (
    <div className="grille-planning__cadre">
      <table className="grille-planning">
        <caption className="pour-lecteur-d-ecran">
          Planning de la semaine, agents en lignes et jours en colonnes
        </caption>

        <colgroup>
          <col className="grille-planning__col-agent" />
          {colonnes.map((colonne) => (
            <col key={colonne.iso} />
          ))}
        </colgroup>

        <thead>
          <tr>
            <th scope="col" className="grille-planning__coin">
              Agent
            </th>
            {colonnes.map((colonne) => (
              <th scope="col" key={colonne.iso} className="grille-planning__jour">
                <span className="grille-planning__jour-nom">{colonne.nom}</span>{' '}
                <span className="chiffres">{colonne.numero}</span>
              </th>
            ))}
          </tr>
        </thead>

        <tbody>
          {semaine.agents.map((agent) => {
            const surcharge = agent.heures > SeuilHeuresHebdomadaires

            return (
              <tr key={agent.agentId}>
                <th scope="row" className="grille-planning__agent">
                  <span className="grille-planning__agent-nom">{agent.nom}</span>
                  <span className="grille-planning__agent-prenom">{agent.prenom}</span>
                  <span
                    className={
                      surcharge
                        ? 'grille-planning__charge grille-planning__charge--retard chiffres'
                        : 'grille-planning__charge chiffres'
                    }
                  >
                    {agent.nombreVacations} vac · {formaterHeures(agent.heures)}
                  </span>
                  {/* La couleur ne porte pas seule l'information : le mot
                      accompagne le passage en --retard. */}
                  {surcharge && (
                    <span className="grille-planning__surcharge">charge élevée</span>
                  )}
                </th>

                {colonnes.map((colonne) => (
                  <td key={colonne.iso} className="grille-planning__case">
                    {vacationsDuJour(agent.vacations, colonne.iso).map((vacation) => (
                      <BlocVacation
                        key={vacation.interventionId}
                        heure={vacation.heure}
                        dureeMinutes={vacation.dureeMinutes}
                        siteNom={vacation.siteNom}
                        prestationLibelle={vacation.prestationLibelle}
                        statut={vacation.statut}
                        alerte={vacation.nonPourvue ? 'Absent' : undefined}
                      />
                    ))}
                  </td>
                ))}
              </tr>
            )
          })}

          {aPourvoir.length > 0 && (
            <tr className="grille-planning__ligne-a-pourvoir">
              <th scope="row" className="grille-planning__agent">
                <span className="grille-planning__agent-nom">À pourvoir</span>
                <span className="grille-planning__agent-prenom">aucun agent affecté</span>
                <span className="grille-planning__charge chiffres">
                  {aPourvoir.length} vac
                </span>
              </th>

              {colonnes.map((colonne) => (
                <td key={colonne.iso} className="grille-planning__case">
                  {aPourvoir
                    .filter((vacation) => jourIso(vacation.date) === colonne.iso)
                    .map((vacation) => (
                      <BlocVacation
                        key={vacation.interventionId}
                        heure={vacation.heure}
                        dureeMinutes={vacation.dureeMinutes}
                        siteNom={vacation.siteNom}
                        prestationLibelle={vacation.prestationLibelle}
                        alerte="À pourvoir"
                      />
                    ))}
                </td>
              ))}
            </tr>
          )}
        </tbody>
      </table>
    </div>
  )
}

function vacationsDuJour(vacations: PlanningVacation[], iso: string): PlanningVacation[] {
  return vacations.filter((vacation) => jourIso(vacation.date) === iso)
}
