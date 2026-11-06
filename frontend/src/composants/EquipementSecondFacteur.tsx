import { useState } from 'react'
import type { FormEvent } from 'react'

import { ErreurApi } from '../api/client'
import { coteQr, saisieDe, traceQr } from '../auth/secondFacteur'
import type { CodesRecuperation as Codes, PreparationSecondFacteur, Session } from '../types/auth'
import { CodesRecuperation } from './CodesRecuperation'
import './EquipementSecondFacteur.css'

interface Proprietes {
  preparer: () => Promise<PreparationSecondFacteur>
  activer: (code: string) => Promise<Codes>
  /** Les codes ont été vus : la page adopte la session, ou recharge l'état. */
  surTermine: (session: Session | null) => void
  /**
   * Un 401 : à la connexion, le défi de cinq minutes est échu, et il faut recommencer
   * par le mot de passe. Sans ce rappel, l'erreur s'affiche ici.
   */
  surDefiPerdu?: (message: string) => void
}

type Etat =
  | { etape: 'invite' }
  | { etape: 'scan'; preparation: PreparationSecondFacteur }
  | { etape: 'codes'; codes: Codes }

/**
 * Équiper un téléphone : afficher le QR code, vérifier un code, remettre les codes de
 * récupération (SPEC.md, étape 23). Servi à la connexion d'un compte obligé, et à
 * l'écran du compte.
 *
 * Les appels sont passés par la page (CLAUDE.md §11) : le même geste vise une session
 * sur l'écran du compte, et le défi de la connexion ailleurs.
 *
 * ⚠️ **La préparation part d'un clic, jamais du montage.** Chaque appel tire une clé
 * neuve et remplace la précédente : le double montage de `StrictMode` en lancerait deux,
 * et si leurs réponses se croisaient, l'écran montrerait une clé que le serveur a déjà
 * remplacée — un QR code qui ne marchera jamais, sans aucune erreur.
 */
export function EquipementSecondFacteur({ preparer, activer, surTermine, surDefiPerdu }: Proprietes) {
  const [etat, setEtat] = useState<Etat>({ etape: 'invite' })
  const [code, setCode] = useState('')
  const [envoi, setEnvoi] = useState(false)
  const [erreur, setErreur] = useState<string | null>(null)

  function echouer(echec: unknown, repli: string) {
    const message = echec instanceof Error ? echec.message : repli

    if (echec instanceof ErreurApi && echec.statut === 401 && surDefiPerdu !== undefined) {
      surDefiPerdu(message)
      return
    }

    setErreur(message)
  }

  async function afficher() {
    setErreur(null)
    setEnvoi(true)

    try {
      setEtat({ etape: 'scan', preparation: await preparer() })
      setCode('')
    } catch (echec: unknown) {
      echouer(echec, 'Préparation impossible.')
    } finally {
      setEnvoi(false)
    }
  }

  async function valider(evenement: FormEvent<HTMLFormElement>) {
    evenement.preventDefault()
    setErreur(null)

    const saisie = saisieDe(code, false)

    if ('refus' in saisie) {
      setErreur(saisie.refus)
      return
    }

    setEnvoi(true)

    try {
      setEtat({ etape: 'codes', codes: await activer('code' in saisie.corps ? saisie.corps.code : '') })
    } catch (echec: unknown) {
      echouer(echec, 'Activation impossible.')
      setCode('')
    } finally {
      setEnvoi(false)
    }
  }

  if (etat.etape === 'codes') {
    return (
      <CodesRecuperation
        codes={etat.codes.codes}
        libelleSuite="Continuer"
        surSuite={() => surTermine(etat.codes.session)}
      />
    )
  }

  const erreurAffichee = erreur !== null && (
    <p className="equipement__erreur" role="alert">
      <strong className="equipement__erreur-mot">Erreur —</strong> {erreur}
    </p>
  )

  if (etat.etape === 'invite') {
    return (
      <div className="equipement">
        <ol className="equipement__etapes">
          <li>Installer une application d’authentification sur le téléphone : Aegis, FreeOTP, Google Authenticator, Microsoft Authenticator…</li>
          <li>Afficher le QR code, et le scanner depuis l’application.</li>
          <li>Saisir le code à six chiffres qu’elle affiche.</li>
        </ol>

        {erreurAffichee}

        <button className="equipement__bouton" type="button" disabled={envoi} onClick={() => void afficher()}>
          {envoi ? 'Préparation…' : 'Afficher le QR code'}
        </button>
      </div>
    )
  }

  const { preparation } = etat
  const cote = coteQr(preparation.modules)

  return (
    <form className="equipement" onSubmit={valider} noValidate>
      <svg
        className="equipement__qr"
        viewBox={`0 0 ${String(cote)} ${String(cote)}`}
        aria-labelledby="equipement-qr-titre"
        shapeRendering="crispEdges"
      >
        <title id="equipement-qr-titre">QR code à scanner avec l’application d’authentification</title>
        <rect className="equipement__qr-fond" width={cote} height={cote} />
        <path className="equipement__qr-modules" d={traceQr(preparation.modules)} />
      </svg>

      <div className="equipement__cle">
        <p className="equipement__cle-libelle">Sans appareil photo, saisir cette clé dans l’application :</p>
        <p className="equipement__cle-valeur">{preparation.cle}</p>
      </div>

      <label className="equipement__etiquette" htmlFor="equipement-code">
        Code affiché par l’application
      </label>
      <input
        id="equipement-code"
        className="equipement__champ"
        inputMode="numeric"
        autoComplete="one-time-code"
        value={code}
        onChange={(evenement) => setCode(evenement.target.value)}
      />

      {erreurAffichee}

      <div className="equipement__actions">
        <button className="equipement__bouton" type="submit" disabled={envoi}>
          {envoi ? 'Vérification…' : 'Activer'}
        </button>

        <button className="equipement__secondaire" type="button" disabled={envoi} onClick={() => void afficher()}>
          Nouveau QR code
        </button>
      </div>
    </form>
  )
}
