import type { ColonneRessource } from '../types/reference'
import type { Ressource } from '../api/ressources'
import './TableauRessource.css'

interface Proprietes {
  colonnes: readonly ColonneRessource[]
  lignes: readonly Ressource[]
  /** Identifiant de la ligne en cours de modification, mise en valeur. */
  choisie: string | null

  /**
   * Rendu d'une cellule, fourni par la page : c'est elle qui résout un
   * identifiant de client en nom et qui met une date en forme. Le tableau ne sait
   * rien du domaine.
   */
  rendu: (ligne: Ressource, colonne: ColonneRessource) => string

  /** Libellé d'une ligne, pour les intitulés de boutons et la confirmation. */
  etiquette: (ligne: Ressource) => string

  /**
   * Ce que la suppression emporte en plus de la ligne, en toutes lettres.
   * Absent : elle n'emporte rien.
   *
   * ⚠️ Toute la chaîne du modèle est en `DeleteBehavior.Cascade` : supprimer un
   * client rend 204 et efface au passage ses sites, leurs contrats, leurs
   * prestations et leurs interventions — mesuré contre le serveur. Une confirmation
   * qui ne dirait que le nom de la ligne laisserait croire qu'une seule ligne part.
   */
  cascade?: string

  /**
   * Libellé de l'action d'ouverture. « Voir » en lecture seule, « Modifier »
   * sinon : promettre une modification à qui ne peut pas modifier est le même
   * mensonge qu'un bouton d'enregistrement qui répond 403.
   */
  libelleModifier: string

  /** Absente : la colonne n'offre pas d'ouverture. */
  onModifier?: (ligne: Ressource) => void
  onSupprimer?: (ligne: Ressource) => void

  /** Identifiant dont la suppression attend confirmation. */
  aConfirmer: string | null
  onDemanderSuppression?: (id: string | null) => void

  /** Identifiant de la ligne dont une action est en cours. */
  enCours: string | null

  videMessage: string
}

/**
 * Liste d'une ressource de référence.
 *
 * Un vrai `<table>` avec `<th scope="col">`, comme `GrillePlanning` : ce sont des
 * données tabulaires, un lecteur d'écran doit pouvoir annoncer « colonne Ville »
 * en parcourant une ligne. Aucune troncature par ellipse — deux résidences d'un
 * même syndic se distinguent souvent par la fin de leur nom (SPEC.md étape 14).
 *
 * La suppression se confirme **dans la ligne**, en deux temps. Pas de
 * `window.confirm` : une boîte de dialogue du navigateur ne se met pas en forme,
 * ne dit pas ce qu'elle va supprimer, et bloque tout le reste de la page.
 */
export function TableauRessource({
  colonnes,
  lignes,
  choisie,
  rendu,
  etiquette,
  cascade,
  libelleModifier,
  onModifier,
  onSupprimer,
  aConfirmer,
  onDemanderSuppression,
  enCours,
  videMessage,
}: Proprietes) {
  const actions = onModifier !== undefined || onSupprimer !== undefined

  if (lignes.length === 0) {
    return <p className="tableau-ressource__vide">{videMessage}</p>
  }

  return (
    <div className="tableau-ressource">
      <table className="tableau-ressource__table">
        <thead>
          <tr>
            {colonnes.map((colonne) => (
              <th
                key={colonne.nom}
                scope="col"
                className={
                  colonne.chiffres === true
                    ? 'tableau-ressource__entete tableau-ressource__entete--chiffres'
                    : 'tableau-ressource__entete'
                }
              >
                {colonne.libelle}
              </th>
            ))}
            {actions && (
              <th scope="col" className="tableau-ressource__entete">
                <span className="pour-lecteur-d-ecran">Actions</span>
              </th>
            )}
          </tr>
        </thead>

        <tbody>
          {lignes.map((ligne) => {
            const confirme = aConfirmer === ligne.id
            const occupee = enCours === ligne.id

            return (
              <tr
                key={ligne.id}
                className={
                  choisie === ligne.id
                    ? 'tableau-ressource__ligne tableau-ressource__ligne--choisie'
                    : 'tableau-ressource__ligne'
                }
              >
                {colonnes.map((colonne, index) => {
                  const contenu = rendu(ligne, colonne)

                  // La première colonne est l'en-tête de sa ligne : elle porte
                  // scope="row", ce qui permet à un lecteur d'écran d'annoncer
                  // « Résidence Bellevue, colonne Ville, Paris ».
                  const classes = [
                    'tableau-ressource__cellule',
                    colonne.chiffres === true ? 'tableau-ressource__cellule--chiffres' : '',
                    index === 0 ? 'tableau-ressource__cellule--titre' : '',
                  ]
                    .filter((classe) => classe !== '')
                    .join(' ')

                  return index === 0 ? (
                    <th key={colonne.nom} scope="row" className={classes}>
                      {contenu}
                    </th>
                  ) : (
                    <td key={colonne.nom} className={classes}>
                      {contenu}
                    </td>
                  )
                })}

                {actions && (
                  <td className="tableau-ressource__cellule tableau-ressource__cellule--actions">
                    {confirme ? (
                      <>
                        {/* Le nom est répété, et ce qui part avec lui est écrit :
                            une confirmation qui ne dit pas quoi elle supprime ne
                            confirme rien. */}
                        <span className="tableau-ressource__question">
                          Supprimer « {etiquette(ligne)} »
                          {cascade === undefined ? ' ?' : <> et {cascade} ?</>}
                        </span>
                        <button
                          className="tableau-ressource__action tableau-ressource__action--danger"
                          type="button"
                          onClick={() => {
                            onSupprimer?.(ligne)
                          }}
                          disabled={occupee}
                        >
                          {occupee ? 'Suppression…' : 'Confirmer'}
                        </button>
                        <button
                          className="tableau-ressource__action"
                          type="button"
                          onClick={() => {
                            onDemanderSuppression?.(null)
                          }}
                          disabled={occupee}
                        >
                          Annuler
                        </button>
                      </>
                    ) : (
                      <>
                        {onModifier !== undefined && (
                          <button
                            className="tableau-ressource__action"
                            type="button"
                            onClick={() => {
                              onModifier(ligne)
                            }}
                          >
                            {libelleModifier}
                            <span className="pour-lecteur-d-ecran"> {etiquette(ligne)}</span>
                          </button>
                        )}
                        {onSupprimer !== undefined && (
                          <button
                            className="tableau-ressource__action"
                            type="button"
                            onClick={() => {
                              onDemanderSuppression?.(ligne.id)
                            }}
                          >
                            Supprimer
                            <span className="pour-lecteur-d-ecran"> {etiquette(ligne)}</span>
                          </button>
                        )}
                      </>
                    )}
                  </td>
                )}
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
