// F22 Driver dashboard & trip management: server calls and display helpers. Every time is shown in Dhaka time
// (Asia/Dhaka), whatever the device's own time zone is. The server checks every rule again.
import { apiRequest } from './api'
import { getLang, t } from './i18n'

const send = (method, path, token, body) => apiRequest(path, { token, method, ...(body === undefined ? {} : { body: JSON.stringify(body) }) })
const query = (params = {}) => {
  const q = new URLSearchParams(Object.fromEntries(Object.entries(params).filter(([, v]) => v !== '' && v !== null && v !== undefined)))
  const text = q.toString()
  return text ? `?${text}` : ''
}

export const driverTripApi = {
  dashboard: (token) => apiRequest('/api/driver/dashboard', { token }),
  assignedBus: (token) => apiRequest('/api/driver/assigned-bus', { token }),
  today: (token) => apiRequest('/api/driver/schedule/today', { token }),
  trips: (params, token) => apiRequest(`/api/driver/trips${query(params)}`, { token }),
  upcoming: (params, token) => apiRequest(`/api/driver/trips/upcoming${query(params)}`, { token }),
  history: (params, token) => apiRequest(`/api/driver/trips/history${query(params)}`, { token }),
  trip: (id, token) => apiRequest(`/api/driver/trips/${id}`, { token }),
  start: (id, token) => send('POST', `/api/driver/trips/${id}/start`, token),
  complete: (id, token) => send('POST', `/api/driver/trips/${id}/complete`, token),
  routes: (token) => apiRequest('/api/driver/routes', { token }),
  workingStatus: (token) => apiRequest('/api/driver/working-status', { token }),
  setWorkingStatus: (status, reason, token) => send('PUT', '/api/driver/working-status', token, { status, reason }),
  statistics: (token) => apiRequest('/api/driver/statistics', { token }),
}

export const adminScheduleApi = {
  operations: (token) => apiRequest('/api/admin/driver-operations', { token }),
  statusHistory: (driverId, token) => apiRequest(`/api/admin/driver-operations/${driverId}/status-history`, { token }),
  list: (params, token) => apiRequest(`/api/admin/driver-schedules${query(params)}`, { token }),
  create: (body, token) => send('POST', '/api/admin/driver-schedules', token, body),
  update: (id, body, token) => send('PUT', `/api/admin/driver-schedules/${id}`, token, body),
  cancel: (id, reason, token) => send('DELETE', `/api/admin/driver-schedules/${id}`, token, { reason }),
  markMissed: (id, reason, token) => send('POST', `/api/admin/driver-schedules/${id}/mark-missed`, token, { reason }),
  assign: (body, token) => send('POST', '/api/admin/driver-assignments', token, body),
  setVehicle: (id, individualBusId, token) => send('PUT', `/api/admin/driver-assignments/${id}`, token, { individualBusId }),
  endAssignment: (id, token) => send('DELETE', `/api/admin/driver-assignments/${id}`, token),
}

export const TRIP_STATUSES = ['SCHEDULED', 'IN_PROGRESS', 'COMPLETED', 'CANCELLED', 'MISSED']
export const TRIP_STATUS_LABELS = { SCHEDULED: 'Scheduled', IN_PROGRESS: 'In progress', COMPLETED: 'Completed', CANCELLED: 'Cancelled', MISSED: 'Missed' }

export const WORK_STATUSES = ['AVAILABLE', 'ON_DUTY', 'ON_TRIP', 'BREAK', 'OFF_DUTY', 'UNAVAILABLE']
export const WORK_STATUS_LABELS = { AVAILABLE: 'Available', ON_DUTY: 'On Duty', ON_TRIP: 'On Trip', BREAK: 'Break', OFF_DUTY: 'Off Duty', UNAVAILABLE: 'Unavailable' }
export const WORK_STATUS_HELP = {
  AVAILABLE: 'Ready to perform your assigned duties.',
  ON_DUTY: 'Your duty period has started.',
  ON_TRIP: 'Set automatically while a trip is in progress.',
  BREAK: 'Taking a permitted break. You cannot start trips.',
  OFF_DUTY: 'Your duty period has ended.',
  UNAVAILABLE: 'Temporarily unavailable. You cannot start trips.',
}

const locale = () => (getLang() === 'bn' ? 'bn-BD' : 'en-BD')
const zoned = (options) => new Intl.DateTimeFormat(locale(), { timeZone: 'Asia/Dhaka', ...options })

export const clock = (iso) => (iso ? zoned({ hour: 'numeric', minute: '2-digit' }).format(new Date(iso)) : '—')
export const dayLabel = (iso) => (iso ? zoned({ weekday: 'short', day: 'numeric', month: 'short' }).format(new Date(iso.length === 10 ? `${iso}T12:00:00+06:00` : iso)) : '—')
export const fullDay = (iso) => (iso ? zoned({ weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }).format(new Date(iso.length === 10 ? `${iso}T12:00:00+06:00` : iso)) : '—')
export const dateTime = (iso) => (iso ? zoned({ day: 'numeric', month: 'short', year: 'numeric', hour: 'numeric', minute: '2-digit' }).format(new Date(iso)) : '—')

// Today's date in Dhaka as yyyy-MM-dd.
export const dhakaDate = (offsetDays = 0, now = Date.now()) => new Date(now + 6 * 3_600_000 + offsetDays * 86_400_000).toISOString().slice(0, 10)

export function minutes(total) {
  if (total == null) return '—'
  const h = Math.floor(total / 60)
  const m = Math.round(total % 60)
  return h ? t('{h} h {m} min', { h, m }) : t('{m} min', { m })
}

// "in 25 min", "in 3 h", "started" — for the next trip.
export function until(iso, now = Date.now()) {
  const diff = Math.round((new Date(iso).getTime() - now) / 60000)
  if (diff <= 0) return t('now')
  if (diff < 60) return t('in {n} min', { n: diff })
  if (diff < 24 * 60) return t('in {h} h {m} min', { h: Math.floor(diff / 60), m: diff % 60 })
  return t('in {d} days', { d: Math.round(diff / 1440) })
}

export const routeText = (trip) => `${t(trip.startStop)} → ${t(trip.endStop)}`
