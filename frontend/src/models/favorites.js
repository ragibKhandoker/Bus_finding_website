import { apiRequest } from './api'

// Favourite buses (Dhaka City buses only; the server refuses any other). The user always comes from the token.
export const busIdOf = (bus) => String(bus?._id || bus?.busId || '')

// Whether a bus can be favourited. Buses from the server say so (isDhakaCity); older data without the flag is
// left to the server to decide.
export const canFavourite = (bus) => bus?.isDhakaCity !== false

export const addFavorite = (busId, token) => apiRequest('/api/favourites', { token, method: 'POST', body: JSON.stringify({ busId }) })

export const removeFavorite = (busId, token) => apiRequest(`/api/favourites/${encodeURIComponent(busId)}`, { token, method: 'DELETE' })

export const myFavorites = (token) => apiRequest('/api/favourites/my', { token })

export const checkFavorite = (busId, token) => apiRequest(`/api/favourites/check/${encodeURIComponent(busId)}`, { token })

const json = (method, body) => ({ method, body: JSON.stringify(body) })

// The rest of the Smart Favourite Bus module.
export const favouriteApi = {
  dashboard: (token) => apiRequest('/api/favourites/dashboard', { token }),

  collections: (token) => apiRequest('/api/favourite-collections', { token }),
  createCollection: (name, token) => apiRequest('/api/favourite-collections', { token, ...json('POST', { name }) }),
  renameCollection: (id, name, token) => apiRequest(`/api/favourite-collections/${id}`, { token, ...json('PUT', { name }) }),
  deleteCollection: (id, token) => apiRequest(`/api/favourite-collections/${id}`, { token, method: 'DELETE' }),
  addToCollection: (id, busId, token, fromCollectionId) => apiRequest(`/api/favourite-collections/${id}/buses`, { token, ...json('POST', { busId, fromCollectionId }) }),
  removeFromCollection: (id, busId, token) => apiRequest(`/api/favourite-collections/${id}/buses/${busId}`, { token, method: 'DELETE' }),

  compare: (busIds, { from, to } = {}, token) => apiRequest('/api/favourites/compare', { token, ...json('POST', { busIds, from, to }) }),

  priceTracking: (token) => apiRequest('/api/favourites/price-tracking', { token }),
  priceHistory: (busId, token) => apiRequest(`/api/favourites/${busId}/price-history`, { token }),

  alerts: (token) => apiRequest('/api/favourites/availability-alerts', { token }),
  createAlert: (body, token) => apiRequest('/api/favourites/availability-alerts', { token, ...json('POST', body) }),
  updateAlert: (id, body, token) => apiRequest(`/api/favourites/availability-alerts/${id}`, { token, ...json('PUT', body) }),
  deleteAlert: (id, token) => apiRequest(`/api/favourites/availability-alerts/${id}`, { token, method: 'DELETE' }),

  reminders: (token) => apiRequest('/api/favourites/reminders', { token }),
  createReminder: (body, token) => apiRequest('/api/favourites/reminders', { token, ...json('POST', body) }),
  updateReminder: (id, body, token) => apiRequest(`/api/favourites/reminders/${id}`, { token, ...json('PUT', body) }),
  deleteReminder: (id, token) => apiRequest(`/api/favourites/reminders/${id}`, { token, method: 'DELETE' }),

  recentlyViewed: (token) => apiRequest('/api/favourites/recently-viewed', { token }),
  recordView: (busId, token) => apiRequest('/api/favourites/recently-viewed', { token, ...json('POST', { busId }) }),
  clearRecentlyViewed: (token) => apiRequest('/api/favourites/recently-viewed', { token, method: 'DELETE' }),

  notifications: (token, { unreadOnly = false } = {}) => apiRequest(`/api/favourites/notifications?${new URLSearchParams({ pageSize: '50', unreadOnly: String(unreadOnly) })}`, { token }),
  readNotification: (id, token) => apiRequest(`/api/favourites/notifications/${id}/read`, { token, method: 'PUT' }),
  readAllNotifications: (token) => apiRequest('/api/favourites/notifications/read-all', { token, method: 'PUT' }),
  deleteNotification: (id, token) => apiRequest(`/api/favourites/notifications/${id}`, { token, method: 'DELETE' }),
}

// Bangladesh time (UTC+6, no daylight saving), as the value of a datetime-local input: "2026-11-10T08:30".
export const dhakaInputValue = (date = new Date()) => new Date(date.getTime() + 6 * 3600 * 1000).toISOString().slice(0, 16)

// "2026-11-10T08:30+06:00" from the server, shown as Dhaka time whatever the device's time zone is.
export const dhakaParts = (local) => {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/.exec(local || '')
  return match ? { date: new Date(Date.UTC(+match[1], +match[2] - 1, +match[3])), time: `${match[4]}:${match[5]}` } : null
}
