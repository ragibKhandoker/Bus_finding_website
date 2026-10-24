// Shared client for the SmartCommute ASP.NET backend. Every page goes through here so errors,
// expired sign-ins and an unreachable server are handled the same way everywhere.

export const API_BASE = (import.meta.env.VITE_API_BASE_URL || 'http://localhost:3000').replace(/\/+$/, '')

// Fired when a request that carried a token comes back 401, meaning the sign-in has expired.
export const UNAUTHORIZED_EVENT = 'smartcommute:unauthorized'

// Reads the "exp" claim of a JWT as a millisecond timestamp, or null if the token cannot be read.
export function tokenExpiry(token) {
  try {
    const payload = JSON.parse(atob(token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')))
    return typeof payload.exp === 'number' ? payload.exp * 1000 : null
  } catch {
    return null
  }
}

export const isTokenExpired = (token) => {
  const expiry = tokenExpiry(token)
  return expiry === null || expiry <= Date.now()
}

// ignoreUnauthorized: do not fire UNAUTHORIZED_EVENT for this call (used when signing out).
export async function apiRequest(path, { token, ignoreUnauthorized = false, ...options } = {}) {
  let response
  try {
    response = await fetch(`${API_BASE}${path}`, {
      ...options,
      headers: {
        ...(options.body ? { 'Content-Type': 'application/json' } : {}),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...options.headers,
      },
    })
  } catch {
    throw new Error(`Cannot reach the SmartCommute server at ${API_BASE}. Make sure the backend is running.`)
  }

  const data = await response.json().catch(() => ({}))
  if (response.status === 401 && token && !ignoreUnauthorized) {
    window.dispatchEvent(new Event(UNAUTHORIZED_EVENT))
  }
  if (!response.ok) {
    throw new Error(data.message || data.error || `Request failed (${response.status})`)
  }
  return data
}
