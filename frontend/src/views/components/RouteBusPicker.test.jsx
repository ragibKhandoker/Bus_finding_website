import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { BUS_PREVIEW, RouteBusPicker } from './RouteBusPicker'

const start = { name: 'Mirpur 10', lat: 23.8084, lon: 90.3683 }
const end = { name: 'Motijheel', lat: 23.7273, lon: 90.4212 }
const option = (id, name, routeNumber, fare, walk = 0.2, extra = {}) => ({
  bus: { _id: id, busName: name, routeNumber, match: { direction: 'forward', boarding: { name: 'Mirpur 10' }, alighting: { name: 'Motijheel' }, segment: [{}, {}, {}, {}, {}] }, ...extra },
  walkToBoardingKm: walk, walkFromAlightingKm: 0.1, rideKm: 12.4, fare,
})
const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))
let routes, calls

beforeEach(() => {
  calls = []
  routes = {
    'GET /bus/near-route': () => reply(200, { success: true, data: [option('r1', 'Bikolpo', 'BK-3', 30), option('r2', 'Shikor', 'SK-1', 35, 0.6)] }),
    'POST /trip/add': () => reply(201, { success: true }),
  }
  vi.stubGlobal('fetch', vi.fn((url, options = {}) => {
    const { pathname, searchParams } = new URL(url)
    const method = options.method || 'GET'
    calls.push({ method, path: pathname, params: Object.fromEntries(searchParams), body: options.body && JSON.parse(options.body) })
    const handler = routes[`${method} ${pathname}`]
    return handler ? handler() : reply(404, { message: 'Not found' })
  }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals() })

describe('Which bus did you take?', () => {
  it('lists the buses between the two places and logs the one chosen', async () => {
    const user = userEvent.setup()
    const notify = vi.fn()
    render(<RouteBusPicker token="tok" start={start} end={end} notify={notify} />)

    const cards = await screen.findAllByTestId('route-bus')
    expect(calls[0].params).toEqual({ fromLat: '23.8084', fromLng: '90.3683', toLat: '23.7273', toLng: '90.4212' })
    expect(cards[0].textContent).toContain('Bikolpo')
    expect(cards[0].textContent).toContain('BK-3')
    expect(cards[0].textContent).toContain('৳30')
    expect(cards[0].textContent).toContain('200 m walk')

    const logButton = screen.getByRole('button', { name: /Log this trip/ })
    expect(logButton.disabled).toBe(true)
    await user.click(within(cards[1]).getByRole('radio'))
    expect(cards[1].className).toContain('is-selected')
    await user.click(logButton)

    await waitFor(() => expect(calls.find((c) => c.method === 'POST')?.body).toEqual({ busId: 'r2', busName: 'Shikor', distance: 12.4, fare: 35, source: 'Mirpur 10', destination: 'Motijheel' }))
    expect(notify).toHaveBeenCalledWith('Trip on Shikor logged. It now appears in your trip history.')
    expect(await screen.findByRole('button', { name: /Trip logged/ })).toBeTruthy()
  })

  it('unselects the chosen bus when it is clicked again', async () => {
    const user = userEvent.setup()
    render(<RouteBusPicker token="tok" start={start} end={end} />)
    const cards = await screen.findAllByTestId('route-bus')
    const logButton = screen.getByRole('button', { name: /Log this trip/ })

    await user.click(cards[0])
    expect(cards[0].className).toContain('is-selected')
    expect(within(cards[0]).getByRole('radio').checked).toBe(true)
    expect(logButton.disabled).toBe(false)

    await user.click(cards[0])
    expect(cards[0].className).not.toContain('is-selected')
    expect(within(cards[0]).getByRole('radio').checked).toBe(false)
    expect(logButton.disabled).toBe(true)

    // Choosing another bus still switches straight to it.
    await user.click(cards[0])
    await user.click(cards[1])
    expect(cards[0].className).not.toContain('is-selected')
    expect(cards[1].className).toContain('is-selected')
  })

  it('says so when no bus runs between the places', async () => {
    routes['GET /bus/near-route'] = () => reply(200, { success: true, data: [] })
    render(<RouteBusPicker token="tok" start={start} end={end} />)
    expect(await screen.findByText(/No bus in our database runs between these two places/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: /Log this trip/ })).toBeNull()
  })

  it("shows the server's message when there is no stop near a place", async () => {
    routes['GET /bus/near-route'] = () => reply(404, { message: 'There is no bus stop within 1 km of the destination.' })
    render(<RouteBusPicker token="tok" start={start} end={end} />)
    expect((await screen.findByRole('alert')).textContent).toBe('There is no bus stop within 1 km of the destination.')
  })

  it('picks the only bus straight away, and keeps a long list short until asked', async () => {
    routes['GET /bus/near-route'] = () => reply(200, { success: true, data: [option('r1', 'Bikolpo', 'BK-3', 30)] })
    const { unmount } = render(<RouteBusPicker token="tok" start={start} end={end} />)
    expect((await screen.findByRole('radio')).checked).toBe(true)
    expect(screen.getByRole('button', { name: /Log this trip/ }).disabled).toBe(false)
    unmount()

    const many = Array.from({ length: BUS_PREVIEW + 3 }, (_, i) => option(`r${i}`, `Bus ${i}`, `B-${i}`, 30 + i))
    routes['GET /bus/near-route'] = () => reply(200, { success: true, data: many })
    const user = userEvent.setup()
    render(<RouteBusPicker token="tok" start={start} end={end} />)
    expect(await screen.findAllByTestId('route-bus')).toHaveLength(BUS_PREVIEW)
    await user.click(screen.getByRole('button', { name: 'Show 3 more buses' }))
    expect(screen.getAllByTestId('route-bus')).toHaveLength(BUS_PREVIEW + 3)
  })

  it('shows nothing until both places are picked', () => {
    render(<RouteBusPicker token="tok" start={start} end={null} />)
    expect(screen.queryByRole('region')).toBeNull()
    expect(calls).toHaveLength(0)
  })
})
