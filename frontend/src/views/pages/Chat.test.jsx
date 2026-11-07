import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ChatPage, mergeMessages } from './Chat'

const me = { id: 'me', name: 'Rafi Ahmed' }
const nadia = { _id: 'u-nadia', id: 'u-nadia', name: 'Nadia Islam', friendCode: 'NAD12', verified: true }
const tanvir = { _id: 'u-tanvir', id: 'u-tanvir', name: 'Tanvir Hasan', friendCode: 'TAN34' }
const msg = (id, senderId, text, minute, extra = {}) => ({ _id: id, chatId: 'c1', senderId, senderName: senderId === 'me' ? me.name : nadia.name, text, content: text, read: false, createdAt: `2026-10-27T10:${String(minute).padStart(2, '0')}:00Z`, ...extra })
const chatList = () => [
  { _id: 'c1', otherParticipant: nadia, lastMessage: msg('m3', 'u-nadia', 'See you at Farmgate', 3), unreadCount: 2, lastMessageAt: '2026-10-27T10:03:00Z' },
  { _id: 'c2', otherParticipant: tanvir, lastMessage: { ...msg('t1', 'me', 'Thanks!', 1), chatId: 'c2' }, unreadCount: 0, lastMessageAt: '2026-10-27T09:01:00Z' },
]

let routes
let calls
const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))

