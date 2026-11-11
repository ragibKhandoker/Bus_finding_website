// The driver console's data requests, each with a cache key, plus a small in-memory cache shared by every page.
// A page shows its cached data at once and refreshes it in the background (stale-while-revalidate), and the console
// fetches the other pages ahead of time, so moving between pages never waits for the network.
import { driverApi } from './driver'
import { dhakaDate, driverTripApi } from './driverTrips'

const cache = new Map()
let cacheOwner = null

// Entries belong to one sign-in; another token starts with an empty cache.
const own = (token) => {
  if (cacheOwner !== token) { cache.clear(); cacheOwner = token }
}

export function cached(token, key) {
  own(token)
  return cache.has(key) ? cache.get(key) : undefined
}

export function remember(token, key, value) {
  own(token)
  cache.set(key, value)
}

export function forgetDriverCache() { cache.clear(); cacheOwner = null }

// One request per page view. key: what the response depends on; load: the call itself.
export const driverRequests = {
  access: (token) => ({ key: 'access', load: () => driverApi.status(token).then((r) => r.data) }),
  workingStatus: (token) => ({ key: 'working-status', load: () => driverTripApi.workingStatus(token) }),
  dashboard: (token) => ({ key: 'dashboard', load: () => driverTripApi.dashboard(token).then((r) => r.data) }),
  bus: (token) => ({ key: 'bus', load: () => driverTripApi.assignedBus(token).then((r) => r.data) }),
  day: (token, date, status = '') => ({ key: `day:${date}:${status}`, load: () => driverTripApi.trips({ date, status, sort: 'oldest', pageSize: 100 }, token) }),
  nextTrip: (token) => ({ key: 'next-trip', load: () => driverTripApi.upcoming({ pageSize: 1 }, token).then((r) => r.data[0] || null) }),
  trips: (token, tab, params, page) => ({ key: `trips:${tab}:${page}`, load: () => driverTripApi.trips({ ...params, page, pageSize: 15 }, token) }),
  activeTrip: (token) => ({ key: 'active-trip', load: () => driverTripApi.trips({ status: 'IN_PROGRESS', pageSize: 1 }, token) }),
  history: (token, f) => ({ key: `history:${f.search}:${f.from}:${f.to}:${f.status}:${f.page}`, load: () => driverTripApi.history(f, token) }),
  upcoming: (token, params) => ({ key: `upcoming:${JSON.stringify(params)}`, load: () => driverTripApi.upcoming(params, token) }),
  routes: (token) => ({ key: 'routes', load: () => driverTripApi.routes(token).then((r) => r.data) }),
  stats: (token) => ({ key: 'stats', load: () => driverTripApi.statistics(token).then((r) => r.data) }),
}

// The default view of every console page, fetched one after another in the background once the console opens.
const inFlight = new Map()
const sequence = new Map()
// fresh: ask the server again even if the same request is already on its way (after the driver changed something).
export function fetchInto(token, request, { fresh = false } = {}) {
  own(token)
  if (!fresh && inFlight.has(request.key)) return inFlight.get(request.key)
  // Only the newest request for a key may write the cache, so a slow older answer never replaces a newer one.
  const id = (sequence.get(request.key) || 0) + 1
  sequence.set(request.key, id)
  const promise = request.load().then((value) => { if (sequence.get(request.key) === id) remember(token, request.key, value); return value })
    .finally(() => { if (inFlight.get(request.key) === promise) inFlight.delete(request.key) })
  inFlight.set(request.key, promise)
  return promise
}

export async function prefetchDriverConsole(token) {
  const today = dhakaDate()
  const list = [
    driverRequests.day(token, today), driverRequests.trips(token, 'today', { date: today, sort: 'oldest' }, 1), driverRequests.activeTrip(token),
    driverRequests.bus(token), driverRequests.upcoming(token, { range: 'week' }), driverRequests.workingStatus(token), driverRequests.routes(token),
    driverRequests.stats(token), driverRequests.history(token, { search: '', from: '', to: '', status: '', page: 1 }), driverRequests.nextTrip(token),
    driverRequests.dashboard(token),
  ]
  for (const request of list) {
    if (cached(token, request.key) !== undefined) continue
    try { await fetchInto(token, request) } catch { /* the page loads it itself when opened */ }
  }
}
