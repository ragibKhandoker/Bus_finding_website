import React, { useState } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'

// The designed pickers are only used where the browser allows replacing its own (Chrome, Edge, Safari).
vi.stubGlobal('CSS', { supports: () => true })
const { NativeControlsEnhancer } = await import('./NativeControlsEnhancer')

function Form() {
  const [time, setTime] = useState('')
  return <div><NativeControlsEnhancer /><label>Time spotted<input type="time" value={time} onChange={(e) => setTime(e.target.value)} /></label><output>{time}</output></div>
}

describe('time picker', () => {
  afterEach(cleanup)

  it('opens a designed clock instead of the browser’s and writes the chosen time to the field', () => {
    render(<Form />)
    const input = screen.getByLabelText('Time spotted')
    fireEvent.mouseDown(input)
    fireEvent.click(input)
    const clock = screen.getByRole('dialog', { name: 'Choose a time' })
    fireEvent.click(screen.getByRole('button', { name: '3' }))
    fireEvent.click(screen.getByRole('button', { name: '45' }))
    fireEvent.click(screen.getByRole('button', { name: 'PM' }))
    expect(input.value).toBe('15:45')
    expect(clock.querySelector('b').textContent).toBe('3:45')
    fireEvent.click(screen.getByRole('button', { name: 'AM' }))
    expect(input.value).toBe('03:45')
    fireEvent.click(screen.getByRole('button', { name: 'Done' }))
    expect(screen.queryByRole('dialog', { name: 'Choose a time' })).toBeNull()
  })
})
