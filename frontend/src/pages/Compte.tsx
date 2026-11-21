import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'

import { activerSecondFacteur, lireEtatSecondFacteur, preparerSecondFacteur, regenererCodes } from '../api/secondFacteur'
import { useAuth } from '../auth/contexte'
import { refusLocal } from '../auth/motDePasse'
import { saisieDe } from '../auth/secondFacteur'
import { ChoixApparence } from '../composants/ChoixApparence'
import { CodesRecuperation } from '../composants/CodesRecuperation'
import { EnTetePage } from '../composants/EnTetePage'
import { EquipementSecondFacteur } from '../composants/EquipementSecondFacteur'
import type { EtatSecondFacteur, SaisieSecondFacteur } from '../types/auth'
import './Compte.css'

/**
 * La règle de mot de passe, dite à l'utilisateur.
 *
 * ⚠️ C'est une **prose**, pas une validation. L'autorité sur la règle est Identity
 * côté serveur, et lui seul : un `minLength` ici, ou un `[MinLength]` sur le DTO,
 * dupliquerait une longueur qui finirait par diverger (CLAUDE.md §6). Identity
 * refuse et parle français depuis l'étape 12, son message traverse l'API tel quel
 * et s'affiche sous le formulaire.
 *
 * Mais la règle doit être écrite quelque part : on ne peut pas corriger ce qu'on
 * ne connaît pas, et découvrir une exigence de complexité par un refus est le
 * genre de friction qui fait abandonner un changement de mot de passe.
 */
const RegleMotDePasse =
  'Dix caractères minimum, avec au moins une majuscule, une minuscule et un chiffre. '
  + 'Aucun caractère spécial exigé.'

