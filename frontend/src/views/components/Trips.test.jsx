import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ActiveTripPanel, TripRow, tripDuration, useBoardControls } from './Trips'
import { formatClock } from '../../controllers/useTrips'

const bus = {
  _id: 'bus1', busName: 'Victor Classic', runsBothWays: false,
  stops: ['Jatrabari', 'Malibagh', 'Rampura', 'Badda'].map((name, order) => ({ name, order, lat: 23.7 + order / 100, lng: 90.4 })),
}
const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))
let calls

beforeEach(() => {
  calls = []
  vi.stubGlobal('fetch', vi.fn((url, options = {}) => {
    const { pathname } = new URL(url)
    calls.push({ method: options.method || 'GET', path: pathname, body: options.body && JSON.parse(options.body) })
    if (pathname === '/bus/individual/bus1') return reply(200, { data: [{ _id: 'v1', busCode: 'R-11-03', currentPassengerCount: 0, totalPassengerCapacity: 40 }] })
    if (pathname === '/individual-bus/v1/board') return reply(200, { data: { busName: 'Victor Classic', source: 'Malibagh' } })
    return reply(404, { message: 'Not found' })
  }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals() })

function Board({ activeTrip = null }) {
  const { button, form } = useBoardControls({ bus, token: 'tok', signedIn: true, openAuth: vi.fn(), notify: vi.fn(), activeTrip })
  return <>{button}{form}</>
}

describe('Time on the bus', () => {
  const startedAt = '2026-11-08T08:00:00Z'
  const activeReply = (serverTime) => reply(200, {
    success: true, serverTime,
    data: { _id: 't1', status: 'active', busName: 'Victor Classic', busCode: 'R-11-03', individualBusId: 'v1', busId: 'bus1', source: 'Malibagh', destination: '', startedAt },
    route: bus,
  })
  const routeFetch = (handlers) => fetch.mockImplementation((url, options = {}) => {
    const handler = handlers[`${options.method || 'GET'} ${new URL(url).pathname}`]
    return handler ? handler() : reply(404, { message: 'Not found' })
  })

  it('formats the running clock', () => {
    expect(formatClock(0)).toBe('00:00')
    expect(formatClock(65_000)).toBe('01:05')
    expect(formatClock(3_923_000)).toBe('1:05:23')
    expect(formatClock(null)).toBe('')
  })

  it('counts up every second while on the bus, by the server clock', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] })
    // This device's clock is 10 minutes slow; the server says 12 min 4 s have passed since boarding.
    vi.setSystemTime(new Date('2026-11-08T08:02:04Z'))
    routeFetch({ 'GET /trip/active': () => activeReply('2026-11-08T08:12:04Z') })
    render(<ActiveTripPanel token="tok" signedIn />)
    const timer = await screen.findByTestId('trip-timer')
    expect(timer.textContent).toContain('12:04')
    act(() => { vi.advanceTimersByTime(3000) })
    expect(timer.textContent).toContain('12:07')
    act(() => { vi.advanceTimersByTime(48 * 60_000) })
    expect(timer.textContent).toContain('1:00:07')
    vi.useRealTimers()
  })

  it('shows the total time on the bus when the trip ends', async () => {
    const user = userEvent.setup()
    routeFetch({
      'GET /trip/active': () => activeReply(new Date().toISOString()),
      'POST /individual-bus/v1/end-trip': () => reply(200, {
        data: { _id: 't1', status: 'completed', busName: 'Victor Classic', source: 'Malibagh', destination: 'Badda', fare: 20, distance: 4.2, gemsEarned: 10, startedAt, endedAt: '2026-11-08T09:05:00Z', durationMinutes: 65 },
        wallet: { gems: 120 }, gemsEarned: 10,
      }),
    })
    render(<ActiveTripPanel token="tok" signedIn notify={vi.fn()} />)
    await user.click(await screen.findByRole('button', { name: 'Getting off at' }))
    await user.click(screen.getByRole('option', { name: 'Badda' }))
    await user.click(screen.getByRole('button', { name: /End trip/ }))
    const done = await screen.findByRole('status')
    expect(done.textContent).toContain('Trip completed!')
    expect(within(done).getByText('Time on the bus').nextSibling.textContent).toContain('1 hr 5 min')
  })
})

