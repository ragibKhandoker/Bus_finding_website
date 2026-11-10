// F17 Lost & Found: calls to the server and the form rules. The server checks everything again.
import { API_BASE, apiRequest } from './api'
import { formatDate, t } from './i18n'

const send = (method, path, token, body) => apiRequest(path, { token, method, ...(body === undefined ? {} : { body: JSON.stringify(body) }) })

export const lostFoundApi = {
  all: (filters, token) => apiRequest(`/api/lostfound/all?${new URLSearchParams(Object.fromEntries(Object.entries(filters).filter(([, v]) => v !== '' && v !== null && v !== undefined)))}`, { token }),
  my: (token) => apiRequest('/api/lostfound/my', { token }),
  get: (id, token) => apiRequest(`/api/lostfound/${id}`, { token }),
  create: (body, token) => send('POST', '/api/lostfound/create', token, body),
  update: (id, body, token) => send('PUT', `/api/lostfound/${id}`, token, body),
  remove: (id, token) => send('DELETE', `/api/lostfound/${id}`, token),
  setStatus: (id, status, note, token) => send('PUT', `/api/lostfound/${id}/status`, token, { status, note }),
  // Messages about a report, between its author and one other user.
  startChat: (reportId, text, image, token) => send('POST', `/api/lostfound/${reportId}/conversations`, token, { text, ...(image ? { image } : {}) }),
  conversations: (token) => apiRequest('/api/lostfound/conversations', { token }),
  messages: (conversationId, after, token) => apiRequest(`/api/lostfound/conversations/${conversationId}/messages${after ? `?after=${encodeURIComponent(after)}` : ''}`, { token }),
  sendMessage: (conversationId, text, image, token) => send('POST', `/api/lostfound/conversations/${conversationId}/messages`, token, { text, ...(image ? { image } : {}) }),
  react: (conversationId, messageId, emoji, token) => send('PUT', `/api/lostfound/conversations/${conversationId}/messages/${messageId}/reaction`, token, { emoji }),
}

export const imageSrc = (url) => (url ? `${API_BASE}${url}` : '')

export const CATEGORIES = ['Mobile Phone', 'Wallet', 'Bag', 'Documents', 'Keys', 'Electronics', 'Clothing', 'Other']
export const CATEGORY_ICONS = { 'Mobile Phone': '📱', Wallet: '👛', Bag: '🎒', Documents: '📄', Keys: '🔑', Electronics: '🎧', Clothing: '🧥', Other: '📦' }
export const STATUS_LABELS = { open: 'Open', 'under-review': 'Under review', resolved: 'Resolved', closed: 'Closed' }
export const OWNER_STATUSES = ['open', 'resolved', 'closed']
export const ALL_STATUSES = ['open', 'under-review', 'resolved', 'closed']

export const MAX_IMAGE_BYTES = 5 * 1024 * 1024
const IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/webp']

// Today in Dhaka (UTC+6), as "2026-11-09", for the date box's maximum.
export const dhakaToday = (now = Date.now()) => new Date(now + 6 * 3_600_000).toISOString().slice(0, 10)

export const emptyForm = (type = 'lost') => ({
  reportType: type, itemName: '', category: '', busId: '', busName: '', location: '', incidentDate: dhakaToday(), incidentTime: '',
  description: '', contactInformation: '', image: '', imagePreview: '', removeImage: false, status: '',
})

export const formFromReport = (r) => ({
  reportType: r.reportType, itemName: r.itemName, category: r.category, busId: r.busId || '', busName: r.busName, location: r.location,
  incidentDate: r.incidentDate, incidentTime: r.incidentTime || '', description: r.description, contactInformation: r.contactInformation,
  image: '', imagePreview: r.imageUrl ? imageSrc(r.imageUrl) : '', removeImage: false, status: r.status,
})

// Problems with the form, by field; empty when it can be sent.
export function formProblems(form, today = dhakaToday()) {
  const p = {}
  if (!['lost', 'found'].includes(form.reportType)) p.reportType = t('Choose Lost or Found.')
  const item = form.itemName.trim()
  if (item.length < 2) p.itemName = t('Enter the item’s name (at least 2 characters).')
  else if (item.length > 100) p.itemName = t('Use at most 100 characters.')
  if (!CATEGORIES.includes(form.category)) p.category = t('Choose a category.')
  if (!form.busId) p.busId = t('Choose the Dhaka bus from the list.')
  if (form.location.trim().length < 2) p.location = t('Enter where it happened, e.g. near Farmgate stop.')
  if (!/^\d{4}-\d{2}-\d{2}$/.test(form.incidentDate)) p.incidentDate = t('Choose the date.')
  else if (form.incidentDate > today) p.incidentDate = t('The date cannot be in the future.')
  if (form.incidentTime && !/^\d{2}:\d{2}$/.test(form.incidentTime)) p.incidentTime = t('Enter the time like 18:30.')
  const description = form.description.trim()
  if (description.length < 10) p.description = t('Describe the item in at least 10 characters (colour, brand, marks…).')
  else if (description.length > 1000) p.description = t('Use at most 1000 characters.')
  const contact = form.contactInformation.trim()
  if (contact.length < 5) p.contactInformation = t('Enter a phone number or email so people can reach you.')
  else if (contact.length > 150) p.contactInformation = t('Use at most 150 characters.')
  return p
}

export const toBody = (form) => ({
  reportType: form.reportType, itemName: form.itemName.trim(), category: form.category, busId: form.busId, location: form.location.trim(),
  incidentDate: form.incidentDate, incidentTime: form.incidentTime || null, description: form.description.trim(),
  contactInformation: form.contactInformation.trim(), ...(form.image ? { image: form.image } : {}), ...(form.removeImage ? { removeImage: true } : {}),
  ...(form.status ? { status: form.status } : {}),
})

// Reads a picture chosen by the user as a data URL, after checking its type and size.
export function readImage(file) {
  return new Promise((resolve, reject) => {
    if (!file) { reject(new Error(t('Choose a picture.'))); return }
    if (!IMAGE_TYPES.includes(file.type)) { reject(new Error(t('Only JPG, PNG or WEBP pictures can be added.'))); return }
    if (file.size > MAX_IMAGE_BYTES) { reject(new Error(t('The picture must be at most 5 MB.'))); return }
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result))
    reader.onerror = () => reject(new Error(t('That picture could not be read.')))
    reader.readAsDataURL(file)
  })
}

// "9 Nov 2026 · 08:15"
export function incidentText(report) {
  const day = formatDate(`${report.incidentDate}T00:00:00`, { day: 'numeric', month: 'short', year: 'numeric' })
  return report.incidentTime ? `${day} · ${report.incidentTime}` : day
}
