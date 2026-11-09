// Ride share (F11): the API client, the choices a ride can have, and the checks the forms run before sending.
// The server checks everything again and decides who may do what; nothing here is trusted by it.
import { apiRequest } from './api'
import { getLang, t } from './i18n'

export const VEHICLES = ['Car', 'Bike', 'CNG', 'Other']
export const GENDERS = ['Any', 'Male', 'Female']
export const CONTACTS = ['chat', 'phone', 'both']
export const SORTS = [['recent', 'Recently posted'], ['departure', 'Earliest departure'], ['cost', 'Lowest cost first'], ['seats', 'Most available seats']]
export const REPORT_REASONS = [['unsafe', 'Unsafe behaviour'], ['harassment', 'Harassment'], ['fake', 'Fake or misleading ride'], ['spam', 'Spam'], ['no_show', 'Did not show up'], ['other', 'Something else']]
export const MAX_PARTICIPANTS = 10
export const PAGE_SIZE = 12

export const GENDER_LABELS = { Any: 'Any gender', Male: 'Male only', Female: 'Female only' }
export const CONTACT_LABELS = { chat: 'In-app chat only', phone: 'Phone call', both: 'Chat and phone' }
export const VEHICLE_CAPACITY = { Car: MAX_PARTICIPANTS, Bike: 2, CNG: 4, Other: MAX_PARTICIPANTS }
// Product names of the ride and request statuses, as the server sends them in `state`.
export const STATE_LABELS = { OPEN: 'Open', FULL: 'Full', COMPLETED: 'Completed', CANCELLED: 'Cancelled', EXPIRED: 'Expired' }
export const REQUEST_LABELS = { PENDING: 'Pending', APPROVED: 'Approved', REJECTED: 'Rejected', CANCELLED: 'Cancelled', REMOVED: 'Removed by host' }

const enc = encodeURIComponent
const json = (value) => JSON.stringify(value)
export const idOf = (item) => String(item?._id || item?.id || '')

export const rideApi = {
  search: (filters, page, token) => apiRequest(`/api/rideshare?${searchQuery(filters, page)}`, { token }),
  get: (id, token) => apiRequest(`/api/rideshare/${enc(id)}`, { token }),
  create: (ride, token) => apiRequest('/api/rideshare', { token, method: 'POST', body: json(ride) }),
  update: (id, ride, token) => apiRequest(`/api/rideshare/${enc(id)}`, { token, method: 'PUT', body: json(ride) }),
  cancel: (id, reason, token) => apiRequest(`/api/rideshare/${enc(id)}/cancel`, { token, method: 'POST', body: json({ reason: reason || null }) }),
  complete: (id, token) => apiRequest(`/api/rideshare/${enc(id)}/complete`, { token, method: 'POST' }),
  remove: (id, token) => apiRequest(`/api/rideshare/${enc(id)}`, { token, method: 'DELETE' }),
  hosted: (token) => apiRequest('/api/rideshare/my/hosted', { token }),
  joined: (token) => apiRequest('/api/rideshare/my/joined', { token }),
  join: (id, note, token) => apiRequest(`/api/rideshare/${enc(id)}/requests`, { token, method: 'POST', body: json({ note: note || null }) }),
  requests: (id, token) => apiRequest(`/api/rideshare/${enc(id)}/requests`, { token }),
  approve: (requestId, token) => apiRequest(`/api/rideshare/requests/${enc(requestId)}/approve`, { token, method: 'POST' }),
  reject: (requestId, token) => apiRequest(`/api/rideshare/requests/${enc(requestId)}/reject`, { token, method: 'POST' }),
  cancelRequest: (requestId, token) => apiRequest(`/api/rideshare/requests/${enc(requestId)}`, { token, method: 'DELETE' }),
  removePassenger: (id, userId, token) => apiRequest(`/api/rideshare/${enc(id)}/passengers/${enc(userId)}`, { token, method: 'DELETE' }),
  messages: (id, after, token) => apiRequest(`/api/rideshare/${enc(id)}/messages${after ? `?after=${enc(after)}` : ''}`, { token }),
  send: (id, text, token) => apiRequest(`/api/rideshare/${enc(id)}/messages`, { token, method: 'POST', body: json({ text }) }),
  reviews: (id, token) => apiRequest(`/api/rideshare/${enc(id)}/reviews`, { token }),
  review: (id, review, token) => apiRequest(`/api/rideshare/${enc(id)}/reviews`, { token, method: 'POST', body: json(review) }),
  profile: (userId, token) => apiRequest(`/api/rideshare/users/${enc(userId)}/profile`, { token }),
  reportRide: (id, report, token) => apiRequest(`/api/rideshare/${enc(id)}/report`, { token, method: 'POST', body: json(report) }),
  reportUser: (userId, report, token) => apiRequest(`/api/rideshare/users/${enc(userId)}/report`, { token, method: 'POST', body: json(report) }),
  block: (userId, token) => apiRequest(`/api/rideshare/users/${enc(userId)}/block`, { token, method: 'POST' }),
  unblock: (userId, token) => apiRequest(`/api/rideshare/users/${enc(userId)}/block`, { token, method: 'DELETE' }),
}

