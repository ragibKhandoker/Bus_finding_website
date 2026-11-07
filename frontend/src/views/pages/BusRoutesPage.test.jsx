import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { BusRoutesPage } from './BusRoutesPage'

const bus = (id, routeNumber, busName, busType, stops) => ({ _id: id, routeNumber, busName, busType, operator: 'Dhaka Transport',
  stops: stops.map((name, order) => ({ name, order, lat: 23.8 + order / 100, lng: 90.4 })) })
const routes = [
  bus('b1', 'DBR-1', 'Raida', 'general', ['Mirpur 10', 'Farmgate', 'Motijheel']),
  bus('b2', 'DBR-2', 'Turag', 'seating', ['Uttara', 'Banani', 'Gulistan']),
]

beforeEach(() => {
  localStorage.clear()
  vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(new Response(JSON.stringify({ success: true, count: routes.length, data: routes }),
    { status: 200, headers: { 'Content-Type': 'application/json' } }))))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

describe('Bus routes page', () => {
  it('shows every route with the bus-type filters, and filters by type and search', async () => {
    const user = userEvent.setup()
    render(<BusRoutesPage navigate={() => {}} />)
    expect(await screen.findByText('Raida')).toBeTruthy()
    expect(screen.getByText('Turag')).toBeTruthy()
    // the filter buttons come from BUS_TYPES
    for (const label of ['All buses', 'general', 'semi-seating', 'seating']) expect(screen.getByRole('button', { name: label })).toBeTruthy()

    await user.click(screen.getByRole('button', { name: 'seating' }))
    expect(screen.queryByText('Raida')).toBeNull()
    expect(screen.getByText('Turag')).toBeTruthy()

    await user.click(screen.getByRole('button', { name: 'All buses' }))
    await user.type(screen.getByPlaceholderText('Search a bus or a stop'), 'farmgate')
    expect(screen.getByText('Raida')).toBeTruthy()
    expect(screen.queryByText('Turag')).toBeNull()
  })
})
