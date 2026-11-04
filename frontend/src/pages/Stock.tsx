import { useEffect, useState } from 'react'

import { listerToutPourChoix } from '../api/ressources'
import type { Page, Ressource } from '../api/ressources'
import { enregistrerMouvement, listerMouvements, listerStocks } from '../api/stock'
import { useAuth } from '../auth/contexte'
import { EnTetePage } from '../composants/EnTetePage'
import { FormulaireMouvement } from '../composants/FormulaireMouvement'
import type { OptionLieu, OptionProduit } from '../composants/FormulaireMouvement'
import { Pagination } from '../composants/Pagination'
import { SousNavigation } from '../composants/SousNavigation'
import { ongletsDe } from '../layout/sections'
import type { Section } from '../layout/sections'
import { texteDe } from '../reference/valeurs'
import {
  corpsMouvement,
  LibellesNature,
  lieuDe,
  lireLieu,
  refusLocal,
  saisieVide,
  valeurLieu,
} from '../stock/mouvements'
import type { SaisieMouvement } from '../stock/mouvements'
import { formaterQuantite } from '../stock/prevision'
import { aUnRole, Roles } from '../types/auth'
import type { LigneStock, Mouvement } from '../types/stock'
import { formaterDateLongue } from '../utils/dates'
import '../composants/TableauRessource.css'
import './Reference.css'
import './Stock.css'

/** Reflet de `RolesKlean.Exploitation` sur `POST /api/mouvements-stock`. */
const RolesMouvement: readonly string[] = [Roles.Administrateur, Roles.Responsable]

const TailleStocks = 50
const TailleMouvements = 25

/** Les noms que le serveur ne donne pas : il ne rend que des identifiants. */
interface Noms {
  produits: readonly OptionProduit[]
  emplacements: readonly OptionLieu[]
  sites: readonly OptionLieu[]
}

interface Obtenue<T> {
  cle: string
  page: Page<T>
}

interface Echouee {
  cle: string
  message: string
}

function messageDe(echec: unknown, repli: string): string {
  return echec instanceof Error ? echec.message : repli
}

/**
 * Les quantités par lieu, et les mouvements qui les expliquent (SPEC.md, étape 22).
 *
 * ⚠️ Aucune quantité ne se modifie ici, et ce n'est pas un manque : une quantité résulte
 * de mouvements. Une erreur se corrige par un mouvement de plus — un inventaire compte ce
 * qui est là, et l'historique garde l'écart.
 */
