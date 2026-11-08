import React, { useEffect, useRef } from 'react'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import { t } from '../../models/i18n'

// Leaflet map with OpenStreetMap tiles for the fare calculator. Needs no API key. OpenStreetMap's tile policy
// requires the attribution shown in the corner; the tiles are for light, interactive use only.
//   start, end   {name, lat, lon}: the chosen places, shown as A and B pins
//   path         the road line as [lat, lon] points (empty when the distance is a straight-line estimate)
//   me           {latitude, longitude}: the rider's real location (from SOS), shown as a pulsing red dot
const DHAKA = [23.7808, 90.4093]
const TILES = 'https://tile.openstreetmap.org/{z}/{x}/{y}.png'
const ATTRIBUTION = '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noreferrer">OpenStreetMap</a> contributors'

// Same pins as the Google map (see .gmap-pin in home.css), so both maps look alike.
const pin = (label, className) => L.divIcon({ className: '', html: `<div class="gmap-pin ${className}">${label}</div>`, iconSize: [28, 28], iconAnchor: [14, 14] })
const hasPoint = (p) => p && Number.isFinite(Number(p.lat)) && Number.isFinite(Number(p.lon))

const meIcon = L.divIcon({ className: '', html: '<div class="map-me"><i></i></div>', iconSize: [22, 22], iconAnchor: [11, 11] })

export default function FareMap({ start, end, path = [], me = null }) {
  const boxRef = useRef(null)
  const mapRef = useRef(null)
  const layerRef = useRef(null)

  // Create the map once.
  useEffect(() => {
    const map = L.map(boxRef.current, { center: DHAKA, zoom: 12, scrollWheelZoom: false })
    L.tileLayer(TILES, { maxZoom: 19, attribution: ATTRIBUTION }).addTo(map)
    layerRef.current = L.layerGroup().addTo(map)
    mapRef.current = map
    // The map is created before the page finishes laying out; measure again once it has.
    const resize = setTimeout(() => map.invalidateSize(), 200)
    return () => { clearTimeout(resize); map.remove(); mapRef.current = null }
  }, [])

  // Redraw the pins and the road line whenever they change.
  useEffect(() => {
    const map = mapRef.current
    const layer = layerRef.current
    if (!map || !layer) return
    layer.clearLayers()
    const bounds = L.latLngBounds([])
    if (path.length > 1) {
      const line = L.polyline(path, { color: '#2f6fde', weight: 5, opacity: 0.85 }).addTo(layer)
      bounds.extend(line.getBounds())
    }
    for (const [place, label, className] of [[start, 'A', 'gmap-start'], [end, 'B', 'gmap-end']]) {
      if (!hasPoint(place)) continue
      const at = [Number(place.lat), Number(place.lon)]
      L.marker(at, { icon: pin(label, className), title: t(place.name), keyboard: false }).bindTooltip(t(place.name)).addTo(layer)
      bounds.extend(at)
    }
    const here = me && Number.isFinite(me.latitude) && Number.isFinite(me.longitude) ? [me.latitude, me.longitude] : null
    if (here) {
      L.marker(here, { icon: meIcon, title: t('Your current location'), keyboard: false, zIndexOffset: 1000 })
        .bindTooltip(t('Your current location'), { permanent: true, direction: 'top', offset: [0, -12], className: 'map-me-label' }).addTo(layer)
    }
    // With only the rider's location, zoom in on it; otherwise fit everything shown.
    if (here && !bounds.isValid()) map.setView(here, 16)
    else {
      if (here) bounds.extend(here)
      if (bounds.isValid()) map.fitBounds(bounds, { padding: [40, 40], maxZoom: 15 })
    }
  }, [start, end, path, me])

  return <div ref={boxRef} className="fare-map" role="region" aria-label={t('Map of the selected places')} />
}
