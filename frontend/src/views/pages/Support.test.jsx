import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AdminSupportInbox, ForcePasswordModal, ForgotPasswordPanel, PasswordRequestsCard, RESET_STORAGE_KEY, SupportPage } from './Support'

let routes
let calls
const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))
const message = (id, text, extra = {}) => ({ _id: id, userId: 'u1', senderId: 'u1', senderName: 'Rafi', fromAdmin: false, text, emergency: false, createdAt: '2026-11-08T09:00:00Z', readAt: null, ...extra })

beforeEach(() => {
  calls = []
  routes = {}
  localStorage.clear()
  vi.stubGlobal('fetch', vi.fn((url, options = {}) => {
    const { pathname } = new URL(url)
    const method = options.method || 'GET'
    const body = options.body && JSON.parse(options.body)
    calls.push({ method, path: pathname, body, auth: options.headers?.Authorization })
    const handler = routes[`${method} ${pathname}`]
    return handler ? handler({ body }) : reply(404, { success: false, message: 'Request not found.' })
  }))
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('forgot password', () => {
  it('sends a request, remembers it, and after approval the user collects a temporary password themselves', async () => {
    const user = userEvent.setup()
    let status = { _id: 'r1', status: 'pending', email: 'rafi@test.local', createdAt: '2026-11-08T09:00:00Z' }
    routes['POST /auth/forgot-password'] = () => reply(201, { success: true, requestId: 'r1', secret: 's3cret', request: status })
    routes['POST /auth/forgot-password/status'] = () => reply(200, { success: true, request: status })
    const onUsePassword = vi.fn()
    const { unmount } = render(<ForgotPasswordPanel initialEmail="rafi@test.local" onUsePassword={onUsePassword} onBack={() => {}} />)

    await user.type(screen.getByPlaceholderText(/I cannot remember/), 'New phone')
    await user.click(screen.getByRole('button', { name: /Send request to admin/ }))
    expect(await screen.findByText('Request sent to the admin')).toBeTruthy()
    expect(calls.find((c) => c.path === '/auth/forgot-password').body).toEqual({ email: 'rafi@test.local', message: 'New phone' })
    expect(JSON.parse(localStorage.getItem(RESET_STORAGE_KEY))).toEqual({ requestId: 'r1', secret: 's3cret', email: 'rafi@test.local' })

    // Coming back later: the saved request is checked with its secret. Approved, but no password is sent with
    // the status: the user asks for it, and it is never saved in the browser.
    unmount()
    status = { ...status, status: 'approved', expiresAt: '2026-11-09T09:00:00Z' }
    routes['POST /auth/forgot-password/reveal'] = () => reply(200, { success: true, temporaryPassword: 'abcDEF2345', expiresAt: '2026-11-09T09:00:00Z' })
    render(<ForgotPasswordPanel onUsePassword={onUsePassword} onBack={() => {}} />)
    expect(await screen.findByText('The admin approved your request')).toBeTruthy()
    expect(screen.queryByTestId('temporary-password')).toBeNull()
    expect(calls.filter((c) => c.path === '/auth/forgot-password/status').at(-1).body).toEqual({ requestId: 'r1', secret: 's3cret' })

    await user.click(screen.getByRole('button', { name: /Show my temporary password/ }))
    expect((await screen.findByTestId('temporary-password')).textContent).toBe('abcDEF2345')
    expect(calls.find((c) => c.path === '/auth/forgot-password/reveal').body).toEqual({ requestId: 'r1', secret: 's3cret' })
    expect(JSON.stringify({ ...localStorage })).not.toContain('abcDEF2345')
    await user.click(screen.getByRole('button', { name: 'Sign in with this password' }))
    expect(onUsePassword).toHaveBeenCalledWith('rafi@test.local', 'abcDEF2345')
  })

  it('shows the reason when the admin dismisses the request and lets the user ask again', async () => {
    const user = userEvent.setup()
    localStorage.setItem(RESET_STORAGE_KEY, JSON.stringify({ requestId: 'r1', secret: 's', email: 'a@test.local' }))
    routes['POST /auth/forgot-password/status'] = () => reply(200, { success: true, request: { _id: 'r1', status: 'dismissed', adminNote: 'Visit the office' } })
    render(<ForgotPasswordPanel onUsePassword={() => {}} onBack={() => {}} />)
    expect(await screen.findByText('Reason: Visit the office')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Send a new request' }))
    expect(localStorage.getItem(RESET_STORAGE_KEY)).toBeNull()
    expect(screen.getByRole('button', { name: /Send request to admin/ })).toBeTruthy()
  })

  it('checks the email before sending', async () => {
    const user = userEvent.setup()
    render(<ForgotPasswordPanel onUsePassword={() => {}} onBack={() => {}} />)
    await user.click(screen.getByRole('button', { name: /Send request to admin/ }))
    expect(screen.getByRole('alert').textContent).toBe('Please enter a valid email address.')
    expect(calls).toHaveLength(0)
  })
})

describe('new password after a temporary one', () => {
  it('saves a new password that meets every rule', async () => {
    const user = userEvent.setup()
    routes['PUT /auth/password'] = () => reply(200, { success: true, user: { _id: 'u1', email: 'a@test.local', mustChangePassword: false } })
    const onDone = vi.fn()
    render(<ForcePasswordModal token="tok" user={{ email: 'a@test.local', mustChangePassword: true }} initialPassword="abcDEF2345" onDone={onDone} onSignOut={() => {}} />)

    await user.type(screen.getByPlaceholderText('Create a password'), 'abcDEF2345')
    await user.type(screen.getByPlaceholderText('Type the same password again'), 'abcDEF2345')
    await user.click(screen.getByRole('button', { name: 'Save new password' }))
    expect(screen.getByRole('alert').textContent).toBe('Please choose a password that meets every rule.')

    await user.clear(screen.getByPlaceholderText('Create a password'))
    await user.clear(screen.getByPlaceholderText('Type the same password again'))
    await user.type(screen.getByPlaceholderText('Create a password'), 'mynew123')
    await user.type(screen.getByPlaceholderText('Type the same password again'), 'mynew123')
    await user.click(screen.getByRole('button', { name: 'Save new password' }))
    await waitFor(() => expect(onDone).toHaveBeenCalled())
    expect(calls[0]).toMatchObject({ method: 'PUT', path: '/auth/password', body: { currentPassword: 'abcDEF2345', newPassword: 'mynew123' }, auth: 'Bearer tok' })
  })
})

describe('contact admin', () => {
  it('shows the conversation and sends an emergency message', async () => {
    const user = userEvent.setup()
    routes['GET /support'] = () => reply(200, { success: true, unread: 1, messages: [message('m1', 'Hello', { fromAdmin: true, senderName: 'Admin' })] })
    routes['POST /support/read'] = () => reply(200, { success: true, marked: 1 })
    routes['POST /support/messages'] = ({ body }) => reply(201, { success: true, message: message('m2', body.text, { emergency: body.emergency }) })
    render(<SupportPage token="tok" user={{ _id: 'u1', role: 'commuter' }} signedIn openAuth={() => {}} navigate={() => {}} />)

    expect((await screen.findAllByTestId('support-message'))[0].textContent).toContain('Hello')
    await waitFor(() => expect(calls.some((c) => c.path === '/support/read')).toBe(true))
    await user.type(screen.getByLabelText('Write a message…'), 'Bus broke down')
    await user.click(screen.getByText('Mark as emergency'))
    await user.click(screen.getByRole('button', { name: /Send emergency/ }))

    const bubbles = await screen.findAllByTestId('support-message')
    expect(bubbles).toHaveLength(2)
    expect(bubbles[1].className).toContain('emergency')
    expect(calls.find((c) => c.path === '/support/messages').body).toEqual({ text: 'Bus broke down', emergency: true })
  })

  it('asks signed-out visitors to sign in', () => {
    render(<SupportPage signedIn={false} openAuth={() => {}} navigate={() => {}} />)
    expect(screen.getByText('Sign in to continue')).toBeTruthy()
  })
})

describe('admin', () => {
  it('approves a waiting request without ever showing the admin a password', async () => {
    const user = userEvent.setup()
    const request = { _id: 'r1', status: 'pending', note: 'Lost phone', user: { _id: 'u1', name: 'Rafi', email: 'rafi@test.local' }, createdAt: '2026-11-08T09:00:00Z' }
    routes['GET /admin/password-requests'] = () => reply(200, { success: true, pending: 1, data: [request] })
    routes['POST /admin/password-requests/r1/approve'] = () => reply(200, { success: true, message: 'Request approved.', request: { ...request, status: 'approved' } })
    render(<PasswordRequestsCard token="tok" notify={() => {}} />)

    const row = await screen.findByTestId('password-request')
    expect(row.textContent).toContain('Lost phone')
    await user.click(within(row).getByRole('button', { name: /Approve/ }))
    const dialog = screen.getByRole('dialog')
    expect(dialog.textContent).toContain('Make sure the request really comes from them')
    await user.click(within(dialog).getByRole('button', { name: 'Approve' }))
    expect(await screen.findByText('Request approved')).toBeTruthy()
    expect(calls.some((c) => c.path === '/admin/password-requests/r1/approve')).toBe(true)
    // nothing that looks like a password is on the admin's screen
    expect(document.querySelector('code')).toBeNull()
    expect(screen.queryByRole('button', { name: /Copy/ })).toBeNull()
  })

  it('lists conversations and replies to the selected user', async () => {
    const user = userEvent.setup()
    const thread = { user: { _id: 'u1', name: 'Rafi', email: 'rafi@test.local' }, lastMessage: message('m1', 'Help!', { emergency: true }), unread: 1, emergencyUnread: 1 }
    routes['GET /admin/support'] = () => reply(200, { success: true, unread: 1, emergencyUnread: 1, data: [thread] })
    routes['GET /admin/support/u1'] = () => reply(200, { success: true, user: thread.user, messages: [thread.lastMessage] })
    routes['POST /admin/support/u1/read'] = () => reply(200, { success: true, marked: 1 })
    routes['POST /admin/support/u1'] = ({ body }) => reply(201, { success: true, message: message('m2', body.text, { fromAdmin: true, senderName: 'Admin' }) })
    render(<AdminSupportInbox token="tok" />)

    const item = await screen.findByTestId('support-thread')
    expect(item.className).toContain('emergency')
    await user.click(item)
    expect((await screen.findAllByTestId('support-message'))[0].textContent).toContain('Help!')
    await waitFor(() => expect(calls.some((c) => c.path === '/admin/support/u1/read')).toBe(true))
    await user.type(screen.getByLabelText('Write a message…'), 'On it')
    await user.click(screen.getByRole('button', { name: /^Send/ }))
    await waitFor(() => expect(screen.getAllByTestId('support-message')).toHaveLength(2))
    expect(calls.find((c) => c.method === 'POST' && c.path === '/admin/support/u1').body).toEqual({ text: 'On it', emergency: false })
  })
})
