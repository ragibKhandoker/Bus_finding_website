import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PasswordPanel, passwordStrength } from './ProfilePage'

let calls
beforeEach(() => {
  calls = []
  vi.stubGlobal('fetch', vi.fn((url, options = {}) => {
    calls.push({ method: options.method || 'GET', path: new URL(url).pathname, body: options.body && JSON.parse(options.body) })
    return Promise.resolve(new Response(JSON.stringify({ success: true, message: 'Password updated.' }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
  }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals() })

describe('password strength', () => {
  it('grows with length and character variety', () => {
    expect(passwordStrength('')).toBe(0)
    expect(passwordStrength('abcdef')).toBe(1)
    expect(passwordStrength('abc123')).toBe(2)
    expect(passwordStrength('abcdef1234')).toBe(3)
    expect(passwordStrength('Abcdef1234!')).toBe(4)
  })
})

describe('Password & security panel', () => {
  it('shows the strength and rules live, each field has its own eye, and a mismatch is flagged', async () => {
    const user = userEvent.setup()
    render(<PasswordPanel token="tok" notify={vi.fn()} />)
    const current = screen.getByPlaceholderText('Enter your current password')
    const next = screen.getByPlaceholderText('Create a new password')
    const confirm = screen.getByPlaceholderText('Type the new password again')

    expect(screen.getByText('Password strength')).toBeTruthy()
    await user.type(next, 'abc')
    expect(screen.getByText('Too short')).toBeTruthy()
    expect(screen.getByText('At least one letter').closest('li').className).toBe('ok')
    expect(screen.getByText('At least one number').closest('li').className).toBe('')
    await user.type(next, 'def1234')
    expect(screen.getByText('Good')).toBeTruthy()
    expect(screen.getByText('At least 6 characters').closest('li').className).toBe('ok')

    await user.click(screen.getByRole('button', { name: 'Show new password' }))
    expect(next.type).toBe('text')
    expect(current.type).toBe('password')
    expect(confirm.type).toBe('password')

    await user.type(confirm, 'abcdef9999')
    expect(screen.getByText('Passwords do not match')).toBeTruthy()
    await user.clear(confirm)
    await user.type(confirm, 'abcdef1234')
    expect(screen.getByText('Passwords match')).toBeTruthy()
  })

  it('sends only the current and new password, then clears the form and confirms', async () => {
    const user = userEvent.setup()
    const notify = vi.fn()
    render(<PasswordPanel token="tok" notify={notify} />)
    await user.type(screen.getByPlaceholderText('Enter your current password'), 'oldpass1')
    await user.type(screen.getByPlaceholderText('Create a new password'), 'newpass12')
    await user.type(screen.getByPlaceholderText('Type the new password again'), 'newpass12')
    await user.click(screen.getByRole('button', { name: /Update password/ }))
    await waitFor(() => expect(calls).toHaveLength(1))
    expect(calls[0]).toMatchObject({ method: 'PUT', path: '/auth/password', body: { currentPassword: 'oldpass1', newPassword: 'newpass12' } })
    expect(await screen.findByText('Password updated')).toBeTruthy()
    expect(screen.getByPlaceholderText('Create a new password').value).toBe('')
    expect(notify).toHaveBeenCalledWith('Password updated.')
  })

  it('refuses a mismatch without calling the server', async () => {
    const user = userEvent.setup()
    render(<PasswordPanel token="tok" notify={vi.fn()} />)
    await user.type(screen.getByPlaceholderText('Enter your current password'), 'oldpass1')
    await user.type(screen.getByPlaceholderText('Create a new password'), 'newpass12')
    await user.type(screen.getByPlaceholderText('Type the new password again'), 'newpass13')
    await user.click(screen.getByRole('button', { name: /Update password/ }))
    expect(screen.getByRole('alert').textContent).toContain('New passwords do not match.')
    expect(calls).toHaveLength(0)
  })
})
