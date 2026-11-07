import { apiRequest } from './api'

export const busIdOf = (bus) => String(bus?._id || bus?.busId || '')

export const addFavorite = (busId, token) => apiRequest('/api/favbus/add', { token, method: 'POST', body: JSON.stringify({ busId }) })

export const removeFavorite = (busId, token) => apiRequest(`/api/favbus/${encodeURIComponent(busId)}`, { token, method: 'DELETE' })
