import { beforeAll, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

// D: the tab is reloaded on a page of search results that was reached through the app (the browser keeps the
// entry's history.state across the reload). The results come back from the address and navigation keeps working.
const base64Url = (value) => btoa(JSON.stringify(value)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_')
const token = `${base64Url({ alg: 'HS256' })}.${base64Url({ exp: Math.floor(Date.now() / 1000) + 3600 })}.sig`
const me = { id: 'u1', _id: 'u1', name: 'Test Rider', email: 'rider@test.local', role: 'commuter', friendCode: 'ME123' }
const stops = ['Jatrabari', 'Malibagh', 'Badda'].map((name, order) => ({ name, order, lat: 23.7 + order / 100, lng: 90.4 }))
const victor = { _id: 'b1', busName: 'Victor Classic', routeNumber: 'DBR-12', busType: 'general', base_fare: 10, per_km_fare: 2.45, stops }

beforeAll(async () => {
  document.body.innerHTML = '<div id="root"></div>'
  window.name = 'sc-tab-test'
  sessionStorage.setItem('sc_tab_id', 'sc-tab-test')
  sessionStorage.setItem('sc_token', token)
  sessionStorage.setItem('sc_user', JSON.stringify(me))
  window.scrollTo = () => {}
  window.history.replaceState({ page: 'Find a bus', idx: 2, from: '/find-a-bus' }, '', '/Find-A-Bus/?mode=name&name=Victor')
  vi.stubGlobal('fetch', vi.fn((url) => {
    const { pathname } = new URL(url)
    const responses = {
      '/auth/validate': { success: true, valid: true, user: me },
      '/bus/search-by-name': { success: true, data: [victor] },
      '/bus/route/DBR-12': { success: true, data: victor },
    }
    const body = responses[pathname] || { success: true, data: [] }
    return Promise.resolve(new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }))
  }))
  await import('./main')
  await waitFor(() => expect(document.querySelector('nav.nav-links')).not.toBeNull())
})

const here = () => window.location.pathname + window.location.search

describe('D: reloading a nested page', () => {
  it('shows the same results, tidies the address and keeps the entry\'s place in the history', async () => {
    expect(await screen.findByText('Victor Classic', {}, { timeout: 4000 })).toBeTruthy()
    expect(here()).toBe('/find-a-bus?mode=name&name=Victor')
    expect(window.history.state).toMatchObject({ idx: 2, from: '/find-a-bus' })
    expect(within(document.querySelector('nav.nav-links')).getByRole('button', { name: 'Find a bus' }).className).toMatch(/active/)
  })

  it('keeps navigating normally after the reload', async () => {
    const user = userEvent.setup()
    await user.click(within(screen.getByText('Victor Classic').closest('article')).getByRole('button', { name: /View details/ }))
    await waitFor(() => expect(here()).toBe('/bus-routes/DBR-12'))
    expect(window.history.state.idx).toBe(3)
    window.history.back()
    await waitFor(() => expect(here()).toBe('/find-a-bus?mode=name&name=Victor'))
    expect(await screen.findByText('Victor Classic')).toBeTruthy()
  })
})
