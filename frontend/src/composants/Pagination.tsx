import './Pagination.css'

interface Proprietes {
  page: number
  taille: number
  total: number
  /** Pendant un chargement : les boutons se désactivent, le texte ne bouge pas. */
  chargement: boolean
  onChanger: (page: number) => void
}

/**
 * Pagination des onze listes de l'étape 8. Composant de présentation pur.
 *
 * ⚠️ Le total est affiché, et ce n'est pas décoratif : sans lui, une liste
 * plafonnée à vingt-cinq lignes est indistinguable d'une entreprise qui n'a que
 * vingt-cinq clients. C'est exactement le genre d'écran qui fait conclure « mes
 * données ont disparu ».
 */
export function Pagination({ page, taille, total, chargement, onChanger }: Proprietes) {
  // Une liste vide a déjà son propre message, sur chaque écran qui pagine : « Aucune
  // ligne » y ferait un second état vide sous le premier. Ne rien rendre non plus
  // avant le premier chargement.
  if (total === 0) return null

  const pages = Math.max(1, Math.ceil(total / taille))
  const premier = (page - 1) * taille + 1
  const dernier = Math.min(page * taille, total)

  return (
    <div className="pagination">
      <p className="pagination__compte">
        <span className="pagination__chiffres">
          {premier}–{dernier}
        </span>{' '}
        sur <span className="pagination__chiffres">{total}</span>
      </p>

      {/* Masqués plutôt que désactivés quand il n'y a qu'une page : deux boutons
          éteints en permanence apprennent à ne plus les regarder. */}
      {pages > 1 && (
        <div className="pagination__boutons">
          <button
            className="pagination__bouton"
            type="button"
            onClick={() => {
              onChanger(page - 1)
            }}
            disabled={chargement || page <= 1}
          >
            Précédent
          </button>

          <p className="pagination__position">
            Page <span className="pagination__chiffres">{page}</span> sur{' '}
            <span className="pagination__chiffres">{pages}</span>
          </p>

          <button
            className="pagination__bouton"
            type="button"
            onClick={() => {
              onChanger(page + 1)
            }}
            disabled={chargement || page >= pages}
          >
            Suivant
          </button>
        </div>
      )}
    </div>
  )
}
