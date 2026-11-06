import { useState } from 'react'
import type { FormEvent } from 'react'
import { Navigate, useLocation } from 'react-router-dom'

import { activerSecondFacteur, preparerSecondFacteur } from '../api/secondFacteur'
import { ErreurApi } from '../api/client'
import { useAuth } from '../auth/contexte'
import { refusPremierMotDePasse } from '../auth/motDePasse'
import { LibellesEtape, saisieDe } from '../auth/secondFacteur'
import { EquipementSecondFacteur } from '../composants/EquipementSecondFacteur'
import { destinationApresConnexion } from '../layout/sections'
import type { EtapeConnexion } from '../types/auth'
import './Connexion.css'

type Etape = 'MotDePasse' | EtapeConnexion

/**
 * La connexion, en un, deux ou trois temps (SPEC.md, étape 23) : le mot de passe ;
 * puis le code de l'application si le compte est équipé ; ou l'équipement d'un
 * téléphone si son rôle l'impose et qu'il ne l'est pas encore.
 *
 * À la toute première connexion, un temps de plus passe avant (étape 24) : le mot de
 * passe saisi est le provisoire que l'administrateur a transmis, et la personne en
 * choisit un. Le serveur enchaîne ensuite sur l'étape qui suit, ou sur la session.
 *
 * Entre les deux temps, aucune session : seulement un cookie de défi de cinq minutes.
 * Un 401 au second temps veut dire « recommencer », et ramène au mot de passe avec le
 * message du serveur.
 */
