import { useEffect, useState } from 'react'

import { lirePhoto, listerAnomalies, traiterAnomalie } from '../api/anomalies'
import { TailleParDefaut } from '../api/ressources'
import type { Page } from '../api/ressources'
import { EnTetePage } from '../composants/EnTetePage'
import { Pagination } from '../composants/Pagination'
import {
  FiltresAnomalies,
  LibellesFiltreAnomalies,
  LibellesStatutAnomalie,
  heureDe,
  statutDuFiltre,
} from '../passage/passage'
import type { FiltreAnomalies } from '../passage/passage'
import type { Anomalie } from '../types/passage'
import { depuisChaineIso, formaterDateLongue, jourIso } from '../utils/dates'
import './Reference.css'
import './Anomalies.css'

function messageDe(echec: unknown, repli: string): string {
  return echec instanceof Error ? echec.message : repli
}

/** Un jour du serveur, minuit UTC : par la sous-chaîne (CLAUDE.md §10). */
function jourDe(horodatage: string): string {
  return formaterDateLongue(depuisChaineIso(jourIso(horodatage)))
}

/** Un instant du serveur : l'heure locale, donc `new Date` est juste ici. */
function instantDe(horodatage: string): string {
  return `${formaterDateLongue(new Date(horodatage))} à ${heureDe(horodatage)}`
}

interface Obtenue {
  cle: string
  page: Page<Anomalie>
}

/**
 * Les anomalies signalées par les agents, et le geste « Traitée » (SPEC.md, étape 25,
 * lot C). Les trois rôles internes ; le chef d'équipe ne lit que celles de son équipe,
 * par le filtre global des vacations.
 *
 * ⚠️ Les photos ne sont jamais posées par leur URL d'API dans un `<img>` : elles passent
 * par `lirePhoto`, qui seul sait rafraîchir une session, puis par une URL `blob:` —
 * d'où `blob:` dans `img-src` (`csp.ts`).
 */
