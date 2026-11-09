import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RidePage, forgetRideResults } from './RideShare'
import { RideDetailsPage } from './RideDetails'
import { MyRidesPage } from './MyRides'
import { searchQuery, validateRideForm, EMPTY_FILTERS, forgetPlaceCaches } from '../../models/rides'

const me = { id: 'me', name: 'Rafi Ahmed', gender: 'Male' }
const future = new Date(Date.now() + 3 * 3600 * 1000).toISOString()
const viewer = (extra = {}) => ({ role: 'none', canRequest: true, cannotRequestReason: null, canChat: false, canManage: false, canReview: false, ...extra })
const ride = (id, extra = {}) => ({
  _id: id, userId: 'u2', host: { id: 'u2', name: 'Nadia Islam', verified: true, rating: 4.6, reviewCount: 8, completedRides: 12 },
  source: 'Mirpur 10', destination: 'Motijheel', departureAt: future, maxParticipants: 4, seatsLeft: 2, participantCount: 2,
  participants: [{ userId: 'p1', userName: 'Karim' }], genderPreference: 'Any', vehicleType: 'Car', costPerPerson: 80,
  contactPreference: 'chat', status: 'active', state: 'OPEN', isOpen: true, viewer: viewer(), createdAt: '2026-11-09T05:00:00Z', ...extra,
})

let routes
let calls
const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))
const page = (items, total = items.length) => reply(200, { success: true, total, totalPages: Math.ceil(total / 12), data: items })