export function Connexion() {
  const { profil, restauration, connecter, saisirCode, choisirPremierMotDePasse, adopterSession } = useAuth()
  const emplacement = useLocation()

  const [etape, setEtape] = useState<Etape>('MotDePasse')
  const [email, setEmail] = useState('')
  const [motDePasse, setMotDePasse] = useState('')
  const [code, setCode] = useState('')
  const [nouveau, setNouveau] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const [recuperation, setRecuperation] = useState(false)
  const [envoi, setEnvoi] = useState(false)
  const [erreur, setErreur] = useState<string | null>(null)

  // Écran demandé avant la redirection, posé par RouteProtegee — ou laissé par la
  // déconnexion du compte précédent : `destinationApresConnexion` ne l'honore que si le
  // rôle de celui-ci l'ouvre.
  const depuis = (emplacement.state as { depuis?: string } | null)?.depuis

  function recommencer(message: string | null = null) {
    setEtape('MotDePasse')
    setMotDePasse('')
    setCode('')
    setNouveau('')
    setConfirmation('')
    setRecuperation(false)
    setErreur(message)
  }

  async function soumettreMotDePasse(evenement: FormEvent<HTMLFormElement>) {
    evenement.preventDefault()
    setErreur(null)
    setEnvoi(true)

    try {
      const suite = await connecter(email, motDePasse)

      // `null` : la session est ouverte, le profil posé, et la redirection suit.
      if (suite !== null) {
        setEtape(suite)
        setCode('')
      }
    } catch (echec: unknown) {
      // Le message vient du serveur et est déjà en français — le réécrire ici le
      // ferait diverger de l'autorité qui l'a produit.
      setErreur(echec instanceof Error ? echec.message : 'Connexion impossible.')
    } finally {
      setEnvoi(false)
    }
  }

  async function soumettreNouveau(evenement: FormEvent<HTMLFormElement>) {
    evenement.preventDefault()
    setErreur(null)

    const refus = refusPremierMotDePasse(nouveau, confirmation)

    if (refus !== null) {
      setErreur(refus)
      return
    }

    setEnvoi(true)

    try {
      const suite = await choisirPremierMotDePasse(nouveau)

      if (suite !== null) {
        setEtape(suite)
        setCode('')
      }
    } catch (echec: unknown) {
      const message = echec instanceof Error ? echec.message : 'Changement impossible.'

      // 401 : le défi de cinq minutes est échu. 400 : la règle d'Identity, dite en
      // français par le serveur — on reste, et on ressaisit.
      if (echec instanceof ErreurApi && echec.statut === 401) recommencer(message)
      else {
        setErreur(message)
        setNouveau('')
        setConfirmation('')
      }
    } finally {
      setEnvoi(false)
    }
  }

  async function soumettreCode(evenement: FormEvent<HTMLFormElement>) {
    evenement.preventDefault()
    setErreur(null)

    // ⚠️ Refus local d'abord : côté serveur, une saisie mal formée compte dans les
    // cinq échecs du verrouillage (auth/secondFacteur.ts).
    const saisie = saisieDe(code, recuperation)

    if ('refus' in saisie) {
      setErreur(saisie.refus)
      return
    }

    setEnvoi(true)

    try {
      await saisirCode(saisie.corps)
    } catch (echec: unknown) {
      const message = echec instanceof Error ? echec.message : 'Vérification impossible.'

      if (echec instanceof ErreurApi && echec.statut === 401) recommencer(message)
      else {
        setErreur(message)
        setCode('')
      }
    } finally {
      setEnvoi(false)
    }
  }

  if (restauration) {
    return (
      <main className="connexion">
        <output className="connexion__attente">Vérification de la session…</output>
      </main>
    )
  }

  if (profil !== null) return <Navigate to={destinationApresConnexion(depuis, profil.roles)} replace />

  // Le mot « Erreur » est écrit, et role="alert" le fait annoncer par un lecteur
  // d'écran : la couleur ne porte pas seule l'information.
  const erreurAffichee = erreur !== null && (
    <p className="connexion__erreur" role="alert">
      <strong className="connexion__erreur-mot">Erreur —</strong> {erreur}
    </p>
  )

  const enTete = (
    <>
      <p className="connexion__marque" aria-hidden="true">
        K
      </p>

      <h1 className="connexion__titre">KLEAN</h1>
      <p className="connexion__accroche">
        {etape === 'MotDePasse' ? 'Gestion opérationnelle du nettoyage' : LibellesEtape[etape]}
      </p>
    </>
  )

  if (etape === 'Equipement') {
    return (
      <main className="connexion">
        <div className="connexion__carte connexion__carte--large">
          {enTete}

          <p className="connexion__explication">
            Votre rôle donne accès aux domiciles et aux arrêts maladie des salariés : il impose
            un second facteur, un code à six chiffres produit par une application sur votre
            téléphone. Cette étape ne se fait qu’une fois.
          </p>

          <EquipementSecondFacteur
            preparer={preparerSecondFacteur}
            activer={activerSecondFacteur}
            surTermine={(session) => {
              if (session !== null) adopterSession(session)
            }}
            surDefiPerdu={recommencer}
          />

          <button className="connexion__lien" type="button" onClick={() => recommencer()}>
            Revenir au mot de passe
          </button>
        </div>
      </main>
    )
  }

  if (etape === 'ChangementMotDePasse') {
    return (
      <main className="connexion">
        <form className="connexion__carte" onSubmit={soumettreNouveau} noValidate>
          {enTete}

          <p className="connexion__explication">
            Le mot de passe que vous avez reçu ne sert qu’une fois. Choisissez le vôtre : dix
            caractères au moins, avec une majuscule, une minuscule et un chiffre.
          </p>

          <label className="connexion__etiquette" htmlFor="nouveau-mot-de-passe">
            Nouveau mot de passe
          </label>
          <input
            id="nouveau-mot-de-passe"
            className="connexion__champ"
            type="password"
            autoComplete="new-password"
            value={nouveau}
            onChange={(evenement) => setNouveau(evenement.target.value)}
          />

          <label className="connexion__etiquette" htmlFor="confirmation-mot-de-passe">
            Le même, une seconde fois
          </label>
          <input
            id="confirmation-mot-de-passe"
            className="connexion__champ"
            type="password"
            autoComplete="new-password"
            value={confirmation}
            onChange={(evenement) => setConfirmation(evenement.target.value)}
          />

          {erreurAffichee}

          <button className="connexion__bouton" type="submit" disabled={envoi}>
            {envoi ? 'Enregistrement…' : 'Enregistrer et continuer'}
          </button>

          <button className="connexion__lien" type="button" onClick={() => recommencer()}>
            Revenir au mot de passe
          </button>
        </form>
      </main>
    )
  }

  if (etape === 'SecondFacteur') {
    return (
      <main className="connexion">
        <form className="connexion__carte" onSubmit={soumettreCode} noValidate>
          {enTete}

          <label className="connexion__etiquette" htmlFor="code">
            {recuperation ? 'Code de récupération' : 'Code affiché par l’application'}
          </label>
          <input
            id="code"
            className={`connexion__champ${recuperation ? '' : ' connexion__champ--code'}`}
            inputMode={recuperation ? 'text' : 'numeric'}
            autoComplete="one-time-code"
            autoCapitalize="characters"
            value={code}
            onChange={(evenement) => setCode(evenement.target.value)}
          />

          {erreurAffichee}

          <button className="connexion__bouton" type="submit" disabled={envoi}>
            {envoi ? 'Vérification…' : 'Valider'}
          </button>

          <button
            className="connexion__lien"
            type="button"
            onClick={() => {
              setRecuperation(!recuperation)
              setCode('')
              setErreur(null)
            }}
          >
            {recuperation ? 'Utiliser le code de l’application' : 'Téléphone perdu ? Utiliser un code de récupération'}
          </button>
        </form>
      </main>
    )
  }

  return (
    <main className="connexion">
      <form className="connexion__carte" onSubmit={soumettreMotDePasse} noValidate>
        {enTete}

        <label className="connexion__etiquette" htmlFor="courriel">
          Adresse électronique
        </label>
        <input
          id="courriel"
          className="connexion__champ"
          type="email"
          name="email"
          autoComplete="username"
          required
          value={email}
          onChange={(evenement) => setEmail(evenement.target.value)}
        />

        <label className="connexion__etiquette" htmlFor="mot-de-passe">
          Mot de passe
        </label>
        <input
          id="mot-de-passe"
          className="connexion__champ"
          type="password"
          name="motDePasse"
          autoComplete="current-password"
          required
          value={motDePasse}
          onChange={(evenement) => setMotDePasse(evenement.target.value)}
        />

        {erreurAffichee}

        <button className="connexion__bouton" type="submit" disabled={envoi}>
          {envoi ? 'Connexion…' : 'Se connecter'}
        </button>
      </form>
    </main>
  )
}
