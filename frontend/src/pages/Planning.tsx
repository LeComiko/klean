import { useEffect, useState } from 'react'

import { getPlanningSemaine } from '../api/planning'
import { CartesKpi } from '../composants/CartesKpi'
import { EnTetePage } from '../composants/EnTetePage'
import { GrillePlanning } from '../composants/GrillePlanning'
import { SelecteurSemaine } from '../composants/SelecteurSemaine'
import type { PlanningSemaine } from '../types/planning'
import { lundiDeLaSemaine, versChaineIso } from '../utils/dates'
import './Planning.css'

/** Semaine obtenue, avec le lundi auquel elle correspond. */
interface Obtenue {
  lundiIso: string
  semaine: PlanningSemaine
}

/** Échec, avec le lundi auquel il correspond. */
interface Echouee {
  lundiIso: string
  message: string
}

/**
 * L'écran de planning. C'est **la page** qui orchestre l'appel réseau : les
 * quatre composants sous elle sont présentatifs et reçoivent leurs données en
 * props (CLAUDE.md §11).
 *
 * Les trois états — chargement, erreur, données — existent, mais `chargement`
 * n'est **pas** un état stocké : il se déduit. Chaque résultat porte le lundi
 * auquel il correspond, et l'on charge tant que ni le résultat ni l'échec ne
 * portent le lundi demandé. Un booléen posé à la main dans l'effet déclencherait
 * un rendu de plus et pourrait désynchroniser — c'est ce que signale la règle
 * `react(set-state-in-effect)` d'oxlint, et elle a raison.
 */
export function Planning() {
  const [lundi, setLundi] = useState(() => lundiDeLaSemaine(new Date()))
  const [obtenue, setObtenue] = useState<Obtenue | null>(null)
  const [echouee, setEchouee] = useState<Echouee | null>(null)

  const lundiIso = versChaineIso(lundi)
  const enErreur = echouee?.lundiIso === lundiIso
  const aJour = obtenue?.lundiIso === lundiIso
  const chargement = !aJour && !enErreur

  useEffect(() => {
    const controleur = new AbortController()

    getPlanningSemaine(lundiIso, controleur.signal)
      .then((recu) => {
        setObtenue({ lundiIso, semaine: recu })
      })
      .catch((echec: unknown) => {
        // Une requête abandonnée n'est pas un échec : c'est le clic suivant sur
        // la flèche. L'afficher ferait clignoter un message à chaque navigation.
        if (controleur.signal.aborted) return

        setEchouee({
          lundiIso,
          message: echec instanceof Error ? echec.message : 'Planning indisponible.',
        })
      })

    return () => {
      controleur.abort()
    }
  }, [lundiIso])

  return (
    <>
      <EnTetePage
        titre="Planning"
        sousTitre="Les agents en lignes, la semaine en colonnes"
        actions={
          <>
            {chargement && obtenue !== null && (
              <output className="planning__attente-semaine">Chargement…</output>
            )}
            <SelecteurSemaine lundi={lundi} onChanger={setLundi} chargement={chargement} />
          </>
        }
      />

      {enErreur && (
        <p className="planning__erreur" role="alert">
          <strong className="planning__erreur-mot">Erreur —</strong> {echouee.message}
        </p>
      )}

      {/* En erreur, la grille disparaît. La garder afficherait les données d'une
          autre semaine sous un en-tête qui annonce celle-ci : mieux vaut ne rien
          montrer que montrer faux. */}
      {!enErreur && obtenue === null && chargement && (
        <output className="planning__attente">Chargement du planning…</output>
      )}

      {!enErreur && obtenue !== null && (
        <div aria-busy={chargement}>
          <CartesKpi
            vacations={obtenue.semaine.totalVacations}
            heures={obtenue.semaine.totalHeures}
            nonPourvues={obtenue.semaine.totalNonPourvues}
          />
          <GrillePlanning semaine={obtenue.semaine} />
        </div>
      )}
    </>
  )
}
