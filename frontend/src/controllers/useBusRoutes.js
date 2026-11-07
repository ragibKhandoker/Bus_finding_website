import { useCallback, useEffect, useState } from 'react'
import { fetchBusRoutes, routesCache, routesFresh } from '../models/busRoutes'

// Every route with its stops, from the database. status: 'loading', 'ready' or 'error'.
export function useBusRoutes() {
  const [routes, setRoutes] = useState(() => routesCache ? { status: 'ready', buses: routesCache } : { status: 'loading' })
  const load = useCallback(() => {
    if (routesFresh) { setRoutes({ status: 'ready', buses: routesCache }); return }
    if (!routesCache) setRoutes({ status: 'loading' })
    fetchBusRoutes()
      .then((buses) => setRoutes({ status: 'ready', buses }))
      .catch(() => { if (!routesCache) setRoutes({ status: 'error' }) })
  }, [])
  useEffect(() => { load() }, [load])
  return [routes, load]
}
