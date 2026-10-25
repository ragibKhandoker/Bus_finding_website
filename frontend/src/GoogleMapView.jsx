import React, { useEffect, useRef, useState } from 'react'
import { MapPinOff } from 'lucide-react'
import { getLang, t } from './i18n'

// With a browser key (VITE_GOOGLE_MAPS_API_KEY) this is the full Google Maps JavaScript API, with markers for
// every stop and the road line. Without one it falls back to Google's free embedded map, which needs no setup.
// Google Maps JavaScript API, loaded once on first use with the browser key. The browser key must be restricted
// to this site's addresses (HTTP referrers) in Google Cloud; the server key for distances stays on the backend.
const BROWSER_KEY = import.meta.env.VITE_GOOGLE_MAPS_API_KEY
// Advanced markers need a map ID; Google's DEMO_MAP_ID works for development, set your own for production.
const MAP_ID = import.meta.env.VITE_GOOGLE_MAPS_MAP_ID || 'DEMO_MAP_ID'
const DHAKA = { lat: 23.7808, lng: 90.4093 }

let loading = null
// Google calls gm_authFailure when the key is invalid or not allowed for this site, after the map has loaded.
let authFailed = false
const authListeners = new Set()

export const mapsConfigured = Boolean(BROWSER_KEY)

function loadGoogleMaps() {
  if (!BROWSER_KEY) return Promise.reject(new Error('missing-key'))
  if (window.google?.maps?.importLibrary) return Promise.resolve(window.google.maps)
  loading ??= new Promise((resolve, reject) => {
    window.gm_authFailure = () => { authFailed = true; authListeners.forEach((listener) => listener()) }
    window.__smartCommuteMapsReady = () => resolve(window.google.maps)
    const script = document.createElement('script')
    script.src = `https://maps.googleapis.com/maps/api/js?${new URLSearchParams({
      key: BROWSER_KEY, v: 'weekly', loading: 'async', callback: '__smartCommuteMapsReady', region: 'BD', language: getLang() === 'bn' ? 'bn' : 'en',
    })}`
    script.async = true
    script.onerror = () => { loading = null; script.remove(); reject(new Error('load-failed')) }
    document.head.appendChild(script)
  })
  return loading
}

const isDarkTheme = () => ['dark', 'night'].includes(document.documentElement.dataset.theme)

function pin(label, className) {
  const el = document.createElement('div')
  el.className = `gmap-pin ${className}`
  el.textContent = label
  return el
}