export function Compte() {
  const { profil, changerMotDePasse, deconnecterPartout } = useAuth()

  const [actuel, setActuel] = useState('')
  const [nouveau, setNouveau] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const [envoi, setEnvoi] = useState(false)
  const [erreur, setErreur] = useState<string | null>(null)
  const [annonce, setAnnonce] = useState<string | null>(null)

  const [aConfirmer, setAConfirmer] = useState(false)
  const [sortie, setSortie] = useState(false)
  const [erreurSortie, setErreurSortie] = useState<string | null>(null)

  // `RouteProtegee` garde déjà cet écran : un profil nul y est impossible. Le test
  // reste nécessaire pour TypeScript, et il ne peut rien afficher d'utile.
  if (profil === null) return null

  async function soumettre(evenement: FormEvent<HTMLFormElement>) {
    evenement.preventDefault()
    setErreur(null)
    setAnnonce(null)

    // ⚠️ Les deux refus que le serveur dirait **en anglais** : la validation de
    // modèle d'ASP.NET s'exécute avant Identity, donc hors d'atteinte du descripteur
    // français. Les arrêter ici n'est pas du confort, c'est la seule façon de rester
    // en français — voir `auth/motDePasse.ts`, où ils sont éprouvés.
    const refus = refusLocal(actuel, nouveau, confirmation)

    if (refus !== null) {
      setErreur(refus)
      return
    }

    setEnvoi(true)

    try {
      await changerMotDePasse(actuel, nouveau)

      setActuel('')
      setNouveau('')
      setConfirmation('')
      setAnnonce(
        'Mot de passe modifié. Cet appareil reste connecté ; tous les autres ont été '
        + 'déconnectés.',
      )
    } catch (echec: unknown) {
      // Le message vient d'Identity, en français. Le réécrire ici le ferait
      // diverger de l'autorité qui l'a produit.
      setErreur(echec instanceof Error ? echec.message : 'Changement impossible.')
    } finally {
      setEnvoi(false)
    }
  }

  async function toutDeconnecter() {
    setErreurSortie(null)
    setSortie(true)

    try {
      // Au succès, le profil tombe à null dans le contexte et `RouteProtegee`
      // renvoie à l'écran de connexion : il n'y a rien à annoncer ici, cet écran
      // n'existe plus.
      await deconnecterPartout()
    } catch (echec: unknown) {
      setErreurSortie(echec instanceof Error ? echec.message : 'Déconnexion impossible.')
      setSortie(false)
      setAConfirmer(false)
    }
  }

  return (
    <>
      <EnTetePage titre="Mon compte" sousTitre={profil.email} />

      <div className="compte">
        <section className="compte__carte" aria-labelledby="compte-identite">
          <h2 className="compte__titre" id="compte-identite">
            Identité
          </h2>

          <dl className="compte__definitions">
            <div className="compte__ligne">
              <dt className="compte__cle">Nom</dt>
              <dd className="compte__valeur">{profil.nom}</dd>
            </div>

            <div className="compte__ligne">
              <dt className="compte__cle">Prénom</dt>
              {/* Le prénom est facultatif à la création d'un compte : un tiret cadratin
                  dit « vide » sans laisser croire à un défaut d'affichage. */}
              <dd className="compte__valeur">{profil.prenom === '' ? '—' : profil.prenom}</dd>
            </div>

            <div className="compte__ligne">
              <dt className="compte__cle">Adresse électronique</dt>
              <dd className="compte__valeur">{profil.email}</dd>
            </div>

            <div className="compte__ligne">
              <dt className="compte__cle">{profil.roles.length > 1 ? 'Rôles' : 'Rôle'}</dt>
              <dd className="compte__valeur">{profil.roles.join(', ')}</dd>
            </div>
          </dl>

          <p className="compte__note">
            Ces informations sont modifiées par un administrateur de l'entreprise. Aucun
            endpoint ne permet de les changer soi-même, et c'est volontaire&nbsp;: le nom
            qui figure sur un planning n'est pas une préférence personnelle.
          </p>
        </section>

        {/* Une préférence de ce navigateur, pas du compte : elle n'appelle aucune route
            (SPEC.md, étape 32). */}
        <section className="compte__carte" aria-labelledby="compte-apparence">
          <h2 className="compte__titre" id="compte-apparence">
            Apparence
          </h2>
          <ChoixApparence idTitre="compte-apparence" />
        </section>

        <section className="compte__carte" aria-labelledby="compte-mot-de-passe">
          <h2 className="compte__titre" id="compte-mot-de-passe">
            Mot de passe
          </h2>

          <p className="compte__note">
            Le mot de passe actuel est demandé&nbsp;: c'est lui qui prouve l'identité, et
            sans lui une session volée suffirait à verrouiller le compte de son
            propriétaire.
          </p>

          <form className="compte__formulaire" onSubmit={soumettre} noValidate>
            <div className="compte__champ">
              <label className="compte__etiquette" htmlFor="mot-de-passe-actuel">
                Mot de passe actuel
              </label>
              <input
                id="mot-de-passe-actuel"
                className="compte__saisie"
                type="password"
                name="motDePasseActuel"
                autoComplete="current-password"
                required
                value={actuel}
                onChange={(evenement) => setActuel(evenement.target.value)}
              />
            </div>

            <div className="compte__champ">
              <label className="compte__etiquette" htmlFor="mot-de-passe-nouveau">
                Nouveau mot de passe
              </label>
              <input
                id="mot-de-passe-nouveau"
                className="compte__saisie"
                type="password"
                name="nouveauMotDePasse"
                autoComplete="new-password"
                aria-describedby="compte-regle"
                required
                value={nouveau}
                onChange={(evenement) => setNouveau(evenement.target.value)}
              />
              <p className="compte__aide" id="compte-regle">
                {RegleMotDePasse}
              </p>
            </div>

            <div className="compte__champ">
              <label className="compte__etiquette" htmlFor="mot-de-passe-confirmation">
                Nouveau mot de passe, à nouveau
              </label>
              <input
                id="mot-de-passe-confirmation"
                className="compte__saisie"
                type="password"
                name="confirmation"
                autoComplete="new-password"
                required
                value={confirmation}
                onChange={(evenement) => setConfirmation(evenement.target.value)}
              />
            </div>

            <p className="compte__consequence">
              Le changement déconnecte <strong>tous les autres appareils</strong>. Celui-ci
              reste connecté.
            </p>

            {/* La couleur ne porte pas seule l'information : le mot « Erreur » est
                écrit, et role="alert" le fait annoncer par un lecteur d'écran. */}
            {erreur !== null && (
              <p className="compte__erreur" role="alert">
                <strong className="compte__erreur-mot">Erreur —</strong> {erreur}
              </p>
            )}

            {/* <output> porte role="status" de façon implicite. */}
            {annonce !== null && <output className="compte__annonce">{annonce}</output>}

            <button className="compte__valider" type="submit" disabled={envoi}>
              {envoi ? 'Changement…' : 'Changer le mot de passe'}
            </button>
          </form>
        </section>

        <DoubleAuthentification />

        <section className="compte__carte" aria-labelledby="compte-appareils">
          <h2 className="compte__titre" id="compte-appareils">
            Appareils connectés
          </h2>

          <p className="compte__note">
            Une session reste ouverte quatorze jours et se renouvelle toute seule tant
            qu'elle sert. KLEAN ne tient pas la liste des appareils&nbsp;: il n'enregistre
            ni leur nom ni leur position, et ne saurait donc pas les nommer ici.
          </p>

          <p className="compte__note">
            Le geste ci-dessous est la réponse à «&nbsp;je crois que quelqu'un d'autre est
            connecté à mon compte&nbsp;». Il coupe toutes les sessions sans changer le mot
            de passe.
          </p>

          {erreurSortie !== null && (
            <p className="compte__erreur" role="alert">
              <strong className="compte__erreur-mot">Erreur —</strong> {erreurSortie}
            </p>
          )}

          {aConfirmer ? (
            <div className="compte__confirmation">
              <p className="compte__confirmation-texte">
                Déconnecter toutes les sessions, <strong>y compris celle-ci</strong>&nbsp;?
                Il faudra se reconnecter immédiatement.
              </p>

              <div className="compte__actions">
                <button
                  className="compte__danger"
                  type="button"
                  onClick={() => void toutDeconnecter()}
                  disabled={sortie}
                >
                  {sortie ? 'Déconnexion…' : 'Oui, tout déconnecter'}
                </button>

                <button
                  className="compte__annuler"
                  type="button"
                  onClick={() => setAConfirmer(false)}
                  disabled={sortie}
                >
                  Annuler
                </button>
              </div>
            </div>
          ) : (
            <button
              className="compte__declencheur"
              type="button"
              onClick={() => setAConfirmer(true)}
            >
              Déconnecter tous les appareils
            </button>
          )}
        </section>
      </div>
    </>
  )
}

