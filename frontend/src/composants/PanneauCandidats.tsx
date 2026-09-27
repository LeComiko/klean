import { useEffect } from 'react'

import type { PlanningNonPourvue } from '../types/planning'
import type { Candidat } from '../types/remplacement'
import {
  depuisChaineIso, formaterDuree, formaterHeures, formaterPlage, jourDeLaSemaine, jourIso,
} from '../utils/dates'
import './PanneauCandidats.css'

interface Proprietes {
  vacation: PlanningNonPourvue
  /** `null` tant que rien n'est revenu : distinct d'une liste vide, qui a un sens. */
  candidats: Candidat[] | null
  chargement: boolean
  erreur: string | null
  /** Identifiant de l'agent en cours d'affectation, pour désarmer les boutons. */
  agentEnCours: string | null
  onAffecter: (agentId: string) => void
  onFermer: () => void
}

/**
 * Le panneau droit : le moteur d'affectation devient visible.
 *
 * Composant **présentatif**. Il reçoit candidats, chargement, erreur et rappels en
 * props, et ne sait rien de l'API — c'est la page qui enchaîne l'ouverture du
 * remplacement, le chargement des candidats, puis la proposition et
 * l'acceptation.
 */
export function PanneauCandidats({
  vacation, candidats, chargement, erreur, agentEnCours, onAffecter, onFermer,
}: Proprietes) {
  // Échap ferme le panneau. C'est du comportement de présentation, pas de
  // l'orchestration : le composant reste présentatif.
  useEffect(() => {
    function surTouche(evenement: KeyboardEvent) {
      if (evenement.key === 'Escape') onFermer()
    }

    window.addEventListener('keydown', surTouche)

    return () => {
      window.removeEventListener('keydown', surTouche)
    }
  }, [onFermer])

  const jour = depuisChaineIso(jourIso(vacation.date))

  return (
    <aside className="panneau-candidats" aria-label="Candidats au remplacement">
      <header className="panneau-candidats__entete">
        <div>
          <p className="panneau-candidats__etiquette">Pourvoir</p>
          <h2 className="panneau-candidats__titre">{vacation.siteNom}</h2>
          <p className="panneau-candidats__quand chiffres">
            {jourDeLaSemaine(jour)} {jour.getDate()} ·{' '}
            {formaterPlage(vacation.heure, vacation.dureeMinutes)} ·{' '}
            {formaterDuree(vacation.dureeMinutes)}
          </p>
          <p className="panneau-candidats__prestation">{vacation.prestationLibelle}</p>
        </div>

        <button
          className="panneau-candidats__fermer"
          type="button"
          onClick={onFermer}
          aria-label="Fermer le panneau"
        >
          <span aria-hidden="true">×</span>
        </button>
      </header>

      {/* Ce que fait le bouton, écrit noir sur blanc. Un clic enregistre la
          proposition ET son acceptation : sans notifications ni application
          agent, il n'y a personne à qui proposer et à attendre (CLAUDE.md §9).
          Le responsable a téléphoné, il consigne le résultat. */}
      <p className="panneau-candidats__note">
        Affecter enregistre la proposition et son acceptation d’un coup : il n’y a
        personne à notifier, la V1 consigne un appel déjà passé.
      </p>

      {erreur !== null && (
        <p className="panneau-candidats__erreur" role="alert">
          <strong>Erreur —</strong> {erreur}
        </p>
      )}

      {chargement && candidats === null && (
        <output className="panneau-candidats__attente">Classement des candidats…</output>
      )}

      {candidats !== null && candidats.length === 0 && (
        <p className="panneau-candidats__aucun">
          <strong>Aucun candidat.</strong> Tous les agents sont écartés par un filtre
          dur : inactif, absent ce jour-là, déjà sur une vacation qui chevauche, ou
          repos légal de onze heures insuffisant.
        </p>
      )}

      {candidats !== null && candidats.length > 0 && (
        <ul className="panneau-candidats__liste" aria-busy={chargement}>
          {candidats.map((candidat) => (
            <li className="candidat" key={candidat.agentId}>
              {/* Le score en chasse fixe : les scores se comparent en colonne, et
                  la chasse fixe les aligne sans effort (CLAUDE.md §12). */}
              <p className="candidat__score chiffres">{Math.round(candidat.score)}</p>

              <div className="candidat__identite">
                <p className="candidat__nom">
                  {candidat.prenom} {candidat.nom}
                </p>

                {/* ⚠️ Les données brutes, pas seulement le score. §8 : « 12 km »
                    se discute, « note 60 » ne se discute pas. Un responsable qui
                    ne voit qu'un score ne peut ni le contester ni apprendre à
                    s'en méfier. */}
                <p className="candidat__brut chiffres">
                  {formaterKm(candidat.distanceKm)}
                  {' · '}
                  {candidat.passages} {candidat.passages === 1 ? 'passage' : 'passages'}
                  {' · '}
                  {formaterHeures(candidat.heuresSemaine)} cette semaine
                </p>
              </div>

              <button
                className="candidat__bouton"
                type="button"
                onClick={() => onAffecter(candidat.agentId)}
                disabled={agentEnCours !== null}
              >
                {agentEnCours === candidat.agentId ? 'Affectation…' : 'Affecter'}
              </button>
            </li>
          ))}
        </ul>
      )}
    </aside>
  )
}

/**
 * `9.3` → `9,3 km`. Null quand l'agent n'a pas de coordonnées : la note de
 * distance vaut alors 50, la valeur neutre, et le dire vaut mieux que d'afficher
 * un tiret que personne ne sait interpréter.
 */
function formaterKm(distanceKm: number | null): string {
  if (distanceKm === null) return 'distance inconnue'

  return `${(Math.round(distanceKm * 10) / 10).toString().replace('.', ',')} km`
}
