import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { nearestStop, qrLink, tokenFromScan } from '../../models/busQr'
import { ScanBoardPage } from './ScanBoardPage'

const TOKEN = 'A'.repeat(20) + 'b_c-' + 'D'.repeat(19)

const route = {
  _id: 'route-1', busName: 'Raida', routeNumber: '36', busType: 'general', operator: 'Raida Paribahan', base_fare: 10, per_km_fare: 2.5, runsBothWays: true,
  stops: [{ name: 'Mirpur 10', order: 0, lat: 23.807, lng: 90.368 }, { name: 'Farmgate', order: 1, lat: 23.758, lng: 90.389 }, { name: 'Motijheel', order: 2, lat: 23.733, lng: 90.417 }],
}
const scan = (x = {}) => ({ scanTicket: 'T'.repeat(43), expiresAt: '2026-11-28T10:10:00Z', vehicle: { id: 'v1', busCode: 'RA-36-01', seatsFree: 12, totalPassengerCapacity: 40 }, route, nearestStop: 'Farmgate', activeTrip: null, onThisBus: false, ...x })

let calls
let routes
const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))

beforeEach(() => {
  calls = []
  routes = {
    'POST /bus/verify-qr': () => reply(200, { success: true, data: scan() }),
    'POST /trip/board-by-qr': () => reply(201, { success: true, message: 'You boarded Raida at Farmgate.', data: { _id: 't1', busName: 'Raida', busCode: 'RA-36-01', source: 'Farmgate', destination: '', startedAt: '2026-11-28T10:00:00Z', status: 'active', boardingMethod: 'qr' } }),
  }
  vi.stubGlobal('fetch', vi.fn((url, options = {}) => {
    const u = new URL(url)
    const key = `${options.method || 'GET'} ${u.pathname}`
    calls.push({ key, body: options.body && JSON.parse(options.body) })
    return (routes[key] || (() => reply(404, { message: 'not mocked' })))()
  }))
  // No location in tests: the stop comes from the server's answer.
  vi.stubGlobal('navigator', { ...navigator, geolocation: undefined, mediaDevices: undefined })
  window.history.replaceState(null, '', `/scan?c=${TOKEN}`)
})
afterEach(() => { cleanup(); vi.unstubAllGlobals(); window.history.replaceState(null, '', '/') })

describe('QR helpers', () => {
  it('reads the token from the printed link or on its own, and nothing else', () => {
    expect(tokenFromScan(qrLink(TOKEN, 'https://smartcommute-dhaka.vercel.app'))).toBe(TOKEN)
    expect(tokenFromScan(` ${TOKEN} `)).toBe(TOKEN)
    expect(tokenFromScan('https://example.com/menu')).toBe('')
    expect(tokenFromScan('hello')).toBe('')
  })

  it('finds the nearest stop among the allowed ones', () => {
    expect(nearestStop(route.stops, { lat: 23.7585, lng: 90.3891 }).name).toBe('Farmgate')
    expect(nearestStop(route.stops, { lat: 23.7585, lng: 90.3891 }, ['Mirpur 10', 'Motijheel']).name).toBe('Motijheel')
    expect(nearestStop(route.stops, null)).toBeNull()
  })
})

describe('ScanBoardPage', () => {
  it('opened from the bus QR link: checks the code, asks to confirm, then starts the trip', async () => {
    const navigate = vi.fn()
    const notify = vi.fn()
    render(<ScanBoardPage token="tok" signedIn navigate={navigate} notify={notify} openAuth={vi.fn()} />)

    expect(await screen.findByText('Are you boarding this bus?')).toBeTruthy()
    expect(screen.getByText('Raida')).toBeTruthy()
    expect(screen.getByText('RA-36-01')).toBeTruthy()
    expect(screen.getByText('12/40')).toBeTruthy()
    expect(calls[0]).toEqual({ key: 'POST /bus/verify-qr', body: { code: TOKEN } })
    // The secret code leaves the address bar once it is used.
    expect(window.location.search).toBe('')
    expect(screen.getByText('Filled in from your location. Change it if it is wrong.')).toBeTruthy()

    await userEvent.click(screen.getByRole('button', { name: /Yes, start my trip/ }))
    expect(await screen.findByText('Trip started!')).toBeTruthy()
    const board = calls.find((c) => c.key === 'POST /trip/board-by-qr')
    // Only the one-time ticket and the stops go to the server, never a bus or route id.
    expect(board.body).toEqual({ scanTicket: 'T'.repeat(43), boardingStop: 'Farmgate', destinationStop: null })
    expect(notify).toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: /View my trip/ }))
    expect(navigate).toHaveBeenCalledWith('My trips')
  })

  it('explains a code that is no longer in use and offers to scan again', async () => {
    routes['POST /bus/verify-qr'] = () => reply(410, { success: false, message: "This QR code is no longer in use. Ask the driver for the bus's current code." })
    render(<ScanBoardPage token="tok" signedIn navigate={vi.fn()} openAuth={vi.fn()} />)
    expect(await screen.findByText("This QR code is no longer in use. Ask the driver for the bus's current code.")).toBeTruthy()
    await userEvent.click(screen.getByRole('button', { name: /Scan again/ }))
    // No camera in the test browser: the scanner says so and offers a photo instead.
    expect(await screen.findByRole('button', { name: /Scan from a photo/ })).toBeTruthy()
  })

  it('does not offer to board while the rider is on another bus', async () => {
    routes['POST /bus/verify-qr'] = () => reply(200, { success: true, data: scan({ activeTrip: { busName: 'Bikash', source: 'Gulistan' } }) })
    render(<ScanBoardPage token="tok" signedIn navigate={vi.fn()} openAuth={vi.fn()} />)
    expect(await screen.findByText('You are already on Bikash')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /Yes, start my trip/ })).toBeNull()
  })

  it('asks a signed-out visitor to sign in first', () => {
    render(<ScanBoardPage token={null} signedIn={false} navigate={vi.fn()} openAuth={vi.fn()} />)
    expect(screen.getByText('Sign in to board the bus you scanned.')).toBeTruthy()
    expect(fetch).not.toHaveBeenCalled()
  })
})
