import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    // The app reads the API address from this; tests stub fetch, so nothing is really sent there.
    env: { VITE_API_BASE_URL: 'http://api.test' },
  },
})
