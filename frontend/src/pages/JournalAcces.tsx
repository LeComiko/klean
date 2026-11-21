import { useEffect, useState } from 'react'

import { lireComptes } from '../api/comptes'
import { lireJournal } from '../api/journal'
import { listerToutPourChoix } from '../api/ressources'
import type { Page, Ressource } from '../api/ressources'
import { useAuth } from '../auth/contexte'
import { nomDe } from '../comptes/comptes'
import { EnTetePage } from '../composants/EnTetePage'
import { Pagination } from '../composants/Pagination'
import { SousNavigation } from '../composants/SousNavigation'
import {
  AnnuaireVide,
  auteurDe,
  cibleDe,
  FiltresVides,
  formaterInstant,
  libelleNature,
  libelleRessource,
  LibellesNature,
  LibellesRessource,
  Natures,
  nomDeCompte,
  refusDePeriode,
  Ressources,
} from '../journal/journal'
import type { Annuaire, FiltresJournal } from '../journal/journal'
import { ongletsDe } from '../layout/sections'
import type { Section } from '../layout/sections'
import { texteDe } from '../reference/valeurs'
import type { Compte } from '../types/comptes'
import type { AccesJournalise } from '../types/journal'
import '../composants/TableauRessource.css'
import './Reference.css'
import './JournalAcces.css'

const Taille = 50

type Chargement =
  | { etat: 'chargement' }
  | { etat: 'erreur'; message: string }
  | { etat: 'donnees'; page: Page<AccesJournalise> }

function messageDe(echec: unknown, repli: string): string {
  return echec instanceof Error ? echec.message : repli
}

/**
 * Le journal d'accès aux données personnelles (SPEC.md, étape 27), administrateur seul :
 * qui a lu quoi, et quand.
 *
 * ⚠️ **Le journal ne nomme personne ; l'écran nomme, et seulement ce qu'il a le droit de
 * nommer.** Les auteurs et les comptes visés viennent de la liste des comptes, les fiches
 * agents de leurs noms pour un choix — deux lectures, journalisées elles aussi, à
 * l'ouverture. Une absence ou une vacation reste un identifiant (`journal.ts`).
 *
 * ⚠️ Chaque page lue laisse une ligne au journal, celle-ci comprise : filtrer sur une autre
 * ressource les écarte.
 */
