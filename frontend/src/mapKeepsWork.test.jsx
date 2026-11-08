import { beforeAll, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

// Boots the whole app on /map, calculates a fare, goes to another page, then presses Back: the places, the
// result and the bus lookup must still be there instead of an empty page.
const base64Url = (value) => btoa(JSON.stringify(value)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_')
const token = `${base64Url({ alg: 'HS256' })}.${base64Url({ exp: Math.floor(Date.now() / 1000) + 3600 })}.sig`
const me = { id: 'u1', _id: 'u1', name: 'Test Rider', email: 'rider@test.local', role: 'commuter', friendCode: 'ME123' }
const places = {
  Mirpur: { display_name: 'Mirpur 10, Dhaka', lat: 23.8083, lon: 90.3682, place_id: 'p1' },
  Motijheel: { display_name: 'Motijheel, Dhaka', lat: 23.7272, lon: 90.4211, place_id: 'p2' },
}
const fare = { _id: 'f1', source: 'Mirpur 10', destination: 'Motijheel', distance: 12.84, farePerKm: 30, fare: 386, distanceEstimated: false, createdAt: '2026-11-08T10:00:00Z' }

beforeAll(async () => {
  document.body.innerHTML = '<div id="root"></div>'
  window.name = 'sc-tab-test'
  sessionStorage.setItem('sc_tab_id', 'sc-tab-test')
  sessionStorage.setItem('sc_token', token)
  sessionStorage.setItem('sc_user', JSON.stringify(me))
  window.scrollTo = () => {}
  window.history.replaceState(null, '', '/map')
  vi.stubGlobal('fetch', vi.fn((url, options = {}) => {
    const { pathname, searchParams } = new URL(url)
    const body = (data, status = 200) => Promise.resolve(new Response(JSON.stringify(data), { status, headers: { 'Content-Type': 'application/json' } }))
    if (pathname === '/auth/validate') return body({ success: true, valid: true, user: me })
    if (pathname === '/api/fares/rate') return body({ success: true, data: { farePerKm: 30, currency: 'BDT', configured: true } })
    if (pathname === '/api/geocoding/search') return body({ success: true, data: [searchParams.get('q').startsWith('Mirpur') ? places.Mirpur : places.Motijheel] })
    if (pathname === '/api/fares/calculate' && options.method === 'POST') return body({ success: true, data: { fare, duration: 25, path: [], saved: true } }, 201)
    if (pathname === '/individual-bus/AL-2-01') return body({ success: true, data: { _id: 'v1', busCode: 'AL-2-01', busName: 'Alif', routeNumber: 'AL-2', currentPassengerCount: 12, totalPassengerCapacity: 40 } })
    return body({ success: true, data: [] })
  }))
  await import('./main')
  await waitFor(() => expect(document.querySelector('nav.nav-links')).not.toBeNull())
})

const pick = async (user, label, text) => {
  const input = screen.getByLabelText(label)
  await user.type(input, text)
  await user.keyboard('{Enter}')
  await user.click(await screen.findByRole('option', { name: new RegExp(text.split(' ')[0]) }, { timeout: 3000 }))
}

describe('Map page keeps the work in progress', () => {
  it('shows the same places, fare and bus lookup after going to another page and pressing Back', async () => {
    const user = userEvent.setup()
    await screen.findByText('Where are you going?', {}, { timeout: 3000 })
    await pick(user, 'From (starting point)', 'Mirpur 10')
    await pick(user, 'To (destination)', 'Motijheel')
    await user.click(screen.getByRole('button', { name: /Calculate distance & fare/ }))
    expect(await screen.findByText('৳386', {}, { timeout: 3000 })).toBeTruthy()

    await user.type(screen.getByRole('combobox', { name: 'Bus code' }), 'AL-2-01')
    await user.click(screen.getByRole('button', { name: /Look up bus/ }))
    expect((await screen.findByTestId('passenger-result')).textContent).toContain('12 of 40 seats taken')

    // Leave for another page, then come back with the browser's Back button.
    await user.click(within(document.querySelector('nav.nav-links')).getByRole('button', { name: 'Home' }))
    await waitFor(() => expect(window.location.pathname).toBe('/'))
    expect(screen.queryByText('৳386')).toBeNull()
    window.history.back()
    await waitFor(() => expect(window.location.pathname).toBe('/map'))

    expect(await screen.findByText('৳386', {}, { timeout: 3000 })).toBeTruthy()
    expect(screen.getByLabelText('From (starting point)').value).toBe('Mirpur 10, Dhaka')
    expect(screen.getByLabelText('To (destination)').value).toBe('Motijheel, Dhaka')
    expect(screen.getByRole('combobox', { name: 'Bus code' }).value).toBe('AL-2-01')
    expect(screen.getByTestId('passenger-result').textContent).toContain('12 of 40 seats taken')
  })
})
