import { beforeAll, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { searchFromUrl, searchUrl } from './views/pages/BusPage'

// Boots the whole app on /find-a-bus, searches, opens a bus's details, then presses Back: the search results
// must come back instead of an empty Find a bus page.
const base64Url = (value) => btoa(JSON.stringify(value)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_')
const token = `${base64Url({ alg: 'HS256' })}.${base64Url({ exp: Math.floor(Date.now() / 1000) + 3600 })}.sig`
const me = { id: 'u1', _id: 'u1', name: 'Test Rider', email: 'rider@test.local', role: 'commuter', friendCode: 'ME123' }
const stops = ['Jatrabari', 'Malibagh', 'Badda'].map((name, order) => ({ name, order, lat: 23.7 + order / 100, lng: 90.4 }))
const victor = { _id: 'b1', busName: 'Victor Classic', routeNumber: 'DBR-12', busType: 'general', base_fare: 10, per_km_fare: 2.45, stops }
const raida = { _id: 'b2', busName: 'Victor Express', routeNumber: 'DBR-40', busType: 'general', base_fare: 10, per_km_fare: 2.45, stops }
const searches = []

beforeAll(async () => {
  document.body.innerHTML = '<div id="root"></div>'
  window.name = 'sc-tab-test'
  sessionStorage.setItem('sc_tab_id', 'sc-tab-test')
  sessionStorage.setItem('sc_token', token)
  sessionStorage.setItem('sc_user', JSON.stringify(me))
  window.scrollTo = () => {}
  window.history.replaceState(null, '', '/find-a-bus')
  vi.stubGlobal('fetch', vi.fn((url) => {
    const { pathname, searchParams } = new URL(url)
    if (pathname === '/bus/search-by-name') searches.push(searchParams.get('busName'))
    const responses = {
      '/auth/validate': { success: true, valid: true, user: me },
      '/bus/search-by-name': { success: true, data: [victor, raida] },
      '/bus/route/DBR-40': { success: true, data: raida },
    }
    const body = responses[pathname] || { success: true, data: [] }
    return Promise.resolve(new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }))
  }))
  await import('./main')
  await waitFor(() => expect(document.querySelector('nav.nav-links')).not.toBeNull())
})

describe('search in the address', () => {
  it('reads and writes both kinds of search', () => {
    expect(searchFromUrl(searchUrl({ mode: 'name', name: 'Victor' }).split('?')[1])).toEqual({ mode: 'name', name: 'Victor' })
    expect(searchFromUrl(searchUrl({ mode: 'route', from: 'Farmgate', to: 'Uttara' }).split('?')[1])).toEqual({ mode: 'route', from: 'Farmgate', to: 'Uttara' })
    expect(searchFromUrl('?mode=name&name=')).toBeNull()
    expect(searchFromUrl('')).toBeNull()
  })
})

describe('Find a bus → details → Back', () => {
  it('comes back to the same search results with the same bus selected', async () => {
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /Search by bus/ }))
    await user.type(screen.getByPlaceholderText(/BRTC/), 'Victor')
    await user.click(screen.getByRole('button', { name: /Search buses/ }))
    await screen.findByText('Victor Express', {}, { timeout: 4000 })
    expect(window.location.pathname + window.location.search).toBe('/find-a-bus?mode=name&name=Victor')

    // Select the second bus, then open its details page.
    const second = screen.getByText('Victor Express').closest('article')
    await user.click(within(second).getByRole('button', { name: /Show on map/ }))
    expect(second.className).toMatch(/selected/)
    await user.click(within(second).getByRole('button', { name: /View details/ }))
    await waitFor(() => expect(window.location.pathname).toBe('/bus-routes/DBR-40'))

    window.history.back()
    await waitFor(() => expect(window.location.pathname + window.location.search).toBe('/find-a-bus?mode=name&name=Victor'))
    // The results are back without typing anything, searched again from the address.
    expect(await screen.findByText('Victor Express', {}, { timeout: 4000 })).toBeTruthy()
    expect(screen.getByRole('heading', { name: /Buses matching “Victor”/ })).toBeTruthy()
    expect(screen.getByPlaceholderText(/BRTC/).value).toBe('Victor')
    expect(searches).toEqual(['Victor', 'Victor'])
    await waitFor(() => expect(screen.getByText('Victor Express').closest('article').className).toMatch(/selected/))
  })
})
