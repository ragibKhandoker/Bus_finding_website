import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { FavouriteDashboard } from './FavouriteDashboard'
import { BusCard } from './BusPage'
import { dhakaInputValue, dhakaParts } from '../../models/favorites'

const stops = (names) => names.map((name, order) => ({ name, order, lat: 23.75 + order / 100, lng: 90.39 }))
const mirpur = { _id: 'b1', busName: 'Bikalpa Auto', routeNumber: 'DBR-30', busType: 'seating', base_fare: 10, per_km_fare: 2.45, isDhakaCity: true, stops: stops(['Mirpur 10', 'Farmgate', 'Motijheel']) }
const uttara = { _id: 'b2', busName: 'Raida', routeNumber: 'DBR-40', busType: 'general', base_fare: 8, per_km_fare: 2.45, isDhakaCity: true, stops: stops(['Uttara', 'Banani', 'Mohakhali']) }
const savar = { _id: 'b3', busName: 'Savar Paribahan', routeNumber: 'DBR-142', busType: 'general', base_fare: 15, per_km_fare: 2.45, isDhakaCity: false, stops: stops(['Gabtoli', 'Savar']) }
const favourite = (bus, extra = {}) => ({
  _id: `f-${bus._id}`, busId: bus._id, busName: bus.busName, routeNumber: bus.routeNumber, operator: null, busType: bus.busType,
  base_fare: bus.base_fare, stopCount: bus.stops.length, firstStop: bus.stops[0].name, lastStop: bus.stops.at(-1).name,
  isDhakaCity: bus.isDhakaCity, createdAt: '2026-11-01T10:00:00Z', ...extra,
})

let routes, calls
const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))

beforeEach(() => {
  calls = []
  routes = {
    'GET /bus/all': () => reply(200, { success: true, data: [mirpur, uttara, savar] }),
    'GET /api/favourites/my': () => reply(200, { success: true, data: [favourite(mirpur), favourite(uttara), favourite(savar)] }),
    'GET /api/favourites/dashboard': () => reply(200, { success: true, data: { favouriteBuses: 2, collections: 1, upcomingReminders: 1, activeAlerts: 0, unreadNotifications: 1 } }),
    'GET /api/favourite-collections': () => reply(200, { success: true, data: [{ _id: 'c1', name: 'Office route', busCount: 1, buses: [favourite(mirpur)] }] }),
    'GET /api/favourites/reminders': () => reply(200, { success: true, data: [] }),
    'GET /api/favourites/availability-alerts': () => reply(200, { success: true, data: [] }),
    'GET /api/favourites/notifications': () => reply(200, { success: true, unreadCount: 1, data: [{ _id: 'n1', title: 'Dhaka bus fare decreased', message: 'The base fare of Raida (DBR-40) changed from ৳10 to ৳8 (৳2 less).', category: 'bus-fare', type: 'success', isRead: false, createdAt: '2026-11-08T10:00:00Z' }] }),
  }
  vi.stubGlobal('fetch', vi.fn((url, options = {}) => {
    const { pathname } = new URL(url)
    const method = options.method || 'GET'
    const body = options.body && JSON.parse(options.body)
    calls.push({ method, path: pathname, body })
    const handler = routes[`${method} ${pathname}`]
    return handler ? handler({ body }) : reply(404, { success: false, message: 'Not found' })
  }))
  vi.spyOn(window, 'confirm').mockReturnValue(true)
})

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

const renderDashboard = () => {
  const notify = vi.fn()
  render(<FavouriteDashboard token="tok" user={{ id: 'u1' }} signedIn openAuth={vi.fn()} notify={notify} navigate={vi.fn()} />)
  return { notify }
}

