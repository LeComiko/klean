import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Link } from 'react-router-dom'

import {
  creerCompte,
  fermerCompte,
  lireComptes,
  pseudonymiserCompte,
  reinitialiserSecondFacteur,
  rouvrirCompte,
} from '../api/comptes'
import { listerToutPourChoix } from '../api/ressources'
import type { Ressource } from '../api/ressources'
import { useAuth } from '../auth/contexte'
import {
  corpsDeCreation,
  etatDe,
  filtrerParRole,
  gestesDe,
  libelleRole,
  LibellesEtat,
  LibellesGeste,
  nomDe,
  proposeFicheAgent,
  questionDe,
  RolesProposes,
  SaisieVide,
} from '../comptes/comptes'
import type { Geste, SaisieCompte } from '../comptes/comptes'
import { EnTetePage } from '../composants/EnTetePage'
import { SousNavigation } from '../composants/SousNavigation'
import { ongletsDe } from '../layout/sections'
import type { Section } from '../layout/sections'
import { texteDe } from '../reference/valeurs'
import { Roles } from '../types/auth'
import type { Compte, CompteCree } from '../types/comptes'
import { formaterDateLongue } from '../utils/dates'
import '../composants/FormulaireRessource.css'
import '../composants/TableauRessource.css'
import './Reference.css'
import './Comptes.css'

type Chargement =
  | { etat: 'chargement' }
  | { etat: 'erreur'; message: string }
  | { etat: 'donnees'; comptes: Compte[] }

interface Option {
  valeur: string
  libelle: string
}

/** Les fiches agents et les clients : ce que nomment la colonne « Rattaché à » et la création. */
interface Rattachements {
  agents: Option[]
  clients: Option[]
}

const Gestes: Record<Geste, (id: string) => Promise<unknown>> = {
  Fermer: fermerCompte,
  Rouvrir: rouvrirCompte,
  Reinitialiser: reinitialiserSecondFacteur,
  Pseudonymiser: pseudonymiserCompte,
}

const Faits: Record<Geste, (nom: string) => string> = {
  Fermer: (nom) => `Le compte de ${nom} est fermé, et ses sessions sont tombées.`,
  Rouvrir: (nom) => `Le compte de ${nom} est rouvert : son mot de passe est celui d’avant.`,
  Reinitialiser: (nom) => `La double authentification de ${nom} est réinitialisée : sa prochaine connexion équipera un téléphone.`,
  Pseudonymiser: (nom) => `${nom} est pseudonymisé : son identité est effacée.`,
}

function messageDe(echec: unknown, repli: string): string {
  return echec instanceof Error ? echec.message : repli
}

/** Un horodatage du serveur est un **instant** : lu en heure locale, pas par sa sous-chaîne. */
function dateDe(instant: string): string {
  return formaterDateLongue(new Date(instant))
}

/**
 * Les comptes de l'entreprise (SPEC.md, étape 24), administrateur seul : qui a accès,
 * ouvrir un compte, fermer celui d'une personne partie.
 *
 * Pas un écran de référence : la création ne porte aucun mot de passe et rend celui
 * que le serveur a tiré, et les gestes de ligne sont tous hors CRUD.
 *
 * ⚠️ **Le mot de passe provisoire ne s'affiche qu'une fois**, juste après la création.
 * La base n'en garde que l'empreinte : perdu avant d'avoir été transmis, il ne se relit
 * pas, et le compte est à fermer puis à recréer. L'écran le dit au lieu de le laisser
 * découvrir.
 */
