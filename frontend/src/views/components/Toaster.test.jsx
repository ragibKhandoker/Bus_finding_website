import React from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { TOAST_MS, Toaster, useToasts } from './Toaster'

let api
function Harness() {
  const toasts = useToasts()
  api = toasts
  return <Toaster toasts={toasts.toasts} onDismiss={toasts.dismiss} onRemove={toasts.remove} />
}

afterEach(() => { cleanup(); vi.useRealTimers() })

describe('Toaster', () => {
  it('stacks notifications newest first and closes each after its time', () => {
    vi.useFakeTimers()
    render(<Harness />)
    act(() => api.notify('You boarded Victor Classic at Malibagh. Have a good trip!'))
    act(() => { vi.advanceTimersByTime(1000) })
    act(() => api.notify('Review posted.'))
    const toasts = screen.getAllByTestId('toast')
    expect(toasts.map((el) => within(el).getByText(/./, { selector: '.toast-message' }).textContent)).toEqual(['Review posted.', 'You boarded Victor Classic at Malibagh. Have a good trip!'])
    expect(toasts[0].className).toContain('is-success')

    // The first one goes after its own time; the second, shown later, is still up.
    // Each close is two steps: the countdown ends, then the exit animation finishes.
    act(() => { vi.advanceTimersByTime(TOAST_MS - 1000) })
    act(() => { vi.advanceTimersByTime(300) })
    expect(screen.getAllByTestId('toast')).toHaveLength(1)
    expect(screen.getByText('Review posted.')).toBeTruthy()
    act(() => { vi.advanceTimersByTime(1000) })
    act(() => { vi.advanceTimersByTime(300) })
    expect(screen.queryAllByTestId('toast')).toHaveLength(0)
  })

  it('stays up while hovered and closes with the close button', () => {
    vi.useFakeTimers()
    render(<Harness />)
    act(() => api.notify('You have been signed out.', 'info'))
    const toast = screen.getByTestId('toast')
    expect(toast.className).toContain('is-info')
    expect(within(toast).getByText('Notice')).toBeTruthy()
    fireEvent.mouseEnter(toast)
    act(() => { vi.advanceTimersByTime(TOAST_MS * 3) })
    expect(screen.getByTestId('toast')).toBeTruthy()
    fireEvent.click(within(toast).getByRole('button', { name: 'Close notification' }))
    act(() => { vi.advanceTimersByTime(300) })
    expect(screen.queryAllByTestId('toast')).toHaveLength(0)
  })

  it('shows the same message once and at most four at a time', () => {
    render(<Harness />)
    act(() => { api.notify('Saved.'); api.notify('Saved.') })
    expect(screen.getAllByTestId('toast')).toHaveLength(1)
    act(() => { for (let i = 0; i < 6; i++) api.notify(`Message ${i}`) })
    expect(screen.getAllByTestId('toast')).toHaveLength(4)
    expect(screen.getAllByTestId('toast')[0].textContent).toContain('Message 5')
  })
})
