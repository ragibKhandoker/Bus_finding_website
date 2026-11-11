import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { DriverDashboardPage } from '../DriverDashboardPage'
import { DriverWorkingStatusPage } from './DriverWorkingStatusPage'
import { DriverTripHistoryPage } from './DriverTripHistoryPage'
import { AdminDriverOperationsPage, nextSlot } from '../AdminDriverOperationsPage'
import { minutes } from '../../../models/driverTrips'
import { forgetDriverCache } from '../../../models/driverRequests'
import { pageFromPath, paramFromPath } from '../../../controllers/router'

let routes, calls
const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))

beforeEach(() => {
  calls = []; routes = {}
  forgetDriverCache()
  localStorage.clear()
  vi.stubGlobal('fetch', vi.fn((url, options = {}) => {
    const { pathname, search } = new URL(url)
    const method = options.method || 'GET'
    const body = options.body && JSON.parse(options.body)
    calls.push({ method, path: pathname, search, body })
    const handler = routes[`${method} ${pathname}`]
    return handler ? handler({ body, search }) : reply(404, { success: false, message: 'Not found.' })
  }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

const driver = { _id: 'd1', name: 'Rahim Ahmed', role: 'driver' }
const approved = () => reply(200, { success: true, data: { registered: true, canAccessDashboard: true, verificationStatus: 'APPROVED' } })
const trip = (over = {}) => ({ id: 't1', code: 'SCH-1A2B3C4D', tripNumber: 1, scheduleDate: '2026-11-12', status: 'SCHEDULED', busName: 'Turag Paribahan', routeNumber: 'DBR-12',
  startStop: 'Mirpur 10', endStop: 'Motijheel', scheduledDeparture: '2026-11-12T02:00:00Z', scheduledArrival: '2026-11-12T03:00:00Z', plannedMinutes: 60,
  canStart: true, canComplete: false, assignmentActive: true, startOpensAt: '2026-11-12T01:30:00Z', vehicle: { code: 'DBR-12-01' }, ...over })
const dashboard = (over = {}) => ({
  driver: { name: 'Rahim Ahmed' }, today: '2026-11-12', workingStatus: { status: 'AVAILABLE', changedAt: '2026-11-12T01:00:00Z', allowedManual: [] }, activeTrip: null,
  assignments: [{ assignment: { id: 'a1', busId: 'b1', busName: 'Turag Paribahan', routeNumber: 'DBR-12', operator: 'Turag', vehicleCode: 'DBR-12-01', isActive: true },
    route: { id: 'b1', routeNumber: 'DBR-12', startStop: 'Mirpur 10', endStop: 'Motijheel', totalStops: 9, distanceKm: 12.4, busType: 'general' } }],
  schedule: { total: 2, completed: 1, remaining: 1, missed: 0, firstDeparture: '2026-11-12T02:00:00Z', lastDeparture: '2026-11-12T04:00:00Z', trips: [trip()] },
  completed: { total: 37, thisWeek: 9, thisMonth: 20, completionRate: 94.9 }, upcoming: { total: 3, trips: [trip()] },
  routes: [{ id: 'b1', routeNumber: 'DBR-12', startStop: 'Mirpur 10', endStop: 'Motijheel', totalStops: 9, distanceKm: 12.4 }], ...over,
})
const props = { token: 'tok', user: driver, signedIn: true, navigate: () => {}, notify: () => {}, signOut: () => {}, openAuth: () => {} }
const statusRoute = () => reply(200, { success: true, data: { status: 'AVAILABLE', allowedManual: ['AVAILABLE', 'ON_DUTY', 'BREAK', 'OFF_DUTY', 'UNAVAILABLE'], hasActiveTrip: false }, history: [] })

describe('driver console routes', () => {
  it('opens a trip from its own address', () => {
    expect(pageFromPath('/driver/trips/3f2a9c1b-1111-4222-8333-444455556666')).toBe('Driver trip')
    expect(paramFromPath('/driver/trips/3f2a9c1b-1111-4222-8333-444455556666')).toBe('3f2a9c1b-1111-4222-8333-444455556666')
    expect(pageFromPath('/driver/trip-history')).toBe('Driver trip history')
    expect(minutes(75)).toBe('1 h 15 min')
  })
})

describe('driver dashboard', () => {
  it('shows the six summary cards from the server', async () => {
    routes['GET /api/driver-registration/status'] = approved
    routes['GET /api/driver/working-status'] = statusRoute
    routes['GET /api/driver/dashboard'] = () => reply(200, { success: true, data: dashboard() })
    render(<DriverDashboardPage {...props} />)
    expect(await screen.findByText('Total Completed Trips')).toBeTruthy()
    for (const label of ['Assigned Bus', "Today's Schedule", 'Upcoming Trips', 'Assigned Routes', 'Working Status']) expect(screen.getAllByText(label).length).toBeGreaterThan(0)
    expect(screen.getByText('37')).toBeTruthy()
    expect(screen.getByText('Welcome back, Rahim!')).toBeTruthy()
    expect(screen.getAllByText('DBR-12-01').length).toBeGreaterThan(0)
  })

  it('opens again instantly from the cache, then refreshes in the background', async () => {
    routes['GET /api/driver-registration/status'] = approved
    routes['GET /api/driver/working-status'] = statusRoute
    routes['GET /api/driver/dashboard'] = () => reply(200, { success: true, data: dashboard() })
    const first = render(<DriverDashboardPage {...props} />)
    await screen.findByText('37')
    first.unmount()
    let release
    routes['GET /api/driver/dashboard'] = () => new Promise((resolve) => { release = () => resolve(reply(200, { success: true, data: dashboard({ completed: { total: 38, thisWeek: 9, thisMonth: 20 } }) })) })
    render(<DriverDashboardPage {...props} />)
    expect(screen.getByText('37')).toBeTruthy() // shown at once, no loader, while the server is still answering
    await waitFor(() => expect(release).toBeTypeOf('function'))
    release()
    expect(await screen.findByText('38')).toBeTruthy()
  })

  it('says so when no bus is assigned', async () => {
    routes['GET /api/driver-registration/status'] = approved
    routes['GET /api/driver/working-status'] = statusRoute
    routes['GET /api/driver/dashboard'] = () => reply(200, { success: true, data: dashboard({ assignments: [], routes: [] }) })
    render(<DriverDashboardPage {...props} />)
    expect((await screen.findAllByText('No bus has been assigned to your account yet.')).length).toBeGreaterThan(0)
  })

  it('asks before starting a trip, then starts it', async () => {
    const user = userEvent.setup()
    routes['GET /api/driver-registration/status'] = approved
    routes['GET /api/driver/working-status'] = statusRoute
    routes['GET /api/driver/dashboard'] = () => reply(200, { success: true, data: dashboard() })
    routes['POST /api/driver/trips/t1/start'] = () => reply(200, { success: true, message: 'Trip started. Drive safely!', data: trip({ status: 'IN_PROGRESS' }) })
    render(<DriverDashboardPage {...props} />)
    await user.click((await screen.findAllByRole('button', { name: /Start Trip/ }))[0])
    expect(calls.some((c) => c.path.endsWith('/start'))).toBe(false)
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: /Start Trip/ }))
    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === '/api/driver/trips/t1/start')).toBe(true))
  })

  it('shows the verification status to a driver who is not approved', async () => {
    routes['GET /api/driver-registration/status'] = () => reply(200, { success: true, data: { registered: true, canAccessDashboard: false, verificationStatus: 'PENDING', applicationCode: 'DRV-1', requestedFields: [], assignments: [], nextStep: 'Wait.' } })
    render(<DriverDashboardPage {...props} />)
    expect(await screen.findByText(/Our administration team is reviewing your documents/)).toBeTruthy()
    expect(calls.some((c) => c.path === '/api/driver/dashboard')).toBe(false)
  })
})

