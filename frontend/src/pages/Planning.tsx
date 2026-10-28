import { useCallback, useEffect, useState } from 'react'

import { genererVacations, getPlanningSemaine } from '../api/planning'
import { accepter, getCandidats, ouvrirRemplacement, proposer, refuser } from '../api/remplacements'
import { useAuth } from '../auth/contexte'
import { CartesKpi } from '../composants/CartesKpi'
import { EnTetePage } from '../composants/EnTetePage'
import { GenerationVacations } from '../composants/GenerationVacations'
import { GrillePlanning } from '../composants/GrillePlanning'
import { ListeNonPourvues } from '../composants/ListeNonPourvues'
import { PanneauCandidats } from '../composants/PanneauCandidats'
import { SelecteurSemaine } from '../composants/SelecteurSemaine'
import { aUnRole, Roles } from '../types/auth'
import type { PlanningSemaine, RapportGeneration } from '../types/planning'
import type { Candidat } from '../types/remplacement'
import { ajouterJours, lundiDeLaSemaine, versChaineIso } from '../utils/dates'
import './Planning.css'

/** Semaine obtenue, avec la clé de la demande à laquelle elle répond. */
interface Obtenue {
  cle: string
  semaine: PlanningSemaine
}

/** Échec, avec la clé de la demande à laquelle il répond. */
interface Echouee {
  cle: string
  message: string
}

/** Contenu du panneau : le remplacement ouvert et son classement. */
interface Panneau {
  cle: string
  remplacementId: string
  candidats: Candidat[]
}

/**
 * Qui génère : le reflet de `RolesKlean.Exploitation` sur `POST /api/planning/generer`.
 * Le chef d'équipe lit le planning sans pouvoir générer, et un bouton qui répondrait
 * 403 est une faute d'interface (CLAUDE.md §11).
 */
const RolesGeneration: readonly string[] = [Roles.Administrateur, Roles.Responsable]

/** Douze semaines, comme `ServiceGeneration.SemainesMaximum`. */
const JoursHorizon = 12 * 7

function messageDe(echec: unknown, repli: string): string {
  return echec instanceof Error ? echec.message : repli
}

/**
 * L'écran de planning, et à partir de cette étape le poste de travail du
 * remplacement.
 *
 * C'est **la page** qui orchestre : les six composants sous elle sont
 * présentatifs et reçoivent leurs données en props (CLAUDE.md §11).
 *
 * Aucun état `chargement` n'est stocké — ni pour la semaine, ni pour le panneau.
 * Chaque résultat porte la **clé** de la demande à laquelle il répond, et l'on
 * charge tant que ni le résultat ni l'échec ne portent la clé courante. Un booléen
 * posé à la main dans un effet déclenche un rendu de plus et peut se
 * désynchroniser — c'est ce que signale `react(set-state-in-effect)`.
 */
