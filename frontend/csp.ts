/**
 * Politique de sécurité du contenu du **document** KLEAN.
 *
 * ⚠️ Ce n'est pas la politique de l'API, et les confondre serait une faute dans
 * les deux sens. L'API ne rend que du JSON : sa politique est
 * `default-src 'none'` et rien d'autre, écrite dans
 * `backend/Middleware/EnTetesSecurite.cs`. Y ajouter les origines de polices
 * ci-dessous l'affaiblirait pour un besoin qu'elle n'a pas ; inversement,
 * appliquer sa politique au document bloquerait la feuille de style et les
 * polices.
 *
 * ## Pourquoi ce fichier existe
 *
 * Une CSP est un **en-tête**, donc l'affaire de qui sert le document — le serveur
 * web de production, pas le code de l'application. Écrite dans un `nginx.conf`
 * elle serait invisible ici, et diverge­rait du jour où `index.html` charge une
 * ressource de plus. Écrite ici, elle est versionnée avec le code qu'elle décrit,
 * relue avec lui, et `vite preview` l'applique — ce qui permet de constater
 * qu'elle ne casse rien **avant** de déployer.
 *
 * Un `<meta http-equiv>` dans `index.html` aurait été plus direct, et c'est le
 * piège : le même document sert en développement, où Vite injecte du style et du
 * script en ligne et ouvre une liaison WebSocket. Une politique unique aurait
 * donc dû porter `'unsafe-inline'` en production pour que `vite dev` fonctionne —
 * c'est-à-dire abandonner l'essentiel de ce qu'une CSP protège, pour le confort
 * du développement.
 */

/**
 * Production.
 *
 * `default-src 'none'` puis une directive par besoin réel : rien n'est autorisé
 * qui n'ait été écrit.
 *
 * ⚠️ `connect-src 'self'` suppose que l'API répond sous la **même origine** que
 * le document — le déploiement le plus simple, et celui que l'étape 18
 * documentera. Un frontend servi depuis une origine distincte doit ajouter ici
 * l'origine de l'API, en plus de la déclarer dans `Cors:Origines` côté backend.
 * Les deux sont nécessaires et aucune ne remplace l'autre.
 *
 * ⚠️ `style-src` et `font-src` portent les deux origines de Google Fonts parce
 * que `index.html` y charge Figtree et IBM Plex Mono, dont dépend toute la
 * typographie de `CLAUDE.md` §12. Les retirer ne casse rien de visible au
 * développement — la police est en cache — et rend l'interface méconnaissable en
 * production.
 */
export const CspProduction = [
  "default-src 'none'",
  "script-src 'self'",
  "style-src 'self' https://fonts.googleapis.com",
  "font-src https://fonts.gstatic.com",
  "img-src 'self' data:",
  "connect-src 'self'",
  "base-uri 'none'",
  "form-action 'self'",
  "frame-ancestors 'none'",
].join('; ')

/**
 * Développement, et **elle ne doit jamais être servie ailleurs**.
 *
 * Les trois assouplissements sont ceux qu'exige le serveur de développement de
 * Vite, et aucun n'est négociable de son côté : le préambule de rafraîchissement
 * de React est un script en ligne, le remplacement de module à chaud injecte des
 * balises `<style>`, et il dialogue par WebSocket.
 *
 * C'est exactement la raison pour laquelle les deux politiques sont séparées.
 */
export const CspDeveloppement = [
  "default-src 'none'",
  "script-src 'self' 'unsafe-inline'",
  "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
  "font-src https://fonts.gstatic.com",
  "img-src 'self' data:",
  "connect-src 'self' ws: wss:",
  "base-uri 'none'",
  "form-action 'self'",
  "frame-ancestors 'none'",
].join('; ')

/**
 * En-têtes accompagnant la CSP. Mêmes valeurs que côté API, mêmes raisons —
 * voir `backend/Middleware/EnTetesSecurite.cs`.
 */
export function entetes(csp: string): Record<string, string> {
  return {
    'Content-Security-Policy': csp,
    'X-Content-Type-Options': 'nosniff',
    'Referrer-Policy': 'no-referrer',
    'X-Frame-Options': 'DENY',
  }
}