describe('Favourite bus dashboard', () => {
  it('shows the overview from the server and the Dhaka City favourites, with older outside-city ones set apart', async () => {
    renderDashboard()
    const overview = document.querySelector('.fd-overview')
    await waitFor(() => expect(within(overview).getByText('Favourite Dhaka buses').closest('button').textContent).toContain('2'))
    expect(within(overview).getByText('Upcoming reminders').closest('button').textContent).toContain('1')
    expect(await screen.findByRole('heading', { level: 3, name: 'Bikalpa Auto' })).toBeTruthy()
    expect(screen.getByRole('heading', { level: 3, name: 'Raida' })).toBeTruthy()
    expect(screen.queryByRole('heading', { level: 3, name: 'Savar Paribahan' })).toBeNull()
    expect(screen.getByText('Saved before the Dhaka City rule')).toBeTruthy()
    // The unread module notification shows on its tab.
    expect(within(screen.getByRole('tab', { name: /Notifications/ })).getByText('1')).toBeTruthy()
  })

  it('compares the ticked buses with the server, showing its highlights', async () => {
    const user = userEvent.setup()
    routes['POST /api/favourites/compare'] = ({ body }) => reply(200, { success: true, data: {
      buses: [mirpur, uttara].map((b) => ({ busId: b._id, busName: b.busName, routeNumber: b.routeNumber, operator: null, startingPoint: b.stops[0].name, destination: b.stops.at(-1).name,
        route: b.stops.map((s) => s.name), totalStops: 3, base_fare: b.base_fare, per_km_fare: 2.45, airConditioning: null, departureInfo: null, vehiclesInService: 3, seatsFreeNow: 120, routeLengthKm: 9.5, servesDestination: null, trip: null })),
      highlights: { lowestFareBusId: body.busIds[1], shortestRouteBusId: null, mostSuitableBusId: null }, notes: ['Route length is measured along the stops.'], unresolved: {},
    } })
    renderDashboard()
    const cards = await screen.findAllByRole('checkbox', { name: 'Compare' })
    await user.click(cards[0]); await user.click(cards[1])
    await user.click(screen.getByRole('button', { name: /Compare 2/ }))
    await user.click(screen.getByRole('button', { name: 'Compare' }))
    const table = await screen.findByRole('table')
    expect(calls.find((c) => c.path === '/api/favourites/compare').body.busIds).toEqual(['b1', 'b2'])
    expect(within(table).getByText('Lowest fare').closest('th').textContent).toContain('Raida')
    expect(within(table).getAllByText('Not recorded')).toHaveLength(2)
  })

  it('adds a reminder for a stop on the bus, in Bangladesh time', async () => {
    const user = userEvent.setup()
    routes['POST /api/favourites/reminders'] = ({ body }) => reply(201, { success: true, data: { _id: 'r1', ...body } })
    const { notify } = renderDashboard()
    await user.click(await screen.findByRole('tab', { name: /Reminders/ }))
    await user.selectOptions(await screen.findByRole('combobox', { name: 'Favourite bus' }), 'b1')
    await waitFor(() => expect(screen.getByRole('option', { name: /Farmgate/ })).toBeTruthy())
    await user.selectOptions(screen.getByRole('combobox', { name: 'Stop you leave from' }), 'Farmgate')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Remind me' }), '30')
    await user.click(screen.getByRole('button', { name: /Add reminder/ }))
    await waitFor(() => expect(notify).toHaveBeenCalledWith('Reminder saved.'))
    const sent = calls.find((c) => c.method === 'POST' && c.path === '/api/favourites/reminders').body
    expect(sent).toMatchObject({ busId: 'b1', stopName: 'Farmgate', leadMinutes: 30 })
    expect(sent.departureAt).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/)
  })

  it('creates a collection and shows the server message when it is refused', async () => {
    const user = userEvent.setup()
    routes['POST /api/favourite-collections'] = () => reply(409, { success: false, message: 'You already have a collection with this name.' })
    const { notify } = renderDashboard()
    await user.click(await screen.findByRole('tab', { name: /Collections/ }))
    await user.type(screen.getByRole('textbox', { name: 'New collection name' }), 'Office route')
    await user.click(screen.getByRole('button', { name: /Create/ }))
    await waitFor(() => expect(notify).toHaveBeenCalledWith('You already have a collection with this name.'))
    // Refused: what was typed stays, so it can be changed.
    expect(screen.getByRole('textbox', { name: 'New collection name' }).value).toBe('Office route')
  })

  it('marks the module notifications read', async () => {
    const user = userEvent.setup()
    routes['PUT /api/favourites/notifications/read-all'] = () => reply(200, { success: true, count: 1 })
    renderDashboard()
    await user.click(await screen.findByRole('tab', { name: /Notifications/ }))
    expect(await screen.findByText('Dhaka bus fare decreased')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: /Mark all as read/ }))
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT' && c.path === '/api/favourites/notifications/read-all')).toBe(true))
  })
})

describe('the heart on bus cards', () => {
  const card = (bus) => render(<BusCard bus={bus} signedIn token="tok" openAuth={vi.fn()} selected={false} onSelect={vi.fn()} favorite={false} favoritePending={false} onFavorite={vi.fn()} onAddTrip={vi.fn()} boarding={{ token: 'tok', signedIn: true, openAuth: vi.fn(), notify: vi.fn(), activeTrip: null }} notify={vi.fn()} />)

  it('is shown for a Dhaka City bus and hidden for one outside the city', () => {
    routes['GET /api/reviews/bus/b1'] = () => reply(200, { success: true, data: [] })
    card(mirpur)
    expect(screen.getByRole('button', { name: 'Add to favourite buses' })).toBeTruthy()
    cleanup()
    card(savar)
    expect(screen.queryByRole('button', { name: 'Add to favourite buses' })).toBeNull()
  })
})

describe('Bangladesh time helpers', () => {
  it('writes the input value in Dhaka time and reads the server time without the device zone', () => {
    expect(dhakaInputValue(new Date('2026-11-10T02:30:00Z'))).toBe('2026-11-10T08:30')
    const parts = dhakaParts('2026-11-10T08:30+06:00')
    expect(parts.time).toBe('08:30')
    expect(parts.date.toISOString()).toBe('2026-11-10T00:00:00.000Z')
  })
})
