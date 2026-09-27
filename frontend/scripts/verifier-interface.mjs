#!/usr/bin/env node
/**
 * Vérificateur mécanique du système d'interface de `CLAUDE.md` §12.
 *
 * Il ne remplace pas un œil : il vérifie ce qu'un œil vérifie mal. Un ratio de
 * contraste de 4,44:1 se distingue de 4,52:1 par le calcul, jamais au regard, et
 * un nom de jeton mal orthographié ne produit aucune erreur — la propriété CSS
 * est simplement ignorée, sans un mot.
 *
 * Node nu, aucune dépendance : le projet n'autorise que `react-router-dom`
 * (CLAUDE.md §3), et un vérificateur qui coûte une chaîne d'outils ne serait pas
 * lancé.
 *
 *     npm run verifier
 */

import { readFileSync, readdirSync, statSync } from 'node:fs'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const racine = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const dossierSource = join(racine, 'src')
const fichierJetons = join(dossierSource, 'styles', 'tokens.css')

const SeuilTexte = 4.5 // WCAG AA, texte normal
const SeuilNonTextuel = 3.0 // WCAG AA, composants d'interface et contours

let echecs = 0

function titre(texte) {
  console.log(`\n\u001b[1m${texte}\u001b[0m`)
}

function verdict(ok, libelle, detail = '') {
  if (!ok) echecs++
  console.log(`  ${ok ? 'OK   ' : 'ÉCHEC'} ${libelle}${detail === '' ? '' : `  ${detail}`}`)
}

// ---------------------------------------------------------------------------
// Lecture des fichiers
// ---------------------------------------------------------------------------

function fichiersCss(dossier) {
  const trouves = []

  for (const entree of readdirSync(dossier)) {
    const chemin = join(dossier, entree)

    if (statSync(chemin).isDirectory()) trouves.push(...fichiersCss(chemin))
    else if (entree.endsWith('.css')) trouves.push(chemin)
  }

  return trouves.sort()
}

const css = fichiersCss(dossierSource).map((chemin) => ({
  chemin,
  relatif: relative(racine, chemin),
  contenu: readFileSync(chemin, 'utf8'),
}))

const jetons = readFileSync(fichierJetons, 'utf8')

// ---------------------------------------------------------------------------
// 1. Jetons : aucun nom inconnu
// ---------------------------------------------------------------------------

titre('Jetons')

const definis = new Set(
  [...jetons.matchAll(/^\s*(--[a-z0-9-]+)\s*:/gm)].map((trouve) => trouve[1]),
)

const utilises = new Map()

for (const fichier of css) {
  for (const trouve of fichier.contenu.matchAll(/var\((--[a-z0-9-]+)/g)) {
    if (!utilises.has(trouve[1])) utilises.set(trouve[1], new Set())
    utilises.get(trouve[1]).add(fichier.relatif)
  }
}

const inconnus = [...utilises.keys()].filter((nom) => !definis.has(nom))

verdict(
  inconnus.length === 0,
  'tout var(--…) est défini dans tokens.css',
  inconnus.length === 0
    ? `${String(definis.size)} définis, ${String(utilises.size)} utilisés`
    : inconnus.map((nom) => `${nom} → ${[...utilises.get(nom)].join(', ')}`).join(' · '),
)

// Un jeton défini et jamais utilisé n'est pas un défaut : §12 est le système
// complet, et les étapes suivantes le consommeront. On l'affiche sans échouer.
const jamais = [...definis].filter((nom) => !utilises.has(nom)).sort()
if (jamais.length > 0) console.log(`         définis, pas encore utilisés : ${jamais.join(', ')}`)

// ---------------------------------------------------------------------------
// 2. Interdits de §12
// ---------------------------------------------------------------------------

titre('Règles non négociables de §12')

const interdits = [
  {
    libelle: 'aucune couleur en dur hors tokens.css',
    motif: /#[0-9a-fA-F]{3,8}\b|\brgba?\(|\bhsla?\(/,
    saufJetons: true,
  },
  {
    libelle: 'aucune ombre portée',
    motif: /box-shadow|text-shadow|drop-shadow/,
  },
  {
    libelle: 'le focus clavier n’est jamais supprimé',
    motif: /outline\s*:\s*(none|0)\b/,
  },
  {
    // « Nom du site lisible — ne pas tronquer au point de confondre deux
    // résidences » (SPEC.md, étape 14).
    libelle: 'aucune troncature par ellipse',
    motif: /text-overflow\s*:\s*ellipsis/,
  },
  {
    // Mesuré : aucune valeur d'opacité ne tient AA. À 0,90 le texte secondaire
    // est encore à 4,29:1, parce que l'opacité s'applique au conteneur et
    // affaiblit aussi ce qui était déjà juste au-dessus du seuil.
    libelle: 'aucune opacité partielle sur du texte',
    motif: /opacity\s*:\s*(0?\.\d+|0)\s*[;}]/,
  },
]

for (const interdit of interdits) {
  const trouves = []

  for (const fichier of css) {
    if (interdit.saufJetons === true && fichier.chemin === fichierJetons) continue

    fichier.contenu.split('\n').forEach((ligne, index) => {
      if (interdit.motif.test(ligne)) {
        trouves.push(`${fichier.relatif}:${String(index + 1)}`)
      }
    })
  }

  verdict(trouves.length === 0, interdit.libelle, trouves.join(' · '))
}

// ---------------------------------------------------------------------------
// 3. Contrastes
// ---------------------------------------------------------------------------

titre('Contrastes WCAG AA, thèmes clair et sombre')

