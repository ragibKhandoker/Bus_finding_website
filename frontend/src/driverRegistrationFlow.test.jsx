import React from 'react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PageErrorBoundary } from './views/components/PageErrorBoundary'

// The six-step form has its own tests; here a stand-in sends the finished application straight away.
vi.mock('./views/components/driver/DriverRegistrationForm', () => ({
  DriverRegistrationForm: ({ onSubmit }) => <button type="button" onClick={() => onSubmit({})}>Send application</button>,
}))

const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))
const token = `a.${btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 3600 }))}.c`
const driver = { _id: 'u1', id: 'u1', name: 'Karim Uddin', email: 'karim@test.local', role: 'driver' }
const pending = { registered: true, applicationCode: 'DRV-1A2B3C4D', verificationStatus: 'PENDING', accountStatus: 'PENDING_VERIFICATION',
  submittedAt: '2026-11-10T08:00:00Z', requestedFields: [], assignments: [], nextStep: 'An administrator will review your documents.' }
let registerReply

beforeEach(() => {
  sessionStorage.clear(); localStorage.clear()
  window.history.replaceState({}, '', '/driver/register')
  Element.prototype.scrollIntoView = () => {}
  window.scrollTo = () => {}
  registerReply = () => reply(201, { success: true, token, user: driver, data: pending })
  vi.stubGlobal('fetch', vi.fn((url, options = {}) => {
    const { pathname } = new URL(url)
    if ((options.method || 'GET') === 'POST' && pathname === '/api/driver-registration/register') return registerReply()
    if (pathname === '/api/driver-registration/status') return reply(200, { success: true, data: pending })
    return reply(200, { success: true, data: [], count: 0 })
  }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

it('opens the status page with a please-wait message after registering', async () => {
  const { App } = await import('./views/App')
  render(<App />)
  await userEvent.click(screen.getByText('Send application'))
  expect(await screen.findByText('Please wait for admin verification', {}, { timeout: 3000 })).toBeTruthy()
  expect(screen.getByText('DRV-1A2B3C4D')).toBeTruthy()
  expect(window.location.pathname).toBe('/driver/status')
  expect(screen.getByText(/Please wait until an administrator verifies your account/, { selector: '.toast *, [class*=toast] *' })).toBeTruthy()
})

it('does not leave a blank page when the reply has no sign-in', async () => {
  registerReply = () => reply(201, { success: true })
  const { App } = await import('./views/App')
  render(<App />)
  await userEvent.click(screen.getByText('Send application'))
  expect(await screen.findByText(/Please sign in to follow the review/)).toBeTruthy()
  expect(window.location.pathname).toBe('/')
})

it('shows a message instead of a white screen when a page fails to draw', () => {
  vi.spyOn(console, 'error').mockImplementation(() => {})
  const Broken = () => { throw new Error('boom') }
  render(<PageErrorBoundary><Broken /></PageErrorBoundary>)
  expect(screen.getByText('This page could not be shown')).toBeTruthy()
  expect(screen.getByRole('button', { name: /Reload page/ })).toBeTruthy()
})
