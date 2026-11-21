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

import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs'
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
    // Le lien de retour du bandeau de descente de niveau. Il est en `texte` et non
    // en `brand-strong` : voir la combinaison interdite plus bas, mesurée.
    ['texte souligné / brand-tint', '--texte', '--brand-tint', SeuilTexte],
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
    // Le QR code du second facteur : une machine le lit, dans les deux thèmes. Un
    // `--qr-module` redéfini en sombre pour « suivre le thème » tomberait ici.
    ['QR : module / fond', '--qr-module', '--qr-fond', SeuilTexte],
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
    // Introduite au lot B de l'étape 16 bis, sur le lien de retour du bandeau de
    // descente de niveau, et trouvée par une mesure faite à la main avant d'être
    // ajoutée ici. Deux gris de marque superposés : le clair trompe, le sombre
    // s'effondre. Le lien est passé en `texte` souligné.
    ['brand-strong sur brand-tint', '--brand-strong', '--brand-tint'],
    ['brand sur brand-tint', '--brand', '--brand-tint'],
    // Posée au même endroit et au même moment, sur le lien de descente de niveau du
    // tableau. Même cause : un gris de marque lu comme « un ton plus marqué » alors
    // qu'il s'agit d'un **fond**. Le thème clair le laisse passer à 12,47:1, le
    // sombre tombe à 2,29:1.
    ['brand-strong sur surface', '--brand-strong', '--surface'],
  ]

  titre('Combinaisons à ne jamais peindre — rappel mesuré, pas un échec')

  for (const [libelle, avant, arriere] of interdites) {
    const enClair = contraste(clair[avant], clair[arriere])
    const enSombre = contraste(sombre[avant], sombre[arriere])

    console.log(
      `         ${libelle.padEnd(28)} clair ${enClair.toFixed(2)}:1 · `
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
// Polices servies en propre — RGPD.md §9.1
// ---------------------------------------------------------------------------

titre('Polices : servies par KLEAN, jamais par un tiers')

// ⚠️ Cette section ne vérifie pas du goût, elle vérifie une conformité. Les deux
// familles de §12 étaient chargées depuis Google Fonts, ce qui transmettait
// l'adresse IP de chaque visiteur de chaque instance de KLEAN à un tiers, sans que
// l'exploitant l'ait choisi. Une régression ici ne casserait RIEN de visible — la
// typographie serait identique, le cache du navigateur masquerait même la requête —
// et ce silence est précisément la raison d'être de cette garde.

const dossierPublic = join(racine, 'public')

// 1. Chaque `url()` du CSS doit pointer sur un fichier réellement présent. Une
//    police absente ne lève aucune erreur de construction : le navigateur retombe
//    sur la fonte système, et l'interface « a juste l'air un peu différente ».
const referencees = new Set()

for (const { relatif, contenu } of css) {
  for (const [, cible] of contenu.matchAll(/url\(\s*['"]?([^'")]+)['"]?\s*\)/g)) {
    if (cible.startsWith('data:')) continue

    referencees.add(cible)

    if (/^https?:/i.test(cible)) {
      verdict(false, `ressource distante dans ${relatif}`, cible)
      continue
    }

    const surDisque = join(dossierPublic, cible.replace(/^\//, ''))
    verdict(existsSync(surDisque), `${cible} présent dans public/`, relatif)
  }
}

verdict(referencees.size >= 4, 'les quatre fichiers de police sont référencés',
  `${String(referencees.size)} url() trouvée(s)`)

// 2. Chaque police présente doit porter la signature `wOF2`. Un fichier tronqué par
//    un téléchargement interrompu fait exactement le même effet qu'un fichier
//    absent, sans l'avertissement d'un 404.
for (const fichier of readdirSync(join(dossierPublic, 'polices'))) {
  if (!fichier.endsWith('.woff2')) continue

  const entete = readFileSync(join(dossierPublic, 'polices', fichier)).subarray(0, 4).toString()
  verdict(entete === 'wOF2', `${fichier} est un woff2 valide`, entete)
}

// 3. La licence OFL de chaque famille doit accompagner les fichiers : c'est ce
//    qu'exige leur redistribution dans un dépôt sous AGPL.
for (const licence of ['Figtree-OFL.txt', 'IBMPlexMono-OFL.txt']) {
  verdict(existsSync(join(dossierPublic, 'polices', licence)), `licence ${licence} versée`)
}

// 4. Aucune origine externe, ni dans les deux politiques de sécurité du contenu, ni
//    dans le document. C'est la garde qui empêche de refaire le défaut : réintroduire
//    une police distante demande de rouvrir la politique, et ce test le refuse.
for (const fichier of ['csp.ts', 'index.html']) {
  const source = readFileSync(join(racine, fichier), 'utf8')

  // Les commentaires nomment les origines d'avant pour expliquer la correction : on
  // ne regarde donc que les lignes de code, pas la prose qui les documente.
  const code = source
    .split('\n')
    .filter((ligne) => !/^\s*(\*|\/\/|<!--|\s{6}[A-ZÀ-Ü⚠`])/.test(ligne))
    .join('\n')

  const externes = [...code.matchAll(/https?:\/\/(?!localhost)[a-z0-9.-]+/gi)].map((m) => m[0])

  verdict(externes.length === 0, `${fichier} ne déclare aucune origine externe`,
    externes.join(' '))
}

// ---------------------------------------------------------------------------
// Taille du texte — SPEC.md, étape 30
// ---------------------------------------------------------------------------

titre('Le texte suit la taille de police du navigateur')

// Une taille en `px` ignore le réglage « taille de police » du navigateur : seul le
// zoom de la page l'agrandit. Le défaut ne se voit pas à la taille par défaut, où
// 0,875rem et 14px se confondent au pixel près. D'où la règle, sur tout le CSS : une
// taille de police, un interligne, ou un jeton d'échelle ne s'écrivent jamais en `px`.
{
  const enPx = []

  for (const { relatif, contenu } of css) {
    // Les commentaires citent les valeurs de §12 en pixels : seul le code compte.
    const code = contenu.replace(/\/\*[\s\S]*?\*\//g, '')

    for (const m of code.matchAll(/(font-size|line-height|--[\w-]+-(?:taille|interligne))\s*:\s*([^;]*\d+(?:\.\d+)?px[^;]*);/g)) {
      enPx.push(`${relatif} › ${m[1]}: ${m[2].trim()}`)
    }
  }

  verdict(enPx.length === 0, 'aucune taille de texte en px', enPx.join(' · '))
}

// ---------------------------------------------------------------------------

console.log(
  echecs === 0
    ? '\n\u001b[32mSystème d’interface conforme à CLAUDE.md §12.\u001b[0m'
    : `\n\u001b[31m${String(echecs)} manquement(s) à CLAUDE.md §12.\u001b[0m`,
)

process.exitCode = echecs === 0 ? 0 : 1
