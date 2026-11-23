import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'

import { ErreurApi } from '../api/client'
import { installer, lireEtatInstallation } from '../api/installation'
import {
  corpsInstallation,
  refusCode,
  refusCompte,
  refusDInstallation,
  refusEntreprise,
  SaisieVide,
} from '../installation/installation'
import type { Etape, Saisie } from '../installation/installation'
import { LibellesTerritoire, Territoires } from '../reference/descriptions'
import './Connexion.css'
import './Installation.css'

/** La barre d'étapes : quatre repères, aucun n'est un lien. */
const Etapes: readonly { etape: Etape; libelle: string }[] = [
  { etape: 'Code', libelle: 'Code' },
  { etape: 'Entreprise', libelle: 'Entreprise' },
  { etape: 'Compte', libelle: 'Compte' },
  { etape: 'Savoir', libelle: 'À savoir' },
]

const Titres: Readonly<Record<Etape, string>> = {
  Code: 'Le code d’installation',
  Entreprise: 'Votre entreprise',
  Compte: 'Votre compte d’administrateur',
  Savoir: 'Ce que vous devez savoir',
}

/** `en-cours` : la lecture de l'état au montage ; `inconnu` : elle a échoué, l'assistant reste. */
type Lecture = 'en-cours' | 'libre' | 'installee' | 'inconnu'

const IdErreur = 'installation-erreur'

/**
 * L'assistant d'installation (SPEC.md, étape 34, lot A2) : le code affiché par le script,
 * l'entreprise, le compte d'administrateur, puis ce que le patron doit savoir avant d'y mettre
 * les données de ses salariés.
 *
 * ⚠️ Toute la validation est dans `installation/installation.ts`, et elle passe **avant** tout
 * appel : le serveur compte les codes faux. Le code n'est vérifié par le serveur qu'au dernier
 * envoi — aucune route ne le vérifie seul.
 *
 * ⚠️ Le code ne passe jamais dans une URL : il vit dans l'état de ce composant, et rien d'autre.
 *
 * Pas de `useAuth` : il n'y a aucune session à attendre, et le POST n'en ouvre aucune. La
 * connexion qui suit fait équiper le second facteur (étape 23).
 *
 * Pas de boucle avec la connexion : on n'y renvoie que si le serveur dit « installée », et elle
 * ne renvoie ici que s'il dit le contraire.
 */
