// F20 Route & stop suggestions: calls to the server and the form rules, which match RouteSuggestionService. The
// server checks everything again, against the real bus data.
import { apiRequest } from './api'
import { formatDate, t } from './i18n'

const send = (method, path, token, body) => apiRequest(path, { token, method, ...(body === undefined ? {} : { body: JSON.stringify(body) }) })
const query = (params) => new URLSearchParams(Object.fromEntries(Object.entries(params).filter(([, v]) => v !== '' && v !== null && v !== undefined)))

export const suggestionApi = {
  create: (body, token) => send('POST', '/api/route-suggestions', token, body),
  update: (id, body, token) => send('PUT', `/api/route-suggestions/${encodeURIComponent(id)}`, token, body),
  my: (token, page = 1) => apiRequest(`/api/route-suggestions/my?${query({ page, pageSize: 50 })}`, { token }),
  get: (id, token) => apiRequest(`/api/route-suggestions/${encodeURIComponent(id)}`, { token }),
  remove: (id, token) => send('DELETE', `/api/route-suggestions/${encodeURIComponent(id)}`, token),
  all: (filters, token) => apiRequest(`/api/route-suggestions/all?${query(filters)}`, { token }),
  preview: (id, token) => apiRequest(`/api/route-suggestions/${encodeURIComponent(id)}/preview`, { token }),
  // extra: the coordinates an admin enters for places the user only described (latitude/longitude or stopLocations).
  review: (id, status, adminReviewNote, token, extra = {}) => send('PUT', `/api/route-suggestions/${encodeURIComponent(id)}/review`, token, { status, adminReviewNote, ...extra }),
}

export const TYPES = ['NEW_ROUTE', 'NEW_STOP', 'INCORRECT_INFORMATION']
export const TYPE_LABELS = { NEW_ROUTE: 'New Bus Route', NEW_STOP: 'New Bus Stop', INCORRECT_INFORMATION: 'Incorrect Information' }
export const TYPE_HELP = {
  NEW_ROUTE: 'A bus route that is missing from SmartCommute.',
  NEW_STOP: 'A stop that a listed bus uses but is not shown.',
  INCORRECT_INFORMATION: 'A wrong bus name, route number, bus type or stop.',
}

export const STATUSES = ['PENDING', 'APPROVED', 'REJECTED']
export const STATUS_LABELS = { PENDING: 'Pending', APPROVED: 'Approved', REJECTED: 'Rejected' }

export const BUS_TYPES = ['general', 'seating']
export const BUS_TYPE_LABELS = { general: 'General (local)', seating: 'Seating service' }

export const CORRECTION_FIELDS = ['BUS_NAME', 'ROUTE_NUMBER', 'BUS_TYPE', 'STOP_NAME', 'STOP_LOCATION']
export const CORRECTION_LABELS = {
  BUS_NAME: 'Bus name is wrong', ROUTE_NUMBER: 'Route number is wrong', BUS_TYPE: 'Bus type is wrong',
  STOP_NAME: "A stop's name is wrong", STOP_LOCATION: "A stop's location is wrong",
}
export const STOP_FIELDS = ['STOP_NAME', 'STOP_LOCATION']

export const MAX_ROUTE_STOPS = 60

export const emptySuggestion = (type = 'NEW_ROUTE') => ({
  // image: a newly chosen photo. When editing, hasPhoto says one is already attached and removePhoto drops it.
  type, title: '', description: '', locationName: '', image: null, hasPhoto: false, removePhoto: false,
  // NEW_ROUTE: middleStops are { name }.
  routeName: '', routeNumber: '', busType: 'general', startLocation: '', endLocation: '', middleStops: [],
  // NEW_STOP and INCORRECT_INFORMATION
  busName: '', busId: '', busStopName: '', relatedBusStopId: '', correctionField: '', correctedValue: '',
})

const clean = (text) => (text || '').trim().replace(/\s+/g, ' ')
const norm = (text) => clean(text).toLowerCase()

// "23.7509" → 23.7509; '' → null; anything else → NaN.
export const toNumber = (text) => (String(text ?? '').trim() === '' ? null : Number(String(text).trim()))

// Problems with a latitude/longitude pair (the admin enters them when approving): real numbers in range.
export function coordinateProblem(lat, lng, required = false) {
  const a = toNumber(lat)
  const b = toNumber(lng)
  if (a === null && b === null) return required ? t('Add the location: latitude and longitude.') : null
  if (a === null || b === null) return t('Give both latitude and longitude.')
  if (!Number.isFinite(a) || !Number.isFinite(b) || a < -90 || a > 90 || b < -180 || b > 180) return t('Latitude must be between -90 and 90 and longitude between -180 and 180.')
  return null
}

