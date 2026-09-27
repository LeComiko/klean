import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

/**
 * Retire l'attribut `Secure` d'un en-tête `Set-Cookie`, et rien d'autre.
 *
 * Découpage plutôt qu'expression régulière : un remplacement textuel sur
 * « secure » risque d'attraper autre chose dans une valeur de cookie, alors
 * qu'une comparaison exacte sur chaque attribut ne peut pas se tromper.
 */
function retirerSecure(biscuit: string): string {
  return biscuit
    .split(';')
    .filter((attribut) => attribut.trim().toLowerCase() !== 'secure')
    .join(';')
}

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    // Le frontend n'appelle jamais l'URL absolue du backend : tout passe par /api.
    proxy: {
      '/api': {
        target: 'http://localhost:5137',
        changeOrigin: true,

        /**
         * ⚠️ Contournement de **développement uniquement**, et le seul de ce type.
         *
         * Les trois cookies de session portent `Secure`. Le navigateur les reçoit
         * ici sur `http://localhost:5173`, donc en clair. Chrome et Firefox font
         * une exception pour `localhost`, qu'ils traitent comme une origine sûre ;
         * **Safari n'en fait pas** et les jette silencieusement. Le symptôme est
         * déroutant : la connexion réussit — le profil vient du corps de la
         * réponse — la navigation fonctionne — elle est en mémoire — et le premier
         * rechargement de page déconnecte, parce que `GET /api/auth/moi` est le
         * premier appel qui exige réellement le retour d'un cookie.
         *
         * Pourquoi ici et pas dans le backend : `ServiceSession` continue d'émettre
         * `Secure` sans condition, et `SessionTests` continue de l'exiger. Rendre
         * l'attribut dépendant de l'environnement aurait obligé à affaiblir ce
         * test, donc à supprimer la seule garde qui protège cet attribut. Ce
         * fichier, lui, ne s'exécute que sous `vite dev` : le contournement est
         * impossible en production par construction, pas par discipline.
         *
         * L'étape 16 apporte HTTPS ; ce bloc disparaîtra avec elle.
         */
        configure(proxy) {
          proxy.on('proxyRes', (reponse) => {
            const biscuits = reponse.headers['set-cookie']

            if (biscuits === undefined) return

            reponse.headers['set-cookie'] = biscuits.map(retirerSecure)
          })
        },
      },
    },
  },
})
