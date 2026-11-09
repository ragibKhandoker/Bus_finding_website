import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { openPath } from './controllers/router'

// Boots the whole app as a signed-in user and walks through it with the menu, links and the browser's own Back and
// Forward buttons: every page change is one browser history entry, and Back/Forward retrace exactly that path.
const base64Url = (value) => btoa(JSON.stringify(value)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_')
const token = `${base64Url({ alg: 'HS256' })}.${base64Url({ exp: Math.floor(Date.now() / 1000) + 3600 })}.sig`
const me = { id: 'u1', _id: 'u1', name: 'Test Rider', email: 'rider@test.local', role: 'commuter', friendCode: 'ME123' }
const stops = ['Jatrabari', 'Malibagh', 'Badda'].map((name, order) => ({ name, order, lat: 23.7 + order / 100, lng: 90.4 }))
const victor = { _id: 'b1', busName: 'Victor Classic', routeNumber: 'DBR-12', busType: 'general', base_fare: 10, per_km_fare: 2.45, stops }
const raida = { _id: 'b2', busName: 'Raida Express', routeNumber: 'DBR-40', busType: 'general', base_fare: 10, per_km_fare: 2.45, stops }
const searches = []

beforeAll(async () => {
  document.body.innerHTML = '<div id="root"></div>'
  window.name = 'sc-tab-test'
  sessionStorage.setItem('sc_tab_id', 'sc-tab-test')
  sessionStorage.setItem('sc_token', token)
  sessionStorage.setItem('sc_user', JSON.stringify(me))
  window.scrollTo = () => {}
  window.history.replaceState(null, '', '/')
  vi.stubGlobal('fetch', vi.fn((url) => {
    const { pathname, searchParams } = new URL(url)
    if (pathname === '/bus/search-by-name') searches.push(searchParams.get('busName'))
    const responses = {
      '/auth/validate': { success: true, valid: true, user: me },
      '/bus/all': { success: true, data: [victor, raida] },
      '/bus/search-by-name': { success: true, data: searchParams.get('busName') === 'Raida' ? [raida] : [victor] },
      '/bus/route/DBR-12': { success: true, data: victor },
      '/bus/route/DBR-40': { success: true, data: raida },
    }
    const body = responses[pathname] || { success: true, data: [] }
    return Promise.resolve(new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }))
  }))
  await import('./main')
  await waitFor(() => expect(document.querySelector('nav.nav-links')).not.toBeNull())
})

const here = () => window.location.pathname + window.location.search
const navBar = () => document.querySelector('nav.nav-links')
const tab = async (user, name) => user.click(within(navBar()).getByRole('button', { name }))
const back = async (expected) => { window.history.back(); await waitFor(() => expect(here()).toBe(expected)) }
const forward = async (expected) => { window.history.forward(); await waitFor(() => expect(here()).toBe(expected)) }
const searchByName = async (user, text) => {
  await user.click(await screen.findByRole('button', { name: /Search by bus/ }))
  const input = screen.getByPlaceholderText(/BRTC/)
  await user.clear(input)
  await user.type(input, text)
  await user.click(screen.getByRole('button', { name: /Search buses/ }))
  await waitFor(() => expect(here()).toBe(`/find-a-bus?mode=name&name=${text}`))
  await screen.findByRole('heading', { name: new RegExp(`Buses matching “${text}”`) }, { timeout: 4000 })
  // The "Searching…" animation runs for a moment before the results show.
  await screen.findByRole('button', { name: /Search buses/ }, { timeout: 4000 })
}

beforeEach(async () => {
  // Every walk starts on the home page.
  await userEvent.setup().click(screen.getByRole('button', { name: 'SmartCommute home' }))
  await waitFor(() => expect(here()).toBe('/'))
})

