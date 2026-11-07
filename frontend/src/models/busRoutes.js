import { apiRequest as request } from './api'

// The route list is fetched once per visit and shared by every page. A copy is kept in localStorage so a
// returning visitor sees the routes at once while the fresh list loads in the background.
export const ROUTES_STORAGE_KEY = 'sc_bus_routes'

export let routesCache = (() => {
  try { const saved = JSON.parse(localStorage.getItem(ROUTES_STORAGE_KEY) || 'null'); return Array.isArray(saved) ? saved : null } catch { return null }
})()

export let routesRequest = null

export let routesFresh = false

export const fetchBusRoutes = () => {
  routesRequest ??= request('/bus/all').then((data) => {
    routesCache = Array.isArray(data.data) ? data.data : []
    routesFresh = true
    try { localStorage.setItem(ROUTES_STORAGE_KEY, JSON.stringify(routesCache)) } catch { /* just not kept for next time */ }
    return routesCache
  }).finally(() => { routesRequest = null })
  return routesRequest
}

export const findCachedRoute = (routeNumber) => routesCache?.find((bus) => bus.routeNumber?.toLowerCase() === routeNumber?.toLowerCase())
