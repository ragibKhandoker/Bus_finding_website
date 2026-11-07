import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, render, renderHook, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ADMIN_ALERT_EVENT, useAdminAlerts } from './useAdminAlerts'
import { NOTIFICATIONS_CHANGED } from '../models/notifications'
import { AdminAlert } from '../views/components/AdminAlert'

const reply = (body) => Promise.resolve(new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }))
let overview
const counts = (extra = {}) => ({ totalUsers: 5, commuters: 4, drivers: 0, admins: 1, newThisWeek: 1, pendingPasswordRequests: 0, unreadMessages: 0, emergencyMessages: 0, pendingVerifications: 0, ...extra })

beforeEach(() => {
  overview = counts()
  vi.stubGlobal('fetch', vi.fn((url) => {
    const { pathname } = new URL(url)
    if (pathname === '/admin/overview') return reply({ success: true, data: overview })
    if (pathname === '/admin/password-requests') return reply({ success: true, pending: 1, data: [{ _id: 'r1', status: 'pending', user: { name: 'Rafi Ahmed', email: 'rafi@test.local' } }] })
    if (pathname === '/admin/support') return reply({ success: true, data: [{ user: { _id: 'u2', name: 'Nadia' }, lastMessage: { text: 'Bus broke down', emergency: true }, unread: 1, emergencyUnread: 1 }] })
    return reply({ success: true })
  }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks(); document.title = '' })

const nextCheck = async () => { await act(async () => { window.dispatchEvent(new Event('focus')) }) }

describe('admin alerts', () => {
  it('alerts at once when a new password request arrives, and refreshes the bell and admin lists', async () => {
    const bell = vi.fn(), lists = vi.fn()
    window.addEventListener(NOTIFICATIONS_CHANGED, bell)
    window.addEventListener(ADMIN_ALERT_EVENT, lists)
    const { result } = renderHook(() => useAdminAlerts({ token: 'tok', isAdmin: true }))
    await waitFor(() => expect(fetch).toHaveBeenCalled())
    expect(result.current[0]).toBeNull() // the first check only records the counts

    overview = counts({ pendingPasswordRequests: 1 })
    await nextCheck()
    await waitFor(() => expect(result.current[0]?.kind).toBe('password'))
    expect(result.current[0].text).toContain('Rafi Ahmed (rafi@test.local)')
    expect(result.current[0].focus).toBe('sp-requests-title')
    expect(result.current[2]).toBe(1)
    expect(document.title).toMatch(/^\(1\)/)
    expect(bell).toHaveBeenCalled()
    expect(lists).toHaveBeenCalled()
    window.removeEventListener(NOTIFICATIONS_CHANGED, bell)
    window.removeEventListener(ADMIN_ALERT_EVENT, lists)
  })

  it('puts an emergency message first', async () => {
    const { result } = renderHook(() => useAdminAlerts({ token: 'tok', isAdmin: true }))
    await waitFor(() => expect(fetch).toHaveBeenCalled())
    overview = counts({ pendingPasswordRequests: 1, unreadMessages: 1, emergencyMessages: 1 })
    await nextCheck()
    await waitFor(() => expect(result.current[0]?.kind).toBe('emergency'))
    expect(result.current[0].title).toBe('Emergency message from Nadia')
  })

  it('does nothing for users who are not admins', async () => {
    renderHook(() => useAdminAlerts({ token: 'tok', isAdmin: false }))
    await nextCheck()
    expect(fetch).not.toHaveBeenCalled()
  })

  it('shows the pop-up with a View button', async () => {
    const user = userEvent.setup()
    const onView = vi.fn()
    render(<AdminAlert alert={{ id: 1, kind: 'password', title: 'New password request', text: 'Rafi forgot their password.' }} onView={onView} onClose={() => {}} />)
    expect(screen.getByTestId('admin-alert').textContent).toContain('New password request')
    await user.click(screen.getByRole('button', { name: /View/ }))
    expect(onView).toHaveBeenCalled()
  })
})