export function Installation() {
  const naviguer = useNavigate()

  const [saisie, setSaisie] = useState<Saisie>(SaisieVide)
  const [etape, setEtape] = useState<Etape>('Code')
  const [envoi, setEnvoi] = useState(false)
  const [erreur, setErreur] = useState<string | null>(null)
  // Le seul champ fautif qu'on sache désigner sans recopier les règles de `installation.ts`.
  const [codeFautif, setCodeFautif] = useState(false)
  // Un 409 qui n'est pas « déjà installé » : l'entreprise orpheline, SIRET pris compris.
  // L'assistant s'arrête là : rien de ce qu'on y saisirait ne lève le blocage.
  const [bloquee, setBloquee] = useState(false)
  const [lecture, setLecture] = useState<Lecture>('en-cours')

  const titre = useRef<HTMLHeadingElement>(null)
  const etapeVue = useRef<Etape>('Code')
  // L'envoi en cours, abandonné si l'écran se démonte : sa réponse n'aurait plus personne à qui
  // parler.
  const envoiEnCours = useRef<AbortController | null>(null)

  useEffect(() => () => envoiEnCours.current?.abort(), [])

  useEffect(() => {
    // Même forme que la connexion : un contrôleur et une minuterie, sans `AbortSignal.any`
    // ni `AbortSignal.timeout`, absents des navigateurs d'avant 2023-2024.
    const controleur = new AbortController()
    let demonte = false

    // Cinq secondes au plus : passé ce délai, l'assistant s'affiche, et le POST dira ce
    // qu'il en est.
    const minuterie = window.setTimeout(() => controleur.abort(), 5000)

    lireEtatInstallation(controleur.signal)
      .then((etat) => {
        if (!demonte) setLecture(etat.installee ? 'installee' : 'libre')
      })
      .catch(() => {
        // Un abandon au démontage — le `StrictMode` monte deux fois — n'est ni une erreur ni
        // un état. Une panne, un 429 ou l'expiration laissent l'assistant.
        if (!demonte) setLecture('inconnu')
      })
      .finally(() => window.clearTimeout(minuterie))

    return () => {
      demonte = true
      window.clearTimeout(minuterie)
      controleur.abort()
    }
  }, [])

  // Le focus suit le titre à chaque changement d'écran. Comparé à l'étape vue, et non à un
  // drapeau de premier rendu : le `StrictMode` rejoue l'effet, et le focus ne doit pas
  // partir au montage.
  useEffect(() => {
    if (etapeVue.current === etape) return
    etapeVue.current = etape
    titre.current?.focus()
  }, [etape])

  // Un refus du serveur sur le même écran : le bouton, désactivé pendant l'envoi, a perdu
  // le focus. Il revient au titre, au-dessus du message, plutôt qu'au début de la page.
  useEffect(() => {
    if (erreur === null) return
    if (document.activeElement === null || document.activeElement === document.body) titre.current?.focus()
  }, [erreur])

  function changer<K extends keyof Saisie>(champ: K, valeur: Saisie[K]) {
    setSaisie((avant) => ({ ...avant, [champ]: valeur }))
  }

  function aller(suivante: Etape, message: string | null = null) {
    // Le mot de passe ne survit à aucun changement d'écran : la saisie, si.
    setSaisie((avant) => ({ ...avant, motDePasse: '', confirmation: '' }))
    setErreur(message)
    setCodeFautif(false)
    setEtape(suivante)
  }

  function soumettreCode(evenement: FormEvent<HTMLFormElement>) {
    evenement.preventDefault()

    const refus = refusCode(saisie.code)
    setErreur(refus)
    setCodeFautif(refus !== null)
    if (refus === null) aller('Entreprise')
  }

  function soumettreEntreprise(evenement: FormEvent<HTMLFormElement>) {
    evenement.preventDefault()

    const refus = refusEntreprise(saisie)
    setErreur(refus)
    if (refus === null) aller('Compte')
  }

  async function soumettreCompte(evenement: FormEvent<HTMLFormElement>) {
    evenement.preventDefault()

    const refus = refusCompte(saisie)
    setErreur(refus)
    if (refus !== null) return

    const controleur = new AbortController()
    envoiEnCours.current = controleur
    setEnvoi(true)

    try {
      await installer(corpsInstallation(saisie), controleur.signal)

      // Plus rien de la saisie n'a d'usage : ni le code, qui ne sert qu'une fois, ni le mot
      // de passe. L'écran 4 ne la relit pas.
      setSaisie(SaisieVide)
      setErreur(null)
      setEtape('Savoir')
    } catch (echec: unknown) {
      if (!controleur.signal.aborted) refuser(echec)
    } finally {
      envoiEnCours.current = null
      setEnvoi(false)
    }
  }

  function refuser(echec: unknown) {
    if (!(echec instanceof ErreurApi)) {
      // Une panne de réseau rend un `TypeError` en anglais, que l'écran ne peut pas montrer.
      setErreur('Le serveur ne répond pas. Vérifiez la connexion, puis réessayez.')
      return
    }

    const { suite, message } = refusDInstallation(echec.statut, echec.message)

    if (suite === 'Code') {
      aller('Code', message)
      setCodeFautif(true)
      return
    }

    // Le message du serveur suffit à le savoir : aucune relecture de l'état, dont l'échec
    // ne doit jamais valoir « pas installé ».
    if (suite === 'Connexion') {
      setLecture('installee')
      return
    }

    setErreur(message)

    if (suite === 'Bloquee') {
      setBloquee(true)
      return
    }

    // 400 : un champ, ou la règle d'Identity sur le mot de passe — on ressaisit. Un 429 ou
    // une panne du serveur laissent la saisie telle quelle.
    if (echec.statut === 400) setSaisie((avant) => ({ ...avant, motDePasse: '', confirmation: '' }))
  }

  if (lecture === 'en-cours') {
    return (
      <main className="connexion">
        <output className="connexion__attente">Vérification…</output>
      </main>
    )
  }

  // L'écran 4 suit la création : une lecture lente qui dirait « installée » ne le chasse pas.
  if (lecture === 'installee' && etape !== 'Savoir') return <Navigate to="/connexion" replace />

  // Le mot « Erreur » est écrit, et role="alert" le fait annoncer : la couleur ne porte pas
  // seule l'information. Rendu avant que le focus ne bouge.
  const erreurAffichee = erreur !== null && (
    <p id={IdErreur} className="connexion__erreur" role="alert">
      <strong className="connexion__erreur-mot">Erreur —</strong> {erreur}
    </p>
  )

  const position = Etapes.findIndex((e) => e.etape === etape)

  const enTete = (
    <>
      <p className="connexion__marque" aria-hidden="true">
        K
      </p>

      <h1 className="connexion__titre">KLEAN</h1>
      <p className="connexion__accroche">Installation</p>

      <ol className="installation__etapes" aria-label="Étapes de l’installation">
        {Etapes.map((e, index) => (
          <li
            key={e.etape}
            className="installation__etape"
            aria-current={index === position ? 'step' : undefined}
          >
            <span className="installation__numero">{index + 1}</span> {e.libelle}
            {index < position && ' — terminée'}
          </li>
        ))}
      </ol>

      <h2
        ref={titre}
        className="installation__titre"
        tabIndex={-1}
        aria-describedby={erreur !== null ? IdErreur : undefined}
      >
        {Titres[etape]}
      </h2>
    </>
  )

  const retour = (precedente: Etape) => (
    <button className="connexion__lien" type="button" disabled={envoi} onClick={() => aller(precedente)}>
      Retour
    </button>
  )

  if (etape === 'Savoir') {
    return (
      <main className="connexion">
        <div className="connexion__carte connexion__carte--large">
          {enTete}

          <p className="connexion__explication installation__premier">
            <strong>Connectez-vous maintenant</strong> : KLEAN vous fait équiper la double
            authentification dès la première connexion. Prévoyez votre téléphone et une
            application d’authentification.
          </p>

          <ul className="installation__savoir">
            <li>
              Vous êtes <strong>responsable du traitement</strong> des données de vos salariés
              et de vos clients.
            </li>
            <li>
              La <strong>note d’information aux agents</strong> se remet avant d’importer leurs
              données, et avant qu’ils utilisent <em>Ma journée</em>. Son modèle est dans le
              fichier RGPD.md, §7, du dépôt de KLEAN.
            </li>
            <li>
              Les <strong>durées de conservation</strong> proposées, au §5 du même fichier,
              sont à valider.
            </li>
            <li>
              La <strong>localisation automatique des sites</strong> est désactivée : les
              coordonnées se saisissent à la main.
              Elle se proposera avec une prochaine version, sur votre choix.
            </li>
          </ul>

          <button className="connexion__bouton" type="button" onClick={() => naviguer('/connexion', { replace: true })}>
            Se connecter
          </button>
        </div>
      </main>
    )
  }

  if (etape === 'Code') {
    return (
      <main className="connexion">
        <div className="connexion__carte">
          {enTete}

          <form className="installation__formulaire" onSubmit={soumettreCode} noValidate>
            <p id="installation-aide-code" className="connexion__explication">
              Le code s’affiche à la fin de l’installation sur le serveur, et à nouveau par{' '}
              <code>sudo klean code</code>.
            </p>

            <label className="connexion__etiquette" htmlFor="installation-code">
              Code d’installation
            </label>
            <input
              id="installation-code"
              className="connexion__champ installation__code"
              autoComplete="off"
              spellCheck={false}
              autoCapitalize="characters"
              required
              aria-invalid={codeFautif || undefined}
              aria-describedby={codeFautif && erreur !== null ? `installation-aide-code ${IdErreur}` : 'installation-aide-code'}
              value={saisie.code}
              onChange={(evenement) => {
                changer('code', evenement.target.value)
                setCodeFautif(false)
              }}
            />

            {erreurAffichee}

            <button className="connexion__bouton" type="submit">
              Continuer
            </button>
          </form>
        </div>
      </main>
    )
  }

  if (etape === 'Entreprise') {
    return (
      <main className="connexion">
        <div className="connexion__carte">
          {enTete}

          <form className="installation__formulaire" onSubmit={soumettreEntreprise} noValidate>
            <label className="connexion__etiquette" htmlFor="installation-raison-sociale">
              Raison sociale
            </label>
            <input
              id="installation-raison-sociale"
              className="connexion__champ"
              autoComplete="organization"
              required
              maxLength={200}
              value={saisie.raisonSociale}
              onChange={(evenement) => changer('raisonSociale', evenement.target.value)}
            />

            <label className="connexion__etiquette" htmlFor="installation-siret">
              SIRET
            </label>
            <input
              id="installation-siret"
              className="connexion__champ"
              inputMode="numeric"
              autoComplete="off"
              required
              aria-describedby="installation-aide-siret"
              value={saisie.siret}
              onChange={(evenement) => changer('siret', evenement.target.value)}
            />
            <p id="installation-aide-siret" className="installation__aide">
              Quatorze chiffres ; les espaces sont acceptées.
            </p>

            <label className="connexion__etiquette" htmlFor="installation-territoire">
              Territoire
            </label>
            <select
              id="installation-territoire"
              className="connexion__champ"
              required
              aria-describedby="installation-aide-territoire"
              value={saisie.territoire}
              onChange={(evenement) => changer('territoire', evenement.target.value)}
            >
              {Territoires.map((territoire) => (
                <option key={territoire} value={territoire}>
                  {LibellesTerritoire[territoire]}
                </option>
              ))}
            </select>
            <p id="installation-aide-territoire" className="installation__aide">
              Il fixe les jours fériés et l’heure de la journée.
            </p>

            {erreurAffichee}

            <button className="connexion__bouton" type="submit">
              Continuer
            </button>

            {retour('Code')}
          </form>
        </div>
      </main>
    )
  }

  // ⚠️ Ni retour à l'entreprise ni nouvel envoi : changer de SIRET contournerait le blocage et
  // laisserait l'orpheline en base. Elle se retire à la main (DEPLOIEMENT.md). Ni lien vers la
  // connexion : sans aucun compte, elle renvoie ici, saisie perdue (vu au navigateur).
  if (bloquee) {
    return (
      <main className="connexion">
        <div className="connexion__carte">
          {enTete}

          {erreurAffichee}

          <p className="connexion__explication">
            Une fois l’entreprise retirée et KLEAN redémarré, rechargez cette page.
          </p>
        </div>
      </main>
    )
  }

  return (
    <main className="connexion">
      <div className="connexion__carte">
        {enTete}

        <form className="installation__formulaire" onSubmit={soumettreCompte} noValidate>
          <label className="connexion__etiquette" htmlFor="installation-email">
            Adresse électronique
          </label>
          <input
            id="installation-email"
            className="connexion__champ"
            type="email"
            autoComplete="username"
            required
            maxLength={256}
            value={saisie.email}
            onChange={(evenement) => changer('email', evenement.target.value)}
          />

          <label className="connexion__etiquette" htmlFor="installation-email-confirmation">
            La même adresse, une seconde fois
          </label>
          <input
            id="installation-email-confirmation"
            className="connexion__champ"
            type="email"
            autoComplete="off"
            required
            maxLength={256}
            value={saisie.emailConfirmation}
            onChange={(evenement) => changer('emailConfirmation', evenement.target.value)}
          />

          <label className="connexion__etiquette" htmlFor="installation-nom">
            Nom
          </label>
          <input
            id="installation-nom"
            className="connexion__champ"
            // oxlint-disable-next-line jsx-a11y/autocomplete-valid -- faux positif : jetons valides de la norme HTML
            autoComplete="family-name"
            required
            maxLength={100}
            value={saisie.nom}
            onChange={(evenement) => changer('nom', evenement.target.value)}
          />

          <label className="connexion__etiquette" htmlFor="installation-prenom">
            Prénom
          </label>
          <input
            id="installation-prenom"
            className="connexion__champ"
            // oxlint-disable-next-line jsx-a11y/autocomplete-valid -- faux positif : jetons valides de la norme HTML
            autoComplete="given-name"
            required
            maxLength={100}
            value={saisie.prenom}
            onChange={(evenement) => changer('prenom', evenement.target.value)}
          />

          <label className="connexion__etiquette" htmlFor="installation-mot-de-passe">
            Mot de passe
          </label>
          <input
            id="installation-mot-de-passe"
            className="connexion__champ"
            type="password"
            autoComplete="new-password"
            required
            aria-describedby="installation-aide-mot-de-passe"
            value={saisie.motDePasse}
            onChange={(evenement) => changer('motDePasse', evenement.target.value)}
          />
          <p id="installation-aide-mot-de-passe" className="installation__aide">
            Dix caractères au moins, avec une majuscule, une minuscule et un chiffre.
          </p>

          <label className="connexion__etiquette" htmlFor="installation-confirmation">
            Le même, une seconde fois
          </label>
          <input
            id="installation-confirmation"
            className="connexion__champ"
            type="password"
            autoComplete="new-password"
            required
            value={saisie.confirmation}
            onChange={(evenement) => changer('confirmation', evenement.target.value)}
          />

          {erreurAffichee}

          <button className="connexion__bouton" type="submit" disabled={envoi}>
            {envoi ? 'Création…' : 'Créer l’entreprise et le compte'}
          </button>

          {retour('Entreprise')}
        </form>
      </div>
    </main>
  )
}
