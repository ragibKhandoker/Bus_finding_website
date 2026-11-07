import React, { useState } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AuthModal } from './AuthModal'

let routes, calls
const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))

beforeEach(() => {
  calls = []; routes = {}
  localStorage.clear()
  vi.stubGlobal('fetch', vi.fn((url, options = {}) => {
    const { pathname } = new URL(url)
    const method = options.method || 'GET'
    const body = options.body && JSON.parse(options.body)
    calls.push({ method, path: pathname, body })
    const handler = routes[`${method} ${pathname}`]
    return handler ? handler({ body }) : reply(404, { success: false, message: 'Not found.' })
  }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

function Harness({ onSuccess }) {
  const [mode, setMode] = useState('login')
  return <AuthModal mode={mode} onMode={setMode} onClose={() => {}} onSuccess={onSuccess} />
}

const signIn = async (user, tab, email = 'a@test.local') => {
  const dialog = screen.getByRole('dialog')
  await user.click(within(dialog).getByRole('tab', { name: new RegExp(tab) }))
  await user.type(within(dialog).getByPlaceholderText(/@/), email)
  await user.type(within(dialog).getByPlaceholderText('Enter your password'), 'secret123')
  await user.click(within(dialog).getByRole('button', { name: tab === 'Admin' ? /Sign in as admin/ : /^Sign in/ }))
}

describe('sign in by role', () => {
  it('offers Commuter, Driver and Admin, and sends the chosen role', async () => {
    const user = userEvent.setup()
    const onSuccess = vi.fn()
    routes['POST /auth/login'] = ({ body }) => reply(200, { success: true, token: 'tok', user: { _id: 'u1', name: 'Dina', role: body.role } })
    render(<Harness onSuccess={onSuccess} />)
    expect(screen.getAllByRole('tab').map((tab) => tab.textContent.trim())).toEqual(['Commuter', 'Driver', 'Admin'])

    await signIn(user, 'Driver')
    expect(calls.find((c) => c.path === '/auth/login').body).toEqual({ email: 'a@test.local', password: 'secret123', role: 'driver' })
    expect(onSuccess).toHaveBeenCalled()
  })

  it('shows the server refusing an account on the wrong role button', async () => {
    const user = userEvent.setup()
    const onSuccess = vi.fn()
    routes['POST /auth/login'] = () => reply(403, { success: false, message: 'This is a commuter account. Choose Commuter to sign in.' })
    render(<Harness onSuccess={onSuccess} />)
    await signIn(user, 'Driver')
    expect(screen.getByRole('alert').textContent).toContain('This is a commuter account. Choose Commuter to sign in.')
    expect(onSuccess).not.toHaveBeenCalled()
  })

  it('still refuses (and ends the session) if a server answers with another role', async () => {
    const user = userEvent.setup()
    const onSuccess = vi.fn()
    routes['POST /auth/login'] = () => reply(200, { success: true, token: 'tok', user: { _id: 'u1', role: 'commuter' } })
    routes['POST /auth/logout'] = () => reply(200, { success: true })
    render(<Harness onSuccess={onSuccess} />)
    await signIn(user, 'Admin')
    expect(screen.getByRole('alert').textContent).toContain('This is a commuter account. Choose Commuter to sign in.')
    expect(calls.some((c) => c.path === '/auth/logout')).toBe(true)
    expect(onSuccess).not.toHaveBeenCalled()
  })
})
