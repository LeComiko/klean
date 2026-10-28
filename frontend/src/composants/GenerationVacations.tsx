import { useState } from 'react'

import type { LigneRapportGeneration, RapportGeneration } from '../types/planning'
import {
  depuisChaineIso, formaterDateLongue, formaterHeure, jourDeLaSemaine, jourIso,
} from '../utils/dates'
import './GenerationVacations.css'

interface Proprietes {
  /** Premier jour accepté, `AAAA-MM-JJ` : aujourd'hui. */
  minimum: string
  /** Dernier jour accepté, `AAAA-MM-JJ` : douze semaines plus loin. */
  maximum: string
  /** Valeur proposée à l'ouverture. Lue une seule fois, au montage. */
  depart: string

  enCours: boolean
  erreur: string | null
  rapport: RapportGeneration | null

  onGenerer: (jusquAu: string) => void
  onFermerRapport: () => void
}

/**
 * « Générer jusqu'au … » et le rapport qui suit (SPEC.md, étape 20).
 *
 * Composant de présentation : l'appel est fait par la page. Le seul état interne est
 * la date saisie, comme les valeurs de `FormulaireRessource`.
 *
 * Le rapport met en avant ce qui demande un regard — les vacations restées à
 * pourvoir, les jours fériés sautés, les orphelines — et résume le reste en une
 * phrase. Il ne nomme aucun agent : le serveur n'en envoie pas, et l'écran ne doit
 * pas les recomposer.
 */
export function GenerationVacations({
  minimum, maximum, depart, enCours, erreur, rapport, onGenerer, onFermerRapport,
}: Proprietes) {
  const [jusquAu, setJusquAu] = useState(depart)

  return (
    <section className="generation" aria-labelledby="generation-titre">
      <form
        className="generation__formulaire"
        onSubmit={(evenement) => {
          evenement.preventDefault()
          if (!enCours && jusquAu !== '') onGenerer(jusquAu)
        }}
      >
        <h2 className="generation__titre" id="generation-titre">
          Générer les vacations
        </h2>

        <label className="generation__libelle" htmlFor="generation-jusqu-au">
          jusqu’au
        </label>
        <input
          className="generation__date"
          id="generation-jusqu-au"
          type="date"
          required
          min={minimum}
          max={maximum}
          value={jusquAu}
          onChange={(evenement) => {
            setJusquAu(evenement.target.value)
          }}
          aria-describedby="generation-aide"
        />

        <button className="generation__bouton" type="submit" disabled={enCours}>
          {enCours ? 'Génération…' : 'Générer'}
        </button>

        <p className="generation__aide" id="generation-aide">
          Depuis les jours et l’heure de chaque prestation, douze semaines au plus. Rejouer
          ne recrée rien, et rien d’existant n’est modifié.
        </p>
      </form>

      {erreur !== null && (
        <p className="generation__erreur" role="alert">
          <strong className="generation__erreur-mot">Refusé —</strong> {erreur}
        </p>
      )}

      {rapport !== null && <Rapport rapport={rapport} onFermer={onFermerRapport} />}
    </section>
  )
}

function Rapport({ rapport, onFermer }: { rapport: RapportGeneration; onFermer: () => void }) {
  const du = formaterDateLongue(depuisChaineIso(jourIso(rapport.du)))
  const au = formaterDateLongue(depuisChaineIso(jourIso(rapport.au)))

  return (
    <div className="rapport">
      <div className="rapport__entete">
        {/* `<output>` sur le résumé seul : c'est la phrase à annoncer quand le rapport
            arrive, et le seul morceau en contenu phrastique — les listes et les
            `details` qui suivent n'y sont pas admis. */}
        <output className="rapport__resume">
          Du {du} au {au} : <Creees rapport={rapport} />
          {rapport.dejaPresentes > 0 && (
            <>
              {' '}
              <span className="chiffres">{rapport.dejaPresentes}</span>{' '}
              {rapport.dejaPresentes > 1
                ? 'déjà présentes, non recréées.'
                : 'déjà présente, non recréée.'}
            </>
          )}
        </output>
        <button className="rapport__fermer" type="button" onClick={onFermer}>
          Fermer le rapport
        </button>
      </div>

      <Section
        titre="Restées à pourvoir"
        lignes={rapport.nonPourvues}
        vide={rapport.creees === 0
          ? 'Aucune vacation créée.'
          : 'Toutes les vacations créées ont reçu leur titulaire.'}
        alerte
      />
      <Section
        titre="Jours fériés sautés"
        lignes={rapport.feries}
        vide="Aucun jour férié dans la période, ou tous inclus par leur contrat."
      />
      <Section
        titre="Orphelines, laissées telles quelles"
        lignes={rapport.orphelines}
        vide="Toutes les vacations planifiées de la période correspondent à leur prestation."
        aide="Planifiées avant un changement de contrat ou de prestation. La génération ne les modifie ni ne les supprime : à revoir une par une."
      />
    </div>
  )
}

/**
 * « Aucune vacation créée. », « 1 vacation créée, avec son titulaire. », « 6 vacations
 * créées, dont 4 avec leur titulaire. » Mesuré dans le navigateur : la forme unique
 * donnait « 0 vacation créée, dont 0 avec leur titulaire » sur une génération rejouée,
 * qui est le cas courant puisqu'elle est idempotente.
 */
function Creees({ rapport }: { rapport: RapportGeneration }) {
  if (rapport.creees === 0) return <>Aucune vacation créée.</>

  if (rapport.creees === 1) {
    return (
      <>
        <strong className="chiffres">1</strong> vacation créée,{' '}
        {rapport.pourvues === 1 ? 'avec son titulaire.' : 'sans titulaire.'}
      </>
    )
  }

  return (
    <>
      <strong className="chiffres">{rapport.creees}</strong> vacations créées, dont{' '}
      <strong className="chiffres">{rapport.pourvues}</strong> avec leur titulaire.
    </>
  )
}

function Section({
  titre, lignes, vide, aide, alerte = false,
}: {
  titre: string
  lignes: readonly LigneRapportGeneration[]
  vide: string
  aide?: string
  alerte?: boolean
}) {
  if (lignes.length === 0) {
    return (
      <p className="rapport__vide">
        <span className="rapport__section-titre">{titre}</span> — {vide}
      </p>
    )
  }

  // `details` replié par défaut : douze semaines sans titulaire font des centaines
  // de lignes, et le nombre suffit à savoir s'il faut ouvrir.
  return (
    <details className={alerte ? 'rapport__section rapport__section--alerte' : 'rapport__section'}>
      <summary className="rapport__sommaire">
        <span className="rapport__section-titre">{titre}</span>
        <span className="rapport__compte chiffres">{lignes.length}</span>
      </summary>

      {aide !== undefined && <p className="rapport__aide">{aide}</p>}

      <ul className="rapport__lignes">
        {lignes.map((ligne) => {
          const jour = depuisChaineIso(jourIso(ligne.date))

          return (
            <li
              key={`${ligne.prestationId}#${ligne.date}#${ligne.interventionId ?? ''}`}
              className="rapport__ligne"
            >
              <span className="rapport__quand">
                {jourDeLaSemaine(jour)} {formaterDateLongue(jour)}
                <span className="chiffres"> · {formaterHeure(ligne.heure)}</span>
              </span>
              <span className="rapport__ou">
                {ligne.site} — {ligne.prestation}
              </span>
              <span className="rapport__cause">{ligne.cause}</span>
            </li>
          )
        })}
      </ul>
    </details>
  )
}
