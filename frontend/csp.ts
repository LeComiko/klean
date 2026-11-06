/**
 * Politique de sécurité du contenu du **document** KLEAN.
 *
 * ⚠️ Ce n'est pas la politique de l'API, et les confondre reste une faute — mais
 * plus pour la raison d'avant. L'API ne rend que du JSON : sa politique est
 * `default-src 'none'` et rien d'autre, écrite dans
 * `backend/Middleware/EnTetesSecurite.cs`. Le document, lui, charge une feuille de
 * style, un script et des polices, tous servis par lui-même : appliquer la
 * politique de l'API au document les bloquerait tous les trois.
 *
 * ⚠️ **Depuis le 3 octobre 2026, aucune des deux politiques ne déclare d'origine
 * externe.** Les polices étaient chargées depuis Google Fonts, ce qui transmettait
 * l'adresse IP de chaque visiteur de chaque instance de KLEAN à un tiers
 * (RGPD.md §9.1) ; elles sont désormais dans `public/polices/`. Ne pas réintroduire
 * d'origine distante ici : ce serait une régression de conformité avant d'être une
 * question technique, et le §2 de `CLAUDE.md` l'interdit — « pas de service tiers
 * obligatoire » vaut aussi pour ce qui ne figure pas dans `package.json`.
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
 * `blob:` dans `img-src` (étape 25) : les photos d'anomalie se lisent par
 * `api.getBlob`, qui seul sait rafraîchir une session, puis s'affichent par
 * `URL.createObjectURL` — tout comme l'aperçu de la photo avant l'envoi. Une URL
 * `blob:` ne désigne qu'un objet créé par la page elle-même : elle n'ouvre aucune
 * origine.
 *
 * ⚠️ `font-src 'self'` et non l'absence de directive : `default-src 'none'`
 * s'appliquerait sinon par héritage et bloquerait les quatre `.woff2` de
 * `public/polices/`. Le défaut ne se verrait pas au développement — le cache du
 * navigateur garde les polices — et rendrait l'interface méconnaissable en
 * production, ce qui est exactement le piège qu'avait tendu la version précédente
 * de ce fichier dans l'autre sens.
 */
export const CspProduction = [
  "default-src 'none'",
  "script-src 'self'",
  "style-src 'self'",
  "font-src 'self'",
  "img-src 'self' data: blob:",
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
 * C'est exactement la raison pour laquelle les deux politiques sont séparées — et
 * c'est désormais la **seule** : les polices étant servies en propre, les deux
 * politiques ne diffèrent plus que par `'unsafe-inline'` et `ws:`, que Vite exige
 * et que la production n'aura jamais.
 */
export const CspDeveloppement = [
  "default-src 'none'",
  "script-src 'self' 'unsafe-inline'",
  "style-src 'self' 'unsafe-inline'",
  "font-src 'self'",
  "img-src 'self' data: blob:",
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
