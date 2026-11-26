import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { LiveBusTrackingPage } from './LiveBusTrackingPage'
import { LiveLocationCard } from '../components/driver/ops/LiveLocationCard'
import { freshnessOf, liveShareState, metresBetween, startLiveShare, stopLiveShare } from '../../models/liveLocation'

vi.mock('../components/LiveBusMap', () => ({ default: ({ bus }) => <div data-testid="map">{bus ? `bus at ${bus.lat},${bus.lng} ${bus.freshness}` : 'no bus'}</div> }))

let routes, calls
const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))
let geo

beforeEach(() => {
  calls = []; routes = {}
  vi.stubGlobal('fetch', vi.fn((url, options = {}) => {
    const { pathname, search } = new URL(url)
    const method = options.method || 'GET'
    const body = options.body && JSON.parse(options.body)
    calls.push({ method, path: pathname, search, body })
    const handler = routes[`${method} ${pathname}`]
    return handler ? handler({ body, search }) : reply(404, { success: false, message: 'Not found.' })
  }))
  // A controllable GPS.
  geo = { watchers: [], getCurrentPosition: vi.fn(), watchPosition: vi.fn((ok, fail) => { geo.watchers.push({ ok, fail }); return geo.watchers.length }), clearWatch: vi.fn() }
  Object.defineProperty(navigator, 'geolocation', { value: geo, configurable: true })
})
afterEach(async () => { await stopLiveShare(); cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks(); vi.useRealTimers() })

const position = (lat, lng, accuracy = 10) => ({ coords: { latitude: lat, longitude: lng, accuracy, speed: 5, heading: 90 }, timestamp: Date.now() })

describe('live location helpers', () => {
  it('uses the configured freshness thresholds', () => {
    expect(freshnessOf(10)).toBe('live')
    expect(freshnessOf(40)).toBe('delayed')
    expect(freshnessOf(90)).toBe('unavailable')
    expect(freshnessOf(null)).toBe('unavailable')
    expect(freshnessOf(40, { liveSeconds: 45, delayedSeconds: 90 })).toBe('live')
    expect(Math.round(metresBetween({ lat: 23.75, lng: 90.39 }, { lat: 23.751, lng: 90.39 }))).toBe(111)
  })
})

describe('driver GPS sharing', () => {
  it('asks for permission, sends the fix, converts speed and throttles repeats', async () => {
    routes['POST /api/live-location/start'] = () => reply(200, { success: true, data: {} })
    routes['POST /api/live-location/update'] = () => reply(200, { success: true, lastUpdatedAt: new Date().toISOString() })
    routes['POST /api/live-location/stop'] = () => reply(200, { success: true, data: {} })
    geo.getCurrentPosition.mockImplementation((ok) => ok(position(23.7578, 90.3897)))
    expect(await startLiveShare('tok')).toBe(true)
    expect(liveShareState().status).toBe('sharing')
    await waitFor(() => expect(calls.filter((c) => c.path === '/api/live-location/update')).toHaveLength(1))
    const sent = calls.find((c) => c.path === '/api/live-location/update').body
    expect(sent).toMatchObject({ latitude: 23.7578, longitude: 90.3897, accuracy: 10, speed: 18, heading: 90 }) // 5 m/s = 18 km/h
    // A second fix a moment later, barely moved: not sent yet.
    geo.watchers[0].ok(position(23.75781, 90.38971))
    await new Promise((r) => setTimeout(r, 20))
    expect(calls.filter((c) => c.path === '/api/live-location/update')).toHaveLength(1)
    await stopLiveShare()
    expect(geo.clearWatch).toHaveBeenCalled()
    expect(calls.some((c) => c.path === '/api/live-location/stop')).toBe(true)
    expect(liveShareState().status).toBe('idle')
  })

  it('stops and explains when permission is denied', async () => {
    routes['POST /api/live-location/start'] = () => reply(200, { success: true, data: {} })
    routes['POST /api/live-location/stop'] = () => reply(200, { success: true, data: {} })
    geo.getCurrentPosition.mockImplementation((ok, fail) => fail({ code: 1 }))
    expect(await startLiveShare('tok')).toBe(false)
    expect(liveShareState().status).toBe('idle')
    expect(liveShareState().error).toMatch(/permission was denied/)
    expect(calls.some((c) => c.path === '/api/live-location/update')).toBe(false)
  })

  it('does not ask for GPS when the server refuses (no trip in progress)', async () => {
    routes['POST /api/live-location/start'] = () => reply(409, { success: false, message: 'Start a trip first: live location is shared only while a trip is in progress.' })
    expect(await startLiveShare('tok')).toBe(false)
    expect(geo.getCurrentPosition).not.toHaveBeenCalled()
    expect(liveShareState().error).toMatch(/Start a trip first/)
  })

  it('stops watching when the server says the trip ended', async () => {
    routes['POST /api/live-location/start'] = () => reply(200, { success: true, data: {} })
    routes['POST /api/live-location/update'] = () => reply(409, { success: false, message: 'Your trip has ended, so live location sharing was stopped.' })
    geo.getCurrentPosition.mockImplementation((ok) => ok(position(23.7578, 90.3897)))
    await startLiveShare('tok')
    await waitFor(() => expect(liveShareState().status).toBe('idle'))
    expect(liveShareState().error).toMatch(/trip has ended/)
    expect(geo.clearWatch).toHaveBeenCalled()
  })

  it('shows the bus, the trip and the buttons on the dashboard card', async () => {
    const user = userEvent.setup()
    routes['GET /api/live-location/me'] = () => reply(200, { success: true, data: { canShare: true, isSharing: false, freshness: 'unavailable',
      bus: { busName: 'Raida', routeNumber: 'R-11', registrationNumber: 'R-11-02' }, trip: { tripNumber: 1, status: 'IN_PROGRESS', from: 'Jatrabari', to: 'Tongi' } } })
    routes['POST /api/live-location/start'] = () => reply(200, { success: true, data: {} })
    routes['POST /api/live-location/update'] = () => reply(200, { success: true, lastUpdatedAt: new Date().toISOString() })
    geo.getCurrentPosition.mockImplementation((ok) => ok(position(23.71, 90.43)))
    render(<LiveLocationCard token="tok" notify={() => {}} />)
    expect(await screen.findByText('R-11-02')).toBeTruthy()
    expect(screen.getByText('Jatrabari → Tongi')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: /Start Live Location/ }))
    expect(await screen.findByRole('button', { name: /Stop Live Location/ })).toBeTruthy()
    expect(screen.getByText('Sharing live')).toBeTruthy()
    expect(screen.getByText('23.71000, 90.43000')).toBeTruthy()
  })

  it('cannot start without a trip in progress', async () => {
    routes['GET /api/live-location/me'] = () => reply(200, { success: true, data: { canShare: false, cannotShareReason: 'Start a trip first: live location is shared only while a trip is in progress.', isSharing: false, freshness: 'unavailable', bus: { busName: 'Raida' } } })
    render(<LiveLocationCard token="tok" notify={() => {}} />)
    expect((await screen.findByRole('button', { name: /Start Live Location/ })).disabled).toBe(true)
    expect(screen.getByText(/Start a trip first/)).toBeTruthy()
  })
})

