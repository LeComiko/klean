import { useEffect, useState } from 'react'

import { listerToutPourChoix } from '../api/ressources'
import type { Ressource } from '../api/ressources'
import { lirePrevision } from '../api/stock'
import { useAuth } from '../auth/contexte'
import { EnTetePage } from '../composants/EnTetePage'
import { SousNavigation } from '../composants/SousNavigation'
import { TableauPrevision } from '../composants/TableauPrevision'
import { ongletsDe } from '../layout/sections'
import type { Section } from '../layout/sections'
import { texteDe } from '../reference/valeurs'
import { HorizonParDefaut, Horizons, libelleHorizon, phraseSansType, resumeAlertes } from '../stock/prevision'
import type { Prevision as DonneesPrevision } from '../types/stock'
import { depuisChaineIso, formaterDateLongue, jourIso } from '../utils/dates'
import '../composants/TableauRessource.css'
import './Reference.css'
import './Prevision.css'

interface Obtenue {
  cle: string
  prevision: DonneesPrevision
}

interface Echouee {
  cle: string
  message: string
}

/** Ce que la prévision nomme : elle ne rend que des identifiants. */
interface Noms {
  produits: ReadonlyMap<string, { nom: string; unite: string }>
  sites: ReadonlyMap<string, string>
}

function messageDe(echec: unknown, repli: string): string {
  return echec instanceof Error ? echec.message : repli
}

/** `2027-05-24T00:00:00Z` → `24 mai 2027`, par la sous-chaîne : minuit UTC (CLAUDE.md §10). */
function jourDe(horodatage: string): string {
  return formaterDateLongue(depuisChaineIso(jourIso(horodatage)))
}

/**
 * Le besoin des semaines à venir, et ce que le stock en couvre (SPEC.md, étape 22).
 *
 * Une lecture : rien ne se déduit d'aucun stock. Le réel s'établit par l'inventaire,
 * sur l'écran du stock.
 */
export function Prevision({ section }: { section: Section }) {
  const { profil } = useAuth()

  const [semaines, setSemaines] = useState(HorizonParDefaut)
  const [noms, setNoms] = useState<Noms | null>(null)
  const [obtenue, setObtenue] = useState<Obtenue | null>(null)
  const [echouee, setEchouee] = useState<Echouee | null>(null)

  const cle = String(semaines)
  const enErreur = echouee?.cle === cle
  const chargement = obtenue?.cle !== cle && !enErreur

  useEffect(() => {
    const controleur = new AbortController()

    Promise.all([
      listerToutPourChoix<Ressource>('/api/produits', controleur.signal),
      listerToutPourChoix<Ressource>('/api/sites', controleur.signal),
    ])
      .then(([produits, sites]) => {
        setNoms({
          produits: new Map(produits.elements.map((produit) => [
            produit.id,
            { nom: texteDe(produit.nom) ?? produit.id, unite: texteDe(produit.unite) ?? '' },
          ])),
          sites: new Map(sites.elements.map((site) => [site.id, texteDe(site.nom) ?? site.id])),
        })
      })
      .catch(() => {
        // Silencieux : le tableau garde les identifiants, la panne se voit.
      })

    return () => {
      controleur.abort()
    }
  }, [])

  useEffect(() => {
    const controleur = new AbortController()

    lirePrevision(semaines, controleur.signal)
      .then((recue) => {
        setObtenue({ cle, prevision: recue })
      })
      .catch((echec: unknown) => {
        if (controleur.signal.aborted) return
        setEchouee({ cle, message: messageDe(echec, 'Prévision indisponible.') })
      })

    return () => {
      controleur.abort()
    }
  }, [cle, semaines])

  const sansType = obtenue === null ? null : phraseSansType(obtenue.prevision.vacationsSansType)

  return (
    <>
      <EnTetePage
        titre="Prévision"
        sousTitre="Le besoin des semaines à venir, d’après le planning, et ce que le stock en couvre"
        actions={
          <label className="prevision__horizon">
            <span className="prevision__horizon-libelle">Horizon</span>
            <select
              className="prevision__horizon-liste"
              value={semaines}
              onChange={(evenement) => {
                setSemaines(Number(evenement.target.value))
              }}
            >
              {Horizons.map((nombre) => (
                <option key={nombre} value={nombre}>
                  {libelleHorizon(nombre)}
                </option>
              ))}
            </select>
          </label>
        }
      />

      <SousNavigation onglets={ongletsDe(section, profil?.roles ?? [])} />

      {enErreur && (
        <p className="reference__erreur" role="alert">
          <strong className="reference__alerte-mot">Erreur —</strong> {echouee.message}
        </p>
      )}

      {!enErreur && obtenue === null && <output className="reference__attente">Chargement…</output>}

      {!enErreur && obtenue !== null && (
        <div aria-busy={chargement}>
          <p className="prevision__periode">
            Du {jourDe(obtenue.prevision.du)} au {jourDe(obtenue.prevision.au)} — vacations planifiées ou en
            cours, multipliées par la consommation de leur type. {resumeAlertes(obtenue.prevision.produits)}
          </p>

          {sansType !== null && <p className="prevision__sans-type">{sansType}</p>}

          {obtenue.prevision.produits.length === 0 ? (
            <p className="tableau-ressource__vide">
              Aucun produit actif. Les produits se créent dans l’onglet Produits, et ce que chaque passage
              en consomme dans l’onglet Consommations.
            </p>
          ) : (
            <TableauPrevision
              produits={obtenue.prevision.produits}
              nomProduit={(id) => noms?.produits.get(id)?.nom ?? id}
              uniteDe={(id) => noms?.produits.get(id)?.unite ?? ''}
              nomSite={(id) => noms?.sites.get(id) ?? id}
            />
          )}

          <p className="prevision__note">
            « Dont réserve » : dépôts et véhicules. Le stock d’un site ne couvre que ce site ; le manque
            est ce que la réserve ne couvre pas. Un produit désactivé n’apparaît pas.
          </p>
        </div>
      )}
    </>
  )
}
