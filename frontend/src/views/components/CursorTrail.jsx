import { useEffect } from 'react'
import '../styles/cursor.css'

// The website's own cursor. A themed arrow replaces the system arrow, a themed pointing hand appears over anything
// clickable, and a soft "snake" of colour slithers after the pointer while it moves, fading when it stops.
// Text fields keep the text cursor, maps keep their grab cursor, and touch screens or people who ask for reduced
// motion get the normal cursor with no trail.
const CLICKABLE = 'a[href],button:not(:disabled),[role="button"],[role="option"],[role="tab"],select,summary,label[for],input[type="checkbox"],input[type="radio"],input[type="date"],input[type="submit"],.ncx-day,.ncx-option'
const PALETTES = {
  light: [[15, 157, 107], [14, 159, 163], [242, 169, 59]],
  dark: [[127, 240, 194], [60, 199, 203], [245, 185, 86]],
  night: [[124, 196, 255], [94, 234, 212], [245, 185, 86]],
}

export function CursorTrail() {
  useEffect(() => {
    if (typeof window.matchMedia !== 'function') return undefined // e.g. test environments
    if (!window.matchMedia('(hover: hover) and (pointer: fine)').matches) return undefined
    const root = document.documentElement
    root.classList.add('sc-cursor-on')

    // ---- Hand over clickable things ----
    // Also covers elements the page styles with cursor:pointer, found by reading their cursor with the hand off.
    let lastTarget = null, hand = false
    const wantsHand = (el) => {
      if (!(el instanceof Element)) return false
      if (el.closest(CLICKABLE)) return true
      if (!hand) return getComputedStyle(el).cursor === 'pointer'
      root.classList.remove('sc-cursor-hand')
      const pointer = getComputedStyle(el).cursor === 'pointer'
      root.classList.add('sc-cursor-hand')
      return pointer
    }

    // ---- The snake ----
    const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    const canvas = document.createElement('canvas')
    canvas.className = 'sc-cursor-snake'
    canvas.setAttribute('aria-hidden', 'true')
    const ctx = canvas.getContext('2d')
    if (!reduce && ctx) document.body.append(canvas)
    let dpr = 1
    const size = () => {
      dpr = Math.min(window.devicePixelRatio || 1, 2)
      canvas.width = Math.round(window.innerWidth * dpr)
      canvas.height = Math.round(window.innerHeight * dpr)
      canvas.style.width = `${window.innerWidth}px`
      canvas.style.height = `${window.innerHeight}px`
    }
    size()

    const LIFE = 650 // ms a piece of the tail stays
    const points = []
    let px = 0, py = 0, hx = 0, hy = 0, started = false, frame = 0

    const colourAt = (f) => {
      const p = PALETTES[root.dataset.theme] || PALETTES.light
      const seg = f < 0.5 ? [p[0], p[1], f * 2] : [p[1], p[2], (f - 0.5) * 2]
      return seg[0].map((c, i) => Math.round(c + (seg[1][i] - c) * seg[2]))
    }

    const draw = (now) => {
      frame = 0
      // The head eases toward the pointer, so the body curves smoothly even when the mouse jumps.
      hx += (px - hx) * 0.38
      hy += (py - hy) * 0.38
      const last = points[points.length - 1]
      if (!last || Math.hypot(hx - last.x, hy - last.y) > 0.8) points.push({ x: hx, y: hy, t: now })
      while (points.length && now - points[0].t > LIFE) points.shift()
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
      ctx.clearRect(0, 0, canvas.width, canvas.height)
      const n = points.length
      if (n > 2) {
        ctx.lineCap = 'round'
        ctx.lineJoin = 'round'
        for (let i = 1; i < n - 1; i += 1) {
          const f = i / (n - 1) // 0 = tail, 1 = head
          const life = 1 - (now - points[i].t) / LIFE
          const [r, g, b] = colourAt(1 - f)
          const a = points[i - 1], m = points[i], c = points[i + 1]
          ctx.beginPath()
          ctx.moveTo((a.x + m.x) / 2, (a.y + m.y) / 2)
          ctx.quadraticCurveTo(m.x, m.y, (m.x + c.x) / 2, (m.y + c.y) / 2)
          ctx.lineWidth = 0.6 + 6.4 * f * f
          ctx.strokeStyle = `rgba(${r},${g},${b},${(0.15 + 0.75 * f) * Math.max(life, 0)})`
          ctx.shadowColor = `rgba(${r},${g},${b},${0.45 * f})`
          ctx.shadowBlur = 8 * f
          ctx.stroke()
        }
      }
      if (n > 1 || Math.hypot(px - hx, py - hy) > 0.5) frame = requestAnimationFrame(draw)
    }
    const kick = () => { if (!reduce && ctx && !frame) frame = requestAnimationFrame(draw) }

    const onMove = (event) => {
      if (event.pointerType && event.pointerType !== 'mouse') return
      px = event.clientX; py = event.clientY
      if (!started) { hx = px; hy = py; started = true }
      if (event.target !== lastTarget) {
        lastTarget = event.target
        hand = wantsHand(event.target)
        root.classList.toggle('sc-cursor-hand', hand)
      }
      kick()
    }
    const onLeave = () => { points.length = 0; started = false; ctx?.clearRect(0, 0, canvas.width, canvas.height) }

    window.addEventListener('pointermove', onMove, { passive: true })
    window.addEventListener('resize', size)
    document.addEventListener('mouseleave', onLeave)
    window.addEventListener('blur', onLeave)
    return () => {
      cancelAnimationFrame(frame)
      window.removeEventListener('pointermove', onMove)
      window.removeEventListener('resize', size)
      document.removeEventListener('mouseleave', onLeave)
      window.removeEventListener('blur', onLeave)
      canvas.remove()
      root.classList.remove('sc-cursor-on', 'sc-cursor-hand')
    }
  }, [])
  return null
}
