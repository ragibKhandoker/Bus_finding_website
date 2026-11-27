// F19 Travel preferences: a commuter's saved journeys (many per user). Calls to the server and the form rules,
// which match CommuterProfileService.CheckFields; the server checks everything again.
import { apiRequest } from './api'
import { t } from './i18n'

const send = (method, path, token, body) => apiRequest(path, { token, method, ...(body === undefined ? {} : { body: JSON.stringify(body) }) })

export const travelPreferenceApi = {
  my: (token) => apiRequest('/api/commuter-profile/my', { token }),
  count: (token) => apiRequest('/api/commuter-profile/my/count', { token }),
  get: (id, token) => apiRequest(`/api/commuter-profile/${encodeURIComponent(id)}`, { token }),
  create: (body, token) => send('POST', '/api/commuter-profile', token, body),
  update: (id, body, token) => send('PUT', `/api/commuter-profile/${encodeURIComponent(id)}`, token, body),
  remove: (id, token) => send('DELETE', `/api/commuter-profile/${encodeURIComponent(id)}`, token),
}

export const MAX_PREFERENCES = 20

export const BUS_TYPES = ['AC', 'Non-AC', 'Both']
export const BUS_TYPE_LABELS = { AC: 'AC', 'Non-AC': 'Non-AC', Both: 'AC or Non-AC' }

export const PURPOSES = [
  ['University', '🎓'], ['Office', '💼'], ['Coaching', '📚'], ['Shopping', '🛍️'], ['Medical', '🏥'], ['Personal', '🙂'], ['Other', '📍'],
]
export const PURPOSE_ICON = Object.fromEntries(PURPOSES)

// Saturday first, as the Bangladeshi week starts.
export const DAYS = ['Saturday', 'Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday']
export const DAY_SHORT = { Saturday: 'Sat', Sunday: 'Sun', Monday: 'Mon', Tuesday: 'Tue', Wednesday: 'Wed', Thursday: 'Thu', Friday: 'Fri' }
// The usual Dhaka work and study week.
export const WORK_WEEK = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday']

export const emptyPreference = () => ({
  preferenceName: '', startLocation: '', destination: '', busName: '', busId: '', preferredTravelTime: '08:00',
  weeklyFrequency: '5', travelDays: [...WORK_WEEK], busType: 'Both', travelPurpose: '',
})

// A saved preference as form values, for editing.
export const toForm = (p) => ({
  preferenceName: p.preferenceName || '', startLocation: p.startLocation || '', destination: p.destination || '',
  busName: p.frequentlyUsedBus || '', busId: p.busId || '', preferredTravelTime: p.preferredTravelTime || '',
  weeklyFrequency: String(p.weeklyFrequency ?? ''), travelDays: [...(p.travelDays || [])], busType: p.busType || 'Both', travelPurpose: p.travelPurpose || '',
})

const clean = (text) => (text || '').trim().replace(/\s+/g, ' ')

export const sortDays = (days) => DAYS.filter((d) => days.includes(d))

// Problems with the form, by field; empty when it can be sent.
export function preferenceProblems(form) {
  const p = {}
  const name = clean(form.preferenceName)
  if (name.length < 2 || name.length > 60) p.preferenceName = t('Give it a name of 2 to 60 characters, e.g. University Journey.')
  const start = clean(form.startLocation)
  const end = clean(form.destination)
  if (start.length < 2 || start.length > 100) p.startLocation = t('Enter where you usually start, e.g. Mirpur 10.')
  if (end.length < 2 || end.length > 100) p.destination = t('Enter where you usually go, e.g. Shahbag.')
  else if (start && start.toLowerCase() === end.toLowerCase()) p.destination = t('The destination must be different from the start.')
  if (!form.busName) p.busName = t('Choose the Dhaka bus you use most.')
  else if (!form.busId) p.busId = t('Choose the route of this bus.')
  if (!/^([01]\d|2[0-3]):[0-5]\d$/.test(form.preferredTravelTime || '')) p.preferredTravelTime = t('Choose a valid travel time.')
  const frequency = Number(form.weeklyFrequency)
  if (!Number.isInteger(frequency) || frequency < 1 || frequency > 7) p.weeklyFrequency = t('Weekly frequency must be between 1 and 7.')
  if (!form.travelDays?.length) p.travelDays = t('Choose at least one travel day.')
  else if (form.travelDays.some((d) => !DAYS.includes(d))) p.travelDays = t('Choose days from the list.')
  if (!BUS_TYPES.includes(form.busType)) p.busType = t('Choose AC, Non-AC or Both.')
  if (!PURPOSES.some(([x]) => x === form.travelPurpose)) p.travelPurpose = t('Choose why you make this journey.')
  return p
}

export const toPreferenceBody = (form) => ({
  preferenceName: clean(form.preferenceName), startLocation: clean(form.startLocation), destination: clean(form.destination),
  busId: form.busId, preferredTravelTime: form.preferredTravelTime, weeklyFrequency: Number(form.weeklyFrequency),
  travelDays: sortDays(form.travelDays), busType: form.busType, travelPurpose: form.travelPurpose,
})

// "08:30" → "8:30 AM".
export function formatTime(value) {
  const match = /^(\d{1,2}):(\d{2})/.exec(value || '')
  if (!match) return value || ''
  const hour = Number(match[1])
  return `${hour % 12 || 12}:${match[2]} ${hour < 12 ? t('AM') : t('PM')}`
}

// "Sun – Thu" style labels: every day, weekdays, or the short names.
export function daysLabel(days) {
  const sorted = sortDays(days || [])
  if (sorted.length === 7) return t('Every day')
  if (sorted.length === WORK_WEEK.length && WORK_WEEK.every((d) => sorted.includes(d))) return t('Sun – Thu')
  return sorted.map((d) => t(DAY_SHORT[d])).join(', ')
}