describe('working status', () => {
  it('never offers On Trip and saves a manual change', async () => {
    const user = userEvent.setup()
    routes['GET /api/driver-registration/status'] = approved
    routes['GET /api/driver/working-status'] = statusRoute
    routes['PUT /api/driver/working-status'] = ({ body }) => reply(200, { success: true, message: 'Working status updated.', data: { status: body.status, allowedManual: [] }, history: [] })
    render(<DriverWorkingStatusPage {...props} />)
    const onTrip = await screen.findByRole('radio', { name: /On Trip/ })
    expect(onTrip.disabled).toBe(true)
    await user.click(screen.getByRole('radio', { name: /Break/ }))
    await waitFor(() => expect(calls.find((c) => c.method === 'PUT')?.body.status).toBe('BREAK'))
  })
})

describe('trip history', () => {
  it('filters by status and lists the trips', async () => {
    const user = userEvent.setup()
    routes['GET /api/driver-registration/status'] = approved
    routes['GET /api/driver/working-status'] = statusRoute
    routes['GET /api/driver/trips/history'] = () => reply(200, { success: true, total: 1, totalPages: 1, data: [trip({ status: 'COMPLETED', canStart: false, actualDeparture: '2026-11-12T02:02:00Z', actualArrival: '2026-11-12T02:58:00Z', durationMinutes: 56 })] })
    render(<DriverTripHistoryPage {...props} />)
    expect(await screen.findByText('56 min')).toBeTruthy()
    await user.selectOptions(screen.getByLabelText('Status'), 'MISSED')
    await waitFor(() => expect(calls.some((c) => c.path === '/api/driver/trips/history' && c.search.includes('status=MISSED'))).toBe(true))
  })
})

