import { useEffect, useState } from 'react'

// The phone's location, followed live while `enabled` (navigator.geolocation.watchPosition). It stays on this
// device: nothing here sends it anywhere.
// status: 'idle' (off), 'asking' (waiting for the first fix or for permission), 'live', 'denied' or 'unavailable'.
// position: { lat, lng, accuracy (m), speed (m/s or null), time } once known.
export function useLivePosition(enabled, geo = typeof navigator !== 'undefined' ? navigator.geolocation : undefined) {
  const [state, setState] = useState({ status: 'idle', position: null })
  useEffect(() => {
    if (!enabled) { setState({ status: 'idle', position: null }); return }
    if (!geo?.watchPosition) { setState({ status: 'unavailable', position: null }); return }
    setState((s) => ({ ...s, status: s.position ? 'live' : 'asking' }))
    const id = geo.watchPosition(
      ({ coords, timestamp }) => setState({
        status: 'live',
        position: { lat: coords.latitude, lng: coords.longitude, accuracy: coords.accuracy ?? null, speed: Number.isFinite(coords.speed) ? coords.speed : null, time: timestamp || Date.now() },
      }),
      // Permission refused stops it; a lost signal keeps the last known position on screen.
      (error) => setState((s) => ({ ...s, status: error?.code === 1 ? 'denied' : s.position ? 'live' : 'unavailable' })),
      { enableHighAccuracy: true, maximumAge: 5000, timeout: 20000 },
    )
    return () => geo.clearWatch?.(id)
  }, [enabled, geo])
  return state
}