export function Planning() {
  const [lundi, setLundi] = useState(() => lundiDeLaSemaine(new Date()))
  const [rechargement, setRechargement] = useState(0)
  const [obtenue, setObtenue] = useState<Obtenue | null>(null)
  const [echouee, setEchouee] = useState<Echouee | null>(null)

  const [interventionChoisie, setInterventionChoisie] = useState<string | null>(null)
  const [rechargementPanneau, setRechargementPanneau] = useState(0)
  const [panneau, setPanneau] = useState<Panneau | null>(null)
  const [panneauEchoue, setPanneauEchoue] = useState<Echouee | null>(null)
  const [agentEnCours, setAgentEnCours] = useState<string | null>(null)

  const { profil } = useAuth()
  const peutGenerer = profil !== null && aUnRole(profil, RolesGeneration)
  const [generationEnCours, setGenerationEnCours] = useState(false)
  const [erreurGeneration, setErreurGeneration] = useState<string | null>(null)
  const [rapport, setRapport] = useState<RapportGeneration | null>(null)

  // Bornes de la saisie, en jour **local**. Le serveur raisonne en jour UTC : autour
  // de minuit les deux diffèrent d'un jour, et son 400 en français le dit alors.
  // Proposé par défaut : jusqu'au dimanche de la quatrième semaine, celle-ci comprise.
  const [bornes] = useState(() => {
    const aujourdhui = new Date()

    return {
      minimum: versChaineIso(aujourdhui),
      maximum: versChaineIso(ajouterJours(aujourdhui, JoursHorizon)),
      depart: versChaineIso(ajouterJours(lundiDeLaSemaine(aujourdhui), 27)),
    }
  })

  const lundiIso = versChaineIso(lundi)
  const cle = `${lundiIso}#${String(rechargement)}`
  const enErreur = echouee?.cle === cle
  const chargement = obtenue?.cle !== cle && !enErreur

  // ---- La semaine ----
  useEffect(() => {
    const controleur = new AbortController()

    getPlanningSemaine(lundiIso, controleur.signal)
      .then((recu) => {
        setObtenue({ cle, semaine: recu })
      })
      .catch((echec: unknown) => {
        // Une requête abandonnée n'est pas un échec : c'est le clic suivant.
        if (controleur.signal.aborted) return

        setEchouee({ cle, message: messageDe(echec, 'Planning indisponible.') })
      })

    return () => {
      controleur.abort()
    }
  }, [cle, lundiIso])

  // La vacation choisie est **dérivée** de la semaine courante, pas mémorisée.
  // Conséquence voulue : dès qu'une affectation la fait sortir des non pourvues,
  // le panneau se ferme de lui-même. C'est exactement ce que la spec demande de
  // voir, et le faire à la main serait redondant avec les données.
  const vacationChoisie =
    obtenue?.semaine.nonPourvues.find(
      (vacation) => vacation.interventionId === interventionChoisie,
    ) ?? null

  const clePanneau =
    interventionChoisie === null
      ? null
      : `${interventionChoisie}#${String(rechargementPanneau)}`

  const chargementPanneau = clePanneau !== null && panneau?.cle !== clePanneau

  // ---- Le panneau : ouvrir le remplacement, puis classer ----
  useEffect(() => {
    if (interventionChoisie === null || clePanneau === null) return

    const controleur = new AbortController()

    ouvrirRemplacement(interventionChoisie, controleur.signal)
      .then(async (remplacement) => {
        const classement = await getCandidats(remplacement.id, controleur.signal)

        setPanneau({ cle: clePanneau, remplacementId: remplacement.id, candidats: classement })
      })
      .catch((echec: unknown) => {
        if (controleur.signal.aborted) return

        setPanneauEchoue({
          cle: interventionChoisie,
          message: messageDe(echec, 'Candidats indisponibles.'),
        })
      })

    return () => {
      controleur.abort()
    }
  }, [interventionChoisie, clePanneau])

  const fermer = useCallback(() => {
    setInterventionChoisie(null)
  }, [])

  /**
   * Un clic : proposer puis accepter. Sans notifications ni application agent, il
   * n'y a personne à qui proposer et à attendre (CLAUDE.md §9) — le responsable a
   * téléphoné, l'écran consigne le résultat.
   */
  async function affecter(agentId: string) {
    if (panneau === null || interventionChoisie === null) return

    setAgentEnCours(agentId)
    setPanneauEchoue(null)

    try {
      await proposer(panneau.remplacementId, agentId)

      try {
        await accepter(panneau.remplacementId)
      } catch (echec: unknown) {
        // ⚠️ AccepterAsync **revérifie** l'éligibilité : entre la proposition et
        // l'acceptation, une absence a pu être saisie. Laisser le remplacement en
        // Propose le bloquerait définitivement — ProposerAsync n'accepte que
        // Recherche ou Refuse en entrée, donc aucun autre agent ne pourrait plus
        // être proposé sur cette vacation. Le refus le ramène en Recherche.
        await refuser(panneau.remplacementId).catch(() => undefined)
        throw echec
      }

      setInterventionChoisie(null)
      setRechargement((tour) => tour + 1)
    } catch (echec: unknown) {
      setPanneauEchoue({
        cle: interventionChoisie,
        message: messageDe(echec, 'Affectation refusée.'),
      })

      // Le classement est périmé : l'agent refusé a pu devenir inéligible, et les
      // charges de la semaine ont bougé. On le recharge, l'erreur reste affichée
      // — elle est indexée sur la vacation, pas sur le tour de chargement.
      setRechargementPanneau((tour) => tour + 1)
    } finally {
      setAgentEnCours(null)
    }
  }

  async function generer(jusquAu: string) {
    setGenerationEnCours(true)
    setErreurGeneration(null)

    try {
      setRapport(await genererVacations(jusquAu))

      // La semaine affichée a pu recevoir des vacations : elle est rechargée, et ses
      // non pourvues apparaissent à leur place dans la liste du dessous.
      setRechargement((tour) => tour + 1)
    } catch (echec: unknown) {
      setErreurGeneration(messageDe(echec, 'Génération refusée.'))
    } finally {
      setGenerationEnCours(false)
    }
  }

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
          autre semaine sous un en-tête qui annonce celle-ci. */}
      {!enErreur && obtenue === null && chargement && (
        <output className="planning__attente">Chargement du planning…</output>
      )}

      {!enErreur && obtenue !== null && (
        <div className="planning" aria-busy={chargement}>
          <div className="planning__principal">
            <CartesKpi
              vacations={obtenue.semaine.totalVacations}
              heures={obtenue.semaine.totalHeures}
              nonPourvues={obtenue.semaine.totalNonPourvues}
            />

            {peutGenerer && (
              <GenerationVacations
                minimum={bornes.minimum}
                maximum={bornes.maximum}
                depart={bornes.depart}
                enCours={generationEnCours}
                erreur={erreurGeneration}
                rapport={rapport}
                onGenerer={(jusquAu) => {
                  void generer(jusquAu)
                }}
                onFermerRapport={() => {
                  setRapport(null)
                }}
              />
            )}

            <GrillePlanning semaine={obtenue.semaine} />

            <ListeNonPourvues
              nonPourvues={obtenue.semaine.nonPourvues}
              agents={obtenue.semaine.agents}
              selectionnee={interventionChoisie}
              onChoisir={(vacation) => {
                setInterventionChoisie(vacation.interventionId)
              }}
            />
          </div>

          {vacationChoisie !== null && (
            <PanneauCandidats
              vacation={vacationChoisie}
              candidats={panneau?.cle === clePanneau ? panneau.candidats : null}
              chargement={chargementPanneau}
              erreur={panneauEchoue?.cle === interventionChoisie ? panneauEchoue.message : null}
              agentEnCours={agentEnCours}
              onAffecter={(agentId) => {
                void affecter(agentId)
              }}
              onFermer={fermer}
            />
          )}
        </div>
      )}
    </>
  )
}
