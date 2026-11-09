import { describe, expect, it } from 'vitest'
import { AT_STOP_KM, distanceKm, rideStops, tripProgress } from './liveTrip'

// A straight route north, stops about 1.1 km apart.
const route = ['Jatrabari', 'Malibagh', 'Rampura', 'Badda', 'Natun Bazar'].map((name, order) => ({ name, order, lat: 23.70 + order * 0.01, lng: 90.40 }))
const at = (lat) => ({ lat, lng: 90.40 })

describe('rideStops', () => {
  it('runs from where the rider got on to where they get off, in travel order', () => {
    expect(rideStops(route, 'Malibagh', 'Badda').map((s) => s.name)).toEqual(['Malibagh', 'Rampura', 'Badda'])
    expect(rideStops(route, 'Badda', 'Jatrabari').map((s) => s.name)).toEqual(['Badda', 'Rampura', 'Malibagh', 'Jatrabari'])
  })

  it('heads for the far end when no destination is chosen yet', () => {
    expect(rideStops(route, 'Rampura', '').map((s) => s.name)).toEqual(['Rampura', 'Badda', 'Natun Bazar'])
    expect(rideStops(route, 'Natun Bazar', '').map((s) => s.name)).toEqual(['Natun Bazar', 'Badda', 'Rampura', 'Malibagh', 'Jatrabari'])
  })

  it('gives nothing when the boarding stop is not on the route', () => {
    expect(rideStops(route, 'Gulistan', 'Badda')).toEqual([])
  })
})

describe('tripProgress', () => {
  const ride = rideStops(route, 'Malibagh', 'Badda')

  it('names the next stop and how far it is to the rider’s stop', () => {
    const p = tripProgress(ride, at(23.715)) // between Malibagh and Rampura
    expect(p.next.name).toBe('Rampura')
    expect(p.toNextKm).toBeCloseTo(distanceKm(at(23.715), at(23.72)), 5)
    expect(p.toEndKm).toBeCloseTo(distanceKm(at(23.715), at(23.73)), 1)
    expect(p.stopsLeft).toBe(2)
    expect(p.passed).toBe(0)
    expect(p.arrived).toBe(false)
  })

  it('moves on to the following stop once at a stop', () => {
    const p = tripProgress(ride, at(23.7201)) // at Rampura
    expect(p.next.name).toBe('Badda')
    expect(p.stopsLeft).toBe(1)
    expect(p.passed).toBe(1)
  })

  it('says when the rider has reached their stop', () => {
    const p = tripProgress(ride, at(23.73 + AT_STOP_KM / 300))
    expect(p.arrived).toBe(true)
    expect(p.stopsLeft).toBe(0)
  })

  it('says when the rider is far from the route', () => {
    expect(tripProgress(ride, { lat: 23.9, lng: 90.6 }).offRoute).toBe(true)
  })

  it('has nothing to say without a position or a ride', () => {
    expect(tripProgress(ride, null)).toBeNull()
    expect(tripProgress([], at(23.71))).toBeNull()
  })
})
