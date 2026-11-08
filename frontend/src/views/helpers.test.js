import { describe, expect, it } from 'vitest'
import { MAX_MAP_WAYPOINTS, googleMapsPlace, googleMapsRouteDirections } from './helpers'

// AL-2 as stored in the database, in route order.
const AL2 = [
  ['Mirpur 1', 23.7957, 90.3537], ['Technical', 23.7808, 90.353], ['Kalyanpur', 23.782, 90.361], ['Shyamoli', 23.7743, 90.3653],
  ['Asad Gate', 23.76, 90.373], ['Dhanmondi 27', 23.7567, 90.374], ['Science Lab', 23.7389, 90.3832], ['New Market', 23.733, 90.3844], ['Azimpur', 23.728, 90.386],
].map(([name, lat, lng], order) => ({ name, lat, lng, order }))

const params = (url) => new URL(url).searchParams

describe('googleMapsRouteDirections', () => {
  it('goes from the first stop to the last through every stop in between', () => {
    const url = googleMapsRouteDirections(AL2)
    expect(url.startsWith('https://www.google.com/maps/dir/?')).toBe(true)
    const p = params(url)
    expect(p.get('api')).toBe('1')
    expect(p.get('travelmode')).toBe('driving')
    expect(p.get('origin')).toBe('23.795700,90.353700')
    expect(p.get('destination')).toBe('23.728000,90.386000')
    expect(p.get('waypoints').split('|')).toEqual(AL2.slice(1, -1).map((s) => `${s.lat.toFixed(6)},${s.lng.toFixed(6)}`))
  })

  it('keeps long routes within the Google Maps waypoint limit, in order, ending at the real terminus', () => {
    const stops = Array.from({ length: 40 }, (_, i) => ({ name: `Stop ${i}`, lat: 23.7 + i / 1000, lng: 90.4, order: i }))
    const p = params(googleMapsRouteDirections(stops))
    const waypoints = p.get('waypoints').split('|')
    expect(waypoints).toHaveLength(MAX_MAP_WAYPOINTS)
    const lats = waypoints.map((w) => Number(w.split(',')[0]))
    expect([...lats].sort((a, b) => a - b)).toEqual(lats)
    expect(p.get('origin')).toBe('23.700000,90.400000')
    expect(p.get('destination')).toBe('23.739000,90.400000')
  })

  it('falls back to the stop name when a stop has no coordinates', () => {
    const p = params(googleMapsRouteDirections([{ name: 'Gulistan', lat: 0, lng: 0 }, { name: 'Uttara', lat: null, lng: undefined }]))
    expect(p.get('origin')).toBe('Gulistan, Dhaka, Bangladesh')
    expect(p.get('destination')).toBe('Uttara, Dhaka, Bangladesh')
    expect(p.has('waypoints')).toBe(false)
  })

  it('gives no link when the route has fewer than two stops', () => {
    expect(googleMapsRouteDirections([AL2[0]])).toBeNull()
    expect(googleMapsRouteDirections(undefined)).toBeNull()
  })
})

describe('googleMapsPlace', () => {
  it('opens one stop at its coordinates', () => {
    expect(params(googleMapsPlace(AL2[3])).get('query')).toBe('23.774300,90.365300')
  })
})
