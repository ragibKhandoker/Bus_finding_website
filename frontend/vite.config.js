import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// `npm run share` (scripts/share.mjs) sets SMARTCOMMUTE_SHARE=1: the dev site is opened through a temporary
// https://….trycloudflare.com link on any phone, and live updates must come back through that link (port 443).
const sharing = process.env.SMARTCOMMUTE_SHARE === '1'

// Every build gets its own id. It is baked into the app (__BUILD_ID__) and written to /version.json, so an open page
// can tell when a newer version has been deployed and refresh itself (see components/UpdateBanner.jsx).
const buildId = String(Date.now())
const versionFile = {
  name: 'smartcommute-version-file',
  generateBundle() { this.emitFile({ type: 'asset', fileName: 'version.json', source: JSON.stringify({ buildId }) }) },
}

export default defineConfig({
  plugins: [react(), versionFile],
  define: { __BUILD_ID__: JSON.stringify(buildId) },
  server: {
    // The site calls /__api/… when shared, so the phone only talks to one address; Vite forwards it to the local API.
    proxy: {
      '/__api': { target: 'http://127.0.0.1:3000', changeOrigin: true, rewrite: (path) => path.replace(/^\/__api/, '') },
    },
    ...(sharing ? { allowedHosts: ['.trycloudflare.com'], hmr: { clientPort: 443, protocol: 'wss' } } : {}),
  },
  test: {
    environment: 'jsdom',
    // The app reads the API address from this; tests stub fetch, so nothing is really sent there.
    env: { VITE_API_BASE_URL: 'http://api.test' },
    setupFiles: ['./src/testSetup.js'],
  },
})
