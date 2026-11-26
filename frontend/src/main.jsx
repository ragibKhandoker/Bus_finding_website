import React from 'react'
import { createRoot } from 'react-dom/client'
import './views/styles/theme.css'
import './views/styles/style.css'
import './views/styles/home.css'
import './views/styles/profile.css'
import './views/styles/admin.css'
import './views/styles/polish.css'
import { App } from './views/App'
import { PageErrorBoundary } from './views/components/PageErrorBoundary'
import './views/styles/driver.css'

// Fade out the splash screen from index.html once the app has drawn. It stays at least a short moment
// so it never just flickers, and waits for the web fonts (up to a limit) so the first view looks right.
function hideSplash() {
  const splash = document.getElementById('splash')
  if (!splash) return
  const MIN_VISIBLE_MS = 900
  const fontsReady = document.fonts?.ready ? Promise.race([document.fonts.ready, new Promise((r) => setTimeout(r, 1500))]) : Promise.resolve()
  fontsReady.then(() => {
    setTimeout(() => {
      splash.classList.add('splash-hide')
      setTimeout(() => splash.remove(), 600)
    }, Math.max(0, MIN_VISIBLE_MS - performance.now()))
  })
}

// The outer boundary covers the header and footer too, so nothing can leave a blank white screen.
createRoot(document.getElementById('root')).render(<React.StrictMode>
  <PageErrorBoundary onHome={() => window.location.assign('/')}><App /></PageErrorBoundary>
</React.StrictMode>)

// Start at the top once the app has drawn, in case anything moved the page while it loaded.
requestAnimationFrame(() => requestAnimationFrame(() => { window.scrollTo(0, 0); hideSplash() }))
