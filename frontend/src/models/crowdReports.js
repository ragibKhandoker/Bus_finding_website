// F19 Bus crowd level & seat availability: calls to the server, the form rules and report freshness.
// The server checks everything again; nothing here is trusted by it.
import { apiRequest } from './api'
import { formatDate, formatNumber, formatTime, t } from './i18n'

const send = (method, path, token, body) => apiRequest(path, { token, method, ...(body === undefined ? {} : { body: JSON.stringify(body) }) })
const query = (params) => new URLSearchParams(Object.fromEntries(Object.entries(params).filter(([, v]) => v !== '' && v !== null && v !== undefined)))

export const crowdApi = {
  create: (body, token) => send('POST', '/api/bus-crowd-reports', token, body),
  update: (id, body, token) => send('PUT', `/api/bus-crowd-reports/${id}`, token, body),
  remove: (id, token) => send('DELETE', `/api/bus-crowd-reports/${id}`, token),
  get: (id, token) => apiRequest(`/api/bus-crowd-reports/${id}`, { token }),
  my: (token, page = 1) => apiRequest(`/api/bus-crowd-reports/my?page=${page}`, { token }),
  list: (filters, token) => apiRequest(`/api/bus-crowd-reports?${query(filters)}`, { token }),
  forBus: (busId, filters, token) => apiRequest(`/api/bus-crowd-reports/bus/${busId}?${query(filters)}`, { token }),
  live: (token) => apiRequest('/api/bus-crowd-reports/live', { token }),
}

export const CROWD_LEVELS = [
  { value: 'LOW', label: 'Low', tone: 'good', description: 'Very few passengers; plenty of space.' },
  { value: 'MEDIUM', label: 'Medium', tone: 'warn', description: 'Some passengers standing, but not extremely crowded.' },
  { value: 'HIGH', label: 'High', tone: 'bad', description: 'Heavily crowded, with very limited space.' },
]
export const SEAT_OPTIONS = [
  { value: 'AVAILABLE', label: 'Available', tone: 'good', description: 'Several empty seats are available.' },
  { value: 'LIMITED', label: 'Limited', tone: 'warn', description: 'Only a few seats are available.' },
  { value: 'NO_SEATS', label: 'No Seats', tone: 'bad', description: 'No empty seats are available.' },
]
export const CROWD_BY_VALUE = Object.fromEntries(CROWD_LEVELS.map((c) => [c.value, c]))
export const SEATS_BY_VALUE = Object.fromEntries(SEAT_OPTIONS.map((s) => [s.value, s]))

export const SORTS = [
  ['recent', 'Most recent'], ['least-crowded', 'Least crowded'], ['most-crowded', 'Most crowded'], ['seats', 'Seats available'],
]
export const FRESHNESS_FILTERS = [
  ['current', 'Current (last hour)'], ['VERY_RECENT', 'Very recent (0–15 min)'], ['RECENT', 'Recent (15–30 min)'], ['AGING', 'Aging (30–60 min)'],
  ['EXPIRED', 'Expired (over 1 hour)'], ['all', 'All reports'],
]

// ---- Freshness ----
// Always from the observation time. Matches CrowdFreshness on the server.
export const EDIT_WINDOW_MINUTES = 15
export const MAX_HOURS_BACK = 24

export const FRESHNESS = {
  VERY_RECENT: { label: 'Very Recent', tone: 'good' },
  RECENT: { label: 'Recent', tone: 'fresh' },
  AGING: { label: 'Aging', tone: 'warn' },
  EXPIRED: { label: 'Expired', tone: 'old' },
}

export const ageMinutes = (observedAt, now = Date.now()) => Math.max(0, Math.floor((now - new Date(observedAt).getTime()) / 60_000))

export function freshnessOf(observedAt, now = Date.now()) {
  const age = (now - new Date(observedAt).getTime()) / 60_000
  return age <= 15 ? 'VERY_RECENT' : age <= 30 ? 'RECENT' : age <= 60 ? 'AGING' : 'EXPIRED'
}

// "Last reported 45 minutes ago", "Last reported 2 hours ago".
export function agoText(observedAt, now = Date.now()) {
  const minutes = ageMinutes(observedAt, now)
  if (minutes < 1) return t('Reported just now')
  if (minutes < 60) return minutes === 1 ? t('Last reported 1 minute ago') : t('Last reported {count} minutes ago', { count: formatNumber(minutes) })
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return hours === 1 ? t('Last reported 1 hour ago') : t('Last reported {count} hours ago', { count: formatNumber(hours) })
  return t('Last reported on {date}', { date: dayTime(observedAt) })
}

// Minutes left to edit a report, 0 when the window is over.
export const editMinutesLeft = (report, now = Date.now()) =>
  report?.isMine && report.editableUntil ? Math.max(0, Math.ceil((new Date(report.editableUntil).getTime() - now) / 60_000)) : 0