export function Comptes({ section }: { section: Section }) {
  const { profil } = useAuth()

  const [chargement, setChargement] = useState<Chargement>({ etat: 'chargement' })
  const [generation, setGeneration] = useState(0)
  const [rattachements, setRattachements] = useState<Rattachements | null>(null)
  const [filtre, setFiltre] = useState('')
  const [formulaire, setFormulaire] = useState(false)
  const [cree, setCree] = useState<CompteCree | null>(null)
  const [aConfirmer, setAConfirmer] = useState<{ id: string; geste: Geste } | null>(null)
  const [enCours, setEnCours] = useState<string | null>(null)
  const [annonce, setAnnonce] = useState<string | null>(null)
  const [erreur, setErreur] = useState<string | null>(null)

  useEffect(() => {
    const controleur = new AbortController()

    lireComptes(controleur.signal)
      .then((comptes) => setChargement({ etat: 'donnees', comptes }))
      .catch((echec: unknown) => {
        if (controleur.signal.aborted) return
        setChargement({ etat: 'erreur', message: messageDe(echec, 'Liste indisponible.') })
      })

    return () => {
      controleur.abort()
    }
  }, [generation])

  useEffect(() => {
    const controleur = new AbortController()

    Promise.all([
      listerToutPourChoix<Ressource>('/api/agents/choix', controleur.signal),
      listerToutPourChoix<Ressource>('/api/clients', controleur.signal),
    ])
      .then(([agents, clients]) => {
        setRattachements({
          agents: agents.elements.map((agent) => ({
            valeur: agent.id,
            libelle: `${texteDe(agent.prenom) ?? ''} ${texteDe(agent.nom) ?? ''}`.trim() || agent.id,
          })),
          clients: clients.elements.map((client) => ({ valeur: client.id, libelle: texteDe(client.nom) ?? client.id })),
        })
      })
      .catch(() => {
        // Silencieux : la colonne garde les identifiants, et la création dit pourquoi
        // elle ne propose rien.
      })

    return () => {
      controleur.abort()
    }
  }, [])

  function nommerAgent(id: string): string {
    return rattachements?.agents.find((option) => option.valeur === id)?.libelle ?? id
  }

  function nommerClient(id: string): string {
    return rattachements?.clients.find((option) => option.valeur === id)?.libelle ?? id
  }

  async function executer(compte: Compte, geste: Geste) {
    setAnnonce(null)
    setErreur(null)
    setEnCours(compte.id)

    try {
      await Gestes[geste](compte.id)
      setAnnonce(Faits[geste](nomDe(compte)))
      setAConfirmer(null)
      setGeneration((precedente) => precedente + 1)
    } catch (echec: unknown) {
      // Le refus du serveur nomme sa sortie — « rattaché à un agent », « le dernier
      // administrateur » : il s'affiche tel quel.
      setErreur(messageDe(echec, 'Geste impossible.'))
      setAConfirmer(null)
    } finally {
      setEnCours(null)
    }
  }

  function demander(compte: Compte, geste: Geste) {
    setAnnonce(null)
    setErreur(null)

    if (questionDe(geste, nomDe(compte)) === null) void executer(compte, geste)
    else setAConfirmer({ id: compte.id, geste })
  }

  return (
    <>
      <EnTetePage
        titre="Comptes"
        sousTitre="Qui a accès à KLEAN, avec quel rôle"
        actions={
          <button
            className="reference__nouveau"
            type="button"
            onClick={() => {
              setFormulaire(true)
              setCree(null)
              setAnnonce(null)
              setErreur(null)
            }}
          >
            Nouveau compte
          </button>
        }
      />

      <SousNavigation onglets={ongletsDe(section, profil?.roles ?? [])} />

      <div className="reference">
        <div className="reference__principal">
          {annonce !== null && <output className="reference__annonce">{annonce}</output>}

          {erreur !== null && (
            <p className="reference__erreur" role="alert">
              <strong className="reference__alerte-mot">Refusé —</strong> {erreur}
            </p>
          )}

          {cree !== null && (
            <MotDePasseProvisoire
              cree={cree}
              surFin={() => {
                setCree(null)
              }}
            />
          )}

          <label className="comptes__filtre">
            <span className="comptes__filtre-libelle">Rôle</span>
            <select
              className="comptes__filtre-liste"
              value={filtre}
              onChange={(evenement) => {
                setFiltre(evenement.target.value)
              }}
            >
              <option value="">Tous</option>
              {RolesProposes.map((role) => (
                <option key={role} value={role}>
                  {libelleRole(role)}
                </option>
              ))}
            </select>
          </label>

          {chargement.etat === 'chargement' && <output className="reference__attente">Chargement…</output>}

          {chargement.etat === 'erreur' && (
            <p className="reference__erreur" role="alert">
              <strong className="reference__alerte-mot">Erreur —</strong> {chargement.message}
            </p>
          )}

          {chargement.etat === 'donnees' && (
            <TableauComptes
              comptes={filtrerParRole(chargement.comptes, filtre)}
              appelantId={profil?.id ?? ''}
              rattachementDe={(compte) =>
                compte.agentId !== null
                  ? `Fiche agent : ${nommerAgent(compte.agentId)}`
                  : compte.clientId !== null
                    ? `Client : ${nommerClient(compte.clientId)}`
                    : '—'
              }
              aConfirmer={aConfirmer}
              enCours={enCours}
              surGeste={demander}
              surConfirmer={(compte, geste) => void executer(compte, geste)}
              surAnnuler={() => {
                setAConfirmer(null)
              }}
            />
          )}
        </div>

        {formulaire && (
          <FormulaireCompte
            rattachements={rattachements}
            surCree={(nouveau) => {
              setFormulaire(false)
              setCree(nouveau)
              setGeneration((precedente) => precedente + 1)
            }}
            surAnnuler={() => {
              setFormulaire(false)
            }}
          />
        )}
      </div>
    </>
  )
}