beforeEach(() => {
  calls = []
  localStorage.clear(); sessionStorage.clear()
  window.history.replaceState({}, '', '/ride-share')
  routes = { 'GET /api/rideshare': () => page([ride('r1'), ride('r2', { destination: 'Banani', vehicleType: 'CNG', costPerPerson: 40 })]) }
  vi.stubGlobal('fetch', vi.fn((url, options = {}) => {
    const { pathname, searchParams } = new URL(url)
    const method = options.method || 'GET'
    const body = options.body && JSON.parse(options.body)
    calls.push({ method, path: pathname, params: Object.fromEntries(searchParams), body, auth: options.headers?.Authorization })
    const handler = routes[`${method} ${pathname}`]
    return handler ? handler({ params: Object.fromEntries(searchParams), body }) : reply(404, { success: false, message: 'Ride post not found.' })
  }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks(); forgetRideResults(); forgetPlaceCaches() })

const props = (extra = {}) => ({ token: 'tok', user: me, signedIn: true, notify: vi.fn(), navigate: vi.fn(), openAuth: vi.fn(), ...extra })

describe('ride form checks', () => {
  const place = (name, lat = 23.8, lng = 90.4) => ({ name, lat, lng })
  const today = new Date(Date.now() + 6 * 3600 * 1000).toISOString().slice(0, 10)
  const ok = { source: place('Mirpur 10'), destination: place('Motijheel', 23.73, 90.41), date: '2099-01-01', time: '08:30', maxParticipants: '4', genderPreference: 'Any',
    vehicleType: 'Car', costPerPerson: '80', instructions: '', contactPreference: 'chat', pickupDetails: '' }
  const now = new Date('2098-12-20T00:00:00Z')
  it('accepts a complete Dhaka ride', () => expect(validateRideForm(ok, now)).toEqual({}))
  it.each([
    [{ source: null }, 'source'],
    [{ source: { name: 'Typed only', lat: null, lng: null } }, 'source'],
    [{ destination: place('Savar', 23.85, 90.26) }, 'destination'],
    [{ destination: place('mirpur 10') }, 'destination'],
    [{ date: '' }, 'date'],
    [{ date: today, time: '00:00' }, 'time'],
    [{ maxParticipants: '1' }, 'maxParticipants'],
    [{ vehicleType: 'Bike', maxParticipants: '3' }, 'maxParticipants'],
    [{ costPerPerson: '' }, 'costPerPerson'],
    [{ costPerPerson: '-5' }, 'costPerPerson'],
    [{ costPerPerson: '12.345' }, 'costPerPerson'],
  ])('refuses %o', (change, field) => {
    const when = change.date === today ? new Date() : now
    expect(Object.keys(validateRideForm({ ...ok, ...change }, when))).toContain(field)
  })
  it('builds the discovery query only from the filters in use', () => {
    expect(searchQuery(EMPTY_FILTERS, 1)).toBe('page=1&pageSize=12')
    expect(searchQuery({ ...EMPTY_FILTERS, destination: ' Banani ', vehicle: 'CNG', gender: 'Female', minSeats: '2', minCost: '0', maxCost: '100', sort: 'cost', date: '2026-11-10' }, 2))
      .toBe('destination=Banani&date=2026-11-10&gender=Female&vehicle=CNG&minSeats=2&minCost=0&maxCost=100&sort=cost&page=2&pageSize=12')
  })
})

describe('Ride discovery', () => {
  it('shows ride cards with host, route, time, vehicle, seats and cost', async () => {
    render(<RidePage {...props()} />)
    const cards = await screen.findAllByTestId('ride-card')
    expect(cards).toHaveLength(2)
    const first = within(cards[0])
    expect(first.getByText('Nadia Islam')).toBeTruthy()
    expect(first.getByText('Mirpur 10')).toBeTruthy()
    expect(first.getByText('Motijheel')).toBeTruthy()
    expect(first.getByText('৳80')).toBeTruthy()
    expect(first.getByText('2 left')).toBeTruthy()
    expect(first.getByText('Open')).toBeTruthy()
    expect(first.getByRole('button', { name: /Request to join/ })).toBeTruthy()
  })

  it('sends the filters to the server and offers to clear them when nothing matches', async () => {
    const user = userEvent.setup()
    render(<RidePage {...props()} />)
    await screen.findAllByTestId('ride-card')
    routes['GET /api/rideshare'] = () => page([])
    await user.selectOptions(screen.getByRole('combobox', { name: 'Vehicle type' }), 'CNG')
    await waitFor(() => expect(calls.at(-1).params.vehicle).toBe('CNG'))
    expect(await screen.findByText('No rides match these filters')).toBeTruthy()
    routes['GET /api/rideshare'] = () => page([ride('r1')])
    await user.click(screen.getAllByRole('button', { name: /Clear filters/ })[0])
    await waitFor(() => expect(calls.at(-1).params.vehicle).toBeUndefined())
    expect(await screen.findAllByTestId('ride-card')).toHaveLength(1)
  })

  it('suggests places in From and To and searches as soon as one is picked', async () => {
    const user = userEvent.setup()
    routes['GET /api/rideshare'] = () => page([ride('r1', { source: 'Badda', destination: 'Banani' })])
    routes['GET /api/stops'] = () => reply(200, { success: true, data: [{ name: 'Badda Link Road', lat: 23.7805, lng: 90.4225 }, { name: 'Gulshan 1', lat: 23.78, lng: 90.416 }] })
    routes['GET /api/geocoding/search'] = () => reply(200, { success: true, data: [] })
    render(<RidePage {...props()} />)
    await screen.findAllByTestId('ride-card')
    await user.type(screen.getByRole('combobox', { name: 'From' }), 'Bad')
    expect(await screen.findByRole('option', { name: /Badda On a listed ride/ })).toBeTruthy()
    expect(await screen.findByRole('option', { name: /Badda Link Road/ })).toBeTruthy()
    await waitFor(() => expect(calls.find((c) => c.path === '/api/geocoding/search')?.params.lang).toBe('en'))
    expect(screen.queryByRole('option', { name: /Gulshan/ })).toBeNull()
    await user.click(screen.getByRole('option', { name: /Badda Link Road/ }))
    expect(screen.getByRole('combobox', { name: 'From' }).value).toBe('Badda Link Road')
    await waitFor(() => expect(calls.at(-1).params.source).toBe('Badda Link Road'))

    await user.type(screen.getByRole('combobox', { name: 'To' }), 'Ban')
    await user.click(await screen.findByRole('option', { name: /Banani/ }))
    await waitFor(() => expect(calls.at(-1).params).toMatchObject({ source: 'Badda Link Road', destination: 'Banani' }))
  })

  it('lets the host cancel their own ride from the list, after confirming', async () => {
    const user = userEvent.setup()
    routes['GET /api/rideshare'] = () => page([ride('mine', { userId: 'me', host: { id: 'me', name: 'Rafi Ahmed' }, viewer: viewer({ role: 'host', canRequest: false, cannotRequestReason: 'own', canManage: true }) }), ride('r2')])
    routes['POST /api/rideshare/mine/cancel'] = () => reply(200, { success: true })
    render(<RidePage {...props()} />)
    const [mineCard, theirs] = await screen.findAllByTestId('ride-card')
    expect(within(theirs).queryByRole('button', { name: /Cancel ride/ })).toBeNull()
    await user.click(within(mineCard).getByRole('button', { name: /Cancel ride/ }))
    const dialog = screen.getByRole('dialog', { name: 'Cancel this ride?' })
    await user.type(within(dialog).getByRole('textbox'), 'Plans changed')
    await user.click(within(dialog).getByRole('button', { name: 'Cancel ride' }))
    await waitFor(() => expect(calls.find((c) => c.path === '/api/rideshare/mine/cancel')?.body).toEqual({ reason: 'Plans changed' }))
    await user.click(within(await screen.findByRole('dialog', { name: 'Ride cancelled' })).getByRole('button', { name: 'OK' }))
    expect(screen.getAllByTestId('ride-card')).toHaveLength(1)
  })

  it('shows a search seen before at once and refreshes it in the background', async () => {
    const first = render(<RidePage {...props()} />)
    expect(await screen.findAllByTestId('ride-card')).toHaveLength(2)
    first.unmount()
    let release
    routes['GET /api/rideshare'] = () => new Promise((resolve) => { release = () => resolve(page([ride('r1')])) })
    render(<RidePage {...props()} />)
    expect(screen.getAllByTestId('ride-card')).toHaveLength(2)
    release()
    await waitFor(() => expect(screen.getAllByTestId('ride-card')).toHaveLength(1))
  })

  it('does not fetch place names until From or To is used', async () => {
    const user = userEvent.setup()
    render(<RidePage {...props()} />)
    await screen.findAllByTestId('ride-card')
    expect(calls.filter((c) => c.path === '/api/rideshare' && c.params.pageSize === '100')).toHaveLength(0)
    await user.click(screen.getByRole('combobox', { name: 'From' }))
    await waitFor(() => expect(calls.filter((c) => c.path === '/api/rideshare' && c.params.pageSize === '100')).toHaveLength(1))
  })

  it('sorts on the server', async () => {
    const user = userEvent.setup()
    render(<RidePage {...props()} />)
    await screen.findAllByTestId('ride-card')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Sort by' }), 'cost')
    await waitFor(() => expect(calls.at(-1).params.sort).toBe('cost'))
  })

  it('pages through long lists', async () => {
    const user = userEvent.setup()
    routes['GET /api/rideshare'] = ({ params }) => page([ride(`r${params.page}`)], 30)
    render(<RidePage {...props()} />)
    await screen.findAllByTestId('ride-card')
    await user.click(screen.getByRole('button', { name: 'Next page' }))
    await waitFor(() => expect(calls.at(-1).params.page).toBe('2'))
  })

  it('shows an error with a retry when rides cannot be loaded', async () => {
    const user = userEvent.setup()
    routes['GET /api/rideshare'] = () => reply(500, { success: false, message: 'Server is down' })
    render(<RidePage {...props()} />)
    expect(await screen.findByText('Server is down')).toBeTruthy()
    routes['GET /api/rideshare'] = () => page([ride('r1')])
    await user.click(screen.getByRole('button', { name: 'Try again' }))
    expect(await screen.findAllByTestId('ride-card')).toHaveLength(1)
  })

  it('asks guests to sign in before offering or joining a ride', async () => {
    const user = userEvent.setup()
    const p = props({ signedIn: false, token: null, user: null })
    render(<RidePage {...p} />)
    await screen.findAllByTestId('ride-card')
    await user.click(screen.getAllByRole('button', { name: /Offer a ride/ })[0])
    await user.click(screen.getAllByRole('button', { name: /Request to join/ })[0])
    expect(p.openAuth).toHaveBeenCalledTimes(2)
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('confirms before sending a join request, then shows it as pending', async () => {
    const user = userEvent.setup()
    routes['POST /api/rideshare/r1/requests'] = ({ body }) => reply(201, { success: true, data: { _id: 'q1', status: 'pending', note: body.note } })
    const p = props()
    render(<RidePage {...p} />)
    const [card] = await screen.findAllByTestId('ride-card')
    await user.click(within(card).getByRole('button', { name: /Request to join/ }))
    const dialog = screen.getByRole('dialog', { name: 'Request to join this ride?' })
    await user.type(within(dialog).getByRole('textbox'), 'At the gate')
    await user.click(within(dialog).getByRole('button', { name: /Send request/ }))
    await waitFor(() => expect(p.notify).toHaveBeenCalledWith('Request sent. The host will be notified.'))
    expect(calls.find((c) => c.method === 'POST').body).toEqual({ note: 'At the gate' })
    expect(within(card).getByText('Pending')).toBeTruthy()
  })

  it('keeps the dialog open with the server reason when joining is refused', async () => {
    const user = userEvent.setup()
    routes['POST /api/rideshare/r1/requests'] = () => reply(409, { success: false, message: 'This ride is full.' })
    render(<RidePage {...props()} />)
    const [card] = await screen.findAllByTestId('ride-card')
    await user.click(within(card).getByRole('button', { name: /Request to join/ }))
    await user.click(screen.getByRole('button', { name: /Send request/ }))
    expect(await screen.findByText('This ride is full.')).toBeTruthy()
    expect(screen.getByRole('dialog')).toBeTruthy()
  })

  it('shows matching bus stops in Offer a ride at once, without waiting for the map search', async () => {
    const user = userEvent.setup()
    routes['GET /api/stops'] = () => reply(200, { success: true, data: [{ name: 'Mirpur 10', lat: 23.8069, lng: 90.3687 }, { name: 'Mirpur 1', lat: 23.7956, lng: 90.3534 }] })
    routes['GET /api/geocoding/search'] = () => new Promise(() => {}) // the map never answers
    render(<RidePage {...props()} />)
    await screen.findAllByTestId('ride-card')
    await user.click(screen.getAllByRole('button', { name: /Offer a ride/ })[0])
    await user.type(within(screen.getByRole('dialog')).getByRole('combobox', { name: 'Source location' }), 'Mirp')
    const options = within(await screen.findByRole('listbox')).getAllByRole('option')
    expect(options.map((o) => o.textContent)).toEqual(['Mirpur 1Bus stop · Dhaka', 'Mirpur 10Bus stop · Dhaka'])
    await user.click(screen.getByRole('option', { name: /Mirpur 10/ }))
    expect(screen.getByRole('combobox', { name: 'Source location' }).value).toBe('Mirpur 10')
    expect(screen.getByLabelText('Location confirmed')).toBeTruthy()
  })

  it('validates the ride form before sending anything', async () => {
    const user = userEvent.setup()
    render(<RidePage {...props()} />)
    await screen.findAllByTestId('ride-card')
    await user.click(screen.getAllByRole('button', { name: /Offer a ride/ })[0])
    await user.click(screen.getByRole('button', { name: /Publish ride/ }))
    expect(screen.getByText('Choose where the ride starts.')).toBeTruthy()
    expect(screen.getByText('Choose the departure time.')).toBeTruthy()
    expect(screen.getByText('Enter the cost per person (0 if it is free).')).toBeTruthy()
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('picks Dhaka places from the suggestions and publishes the ride', async () => {
    const user = userEvent.setup()
    routes['GET /api/stops'] = () => reply(200, { success: true, data: [{ name: 'Mirpur 10', lat: 23.8069, lng: 90.3687 }, { name: 'Motijheel', lat: 23.733, lng: 90.4172 }] })
    routes['GET /api/geocoding/search'] = () => reply(200, { success: true, data: [{ display_name: 'Savar, Dhaka', lat: 23.85, lon: 90.26 }] })
    routes['POST /api/rideshare'] = ({ body }) => reply(201, { success: true, data: { ...ride('new'), ...body } })
    const p = props()
    render(<RidePage {...p} />)
    await screen.findAllByTestId('ride-card')
    await user.click(screen.getAllByRole('button', { name: /Offer a ride/ })[0])
    const dialog = screen.getByRole('dialog')
    await user.type(within(dialog).getByRole('combobox', { name: 'Source location' }), 'Mirpur')
    await user.click(await within(dialog).findByRole('option', { name: /Mirpur 10/ }))
    await user.type(within(dialog).getByRole('combobox', { name: 'Destination' }), 'Motij')
    expect(within(dialog).queryByRole('option', { name: /Savar/ })).toBeNull()
    await user.click(await within(dialog).findByRole('option', { name: /Motijheel/ }))
    const tomorrow = new Date(Date.now() + 30 * 3600 * 1000).toISOString().slice(0, 10)
    await user.clear(within(dialog).getByLabelText('Departure date'))
    await user.type(within(dialog).getByLabelText('Departure date'), tomorrow)
    await user.type(within(dialog).getByLabelText(/Departure time/), '08:30')
    await user.click(within(dialog).getByRole('radio', { name: /CNG/ }))
    await user.type(within(dialog).getByLabelText(/Estimated cost per person/), '60')
    await user.click(within(dialog).getByRole('button', { name: /Publish ride/ }))
    await waitFor(() => expect(p.navigate).toHaveBeenCalledWith('Ride details', { route: 'new' }))
    expect(calls.find((c) => c.method === 'POST' && c.path === '/api/rideshare').body).toMatchObject({
      source: 'Mirpur 10', sourceLat: 23.8069, destination: 'Motijheel', destinationLng: 90.4172, departureDate: tomorrow, departureTime: '08:30',
      vehicleType: 'CNG', maxParticipants: 4, costPerPerson: 60, genderPreference: 'Any', contactPreference: 'chat',
    })
  })
})

describe('Ride details', () => {
  const renderDetails = (r, extra = {}) => {
    routes['GET /api/rideshare/r1'] = () => reply(200, { success: true, data: r })
    const p = props(extra)
    render(<RideDetailsPage rideId="r1" {...p} />)
    return p
  }

  it('shows everything about the ride and hides private details from strangers', async () => {
    renderDetails(ride('r1', { instructions: 'AC car, no smoking' }))
    expect(await screen.findByText('AC car, no smoking')).toBeTruthy()
    expect(screen.getAllByText('Nadia Islam').length).toBeGreaterThan(0)
    expect(screen.getByRole('button', { name: 'Request to join' })).toBeTruthy()
    expect(screen.queryByText(/Pickup details/)).toBeNull()
    expect(screen.queryByRole('heading', { name: /Ride chat/ })).toBeNull()
    expect(screen.getAllByText('Seat available')).toHaveLength(2)
  })

  it('lets an approved rider see the pickup, chat and leave', async () => {
    const user = userEvent.setup()
    routes['GET /api/rideshare/r1/messages'] = () => reply(200, { success: true, data: [{ _id: 'm1', senderId: 'u2', senderName: 'Nadia Islam', fromHost: true, text: 'Meet at gate 2', createdAt: '2026-11-09T05:00:00Z' }] })
    routes['POST /api/rideshare/r1/messages'] = ({ body }) => reply(201, { success: true, data: { _id: 'm2', senderId: 'me', senderName: 'Rafi Ahmed', text: body.text, createdAt: '2026-11-09T05:01:00Z' } })
    routes['DELETE /api/rideshare/requests/q1'] = () => reply(200, { success: true })
    renderDetails(ride('r1', { pickupDetails: 'Gate 2', contactPreference: 'both', contact: { whatsApp: '8801712345678', phone: '01712345678', chatOnly: false },
      viewer: viewer({ role: 'approved', requestId: 'q1', canRequest: false, cannotRequestReason: 'requested', canChat: true }) }))
    expect(await screen.findByText('You are on this ride')).toBeTruthy()
    const whatsApp = screen.getByRole('link', { name: 'Message Nadia Islam on WhatsApp' })
    expect(whatsApp.getAttribute('href')).toMatch(/^https:\/\/wa\.me\/8801712345678\?text=Hi%20Nadia%20Islam/)
    expect(whatsApp.getAttribute('target')).toBe('_blank')
    expect(screen.getAllByText(/Gate 2/).length).toBeGreaterThan(0)
    expect(await screen.findByText('Meet at gate 2')).toBeTruthy()
    await user.type(screen.getByRole('textbox', { name: 'Message' }), 'On my way')
    await user.click(screen.getByRole('button', { name: 'Send' }))
    expect(await screen.findByText('On my way')).toBeTruthy()
    await user.click(screen.getAllByRole('button', { name: 'Leave ride' })[0])
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Leave ride' }))
    await waitFor(() => expect(calls.some((c) => c.method === 'DELETE' && c.path === '/api/rideshare/requests/q1')).toBe(true))
  })

  it('gives the host passenger management with accept, reject and remove', async () => {
    const user = userEvent.setup()
    const requester = (id, name, status) => ({ _id: id, status, state: status === 'accepted' ? 'APPROVED' : status.toUpperCase(), createdAt: '2026-11-09T05:00:00Z',
      requester: { id: `u-${id}`, name, rating: null, reviewCount: 0, completedRides: 1 } })
    routes['GET /api/rideshare/r1/requests'] = () => reply(200, { success: true, data: [requester('a', 'Sadia', 'pending'), { ...requester('b', 'Karim', 'accepted'), contactVisible: true, whatsApp: null }] })
    routes['POST /api/rideshare/requests/a/approve'] = () => reply(200, { success: true, ride: { seatsLeft: 1 } })
    routes['DELETE /api/rideshare/r1/passengers/u-b'] = () => reply(200, { success: true })
    routes['GET /api/rideshare/r1/messages'] = () => reply(200, { success: true, data: [] })
    const p = renderDetails(ride('r1', { userId: 'me', host: { id: 'me', name: 'Rafi Ahmed' }, pendingCount: 1, viewer: viewer({ role: 'host', canRequest: false, cannotRequestReason: 'own', canChat: true, canManage: true }) }))
    expect(await screen.findByText('Passenger management')).toBeTruthy()
    await user.click(await screen.findByRole('button', { name: /Accept/ }))
    await waitFor(() => expect(p.notify).toHaveBeenCalledWith('Sadia approved. 1 seat left.'))
    expect(p.notify).toHaveBeenCalledTimes(1)
    await user.click(screen.getByRole('tab', { name: /Approved/ }))
    expect(screen.getByText(/No WhatsApp/)).toBeTruthy()
    expect(screen.queryByText('This number has no WhatsApp')).toBeNull()
    await user.click(screen.getByRole('button', { name: /Remove/ }))
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Remove rider' }))
    await waitFor(() => expect(calls.some((c) => c.method === 'DELETE' && c.path === '/api/rideshare/r1/passengers/u-b')).toBe(true))
    expect(screen.getByRole('button', { name: /Cancel ride/ })).toBeTruthy()
    expect(screen.getByRole('button', { name: /Mark as completed/ })).toBeTruthy()
  })

  it('lets the host view a requester’s profile and accept from it', async () => {
    const user = userEvent.setup()
    routes['GET /api/rideshare/r1/requests'] = () => reply(200, { success: true, data: [{ _id: 'a', status: 'pending', state: 'PENDING', note: 'I will wait at the gate', createdAt: '2026-11-09T05:00:00Z', requester: { id: 'u-a', name: 'Imam hasan', completedRides: 0 } }] })
    routes['GET /api/rideshare/users/u-a/profile'] = () => reply(200, { success: true, data: { id: 'u-a', name: 'Imam hasan', gender: 'Male', verified: false, identityVerified: true, bio: 'Student at BUET, ride daily to Motijheel.',
      memberSince: '2026-01-01T00:00:00Z', rating: 4.5, reviewCount: 2, completedRides: 3, hostedRides: 1, reviews: [], isMe: false, blockedByMe: false } })
    routes['POST /api/rideshare/requests/a/approve'] = () => reply(200, { success: true, ride: { seatsLeft: 1 } })
    routes['GET /api/rideshare/r1/messages'] = () => reply(200, { success: true, data: [] })
    const p = renderDetails(ride('r1', { viewer: viewer({ role: 'host', canRequest: false, canChat: true, canManage: true }) }))
    await user.click(await screen.findByRole('button', { name: 'View profile of Imam hasan' }))
    const dialog = await screen.findByRole('dialog', { name: 'Rider profile' })
    expect(await within(dialog).findByText('Student at BUET, ride daily to Motijheel.')).toBeTruthy()
    expect(within(dialog).getByText('NID verified')).toBeTruthy()
    expect(within(dialog).getByText('Note: “I will wait at the gate”')).toBeTruthy()
    await user.click(within(dialog).getByRole('button', { name: /Accept/ }))
    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === '/api/rideshare/requests/a/approve')).toBe(true))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(p.notify).toHaveBeenCalledWith('Imam hasan approved. 1 seat left.')
  })

  it('approving the last seat says so in one message', async () => {
    const user = userEvent.setup()
    routes['GET /api/rideshare/r1/requests'] = () => reply(200, { success: true, data: [{ _id: 'a', status: 'pending', state: 'PENDING', createdAt: '2026-11-09T05:00:00Z', requester: { id: 'u-a', name: 'Sadia', completedRides: 0 } }] })
    routes['POST /api/rideshare/requests/a/approve'] = () => reply(200, { success: true, ride: { seatsLeft: 0 } })
    routes['GET /api/rideshare/r1/messages'] = () => reply(200, { success: true, data: [] })
    const p = renderDetails(ride('r1', { viewer: viewer({ role: 'host', canRequest: false, canChat: true, canManage: true }) }))
    await user.click(await screen.findByRole('button', { name: /Accept/ }))
    await waitFor(() => expect(p.notify).toHaveBeenCalledWith('Sadia approved. All seats are taken, so the ride is now full.'))
    expect(p.notify).toHaveBeenCalledTimes(1)
  })

  it('cancels a ride only after confirmation, with the reason', async () => {
    const user = userEvent.setup()
    routes['POST /api/rideshare/r1/cancel'] = () => reply(200, { success: true })
    routes['GET /api/rideshare/r1/requests'] = () => reply(200, { success: true, data: [] })
    routes['GET /api/rideshare/r1/messages'] = () => reply(200, { success: true, data: [] })
    renderDetails(ride('r1', { viewer: viewer({ role: 'host', canRequest: false, canChat: true, canManage: true }) }))
    await user.click(await screen.findByRole('button', { name: /Cancel ride/ }))
    const dialog = screen.getByRole('dialog')
    await user.type(within(dialog).getByRole('textbox'), 'Car broke down')
    await user.click(within(dialog).getByRole('button', { name: 'Cancel ride' }))
    await waitFor(() => expect(calls.find((c) => c.path === '/api/rideshare/r1/cancel')?.body).toEqual({ reason: 'Car broke down' }))
  })

  it('lets members of a completed ride review each other', async () => {
    const user = userEvent.setup()
    routes['GET /api/rideshare/r1/reviews'] = () => reply(200, { success: true, data: [], canReview: [{ userId: 'u2', name: 'Nadia Islam' }] })
    routes['POST /api/rideshare/r1/reviews'] = () => reply(201, { success: true })
    routes['GET /api/rideshare/r1/messages'] = () => reply(200, { success: true, data: [] })
    renderDetails(ride('r1', { state: 'COMPLETED', status: 'completed', viewer: viewer({ role: 'approved', canRequest: false, cannotRequestReason: 'closed', canChat: true, canReview: true }) }))
    await user.click(await screen.findByRole('radio', { name: '4 stars' }))
    await user.click(screen.getByRole('button', { name: /Submit review/ }))
    await waitFor(() => expect(calls.find((c) => c.method === 'POST' && c.path === '/api/rideshare/r1/reviews').body).toMatchObject({ reviewedUserId: 'u2', rating: 4 }))
  })

  it('explains why a full or closed ride takes no requests', async () => {
    renderDetails(ride('r1', { state: 'FULL', status: 'full', seatsLeft: 0, viewer: viewer({ canRequest: false, cannotRequestReason: 'full' }) }))
    expect(await screen.findByText('This ride is full')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Request to join' })).toBeNull()
  })
})

describe('My rides', () => {
  it('lists hosted and joined rides by status', async () => {
    const user = userEvent.setup()
    routes['GET /api/rideshare/my/hosted'] = () => reply(200, { success: true, data: [
      ride('h1', { pendingCount: 2, viewer: viewer({ role: 'host', canManage: true }) }),
      ride('h2', { destination: 'Uttara', state: 'COMPLETED', status: 'completed', viewer: viewer({ role: 'host' }) }),
    ] })
    routes['GET /api/rideshare/my/joined'] = () => reply(200, { success: true, data: [
      { request: { _id: 'q1', status: 'pending', state: 'PENDING' }, ride: ride('j1', { destination: 'Gulshan' }) },
      { request: { _id: 'q2', status: 'accepted', state: 'APPROVED' }, ride: ride('j2', { destination: 'Badda' }) },
    ] })
    routes['DELETE /api/rideshare/requests/q1'] = () => reply(200, { success: true })
    render(<MyRidesPage {...props()} />)
    expect(await screen.findByText('2 waiting')).toBeTruthy()
    expect(screen.queryByText('Uttara')).toBeNull()
    await user.click(screen.getByRole('tab', { name: /Completed/ }))
    expect(screen.getByText('Uttara')).toBeTruthy()

    await user.click(screen.getByRole('tab', { name: 'My joined rides' }))
    expect(screen.getByText('Gulshan')).toBeTruthy()
    expect(screen.queryByText('Badda')).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Cancel' }))
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Cancel request' }))
    await waitFor(() => expect(calls.some((c) => c.method === 'DELETE' && c.path === '/api/rideshare/requests/q1')).toBe(true))
    // The dialog stays to confirm it worked, until OK.
    const done = await screen.findByRole('dialog', { name: 'Request cancelled' })
    expect(within(done).getByText(/Your request was withdrawn/)).toBeTruthy()
    await user.click(within(done).getByRole('button', { name: 'OK' }))
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('asks guests to sign in', () => {
    const p = props({ signedIn: false, token: null, user: null })
    render(<MyRidesPage {...p} />)
    expect(screen.getByText('Sign in to see your rides')).toBeTruthy()
  })
})
