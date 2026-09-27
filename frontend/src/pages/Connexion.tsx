import { useState } from 'react'
import type { FormEvent } from 'react'
import { Navigate, useLocation } from 'react-router-dom'

import { useAuth } from '../auth/contexte'
import './Connexion.css'

/**
 * La racine, pas un écran nommé : c'est `Accueil` dans `App.tsx` qui choisit la
 * première section ouverte au profil. Viser un écran en dur ici pourrait envoyer
 * sur une section fermée au rôle qui vient de se connecter.
 */
const ApresConnexion = '/'

export function Connexion() {
  const { profil, restauration, connecter } = useAuth()
  const emplacement = useLocation()

  const [email, setEmail] = useState('')
  const [motDePasse, setMotDePasse] = useState('')
  const [envoi, setEnvoi] = useState(false)
  const [erreur, setErreur] = useState<string | null>(null)

  // Écran demandé avant la redirection, posé par RouteProtegee.
  const depuis = (emplacement.state as { depuis?: string } | null)?.depuis ?? ApresConnexion

  async function soumettre(evenement: FormEvent<HTMLFormElement>) {
    evenement.preventDefault()
    setErreur(null)
    setEnvoi(true)

    try {
      await connecter(email, motDePasse)
    } catch (echec: unknown) {
      // Le message vient du serveur et est déjà en français — le réécrire ici le
      // ferait diverger de l'autorité qui l'a produit.
      setErreur(echec instanceof Error ? echec.message : 'Connexion impossible.')
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

  if (profil !== null) return <Navigate to={depuis} replace />

  return (
    <main className="connexion">
      <form className="connexion__carte" onSubmit={soumettre} noValidate>
        <p className="connexion__marque" aria-hidden="true">
          K
        </p>

        <h1 className="connexion__titre">KLEAN</h1>
        <p className="connexion__accroche">Gestion opérationnelle du nettoyage</p>

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

        {/* La couleur ne porte pas seule l'information : le mot « Erreur » est
            écrit, et role="alert" le fait annoncer par un lecteur d'écran. */}
        {erreur !== null && (
          <p className="connexion__erreur" role="alert">
            <strong className="connexion__erreur-mot">Erreur —</strong> {erreur}
          </p>
        )}

        <button className="connexion__bouton" type="submit" disabled={envoi}>
          {envoi ? 'Connexion…' : 'Se connecter'}
        </button>
      </form>
    </main>
  )
}
