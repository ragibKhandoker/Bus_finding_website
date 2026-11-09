import React, { useRef } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, render } from '@testing-library/react'
import { useRevealOnScroll } from './useRevealOnScroll'

// A stand-in IntersectionObserver the test can drive: show(el) reports el as scrolled into view.
let observers = []
class FakeObserver {
  constructor(callback) { this.callback = callback; this.watched = new Set(); observers.push(this) }
  observe(el) { this.watched.add(el) }
  unobserve(el) { this.watched.delete(el) }
  disconnect() { this.watched.clear() }
}
const show = (el) => observers.forEach((o) => o.watched.has(el) && o.callback([{ target: el, isIntersecting: true }]))
// Blocks marked data-below sit under the bottom of the screen.
const placeBelowFold = () => vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function rect() {
  const top = this.dataset?.below ? window.innerHeight + 200 : 10
  return { top, bottom: top + 100, left: 0, right: 100, width: 100, height: 100, x: 0, y: top }
})

function Page({ children }) {
  const ref = useRef(null)
  useRevealOnScroll(ref, 'page')
  return <main ref={ref}>{children}</main>
}

beforeEach(() => {
  observers = []
  vi.stubGlobal('IntersectionObserver', FakeObserver)
  vi.stubGlobal('matchMedia', () => ({ matches: false }))
  placeBelowFold()
})
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals() })

describe('useRevealOnScroll', () => {
  it('hides only cards below the screen, and shows them as they are scrolled to', () => {
    const { container } = render(<Page><section className="panel" data-testid="top">A</section><section className="panel" data-below="1">B</section></Page>)
    const [top, below] = container.querySelectorAll('.panel')
    expect(top.className).not.toMatch(/reveal/)
    expect(below.className).toMatch(/reveal-pending/)
    act(() => show(below))
    expect(below.className).toMatch(/reveal-in/)
    expect(below.className).not.toMatch(/reveal-pending/)
  })

  it('picks up cards added later, moves a card inside a panel with its panel, and leaves nothing hidden on leaving', async () => {
    const { container, rerender, unmount } = render(<Page><div /></Page>)
    rerender(<Page><section className="panel" data-below="1"><article className="ride-card panel" data-below="1">R</article></section></Page>)
    await act(async () => {})
    const outer = container.querySelector('section.panel'), inner = container.querySelector('.ride-card')
    expect(outer.className).toMatch(/reveal-pending/)
    expect(inner.className).not.toMatch(/reveal/)
    unmount()
    expect(outer.className).not.toMatch(/reveal/)
  })

  it('does nothing when the device asks for reduced motion', () => {
    vi.stubGlobal('matchMedia', () => ({ matches: true }))
    const { container } = render(<Page><section className="panel" data-below="1">B</section></Page>)
    expect(container.querySelector('.panel').className).toBe('panel')
  })
})