export function Anomalies() {
  const [filtre, setFiltre] = useState<FiltreAnomalies>('Ouverte')
  const [page, setPage] = useState(1)
  const [rechargement, setRechargement] = useState(0)

  const [obtenue, setObtenue] = useState<Obtenue | null>(null)
  const [erreur, setErreur] = useState<{ cle: string; message: string } | null>(null)

  const [enCours, setEnCours] = useState<string | null>(null)
  const [annonce, setAnnonce] = useState<string | null>(null)
  const [refus, setRefus] = useState<string | null>(null)

  const cle = `${filtre}-${String(page)}-${String(rechargement)}`
  const enErreur = erreur?.cle === cle
  const chargement = obtenue?.cle !== cle && !enErreur

  useEffect(() => {
    const controleur = new AbortController()

    listerAnomalies(page, TailleParDefaut, statutDuFiltre(filtre), controleur.signal)
      .then((recue) => {
        setObtenue({ cle, page: recue })
      })
      .catch((echec: unknown) => {
        if (controleur.signal.aborted) return
        setErreur({ cle, message: messageDe(echec, 'Anomalies indisponibles.') })
      })

    return () => {
      controleur.abort()
    }
  }, [cle, filtre, page])

  async function traiter(anomalie: Anomalie) {
    setEnCours(anomalie.id)
    setRefus(null)
    setAnnonce(null)

    try {
      await traiterAnomalie(anomalie.id)

      setAnnonce(`Anomalie de « ${anomalie.site} » marquée traitée.`)
      // Relire plutôt que modifier la ligne : sous le filtre « Ouvertes », elle sort de la
      // liste, et le total doit le dire.
      setRechargement((n) => n + 1)
    } catch (echec: unknown) {
      setRefus(messageDe(echec, 'L’anomalie n’a pas été marquée traitée.'))
    } finally {
      setEnCours(null)
    }
  }

  return (
    <>
      <EnTetePage
        titre="Anomalies"
        sousTitre="Ce que les agents ont constaté sur place, et ce qu’il reste à traiter"
        actions={
          <label className="anomalies__filtre">
            <span className="anomalies__filtre-libelle">Afficher</span>
            <select
              className="anomalies__filtre-liste"
              value={filtre}
              onChange={(evenement) => {
                setFiltre(evenement.target.value as FiltreAnomalies)
                setPage(1)
              }}
            >
              {FiltresAnomalies.map((valeur) => (
                <option key={valeur} value={valeur}>
                  {LibellesFiltreAnomalies[valeur]}
                </option>
              ))}
            </select>
          </label>
        }
      />

      {annonce !== null && <output className="reference__annonce">{annonce}</output>}

      {refus !== null && (
        <p className="reference__erreur" role="alert">
          <strong className="reference__alerte-mot">Refusé —</strong> {refus}
        </p>
      )}

      {enErreur && (
        <p className="reference__erreur" role="alert">
          <strong className="reference__alerte-mot">Erreur —</strong> {erreur.message}
        </p>
      )}

      {!enErreur && obtenue === null && <output className="reference__attente">Chargement…</output>}

      {!enErreur && obtenue !== null && (
        <div aria-busy={chargement}>
          {obtenue.page.elements.length === 0 ? (
            <p className="anomalies__vide">
              {filtre === 'Ouverte' ? 'Aucune anomalie ouverte.' : 'Aucune anomalie.'}
            </p>
          ) : (
            <ul className="anomalies__liste">
              {obtenue.page.elements.map((anomalie) => (
                <li key={anomalie.id} className="anomalies__carte">
                  <div className="anomalies__entete">
                    <p className="anomalies__site">{anomalie.site}</p>
                    <span className={`anomalies__statut anomalies__statut--${anomalie.statut}`}>
                      {LibellesStatutAnomalie[anomalie.statut]}
                    </span>
                  </div>

                  <p className="anomalies__contexte">
                    Vacation du {jourDe(anomalie.datePrevue)} · signalée le {instantDe(anomalie.dateSignalement)}
                  </p>

                  <p className="anomalies__description">{anomalie.description}</p>

                  {anomalie.photos.length > 0 && (
                    <ul className="anomalies__photos" aria-label="Photos">
                      {anomalie.photos.map((photoId, index) => (
                        <li key={photoId}>
                          <Vignette
                            anomalieId={anomalie.id}
                            photoId={photoId}
                            libelle={`Prise ${String(index + 1)} sur ${String(anomalie.photos.length)}, ${anomalie.site}`}
                          />
                        </li>
                      ))}
                    </ul>
                  )}

                  <div className="anomalies__pied">
                    {anomalie.statut === 'Ouverte' ? (
                      <button
                        className="reference__action"
                        type="button"
                        disabled={enCours !== null}
                        onClick={() => void traiter(anomalie)}
                      >
                        {enCours === anomalie.id ? 'Enregistrement…' : 'Marquer traitée'}
                      </button>
                    ) : (
                      anomalie.dateTraitement !== null && (
                        <p className="anomalies__contexte">Traitée le {instantDe(anomalie.dateTraitement)}</p>
                      )
                    )}
                  </div>
                </li>
              ))}
            </ul>
          )}

          <Pagination
            page={obtenue.page.page}
            taille={obtenue.page.taille}
            total={obtenue.page.total}
            chargement={chargement}
            onChanger={setPage}
          />
        </div>
      )}
    </>
  )
}

interface ProprietesVignette {
  anomalieId: string
  photoId: string
  libelle: string
}

/**
 * Une photo, lue par l'API puis montrée par une URL `blob:`, révoquée au démontage. Un
 * clic l'agrandit : une vignette ne suffit pas à juger d'une fuite ou d'un dégât.
 */
function Vignette({ anomalieId, photoId, libelle }: ProprietesVignette) {
  const [url, setUrl] = useState<string | null>(null)
  const [echec, setEchec] = useState(false)
  const [grande, setGrande] = useState(false)

  useEffect(() => {
    const controleur = new AbortController()
    let creee: string | null = null

    lirePhoto(anomalieId, photoId, controleur.signal)
      .then((blob) => {
        creee = URL.createObjectURL(blob)
        setUrl(creee)
      })
      .catch(() => {
        if (!controleur.signal.aborted) setEchec(true)
      })

    return () => {
      controleur.abort()
      if (creee !== null) URL.revokeObjectURL(creee)
    }
  }, [anomalieId, photoId])

  if (echec) return <p className="anomalies__photo-absente">Photo illisible</p>

  if (url === null) return <p className="anomalies__photo-absente">Photo…</p>

  return (
    <button
      className={grande ? 'anomalies__photo anomalies__photo--grande' : 'anomalies__photo'}
      type="button"
      aria-pressed={grande}
      title={grande ? 'Réduire' : 'Agrandir'}
      onClick={() => {
        setGrande(!grande)
      }}
    >
      <img src={url} alt={libelle} />
    </button>
  )
}
