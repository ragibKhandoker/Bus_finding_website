import { apiRequest } from './api'

// QR boarding (see backend BusQrService). The code inside a bus is a link to this site's /scan page with the bus's
// secret token, so a phone's own camera app opens SmartCommute too; the in-app scanner reads the same link.
export const qrApi = {
  current: (token, vehicleId) => apiRequest(`/bus/${encodeURIComponent(vehicleId)}/qr`, { token }),
  generate: (token, vehicleId) => apiRequest(`/bus/${encodeURIComponent(vehicleId)}/generate-qr`, { token, method: 'POST' }),
  deactivate: (token, vehicleId) => apiRequest(`/bus/${encodeURIComponent(vehicleId)}/qr`, { token, method: 'DELETE' }),
  manageable: (token, routeId) => apiRequest(`/bus/qr/manageable${routeId ? `?routeId=${encodeURIComponent(routeId)}` : ''}`, { token }),
  verify: (token, code, point) => apiRequest('/bus/verify-qr', { token, method: 'POST', body: JSON.stringify({ code, ...pointBody(point) }) }),
  board: (token, body, point) => apiRequest('/trip/board-by-qr', { token, method: 'POST', body: JSON.stringify({ ...body, ...pointBody(point) }) }),
}

const pointBody = (point) => (point ? { lat: point.lat, lng: point.lng } : {})

// The link printed in the QR code: <this site>/scan?c=<token>.
export const qrLink = (token, origin = window.location.origin) => `${origin}/scan?c=${encodeURIComponent(token)}`

// The token from what a camera read (the link, or a bare token), or '' when it is not a SmartCommute code.
export function tokenFromScan(text) {
  const value = String(text || '').trim()
  let token = value
  try {
    const url = new URL(value)
    token = url.searchParams.get('c') || ''
  } catch { /* not a link: maybe the token itself */ }
  return /^[A-Za-z0-9_-]{40,64}$/.test(token) ? token : ''
}

// The phone's position, or null when location is off, refused or slow. Never throws. A quick network-based fix is
// enough to pick the nearest bus stop, and arrives much faster than a GPS one. maxAge: how old a remembered position
// may be (0 when the rider has moved, e.g. finding the stop they are getting off at).
export function currentPoint(timeout = 6000, maxAge = 60_000) {
  if (typeof navigator === 'undefined' || !navigator.geolocation) return Promise.resolve(null)
  return new Promise((resolve) => {
    const done = (value) => { clearTimeout(timer); resolve(value) }
    const timer = setTimeout(() => resolve(null), timeout + 500)
    navigator.geolocation.getCurrentPosition(
      (pos) => done({ lat: pos.coords.latitude, lng: pos.coords.longitude }),
      () => done(null),
      { enableHighAccuracy: false, timeout, maximumAge: maxAge },
    )
  })
}

// Straight-line distance in km between two { lat, lng } points.
export function distanceKm(a, b) {
  const rad = (d) => (d * Math.PI) / 180
  const dLat = rad(b.lat - a.lat)
  const dLng = rad(b.lng - a.lng)
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(rad(a.lat)) * Math.cos(rad(b.lat)) * Math.sin(dLng / 2) ** 2
  return 6371 * 2 * Math.asin(Math.sqrt(h))
}

// The stop (of those named in `allowed`, if given) nearest to the point, with its distance: { name, km } or null.
export function nearestStop(stops, point, allowed) {
  if (!point) return null
  let best = null
  for (const stop of stops || []) {
    if (allowed && !allowed.includes(stop.name)) continue
    if (typeof stop.lat !== 'number' || typeof stop.lng !== 'number') continue
    const km = distanceKm(point, stop)
    if (!best || km < best.km) best = { name: stop.name, km }
  }
  return best
}

// A stop counts as "where you are" within this distance (the server uses the same).
export const NEAR_STOP_KM = 1