interface ProprietesTableau {
  comptes: readonly Compte[]
  appelantId: string
  rattachementDe: (compte: Compte) => string
  aConfirmer: { id: string; geste: Geste } | null
  enCours: string | null
  surGeste: (compte: Compte, geste: Geste) => void
  surConfirmer: (compte: Compte, geste: Geste) => void
  surAnnuler: () => void
}

function TableauComptes({
  comptes,
  appelantId,
  rattachementDe,
  aConfirmer,
  enCours,
  surGeste,
  surConfirmer,
  surAnnuler,
}: ProprietesTableau) {
  if (comptes.length === 0) return <p className="tableau-ressource__vide">Aucun compte pour ce rôle.</p>

  return (
    <div className="tableau-ressource">
      <table className="tableau-ressource__table">
        <thead>
          <tr>
            <th scope="col" className="tableau-ressource__entete">Nom</th>
            <th scope="col" className="tableau-ressource__entete">Adresse</th>
            <th scope="col" className="tableau-ressource__entete">Rôle</th>
            <th scope="col" className="tableau-ressource__entete">État</th>
            <th scope="col" className="tableau-ressource__entete">Double authentification</th>
            <th scope="col" className="tableau-ressource__entete">Rattaché à</th>
            <th scope="col" className="tableau-ressource__entete">
              <span className="pour-lecteur-d-ecran">Gestes</span>
            </th>
          </tr>
        </thead>

        <tbody>
          {comptes.map((compte) => {
            const etat = etatDe(compte)
            const nom = nomDe(compte)
            const confirmation = aConfirmer?.id === compte.id ? aConfirmer.geste : null
            const occupe = enCours === compte.id

            return (
              <tr key={compte.id} className="tableau-ressource__ligne">
                <th scope="row" className="tableau-ressource__cellule tableau-ressource__cellule--titre">
                  {nom}
                  {compte.id === appelantId && <span className="comptes__vous"> (vous)</span>}
                </th>
                <td className="tableau-ressource__cellule">{compte.email}</td>
                <td className="tableau-ressource__cellule">{libelleRole(compte.role)}</td>
                <td className="tableau-ressource__cellule">
                  {/* Le mot porte l'état ; la graisse distingue ce qui demande un geste. */}
                  <span className={`comptes__etat comptes__etat--${etat}`}>{LibellesEtat[etat]}</span>
                  {compte.dateAnonymisation !== null && (
                    <span className="comptes__date"> le {dateDe(compte.dateAnonymisation)}</span>
                  )}
                </td>
                <td className="tableau-ressource__cellule">{compte.secondFacteurActif ? 'Active' : '—'}</td>
                <td className="tableau-ressource__cellule">{rattachementDe(compte)}</td>
                <td className="tableau-ressource__cellule tableau-ressource__cellule--actions comptes__actions">
                  {confirmation !== null ? (
                    <>
                      <span className="tableau-ressource__question comptes__question">{questionDe(confirmation, nom)}</span>
                      <button
                        className="tableau-ressource__action tableau-ressource__action--danger"
                        type="button"
                        disabled={occupe}
                        onClick={() => {
                          surConfirmer(compte, confirmation)
                        }}
                      >
                        {occupe ? 'En cours…' : 'Confirmer'}
                      </button>
                      <button className="tableau-ressource__action" type="button" disabled={occupe} onClick={surAnnuler}>
                        Annuler
                      </button>
                    </>
                  ) : (
                    <>
                      {gestesDe(compte, appelantId).map((geste) => (
                        <button
                          key={geste}
                          className="tableau-ressource__action"
                          type="button"
                          disabled={occupe}
                          onClick={() => {
                            surGeste(compte, geste)
                          }}
                        >
                          {LibellesGeste[geste]}
                          <span className="pour-lecteur-d-ecran"> : {nom}</span>
                        </button>
                      ))}
                      {/* La sortie que nommerait le refus 409 du serveur, montrée avant :
                          seule la route de l'agent efface les trois endroits où la
                          personne est nommée. */}
                      {compte.agentId !== null && etat !== 'Pseudonymise' && compte.id !== appelantId && (
                        <Link className="tableau-ressource__action tableau-ressource__action--lien" to="/agents">
                          Pseudonymiser depuis sa fiche agent
                        </Link>
                      )}
                    </>
                  )}
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

/** Le mot de passe provisoire, une seule fois, avec ce qu'il faut en faire. */
function MotDePasseProvisoire({ cree, surFin }: { cree: CompteCree; surFin: () => void }) {
  const [copie, setCopie] = useState(false)

  return (
    <section className="comptes__provisoire" aria-labelledby="comptes-provisoire-titre">
      <h2 className="comptes__provisoire-titre" id="comptes-provisoire-titre">
        Compte créé pour {nomDe(cree.compte)}
      </h2>

      <p className="comptes__provisoire-texte">
        Son mot de passe provisoire, à lui transmettre — de vive voix ou par un autre canal que
        son adresse, qu’un tiers pourrait lire. Il ne sert qu’une fois : sa première connexion
        lui fera choisir le sien.
      </p>

      <p className="comptes__provisoire-valeur">{cree.motDePasseProvisoire}</p>

      <p className="comptes__provisoire-texte">
        <strong>Il ne s’affichera plus.</strong> Perdu avant d’avoir été transmis, il ne se relit
        pas : fermer le compte, et en créer un autre.
      </p>

      <div className="comptes__provisoire-actions">
        <button
          className="reference__action"
          type="button"
          onClick={() => {
            void navigator.clipboard
              .writeText(cree.motDePasseProvisoire)
              .then(() => {
                setCopie(true)
              })
              .catch(() => {
                // Sans presse-papiers, le mot de passe reste lisible à l'écran.
              })
          }}
        >
          {copie ? 'Copié' : 'Copier'}
        </button>
        <button className="reference__nouveau" type="button" onClick={surFin}>
          C’est transmis
        </button>
      </div>
    </section>
  )
}

interface ProprietesFormulaire {
  rattachements: Rattachements | null
  surCree: (cree: CompteCree) => void
  surAnnuler: () => void
}

function FormulaireCompte({ rattachements, surCree, surAnnuler }: ProprietesFormulaire) {
  const [saisie, setSaisie] = useState<SaisieCompte>(SaisieVide)
  const [envoi, setEnvoi] = useState(false)
  const [erreur, setErreur] = useState<string | null>(null)

  function changer(champ: keyof SaisieCompte, valeur: string) {
    setSaisie((precedente) => ({ ...precedente, [champ]: valeur }))
  }

  async function enregistrer(evenement: FormEvent<HTMLFormElement>) {
    evenement.preventDefault()
    setErreur(null)

    const resultat = corpsDeCreation(saisie)

    if ('refus' in resultat) {
      setErreur(resultat.refus)
      return
    }

    setEnvoi(true)

    try {
      surCree(await creerCompte(resultat.corps))
    } catch (echec: unknown) {
      setErreur(messageDe(echec, 'Création impossible.'))
    } finally {
      setEnvoi(false)
    }
  }

  const ficheAgent = proposeFicheAgent(saisie.role)

  return (
    <form className="formulaire" onSubmit={(evenement) => void enregistrer(evenement)} noValidate aria-label="Nouveau compte">
      <header className="formulaire__entete">
        <h2 className="formulaire__titre">Nouveau compte</h2>
        <button className="formulaire__fermer" type="button" onClick={surAnnuler} aria-label="Fermer le formulaire">
          <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true" focusable="false">
            <path d="M6 6l12 12M18 6L6 18" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
          </svg>
        </button>
      </header>

      {erreur !== null && (
        <p className="formulaire__erreur" role="alert">
          <strong className="formulaire__erreur-mot">Refusé —</strong> {erreur}
        </p>
      )}

      <div className="formulaire__champs">
        <Saisie libelle="Adresse électronique" nom="email" type="email" requis valeur={saisie.email} surChanger={changer} />
        <Saisie libelle="Nom" nom="nom" requis valeur={saisie.nom} surChanger={changer} />
        <Saisie libelle="Prénom" nom="prenom" valeur={saisie.prenom} surChanger={changer} />

        <div className="champ">
          <label className="champ__libelle" htmlFor="compte-role">
            Rôle<span className="champ__requis"> (obligatoire)</span>
          </label>
          <select
            id="compte-role"
            className="champ__liste"
            value={saisie.role}
            onChange={(evenement) => {
              changer('role', evenement.target.value)
            }}
          >
            <option value="">— choisir —</option>
            {RolesProposes.map((role) => (
              <option key={role} value={role}>
                {libelleRole(role)}
              </option>
            ))}
          </select>
          {(saisie.role === Roles.Administrateur || saisie.role === Roles.Responsable) && (
            <p className="champ__aide">Ce rôle impose la double authentification : la première connexion équipera un téléphone.</p>
          )}
        </div>

        {/* ⚠️ Un champ masqué n'est pas rendu, et `corpsDeCreation` l'envoie à null : les
            deux moitiés du masquage (CLAUDE.md §10). */}
        {ficheAgent && (
          <Liste
            libelle="Fiche agent"
            nom="agentId"
            requis={saisie.role === Roles.Agent}
            aide={saisie.role === Roles.Agent ? undefined : 'Facultative : un responsable qui fait aussi le ménage porte la sienne.'}
            options={rattachements?.agents ?? []}
            valeur={saisie.agentId}
            surChanger={changer}
          />
        )}

        {saisie.role === Roles.ClientSyndic && (
          <Liste
            libelle="Client"
            nom="clientId"
            requis
            options={rattachements?.clients ?? []}
            valeur={saisie.clientId}
            surChanger={changer}
          />
        )}

        {rattachements === null && (ficheAgent || saisie.role === Roles.ClientSyndic) && (
          <p className="champ__avertissement">Les fiches et les clients n’ont pas pu être chargés.</p>
        )}
      </div>

      <p className="champ__aide">
        Aucun mot de passe à choisir : KLEAN en tire un, que vous transmettrez, et que la personne
        remplacera à sa première connexion.
      </p>

      <div className="formulaire__actions">
        <button className="formulaire__valider" type="submit" disabled={envoi}>
          {envoi ? 'Création…' : 'Créer le compte'}
        </button>
        <button className="formulaire__annuler" type="button" onClick={surAnnuler} disabled={envoi}>
          Annuler
        </button>
      </div>
    </form>
  )
}

interface ProprietesSaisie {
  libelle: string
  nom: keyof SaisieCompte
  type?: string
  requis?: boolean
  valeur: string
  surChanger: (champ: keyof SaisieCompte, valeur: string) => void
}

/** Un champ de texte. `maxLength` sur la saisie, sinon le serveur refuserait en anglais (CLAUDE.md §10). */
function Saisie({ libelle, nom, type = 'text', requis = false, valeur, surChanger }: ProprietesSaisie) {
  const identifiant = `compte-${nom}`

  return (
    <div className="champ">
      <label className="champ__libelle" htmlFor={identifiant}>
        {libelle}
        {requis && <span className="champ__requis"> (obligatoire)</span>}
      </label>
      <input
        id={identifiant}
        className="champ__saisie"
        type={type}
        maxLength={nom === 'email' ? 256 : 100}
        autoComplete="off"
        value={valeur}
        onChange={(evenement) => {
          surChanger(nom, evenement.target.value)
        }}
      />
    </div>
  )
}

interface ProprietesListe {
  libelle: string
  nom: keyof SaisieCompte
  requis?: boolean
  aide?: string
  options: readonly Option[]
  valeur: string
  surChanger: (champ: keyof SaisieCompte, valeur: string) => void
}

function Liste({ libelle, nom, requis = false, aide, options, valeur, surChanger }: ProprietesListe) {
  const identifiant = `compte-${nom}`

  return (
    <div className="champ">
      <label className="champ__libelle" htmlFor={identifiant}>
        {libelle}
        {requis && <span className="champ__requis"> (obligatoire)</span>}
      </label>
      <select
        id={identifiant}
        className="champ__liste"
        value={valeur}
        onChange={(evenement) => {
          surChanger(nom, evenement.target.value)
        }}
      >
        <option value="">{requis ? '— choisir —' : '— aucune —'}</option>
        {options.map((option) => (
          <option key={option.valeur} value={option.valeur}>
            {option.libelle}
          </option>
        ))}
      </select>
      {aide !== undefined && <p className="champ__aide">{aide}</p>}
    </div>
  )
}
