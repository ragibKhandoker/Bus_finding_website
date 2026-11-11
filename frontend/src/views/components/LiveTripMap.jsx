import React, { useEffect, useRef } from 'react'
import L from 'leaflet'
import { watchLeafletResize } from './mapResponsive'
import 'leaflet/dist/leaflet.css'
import { t } from '../../models/i18n'

// The live map while riding: the whole route faintly, the ride (boarding to getting off) in green with its stops,
// and the rider's position as a moving blue dot with its accuracy circle. It follows the rider until they drag
// the map; onUserMove tells the panel so it can offer "Re-center".
//   route     the whole route's stops [{ name, lat, lng }] in order
//   ride      the stops of this ride in travel order (from rideStops)
//   position  { lat, lng, accuracy } or null
//   passed    index in ride of the last stop reached (those are drawn as done)
//   follow    keep the rider in the middle
const TILES = 'https://tile.openstreetmap.org/{z}/{x}/{y}.png'
const ATTRIBUTION = '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noreferrer">OpenStreetMap</a> contributors'
const pin = (label, className) => L.divIcon({ className: '', html: `<div class="gmap-pin ${className}">${label}</div>`, iconSize: [28, 28], iconAnchor: [14, 14] })
const meIcon = L.divIcon({ className: '', html: '<div class="live-me"><i></i></div>', iconSize: [24, 24], iconAnchor: [12, 12] })
const at = (s) => [s.lat, s.lng]

export default function LiveTripMap({ route = [], ride = [], position = null, passed = -1, follow = true, onUserMove }) {
  const boxRef = useRef(null)
  const mapRef = useRef(null)
  const routeLayer = useRef(null)
  const meLayer = useRef(null)
  const centred = useRef(false)
  const moveRef = useRef(onUserMove)
  moveRef.current = onUserMove

  useEffect(() => {
    const map = L.map(boxRef.current, { zoom: 14, center: ride[0] ? at(ride[0]) : [23.7808, 90.4093], scrollWheelZoom: false })
    L.tileLayer(TILES, { maxZoom: 19, attribution: ATTRIBUTION }).addTo(map)
    routeLayer.current = L.layerGroup().addTo(map)
    meLayer.current = L.layerGroup().addTo(map)
    // The rider moving the map themselves stops following.
    map.on('dragstart', () => moveRef.current?.())
    mapRef.current = map
    const resize = setTimeout(() => map.invalidateSize(), 200)
    const stopWatching = watchLeafletResize(boxRef.current, map)
    return () => { clearTimeout(resize); stopWatching(); map.remove(); mapRef.current = null }
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  // The route and the ride, redrawn when the trip or the progress changes.
  useEffect(() => {
    const map = mapRef.current, layer = routeLayer.current
    if (!map || !layer) return
    layer.clearLayers()
    if (route.length > 1) L.polyline(route.map(at), { color: '#94a3a0', weight: 4, opacity: 0.45, dashArray: '6 8' }).addTo(layer)
    if (ride.length > 1) {
      L.polyline(ride.map(at), { color: '#19865e', weight: 6, opacity: 0.85 }).addTo(layer)
      // The part already ridden, darker: from the first stop to the last one reached.
      if (passed >= 1) L.polyline(ride.slice(0, passed + 1).map(at), { color: '#0f5c3f', weight: 6, opacity: 0.95 }).addTo(layer)
    }
    ride.forEach((s, i) => {
      if (i === 0 || i === ride.length - 1) return
      L.circleMarker(at(s), { radius: 5, color: '#fff', weight: 2, fillColor: i <= passed ? '#0f5c3f' : '#19865e', fillOpacity: 1 })
        .bindTooltip(t(s.name), { direction: 'top', offset: [0, -4] }).addTo(layer)
    })
    if (ride[0]) L.marker(at(ride[0]), { icon: pin('A', 'gmap-start'), keyboard: false }).bindTooltip(t(ride[0].name)).addTo(layer)
    if (ride.length > 1) L.marker(at(ride[ride.length - 1]), { icon: pin('B', 'gmap-end'), keyboard: false }).bindTooltip(t(ride[ride.length - 1].name)).addTo(layer)
    // Before there is a position, show the whole ride.
    if (!position && ride.length > 1) map.fitBounds(L.latLngBounds(ride.map(at)), { padding: [30, 30] })
  }, [route, ride, passed]) // eslint-disable-line react-hooks/exhaustive-deps

  // The rider's dot, moved on every new position; the map follows it unless the rider has moved the map.
  useEffect(() => {
    const map = mapRef.current, layer = meLayer.current
    if (!map || !layer) return
    layer.clearLayers()
    if (!position) { centred.current = false; return }
    const here = [position.lat, position.lng]
    if (position.accuracy && position.accuracy < 1000) L.circle(here, { radius: position.accuracy, color: '#1a73e8', weight: 1, opacity: 0.4, fillOpacity: 0.08 }).addTo(layer)
    L.marker(here, { icon: meIcon, keyboard: false, zIndexOffset: 1000, title: t('You are here') }).addTo(layer)
    if (follow) {
      if (!centred.current) { map.setView(here, Math.max(map.getZoom(), 16)); centred.current = true }
      else map.panTo(here, { animate: true, duration: 0.8 })
    }
  }, [position, follow])

  return <div ref={boxRef} className="live-trip-map" role="region" aria-label={t('Live map of your ride')} />
}