// Users describe places in words; coordinates are never asked of them.
export function suggestionProblems(form) {
  const p = {}
  if (!TYPES.includes(form.type)) p.type = t('Choose what you are suggesting.')
  const title = clean(form.title)
  if (title.length < 5 || title.length > 150) p.title = t('Write a short title (5 to 150 characters).')
  const description = (form.description || '').trim()
  if (description.length < 10 || description.length > 2000) p.description = t('Describe your suggestion in 10 to 2000 characters.')
  const where = clean(form.locationName)
  if (where.length > 200) p.locationName = t('Use at most 200 characters.')
  if (form.type === 'NEW_ROUTE') {
    const name = clean(form.routeName)
    if (name.length < 2 || name.length > 100) p.routeName = t('Enter the bus name, e.g. Raida Paribahan.')
    if (form.routeNumber.trim() && !/^[A-Za-z0-9][A-Za-z0-9-]{0,19}$/.test(form.routeNumber.trim())) p.routeNumber = t('Use letters, digits and dashes only, e.g. 36 or DBR-12.')
    const stops = routeStops(form)
    const seen = new Set()
    stops.forEach((s, i) => {
      const key = i === 0 ? 'startLocation' : i === stops.length - 1 ? 'endLocation' : `stop-${i - 1}`
      const name = clean(s.name)
      if (name.length < 2) p[key] = i === 0 ? t('Enter where the route starts.') : i === stops.length - 1 ? t('Enter where the route ends.') : t('Enter the stop name or remove this row.')
      else if (seen.has(norm(name))) p[key] = t('This stop is already in the list.')
      seen.add(norm(name))
    })
    if (stops.length > MAX_ROUTE_STOPS) p.middleStops = t('A route can have at most {count} stops.', { count: MAX_ROUTE_STOPS })
  } else if (form.type === 'NEW_STOP' || form.type === 'INCORRECT_INFORMATION') {
    if (!form.busName) p.busName = t('Choose the Dhaka bus.')
    else if (!form.busId) p.busId = t('Choose the route of this bus.')
  }
  if (form.type === 'NEW_STOP') {
    const name = clean(form.busStopName)
    if (name.length < 2 || name.length > 100) p.busStopName = t('Enter the stop name.')
    if (where.length < 3) p.locationName = t('Describe where the stop is, e.g. In front of the Bijoy Sarani signal.')
  }
  if (form.type === 'INCORRECT_INFORMATION') {
    if (!CORRECTION_FIELDS.includes(form.correctionField)) p.correctionField = t('Choose what is wrong.')
    if (STOP_FIELDS.includes(form.correctionField) && !form.relatedBusStopId) p.relatedBusStopId = t('Choose the stop.')
    if (form.correctionField === 'STOP_LOCATION') {
      if (where.length < 3) p.locationName = t('Describe where the stop really is, e.g. 50 m north, in front of the mosque.')
    } else if (form.correctionField === 'BUS_TYPE') {
      if (!BUS_TYPES.includes(form.correctedValue)) p.correctedValue = t('Choose the correct bus type.')
    } else if (form.correctionField === 'ROUTE_NUMBER') {
      if (!/^[A-Za-z0-9][A-Za-z0-9-]{0,19}$/.test(form.correctedValue.trim())) p.correctedValue = t('Enter the correct route number, e.g. 36 or DBR-12.')
    } else if (form.correctionField) {
      const value = clean(form.correctedValue)
      if (value.length < 2 || value.length > 100) p.correctedValue = t('Enter the correct name.')
    }
  }
  return p
}

// A saved suggestion as form values, for editing it.
export function toForm(s) {
  const stops = (s.stops || []).slice().sort((a, b) => a.order - b.order).map((x) => x.name)
  return {
    ...emptySuggestion(s.type),
    title: s.title || '', description: s.description || '', locationName: s.locationName || '', hasPhoto: Boolean(s.imageUrl),
    routeName: s.routeName || '', routeNumber: s.routeNumber || '', busType: s.busType || 'general',
    startLocation: stops[0] || '', endLocation: stops.length > 1 ? stops[stops.length - 1] : '', middleStops: stops.slice(1, -1).map((name) => ({ name })),
    busName: s.relatedBusName || '', busId: s.relatedBusId || '', busStopName: s.busStopName || '', relatedBusStopId: s.relatedBusStopId || '',
    correctionField: s.correctionField || '', correctedValue: s.correctedValue || '',
  }
}

// Start, the stops in between, then end, as the server wants them.
export const routeStops = (form) => [{ name: form.startLocation }, ...form.middleStops, { name: form.endLocation }]

// Only the fields of the chosen type are sent.
export function toSuggestionBody(form) {
  const body = { type: form.type, title: clean(form.title), description: form.description.trim(), locationName: clean(form.locationName) || null, image: form.image?.data || null }
  if (form.removePhoto) body.removeImage = true
  if (form.type === 'NEW_ROUTE') {
    Object.assign(body, {
      routeName: clean(form.routeName), routeNumber: form.routeNumber.trim() || null, busType: form.busType,
      stops: routeStops(form).map((s) => ({ name: clean(s.name) })),
    })
  } else {
    body.relatedBusId = form.busId
  }
  if (form.type === 'NEW_STOP') Object.assign(body, { busStopName: clean(form.busStopName) })
  if (form.type === 'INCORRECT_INFORMATION') {
    Object.assign(body, { correctionField: form.correctionField, relatedBusStopId: STOP_FIELDS.includes(form.correctionField) ? form.relatedBusStopId : null })
    if (form.correctionField !== 'STOP_LOCATION') body.correctedValue = form.correctionField === 'ROUTE_NUMBER' ? form.correctedValue.trim() : clean(form.correctedValue)
  }
  return body
}

// A search on OpenStreetMap for a described place in Dhaka, to help the admin find its coordinates.
export const mapSearchLink = (text) => `https://www.openstreetmap.org/search?query=${encodeURIComponent(`${text}, Dhaka`)}`

// Where the suggestion is, in a few words, for the cards.
export function suggestionPlace(s) {
  if (s.type === 'NEW_ROUTE') return [s.startLocation, s.endLocation].filter(Boolean).join(' → ')
  const route = s.relatedBusName ? `${t(s.relatedBusName)}${s.relatedRouteNumber ? ` (${s.relatedRouteNumber})` : ''}` : ''
  const stop = s.busStopName || s.relatedStopName
  return [route, stop && t(stop), s.locationName].filter(Boolean).join(' · ')
}

export const day = (iso) => formatDate(iso, { day: 'numeric', month: 'short', year: 'numeric' })

export const mapLink = (lat, lng) => `https://www.openstreetmap.org/?mlat=${lat}&mlon=${lng}#map=17/${lat}/${lng}`
