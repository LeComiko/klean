/**
 * Contrats du stock, miroirs de `backend/DTOs/StockDto.cs`, `MouvementStockDto.cs` et
 * `PrevisionStockDto.cs` (SPEC.md, étape 22).
 *
 * ⚠️ Des identifiants, jamais des noms : le serveur ne nomme ni produit, ni lieu, ni
 * auteur. L'écran les résout avec les listes qu'il a déjà.
 */

export interface LigneStock {
  id: string
  produitId: string
  /** L'un des deux exactement. */
  emplacementId: string | null
  siteId: string | null
  quantite: number
}

/** Les noms exacts de `NatureMouvement` : le serveur refuse tout autre texte. */
export type NatureMouvement = 'Entree' | 'Sortie' | 'Transfert' | 'Inventaire'

export interface Mouvement {
  id: string
  produitId: string
  nature: NatureMouvement
  origineEmplacementId: string | null
  origineSiteId: string | null
  destinationEmplacementId: string | null
  destinationSiteId: string | null
  /** Déplacée ; pour un inventaire, comptée. */
  quantite: number
  /** Inventaire seulement : comptée moins attendue. */
  ecart: number | null
  dateMouvement: string
  auteurId: string
}

/** Corps de `POST /api/mouvements-stock`. Ni date ni auteur : le serveur les pose. */
export interface MouvementACreer {
  produitId: string
  nature: NatureMouvement
  origineEmplacementId: string | null
  origineSiteId: string | null
  destinationEmplacementId: string | null
  destinationSiteId: string | null
  quantite: number
}

export interface PrevisionSite {
  siteId: string
  besoin: number
  stock: number
  aApporter: number
}

export interface PrevisionProduit {
  produitId: string
  seuilAlerte: number
  stockTotal: number
  /** Dépôts et véhicules. */
  stockReserve: number
  besoin: number
  manque: number
  sousLeSeuil: boolean
  couvertureCourte: boolean
  sites: PrevisionSite[]
}

export interface Prevision {
  du: string
  au: string
  semaines: number
  vacationsSansType: number
  produits: PrevisionProduit[]
}
