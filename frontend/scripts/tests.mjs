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

/** Séries dont le résultat dépend du fuseau : rejouées dans chacun. */
const SensiblesAuFuseau = ['src/utils/dates.test.ts']

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
  { fichier: 'src/layout/sections.test.ts', libelle: 'sections.ts — navigation et arrivée' },
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
  executer(`dates.ts — TZ=${fuseau}`, SensiblesAuFuseau, fuseau)
}

console.log(
  echecs === 0
    ? '\n\u001b[32mToutes les séries frontend sont vertes.\u001b[0m'
    : `\n\u001b[31m${String(echecs)} série(s) en échec.\u001b[0m`,
)

process.exitCode = echecs === 0 ? 0 : 1
