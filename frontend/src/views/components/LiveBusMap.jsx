import React, { useEffect, useRef } from 'react'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import { t } from '../../models/i18n'
import { cooperativeTouchHint, fitPadding, isTouchScreen, watchLeafletResize } from './mapResponsive'

// F23: the live tracking map (Leaflet + OpenStreetMap, like the project's other maps).
//   stops     the selected bus's route in travel order [{ name, lat, lng }]
//   bus       the selected bus's position { lat, lng, heading, freshness } or null
//   others    every other live bus, for picking one on the map [{ id, lat, lng, freshness, label }]
//   follow    keep the selected bus in view as it moves
//   recenter  change this number to centre on the bus now
// Only the markers and the line change when data arrives; the map itself is created once.
const TILES = 'https://tile.openstreetmap.org/{z}/{x}/{y}.png'
const ATTRIBUTION = '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noreferrer">OpenStreetMap</a> contributors'
const DHAKA = [23.7808, 90.4093]
const BUS_SVG = '<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M8 6v6"/><path d="M15 6v6"/><path d="M2 12h19.6"/><path d="M18 18h3s.5-1.7.8-2.8c.1-.4.2-.8.2-1.2 0-.4-.1-.8-.2-1.2l-1.4-5C20.1 6.8 19.1 6 18 6H4a2 2 0 0 0-2 2v10h3"/><circle cx="7" cy="18" r="2"/><path d="M9 18h5"/><circle cx="16" cy="18" r="2"/></svg>'

const busIcon = (freshness, heading, small = false) => L.divIcon({
  className: '',
  html: `<div class="lb-marker is-${freshness}${small ? ' small' : ''}">${heading != null && !small ? `<i class="lb-heading" style="transform:rotate(${heading}deg)"></i>` : ''}<span>${BUS_SVG}</span></div>`,
  iconSize: small ? [30, 30] : [44, 44], iconAnchor: small ? [15, 15] : [22, 22],
})
const endPin = (label, className) => L.divIcon({ className: '', html: `<div class="gmap-pin ${className}">${label}</div>`, iconSize: [28, 28], iconAnchor: [14, 14] })
const ok = (p) => p && Number.isFinite(p.lat) && Number.isFinite(p.lng)

export default function LiveBusMap({ stops = [], bus = null, others = [], onPick, follow = true, recenter = 0 }) {
  const box = useRef(null)
  const mapRef = useRef(null)
  const routeLayer = useRef(null)
  const othersLayer = useRef(null)
  const marker = useRef(null)
  const anim = useRef(0)
  const fitted = useRef('')
  const pickRef = useRef(onPick)
  pickRef.current = onPick

  useEffect(() => {
    const touch = isTouchScreen()
    const map = L.map(box.current, { center: DHAKA, zoom: 12, scrollWheelZoom: true, dragging: !touch, tap: !touch, zoomControl: true })
    L.tileLayer(TILES, { maxZoom: 19, attribution: ATTRIBUTION }).addTo(map)
    routeLayer.current = L.layerGroup().addTo(map)
    othersLayer.current = L.layerGroup().addTo(map)
    mapRef.current = map
    const resize = setTimeout(() => map.invalidateSize(), 200)
    const stopWatching = watchLeafletResize(box.current, map)
    const stopHint = touch ? cooperativeTouchHint(box.current) : () => {}
    return () => { clearTimeout(resize); stopWatching(); stopHint(); cancelAnimationFrame(anim.current); map.remove(); mapRef.current = null; marker.current = null }
  }, [])

  // The route: a line through the stops, small dots for stops, A and B at the ends. Fitted once per route.
  useEffect(() => {
    const map = mapRef.current
    const layer = routeLayer.current
    if (!map || !layer) return
    layer.clearLayers()
    const points = stops.filter(ok).map((s) => [s.lat, s.lng])
    if (points.length < 2) return
    L.polyline(points, { color: '#19865e', weight: 5, opacity: 0.75 }).addTo(layer)
    stops.filter(ok).forEach((s, i, all) => {
      if (i === 0) L.marker([s.lat, s.lng], { icon: endPin('A', 'gmap-start'), keyboard: false }).bindTooltip(t(s.name)).addTo(layer)
      else if (i === all.length - 1) L.marker([s.lat, s.lng], { icon: endPin('B', 'gmap-end'), keyboard: false }).bindTooltip(t(s.name)).addTo(layer)
      else L.circleMarker([s.lat, s.lng], { radius: 4.5, color: '#ffffff', weight: 2, fillColor: '#19865e', fillOpacity: 1 }).bindTooltip(t(s.name)).addTo(layer)
    })
    const key = points.map((p) => p.join(',')).join('|')
    if (fitted.current !== key) {
      fitted.current = key
      const bounds = L.latLngBounds(points)
      if (ok(bus)) bounds.extend([bus.lat, bus.lng])
      const pad = fitPadding(box.current)
      map.fitBounds(bounds, { padding: [pad, pad] })
    }
  }, [stops]) // eslint-disable-line react-hooks/exhaustive-deps

  // The selected bus: glide from the old position to the new one instead of jumping.
  useEffect(() => {
    const map = mapRef.current
    if (!map) return
    if (!ok(bus)) {
      if (marker.current) { marker.current.remove(); marker.current = null }
      return
    }
    const target = L.latLng(bus.lat, bus.lng)
    if (!marker.current) {
      marker.current = L.marker(target, { icon: busIcon(bus.freshness, bus.heading), zIndexOffset: 1000, keyboard: false }).addTo(map)
      if (!stops.length) map.setView(target, 15)
    } else {
      marker.current.setIcon(busIcon(bus.freshness, bus.heading))
      const from = marker.current.getLatLng()
      const startAt = performance.now()
      cancelAnimationFrame(anim.current)
      const step = (time) => {
        const k = Math.min(1, (time - startAt) / 900)
        const e = 1 - (1 - k) ** 3
        marker.current?.setLatLng([from.lat + (target.lat - from.lat) * e, from.lng + (target.lng - from.lng) * e])
        if (k < 1) anim.current = requestAnimationFrame(step)
      }
      anim.current = requestAnimationFrame(step)
    }
    if (follow && !map.getBounds().pad(-0.15).contains(target)) map.panTo(target, { animate: true })
  }, [bus?.lat, bus?.lng, bus?.freshness, bus?.heading, follow]) // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    if (recenter && mapRef.current && ok(bus)) mapRef.current.setView([bus.lat, bus.lng], Math.max(mapRef.current.getZoom(), 15), { animate: true })
  }, [recenter]) // eslint-disable-line react-hooks/exhaustive-deps

  // Every other live bus, small; tapping one selects it.
  useEffect(() => {
    const layer = othersLayer.current
    if (!layer) return
    layer.clearLayers()
    others.filter(ok).forEach((o) => {
      L.marker([o.lat, o.lng], { icon: busIcon(o.freshness, null, true), keyboard: false, title: o.label })
        .bindTooltip(o.label).on('click', () => pickRef.current?.(o)).addTo(layer)
    })
  }, [others])

  return <div ref={box} className="lb-map" role="region" aria-label={t('Live bus map')} />
}
