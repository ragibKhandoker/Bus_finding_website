import { useCallback, useEffect, useState } from 'react'
import { apiRequest as request } from '../models/api'
import { errorText } from '../models/messages'
import { TRIP_CHANGED_EVENT } from '../models/trips'

// Runs load now and again whenever a trip is boarded or ended anywhere in the app.
export function useTripChanges(load) {
  useEffect(() => {
    load()
    window.addEventListener(TRIP_CHANGED_EVENT, load)
    return () => window.removeEventListener(TRIP_CHANGED_EVENT, load)
  }, [load])
}

// The rider's active trip. status: 'idle' (signed out), 'loading', 'ready' (trip may be null) or 'error'.
export function useActiveTrip(token, signedIn) {
  const [state, setState] = useState({ status: signedIn ? 'loading' : 'idle', trip: null, route: null })
  const load = useCallback(() => {
    if (!signedIn) { setState({ status: 'idle', trip: null, route: null }); return }
    request('/trip/active', { token })
      .then((data) => setState({ status: 'ready', trip: data.data || null, route: data.route || null }))
      .catch((e) => setState({ status: 'error', trip: null, route: null, message: errorText(e) }))
  }, [token, signedIn])
  useTripChanges(load)
  return state
}