// ---- Place suggestions inside Dhaka ----
// Bus stops come first and appear at once: every Dhaka stop (about 18 KB) is fetched once per tab and matched in the
// browser as the user types. The map search (slower, about a second) runs a moment after typing stops and its
// places are added when they arrive, so a slow map never holds the list back.

let stopsPromise = null
export function loadStops() {
  if (!stopsPromise) {
    stopsPromise = apiRequest('/api/stops')
      .then((d) => (d.data || []).map((s) => ({ name: s.name, lat: Number(s.lat), lng: Number(s.lng), kind: 'stop' }))
        .filter((s) => s.name && inDhaka(s.lat, s.lng)))
      .catch((error) => { stopsPromise = null; throw error })
  }
  return stopsPromise
}
// Tests start each case without the cached lists.
export const forgetPlaceCaches = () => { stopsPromise = null; mapCache.clear() }

// Stops whose English or Bangla name matches what was typed: names starting with it first, then a word starting
// with it, then the rest.
export function matchStops(stops, text, limit = 6) {
  const q = text.trim().toLowerCase()
  if (!q) return []
  const scored = []
  for (const stop of stops) {
    const names = [stop.name.toLowerCase(), t(stop.name).toLowerCase()]
    const score = names.some((n) => n.startsWith(q)) ? 0 : names.some((n) => n.split(/[\s,-]+/).some((w) => w.startsWith(q))) ? 1 : names.some((n) => n.includes(q)) ? 2 : -1
    if (score >= 0) scored.push([score, stop])
  }
  return scored.sort((x, y) => x[0] - y[0] || x[1].name.localeCompare(y[1].name)).slice(0, limit).map(([, stop]) => stop)
}

const mapCache = new Map()
export async function searchMapPlaces(text) {
  const q = text.trim().toLowerCase()
  if (mapCache.has(q)) return mapCache.get(q)
  const d = await apiRequest(`/api/geocoding/search?q=${enc(text.trim())}&dhaka=true&limit=5&lang=en`)
  const places = (d.data || []).map((p) => ({ name: shortPlace(p.display_name), lat: Number(p.lat), lng: Number(p.lon), kind: 'place' }))
    .filter((p) => p.name && inDhaka(p.lat, p.lng))
  mapCache.set(q, places)
  if (mapCache.size > 100) mapCache.delete(mapCache.keys().next().value)
  return places
}

export function mergePlaces(...lists) {
  const seen = new Set()
  return lists.flat().filter((p) => { const key = p.name.toLowerCase(); if (seen.has(key)) return false; seen.add(key); return true }).slice(0, 8)
}

// Calls onUpdate(places, stillLoading) with the stop matches at once and again when the map places arrive.
// isStale() says a newer search has started, so this one stops before asking the map.
export async function searchPlaces(text, { onUpdate, isStale = () => false, mapDelayMs = 350 } = {}) {
  const q = text.trim()
  if (!q) { onUpdate?.([], false); return [] }
  let stops = []
  try { stops = matchStops(await loadStops(), q) } catch { /* the map can still answer */ }
  if (isStale()) return stops
  const wantMap = q.length >= 3
  onUpdate?.(stops, wantMap)
  if (!wantMap) return stops
  if (mapDelayMs) await new Promise((resolve) => window.setTimeout(resolve, mapDelayMs))
  if (isStale()) return stops
  let places = []
  try { places = await searchMapPlaces(q) } catch { /* stops are still shown */ }
  if (isStale()) return stops
  const merged = mergePlaces(stops, places)
  onUpdate?.(merged, false)
  return merged
}

// The same bounds the server checks (Data/dhaka-city-boundary.json).
export const DHAKA_BOUNDS = { minLat: 23.66, maxLat: 23.92, minLng: 90.32, maxLng: 90.52 }
export const inDhaka = (lat, lng) => lat >= DHAKA_BOUNDS.minLat && lat <= DHAKA_BOUNDS.maxLat && lng >= DHAKA_BOUNDS.minLng && lng <= DHAKA_BOUNDS.maxLng
// "Dhanmondi 27, Dhanmondi, Dhaka, Dhaka Division, 1209, Bangladesh" → "Dhanmondi 27, Dhanmondi".
export const shortPlace = (name = '') => name.split(',').map((part) => part.trim()).filter(Boolean).slice(0, 2).join(', ')