/** Les deux thèmes, lus dans tokens.css : la table ne peut pas divariguer du CSS. */
function lireTheme(source) {
  const table = {}

  for (const trouve of source.matchAll(/(--[a-z0-9-]+)\s*:\s*(#[0-9a-fA-F]{6})\s*;/g)) {
    table[trouve[1]] = trouve[2]
  }

  return table
}

const blocSombre = /@media\s*\(prefers-color-scheme:\s*dark\)\s*\{([\s\S]*?)\n\}/.exec(jetons)

if (blocSombre === null) {
  verdict(false, 'le bloc prefers-color-scheme: dark est présent dans tokens.css')
} else {
  const clair = lireTheme(jetons.slice(0, jetons.indexOf(blocSombre[0])))
  const sombre = { ...clair, ...lireTheme(blocSombre[1]) }

  /**
   * Les paires réellement peintes par les feuilles de style. Écrites à la main
   * parce qu'aucune analyse statique ne sait quel fond se trouve derrière un
   * texte : c'est la seule partie de ce vérificateur qui demande un humain.
   */
  const paires = [
    ['texte / canvas', '--texte', '--canvas', SeuilTexte],
    ['texte / surface', '--texte', '--surface', SeuilTexte],
    ['texte / surface-2', '--texte', '--surface-2', SeuilTexte],
    ['texte / brand-tint', '--texte', '--brand-tint', SeuilTexte],
    ['alerte / brand-tint', '--alerte', '--brand-tint', SeuilTexte],
    ['texte / hachure', '--texte', '--hachure', SeuilTexte],
    ['texte-2 / canvas', '--texte-2', '--canvas', SeuilTexte],
    ['texte-2 / surface', '--texte-2', '--surface', SeuilTexte],
    ['texte-2 / surface-2', '--texte-2', '--surface-2', SeuilTexte],
    ['sur-brand / brand', '--sur-brand', '--brand', SeuilTexte],
    ['sur-brand / brand-strong', '--sur-brand', '--brand-strong', SeuilTexte],
    ['alerte / surface', '--alerte', '--surface', SeuilTexte],
    ['alerte / surface-2', '--alerte', '--surface-2', SeuilTexte],
    ['retard / surface-2', '--retard', '--surface-2', SeuilTexte],
    ['realise / surface', '--realise', '--surface', SeuilTexte],
    // Le contour de focus est décalé de 2 px : il se dessine sur le fond du
    // parent, jamais sur celui de l'élément focalisé. Seuil non textuel.
    ['contour de focus : alerte / canvas', '--alerte', '--canvas', SeuilNonTextuel],
    ['contour de focus : alerte / surface', '--alerte', '--surface', SeuilNonTextuel],
  ]

  for (const [nom, theme] of [['clair', clair], ['sombre', sombre]]) {
    for (const [libelle, avant, arriere, seuil] of paires) {
      const a = theme[avant]
      const b = theme[arriere]

      if (a === undefined || b === undefined) {
        verdict(false, `${nom} · ${libelle}`, 'jeton absent de tokens.css')
        continue
      }

      const ratio = contraste(a, b)

      verdict(
        ratio >= seuil,
        `${nom} · ${libelle}`,
        `${ratio.toFixed(2)}:1 (seuil ${seuil.toFixed(1)})`,
      )
    }
  }

  /**
   * Combinaisons **mesurées comme non conformes**, à ne jamais peindre.
   *
   * Elles n'ont pas leur place dans la liste ci-dessus : leur ratio ne dépend que
   * des valeurs de §12, et aucun correctif de feuille de style ne les rendra
   * conformes. Les inscrire en « doit passer » produirait un échec permanent.
   *
   * Le vérificateur ne sait pas quel fond se trouve derrière un texte : il ne
   * peut donc pas les détecter dans le CSS. Il les rappelle, pour qu'on ne les
   * réinvente pas — celle qui reste a déjà été introduite deux fois par
   * inadvertance dans ce projet, et trouvée par cet outil les deux fois.
   *
   * `--texte-3` figurait ici. Le jeton a été **retiré de §12** : aucune valeur ne
   * tenait AA sans se confondre avec `--texte-2`. Sa réapparition ne relève donc
   * plus du rappel mais du contrôle des jetons plus haut, qui échoue sur tout
   * `var(--…)` absent de `tokens.css` — un garde-fou plus dur que ce bloc.
   */
  const interdites = [
    ['texte-2 sur brand-tint', '--texte-2', '--brand-tint'],
  ]

  titre('Combinaisons à ne jamais peindre — rappel mesuré, pas un échec')

  for (const [libelle, avant, arriere] of interdites) {
    const enClair = contraste(clair[avant], clair[arriere])
    const enSombre = contraste(sombre[avant], sombre[arriere])

    console.log(
      `         ${libelle.padEnd(24)} clair ${enClair.toFixed(2)}:1 · `
        + `sombre ${enSombre.toFixed(2)}:1   (seuil ${SeuilTexte.toFixed(1)})`,
    )
  }
}

function canal(composante) {
  const valeur = composante / 255

  return valeur <= 0.03928 ? valeur / 12.92 : ((valeur + 0.055) / 1.055) ** 2.4
}

function luminance(hexa) {
  const brut = hexa.slice(1)
  const [r, v, b] = [0, 2, 4].map((debut) => Number.parseInt(brut.slice(debut, debut + 2), 16))

  return 0.2126 * canal(r) + 0.7152 * canal(v) + 0.0722 * canal(b)
}

function contraste(premier, second) {
  const a = luminance(premier)
  const b = luminance(second)

  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05)
}

// ---------------------------------------------------------------------------

console.log(
  echecs === 0
    ? '\n\u001b[32mSystème d’interface conforme à CLAUDE.md §12.\u001b[0m'
    : `\n\u001b[31m${String(echecs)} manquement(s) à CLAUDE.md §12.\u001b[0m`,
)

process.exitCode = echecs === 0 ? 0 : 1
