import { useCallback, useEffect, useState } from 'react'

// How often an open conversation, or a waiting password request, checks for news.
export const POLL_MS = 15000

// Polls a loader while mounted; returns [data, status, reload].
export function usePolling(load, deps) {
  const [state, setState] = useState({ data: null, status: 'loading' })
  const reload = useCallback(async (quiet = false) => {
    if (!quiet) setState((current) => ({ ...current, status: current.data ? current.status : 'loading' }))
    try { setState({ data: await load(), status: 'ready' }) } catch { setState((current) => current.data ? current : { data: null, status: 'error' }) }
  }, deps) // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => {
    reload()
    const timer = window.setInterval(() => reload(true), POLL_MS)
    return () => window.clearInterval(timer)
  }, [reload])
  return [state.data, state.status, reload, (data) => setState({ data, status: 'ready' })]
}

// ---- Users: Contact admin page ----