export function Stock({ section }: { section: Section }) {
  const { profil } = useAuth()
  const ecriture = profil !== null && aUnRole(profil, RolesMouvement)

  const [noms, setNoms] = useState<Noms | null>(null)

  const [lieu, setLieu] = useState('')
  const [page, setPage] = useState(1)
  const [pageMouvements, setPageMouvements] = useState(1)
  const [rechargement, setRechargement] = useState(0)

  const [stocks, setStocks] = useState<Obtenue<LigneStock> | null>(null)
  const [mouvements, setMouvements] = useState<Obtenue<Mouvement> | null>(null)
  const [echouee, setEchouee] = useState<Echouee | null>(null)

  const [saisie, setSaisie] = useState<SaisieMouvement | null>(null)
  const [enCours, setEnCours] = useState(false)
  const [erreur, setErreur] = useState<string | null>(null)
  const [annonce, setAnnonce] = useState<string | null>(null)

  const cleStocks = `${lieu}#${String(page)}#${String(rechargement)}`
  const cleMouvements = `${String(pageMouvements)}#${String(rechargement)}`

  // ---- Les noms : produits, emplacements, sites ----
  useEffect(() => {
    const controleur = new AbortController()

    Promise.all([
      listerToutPourChoix<Ressource>('/api/produits', controleur.signal),
      listerToutPourChoix<Ressource>('/api/emplacements', controleur.signal),
      listerToutPourChoix<Ressource>('/api/sites', controleur.signal),
    ])
      .then(([produits, emplacements, sites]) => {
        setNoms({
          produits: produits.elements.map((produit) => ({
            valeur: produit.id,
            libelle: texteDe(produit.nom) ?? produit.id,
            unite: texteDe(produit.unite) ?? '',
            actif: produit.actif !== false,
          })),
          emplacements: emplacements.elements.map((emplacement) => ({
            valeur: valeurLieu('emplacement', emplacement.id),
            libelle: `${texteDe(emplacement.nom) ?? emplacement.id} (${
              emplacement.type === 'Vehicule' ? 'véhicule' : 'dépôt'
            }${emplacement.actif === false ? ', désactivé' : ''})`,
            recoit: emplacement.actif !== false,
          })),
          sites: sites.elements.map((site) => ({
            valeur: valeurLieu('site', site.id),
            libelle: `${texteDe(site.nom) ?? site.id} (site)`,
            recoit: true,
          })),
        })
      })
      .catch(() => {
        // Silencieux : les tableaux afficheront les identifiants, et la liste des
        // lieux restera vide — la panne se voit sans seconde bannière.
      })

    return () => {
      controleur.abort()
    }
  }, [])

  // ---- Les quantités ----
  useEffect(() => {
    const controleur = new AbortController()
    const filtre = lireLieu(lieu)

    listerStocks(
      page,
      TailleStocks,
      { emplacementId: filtre.emplacementId ?? undefined, siteId: filtre.siteId ?? undefined },
      controleur.signal,
    )
      .then((recue) => {
        setStocks({ cle: cleStocks, page: recue })
      })
      .catch((echec: unknown) => {
        if (controleur.signal.aborted) return
        setEchouee({ cle: cleStocks, message: messageDe(echec, 'Stock indisponible.') })
      })

    return () => {
      controleur.abort()
    }
  }, [cleStocks, lieu, page])

  // ---- L'historique ----
  useEffect(() => {
    const controleur = new AbortController()

    listerMouvements(pageMouvements, TailleMouvements, controleur.signal)
      .then((recue) => {
        setMouvements({ cle: cleMouvements, page: recue })
      })
      .catch((echec: unknown) => {
        if (controleur.signal.aborted) return
        setEchouee({ cle: cleMouvements, message: messageDe(echec, 'Mouvements indisponibles.') })
      })

    return () => {
      controleur.abort()
    }
  }, [cleMouvements, pageMouvements])

  const enErreur = echouee !== null && (echouee.cle === cleStocks || echouee.cle === cleMouvements)

  function nomProduit(id: string): string {
    return noms?.produits.find((produit) => produit.valeur === id)?.libelle ?? id
  }

  function uniteDe(id: string): string {
    return noms?.produits.find((produit) => produit.valeur === id)?.unite ?? ''
  }

  function nomLieu(valeur: string): string {
    if (valeur === '') return '—'

    return [...(noms?.emplacements ?? []), ...(noms?.sites ?? [])]
      .find((option) => option.valeur === valeur)?.libelle ?? valeur
  }

  async function enregistrer() {
    if (saisie === null) return

    const refus = refusLocal(saisie)

    if (refus !== null) {
      setErreur(refus)
      return
    }

    setEnCours(true)
    setErreur(null)
    setAnnonce(null)

    try {
      const enregistre = await enregistrerMouvement(corpsMouvement(saisie))
      setAnnonce(phraseEnregistrement(enregistre, nomProduit(enregistre.produitId), uniteDe(enregistre.produitId)))

      // Le formulaire reste ouvert, les lieux gardés : on enchaîne plusieurs
      // transferts du même dépôt vers le même véhicule. Le produit et la quantité
      // repartent à vide, pour ne pas enregistrer deux fois la même ligne.
      setSaisie({ ...saisie, produitId: '', quantite: '' })
      setPageMouvements(1)
      setRechargement((tour) => tour + 1)
    } catch (echec: unknown) {
      // Le message du serveur : il nomme la sortie — « Un inventaire corrige une
      // quantité fausse », « le réactiver ».
      setErreur(messageDe(echec, 'Mouvement refusé.'))
    } finally {
      setEnCours(false)
    }
  }

  const lignes = stocks === null
    ? []
    : [...stocks.page.elements].sort((a, b) =>
      nomLieu(lieuDe(a.emplacementId, a.siteId)).localeCompare(nomLieu(lieuDe(b.emplacementId, b.siteId)), 'fr')
      || nomProduit(a.produitId).localeCompare(nomProduit(b.produitId), 'fr'))

  return (
    <>
      <EnTetePage
        titre="Stock"
        sousTitre="Les quantités par lieu, et les mouvements qui les expliquent"
        actions={
          ecriture ? (
            <button
              className="reference__nouveau"
              type="button"
              onClick={() => {
                setSaisie(saisie ?? saisieVide())
                setErreur(null)
                setAnnonce(null)
              }}
            >
              Nouveau mouvement
            </button>
          ) : undefined
        }
      />

      <SousNavigation onglets={ongletsDe(section, profil?.roles ?? [])} />

      {annonce !== null && <output className="reference__annonce">{annonce}</output>}

      {enErreur && (
        <p className="reference__erreur" role="alert">
          <strong className="reference__alerte-mot">Erreur —</strong> {echouee.message}
        </p>
      )}

      <div className="reference">
        <div className="reference__principal">
          <div className="stock__filtre">
            <label className="stock__filtre-libelle" htmlFor="stock-lieu">
              Lieu
            </label>
            <select
              id="stock-lieu"
              className="stock__filtre-liste"
              value={lieu}
              onChange={(evenement) => {
                setLieu(evenement.target.value)
                setPage(1)
              }}
            >
              <option value="">Tous les lieux</option>
              <optgroup label="Dépôts et véhicules">
                {(noms?.emplacements ?? []).map((option) => (
                  <option key={option.valeur} value={option.valeur}>
                    {option.libelle}
                  </option>
                ))}
              </optgroup>
              <optgroup label="Sites">
                {(noms?.sites ?? []).map((option) => (
                  <option key={option.valeur} value={option.valeur}>
                    {option.libelle}
                  </option>
                ))}
              </optgroup>
            </select>
          </div>

          {stocks === null && !enErreur && <output className="reference__attente">Chargement…</output>}

          {stocks !== null && (
            <>
              <div className="tableau-ressource" aria-busy={stocks.cle !== cleStocks}>
                <table className="tableau-ressource__table" aria-label="Quantités en stock">
                  <thead>
                    <tr>
                      <th className="tableau-ressource__entete" scope="col">Lieu</th>
                      <th className="tableau-ressource__entete" scope="col">Produit</th>
                      <th className="tableau-ressource__entete tableau-ressource__entete--chiffres" scope="col">
                        Quantité
                      </th>
                      <th className="tableau-ressource__entete" scope="col">Unité</th>
                    </tr>
                  </thead>
                  <tbody>
                    {lignes.map((ligne) => (
                      <tr key={ligne.id} className="tableau-ressource__ligne">
                        <td className="tableau-ressource__cellule tableau-ressource__cellule--titre">
                          {nomLieu(lieuDe(ligne.emplacementId, ligne.siteId))}
                        </td>
                        <td className="tableau-ressource__cellule">{nomProduit(ligne.produitId)}</td>
                        <td className="tableau-ressource__cellule tableau-ressource__cellule--chiffres">
                          {formaterQuantite(ligne.quantite)}
                        </td>
                        <td className="tableau-ressource__cellule">{uniteDe(ligne.produitId)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>

                {lignes.length === 0 && (
                  <p className="tableau-ressource__vide">
                    {lieu === ''
                      ? 'Aucun stock pour l’instant. Une quantité naît d’un mouvement : une entrée, un transfert ou un inventaire.'
                      : 'Rien en ce lieu.'}
                    {ecriture && lieu === '' && ' Le bouton « Nouveau mouvement » ouvre la saisie.'}
                  </p>
                )}
              </div>

              <Pagination
                page={stocks.page.page}
                taille={stocks.page.taille}
                total={stocks.page.total}
                chargement={stocks.cle !== cleStocks}
                onChanger={setPage}
              />
            </>
          )}

          <h2 className="stock__titre" id="titre-mouvements">Derniers mouvements</h2>

          {mouvements !== null && (
            <>
              <div className="tableau-ressource" aria-busy={mouvements.cle !== cleMouvements}>
                <table className="tableau-ressource__table" aria-labelledby="titre-mouvements">
                  <thead>
                    <tr>
                      <th className="tableau-ressource__entete" scope="col">Date</th>
                      <th className="tableau-ressource__entete" scope="col">Nature</th>
                      <th className="tableau-ressource__entete" scope="col">Produit</th>
                      <th className="tableau-ressource__entete" scope="col">Depuis</th>
                      <th className="tableau-ressource__entete" scope="col">Vers</th>
                      <th className="tableau-ressource__entete tableau-ressource__entete--chiffres" scope="col">
                        Quantité
                      </th>
                      <th className="tableau-ressource__entete tableau-ressource__entete--chiffres" scope="col">
                        Écart
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {mouvements.page.elements.map((mouvement) => (
                      <tr key={mouvement.id} className="tableau-ressource__ligne">
                        <td className="tableau-ressource__cellule stock__date">{formaterInstant(mouvement.dateMouvement)}</td>
                        <td className="tableau-ressource__cellule stock__nature">
                          {LibellesNature[mouvement.nature]}
                        </td>
                        <td className="tableau-ressource__cellule">{nomProduit(mouvement.produitId)}</td>
                        <td className="tableau-ressource__cellule">
                          {nomLieu(lieuDe(mouvement.origineEmplacementId, mouvement.origineSiteId))}
                        </td>
                        <td className="tableau-ressource__cellule">
                          {nomLieu(lieuDe(mouvement.destinationEmplacementId, mouvement.destinationSiteId))}
                        </td>
                        <td className="tableau-ressource__cellule tableau-ressource__cellule--chiffres">
                          {formaterQuantite(mouvement.quantite)}
                        </td>
                        <td className="tableau-ressource__cellule tableau-ressource__cellule--chiffres">
                          {mouvement.ecart === null ? '' : signe(mouvement.ecart)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>

                {mouvements.page.elements.length === 0 && (
                  <p className="tableau-ressource__vide">Aucun mouvement pour l’instant.</p>
                )}
              </div>

              <Pagination
                page={mouvements.page.page}
                taille={mouvements.page.taille}
                total={mouvements.page.total}
                chargement={mouvements.cle !== cleMouvements}
                onChanger={setPageMouvements}
              />
            </>
          )}
        </div>

        {saisie !== null && noms !== null && (
          <FormulaireMouvement
            saisie={saisie}
            produits={noms.produits}
            emplacements={noms.emplacements}
            sites={noms.sites}
            enCours={enCours}
            erreur={erreur}
            onChanger={(suivante) => {
              setSaisie(suivante)
              setErreur(null)
            }}
            onEnregistrer={() => void enregistrer()}
            onAnnuler={() => {
              setSaisie(null)
              setErreur(null)
            }}
          />
        )}
      </div>
    </>
  )
}

/** `+1,5`, `−2` : le signe dit le sens de l'écart, un zéro n'en porte pas. */
function signe(ecart: number): string {
  if (ecart > 0) return `+${formaterQuantite(ecart)}`
  if (ecart < 0) return `−${formaterQuantite(-ecart)}`

  return '0'
}

/**
 * `24 mai 2027, 09:05`. Un vrai instant, pas un jour à minuit UTC : l'heure locale est
 * la bonne lecture, contrairement aux dates de vacation (CLAUDE.md §10).
 */
function formaterInstant(horodatage: string): string {
  const instant = new Date(horodatage)
  const heure = `${String(instant.getHours()).padStart(2, '0')}:${String(instant.getMinutes()).padStart(2, '0')}`

  return `${formaterDateLongue(instant)}, ${heure}`
}

/** Accordé à la nature : une entrée, une sortie, un transfert, un inventaire. */
const Enregistre: Readonly<Record<Mouvement['nature'], string>> = {
  Entree: 'Entrée enregistrée',
  Sortie: 'Sortie enregistrée',
  Transfert: 'Transfert enregistré',
  Inventaire: 'Inventaire enregistré',
}

/**
 * L'annonce d'un mouvement, avec ce qui a bougé. L'unité est entre parenthèses : elle
 * s'écrit au singulier et ne s'accorde pas (même règle que le refus du serveur).
 */
function phraseEnregistrement(mouvement: Mouvement, produit: string, unite: string): string {
  const quantite = `${formaterQuantite(mouvement.quantite)}${unite === '' ? '' : ` (${unite})`}`
  const debut = `${Enregistre[mouvement.nature]} pour « ${produit} » : ${quantite}`

  return mouvement.nature === 'Inventaire'
    ? `${debut} compté, écart ${signe(mouvement.ecart ?? 0)}.`
    : `${debut}.`
}