describe('admin driver operations', () => {
  it('schedules a trip for the chosen driver', async () => {
    const user = userEvent.setup()
    routes['GET /api/admin/driver-operations'] = () => reply(200, { success: true, data: [{ driverId: 'd1', name: 'Rahim Ahmed', email: 'r@x.bd', phone: '01712345678', applicationCode: 'DRV-1',
      workingStatus: 'AVAILABLE', assignments: [{ id: 'a1', busId: 'b1', busName: 'Turag Paribahan', routeNumber: 'DBR-12', isActive: true, assignedAt: '2026-11-01T00:00:00Z' }],
      todayTotal: 0, todayCompleted: 0, missedLast30Days: 0, activeTrip: null, nextTrip: null }] })
    routes['GET /api/admin/driver-schedules'] = () => reply(200, { success: true, total: 0, totalPages: 1, data: [] })
    routes['GET /api/admin/buses/b1/vehicles'] = () => reply(200, { success: true, data: [] })
    routes['GET /bus/all'] = () => reply(200, { success: true, data: [] })
    routes['POST /api/admin/driver-schedules'] = ({ body }) => reply(201, { success: true, message: 'Trip scheduled. The driver was notified.', data: trip(body) })
    render(<AdminDriverOperationsPage token="tok" signedIn isAdmin navigate={() => {}} notify={() => {}} />)
    await user.click(await screen.findByRole('button', { name: /Rahim Ahmed/ }))
    await user.click(await screen.findByRole('button', { name: 'Schedule trip' }))
    const slot = nextSlot()
    await waitFor(() => expect(calls.find((c) => c.method === 'POST')?.body).toMatchObject({ driverId: 'd1', busId: 'b1', ...slot, direction: 'FORWARD' }))
  })

  it('suggests a slot today that can be started straight away', () => {
    const at = (hhmm) => Date.parse(`2026-11-11T${hhmm}:00+06:00`)
    expect(nextSlot(at('10:02'))).toEqual({ date: '2026-11-11', departureTime: '10:20', arrivalTime: '11:20' })
    expect(nextSlot(at('23:10'))).toEqual({ date: '2026-11-12', departureTime: '08:00', arrivalTime: '09:00' })
  })
})
