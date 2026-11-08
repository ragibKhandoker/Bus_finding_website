import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PassengerLookup } from './PassengerLookup'

const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))
const vehicle = { _id: 'v1', busCode: 'AL-2-01', busName: 'Alif', routeNumber: 'AL-2', currentPassengerCount: 12, totalPassengerCapacity: 40 }
let calls

beforeEach(() => {
  calls = []
  vi.stubGlobal('fetch', vi.fn((url) => {
    const { pathname, searchParams } = new URL(url)
    calls.push({ path: pathname, q: searchParams.get('q') })
    if (pathname === '/individual-bus/search') return reply(200, { success: true, data: [vehicle, { ...vehicle, _id: 'v2', busCode: 'AL-2-02' }] })
    if (pathname === '/individual-bus/AL-2-01') return reply(200, { success: true, data: vehicle })
    if (pathname === '/individual-bus/AL-2-09') return reply(200, { success: true, data: { ...vehicle, busCode: 'AL-2-09', currentPassengerCount: 38 } })
    return reply(404, { success: false, message: `No bus has the code "${decodeURIComponent(pathname.split('/').pop())}". Check the code written on the bus, e.g. AL-2-01.` })
  }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals() })

const renderLookup = (props = {}) => render(<PassengerLookup token="tok" signedIn openAuth={vi.fn()} navigate={vi.fn()} {...props} />)

describe('Check passenger count', () => {
  it('looks a bus up by the code written on it and shows how full it is', async () => {
    const user = userEvent.setup()
    renderLookup()
    await user.type(screen.getByRole('combobox', { name: 'Bus code' }), 'AL-2-01')
    await user.click(screen.getByRole('button', { name: /Look up bus/ }))
    const result = await screen.findByTestId('passenger-result')
    expect(calls.some((c) => c.path === '/individual-bus/AL-2-01')).toBe(true)
    expect(result.textContent).toContain('Alif · AL-2')
    expect(result.textContent).toContain('12 of 40 seats taken')
    expect(result.textContent).toContain('28 free')
    expect(result.className).toContain('is-free')
  })

  it('suggests bus codes while typing', async () => {
    const user = userEvent.setup()
    renderLookup()
    await user.type(screen.getByRole('combobox', { name: 'Bus code' }), 'AL')
    await waitFor(() => expect(document.querySelectorAll('datalist option')).toHaveLength(2))
    expect([...document.querySelectorAll('datalist option')].map((o) => o.value)).toEqual(['AL-2-01', 'AL-2-02'])
    expect(calls.find((c) => c.path === '/individual-bus/search').q).toBe('AL')
  })

  it('warns when a bus is nearly full', async () => {
    const user = userEvent.setup()
    renderLookup()
    await user.type(screen.getByRole('combobox', { name: 'Bus code' }), 'AL-2-09')
    await user.click(screen.getByRole('button', { name: /Look up bus/ }))
    const result = await screen.findByTestId('passenger-result')
    expect(result.className).toContain('is-full')
    expect(result.textContent).toContain('This bus is almost full.')
  })

  it('explains an unknown code instead of a bare "Request failed"', async () => {
    const user = userEvent.setup()
    renderLookup()
    await user.type(screen.getByRole('combobox', { name: 'Bus code' }), 'XYZ-1')
    await user.click(screen.getByRole('button', { name: /Look up bus/ }))
    expect((await screen.findByRole('alert')).textContent).toBe('No bus has the code "XYZ-1". Check the code written on the bus, e.g. AL-2-01.')
  })

  it('asks for a code when the box is empty, and sends visitors to sign in', async () => {
    const user = userEvent.setup()
    renderLookup()
    await user.click(screen.getByRole('button', { name: /Look up bus/ }))
    expect(screen.getByRole('alert').textContent).toBe('Type the code written on the bus, e.g. AL-2-01.')
    cleanup()
    const openAuth = vi.fn()
    renderLookup({ signedIn: false, openAuth })
    await user.click(screen.getByRole('button', { name: /Look up bus/ }))
    expect(openAuth).toHaveBeenCalled()
    expect(calls).toHaveLength(0)
  })
})