export function JournalAcces({ section }: { section: Section }) {
  const { profil } = useAuth()

  const [filtres, setFiltres] = useState<FiltresJournal>(FiltresVides)
  const [page, setPage] = useState(1)
  const [chargement, setChargement] = useState<Chargement>({ etat: 'chargement' })
  const [comptes, setComptes] = useState<Compte[]>([])
  const [annuaire, setAnnuaire] = useState<Annuaire>(AnnuaireVide)

  const refus = refusDePeriode(filtres.du, filtres.au)

  useEffect(() => {
    const controleur = new AbortController()

    // ⚠️ Chacune des deux peut échouer seule : un nom manquant ne doit pas cacher le
    // journal, il le rend seulement moins lisible, et l'écran le dit.
    Promise.allSettled([
      lireComptes(controleur.signal),
      listerToutPourChoix<Ressource>('/api/agents/choix', controleur.signal),
    ]).then(([lusComptes, lusAgents]) => {
      if (controleur.signal.aborted) return

      const listeComptes = lusComptes.status === 'fulfilled' ? lusComptes.value : []
      const agents = lusAgents.status === 'fulfilled' ? lusAgents.value : null

      setComptes([...listeComptes].sort((a, b) => nomDe(a).localeCompare(nomDe(b), 'fr')))
      setAnnuaire({
        comptes: new Map(listeComptes.map((compte) => [compte.id, nomDe(compte)])),
        agents: new Map(
          (agents?.elements ?? []).map((agent) => [
            agent.id,
            `${texteDe(agent.prenom) ?? ''} ${texteDe(agent.nom) ?? ''}`.trim() || agent.id,
          ]),
        ),
        incomplet: lusComptes.status === 'rejected' || agents === null || agents.total > agents.elements.length,
      })
    }, () => undefined)

    return () => {
      controleur.abort()
    }
  }, [])

  useEffect(() => {
    if (refus !== null) return

    const controleur = new AbortController()

    lireJournal(filtres, page, Taille, controleur.signal)
      .then((lue) => setChargement({ etat: 'donnees', page: lue }))
      .catch((echec: unknown) => {
        if (controleur.signal.aborted) return
        setChargement({ etat: 'erreur', message: messageDe(echec, 'Journal indisponible.') })
      })

    return () => {
      controleur.abort()
    }
  }, [filtres, page, refus])

  /** Une autre page ou d'autres filtres : l'état passe en chargement au geste, pas dans l'effet. */
  function lire(suivante: number, changement?: Partial<FiltresJournal>) {
    if (changement !== undefined) setFiltres((precedents) => ({ ...precedents, ...changement }))
    setPage(suivante)
    setChargement({ etat: 'chargement' })
  }

  /** Tout changement de filtre ramène à la première page : la quinzième d'un autre filtre n'existe peut-être pas. */
  function filtrer(changement: Partial<FiltresJournal>) {
    lire(1, changement)
  }

  const filtre = Object.values(filtres).some((valeur) => valeur !== '')

  return (
    <>
      <EnTetePage titre="Journal d’accès" sousTitre="Qui a lu quelles données personnelles, et quand" />

      <SousNavigation onglets={ongletsDe(section, profil?.roles ?? [])} />

      <div className="reference__principal">
        <p className="journal-acces__notice">
          Le journal dit qui, quelles données, quand et combien de lignes, jamais ce qui a été
          lu. Il se conserve douze mois et se purge hors de l’application. Votre lecture de cet
          écran y est inscrite, comme toute autre.
        </p>

        <form
          className="journal-acces__filtres"
          aria-label="Filtres du journal"
          onSubmit={(evenement) => {
            evenement.preventDefault()
          }}
        >
          <label className="journal-acces__champ">
            <span className="journal-acces__libelle">Qui</span>
            <select
              className="journal-acces__saisie"
              value={filtres.utilisateurId}
              onChange={(evenement) => {
                filtrer({ utilisateurId: evenement.target.value })
              }}
            >
              <option value="">Tout le monde</option>
              {comptes.map((compte) => (
                <option key={compte.id} value={compte.id}>
                  {nomDe(compte)}
                </option>
              ))}
              {/* Un auteur choisi d'un clic, absent de la liste : il reste nommé. */}
              {filtres.utilisateurId !== '' && !comptes.some((compte) => compte.id === filtres.utilisateurId) && (
                <option value={filtres.utilisateurId}>
                  {nomDeCompte(filtres.utilisateurId, annuaire)}
                </option>
              )}
            </select>
          </label>

          <label className="journal-acces__champ">
            <span className="journal-acces__libelle">Données</span>
            <select
              className="journal-acces__saisie"
              value={filtres.ressource}
              onChange={(evenement) => {
                filtrer({ ressource: evenement.target.value })
              }}
            >
              <option value="">Toutes</option>
              {Ressources.map((ressource) => (
                <option key={ressource} value={ressource}>
                  {LibellesRessource[ressource]}
                </option>
              ))}
            </select>
          </label>

          <label className="journal-acces__champ">
            <span className="journal-acces__libelle">Geste</span>
            <select
              className="journal-acces__saisie"
              value={filtres.nature}
              onChange={(evenement) => {
                filtrer({ nature: evenement.target.value })
              }}
            >
              <option value="">Tous</option>
              {Natures.map((nature) => (
                <option key={nature} value={nature}>
                  {LibellesNature[nature]}
                </option>
              ))}
            </select>
          </label>

          <label className="journal-acces__champ">
            <span className="journal-acces__libelle">Du</span>
            <input
              className="journal-acces__saisie journal-acces__saisie--date"
              type="date"
              value={filtres.du}
              onChange={(evenement) => {
                filtrer({ du: evenement.target.value })
              }}
            />
          </label>

          <label className="journal-acces__champ">
            <span className="journal-acces__libelle">Au</span>
            <input
              className="journal-acces__saisie journal-acces__saisie--date"
              type="date"
              value={filtres.au}
              min={filtres.du === '' ? undefined : filtres.du}
              onChange={(evenement) => {
                filtrer({ au: evenement.target.value })
              }}
            />
          </label>

          {filtre && (
            <button
              className="tableau-ressource__action journal-acces__effacer"
              type="button"
              onClick={() => {
                filtrer(FiltresVides)
              }}
            >
              Effacer les filtres
            </button>
          )}
        </form>

        {annuaire.incomplet && chargement.etat === 'donnees' && (
          <output className="journal-acces__incomplet">
            Les noms n’ont pas tous pu être retrouvés : une personne non nommée apparaît par le
            début de son identifiant, ce qui ne veut pas dire qu’elle n’existe pas.
          </output>
        )}

        {refus !== null && (
          <p className="reference__erreur" role="alert">
            <strong className="reference__alerte-mot">Refusé —</strong> {refus}
          </p>
        )}

        {refus === null && chargement.etat === 'chargement' && (
          <output className="reference__attente">Chargement…</output>
        )}

        {refus === null && chargement.etat === 'erreur' && (
          <p className="reference__erreur" role="alert">
            <strong className="reference__alerte-mot">Erreur —</strong> {chargement.message}
          </p>
        )}

        {refus === null && chargement.etat === 'donnees' && (
          <>
            <TableauJournal
              acces={chargement.page.elements}
              annuaire={annuaire}
              videMessage={filtre ? 'Aucun accès pour ces filtres.' : 'Aucun accès consigné.'}
              surAuteur={(utilisateurId) => {
                filtrer({ utilisateurId })
              }}
            />

            <Pagination
              page={chargement.page.page}
              taille={chargement.page.taille}
              total={chargement.page.total}
              chargement={false}
              onChanger={(suivante) => {
                lire(suivante)
              }}
            />
          </>
        )}
      </div>
    </>
  )
}

