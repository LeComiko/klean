import { useEffect, useState } from 'react'

import { demarrer, envoyerPhoto, lireMaJournee, signalerAnomalie, terminer } from '../api/passage'
import { EnTetePage } from '../composants/EnTetePage'
import {
  LibellesStatut,
  LongueurMaxDescription,
  NoticeLocalisation,
  bilanDe,
  gesteDe,
  heureDe,
  localiser,
  phrasePreuve,
  photosRestantes,
  pointsFaits,
  pointsNonCoches,
  refusDescription,
  refusFichier,
  resumeChecklist,
} from '../passage/passage'
import { reduirePhoto } from '../passage/photo'
import type { AnomalieAgent, Pointage, VacationAgent } from '../types/passage'
import { formaterDateLongue, formaterPlage, jourDeLaSemaine } from '../utils/dates'
import './MaJournee.css'

function messageDe(echec: unknown, repli: string): string {
  return echec instanceof Error ? echec.message : repli
}

/** La photo prête à partir : réduite, montrée, pas encore envoyée. */
interface Apercu {
  anomalieId: string
  photo: Blob
  url: string
}

/** Ce qui est en cours : il n'y en a qu'un à la fois, et l'écran le dit. */
type Attente = 'Localisation' | 'Envoi' | 'Preparation' | null

/**
 * La journée de l'agent, sur son téléphone (SPEC.md, étape 25, lot C) : ses vacations,
 * puis, sur l'une, les consignes, « Démarrer », la checklist, les anomalies et
 * « Terminer ». Densité mobile de §12 : une action par écran, cibles de 56 px.
 *
 * ⚠️ La position n'est demandée qu'**au geste**, jamais à l'ouverture de l'écran, et la
 * notice qui dit ce qui est relevé est affichée juste au-dessus de chaque bouton qui la
 * demande. En ligne seulement : sans réseau, le geste échoue et le dit.
 */
