import React, { useEffect, useRef } from 'react'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'

// A route's stops on the existing OpenStreetMap/Leaflet map (the same tiles as the live trip map): the line in
// green, the first stop in green and the last in red. Read-only: drivers cannot change official routes.
const TILES = 'https://tile.openstreetmap.org/{z}/{x}/{y}.png'
const ATTRIBUTION = '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noreferrer">OpenStreetMap</a> contributors'
const pin = (label, className) => L.divIcon({ className: '', html: `<div class="gmap-pin ${className}">${label}</div>`, iconSize: [28, 28], iconAnchor: [14, 14] })

export default function RouteMap({ stops }) {
  const box = useRef(null)
  useEffect(() => {
    const points = stops.filter((s) => Number.isFinite(s.lat) && Number.isFinite(s.lng) && (s.lat || s.lng)).map((s) => [s.lat, s.lng])
    if (!box.current || points.length < 2) return undefined
    const map = L.map(box.current, { scrollWheelZoom: false })
    L.tileLayer(TILES, { maxZoom: 19, attribution: ATTRIBUTION }).addTo(map)
    L.polyline(points, { color: '#19865e', weight: 5, opacity: 0.85 }).addTo(map)
    points.forEach((p, i) => {
      if (i === 0) L.marker(p, { icon: pin('A', 'gmap-start') }).addTo(map)
      else if (i === points.length - 1) L.marker(p, { icon: pin('B', 'gmap-end') }).addTo(map)
      else L.circleMarker(p, { radius: 5, color: '#ffffff', weight: 2, fillColor: '#19865e', fillOpacity: 1 }).bindTooltip(stops[i].name).addTo(map)
    })
    map.fitBounds(L.latLngBounds(points), { padding: [24, 24] })
    const resize = setTimeout(() => map.invalidateSize(), 200)
    return () => { clearTimeout(resize); map.remove() }
  }, [stops])
  return <div ref={box} className="do-map" aria-label="Route map" />
}