describe('One bus at a time', () => {
  const onOtherBus = { _id: 't9', busId: 'other-route', busName: 'Raida Paribahan', status: 'active' }

  it('locks Board on every other bus while you are on one, and says why', async () => {
    const user = userEvent.setup()
    render(<Board activeTrip={onOtherBus} />)
    const board = screen.getByRole('button', { name: /Board/ })
    expect(board.disabled).toBe(true)
    await user.click(board)
    expect(calls).toHaveLength(0)
    expect(screen.getByRole('note').textContent).toContain('You are already on Raida Paribahan')
    expect(screen.getByRole('note').textContent).toContain('You can only be on one bus at a time.')
    expect(screen.getByRole('button', { name: /Go to my trip/ })).toBeTruthy()
  })

  it('shows "On this bus" on the bus you are on', () => {
    render(<Board activeTrip={{ ...onOtherBus, busId: 'bus1', busName: 'Victor Classic' }} />)
    expect(screen.getByText('On this bus')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /Board/ })).toBeNull()
  })

  it('lets you board again once the trip has ended', async () => {
    const user = userEvent.setup()
    const { rerender } = render(<Board activeTrip={onOtherBus} />)
    expect(screen.getByRole('button', { name: /Board/ }).disabled).toBe(true)
    rerender(<Board activeTrip={null} />)
    expect(screen.queryByRole('note')).toBeNull()
    await user.click(screen.getByRole('button', { name: /Board/ }))
    expect(await screen.findByRole('button', { name: 'Getting on at' })).toBeTruthy()
  })

  it("shows the server's refusal if another trip started elsewhere (another tab or phone)", async () => {
    const user = userEvent.setup()
    fetch.mockImplementation((url) => {
      const { pathname } = new URL(url)
      if (pathname === '/bus/individual/bus1') return reply(200, { data: [{ _id: 'v1', busCode: 'R-11-03', currentPassengerCount: 0, totalPassengerCapacity: 40 }] })
      return reply(409, { message: 'You already have an active trip on Raida Paribahan. End it before boarding another bus.' })
    })
    render(<Board />)
    await user.click(screen.getByRole('button', { name: /Board/ }))
    await user.click(await screen.findByRole('button', { name: 'Getting on at' }))
    await user.click(screen.getByRole('option', { name: 'Malibagh' }))
    await user.click(screen.getByRole('button', { name: /Start trip/ }))
    expect((await screen.findByRole('alert')).textContent).toBe('You already have an active trip on Raida Paribahan. End it before boarding another bus.')
  })
})

