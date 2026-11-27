import React, { useState } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { VoiceSearchButton } from './VoiceSearch'

let last = null
class FakeRecognition {
  constructor() { last = this }
  start() {}
  stop() { this.onend?.() }
  abort() {}
  say(text) {
    const alt = { transcript: text }
    const result = Object.assign([alt], { isFinal: true })
    this.onresult?.({ results: [result] })
    this.onend?.()
  }
}

function Page({ onResult }) {
  const [slot, setSlot] = useState(null)
  return <div><VoiceSearchButton slot={slot} onResult={onResult} /><div data-testid="slot" ref={setSlot} /><p>Other content</p></div>
}

describe('VoiceSearchButton', () => {
  afterEach(() => { cleanup(); delete window.webkitSpeechRecognition; last = null })

  it('shows no button where the browser has no speech recognition', () => {
    render(<Page onResult={() => null} />)
    expect(screen.queryByRole('button', { name: 'Search by voice' })).toBeNull()
  })

  it('shows the card inside the page slot, not over the page, and passes on what was said', () => {
    window.webkitSpeechRecognition = FakeRecognition
    const onResult = vi.fn(() => 'Uttara to Motijheel')
    render(<Page onResult={onResult} />)
    fireEvent.click(screen.getByRole('button', { name: 'Search by voice' }))
    const card = screen.getByRole('status', { name: 'Voice search' })
    expect(screen.getByTestId('slot').contains(card)).toBe(true)
    expect(card.className).not.toContain('is-floating')
    act(() => last.say('uttara theke motijheel'))
    expect(onResult).toHaveBeenCalledWith(['uttara theke motijheel'])
    expect(screen.getByText('“uttara theke motijheel”')).toBeTruthy()
  })
})
