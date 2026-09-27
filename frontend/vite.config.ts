import { existsSync, readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// ⚠️ Extension explicite, et elle est nécessaire : ce module est chargé sous Node,
// qui n'applique pas la résolution sans extension de Vite. Voir le piège des
// extensions d'import en CLAUDE.md §10.
import { CspDeveloppement, CspProduction, entetes } from './csp.ts'

/**
 * Certificat de développement, exporté depuis celui de .NET.
 *
 * Le serveur de développement parle **https**, et ce n'est pas du zèle : les trois
 * cookies de session portent `Secure` sans condition, et Safari jette un cookie
 * `Secure` reçu en clair — là où Chrome et Firefox font une exception pour
 * `localhost`. Le symptôme ne désigne jamais le cookie : la connexion réussit, la
 * navigation fonctionne, et le premier rechargement de page déconnecte, parce que
 * `GET /api/auth/moi` est le premier appel qui exige réellement le retour d'un
 * cookie (CLAUDE.md §10).
 *
 * La version précédente de ce fichier retirait l'attribut `Secure` des
 * `Set-Cookie` au passage du proxy. Ce contournement disparaît ici : servir la
 * page en https règle la cause au lieu de masquer l'effet, et le développement
 * cesse de différer de la production sur le point le plus sensible de
 * l'authentification.
 *
 * ⚠️ Le saut proxy → backend reste **en clair**, délibérément. Ce qui décide du
 * sort d'un cookie `Secure`, c'est l'origine que voit le navigateur ; ce saut-là
 * est en boucle locale sur la même machine, et lui imposer TLS n'ajouterait qu'un
 * certificat à gérer.
 */
const dossierCertificats = fileURLToPath(new URL('./certs/', import.meta.url))
const certificat = `${dossierCertificats}localhost.pem`
const cleCertificat = `${dossierCertificats}localhost.key`

const CommandeExport =
  'dotnet dev-certs https --export-path frontend/certs/localhost.pem'
  + ' --format PEM --no-password'

function tls(avertir: boolean): { cert: Buffer; key: Buffer } | undefined {
  if (existsSync(certificat) && existsSync(cleCertificat)) {
    return { cert: readFileSync(certificat), key: readFileSync(cleCertificat) }
  }

  // ⚠️ Silencieux à la construction. `vite build` charge ce fichier lui aussi, et
  // n'a aucun besoin de certificat : avertir là afficherait une alerte jaune à
  // chaque intégration continue, ce qui apprend à ignorer les alertes jaunes.
  if (!avertir) return undefined

  // Bruyant à dessein, en revanche, quand on sert la page. Sans certificat le
  // serveur repart en clair, donc fonctionne — sauf sous Safari, où la session se
  // perd au premier rechargement sans qu'aucune erreur ne l'explique. Mieux vaut
  // un avertissement qu'on ne peut pas manquer qu'un démarrage qui échoue et
  // bloque tout le monde.
  console.warn(
    '\n\u001b[33mAucun certificat de développement dans frontend/certs/.\u001b[0m\n'
    + '  Le serveur démarre en http, et les cookies « Secure » de KLEAN seront\n'
    + '  rejetés par Safari : la session se perdra au premier rechargement.\n'
    + `  Pour l'obtenir, depuis la racine du dépôt :\n\n    ${CommandeExport}\n`,
  )

  return undefined
}

/**
 * Le frontend n'appelle jamais l'URL absolue du backend : tout passe par `/api`.
 * Partagé par `server` et `preview` — voir le commentaire de `preview`.
 */
const Proxy = {
  '/api': {
    target: 'http://localhost:5137',
    changeOrigin: true,
  },
}

export default defineConfig(({ command }) => ({
  plugins: [react()],

  server: {
    port: 5173,
    strictPort: true,
    https: tls(command === 'serve'),

    // La CSP du développement, la seule qui tolère le script en ligne et la
    // liaison WebSocket de Vite. Voir csp.ts : elle n'est jamais servie ailleurs.
    headers: entetes(CspDeveloppement),

    // Le frontend n'appelle jamais l'URL absolue du backend : tout passe par /api.
    proxy: Proxy,
  },

  // `npm run preview` sert la construction de production, et le fait avec la CSP
  // de production. C'est le seul endroit où l'on peut constater qu'elle ne bloque
  // ni la feuille de style ni les polices **avant** de déployer — la vérification
  // qui manquait, et la raison pour laquelle la politique vit dans le dépôt.
  preview: {
    port: 4173,
    strictPort: true,
    https: tls(false),
    headers: entetes(CspProduction),

    // Le même proxy qu'en développement. Sans lui, `npm run preview` sert une
    // page qui ne peut appeler personne : on constaterait que la CSP « ne bloque
    // rien » sur un écran qui ne fait rien, ce qui ne prouve pas grand-chose.
    proxy: Proxy,
  },
}))