describe('Trip history details', () => {
  const finished = {
    _id: 't1', status: 'completed', busName: 'Victor Classic', routeNumber: 'DBR-12', busCode: 'R-11-03', busType: 'general', operator: 'Victor Paribahan',
    source: 'Jatrabari', destination: 'Natun Bazar', fare: 25, distance: 9.4, gemsEarned: 10, baseFare: 10, perKmFare: 2.45,
    startedAt: '2026-11-08T08:00:00Z', endedAt: '2026-11-08T08:47:00Z', durationMinutes: 47,
    stopsRidden: ['Jatrabari', 'Sayedabad', 'Kamalapur', 'Khilgaon', 'Malibagh', 'Rampura', 'Badda', 'Natun Bazar'],
  }

  it('opens a trip to show the bus, time, fare and every stop ridden', async () => {
    const user = userEvent.setup()
    const onOpenRoute = vi.fn()
    render(<TripRow trip={finished} onOpenRoute={onOpenRoute} />)
    const head = screen.getByRole('button', { name: /Jatrabari.*Natun Bazar/ })
    expect(head.textContent).toContain('DBR-12')
    expect(head.textContent).toContain('R-11-03')
    expect(screen.queryByText('Victor Paribahan')).toBeNull()

    await user.click(head)
    expect(head.getAttribute('aria-expanded')).toBe('true')
    for (const text of ['Victor Paribahan', 'General', '9.4 km']) expect(screen.getByText(text)).toBeTruthy()
    expect(screen.getByText('Time on the bus').nextSibling.textContent).toBe('47 min')
    expect(screen.getByText('Gems earned').nextSibling.textContent).toBe('+10')

    // A long ride shows its ends first, then every stop.
    const timeline = () => screen.getByRole('list')
    expect(within(timeline()).queryByText('Kamalapur')).toBeNull()
    await user.click(screen.getByRole('button', { name: '+4 more stops' }))
    expect(within(timeline()).getAllByRole('listitem').map((li) => li.firstChild.textContent)).toEqual(finished.stopsRidden)

    await user.click(screen.getByRole('button', { name: /View this bus route/ }))
    expect(onOpenRoute).toHaveBeenCalledWith('DBR-12')
  })

  it('leaves out details an older trip did not save', async () => {
    const user = userEvent.setup()
    render(<TripRow trip={{ _id: 't2', status: 'completed', busName: 'Bus', source: 'Mirpur 10', destination: 'Farmgate', fare: 20, distance: 0, createdAt: '2026-10-01T10:00:00Z' }} />)
    await user.click(screen.getByRole('button', { name: /Mirpur 10/ }))
    expect(screen.queryByText('Route number')).toBeNull()
    expect(screen.queryByText('Stops on this ride')).toBeNull()
    expect(screen.queryByRole('button', { name: /View this bus route/ })).toBeNull()
    expect(screen.getByText('Fare paid')).toBeTruthy()
  })

  it('shows how long the ride took on the summary line', () => {
    render(<TripRow trip={finished} />)
    expect(screen.getByRole('button', { name: /Jatrabari/ }).textContent).toContain('47 min')
  })

  it('formats time on the bus', () => {
    expect(tripDuration(0.4)).toBe('under a minute')
    expect(tripDuration(25)).toBe('25 min')
    expect(tripDuration(65)).toBe('1 hr 5 min')
    expect(tripDuration(null)).toBeNull()
  })
})

describe('Board form stop pickers', () => {
  it('picks stops from the styled lists and boards with them', async () => {
    const user = userEvent.setup()
    render(<Board />)
    await user.click(screen.getByRole('button', { name: /Board/ }))
    const on = await screen.findByRole('button', { name: 'Getting on at' })
    expect(on.textContent).toContain('Choose a stop')

    await user.click(on)
    const list = screen.getByRole('listbox')
    expect(within(list).getAllByRole('option').map((o) => o.textContent)).toEqual(['Choose a stop', 'Jatrabari', 'Malibagh', 'Rampura', 'Badda'])
    await user.click(within(list).getByRole('option', { name: 'Malibagh' }))
    expect(screen.queryByRole('listbox')).toBeNull()
    expect(on.textContent).toContain('Malibagh')

    // Only stops after the boarding stop can be the destination.
    const off = screen.getByRole('button', { name: 'Getting off at (optional)' })
    expect(off.textContent).toContain('Decide later')
    await user.click(off)
    expect(within(screen.getByRole('listbox')).getAllByRole('option').map((o) => o.textContent)).toEqual(['Decide later', 'Rampura', 'Badda'])
    await user.click(screen.getByRole('option', { name: 'Badda' }))

    await user.click(screen.getByRole('button', { name: /Start trip/ }))
    expect(calls.find((c) => c.path === '/individual-bus/v1/board').body).toEqual({ boardingStop: 'Malibagh', destinationStop: 'Badda' })
  })

  it('clears a destination that no longer comes after the new boarding stop', async () => {
    const user = userEvent.setup()
    render(<Board />)
    await user.click(screen.getByRole('button', { name: /Board/ }))
    await user.click(await screen.findByRole('button', { name: 'Getting on at' }))
    await user.click(screen.getByRole('option', { name: 'Jatrabari' }))
    await user.click(screen.getByRole('button', { name: 'Getting off at (optional)' }))
    await user.click(screen.getByRole('option', { name: 'Malibagh' }))
    await user.click(screen.getByRole('button', { name: 'Getting on at' }))
    await user.click(screen.getByRole('option', { name: 'Rampura' }))
    expect(screen.getByRole('button', { name: 'Getting off at (optional)' }).textContent).toContain('Decide later')
  })
})
