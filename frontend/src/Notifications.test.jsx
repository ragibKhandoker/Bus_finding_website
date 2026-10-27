import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, render, renderHook, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { NOTIFICATIONS_CHANGED, NotificationBell, NotificationsPage, SosAlert, badgeText, useUnreadCount } from './Notifications'

const me = { id: 'me', name: 'Rafi Ahmed' }
const note = (id, title, extra = {}) => ({ _id: id, title, message: `${title} message`, type: 'info', category: 'general', isRead: false, actionUrl: null, createdAt: '2026-10-27T09:00:00Z', ...extra })

let routes
let calls
const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))

beforeEach(() => {
  calls = []
  routes = {}
  vi.stubGlobal('fetch', vi.fn((url, options = {}) => {
    const { pathname } = new URL(url)
    const method = options.method || 'GET'
    const body = options.body && JSON.parse(options.body)
    calls.push({ method, path: pathname, body, auth: options.headers?.Authorization })
    const handler = routes[`${method} ${pathname}`]
    return handler ? handler({ body }) : reply(404, { success: false, message: 'Notification not found.' })
  }))
  vi.spyOn(window, 'confirm').mockReturnValue(true)
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('unread badge', () => {
  it('shows a compact count and hides at zero', () => {
    expect(badgeText(7)).toBe('7')
    expect(badgeText(150)).toBe('99+')
    const { rerender } = render(<NotificationBell count={0} onOpen={() => {}} />)
    expect(screen.queryByTestId('unread-badge')).toBeNull()
    rerender(<NotificationBell count={12} onOpen={() => {}} />)
    expect(screen.getByTestId('unread-badge').textContent).toBe('12')
    expect(screen.getByRole('button', { name: 'Notifications, 12 unread' })).toBeTruthy()
  })

  it('loads the count and refreshes it when notifications change', async () => {
    let count = 4
    routes['GET /api/notifications/unread/me'] = () => reply(200, { success: true, count })
    const { result } = renderHook(() => useUnreadCount({ token: 'tok', user: me, signedIn: true }))
    await waitFor(() => expect(result.current).toBe(4))
    expect(calls[0].auth).toBe('Bearer tok')
    count = 1
    act(() => { window.dispatchEvent(new Event(NOTIFICATIONS_CHANGED)) })
    await waitFor(() => expect(result.current).toBe(1))
  })

  it('stays at zero and asks nothing when signed out', () => {
    const { result } = renderHook(() => useUnreadCount({ token: null, user: null, signedIn: false }))
    expect(result.current).toBe(0)
    expect(calls).toHaveLength(0)
  })
})

describe('NotificationsPage', () => {
  const list = (items, unreadCount) => () => reply(200, { success: true, total: items.length, totalPages: 1, unreadCount, data: items })
  const renderPage = () => {
    const notify = vi.fn()
    render(<NotificationsPage token="tok" user={me} signedIn openAuth={vi.fn()} notify={notify} />)
    return { notify }
  }
  const row = async (title) => (await screen.findByText(title)).closest('[data-testid="notification"]')

  beforeEach(() => {
    routes['GET /api/notifications/user/me'] = list([
      note('n1', 'New friend request', { category: 'friend', actionUrl: '/friends' }),
      note('n2', '🚨 SOS from Nadia', { category: 'sos', type: 'sos', actionUrl: 'https://www.google.com/maps?q=23.810332,90.412518' }),
      note('n3', 'You earned gems', { category: 'reward', isRead: true }),
      note('n4', 'Odd link', { actionUrl: 'javascript:alert(1)' }),
    ], 3)
  })

  it('shows notifications with unread state and a safe map link for SOS', async () => {
    renderPage()
    expect((await row('New friend request')).className).toContain('unread')
    expect((await row('You earned gems')).className).not.toContain('unread')
    expect(screen.getByTestId('page-unread').textContent).toBe('3 unread')
    const map = within(await row('🚨 SOS from Nadia')).getByRole('link', { name: /Open location in Google Maps/ })
    expect(map.getAttribute('href')).toBe('https://www.google.com/maps?q=23.810332,90.412518')
    expect(map.getAttribute('target')).toBe('_blank')
    expect(map.getAttribute('rel')).toContain('noopener')
    expect(within(await row('Odd link')).queryByRole('link')).toBeNull()
    expect(within(await row('Odd link')).queryByRole('button', { name: 'Open' })).toBeNull()
  })

  it('marks one as read and tells the bell', async () => {
    routes['PATCH /api/notifications/read/n1'] = () => reply(200, { success: true, unreadCount: 2 })
    const changed = vi.fn()
    window.addEventListener(NOTIFICATIONS_CHANGED, changed)
    const user = userEvent.setup()
    renderPage()
    const item = await row('New friend request')
    await user.click(within(item).getByRole('button', { name: 'Mark "New friend request" as read' }))
    await waitFor(() => expect(item.className).not.toContain('unread'))
    expect(screen.getByTestId('page-unread').textContent).toBe('2 unread')
    expect(changed).toHaveBeenCalled()
    window.removeEventListener(NOTIFICATIONS_CHANGED, changed)
  })

  it('deletes one notification only after the server agrees', async () => {
    routes['DELETE /api/notifications/n2'] = () => reply(500, { success: false, message: 'Something went wrong on the server.' })
    const user = userEvent.setup()
    renderPage()
    await user.click(within(await row('🚨 SOS from Nadia')).getByRole('button', { name: 'Delete "🚨 SOS from Nadia"' }))
    expect(await screen.findByText('Something went wrong on the server.')).toBeTruthy()
    expect(screen.getByText('🚨 SOS from Nadia')).toBeTruthy()

    routes['DELETE /api/notifications/n2'] = () => reply(200, { success: true, unreadCount: 2 })
    await user.click(within(await row('🚨 SOS from Nadia')).getByRole('button', { name: 'Delete "🚨 SOS from Nadia"' }))
    await waitFor(() => expect(screen.queryByText('🚨 SOS from Nadia')).toBeNull())
    expect(screen.getByTestId('page-unread').textContent).toBe('2 unread')
  })

  it('marks all as read', async () => {
    routes['PATCH /api/notifications/read-all/me'] = () => reply(200, { success: true, unreadCount: 0 })
    const user = userEvent.setup()
    const { notify } = renderPage()
    await user.click(await screen.findByRole('button', { name: /Mark all read/ }))
    await waitFor(() => expect(screen.getByTestId('page-unread').textContent).toBe('0 unread'))
    expect(screen.getAllByTestId('notification').every((r) => !r.className.includes('unread'))).toBe(true)
    expect(notify).toHaveBeenCalledWith('Notifications marked as read.')
    expect(screen.getByRole('button', { name: /Mark all read/ }).disabled).toBe(true)
  })

  it('clears everything only after confirming', async () => {
    routes['DELETE /api/notifications/clear/me'] = () => reply(200, { success: true, unreadCount: 0 })
    const user = userEvent.setup()
    renderPage()
    window.confirm.mockReturnValueOnce(false)
    await user.click(await screen.findByRole('button', { name: /Clear all/ }))
    expect(calls.some((c) => c.method === 'DELETE')).toBe(false)
    await user.click(screen.getByRole('button', { name: /Clear all/ }))
    expect(await screen.findByText('You are all caught up.')).toBeTruthy()
    expect(calls.filter((c) => c.path === '/api/notifications/clear/me')).toHaveLength(1)
  })

  it('shows loading, then an error with a retry', async () => {
    routes['GET /api/notifications/user/me'] = () => reply(500, { success: false, message: 'Something went wrong on the server.' })
    const user = userEvent.setup()
    renderPage()
    expect(screen.getByText('Loading notifications…')).toBeTruthy()
    expect(await screen.findByText('Could not load your notifications.')).toBeTruthy()
    routes['GET /api/notifications/user/me'] = list([], 0)
    await user.click(screen.getByRole('button', { name: /Try again/ }))
    expect(await screen.findByText('You are all caught up.')).toBeTruthy()
  })
})

describe('SosAlert', () => {
  const geo = (impl) => Object.defineProperty(navigator, 'geolocation', { value: impl ? { getCurrentPosition: impl } : undefined, configurable: true })
  const renderSos = (props = {}) => {
    const notify = vi.fn()
    const openAuth = vi.fn()
    render(<SosAlert token="tok" signedIn notify={notify} openAuth={openAuth} {...props} />)
    return { notify, openAuth }
  }

  it('asks for confirmation, then sends the real location', async () => {
    geo((ok) => ok({ coords: { latitude: 23.8103321, longitude: 90.4125181 } }))
    routes['POST /api/notifications/sos'] = () => reply(201, { success: true, count: 3 })
    const user = userEvent.setup()
    const { notify } = renderSos()
    await user.click(screen.getByRole('button', { name: /Send SOS/ }))
    expect(calls).toHaveLength(0)
    await user.click(screen.getByRole('radio', { name: 'Medical' }))
    await user.type(screen.getByRole('textbox'), 'Near Farmgate')
    await user.click(screen.getByRole('button', { name: 'Yes, send SOS now' }))

    expect(await screen.findByText('SOS sent to 3 people.')).toBeTruthy()
    expect(calls[0].body).toEqual({ latitude: 23.8103321, longitude: 90.4125181, serviceName: 'Medical', message: 'Near Farmgate' })
    expect(calls[0].auth).toBe('Bearer tok')
    expect(notify).toHaveBeenCalledWith('SOS sent to 3 people.')
  })

  it.each([
    [1, 'Location permission was denied'],
    [2, 'Your location could not be found'],
    [3, 'Finding your location took too long'],
  ])('sends nothing when location fails (code %i) and lets you retry', async (code, text) => {
    geo((_ok, fail) => fail({ code }))
    const user = userEvent.setup()
    renderSos()
    await user.click(screen.getByRole('button', { name: /Send SOS/ }))
    await user.click(screen.getByRole('button', { name: 'Yes, send SOS now' }))
    expect((await screen.findByRole('alert')).textContent).toContain(text)
    expect(calls).toHaveLength(0)
    expect(screen.getByRole('button', { name: 'Try again' })).toBeTruthy()
  })

  it('explains when the browser has no location support', async () => {
    geo(null)
    const user = userEvent.setup()
    renderSos()
    await user.click(screen.getByRole('button', { name: /Send SOS/ }))
    await user.click(screen.getByRole('button', { name: 'Yes, send SOS now' }))
    expect((await screen.findByRole('alert')).textContent).toBe('This browser cannot share your location, so no alert was sent.')
    expect(calls).toHaveLength(0)
  })

  it('shows the server refusal instead of success, and sends once while busy', async () => {
    geo((ok) => ok({ coords: { latitude: 23.8, longitude: 90.4 } }))
    let finish
    routes['POST /api/notifications/sos'] = () => new Promise((resolve) => { finish = () => resolve(new Response(JSON.stringify({ success: false, message: 'No alert was sent: SOS goes to your friends and the people on your open ride shares, and you have none yet. Add friends first, and call 999 in an emergency.' }), { status: 409 })) })
    const user = userEvent.setup()
    const { notify } = renderSos()
    await user.click(screen.getByRole('button', { name: /Send SOS/ }))
    await user.click(screen.getByRole('button', { name: 'Yes, send SOS now' }))
    const sending = await screen.findByRole('button', { name: 'Sending SOS…' })
    expect(sending.disabled).toBe(true)
    await user.click(sending)
    finish()
    expect((await screen.findByRole('alert')).textContent).toContain('No alert was sent')
    expect(calls.filter((c) => c.method === 'POST')).toHaveLength(1)
    expect(notify).not.toHaveBeenCalled()
  })

  it('sends visitors to sign in', async () => {
    const user = userEvent.setup()
    const { openAuth } = renderSos({ signedIn: false, token: null })
    await user.click(screen.getByRole('button', { name: /Send SOS/ }))
    expect(openAuth).toHaveBeenCalled()
    expect(screen.queryByRole('button', { name: 'Yes, send SOS now' })).toBeNull()
  })
})
