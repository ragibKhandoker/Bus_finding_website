import { apiRequest } from './api'

export const PAGE_SIZE = 20

// How often the bell re-checks the unread count while the tab is visible.
export const UNREAD_POLL_MS = 15000

// Fired after anything changes the unread count, so the bell updates at once.
export const NOTIFICATIONS_CHANGED = 'smartcommute:notifications-changed'

export const changed = () => window.dispatchEvent(new Event(NOTIFICATIONS_CHANGED))

export const notificationApi = {
  list: (userId, token, page = 1) => apiRequest(`/api/notifications/user/${encodeURIComponent(userId)}?page=${page}&pageSize=${PAGE_SIZE}`, { token }),
  unread: (userId, token) => apiRequest(`/api/notifications/unread/${encodeURIComponent(userId)}`, { token }),
  markRead: (id, token) => apiRequest(`/api/notifications/read/${encodeURIComponent(id)}`, { token, method: 'PATCH' }),
  markAllRead: (userId, token) => apiRequest(`/api/notifications/read-all/${encodeURIComponent(userId)}`, { token, method: 'PATCH' }),
  remove: (id, token) => apiRequest(`/api/notifications/${encodeURIComponent(id)}`, { token, method: 'DELETE' }),
  clear: (userId, token) => apiRequest(`/api/notifications/clear/${encodeURIComponent(userId)}`, { token, method: 'DELETE' }),
  sos: (body, token) => apiRequest('/api/notifications/sos', { token, method: 'POST', body: JSON.stringify(body) }),
}
