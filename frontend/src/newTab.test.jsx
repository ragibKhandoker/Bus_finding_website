import { beforeAll, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

// Boots the whole app as a NEW tab while another tab is signed in as admin: this tab must start signed out,
// and only uses the admin's sign-in if the user picks "Continue as …".
const base64Url = (value) => btoa(JSON.stringify(value)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_')
const token = `${base64Url({ alg: 'HS256' })}.${base64Url({ exp: Math.floor(Date.now() / 1000) + 3600 })}.sig`
const admin = { id: 'a1', _id: 'a1', name: 'Ragib Ahsan', email: 'ragib@admin.test', role: 'admin', friendCode: 'ADM12' }

beforeAll(async () => {
  document.body.innerHTML = '<div id="root"></div>'
  // The admin tab saved its sign-in as the most recent one; this tab has nothing of its own.
  localStorage.setItem('sc_token', token)
  localStorage.setItem('sc_user', JSON.stringify(admin))
  window.history.pushState({}, '', '/profile')
  window.scrollTo = () => {}
  vi.stubGlobal('fetch', vi.fn((url) => {
    const { pathname } = new URL(url)
    const body = pathname === '/auth/validate' ? { success: true, valid: true, user: admin } : { success: true, data: [] }
    return Promise.resolve(new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }))
  }))
  await import('./main')
  await waitFor(() => expect(document.querySelector('nav.nav-links')).not.toBeNull())
})

describe('a new tab', () => {
  it('starts signed out instead of taking the admin sign-in', async () => {
    expect(await screen.findByText('Sign in to manage your profile.')).toBeTruthy()
    expect(document.querySelector('.profile-chip')).toBeNull()
    expect(sessionStorage.getItem('sc_token')).toBeNull()
  })

  it('can sign in with another account, or continue as the admin with one click', async () => {
    const user = userEvent.setup()
    await user.click(screen.getAllByRole('button', { name: /Sign in/ })[0])
    const dialog = await screen.findByRole('dialog')
    // The admin account is not offered on the Commuter tab: each account signs in only through its own role.
    expect(within(dialog).queryByRole('button', { name: /Continue as Ragib Ahsan/ })).toBeNull()
    expect(within(dialog).getByPlaceholderText('you@example.com')).toBeTruthy()

    await user.click(within(dialog).getByRole('tab', { name: /Admin/ }))
    expect(within(dialog).getByText('or sign in with another account')).toBeTruthy()
    await user.click(within(dialog).getByRole('button', { name: /Continue as Ragib Ahsan/ }))
    expect(await screen.findByRole('heading', { level: 1, name: 'Admin profile.' })).toBeTruthy()
    expect(sessionStorage.getItem('sc_session_borrowed')).toBe('1')
  })
})
