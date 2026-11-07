import { formatDate, formatNumber, t } from '../models/i18n'

export const money = (value) => `৳${formatNumber(value)}`

export const getStops = (bus) => Array.isArray(bus.stops) ? bus.stops.map((stop) => typeof stop === 'string' ? stop : stop.name).filter(Boolean) : []

export const coordinate = (value) => formatNumber(value, { minimumFractionDigits: 4, maximumFractionDigits: 4, useGrouping: false })

export const sortedStops = (bus) => [...(bus?.stops || [])].sort((a, b) => (a.order ?? 0) - (b.order ?? 0))

// Length along the stops in km, stop to stop in straight lines, so it is an estimate of the road distance.
export const routeLengthKm = (stops) => stops.slice(1).reduce((sum, stop, i) => {
  const prev = stops[i], rad = Math.PI / 180
  const a = Math.sin((stop.lat - prev.lat) * rad / 2) ** 2 + Math.cos(prev.lat * rad) * Math.cos(stop.lat * rad) * Math.sin((stop.lng - prev.lng) * rad / 2) ** 2
  return sum + 12742 * Math.asin(Math.sqrt(a))
}, 0)

export const BUS_TYPES = ['general', 'semi-seating', 'seating']

// "1 hr 5 min" / "25 min", in the current language.
export const formatDuration = (minutes) => {
  const total = Math.max(1, Math.round(minutes))
  const hours = Math.floor(total / 60), mins = total % 60
  return hours ? (mins ? t('{h} hr {m} min', { h: hours, m: mins }) : t('{h} hr', { h: hours })) : t('{m} min', { m: mins })
}

// Google Maps directions between two places, opened in a new tab (or the Google Maps app on phones).
export const googleMapsDirections = (from, to) => `https://www.google.com/maps/dir/?${new URLSearchParams({
  api: '1', origin: `${from}, Dhaka, Bangladesh`, destination: `${to}, Dhaka, Bangladesh`, travelmode: 'driving',
})}`

export const signedMoney = (value) => `${value > 0 ? '+' : value < 0 ? '−' : ''}৳${formatNumber(Math.abs(value), { maximumFractionDigits: 2 })}`

export const signedGems = (value) => `${value > 0 ? '+' : '−'}${formatNumber(Math.abs(value))}`

export const shortDate = (value) => formatDate(value) || '—'

export const greeting = () => { const hour = new Date().getHours(); return hour < 12 ? 'Good morning' : hour < 17 ? 'Good afternoon' : 'Good evening' }