interface ProprietesTableau {
  acces: readonly AccesJournalise[]
  annuaire: Annuaire
  videMessage: string
  surAuteur: (utilisateurId: string) => void
}

function TableauJournal({ acces, annuaire, videMessage, surAuteur }: ProprietesTableau) {
  if (acces.length === 0) return <p className="tableau-ressource__vide">{videMessage}</p>

  return (
    <div className="tableau-ressource">
      <table className="tableau-ressource__table">
        <thead>
          <tr>
            <th scope="col" className="tableau-ressource__entete">Quand</th>
            <th scope="col" className="tableau-ressource__entete">Qui</th>
            <th scope="col" className="tableau-ressource__entete">Données</th>
            <th scope="col" className="tableau-ressource__entete">Geste</th>
            <th scope="col" className="tableau-ressource__entete">Visé</th>
          </tr>
        </thead>

        <tbody>
          {acces.map((ligne) => {
            const auteur = auteurDe(ligne, annuaire)

            return (
              <tr key={ligne.id} className="tableau-ressource__ligne">
                <td className="tableau-ressource__cellule journal-acces__quand">{formaterInstant(ligne.horodatage)}</td>
                <th scope="row" className="tableau-ressource__cellule tableau-ressource__cellule--titre">
                  {/* « Qu'a lu cette personne » : la question d'un contrôle, en un clic. */}
                  <button
                    className="journal-acces__auteur"
                    type="button"
                    onClick={() => {
                      surAuteur(ligne.utilisateurId)
                    }}
                  >
                    {auteur}
                    <span className="pour-lecteur-d-ecran"> : voir ses seuls accès</span>
                  </button>
                </th>
                <td className="tableau-ressource__cellule">{libelleRessource(ligne.ressource)}</td>
                <td className={`tableau-ressource__cellule journal-acces__geste${ligne.nature === 'Liste' || ligne.nature === 'Lecture' ? '' : ' journal-acces__geste--marque'}`}>
                  {libelleNature(ligne.nature)}
                </td>
                <td className="tableau-ressource__cellule">{cibleDe(ligne, annuaire)}</td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
