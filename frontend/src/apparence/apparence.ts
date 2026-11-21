/**
 * Le choix d'apparence de *Mon compte* : automatique, clair ou sombre (SPEC.md,
 * étape 32).
 *
 * Il ne quitte pas le navigateur. Rangé dans le stockage local sous une seule clé, il
 * n'est ni envoyé au serveur ni rattaché au compte : un autre appareil garde son
 * propre choix. C'est une préférence d'affichage, pas une donnée personnelle — voir
 * RGPD.md §3.
 *
 * Le thème lui-même est dans `styles/tokens.css` : `color-scheme` y décide entre les
 * deux valeurs de chaque `light-dark()`, et `data-theme` sur la racine le force. Ce
 * module ne pose que l'attribut.
 *
 * Extrait de l'écran pour être éprouvé : une valeur illisible ou un stockage qui lève
 * — navigation privée, stockage désactivé — doivent rendre « automatique », jamais
 * une page blanche (CLAUDE.md §11).
 */

export const Apparences = ['automatique', 'clair', 'sombre'] as const

export type Apparence = (typeof Apparences)[number]

export const LibellesApparence: Record<Apparence, string> = {
  automatique: 'Automatique',
  clair: 'Clair',
  sombre: 'Sombre',
}

/** La clé du stockage local. Préfixée : l'origine peut servir d'autres choses. */
export const CleApparence = 'klean.apparence'

/** Le strict nécessaire du stockage, pour qu'une série le remplace. */
export interface StockageApparence {
  getItem(cle: string): string | null
  setItem(cle: string, valeur: string): void
}

/** Le strict nécessaire de la racine du document. */
export interface RacineApparence {
  dataset: Record<string, string | undefined>
}

/** Une valeur stockée, relue. Tout ce qui n'est pas un des trois noms est « automatique ». */
export function lireApparence(brut: string | null): Apparence {
  return (Apparences as readonly string[]).includes(brut ?? '') ? (brut as Apparence) : 'automatique'
}

/**
 * La valeur de `data-theme` pour un choix, ou `null` pour le laisser au système.
 * ⚠️ Ces deux valeurs doivent exister dans `tokens.css` : la série le vérifie.
 */
export function attributTheme(apparence: Apparence): 'clair' | 'sombre' | null {
  return apparence === 'automatique' ? null : apparence
}

/** Le choix enregistré. Un stockage absent ou qui lève rend « automatique ». */
export function apparenceEnregistree(stockage: StockageApparence | null): Apparence {
  if (stockage === null) return 'automatique'

  try {
    return lireApparence(stockage.getItem(CleApparence))
  } catch {
    return 'automatique'
  }
}

/**
 * Enregistre le choix. Un stockage indisponible ne fait pas échouer le geste : le
 * thème change quand même, il ne sera simplement pas retenu au prochain chargement.
 * Rend `false` dans ce cas, pour que l'écran puisse le dire.
 */
export function enregistrerApparence(stockage: StockageApparence | null, apparence: Apparence): boolean {
  if (stockage === null) return false

  try {
    stockage.setItem(CleApparence, apparence)
    return true
  } catch {
    return false
  }
}

/** Pose ou retire `data-theme` sur la racine. */
export function appliquerApparence(racine: RacineApparence, apparence: Apparence): void {
  const attribut = attributTheme(apparence)

  if (attribut === null) delete racine.dataset.theme
  else racine.dataset.theme = attribut
}

/**
 * Le stockage local du navigateur, ou `null`. Le simple accès à `localStorage` lève
 * quand le stockage est bloqué : il faut l'entourer, pas seulement ses appels.
 */
export function stockageLocal(): StockageApparence | null {
  try {
    return typeof window === 'undefined' ? null : window.localStorage
  } catch {
    return null
  }
}
