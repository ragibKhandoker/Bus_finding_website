import { useCallback, useEffect, useState } from 'react'
import { apiRequest as request } from '../models/api'
import { errorText } from '../models/messages'
import { TRIP_CHANGED_EVENT } from '../models/trips'
import { formatNumber } from '../models/i18n'

// Runs load now and again whenever a trip is boarded or ended anywhere in the app.
export function useTripChanges(load) {
  useEffect(() => {
    load()
    window.addEventListener(TRIP_CHANGED_EVENT, load)
    return () => window.removeEventListener(TRIP_CHANGED_EVENT, load)
  }, [load])
}

// The rider's active trip. status: 'idle' (signed out), 'loading', 'ready' (trip may be null) or 'error'.
// clockOffset: server time minus this device's time, in ms (0 when the server did not say).
export function useActiveTrip(token, signedIn) {
  const [state, setState] = useState({ status: signedIn ? 'loading' : 'idle', trip: null, route: null, clockOffset: 0 })
  const load = useCallback(() => {
    if (!signedIn) { setState({ status: 'idle', trip: null, route: null, clockOffset: 0 }); return }
    request('/trip/active', { token })
      .then((data) => {
        const serverNow = data.serverTime ? Date.parse(data.serverTime) : NaN
        setState({ status: 'ready', trip: data.data || null, route: data.route || null, clockOffset: Number.isFinite(serverNow) ? serverNow - Date.now() : 0 })
      })
      .catch((e) => setState({ status: 'error', trip: null, route: null, clockOffset: 0, message: errorText(e) }))
  }, [token, signedIn])
  useTripChanges(load)
  return state
}

// Milliseconds since `since`, by the server's clock, updated every second. null while there is no start time.
export function useElapsed(since, clockOffset = 0) {
  const start = since ? Date.parse(since) : NaN
  const read = useCallback(() => (Number.isFinite(start) ? Math.max(0, Date.now() + clockOffset - start) : null), [start, clockOffset])
  const [elapsed, setElapsed] = useState(read)
  useEffect(() => {
    setElapsed(read())
    if (!Number.isFinite(start)) return
    const timer = window.setInterval(() => setElapsed(read()), 1000)
    return () => window.clearInterval(timer)
  }, [read, start])
  return elapsed
}

// A running clock: "12:04" under an hour, "1:05:23" after.
export function formatClock(ms) {
  if (ms === null || ms === undefined) return ''
  const total = Math.floor(ms / 1000)
  const h = Math.floor(total / 3600), m = Math.floor((total % 3600) / 60), s = total % 60
  // In the current language's digits (১:০৫:২৩ in Bangla).
  const two = (n) => formatNumber(n, { minimumIntegerDigits: 2, useGrouping: false })
  return h ? `${formatNumber(h, { useGrouping: false })}:${two(m)}:${two(s)}` : `${two(m)}:${two(s)}`
}
