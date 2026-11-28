import React, { useEffect, useRef, useState } from 'react'
import { RefreshCw, X } from 'lucide-react'
import { t } from '../../models/i18n'
import '../styles/update-banner.css'

// The deployed site checks /version.json once a minute and whenever the person comes back to it. When a newer build
// has been deployed: if they were away (another app or tab), the page simply reloads as they return; if they are on
// it, a small banner offers Refresh, so nothing they are typing is lost. Only in production builds.
export const UPDATE_CHECK_MS = 60_000

export function UpdateBanner({ enabled = import.meta.env.PROD, current = typeof __BUILD_ID__ === 'string' ? __BUILD_ID__ : '' }) {
  const [ready, setReady] = useState(false)
  const newer = useRef(false)
  useEffect(() => {
    if (!enabled || !current) return undefined
    let stopped = false
    const check = async () => {
      if (newer.current) return
      try {
        const res = await fetch(`/version.json?t=${Date.now()}`, { cache: 'no-store' })
        if (!res.ok) return
        const { buildId } = await res.json()
        if (!stopped && buildId && buildId !== current) { newer.current = true; setReady(true) }
      } catch { /* offline or deploying: try again later */ }
    }
    const onVisible = () => {
      if (document.visibilityState !== 'visible') return
      if (newer.current) window.location.reload()
      else check()
    }
    const timer = window.setInterval(check, UPDATE_CHECK_MS)
    document.addEventListener('visibilitychange', onVisible)
    window.addEventListener('focus', onVisible)
    check()
    return () => { stopped = true; window.clearInterval(timer); document.removeEventListener('visibilitychange', onVisible); window.removeEventListener('focus', onVisible) }
  }, [enabled, current])

  if (!ready) return null
  return <div className="update-banner" role="status">
    <RefreshCw size={16} aria-hidden="true" />
    <span>{t('A new version of SmartCommute is ready.')}</span>
    <button type="button" className="update-banner-go" onClick={() => window.location.reload()}>{t('Refresh')}</button>
    <button type="button" className="update-banner-close" onClick={() => setReady(false)} aria-label={t('Close')}><X size={15} /></button>
  </div>
}