const liveBus = (over = {}) => ({ sessionId: 's1', busId: 'b1', vehicleId: 'v1', busName: 'Raida', routeNumber: 'R-11', registrationNumber: 'R-11-02', busType: 'general',
  startStop: 'Jatrabari', endStop: 'Tongi', isSharing: true, latitude: 23.75, longitude: 90.39, speed: 20, accuracy: 12, lastUpdatedAt: new Date().toISOString(), ...over })

describe('commuter live tracking', () => {
  it('lists live buses, opens one and shows its progress', async () => {
    const user = userEvent.setup()
    window.history.replaceState({}, '', '/live-tracking')
    routes['GET /api/live-location/active'] = () => reply(200, { success: true, serverTime: new Date().toISOString(), settings: { liveSeconds: 15, delayedSeconds: 60 }, data: [liveBus()] })
    routes['GET /bus/all'] = () => reply(200, { success: true, data: [] })
    routes['GET /api/live-location/bus/v1/progress'] = () => reply(200, { success: true, serverTime: new Date().toISOString(), data: {
      bus: liveBus(), route: { id: 'b1', start: 'Jatrabari', destination: 'Tongi', direction: 'FORWARD', stops: [{ name: 'Jatrabari', lat: 23.71, lng: 90.43 }, { name: 'Tongi', lat: 23.89, lng: 90.4 }] },
      progress: { estimated: true, percent: 42.5, remainingKm: 9.8, travelledKm: 7, remainingStopCount: 3, onRoute: true, currentStop: null,
        lastPassedStop: { name: 'Farmgate', order: 2, kmFromStart: 6 }, nextStop: { name: 'Mohakhali', order: 3, kmFromStart: 9 }, remainingStops: [{ name: 'Mohakhali', order: 3, kmFromStart: 9 }] } } })
    render(<LiveBusTrackingPage token="tok" signedIn openAuth={() => {}} />)
    await user.click(await screen.findByRole('button', { name: /Raida/ }))
    expect(await screen.findByText('Between Farmgate and Mohakhali')).toBeTruthy()
    expect(screen.getAllByText('Mohakhali').length).toBeGreaterThan(0)
    expect(screen.getByText('42.5%')).toBeTruthy()
    expect(screen.getByTestId('map').textContent).toBe('bus at 23.75,90.39 live')
    expect(window.location.search).toContain('bus=v1')
  })

  it('warns that an old location is only the last known one', async () => {
    window.history.replaceState({}, '', '/live-tracking?bus=v1&session=s1')
    const old = new Date(Date.now() - 120000).toISOString()
    routes['GET /api/live-location/active'] = () => reply(200, { success: true, serverTime: new Date().toISOString(), data: [] })
    routes['GET /bus/all'] = () => reply(200, { success: true, data: [] })
    routes['GET /api/live-location/bus/v1/progress'] = () => reply(200, { success: true, serverTime: new Date().toISOString(), data: {
      bus: liveBus({ lastUpdatedAt: old }), route: { id: 'b1', start: 'Jatrabari', destination: 'Tongi', stops: [] }, progress: null } })
    render(<LiveBusTrackingPage token="tok" signedIn openAuth={() => {}} />)
    expect(await screen.findByText(/This is the last known location/)).toBeTruthy()
    expect(screen.getAllByText('Location unavailable').length).toBeGreaterThan(0)
  })

  it('asks visitors to sign in', () => {
    render(<LiveBusTrackingPage token={null} signedIn={false} openAuth={() => {}} />)
    expect(screen.getByText(/Sign in to follow Dhaka buses live/)).toBeTruthy()
    expect(calls).toHaveLength(0)
  })
})