type Chargement =
  | { etat: 'chargement' }
  | { etat: 'erreur'; message: string }
  | { etat: 'donnees'; valeur: EtatSecondFacteur }

/**
 * Le second facteur de son propre compte (SPEC.md, étape 23) : l'activer s'il est
 * facultatif, régénérer les codes de récupération, le retirer.
 *
 * Pour un compte que son rôle oblige, « Désactiver » s'appelle **« Changer de
 * téléphone »** et en a l'effet : toutes les sessions se ferment, celle-ci comprise, et
 * la connexion suivante impose l'équipement du nouveau. Un compte obligé est toujours
 * équipé ici — sans second facteur, il n'aurait pas de session pour voir cet écran.
 *
 * ⚠️ **Ce geste accepte un code de récupération**, et c'est son cas principal : qui
 * change de téléphone a souvent perdu l'ancien, et s'est connecté par un de ces codes.
 * Sur le code de l'application seul, il n'aurait aucune sortie sans un administrateur.
 * La régénération des codes, elle, reste sur le code de l'application.
 */
function DoubleAuthentification() {
  const { adopterSession, desactiverSecondFacteur } = useAuth()

  const [chargement, setChargement] = useState<Chargement>({ etat: 'chargement' })
  const [generation, setGeneration] = useState(0)
  const [mode, setMode] = useState<'repos' | 'equipement' | { codes: string[] }>('repos')
  const [code, setCode] = useState('')
  const [recuperation, setRecuperation] = useState(false)
  const [envoi, setEnvoi] = useState(false)
  const [erreur, setErreur] = useState<string | null>(null)
  const [annonce, setAnnonce] = useState<string | null>(null)

  useEffect(() => {
    const controleur = new AbortController()

    lireEtatSecondFacteur(controleur.signal)
      .then((valeur) => setChargement({ etat: 'donnees', valeur }))
      .catch((echec: unknown) => {
        if (controleur.signal.aborted) return
        setChargement({ etat: 'erreur', message: echec instanceof Error ? echec.message : 'État indisponible.' })
      })

    return () => {
      controleur.abort()
    }
  }, [generation])

  function recharger(message: string | null) {
    setMode('repos')
    setCode('')
    setRecuperation(false)
    setErreur(null)
    setAnnonce(message)
    setGeneration((precedente) => precedente + 1)
  }

  /**
   * La saisie à envoyer, ou `null` après avoir affiché le refus local — qui épargne un
   * essai au plafond de débit du serveur.
   */
  function saisieFaite(): SaisieSecondFacteur | null {
    setErreur(null)
    setAnnonce(null)

    const saisie = saisieDe(code, recuperation)

    if ('refus' in saisie) {
      setErreur(saisie.refus)
      return null
    }

    return saisie.corps
  }

  async function regenerer() {
    const saisie = saisieFaite()
    if (saisie === null || !('code' in saisie)) return

    setEnvoi(true)

    try {
      setMode({ codes: (await regenererCodes(saisie.code)).codes })
      setCode('')
    } catch (echec: unknown) {
      setErreur(echec instanceof Error ? echec.message : 'Régénération impossible.')
      setCode('')
    } finally {
      setEnvoi(false)
    }
  }

  async function desactiver(obligatoire: boolean) {
    const saisie = saisieFaite()
    if (saisie === null) return

    setEnvoi(true)

    try {
      // Obligatoire : la session tombe, et RouteProtegee renvoie à la connexion.
      // Rien à annoncer, cet écran n'existe plus.
      await desactiverSecondFacteur(saisie)

      if (!obligatoire) recharger('Double authentification désactivée. Cet appareil reste connecté.')
    } catch (echec: unknown) {
      setErreur(echec instanceof Error ? echec.message : 'Désactivation impossible.')
      setCode('')
    } finally {
      setEnvoi(false)
    }
  }

  const erreurAffichee = erreur !== null && (
    <p className="compte__erreur" role="alert">
      <strong className="compte__erreur-mot">Erreur —</strong> {erreur}
    </p>
  )

  return (
    <section className="compte__carte" aria-labelledby="compte-second-facteur">
      <h2 className="compte__titre" id="compte-second-facteur">
        Double authentification
      </h2>

      {chargement.etat === 'chargement' && <output className="compte__note">Chargement…</output>}

      {chargement.etat === 'erreur' && (
        <p className="compte__erreur" role="alert">
          <strong className="compte__erreur-mot">Erreur —</strong> {chargement.message}
        </p>
      )}

      {annonce !== null && <output className="compte__annonce">{annonce}</output>}

      {chargement.etat === 'donnees' && typeof mode === 'object' && (
        <CodesRecuperation
          codes={mode.codes}
          libelleSuite="Terminé"
          surSuite={() => recharger('Nouveaux codes enregistrés. Les anciens ne servent plus.')}
        />
      )}

      {chargement.etat === 'donnees' && mode === 'equipement' && (
        <>
          <EquipementSecondFacteur
            preparer={preparerSecondFacteur}
            activer={activerSecondFacteur}
            surTermine={(session) => {
              if (session !== null) adopterSession(session)
              recharger('Double authentification activée. Les autres appareils ont été déconnectés.')
            }}
          />

          <button className="compte__annuler" type="button" onClick={() => recharger(null)}>
            Annuler
          </button>
        </>
      )}

      {chargement.etat === 'donnees' && mode === 'repos' && !chargement.valeur.actif && (
        <>
          <p className="compte__note">
            Facultative pour votre rôle. Une fois activée, la connexion demande en plus du mot de
            passe un code à six chiffres, produit par une application sur votre téléphone : un mot
            de passe volé ne suffit plus.
          </p>

          <p className="compte__consequence">
            L’activation déconnecte <strong>tous les autres appareils</strong>. Celui-ci reste
            connecté.
          </p>

          <button className="compte__valider" type="button" onClick={() => setMode('equipement')}>
            Activer
          </button>
        </>
      )}

      {chargement.etat === 'donnees' && mode === 'repos' && chargement.valeur.actif && (
        <form className="compte__formulaire" onSubmit={(evenement) => evenement.preventDefault()} noValidate>
          <dl className="compte__definitions">
            <div className="compte__ligne">
              <dt className="compte__cle">État</dt>
              <dd className="compte__valeur">
                Active{chargement.valeur.obligatoire ? ', imposée par votre rôle' : ''}
              </dd>
            </div>

            <div className="compte__ligne">
              <dt className="compte__cle">Codes de récupération</dt>
              <dd className="compte__valeur compte__valeur--chiffres">
                {chargement.valeur.codesRestants} sur 10
              </dd>
            </div>
          </dl>

          {chargement.valeur.codesRestants <= 2 && (
            <p className="compte__consequence">
              Il reste peu de codes de récupération : les régénérer avant d’en avoir besoin.
            </p>
          )}

          <div className="compte__champ">
            <label className="compte__etiquette" htmlFor="compte-code">
              {recuperation ? 'Code de récupération' : 'Code affiché par l’application'}
            </label>
            <input
              id="compte-code"
              className={`compte__saisie${recuperation ? '' : ' compte__saisie--code'}`}
              inputMode={recuperation ? 'text' : 'numeric'}
              autoComplete="one-time-code"
              autoCapitalize="characters"
              aria-describedby="compte-code-aide"
              value={code}
              onChange={(evenement) => setCode(evenement.target.value)}
            />
            <p className="compte__aide" id="compte-code-aide">
              {recuperation
                ? 'Le code est consommé : il ne servira plus.'
                : 'Demandé pour chacun des deux gestes ci-dessous : une session ouverte ne suffit pas.'}
            </p>
          </div>

          <button
            className="compte__lien"
            type="button"
            onClick={() => {
              setRecuperation(!recuperation)
              setCode('')
              setErreur(null)
            }}
          >
            {recuperation ? 'Utiliser le code de l’application' : 'Téléphone perdu ? Utiliser un code de récupération'}
          </button>

          <p className="compte__consequence">
            {chargement.valeur.obligatoire
              ? <>Changer de téléphone <strong>déconnecte tous les appareils, celui-ci compris</strong>. La connexion suivante demandera d’équiper le nouveau.</>
              : <>La désactivation déconnecte <strong>tous les autres appareils</strong>. Celui-ci reste connecté.</>}
          </p>

          {erreurAffichee}

          <div className="compte__actions">
            {/* Sans téléphone, des codes neufs ne servent à rien : c'est le téléphone qu'il
                faut remplacer, et l'équipement suivant en remettra dix. */}
            {!recuperation && (
              <button className="compte__valider" type="button" disabled={envoi} onClick={() => void regenerer()}>
                Régénérer les codes
              </button>
            )}

            <button
              className="compte__declencheur"
              type="button"
              disabled={envoi}
              onClick={() => void desactiver(chargement.valeur.obligatoire)}
            >
              {chargement.valeur.obligatoire ? 'Changer de téléphone' : 'Désactiver'}
            </button>
          </div>
        </form>
      )}
    </section>
  )
}
