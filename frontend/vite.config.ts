import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
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
      },
    },
  },
})
