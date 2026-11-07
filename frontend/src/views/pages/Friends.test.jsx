import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { FriendsPage, cleanCode, isValidCode } from './Friends'

// A fake backend: each test sets the responses it needs; every call is recorded.
const alice = { _id: 'a1', id: 'a1', name: 'Alice Rahman', friendCode: 'ALC12', bio: '', verified: true, memberSince: '2026-01-02T00:00:00Z' }
const bob = { _id: 'b2', id: 'b2', name: 'Bob Karim', friendCode: 'BOB34', bio: 'Uttara to Motijheel', verified: false, memberSince: '2026-02-03T00:00:00Z' }
let routes
let calls

const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))

beforeEach(() => {
  calls = []
  routes = {
    'GET /auth/friend-code': () => reply(200, { success: true, friendCode: 'ME123' }),
    'GET /friends/requests/incoming': () => reply(200, { success: true, data: [] }),
    'GET /friends/requests/sent': () => reply(200, { success: true, data: [] }),
    'GET /friends': () => reply(200, { success: true, data: [] }),
  }
  vi.stubGlobal('fetch', vi.fn((url, options = {}) => {
    const { pathname, search } = new URL(url)
    const method = options.method || 'GET'
    calls.push({ method, path: pathname, search, body: options.body && JSON.parse(options.body), auth: options.headers?.Authorization })
    const handler = routes[`${method} ${pathname}`]
    return handler ? handler({ search, body: options.body && JSON.parse(options.body) }) : reply(404, { success: false, message: 'Not found' })
  }))
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const renderPage = (props = {}) => {
  const notify = vi.fn()
  const onChat = vi.fn()
  render(<FriendsPage token="test-token" signedIn openAuth={vi.fn()} onChat={onChat} notify={notify} {...props} />)
  return { notify, onChat }
}
const section = (title) => screen.getByRole('heading', { name: new RegExp(`^${title}`) }).closest('article')

describe('friend code helpers', () => {
  it('upper-cases and keeps only five letters or digits', () => {
    expect(cleanCode('r7-k 2m9')).toBe('R7K2M')
    expect(isValidCode('R7K2M')).toBe(true)
    expect(isValidCode('R7K2')).toBe(false)
  })
})

describe('FriendsPage', () => {
  it('shows the friend code and copies it', async () => {
    const user = userEvent.setup()
    // user-event installs its own clipboard in setup(), so the spy goes on afterwards.
    const writeText = vi.spyOn(navigator.clipboard, 'writeText')
    const { notify } = renderPage()

    expect(await screen.findByText('ME123')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: /copy code/i }))
    expect(writeText).toHaveBeenCalledWith('ME123')
    expect(notify).toHaveBeenCalledWith('Friend code copied.')
    expect(screen.getByRole('button', { name: /copied/i })).toBeTruthy()
    expect(calls.every((c) => c.auth === 'Bearer test-token')).toBe(true)
  })

  it('shows empty states when there is nothing yet', async () => {
    renderPage()
    expect(await screen.findByText('No pending requests.')).toBeTruthy()
    expect(screen.getByText('No requests waiting for an answer.')).toBeTruthy()
    expect(screen.getByText(/Your friend list is waiting to grow/)).toBeTruthy()
  })

  it('upper-cases the search box and refuses a malformed code without calling the server', async () => {
    const user = userEvent.setup()
    renderPage()
    const input = screen.getByLabelText("Enter a friend's code")
    await user.type(input, 'ab-1')
    expect(input.value).toBe('AB1')
    await user.click(screen.getByRole('button', { name: 'Search' }))
    expect(screen.getByText('A friend code is exactly 5 letters or digits.')).toBeTruthy()
    expect(calls.some((c) => c.path === '/auth/search-friend')).toBe(false)
  })

  it('shows "User not found" for an unknown code', async () => {
    routes['GET /auth/search-friend'] = () => reply(404, { success: false, message: 'User not found.' })
    const user = userEvent.setup()
    renderPage()
    await user.type(screen.getByLabelText("Enter a friend's code"), 'zzzzz')
    await user.click(screen.getByRole('button', { name: 'Search' }))
    expect(await screen.findByTestId('not-found')).toHaveProperty('textContent', 'User not found')
    expect(calls.find((c) => c.path === '/auth/search-friend').search).toBe('?code=ZZZZZ')
  })

  it('finds a user and sends a friend request with their code', async () => {
    routes['GET /auth/search-friend'] = () => reply(200, { success: true, user: bob, relationship: 'none', requestId: null })
    const request = { _id: 'r1', sender: alice, receiver: bob, status: 'pending', createdAt: '2026-10-27T00:00:00Z' }
    routes['POST /friends/request'] = () => {
      routes['GET /friends/requests/sent'] = () => reply(200, { success: true, data: [request] })
      return reply(201, { success: true, message: 'Friend request sent.', data: request })
    }
    const user = userEvent.setup()
    const { notify } = renderPage()
    await user.type(screen.getByLabelText("Enter a friend's code"), 'bob34')
    await user.click(screen.getByRole('button', { name: 'Search' }))

    const result = await screen.findByTestId('search-result')
    expect(within(result).getByText('Bob Karim')).toBeTruthy()
    expect(within(result).getByText('Uttara to Motijheel')).toBeTruthy()
    await user.click(within(result).getByRole('button', { name: /add friend/i }))

    expect(calls.find((c) => c.method === 'POST' && c.path === '/friends/request').body).toEqual({ friendCode: 'BOB34' })
    expect(notify).toHaveBeenCalledWith('Friend request sent.')
    expect(await within(result).findByText('Request sent')).toBeTruthy()
    expect(await within(section('Sent requests')).findByText('Bob Karim')).toBeTruthy()
  })

  it.each([
    ['self', 'This is your own friend code.'],
    ['friends', 'Already friends'],
    ['request_sent', 'Request sent'],
  ])('shows the "%s" relationship instead of an Add friend button', async (relationship, text) => {
    routes['GET /auth/search-friend'] = () => reply(200, { success: true, user: bob, relationship, requestId: null })
    const user = userEvent.setup()
    renderPage()
    await user.type(screen.getByLabelText("Enter a friend's code"), 'BOB34')
    await user.click(screen.getByRole('button', { name: 'Search' }))
    const result = await screen.findByTestId('search-result')
    expect(within(result).getByText(text)).toBeTruthy()
    expect(within(result).queryByRole('button', { name: /add friend/i })).toBeNull()
  })

  it('accepts an incoming request and moves the sender into the friends list', async () => {
    let accepted = false
    routes['GET /friends/requests/incoming'] = () => reply(200, { success: true, data: accepted ? [] : [{ _id: 'r9', sender: alice, receiver: bob, status: 'pending', createdAt: '2026-10-26T00:00:00Z' }] })
    routes['GET /friends'] = () => reply(200, { success: true, data: accepted ? [{ ...alice, friendsSince: '2026-10-27T00:00:00Z' }] : [] })
    routes['POST /friends/requests/r9/accept'] = () => { accepted = true; return reply(200, { success: true, message: 'Friend request accepted.' }) }
    const user = userEvent.setup()
    const { notify } = renderPage()

    const incoming = section('Friend requests')
    await within(incoming).findByText('Alice Rahman')
    await user.click(within(incoming).getByRole('button', { name: 'Accept request from Alice Rahman' }))

    expect(notify).toHaveBeenCalledWith('Friend request accepted.')
    await waitFor(() => expect(within(incoming).getByText('No pending requests.')).toBeTruthy())
    const friends = section('My friends')
    await waitFor(() => expect(within(friends).getAllByText('Alice Rahman')).toHaveLength(1))
    expect(calls.filter((c) => c.path === '/friends/requests/r9/accept')).toHaveLength(1)
  })

  it('declines an incoming request without adding a friend, and ignores double clicks', async () => {
    let finish
    routes['GET /friends/requests/incoming'] = () => reply(200, { success: true, data: [{ _id: 'r5', sender: alice, receiver: bob, status: 'pending', createdAt: '2026-10-26T00:00:00Z' }] })
    routes['POST /friends/requests/r5/reject'] = () => new Promise((resolve) => { finish = () => resolve(new Response(JSON.stringify({ success: true }), { status: 200 })) })
    const user = userEvent.setup()
    renderPage()

    const incoming = section('Friend requests')
    const decline = await within(incoming).findByRole('button', { name: 'Decline request from Alice Rahman' })
    await user.click(decline)
    expect(decline.disabled).toBe(true)
    await user.click(decline)
    expect(calls.filter((c) => c.path === '/friends/requests/r5/reject')).toHaveLength(1)

    routes['GET /friends/requests/incoming'] = () => reply(200, { success: true, data: [] })
    finish()
    await waitFor(() => expect(within(incoming).getByText('No pending requests.')).toBeTruthy())
    expect(within(section('My friends')).queryByText('Alice Rahman')).toBeNull()
  })

  it('lists pending sent requests and friends, and opens a chat', async () => {
    routes['GET /friends/requests/sent'] = () => reply(200, { success: true, data: [{ _id: 'r2', sender: alice, receiver: bob, status: 'pending', createdAt: '2026-10-25T00:00:00Z' }] })
    routes['GET /friends'] = () => reply(200, { success: true, data: [{ ...alice, friendsSince: '2026-10-20T00:00:00Z' }, { ...alice, friendsSince: '2026-10-20T00:00:00Z' }] })
    const user = userEvent.setup()
    const { onChat } = renderPage()

    const sent = section('Sent requests')
    expect(await within(sent).findByText('Bob Karim')).toBeTruthy()
    expect(within(sent).getByText('Pending')).toBeTruthy()
    const friends = section('My friends')
    await within(friends).findByText('Alice Rahman')
    expect(within(friends).getAllByText('Alice Rahman')).toHaveLength(1)
    await user.click(within(friends).getByRole('button', { name: 'Message Alice Rahman' }))
    expect(onChat).toHaveBeenCalledWith(expect.objectContaining({ id: 'a1' }))
  })

  it('shows an error and a retry button when the server cannot be reached', async () => {
    fetch.mockImplementation(() => Promise.reject(new TypeError('Failed to fetch')))
    renderPage()
    expect(await screen.findByText(/Cannot reach the SmartCommute server/)).toBeTruthy()
    expect(screen.getByRole('button', { name: /try again/i })).toBeTruthy()
  })

  it('asks visitors to sign in instead of loading anything', () => {
    renderPage({ signedIn: false, token: null })
    expect(screen.getByText('Sign in to continue')).toBeTruthy()
    expect(calls).toHaveLength(0)
  })
})