export const EMPTY_FILTERS = { source: '', destination: '', date: '', gender: 'all', vehicle: 'all', minSeats: '', minCost: '', maxCost: '', sort: 'recent' }

export function searchQuery(filters, page = 1) {
  const params = new URLSearchParams()
  const f = { ...EMPTY_FILTERS, ...filters }
  if (f.source.trim()) params.set('source', f.source.trim())
  if (f.destination.trim()) params.set('destination', f.destination.trim())
  if (f.date) params.set('date', f.date)
  if (f.gender !== 'all') params.set('gender', f.gender)
  if (f.vehicle !== 'all') params.set('vehicle', f.vehicle)
  if (f.minSeats) params.set('minSeats', f.minSeats)
  if (f.minCost !== '') params.set('minCost', f.minCost)
  if (f.maxCost !== '') params.set('maxCost', f.maxCost)
  if (f.sort !== 'recent') params.set('sort', f.sort)
  params.set('page', String(page))
  params.set('pageSize', String(PAGE_SIZE))
  return params.toString()
}

export const activeFilterCount = (filters) => Object.keys(EMPTY_FILTERS).filter((key) => key !== 'sort' && String(filters[key] ?? '') !== String(EMPTY_FILTERS[key])).length

// ---- Dhaka time ----

// Today's date in Dhaka ("2026-11-09"), whatever the browser's time zone.
export function dhakaToday(now = new Date()) {
  return new Date(now.getTime() + 6 * 3600 * 1000).toISOString().slice(0, 10)
}
// "2026-11-10T08:30+06:00" (or a UTC time) → { date: '2026-11-10', time: '08:30' } in Dhaka time.
export function dhakaParts(value) {
  if (!value) return null
  const at = new Date(value)
  if (Number.isNaN(at.getTime())) return null
  const local = new Date(at.getTime() + 6 * 3600 * 1000).toISOString()
  return { date: local.slice(0, 10), time: local.slice(11, 16) }
}
// "Mon, 10 Nov · 8:30 PM" in Dhaka time.
export function formatDeparture(value) {
  if (!value) return t('Time not set')
  const at = new Date(value)
  if (Number.isNaN(at.getTime())) return t('Time not set')
  const lang = getLang() === 'bn' ? 'bn-BD' : 'en-GB'
  const date = at.toLocaleDateString(lang, { weekday: 'short', day: 'numeric', month: 'short', timeZone: 'Asia/Dhaka' })
  const time = at.toLocaleTimeString(lang, { hour: 'numeric', minute: '2-digit', hour12: true, timeZone: 'Asia/Dhaka' })
  return `${date} · ${time}`
}
// The departure split for a card: { day: 'Today' | 'Tomorrow' | 'Mon, 10 Nov', time: '8:30 PM' } in Dhaka time.
export function departureParts(value, now = new Date()) {
  if (!value) return null
  const at = new Date(value)
  if (Number.isNaN(at.getTime())) return null
  const lang = getLang() === 'bn' ? 'bn-BD' : 'en-GB'
  const dayOf = (d) => dhakaToday(d)
  const today = dayOf(now)
  const tomorrow = dayOf(new Date(now.getTime() + 24 * 3600 * 1000))
  const day = dayOf(at) === today ? t('Today') : dayOf(at) === tomorrow ? t('Tomorrow')
    : at.toLocaleDateString(lang, { weekday: 'short', day: 'numeric', month: 'short', timeZone: 'Asia/Dhaka' })
  return { day, time: at.toLocaleTimeString(lang, { hour: 'numeric', minute: '2-digit', hour12: true, timeZone: 'Asia/Dhaka' }) }
}

// "in 2 h 15 min", "in 3 days", "left 20 min ago".
export function relativeDeparture(value, now = Date.now()) {
  if (!value) return ''
  const diff = Math.round((new Date(value).getTime() - now) / 60000)
  const span = Math.abs(diff)
  const text = span < 60 ? t('{n} min', { n: span }) : span < 48 * 60 ? t('{h} h {m} min', { h: Math.floor(span / 60), m: span % 60 }) : t('{n} days', { n: Math.round(span / 1440) })
  return diff >= 0 ? t('in {time}', { time: text }) : t('left {time} ago', { time: text })
}

// ---- Create and edit form ----

export const EMPTY_RIDE = {
  source: null, destination: null, date: '', time: '', maxParticipants: '4', genderPreference: 'Any', vehicleType: 'Car',
  costPerPerson: '', instructions: '', contactPreference: 'chat', pickupDetails: '',
}

