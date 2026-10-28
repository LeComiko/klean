/**
 * Écrit sur la sortie standard le bloc `header` que Caddy renvoie avec le
 * document KLEAN en production (SPEC.md étape 18).
 *
 * La politique de sécurité du contenu n'est écrite qu'à un endroit,
 * `frontend/csp.ts`, et ce script la **lit** au moment de construire l'image.
 * Recopiée dans le Caddyfile, elle divergerait du jour où `index.html` charge une
 * ressource de plus — sans erreur, puisqu'une CSP trop stricte ne casse rien à la
 * construction et tout dans le navigateur.
 *
 *     node scripts/entetes-caddy.mjs > entetes-document.caddy
 *
 * ⚠️ Extension `.ts` explicite, et Node 22.18 au moins : c'est Node qui charge
 * `csp.ts`, pas Vite (CLAUDE.md §10, piège des extensions d'import).
 */
import { CspProduction, entetes } from '../csp.ts'

const lignes = Object.entries(entetes(CspProduction)).map(([nom, valeur]) => {
  // Les valeurs sont écrites entre guillemets doubles dans le Caddyfile. Une
  // valeur qui en contiendrait un fermerait la chaîne en silence et ferait lire
  // la suite comme une autre directive.
  if (valeur.includes('"') || valeur.includes('\n')) {
    throw new Error(`En-tête ${nom} : guillemet ou saut de ligne interdit dans « ${valeur} ».`)
  }

  return `\t${nom} "${valeur}"`
})

process.stdout.write(
  '# Généré par frontend/scripts/entetes-caddy.mjs depuis frontend/csp.ts.\n'
  + '# Ne pas modifier : reconstruire l\'image.\n'
  + `header {\n${lignes.join('\n')}\n}\n`,
)
