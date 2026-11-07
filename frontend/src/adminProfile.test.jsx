import { beforeAll, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

// Boots the whole app as a signed-in admin on /profile: the admin sees their admin profile, not a commuter's.
const base64Url = (value) => btoa(JSON.stringify(value)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_')
const token = `${base64Url({ alg: 'HS256' })}.${base64Url({ exp: Math.floor(Date.now() / 1000) + 3600 })}.sig`
const admin = { id: 'a1', _id: 'a1', name: 'Ragib Ahsan', email: 'ragib@admin.test', role: 'admin', friendCode: 'ADM12', createdAt: '2026-10-01T00:00:00Z' }
const overview = { totalUsers: 42, commuters: 30, drivers: 10, admins: 2, newThisWeek: 5, pendingPasswordRequests: 2, unreadMessages: 3, emergencyMessages: 1, pendingVerifications: 0 }

beforeAll(async () => {
  document.body.innerHTML = '<div id="root"></div>'
  // This tab's own sign-in (see session.js): the tab id in window.name matches the one in sessionStorage.
  window.name = 'sc-tab-test'
  sessionStorage.setItem('sc_tab_id', 'sc-tab-test')
  sessionStorage.setItem('sc_token', token)
  sessionStorage.setItem('sc_user', JSON.stringify(admin))
  window.history.pushState({}, '', '/profile')
  window.scrollTo = () => {}
  vi.stubGlobal('fetch', vi.fn((url) => {
    const { pathname } = new URL(url)
    const responses = {
      '/auth/validate': { success: true, valid: true, user: admin },
      '/admin/overview': { success: true, data: overview },
    }
    const body = responses[pathname] || { success: true, data: [] }
    return Promise.resolve(new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }))
  }))
  await import('./main')
  await waitFor(() => expect(document.querySelector('nav.nav-links')).not.toBeNull())
})

describe('admin profile', () => {
  it('shows the admin panel profile with live counts instead of the commuter profile', async () => {
    expect(await screen.findByRole('heading', { level: 1, name: 'Admin profile.' })).toBeTruthy()
    expect(screen.getAllByText('Administrator').length).toBeGreaterThan(0)
    // Commuter-only parts are not shown to admins.
    expect(screen.queryByText('Friend code')).toBeNull()
    expect(screen.queryByText('Verify your NID or student pass')).toBeNull()
    expect(screen.queryByText('Profile completeness')).toBeNull()

    // 2 password requests + 3 unread messages (the emergency is one of them) + 0 verifications.
    await waitFor(() => expect(screen.getByText('5 items need your attention.')).toBeTruthy())
    const tasks = screen.getAllByTestId('admin-task')
    const inbox = tasks.find((task) => task.textContent.includes('Support inbox'))
    expect(inbox.className).toContain('urgent')
    expect(inbox.textContent).toContain('1 emergency messages waiting')
    expect(screen.getByText('Password & security')).toBeTruthy()
  })

  it('shows the header account button as Admin, without the wallet', () => {
    const chip = document.querySelector('.profile-chip')
    expect(chip.className).toContain('is-admin')
    expect(chip.textContent).toContain('Admin')
    expect(screen.queryByTitle('My wallet')).toBeNull()
  })

  it('opens the admin panel from a shortcut', async () => {
    const user = userEvent.setup()
    const tasks = screen.getAllByTestId('admin-task')
    await user.click(tasks.find((task) => task.textContent.includes('Forgot-password requests')))
    expect(await screen.findByRole('heading', { level: 1, name: 'Admin panel.' })).toBeTruthy()
    expect(window.location.pathname).toBe('/admin')
    expect(within(document.querySelector('main')).getByText('Forgot-password requests')).toBeTruthy()
    expect(screen.getByRole('tab', { name: /Password requests/ }).getAttribute('aria-selected')).toBe('true')
  })

  it('shows live numbers on the admin dashboard and switches sections with the tabs', async () => {
    const user = userEvent.setup()
    const kpis = await screen.findAllByTestId('admin-kpi')
    await waitFor(() => expect(kpis[0].textContent).toContain('42'))
    const inbox = kpis.find((kpi) => kpi.textContent.includes('Unread messages'))
    expect(inbox.className).toContain('danger')
    expect(inbox.textContent).toContain('1 emergency messages waiting')

    await user.click(screen.getByRole('tab', { name: /Users/ }))
    expect(await screen.findByRole('heading', { level: 2, name: /All users/ })).toBeTruthy()
    expect(screen.getByRole('group', { name: 'Filter by role' })).toBeTruthy()

    await user.click(screen.getByRole('tab', { name: /Verifications/ }))
    expect(await screen.findByRole('heading', { level: 2, name: /Account verification/ })).toBeTruthy()
  })
})