// Interactive map of a trip. All props are optional:
//   start, end    {name, lat, lng}: the searched places, shown as A and B pins
//   roadPath      encoded polyline of Google's suggested driving route (blue line)
//   busStops      the selected bus's stops in travel order (thin green line with dots)
//   segment       the stops actually ridden, boarding to getting off (thick green line, numbered)
// Google's driving route and the bus route are drawn differently on purpose: a bus does not follow Google's road.
export default function GoogleMapView({ start, end, roadPath, busStops = [], segment = [], className = '' }) {
  const boxRef = useRef(null)
  const mapRef = useRef(null)
  const overlaysRef = useRef([])
  const [status, setStatus] = useState(BROWSER_KEY ? (authFailed ? 'auth' : 'loading') : 'missing-key')

  // Create the map once.
  useEffect(() => {
    if (!BROWSER_KEY || authFailed) return
    let cancelled = false
    const onAuthFailure = () => setStatus('auth')
    authListeners.add(onAuthFailure)
    loadGoogleMaps()
      .then(async (maps) => {
        const { Map } = await maps.importLibrary('maps')
        await Promise.all([maps.importLibrary('marker'), maps.importLibrary('geometry')])
        if (cancelled || !boxRef.current) return
        mapRef.current = new Map(boxRef.current, {
          center: DHAKA, zoom: 12, mapId: MAP_ID, gestureHandling: 'cooperative', streetViewControl: false, mapTypeControl: false, fullscreenControl: true,
          colorScheme: isDarkTheme() ? 'DARK' : 'LIGHT',
        })
        setStatus('ready')
      })
      .catch(() => { if (!cancelled) setStatus('failed') })
    return () => { cancelled = true; authListeners.delete(onAuthFailure) }
  }, [])

  // Redraw markers and lines whenever the trip or the selected bus changes.
  useEffect(() => {
    const map = mapRef.current
    if (status !== 'ready' || !map) return
    const { maps } = window.google
    overlaysRef.current.forEach((overlay) => overlay.setMap ? overlay.setMap(null) : (overlay.map = null))
    overlaysRef.current = []
    const add = (overlay) => { overlaysRef.current.push(overlay); return overlay }
    const bounds = new maps.LatLngBounds()
    const point = (stop) => ({ lat: Number(stop.lat), lng: Number(stop.lng) })
    const hasPoint = (stop) => stop && Number.isFinite(Number(stop.lat)) && Number.isFinite(Number(stop.lng))

    if (roadPath) {
      const path = maps.geometry.encoding.decodePath(roadPath)
      add(new maps.Polyline({ map, path, strokeColor: '#2f6fde', strokeOpacity: 0.85, strokeWeight: 5, zIndex: 1 }))
      path.forEach((p) => bounds.extend(p))
    }

    const fullRoute = busStops.filter(hasPoint)
    if (fullRoute.length > 1) {
      add(new maps.Polyline({ map, path: fullRoute.map(point), strokeColor: '#19865e', strokeOpacity: 0.45, strokeWeight: 3, zIndex: 2 }))
      fullRoute.forEach((stop) => add(new maps.marker.AdvancedMarkerElement({ map, position: point(stop), title: t(stop.name), content: pin('', 'gmap-stop'), zIndex: 3 })))
      if (!segment.length) fullRoute.forEach((stop) => bounds.extend(point(stop)))
    }

    const ridden = segment.filter(hasPoint)
    if (ridden.length > 1) {
      add(new maps.Polyline({ map, path: ridden.map(point), strokeColor: '#19865e', strokeOpacity: 1, strokeWeight: 6, zIndex: 4 }))
      ridden.forEach((stop, index) => {
        add(new maps.marker.AdvancedMarkerElement({ map, position: point(stop), title: t(stop.name), content: pin(String(index + 1), 'gmap-ride'), zIndex: 5 }))
        bounds.extend(point(stop))
      })
    }

    if (hasPoint(start)) { add(new maps.marker.AdvancedMarkerElement({ map, position: point(start), title: t(start.name), content: pin('A', 'gmap-start'), zIndex: 10 })); bounds.extend(point(start)) }
    if (hasPoint(end)) { add(new maps.marker.AdvancedMarkerElement({ map, position: point(end), title: t(end.name), content: pin('B', 'gmap-end'), zIndex: 10 })); bounds.extend(point(end)) }

    if (!bounds.isEmpty()) map.fitBounds(bounds, 48)
  }, [status, start, end, roadPath, busStops, segment])

  // No key, or the key did not work: show Google's free embedded map instead, which needs no key.
  if (['missing-key', 'auth', 'failed'].includes(status)) {
    const embed = embedUrl({ start, end, busStops, segment })
    return <div className={`gmap ${className}`}>
      {embed
        ? <iframe className="gmap-canvas gmap-embed" src={embed} title={t('Google Maps')} loading="lazy" referrerPolicy="no-referrer-when-downgrade" allowFullScreen />
        : <div className="gmap-overlay gmap-problem"><MapPinOff size={22} /><span>{t('Search a route to see it on the map.')}</span></div>}
    </div>
  }
  return <div className={`gmap ${className}`}>
    <div ref={boxRef} className="gmap-canvas" />
    {status === 'loading' && <div className="gmap-overlay"><span className="spinner" /> {t('Loading map…')}</div>}
  </div>
}

// Google's embeddable map link, which works without an API key: directions from the start to the destination,
// or from the first to the last stop of the selected bus when there is no searched trip.
function embedUrl({ start, end, busStops, segment }) {
  const ends = start && end ? [start, end]
    : segment.length > 1 ? [segment[0], segment[segment.length - 1]]
    : busStops.length > 1 ? [busStops[0], busStops[busStops.length - 1]] : null
  if (!ends) return null
  const at = (stop) => `${Number(stop.lat)},${Number(stop.lng)}`
  return `https://maps.google.com/maps?${new URLSearchParams({ saddr: at(ends[0]), daddr: at(ends[1]), hl: getLang() === 'bn' ? 'bn' : 'en', output: 'embed' })}`
}