describe('browser history', () => {
  it('A: Home → Find a bus → results → bus details, then Back one page at a time', async () => {
    const user = userEvent.setup()
    await tab(user, 'Find a bus')
    expect(here()).toBe('/find-a-bus')
    await searchByName(user, 'Victor')
    await user.click(within(screen.getByText('Victor Classic').closest('article')).getByRole('button', { name: /View details/ }))
    await waitFor(() => expect(here()).toBe('/bus-routes/DBR-12'))

    await back('/find-a-bus?mode=name&name=Victor')
    expect(await screen.findByText('Victor Classic')).toBeTruthy()
    await back('/find-a-bus')
    // The search form, with no results.
    await waitFor(() => expect(screen.queryByRole('heading', { name: /Buses matching/ })).toBeNull())
    expect(screen.getByRole('heading', { level: 1, name: 'Find your bus.' })).toBeTruthy()
    await back('/')
  })

  it('B + E: the main tabs retrace their exact order with Back, then Forward', async () => {
    const user = userEvent.setup()
    const visited = ['/', '/find-a-bus', '/bus-routes', '/ride-share', '/my-trips']
    for (const name of ['Find a bus', 'Bus routes', 'Ride share', 'My trips']) await tab(user, name)
    expect(here()).toBe('/my-trips')
    expect(within(navBar()).getByRole('button', { name: 'My trips' }).className).toMatch(/active/)
    for (let i = visited.length - 2; i >= 0; i--) await back(visited[i])
    expect(within(navBar()).getByRole('button', { name: 'Home' }).className).toMatch(/active/)
    for (let i = 1; i < visited.length; i++) await forward(visited[i])
    expect(await screen.findByRole('heading', { level: 1, name: 'Trip history.' })).toBeTruthy()
  })

  it('C: Back from a route opened from Bus routes goes to the page actually visited before, not Home', async () => {
    const user = userEvent.setup()
    await tab(user, 'Find a bus')
    await searchByName(user, 'Raida')
    await tab(user, 'Bus routes')
    await user.click(await screen.findByRole('button', { name: /Victor Classic/ }))
    await waitFor(() => expect(here()).toBe('/bus-routes/DBR-12'))
    expect(await screen.findByRole('heading', { level: 1, name: 'Victor Classic' })).toBeTruthy()

    await back('/bus-routes')
    await back('/find-a-bus?mode=name&name=Raida')
    expect(await screen.findByText('Raida Express')).toBeTruthy()
    await back('/find-a-bus')
    await back('/')
  })

  it('each new search is its own entry; the same search again adds nothing; Back/Forward switch between them', async () => {
    const user = userEvent.setup()
    await tab(user, 'Find a bus')
    await searchByName(user, 'Victor')
    const length = window.history.length
    await user.click(screen.getByRole('button', { name: /Search buses/ }))
    expect(window.history.length).toBe(length)
    await screen.findByRole('button', { name: /Search buses/ }, { timeout: 4000 })
    await searchByName(user, 'Raida')
    expect(window.history.length).toBe(length + 1)

    const asked = searches.length
    await back('/find-a-bus?mode=name&name=Victor')
    expect(await screen.findByText('Victor Classic')).toBeTruthy()
    expect(screen.getByPlaceholderText(/BRTC/).value).toBe('Victor')
    await forward('/find-a-bus?mode=name&name=Raida')
    expect(await screen.findByText('Raida Express')).toBeTruthy()
    expect(screen.getByPlaceholderText(/BRTC/).value).toBe('Raida')
    // Results seen a moment ago come back without asking the server again.
    expect(searches.length).toBe(asked)
  }, 15000)

  it('F: the route page\'s "All bus routes" button steps back like the browser does', async () => {
    const user = userEvent.setup()
    await tab(user, 'Bus routes')
    await user.click(await screen.findByRole('button', { name: /Victor Classic/ }))
    await waitFor(() => expect(here()).toBe('/bus-routes/DBR-12'))
    const length = window.history.length
    await user.click(await screen.findByRole('button', { name: /All bus routes/ }))
    await waitFor(() => expect(here()).toBe('/bus-routes'))
    // It went back (no new entry), so Forward returns to the route, the same as after the browser's Back.
    expect(window.history.length).toBe(length)
    await forward('/bus-routes/DBR-12')
    await back('/bus-routes')
    await back('/')
  })

  it('F: a route page with nothing behind it in the app opens the list as a new page instead', async () => {
    const user = userEvent.setup()
    // As if the route's address had been opened directly: an entry with no in-app history behind it.
    window.history.pushState(null, '', '/bus-routes/DBR-40')
    window.dispatchEvent(new PopStateEvent('popstate'))
    expect(await screen.findByRole('heading', { level: 1, name: 'Raida Express' })).toBeTruthy()
    await user.click(screen.getByRole('button', { name: /All bus routes/ }))
    await waitFor(() => expect(here()).toBe('/bus-routes'))
    await back('/bus-routes/DBR-40')
  })

  it('G: clicking the tab already open adds no history entries', async () => {
    const user = userEvent.setup()
    await tab(user, 'Bus routes')
    const length = window.history.length
    for (let i = 0; i < 4; i++) await tab(user, 'Bus routes')
    await user.click(screen.getByRole('button', { name: 'SmartCommute home' }))
    await tab(user, 'Home')
    await tab(user, 'Home')
    expect(window.history.length).toBe(length + 1)
    await back('/bus-routes')
    await back('/')
  })

  it('links opened from inside the app (notifications) are one normal entry; an unknown one shows Home', async () => {
    openPath('/ride-share')
    await waitFor(() => expect(here()).toBe('/ride-share'))
    expect(await screen.findByRole('heading', { level: 1, name: 'Share the journey.' })).toBeTruthy()
    const length = window.history.length
    openPath('/ride-share')
    expect(window.history.length).toBe(length)
    await back('/')
    openPath('/no-such-page')
    await waitFor(() => expect(here()).toBe('/'))
    expect(window.history.length).toBe(length)
  })

  it('every entry knows its place, so the app can tell when there is an in-app page to go back to', async () => {
    const user = userEvent.setup()
    const start = window.history.state.idx
    await tab(user, 'Bus routes')
    expect(window.history.state).toMatchObject({ idx: start + 1, from: '/' })
    await back('/')
    expect(window.history.state.idx).toBe(start)
  })
})