export function MaJournee() {
  const [journee, setJournee] = useState<VacationAgent[] | null>(null)
  const [erreur, setErreur] = useState<string | null>(null)
  const [rechargement, setRechargement] = useState(0)

  const [choisieId, setChoisieId] = useState<string | null>(null)
  const [coches, setCoches] = useState<ReadonlySet<string>>(new Set())
  const [confirmerFin, setConfirmerFin] = useState(false)

  const [attente, setAttente] = useState<Attente>(null)
  const [refus, setRefus] = useState<string | null>(null)

  const [description, setDescription] = useState('')
  const [apercu, setApercu] = useState<Apercu | null>(null)

  useEffect(() => {
    const controleur = new AbortController()

    lireMaJournee(controleur.signal)
      .then((recue) => {
        setJournee(recue)
        setErreur(null)
      })
      .catch((echec: unknown) => {
        if (controleur.signal.aborted) return
        setErreur(messageDe(echec, 'Journée indisponible.'))
      })

    return () => {
      controleur.abort()
    }
  }, [rechargement])

  // Une URL `blob:` retient la photo en mémoire tant qu'elle n'est pas révoquée.
  useEffect(() => {
    if (apercu === null) return undefined

    return () => {
      URL.revokeObjectURL(apercu.url)
    }
  }, [apercu])

  // La vacation en cours s'ouvre d'elle-même : c'est celle où l'agent se trouve.
  const ouverte = journee?.find((vacation) => vacation.id === choisieId)
    ?? (choisieId === null ? journee?.find((vacation) => vacation.statut === 'EnCours') : undefined)

  function remplacer(recue: VacationAgent) {
    setJournee((avant) => avant?.map((vacation) => (vacation.id === recue.id ? recue : vacation)) ?? null)
  }

  function ouvrir(id: string | null) {
    setChoisieId(id)
    setCoches(new Set())
    setConfirmerFin(false)
    setRefus(null)
    setDescription('')
    setApercu(null)
  }

  async function pointer(vacation: VacationAgent) {
    setRefus(null)
    setAttente('Localisation')

    try {
      const position = await localiser(navigator.geolocation)

      setAttente('Envoi')

      const recue = gesteDe(vacation.statut) === 'Demarrer'
        ? await demarrer(vacation.id, position)
        : await terminer(vacation.id, position, pointsFaits(vacation.checklist, coches))

      remplacer(recue)
      setChoisieId(recue.id)
      setConfirmerFin(false)
    } catch (echec: unknown) {
      setRefus(messageDe(echec, 'Le geste n’a pas abouti.'))
    } finally {
      setAttente(null)
    }
  }

  async function signaler(vacation: VacationAgent) {
    const local = refusDescription(description)
    if (local !== null) {
      setRefus(local)
      return
    }

    setRefus(null)
    setAttente('Envoi')

    try {
      const anomalie = await signalerAnomalie(vacation.id, description.trim())

      remplacer({ ...vacation, anomalies: [...vacation.anomalies, anomalie] })
      setDescription('')
    } catch (echec: unknown) {
      setRefus(messageDe(echec, 'L’anomalie n’a pas été enregistrée.'))
    } finally {
      setAttente(null)
    }
  }

  async function preparer(anomalie: AnomalieAgent, fichier: File | undefined) {
    if (fichier === undefined) return

    const local = refusFichier(fichier)
    if (local !== null) {
      setRefus(local)
      return
    }

    setRefus(null)
    setAttente('Preparation')

    try {
      const photo = await reduirePhoto(fichier)

      setApercu({ anomalieId: anomalie.id, photo, url: URL.createObjectURL(photo) })
    } catch (echec: unknown) {
      setRefus(messageDe(echec, 'Cette photo n’a pas pu être préparée.'))
    } finally {
      setAttente(null)
    }
  }

  async function envoyer(vacation: VacationAgent, envoi: Apercu) {
    setRefus(null)
    setAttente('Envoi')

    try {
      await envoyerPhoto(envoi.anomalieId, envoi.photo)

      remplacer({
        ...vacation,
        anomalies: vacation.anomalies.map((anomalie) =>
          (anomalie.id === envoi.anomalieId ? { ...anomalie, photos: anomalie.photos + 1 } : anomalie)),
      })
      setApercu(null)
    } catch (echec: unknown) {
      setRefus(messageDe(echec, 'La photo n’a pas été envoyée.'))
    } finally {
      setAttente(null)
    }
  }

  const aujourdhui = new Date()
  const occupe = attente !== null

  return (
    <div className="ma-journee">
      <EnTetePage
        titre="Ma journée"
        sousTitre={`${jourDeLaSemaine(aujourdhui)} ${formaterDateLongue(aujourdhui)}`}
      />

      {erreur !== null && (
        <div className="ma-journee__alerte" role="alert">
          <p><strong>Erreur —</strong> {erreur}</p>
          <button
            className="ma-journee__secondaire"
            type="button"
            onClick={() => {
              setRechargement((n) => n + 1)
            }}
          >
            Réessayer
          </button>
        </div>
      )}

      {erreur === null && journee === null && <output className="ma-journee__attente">Chargement…</output>}

      {journee !== null && ouverte === undefined && (
        journee.length === 0 ? (
          <p className="ma-journee__vide">Aucune vacation aujourd’hui.</p>
        ) : (
          <ul className="ma-journee__liste">
            {journee.map((vacation) => (
              <li key={vacation.id}>
                <button
                  className="ma-journee__carte"
                  type="button"
                  onClick={() => {
                    ouvrir(vacation.id)
                  }}
                >
                  <span className="ma-journee__plage chiffres">
                    {formaterPlage(vacation.heurePrevue, vacation.dureeEstimeeMinutes)}
                  </span>
                  <span className="ma-journee__site">{vacation.site}</span>
                  <span className="ma-journee__prestation">{vacation.prestation}</span>
                  <Statut vacation={vacation} />
                </button>
              </li>
            ))}
          </ul>
        )
      )}

      {ouverte !== undefined && (
        <article className="ma-journee__vacation" aria-busy={occupe}>
          <button
            className="ma-journee__retour"
            type="button"
            onClick={() => {
              // `''` plutôt que `null` : sinon la vacation en cours se rouvrirait d'elle-même.
              ouvrir('')
            }}
          >
            ‹ Toutes mes vacations
          </button>

          <h2 className="ma-journee__titre">{ouverte.site}</h2>
          <p className="ma-journee__plage chiffres">
            {formaterPlage(ouverte.heurePrevue, ouverte.dureeEstimeeMinutes)}
          </p>
          <p className="ma-journee__texte">{ouverte.prestation}</p>
          <p className="ma-journee__texte">
            {[ouverte.adresse, [ouverte.codePostal, ouverte.ville].filter(Boolean).join(' ')]
              .filter((morceau) => morceau !== '')
              .join(', ')}
          </p>
          <Statut vacation={ouverte} />

          {ouverte.consignes !== null && ouverte.consignes.trim() !== '' && (
            <section className="ma-journee__bloc">
              <h3 className="ma-journee__sous-titre">Consignes</h3>
              <p className="ma-journee__consignes">{ouverte.consignes}</p>
            </section>
          )}

          {ouverte.debut !== null && <LignePointage moment="Démarré" pointage={ouverte.debut} />}
          {ouverte.fin !== null && <LignePointage moment="Terminé" pointage={ouverte.fin} />}

          {bilanDe(ouverte) !== null && (
            <p
              className={ouverte.statut === 'Terminee'
                ? 'ma-journee__bilan ma-journee__bilan--certifie'
                : 'ma-journee__bilan'}
            >
              {bilanDe(ouverte)}
            </p>
          )}

          {ouverte.statut === 'EnCours' && (
            <fieldset className="ma-journee__bloc ma-journee__checklist">
              <legend className="ma-journee__sous-titre">Checklist</legend>
              {ouverte.checklist.map((point) => (
                point.pointControleId === null ? null : (
                  <label key={point.pointControleId} className="ma-journee__point">
                    <input
                      type="checkbox"
                      checked={coches.has(point.pointControleId)}
                      disabled={occupe}
                      onChange={(evenement) => {
                        const id = point.pointControleId ?? ''
                        const suivantes = new Set(coches)

                        if (evenement.target.checked) suivantes.add(id)
                        else suivantes.delete(id)

                        setCoches(suivantes)
                        setConfirmerFin(false)
                      }}
                    />
                    <span>{point.libelle}</span>
                  </label>
                )
              ))}
              <p className="ma-journee__note">{resumeChecklist(ouverte.checklist, coches)}</p>
            </fieldset>
          )}

          {ouverte.statut !== 'EnCours' && ouverte.checklist.some((point) => point.fait !== null) && (
            <section className="ma-journee__bloc">
              <h3 className="ma-journee__sous-titre">Checklist déclarée</h3>
              <ul className="ma-journee__declares">
                {ouverte.checklist.map((point, index) => (
                  <li key={point.pointControleId ?? `supprime-${String(index)}`}>
                    <strong>{point.fait === true ? 'Fait' : 'Non fait'}</strong> — {point.libelle}
                  </li>
                ))}
              </ul>
            </section>
          )}

          {(ouverte.statut === 'EnCours' || ouverte.anomalies.length > 0) && (
            <section className="ma-journee__bloc">
              <h3 className="ma-journee__sous-titre">Anomalies</h3>

              {ouverte.anomalies.length > 0 && (
                <ul className="ma-journee__anomalies">
                  {ouverte.anomalies.map((anomalie) => (
                    <li key={anomalie.id} className="ma-journee__anomalie">
                      <p className="ma-journee__texte">{anomalie.description}</p>
                      <p className="ma-journee__note">
                        {anomalie.photos === 0
                          ? 'Aucune photo'
                          : `${String(anomalie.photos)} photo${anomalie.photos > 1 ? 's' : ''}`}
                      </p>

                      {apercu?.anomalieId === anomalie.id && (
                        <div className="ma-journee__apercu">
                          <img src={apercu.url} alt="Aperçu, avant l’envoi" />
                          <div className="ma-journee__gestes">
                            <button
                              className="ma-journee__principal"
                              type="button"
                              disabled={occupe}
                              onClick={() => void envoyer(ouverte, apercu)}
                            >
                              Envoyer la photo
                            </button>
                            <button
                              className="ma-journee__secondaire"
                              type="button"
                              disabled={occupe}
                              onClick={() => {
                                setApercu(null)
                              }}
                            >
                              Annuler
                            </button>
                          </div>
                        </div>
                      )}

                      {ouverte.statut === 'EnCours' && apercu === null && photosRestantes(anomalie) > 0 && (
                        // Le champ de fichier est masqué, pas retiré : il reste focalisable,
                        // et le libellé porte la cible tactile et le contour de focus.
                        <label className="ma-journee__secondaire ma-journee__photo">
                          Ajouter une photo
                          <input
                            className="pour-lecteur-d-ecran"
                            type="file"
                            accept="image/*"
                            capture="environment"
                            disabled={occupe}
                            onChange={(evenement) => {
                              const fichier = evenement.target.files?.[0]

                              // Vidé aussitôt : choisir deux fois la même photo doit déclencher.
                              evenement.target.value = ''
                              void preparer(anomalie, fichier)
                            }}
                          />
                        </label>
                      )}
                    </li>
                  ))}
                </ul>
              )}

              {ouverte.statut === 'EnCours' && (
                <form
                  className="ma-journee__signalement"
                  onSubmit={(evenement) => {
                    evenement.preventDefault()
                    void signaler(ouverte)
                  }}
                >
                  <label className="ma-journee__etiquette" htmlFor="description-anomalie">
                    Signaler une anomalie
                  </label>
                  <textarea
                    id="description-anomalie"
                    className="ma-journee__zone"
                    value={description}
                    maxLength={LongueurMaxDescription}
                    rows={3}
                    disabled={occupe}
                    placeholder="Ce qui a été constaté, et où. Sans nommer de personne."
                    onChange={(evenement) => {
                      setDescription(evenement.target.value)
                    }}
                  />
                  <button className="ma-journee__secondaire" type="submit" disabled={occupe}>
                    Enregistrer l’anomalie
                  </button>
                </form>
              )}
            </section>
          )}

          {refus !== null && (
            <p className="ma-journee__refus" role="alert">
              <strong>Refusé —</strong> {refus}
            </p>
          )}

          {attente !== null && (
            <output className="ma-journee__attente">
              {attente === 'Localisation'
                ? 'Relevé de la position…'
                : attente === 'Preparation' ? 'Préparation de la photo…' : 'Envoi…'}
            </output>
          )}

          {gesteDe(ouverte.statut) !== 'Aucun' && (
            <div className="ma-journee__pied">
              <p className="ma-journee__notice">{NoticeLocalisation}</p>

              {confirmerFin ? (
                <>
                  <p className="ma-journee__texte">
                    {resumeChecklist(ouverte.checklist, coches)} : les autres seront déclarés non faits.
                    Terminer quand même ?
                  </p>
                  <button
                    className="ma-journee__principal"
                    type="button"
                    disabled={occupe}
                    onClick={() => void pointer(ouverte)}
                  >
                    Confirmer la fin
                  </button>
                  <button
                    className="ma-journee__secondaire"
                    type="button"
                    disabled={occupe}
                    onClick={() => {
                      setConfirmerFin(false)
                    }}
                  >
                    Revenir à la checklist
                  </button>
                </>
              ) : (
                <button
                  className="ma-journee__principal"
                  type="button"
                  disabled={occupe}
                  onClick={() => {
                    // Un rapport, jamais un blocage : la fin reste possible, mais elle
                    // se confirme quand un point n'est pas coché.
                    const incomplet = gesteDe(ouverte.statut) === 'Terminer'
                      && pointsNonCoches(ouverte.checklist, coches) > 0

                    if (incomplet) setConfirmerFin(true)
                    else void pointer(ouverte)
                  }}
                >
                  {gesteDe(ouverte.statut) === 'Demarrer' ? 'Démarrer' : 'Terminer'}
                </button>
              )}
            </div>
          )}
        </article>
      )}
    </div>
  )
}

/** Le statut d'une vacation : une pastille **et** un mot, jamais la couleur seule (§12). */
function Statut({ vacation }: { vacation: VacationAgent }) {
  return (
    <span className={`ma-journee__statut ma-journee__statut--${vacation.statut}`}>
      {LibellesStatut[vacation.statut]}
    </span>
  )
}

function LignePointage({ moment, pointage }: { moment: string; pointage: Pointage }) {
  return (
    <p className="ma-journee__pointage">
      <strong className="chiffres">{moment} à {heureDe(pointage.horodatage)}</strong> — {phrasePreuve(pointage)}
    </p>
  )
}
