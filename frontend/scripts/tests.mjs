#!/usr/bin/env node
/**
 * Lanceur des séries frontend.
 *
 * Node 22.18 et plus dépouille les types TypeScript sans drapeau et porte son
 * propre lanceur de tests. Aucune dépendance n'est donc ajoutée : ni `vitest`,
 * ni `jsdom`, ni bibliothèque de rendu. `CLAUDE.md` §3 n'autorise que
 * `react-router-dom`, et il n'y a rien à négocier ici.
 *
 * ⚠️ Raison d'être de ce fichier : **`utils/dates.ts` doit être éprouvé dans
 * plusieurs fuseaux horaires**, et un fuseau se fixe par processus. Les deux
 * pièges de date de `CLAUDE.md` §10 sont symétriques — `toISOString` casse aux
 * décalages positifs, `new Date('AAAA-MM-JJ')` aux négatifs — et les deux
 * sabotages ont montré qu'aucun ne se voit sous `TZ=UTC`. Une intégration
 * continue qui ne tourne qu'en UTC, ce qui est le cas par défaut, validerait donc
 * un code faux dans les deux sens.
 *
 *     npm test
 */

import { spawnSync } from 'node:child_process'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const racine = resolve(dirname(fileURLToPath(import.meta.url)), '..')

/**
 * Cinq fuseaux choisis pour couvrir les cas qui cassent :
 * la cible du produit, celui de l'intégration continue, un décalage négatif, le
 * décalage positif extrême, et un décalage au quart d'heure.
 */
const Fuseaux = [
  'Europe/Paris',
  'UTC',
  'America/Los_Angeles',
  'Pacific/Kiritimati',
  'Pacific/Chatham',
]

/**
 * Séries dont le résultat dépend du fuseau : rejouées dans chacun.
 *
 * ⚠️ `valeurs.test.ts` en fait partie, et ce n'est pas par précaution : `rendu`
 * compose `jourIso`, `depuisChaineIso` et `formaterDateLongue` pour afficher une
 * date de tableau. La mauvaise composition — `new Date(valeur).getDate()` — décale
 * la colonne entière d'un jour à tout décalage négatif, et ne montre rien en
 * Europe. Éprouver les trois fonctions séparément ne prouve pas leur assemblage.
 */
const SensiblesAuFuseau = [
  { fichier: 'src/utils/dates.test.ts', libelle: 'dates.ts' },
  { fichier: 'src/reference/valeurs.test.ts', libelle: 'valeurs.ts' },
]

/**
 * Séries indifférentes au fuseau : une seule exécution chacune.
 *
 * ⚠️ Une exécution **par fichier**, et pas un seul `node --test` pour toute la
 * liste. Regrouper faisait attribuer l'échec au premier libellé de la liste : une
 * faute dans sections.test.ts était rapportée sur « client.ts — session et CSRF »,
 * ce qui envoie chercher au mauvais endroit.
 */
const Indifferentes = [
  { fichier: 'src/api/client.test.ts', libelle: 'client.ts — session et CSRF' },
  { fichier: 'src/api/ressources.test.ts', libelle: 'ressources.ts — URL de liste' },
  { fichier: 'src/layout/sections.test.ts', libelle: 'sections.ts — navigation et arrivée' },
  { fichier: 'src/reference/descriptions.test.ts', libelle: 'descriptions.ts — écrans de référence' },
  { fichier: 'src/auth/motDePasse.test.ts', libelle: 'motDePasse.ts — refus locaux' },
  { fichier: 'src/auth/secondFacteur.test.ts', libelle: 'secondFacteur.ts — codes et QR' },
  { fichier: 'src/stock/mouvements.test.ts', libelle: 'mouvements.ts — saisie d’un mouvement' },
  { fichier: 'src/stock/prevision.test.ts', libelle: 'prevision.ts — états et phrases' },
  { fichier: 'src/comptes/comptes.test.ts', libelle: 'comptes.ts — gestes et création' },
  { fichier: 'src/passage/passage.test.ts', libelle: 'passage.ts — gestes, preuves et checklist' },
]

let echecs = 0

function executer(libelle, fichiers, fuseau) {
  const resultat = spawnSync(
    process.execPath,
    ['--test', ...fichiers],
    {
      cwd: racine,
      env: fuseau === undefined ? process.env : { ...process.env, TZ: fuseau },
      encoding: 'utf8',
    },
  )

  const sortie = `${resultat.stdout ?? ''}${resultat.stderr ?? ''}`
  const compte = (nom) => /** @type {string} */ (
    new RegExp(`^# ${nom} (\\d+)$`, 'm').exec(sortie)?.[1] ?? '?'
  )

  const ok = resultat.status === 0

  if (!ok) echecs++

  console.log(
    `  ${ok ? '\u001b[32mOK   \u001b[0m' : '\u001b[31mÉCHEC\u001b[0m'} ` +
      `${libelle.padEnd(34)} ${compte('pass')} réussis, ${compte('fail')} échoués`,
  )

  // Sur échec, la sortie complète : un résumé ne sert à rien pour diagnostiquer.
  if (!ok) console.log(sortie.split('\n').map((ligne) => `        ${ligne}`).join('\n'))
}

console.log('\n\u001b[1mSéries indifférentes au fuseau\u001b[0m')
for (const serie of Indifferentes) {
  executer(serie.libelle, [serie.fichier], undefined)
}

console.log('\n\u001b[1mSéries sensibles au fuseau\u001b[0m')
for (const fuseau of Fuseaux) {
  // ⚠️ Une exécution par fichier ici aussi, pour la même raison qu'au-dessus. Une
  // première version groupait les deux séries par fuseau : une faute dans
  // valeurs.test.ts était rapportée sous « dates.ts — TZ=Europe/Paris », ce qui
  // envoie chercher dans le fichier le mieux éprouvé du dépôt.
  for (const serie of SensiblesAuFuseau) {
    executer(`${serie.libelle} — TZ=${fuseau}`, [serie.fichier], fuseau)
  }
}

console.log(
  echecs === 0
    ? '\n\u001b[32mToutes les séries frontend sont vertes.\u001b[0m'
    : `\n\u001b[31m${String(echecs)} série(s) en échec.\u001b[0m`,
)

process.exitCode = echecs === 0 ? 0 : 1
