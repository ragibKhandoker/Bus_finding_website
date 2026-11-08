import React from 'react'
import { afterEach, describe, expect, it } from 'vitest'
import { act, cleanup, render, renderHook, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PERSIST_PREFIX, clearPersistentState, usePersistentState } from './usePersistentState'

afterEach(() => cleanup())

function Draft({ name }) {
  const [text, setText] = usePersistentState(name ? `test:draft:${name}` : null, '')
  return <input aria-label="draft" value={text} onChange={(e) => setText(e.target.value)} />
}

describe('usePersistentState', () => {
  it('keeps what was typed when the page is left and opened again', async () => {
    const user = userEvent.setup()
    const { unmount } = render(<Draft name="a" />)
    await user.type(screen.getByLabelText('draft'), 'Half written')
    unmount() // leaving the page
    render(<Draft name="a" />) // coming back
    expect(screen.getByLabelText('draft').value).toBe('Half written')
  })

  it('keeps a separate value for each key, and switches when the key changes', async () => {
    const user = userEvent.setup()
    const { rerender } = render(<Draft name="a" />)
    await user.type(screen.getByLabelText('draft'), 'for A')
    rerender(<Draft name="b" />)
    expect(screen.getByLabelText('draft').value).toBe('')
    await user.type(screen.getByLabelText('draft'), 'for B')
    rerender(<Draft name="a" />)
    expect(screen.getByLabelText('draft').value).toBe('for A')
  })

  it('works like plain state with no key, keeping nothing', async () => {
    const user = userEvent.setup()
    const { unmount } = render(<Draft name={null} />)
    await user.type(screen.getByLabelText('draft'), 'gone')
    unmount()
    render(<Draft name={null} />)
    expect(screen.getByLabelText('draft').value).toBe('')
    expect(Object.keys(sessionStorage).some((k) => k.startsWith(PERSIST_PREFIX))).toBe(false)
  })

  it('accepts updater functions and objects, and an initial function', () => {
    const { result, unmount } = renderHook(() => usePersistentState('test:form', () => ({ from: '', to: '' })))
    act(() => result.current[1]((form) => ({ ...form, from: 'Mirpur 10' })))
    expect(result.current[0]).toEqual({ from: 'Mirpur 10', to: '' })
    unmount()
    const again = renderHook(() => usePersistentState('test:form', () => ({ from: '', to: '' })))
    expect(again.result.current[0]).toEqual({ from: 'Mirpur 10', to: '' })
  })

  it('forgets everything kept on sign-out, and nothing else', () => {
    sessionStorage.setItem('sc_token', 'keep-me')
    const { unmount } = renderHook(() => usePersistentState('test:x', 'value'))
    unmount()
    expect(sessionStorage.getItem(`${PERSIST_PREFIX}test:x`)).toBe('"value"')
    clearPersistentState()
    expect(sessionStorage.getItem(`${PERSIST_PREFIX}test:x`)).toBeNull()
    expect(sessionStorage.getItem('sc_token')).toBe('keep-me')
    sessionStorage.removeItem('sc_token')
  })

  it('starts fresh when the kept value cannot be read', () => {
    sessionStorage.setItem(`${PERSIST_PREFIX}test:broken`, '{not json')
    const { result } = renderHook(() => usePersistentState('test:broken', 'fallback'))
    expect(result.current[0]).toBe('fallback')
  })
})
