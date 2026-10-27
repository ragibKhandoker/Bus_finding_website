import { beforeAll, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

// Boots the whole app (main.jsx renders into #root) as a signed-in user and opens Friends from the More menu.
const base64Url = (value) => btoa(JSON.stringify(value)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_')
const token = `${base64Url({ alg: 'HS256' })}.${base64Url({ exp: Math.floor(Date.now() / 1000) + 3600 })}.sig`
const friend = { _id: 'f1', id: 'f1', name: 'Nadia Islam', friendCode: 'NAD12', friendsSince: '2026-10-01T00:00:00Z' }
const me = { id: 'u1', _id: 'u1', name: 'Test Rider', email: 'rider@test.local', role: 'commuter', friendCode: 'ME123' }

beforeAll(async () => {
  document.body.innerHTML = '<div id="root"></div>'
  localStorage.setItem('sc_token', token)
  localStorage.setItem('sc_user', JSON.stringify(me))
  window.scrollTo = () => {}
  vi.stubGlobal('fetch', vi.fn((url) => {
    const { pathname } = new URL(url)
    const responses = {
      '/auth/validate': { success: true, valid: true, user: me },
      '/auth/friend-code': { success: true, friendCode: 'ME123' },
      '/friends': { success: true, data: [friend] },
      '/api/chat/unread-counts': { success: true, total: 3, unreadCounts: { f1: 3 }, conversations: [] },
      '/api/chat': { success: true, chats: [] },
      '/api/notifications/unread/u1': { success: true, count: 3 },
      '/api/notifications/user/u1': { success: true, total: 0, totalPages: 1, unreadCount: 3, data: [] },
      '/api/chat/f1': { success: true, created: false, chat: { _id: 'c9', otherParticipant: friend, hasMoreMessages: false, messages: [{ _id: 'm1', senderId: 'f1', senderName: 'Nadia Islam', text: 'Hi Rider!', content: 'Hi Rider!', read: true, createdAt: '2026-10-27T10:00:00Z' }] } },
    }
    const body = responses[pathname] || { success: true, data: [] }
    return Promise.resolve(new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }))
  }))
  await import('./main.jsx')
  await waitFor(() => expect(document.querySelector('nav.nav-links')).not.toBeNull())
})

const openFriendsFromMore = async (user) => {
  const nav = document.querySelector('nav.nav-links')
  await user.click(within(nav).getByRole('button', { name: 'More' }))
  await user.click(within(nav.querySelector('.more-nav-menu')).getByRole('button', { name: 'Friends' }))
}

describe('More → Friends navigation', () => {
  it('opens the Friends page from the desktop More menu', async () => {
    const user = userEvent.setup()
    await openFriendsFromMore(user)
    expect(window.location.pathname).toBe('/friends')
    expect(await screen.findByRole('heading', { level: 1, name: 'Friends' })).toBeTruthy()
    expect(await screen.findByTestId('my-friend-code')).toHaveProperty('textContent', 'ME123')
  })

  it('opens the Friends page from the mobile menu', async () => {
    const user = userEvent.setup()
    window.history.pushState({}, '', '/')
    window.dispatchEvent(new PopStateEvent('popstate'))
    await user.click(screen.getByRole('button', { name: 'Toggle navigation' }))
    expect(document.querySelector('nav.nav-links.open')).not.toBeNull()
    await openFriendsFromMore(user)
    expect(window.location.pathname).toBe('/friends')
    expect(document.querySelector('nav.nav-links.open')).toBeNull()
    expect(await screen.findByRole('heading', { level: 1, name: 'Friends' })).toBeTruthy()
  })

  it('opens the chat with a friend from the Friends page, showing their unread count first', async () => {
    const user = userEvent.setup()
    await openFriendsFromMore(user)
    const message = await screen.findByRole('button', { name: 'Message Nadia Islam, 3 unread' })
    expect(within(message).getByTestId('friend-unread').textContent).toBe('3')
    await user.click(message)
    expect(window.location.pathname).toBe('/messages')
    expect(await screen.findByText('Hi Rider!')).toBeTruthy()
    expect(document.querySelector('.chat-title').textContent).toContain('Nadia Islam')
    expect(window.location.search).toBe('?chat=c9')
  })

  it('opens Ride share from the navigation bar on desktop and on mobile', async () => {
    const user = userEvent.setup()
    const nav = document.querySelector('nav.nav-links')
    await user.click(within(nav).getByRole('button', { name: 'Ride share' }))
    expect(window.location.pathname).toBe('/ride-share')
    expect(await screen.findByRole('heading', { level: 1, name: 'Share the journey.' })).toBeTruthy()

    window.history.pushState({}, '', '/')
    window.dispatchEvent(new PopStateEvent('popstate'))
    await user.click(screen.getByRole('button', { name: 'Toggle navigation' }))
    await user.click(within(document.querySelector('nav.nav-links.open')).getByRole('button', { name: 'Ride share' }))
    expect(window.location.pathname).toBe('/ride-share')
    expect(document.querySelector('nav.nav-links.open')).toBeNull()
    expect(await screen.findByRole('tab', { name: /Open rides/ })).toBeTruthy()
  })

  it('shows the unread count on the bell, which opens Notifications', async () => {
    const user = userEvent.setup()
    const bell = await screen.findByRole('button', { name: 'Notifications, 3 unread' })
    expect(within(bell).getByTestId('unread-badge').textContent).toBe('3')
    await user.click(bell)
    expect(window.location.pathname).toBe('/notifications')
    expect(await screen.findByRole('heading', { level: 1, name: 'Notifications' })).toBeTruthy()
  })
})
