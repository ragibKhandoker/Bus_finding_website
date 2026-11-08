import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { TripRow, tripDuration, useBoardControls } from './Trips'

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

function Board() {
  const { button, form } = useBoardControls({ bus, token: 'tok', signedIn: true, openAuth: vi.fn(), notify: vi.fn(), activeTrip: null })
  return <>{button}{form}</>
}

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
    for (const text of ['Victor Paribahan', 'General', '47 min', '9.4 km']) expect(screen.getByText(text)).toBeTruthy()
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