export const clock = (value) => formatTime(value)
export const dayTime = (value) => (value ? formatDate(value, { day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit' }) : '')

// ---- Dhaka time for the form ----
const DHAKA_OFFSET = 6 * 3_600_000
export const dhakaDate = (now = Date.now()) => new Date(now + DHAKA_OFFSET).toISOString().slice(0, 10)
export const dhakaTime = (now = Date.now()) => new Date(now + DHAKA_OFFSET).toISOString().slice(11, 16)
// "2026-11-10" + "08:30" (Dhaka) → milliseconds since the epoch.
export const dhakaToMillis = (date, time) => Date.parse(`${date}T${time}:00+06:00`)
// A stored UTC time → the Dhaka date and time fields.
export const toDhakaFields = (iso) => {
  const ms = new Date(iso).getTime()
  return { observationDate: dhakaDate(ms), observationTime: dhakaTime(ms) }
}

// ---- Buses, routes and stops for the form ----
// Each bus row is one route. Grouped by bus name, each route keeps its stops (with their database ids) in order.
export function groupBuses(routes) {
  const byName = new Map()
  for (const b of routes || []) {
    const name = (b.busName || '').trim()
    const id = b._id || b.id
    if (!name || !id) continue
    const stops = [...(b.stops || [])].sort((x, y) => (x.order ?? 0) - (y.order ?? 0)).filter((s) => s.id)
    const first = stops[0]?.name
    const last = stops[stops.length - 1]?.name
    const entry = byName.get(name) || { name, routes: [] }
    entry.routes.push({
      id, number: b.routeNumber, stops, first, last, runsBothWays: b.runsBothWays !== false,
      label: `${b.routeNumber ? `${t('Route {number}', { number: b.routeNumber })}: ` : ''}${first && last ? `${t(first)} → ${t(last)}` : ''}`,
    })
    byName.set(name, entry)
  }
  return [...byName.values()].sort((a, b) => a.name.localeCompare(b.name))
}

// ---- The form ----

export const emptyCrowdForm = (now = Date.now()) => ({
  busName: '', busId: '', busStopId: '', travelDirection: '', crowdLevel: '', seatAvailability: '',
  observationDate: dhakaDate(now), observationTime: dhakaTime(now), availableSeatCount: '', standingPassengerCount: '', additionalComments: '',
})

// A saved report back into the form, for editing.
export const formFromReport = (r) => ({
  busName: r.busName, busId: r.busId, busStopId: r.busStopId, travelDirection: r.travelDirection, crowdLevel: r.crowdLevel, seatAvailability: r.seatAvailability,
  ...toDhakaFields(r.observedAt), availableSeatCount: r.availableSeatCount ?? '', standingPassengerCount: r.standingPassengerCount ?? '',
  additionalComments: r.additionalComments || '',
})

const wholeNumber = (value) => /^\d+$/.test(String(value).trim())

// Problems with the form, by field; empty when it can be sent.
export function crowdProblems(form, now = Date.now()) {
  const p = {}
  if (!form.busName) p.busName = t('Choose the Dhaka bus you saw.')
  if (!form.busId) p.busId = t('Choose the route of this bus.')
  if (!form.busStopId) p.busStopId = t('Choose the stop where you saw the bus.')
  if (!['FORWARD', 'RETURN'].includes(form.travelDirection)) p.travelDirection = t('Choose the direction the bus was going.')
  if (!CROWD_BY_VALUE[form.crowdLevel]) p.crowdLevel = t('Choose how crowded the bus was.')
  if (!SEATS_BY_VALUE[form.seatAvailability]) p.seatAvailability = t('Choose whether seats were free.')
  const validDate = /^\d{4}-\d{2}-\d{2}$/.test(form.observationDate || '')
  const validTime = /^\d{2}:\d{2}$/.test(form.observationTime || '')
  if (!validDate) p.observationDate = t('Choose the date you saw the bus.')
  if (!validTime) p.observationTime = t('Choose the time you saw the bus.')
  if (validDate && validTime) {
    const at = dhakaToMillis(form.observationDate, form.observationTime)
    if (Number.isNaN(at)) p.observationTime = t('Choose the time you saw the bus.')
    else if (at > now + 5 * 60_000) p.observationTime = t('The time cannot be in the future.')
    else if (at < now - MAX_HOURS_BACK * 3_600_000) p.observationDate = t('Only report buses you saw in the last 24 hours.')
  }
  const seats = String(form.availableSeatCount ?? '').trim()
  if (seats !== '') {
    if (!wholeNumber(seats)) p.availableSeatCount = t('Enter a whole number, 0 or more.')
    else if (Number(seats) > 100) p.availableSeatCount = t('Enter at most 100 seats.')
    else if (form.seatAvailability === 'NO_SEATS' && Number(seats) > 0) p.availableSeatCount = t('You chose No Seats, so free seats must be 0 or empty.')
    else if (form.seatAvailability && form.seatAvailability !== 'NO_SEATS' && Number(seats) === 0) p.availableSeatCount = t('0 free seats means No Seats. Change one of them.')
  }
  const standing = String(form.standingPassengerCount ?? '').trim()
  if (standing !== '') {
    if (!wholeNumber(standing)) p.standingPassengerCount = t('Enter a whole number, 0 or more.')
    else if (Number(standing) > 200) p.standingPassengerCount = t('Enter at most 200 passengers.')
  }
  if ((form.additionalComments || '').trim().length > 500) p.additionalComments = t('Use at most 500 characters.')
  return p
}

const numberOrNull = (value) => (String(value ?? '').trim() === '' ? null : Number(value))

export const toCrowdBody = (form) => ({
  busId: form.busId, busStopId: form.busStopId, travelDirection: form.travelDirection, crowdLevel: form.crowdLevel, seatAvailability: form.seatAvailability,
  observationDate: form.observationDate, observationTime: form.observationTime, availableSeatCount: numberOrNull(form.availableSeatCount),
  standingPassengerCount: numberOrNull(form.standingPassengerCount), additionalComments: form.additionalComments.trim() || null,
})
