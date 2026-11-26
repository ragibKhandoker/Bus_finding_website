// F23 Live bus location: API calls, freshness helpers, and the driver's GPS sharing session.
//
// The sharing session lives here, outside any page, so it keeps running while the driver moves around the app
// (dashboard → schedule → trips). It only ever starts from the driver's own click, asks the browser for permission,
// and stops on "Stop", when the trip ends (the server answers 409), when the driver signs out, or when the page is
// closed (a keepalive request tells the server). Nothing is sent while sharing is off.
import { API_BASE, apiRequest } from './api'
import { t } from './i18n'

const send = (method, path, token, body) => apiRequest(path, { token, method, ...(body === undefined ? {} : { body: JSON.stringify(body) }) })
const query = (params = {}) => { const q = new URLSearchParams(Object.fromEntries(Object.entries(params).filter(([, v]) => v))); return q.toString() ? `?${q}` : '' }

export const liveApi = {
  me: (token) => apiRequest('/api/live-location/me', { token }),
  start: (token) => send('POST', '/api/live-location/start', token),
  update: (fix, token) => send('POST', '/api/live-location/update', token, fix),
  stop: (token) => send('POST', '/api/live-location/stop', token),
  active: (search, token) => apiRequest(`/api/live-location/active${query({ search })}`, { token }),
  bus: (busId, session, token) => apiRequest(`/api/live-location/bus/${busId}${query({ session })}`, { token }),
  progress: (busId, session, token) => apiRequest(`/api/live-location/bus/${busId}/progress${query({ session })}`, { token }),
}

export const FRESHNESS = {
  live: { label: 'Live', text: 'Updated in the last few seconds' },
  delayed: { label: 'Delayed', text: 'The last update is a little old' },
  unavailable: { label: 'Location unavailable', text: 'Showing the last known location' },
}

// Freshness from the age of a fix, with the same thresholds as the server (it sends them).
export function freshnessOf(secondsAgo, settings = { liveSeconds: 15, delayedSeconds: 60 }) {
  if (secondsAgo == null || !Number.isFinite(secondsAgo)) return 'unavailable'
  return secondsAgo <= settings.liveSeconds ? 'live' : secondsAgo <= settings.delayedSeconds ? 'delayed' : 'unavailable'
}

export function ago(seconds) {
  if (seconds == null) return t('never')
  if (seconds < 5) return t('just now')
  if (seconds < 60) return t('{n} s ago', { n: Math.round(seconds) })
  if (seconds < 3600) return t('{n} min ago', { n: Math.floor(seconds / 60) })
  return t('{n} h ago', { n: Math.floor(seconds / 3600) })
}

export const exactTime = (iso) => (iso ? new Intl.DateTimeFormat('en-GB', { timeZone: 'Asia/Dhaka', hour: '2-digit', minute: '2-digit', second: '2-digit', day: 'numeric', month: 'short' }).format(new Date(iso)) : '—')

// Straight-line metres between two fixes.
export function metresBetween(a, b) {
  const r = 6371000
  const toRad = (d) => (d * Math.PI) / 180
  const dLat = toRad(b.lat - a.lat)
  const dLng = toRad(b.lng - a.lng)
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(toRad(a.lat)) * Math.cos(toRad(b.lat)) * Math.sin(dLng / 2) ** 2
  return 2 * r * Math.asin(Math.sqrt(h))
}

// ---- The driver's sharing session ----

// Send at most every SEND_EVERY_MS, sooner after real movement, and repeat the last fix while standing still.
export const SEND_EVERY_MS = 8000
export const MOVE_SEND_MS = 3000
export const MOVE_METRES = 30
export const HEARTBEAT_MS = 10000

const GEO_ERRORS = {
  1: { code: 'denied', text: 'Location permission was denied. Allow location for this site in your browser settings, then try again.' },
  2: { code: 'unavailable', text: 'GPS is unavailable right now. Check that location services are on.' },
  3: { code: 'timeout', text: 'Getting your location is taking too long. Move to an open area; sharing keeps trying.' },
}

const initial = () => ({ status: 'idle', error: '', warning: '', fix: null, lastSentAt: null, lastServerAt: null, sentCount: 0, offline: false })
let state = initial()
let token = null
let watchId = null
let heartbeat = null
let sending = false
const listeners = new Set()

const set = (patch) => { state = { ...state, ...patch }; listeners.forEach((fn) => fn(state)) }
export const liveShareState = () => state
export function subscribeLiveShare(fn) { listeners.add(fn); fn(state); return () => listeners.delete(fn) }

const geo = () => (typeof navigator !== 'undefined' ? navigator.geolocation : undefined)

function clearWatching() {
  if (watchId !== null) { geo()?.clearWatch(watchId); watchId = null }
  if (heartbeat) { clearInterval(heartbeat); heartbeat = null }
}