beforeEach(() => {
  calls = []
  window.history.replaceState({}, '', '/messages')
  routes = {
    'GET /api/chat': () => reply(200, { success: true, chats: chatList() }),
    'GET /api/chat/messages/c1': () => reply(200, { success: true, hasMore: false, messages: [msg('m1', 'me', 'Are you on the bus?', 1), msg('m2', 'u-nadia', 'Yes, near Shahbag', 2), msg('m3', 'u-nadia', 'See you at Farmgate', 3)] }),
    'POST /api/chat/c1/read': () => reply(200, { success: true, marked: 2 }),
  }
  vi.stubGlobal('fetch', vi.fn((url, options = {}) => {
    const { pathname, search } = new URL(url)
    const method = options.method || 'GET'
    const body = options.body && JSON.parse(options.body)
    calls.push({ method, path: pathname, search, body })
    const handler = routes[`${method} ${pathname}`]
    return handler ? handler({ search, body }) : reply(404, { success: false, message: 'Chat not found.' })
  }))
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const renderChat = (props = {}) => {
  const onFindFriends = vi.fn()
  render(<ChatPage token="tok" user={me} signedIn openAuth={vi.fn()} onFindFriends={onFindFriends} {...props} />)
  return { onFindFriends }
}
const sidebar = () => screen.getByRole('complementary', { name: 'Conversations' })
const openNadia = async (user) => {
  await user.click(await within(sidebar()).findByRole('button', { name: /Nadia Islam/ }))
  return screen.findByRole('log')
}

describe('mergeMessages', () => {
  it('keeps each message once, oldest first', () => {
    const merged = mergeMessages([msg('b', 'me', 'two', 2), msg('a', 'me', 'one', 1)], [msg('b', 'me', 'two', 2), msg('c', 'me', 'three', 3)])
    expect(merged.map((m) => m._id)).toEqual(['a', 'b', 'c'])
  })
})

describe('ChatPage', () => {
  it('lists conversations with previews, times and unread counts', async () => {
    renderChat()
    const list = sidebar()
    expect(await within(list).findByText('Nadia Islam')).toBeTruthy()
    expect(within(list).getByText('See you at Farmgate')).toBeTruthy()
    expect(within(list).getByText('You: Thanks!')).toBeTruthy()
    const rows = within(list).getAllByRole('button', { name: /Islam|Hasan/ })
    expect(rows.map((r) => r.textContent)).toEqual([expect.stringContaining('Nadia'), expect.stringContaining('Tanvir')])
    expect(within(list).getByTestId('unread-badge').textContent).toBe('2')
    expect(screen.getByTestId('total-unread').textContent).toBe('2 unread')
  })

  it('shows loading, then an empty state that points to Friends', async () => {
    routes['GET /api/chat'] = () => reply(200, { success: true, chats: [] })
    const user = userEvent.setup()
    const { onFindFriends } = renderChat()
    expect(screen.getByText('Loading…')).toBeTruthy()
    expect(await screen.findByText(/No conversations yet/)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: /Message a friend/ }))
    expect(onFindFriends).toHaveBeenCalled()
  })

  it('opens a conversation oldest first with each sender named, then marks it read on the server', async () => {
    routes['POST /api/chat/c1/read'] = () => {
      routes['GET /api/chat'] = () => reply(200, { success: true, chats: chatList().map((c) => ({ ...c, unreadCount: 0 })) })
      return reply(200, { success: true, marked: 2 })
    }
    const user = userEvent.setup()
    renderChat()
    const log = await openNadia(user)
    await within(log).findByText('See you at Farmgate')

    const bubbles = within(log).getAllByTestId('message')
    expect(bubbles.map((b) => b.querySelector('.message-text').textContent)).toEqual(['Are you on the bus?', 'Yes, near Shahbag', 'See you at Farmgate'])
    expect(within(bubbles[0]).getByText('You')).toBeTruthy()
    expect(bubbles[0].className).toContain('mine')
    expect(within(bubbles[1]).getByText('Nadia Islam')).toBeTruthy()
    expect(bubbles[1].className).not.toContain('mine')

    await waitFor(() => expect(calls.some((c) => c.method === 'POST' && c.path === '/api/chat/c1/read')).toBe(true))
    expect(calls.find((c) => c.path === '/api/chat/c1/read').search).toBe('?upTo=m3')
    await waitFor(() => expect(within(sidebar()).queryByTestId('unread-badge')).toBeNull())
    expect(window.location.search).toBe('?chat=c1')
  })

  it('keeps the unread count when the server does not confirm the read', async () => {
    routes['POST /api/chat/c1/read'] = () => reply(500, { success: false, message: 'Something went wrong on the server.' })
    const user = userEvent.setup()
    renderChat()
    await openNadia(user)
    await waitFor(() => expect(calls.some((c) => c.path === '/api/chat/c1/read')).toBe(true))
    expect(within(sidebar()).getByTestId('unread-badge').textContent).toBe('2')
  })

  it('sends a trimmed message with Enter and shows it', async () => {
    routes['POST /api/chat/send'] = ({ body }) => reply(201, { success: true, message: msg('m4', 'me', body.content, 4) })
    const user = userEvent.setup()
    renderChat()
    const log = await openNadia(user)
    await within(log).findByText('See you at Farmgate')

    const box = screen.getByLabelText('Write a message…')
    await user.type(box, '  On my way  {Enter}')
    await waitFor(() => expect(calls.some((c) => c.path === '/api/chat/send')).toBe(true))
    expect(calls.find((c) => c.path === '/api/chat/send').body).toEqual({ chatId: 'c1', content: 'On my way' })
    expect(await within(log).findByText('On my way')).toBeTruthy()
    expect(box.value).toBe('')
  })

  it('never sends an empty or whitespace-only message, and Shift+Enter adds a line instead', async () => {
    const user = userEvent.setup()
    renderChat()
    await openNadia(user)
    const box = await screen.findByLabelText('Write a message…')
    await waitFor(() => expect(box.disabled).toBe(false))
    const sendButton = screen.getByRole('button', { name: /^Send$/ })

    expect(sendButton.disabled).toBe(true)
    await user.type(box, '   ')
    expect(sendButton.disabled).toBe(true)
    await user.type(box, '{Enter}')
    expect(screen.getByRole('alert').textContent).toBe('Message cannot be empty.')
    await user.clear(box)
    await user.type(box, 'line one{Shift>}{Enter}{/Shift}line two')
    expect(box.value).toBe('line one\nline two')
    expect(calls.some((c) => c.path === '/api/chat/send')).toBe(false)
  })

  it('shows the server error when sending fails and keeps the text', async () => {
    routes['POST /api/chat/send'] = () => reply(403, { success: false, message: 'You can only message your friends.' })
    const user = userEvent.setup()
    renderChat()
    await openNadia(user)
    const box = await screen.findByLabelText('Write a message…')
    await waitFor(() => expect(box.disabled).toBe(false))
    await user.type(box, 'hello')
    await user.click(screen.getByRole('button', { name: /^Send$/ }))
    expect((await screen.findByRole('alert')).textContent).toBe('You can only message your friends.')
    expect(box.value).toBe('hello')
  })

  it('renders message text as text, not HTML', async () => {
    routes['GET /api/chat/messages/c1'] = () => reply(200, { success: true, hasMore: false, messages: [msg('x', 'u-nadia', '<img src=x onerror="alert(1)">', 1)] })
    const user = userEvent.setup()
    renderChat()
    const log = await openNadia(user)
    expect(await within(log).findByText('<img src=x onerror="alert(1)">')).toBeTruthy()
    expect(log.querySelector('img')).toBeNull()
  })

  it('loads older messages above without duplicates', async () => {
    routes['GET /api/chat/messages/c1'] = ({ search }) => search.includes('before=m2')
      ? reply(200, { success: true, hasMore: false, messages: [msg('m1', 'me', 'first', 1), msg('m2', 'u-nadia', 'second', 2)] })
      : reply(200, { success: true, hasMore: true, messages: [msg('m2', 'u-nadia', 'second', 2), msg('m3', 'u-nadia', 'third', 3)] })
    const user = userEvent.setup()
    renderChat()
    const log = await openNadia(user)
    await user.click(await within(log).findByRole('button', { name: 'Load older messages' }))
    await within(log).findByText('first')
    expect(within(log).getAllByTestId('message').map((b) => b.querySelector('.message-text').textContent)).toEqual(['first', 'second', 'third'])
    expect(within(log).queryByRole('button', { name: 'Load older messages' })).toBeNull()
  })

  it('opens the conversation the Friends page asked for, reusing what the server returns', async () => {
    window.history.replaceState({}, '', '/messages?with=u-nadia')
    routes['GET /api/chat/u-nadia'] = () => reply(200, { success: true, created: false, chat: { _id: 'c1', otherParticipant: nadia, hasMoreMessages: false, messages: [msg('m1', 'u-nadia', 'Hi again', 1, { read: true })] } })
    renderChat()
    const log = await screen.findByRole('log')
    expect(await within(log).findByText('Hi again')).toBeTruthy()
    expect(document.querySelector('.chat-title').textContent).toContain('Nadia Islam')
    await waitFor(() => expect(window.location.search).toBe('?chat=c1'))
  })

  it('shows why a conversation could not be opened', async () => {
    window.history.replaceState({}, '', '/messages?with=stranger')
    routes['GET /api/chat/stranger'] = () => reply(403, { success: false, message: 'You can only chat with your friends.' })
    renderChat()
    expect(await screen.findByText('You can only chat with your friends.')).toBeTruthy()
    expect(screen.queryByRole('log')).toBeNull()
  })

  it('reopens the saved conversation after a browser refresh', async () => {
    window.history.replaceState({}, '', '/messages?chat=c1')
    renderChat()
    const log = await screen.findByRole('log')
    expect(await within(log).findByText('Yes, near Shahbag')).toBeTruthy()
  })

  it('goes back from a conversation to the list (the phone layout)', async () => {
    const user = userEvent.setup()
    renderChat()
    await openNadia(user)
    const layout = document.querySelector('.chat-layout')
    expect(layout.classList.contains('has-chat')).toBe(true)
    await user.click(screen.getByRole('button', { name: 'Back to conversations' }))
    expect(layout.classList.contains('has-chat')).toBe(false)
    expect(screen.queryByRole('log')).toBeNull()
    expect(window.location.search).toBe('')
  })

  it('shows an error with a retry when conversations cannot be loaded', async () => {
    routes['GET /api/chat'] = () => reply(500, { success: false, message: 'Something went wrong on the server.' })
    const user = userEvent.setup()
    renderChat()
    expect(await screen.findByText('Could not load your conversations.')).toBeTruthy()
    routes['GET /api/chat'] = () => reply(200, { success: true, chats: chatList() })
    await user.click(within(sidebar()).getByRole('button', { name: /Try again/ }))
    expect(await within(sidebar()).findByText('Nadia Islam')).toBeTruthy()
  })
})