// The form filled from a ride, for editing.
export function formFromRide(ride) {
  const parts = dhakaParts(ride.departureLocal || ride.departureAt) || { date: '', time: '' }
  const place = (name, lat, lng) => name ? { name, lat: lat ?? null, lng: lng ?? null } : null
  return {
    source: place(ride.source, ride.sourceLat, ride.sourceLng),
    destination: place(ride.destination, ride.destinationLat, ride.destinationLng),
    date: parts.date, time: parts.time,
    maxParticipants: String(ride.maxParticipants), genderPreference: ride.genderPreference, vehicleType: ride.vehicleType || 'Car',
    costPerPerson: ride.costPerPerson == null ? '' : String(ride.costPerPerson), instructions: ride.instructions || '',
    contactPreference: ride.contactPreference || 'chat', pickupDetails: ride.pickupDetails || '',
  }
}

// Field errors for the ride form; an empty object means it can be sent. The server repeats every check.
export function validateRideForm(form, now = new Date()) {
  const errors = {}
  const placeOk = (p) => p && p.name?.trim() && Number.isFinite(p.lat) && Number.isFinite(p.lng)
  if (!form.source?.name?.trim()) errors.source = t('Choose where the ride starts.')
  else if (!placeOk(form.source)) errors.source = t('Pick the start from the Dhaka suggestions.')
  else if (!inDhaka(form.source.lat, form.source.lng)) errors.source = t('Rides are for Dhaka City only.')
  if (!form.destination?.name?.trim()) errors.destination = t('Choose where the ride goes.')
  else if (!placeOk(form.destination)) errors.destination = t('Pick the destination from the Dhaka suggestions.')
  else if (!inDhaka(form.destination.lat, form.destination.lng)) errors.destination = t('Rides are for Dhaka City only.')
  if (!errors.source && !errors.destination && form.source.name.trim().toLowerCase() === form.destination.name.trim().toLowerCase()) {
    errors.destination = t('The destination must be different from the start.')
  }
  if (!form.date) errors.date = t('Choose the departure date.')
  if (!form.time) errors.time = t('Choose the departure time.')
  if (form.date && form.time) {
    const at = new Date(`${form.date}T${form.time}:00+06:00`)
    if (Number.isNaN(at.getTime())) errors.date = t('Choose a real date and time.')
    else if (at.getTime() < now.getTime() - 60 * 1000) errors.time = t('The departure time cannot be in the past.')
    else if (at.getTime() > now.getTime() + 60 * 24 * 3600 * 1000) errors.date = t('Rides can be posted at most 60 days ahead.')
  }
  if (!VEHICLES.includes(form.vehicleType)) errors.vehicleType = t('Choose a vehicle type.')
  const max = Number(form.maxParticipants)
  const cap = VEHICLE_CAPACITY[form.vehicleType] || MAX_PARTICIPANTS
  if (!/^\d+$/.test(String(form.maxParticipants).trim()) || max < 2 || max > MAX_PARTICIPANTS) {
    errors.maxParticipants = t('Choose a whole number from 2 to {max}, counting you.', { max: MAX_PARTICIPANTS })
  } else if (max > cap) {
    errors.maxParticipants = form.vehicleType === 'Bike' ? t('A bike takes one rider, so choose 2.') : t('A {vehicle} takes at most {max} people, counting you.', { vehicle: form.vehicleType, max: cap })
  }
  if (!GENDERS.includes(form.genderPreference)) errors.genderPreference = t('Choose who can join.')
  const cost = String(form.costPerPerson).trim()
  if (cost === '') errors.costPerPerson = t('Enter the cost per person (0 if it is free).')
  else if (!/^\d+(\.\d{1,2})?$/.test(cost)) errors.costPerPerson = Number(cost) < 0 ? t('The cost cannot be negative.') : t('Enter an amount in taka, like 80 or 75.50.')
  else if (Number(cost) > 10000) errors.costPerPerson = t('The cost can be at most ৳10,000.')
  if (form.instructions.length > 500) errors.instructions = t('At most 500 characters.')
  if (form.pickupDetails.length > 300) errors.pickupDetails = t('At most 300 characters.')
  return errors
}

// What the server expects from the form.
export const rideBody = (form) => ({
  source: form.source.name.trim(), sourceLat: form.source.lat, sourceLng: form.source.lng,
  destination: form.destination.name.trim(), destinationLat: form.destination.lat, destinationLng: form.destination.lng,
  departureDate: form.date, departureTime: form.time,
  maxParticipants: Number(form.maxParticipants), genderPreference: form.genderPreference, vehicleType: form.vehicleType,
  costPerPerson: Number(form.costPerPerson), instructions: form.instructions.trim() || null,
  contactPreference: form.contactPreference, pickupDetails: form.pickupDetails.trim() || null,
})

export const errorText = (error) => t(error?.message || 'Something went wrong. Please try again.')
