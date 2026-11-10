// F18 Complaints & feedback: calls to the server and the form rules. The server checks everything again.
import { apiBlob, apiRequest } from './api'
import { formatDate, t } from './i18n'

const send = (method, path, token, body) => apiRequest(path, { token, method, ...(body === undefined ? {} : { body: JSON.stringify(body) }) })
const query = (params) => new URLSearchParams(Object.fromEntries(Object.entries(params).filter(([, v]) => v !== '' && v !== null && v !== undefined)))

export const complaintApi = {
  create: (body, token) => send('POST', '/api/complaints/create', token, body),
  my: (token) => apiRequest('/api/complaints/my', { token }),
  get: (id, token) => apiRequest(`/api/complaints/${id}`, { token }),
  history: (id, token) => apiRequest(`/api/complaints/${id}/history`, { token }),
  remove: (id, token) => send('DELETE', `/api/complaints/${id}`, token),
  all: (filters, token) => apiRequest(`/api/complaints/all?${query(filters)}`, { token }),
  stats: (token) => apiRequest('/api/complaints/stats', { token }),
  setStatus: (id, status, adminNotes, token) => send('PUT', `/api/complaints/${id}/status`, token, { status, adminNotes }),
  // Evidence is private: fetched with the sign-in token and shown as a blob.
  evidence: (url, token) => apiBlob(url, { token }),
}

export const CATEGORIES = [
  ['Overcharging / Extra Fare', '💸'], ['Staff Misbehavior', '😠'], ['Overcrowding', '👥'], ['Poor Bus Condition', '🔧'], ['Route Violation', '🔀'],
  ['Bus Not Stopping at Designated Stop', '🚏'], ['Delayed Bus Service', '⏰'], ['Safety Issues', '⚠️'], ['Harassment', '🛑'], ['Other Service Issues', '💬'],
]
export const CATEGORY_ICON = Object.fromEntries(CATEGORIES)

export const STATUSES = ['PENDING', 'REVIEWING', 'RESOLVED', 'REJECTED']
export const STATUS_LABELS = { PENDING: 'Pending', REVIEWING: 'Reviewing', RESOLVED: 'Resolved', REJECTED: 'Rejected' }

// Ready-made notes for admins; they can change them before saving.
export const NOTE_TEMPLATES = {
  REVIEWING: 'We are reviewing your complaint and checking with the bus operator.',
  RESOLVED: 'Complaint reviewed. Necessary action has been taken regarding the reported bus service issue.',
  REJECTED: 'Insufficient evidence was provided to verify this complaint.',
  PENDING: 'The complaint was moved back to pending for another look.',
}

export const MAX_EVIDENCE = 3

export const dhakaToday = (now = Date.now()) => new Date(now + 6 * 3_600_000).toISOString().slice(0, 10)

export const emptyComplaint = () => ({
  busName: '', busId: '', stopName: '', category: '', subject: '', description: '', incidentDate: dhakaToday(), incidentLocation: '', evidence: [],
})

// Problems with the form, by field; empty when it can be sent.
export function complaintProblems(form, today = dhakaToday()) {
  const p = {}
  if (!form.busName) p.busName = t('Choose the Dhaka bus.')
  if (!form.busId) p.busId = t('Choose the route of this bus.')
  if (!CATEGORIES.some(([c]) => c === form.category)) p.category = t('Choose what the complaint is about.')
  const subject = form.subject.trim()
  if (subject.length < 5) p.subject = t('Write a short subject (at least 5 characters).')
  else if (subject.length > 150) p.subject = t('Use at most 150 characters.')
  const description = form.description.trim()
  if (description.length < 20) p.description = t('Describe what happened in at least 20 characters.')
  else if (description.length > 2000) p.description = t('Use at most 2000 characters.')
  if (!/^\d{4}-\d{2}-\d{2}$/.test(form.incidentDate)) p.incidentDate = t('Choose the date it happened.')
  else if (form.incidentDate > today) p.incidentDate = t('The date cannot be in the future.')
  if (form.incidentLocation.trim().length < 2) p.incidentLocation = t('Enter where it happened, e.g. Farmgate.')
  if (form.evidence.length > MAX_EVIDENCE) p.evidence = t('Add at most {count} pictures.', { count: MAX_EVIDENCE })
  return p
}

export const toComplaintBody = (form) => ({
  busId: form.busId, category: form.category, subject: form.subject.trim(), description: form.description.trim(), incidentDate: form.incidentDate,
  incidentLocation: form.incidentLocation.trim(), ...(form.stopName ? { stopName: form.stopName } : {}),
  ...(form.evidence.length ? { evidence: form.evidence.map((e) => e.data) } : {}),
})

export const day = (value) => (value ? formatDate(value.length === 10 ? `${value}T00:00:00` : value, { day: 'numeric', month: 'short', year: 'numeric' }) : '')
export const dayTime = (value) => (value ? formatDate(value, { day: 'numeric', month: 'short', year: 'numeric', hour: 'numeric', minute: '2-digit' }) : '')