async function push(fix, force = false) {
  if (!token || state.status !== 'sharing' || sending) return
  const now = Date.now()
  const last = state.lastSentFix
  const since = state.lastSentAt ? now - state.lastSentAt : Infinity
  const moved = last ? metresBetween(last, fix) : Infinity
  if (!force && since < SEND_EVERY_MS && !(moved >= MOVE_METRES && since >= MOVE_SEND_MS)) return
  if (typeof navigator !== 'undefined' && navigator.onLine === false) { set({ offline: true, warning: t('No internet connection. Your last location stays on the map as last known; sharing resumes when you are back online.') }); return }
  sending = true
  try {
    const res = await liveApi.update({ latitude: fix.lat, longitude: fix.lng, accuracy: fix.accuracy, speed: fix.speed, heading: fix.heading }, token)
    set({ lastSentAt: now, lastSentFix: fix, lastServerAt: res.lastUpdatedAt || state.lastServerAt, sentCount: state.sentCount + 1, offline: false, warning: '', error: '' })
  } catch (e) {
    if (e.status === 409 || e.status === 403) {
      // The server ended sharing (trip completed, not assigned any more…): stop here too.
      clearWatching()
      set({ status: 'idle', warning: '', error: t(e.message) })
    } else if (e.status === 400) {
      set({ warning: t(e.message) })
    } else if (e.status === 429) {
      set({ warning: t('Too many updates; the next one goes in a moment.') })
    } else {
      set({ offline: true, warning: t('Could not reach the server. Retrying with your next location…') })
    }
  } finally { sending = false }
}

function onPosition(position) {
  const c = position.coords
  const fix = {
    lat: c.latitude, lng: c.longitude, accuracy: Number.isFinite(c.accuracy) ? Math.round(c.accuracy) : null,
    speed: Number.isFinite(c.speed) && c.speed !== null ? Math.round(c.speed * 3.6 * 10) / 10 : null,
    heading: Number.isFinite(c.heading) && c.heading !== null ? Math.round(c.heading) : null, at: position.timestamp || Date.now(),
  }
  set({ fix, error: state.error && state.error.includes('GPS') ? '' : state.error })
  push(fix)
}

function onGeoError(err) {
  const info = GEO_ERRORS[err?.code] || GEO_ERRORS[2]
  if (info.code === 'denied') {
    clearWatching()
    set({ status: 'idle', error: t(info.text) })
    if (token) liveApi.stop(token).catch(() => {})
  } else {
    set({ warning: t(info.text) })
  }
}

// Starts sharing: the server first checks the driver may share (trip in progress, assigned bus), then the
// browser asks for location permission. Returns true when sharing is on.
export async function startLiveShare(nextToken) {
  if (state.status === 'sharing' || state.status === 'requesting') return state.status === 'sharing'
  if (!geo()) { set({ status: 'idle', error: t('This browser cannot share your location.') }); return false }
  if (typeof window !== 'undefined' && window.isSecureContext === false) {
    set({ status: 'idle', error: t('Location needs a secure (HTTPS) connection. Open the site over https:// or on localhost.') })
    return false
  }
  token = nextToken
  set({ ...initial(), status: 'requesting' })
  try {
    await liveApi.start(token)
  } catch (e) { set({ status: 'idle', error: t(e.message) }); return false }
  return new Promise((resolve) => {
    // The first fix both asks for permission and gives the starting position.
    geo().getCurrentPosition((position) => {
      set({ status: 'sharing' })
      onPosition(position)
      push(state.fix, true)
      watchId = geo().watchPosition(onPosition, onGeoError, { enableHighAccuracy: true, maximumAge: 5000, timeout: 20000 })
      // Standing still (a traffic jam) still counts: repeat the latest fix so commuters see it is live.
      heartbeat = setInterval(() => { if (state.fix && Date.now() - state.fix.at < 30000) push({ ...state.fix }, Date.now() - (state.lastSentAt || 0) >= HEARTBEAT_MS) }, 2000)
      resolve(true)
    }, (err) => {
      onGeoError(err)
      if (err?.code !== 1) set({ status: 'idle', error: t((GEO_ERRORS[err?.code] || GEO_ERRORS[2]).text) })
      liveApi.stop(token).catch(() => {})
      resolve(false)
    }, { enableHighAccuracy: true, maximumAge: 0, timeout: 20000 })
  })
}

// Stops sharing here and tells the server (unless it already ended it).
export async function stopLiveShare(reason = '') {
  const had = state.status === 'sharing' || state.status === 'requesting'
  clearWatching()
  const current = token
  set({ ...initial(), status: 'idle', error: reason ? t(reason) : '' })
  if (had && current) { try { await liveApi.stop(current) } catch { /* the server also stops it when the trip ends */ } }
}

// The trip is over on the server already: just stop watching.
export function endLiveShareLocally(message) {
  clearWatching()
  set({ ...initial(), status: 'idle', error: message ? t(message) : '' })
}

// Closing or reloading the page: stop sharing on the server too, so commuters are not shown a frozen bus as live.
if (typeof window !== 'undefined') {
  window.addEventListener('pagehide', () => {
    if (state.status !== 'sharing' || !token) return
    try {
      fetch(`${API_BASE}/api/live-location/stop`, { method: 'POST', keepalive: true, headers: { Authorization: `Bearer ${token}` } })
    } catch { /* the server marks it unavailable after a minute anyway */ }
  })
  window.addEventListener('online', () => { if (state.status === 'sharing' && state.fix) { set({ offline: false }); push({ ...state.fix, at: Date.now() }, true) } })
  window.addEventListener('offline', () => { if (state.status === 'sharing') set({ offline: true, warning: t('No internet connection. Your last location stays on the map as last known; sharing resumes when you are back online.') }) })
}
