import { Fragment, useState } from 'react'

import { etatsDe, formaterQuantite } from '../stock/prevision'
import type { PrevisionProduit } from '../types/stock'
import './TableauRessource.css'
import './TableauPrevision.css'

interface Proprietes {
  produits: readonly PrevisionProduit[]
  nomProduit: (id: string) => string
  uniteDe: (id: string) => string
  nomSite: (id: string) => string
}

/**
 * Une ligne par produit, ses états en mots, et sous chacune ce qu'il faut apporter à
 * chaque site : la proposition de réapprovisionnement de SPEC.md, en lecture.
 *
 * Composant de présentation : la page charge, il affiche (CLAUDE.md §11).
 */
export function TableauPrevision({ produits, nomProduit, uniteDe, nomSite }: Proprietes) {
  const [ouverts, setOuverts] = useState<ReadonlySet<string>>(new Set())

  function basculer(id: string) {
    setOuverts((precedents) => {
      const suivants = new Set(precedents)

      if (suivants.has(id)) suivants.delete(id)
      else suivants.add(id)

      return suivants
    })
  }

  return (
    <div className="tableau-ressource">
      <table className="tableau-ressource__table" aria-label="Besoin et couverture par produit">
        <thead>
          <tr>
            <th className="tableau-ressource__entete" scope="col">Produit</th>
            <th className="tableau-ressource__entete" scope="col">État</th>
            <th className="tableau-ressource__entete tableau-ressource__entete--chiffres" scope="col">Stock</th>
            <th className="tableau-ressource__entete tableau-ressource__entete--chiffres" scope="col">
              Dont réserve
            </th>
            <th className="tableau-ressource__entete tableau-ressource__entete--chiffres" scope="col">Besoin</th>
            <th className="tableau-ressource__entete tableau-ressource__entete--chiffres" scope="col">Manque</th>
            <th className="tableau-ressource__entete" scope="col">Unité</th>
            <th className="tableau-ressource__entete" scope="col">
              <span className="pour-lecteur-d-ecran">Détail</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {produits.map((ligne) => {
            const ouvert = ouverts.has(ligne.produitId)
            const detail = `prevision-detail-${ligne.produitId}`

            return (
              <Fragment key={ligne.produitId}>
                <tr className="tableau-ressource__ligne">
                  <td className="tableau-ressource__cellule tableau-ressource__cellule--titre">
                    {nomProduit(ligne.produitId)}
                  </td>
                  <td className="tableau-ressource__cellule">
                    <span className="prevision__etats">
                      {etatsDe(ligne).map((etat) => (
                        <span key={etat.mot} className={`etat-stock etat-stock--${etat.ton}`}>
                          <span className="etat-stock__pastille" aria-hidden="true" />
                          {etat.mot}
                        </span>
                      ))}
                    </span>
                  </td>
                  <td className="tableau-ressource__cellule tableau-ressource__cellule--chiffres">
                    {formaterQuantite(ligne.stockTotal)}
                  </td>
                  <td className="tableau-ressource__cellule tableau-ressource__cellule--chiffres">
                    {formaterQuantite(ligne.stockReserve)}
                  </td>
                  <td className="tableau-ressource__cellule tableau-ressource__cellule--chiffres">
                    {formaterQuantite(ligne.besoin)}
                  </td>
                  <td className="tableau-ressource__cellule tableau-ressource__cellule--chiffres">
                    {ligne.manque > 0 ? formaterQuantite(ligne.manque) : '—'}
                  </td>
                  <td className="tableau-ressource__cellule">{uniteDe(ligne.produitId)}</td>
                  <td className="tableau-ressource__cellule tableau-ressource__cellule--actions">
                    {ligne.sites.length > 0 && (
                      <button
                        className="tableau-ressource__action"
                        type="button"
                        aria-expanded={ouvert}
                        aria-controls={detail}
                        onClick={() => {
                          basculer(ligne.produitId)
                        }}
                      >
                        {ouvert ? 'Masquer les sites' : `Par site (${String(ligne.sites.length)})`}
                      </button>
                    )}
                  </td>
                </tr>

                {ouvert && (
                  <tr className="prevision__detail" id={detail}>
                    <td className="prevision__detail-cellule" colSpan={8}>
                      <table className="prevision__sites" aria-label={`Besoin par site — ${nomProduit(ligne.produitId)}`}>
                        <thead>
                          <tr>
                            <th className="prevision__sites-entete" scope="col">Site</th>
                            <th className="prevision__sites-entete prevision__sites-entete--chiffres" scope="col">
                              Besoin
                            </th>
                            <th className="prevision__sites-entete prevision__sites-entete--chiffres" scope="col">
                              Sur place
                            </th>
                            <th className="prevision__sites-entete prevision__sites-entete--chiffres" scope="col">
                              À apporter
                            </th>
                          </tr>
                        </thead>
                        <tbody>
                          {ligne.sites.map((site) => (
                            <tr key={site.siteId}>
                              <td className="prevision__sites-cellule">{nomSite(site.siteId)}</td>
                              <td className="prevision__sites-cellule prevision__sites-cellule--chiffres">
                                {formaterQuantite(site.besoin)}
                              </td>
                              <td className="prevision__sites-cellule prevision__sites-cellule--chiffres">
                                {formaterQuantite(site.stock)}
                              </td>
                              <td className="prevision__sites-cellule prevision__sites-cellule--chiffres prevision__sites-cellule--fort">
                                {site.aApporter > 0 ? formaterQuantite(site.aApporter) : '—'}
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </td>
                  </tr>
                )}
              </Fragment>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
