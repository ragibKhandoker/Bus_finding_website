import { t } from '../../models/i18n'

// Small helpers that make the Leaflet maps behave well on phones.

const media = (query) => typeof window !== 'undefined' && typeof window.matchMedia === 'function' && window.matchMedia(query).matches

// A touch screen (phones, tablets): one finger should scroll the page, not drag the map.
export const isTouchScreen = () => media('(pointer: coarse)')

// Less padding around a fitted route on a narrow map, so the route itself fills the space.
export const fitPadding = (box) => ((box?.clientWidth || 800) < 480 ? 18 : 40)

// Leaflet does not notice when its box changes size (rotating the phone, the address bar hiding, the layout
// changing). Re-measure then, and let the caller fit the route again. Returns a cleanup function.
export function watchLeafletResize(box, map, onResize) {
  if (!box || typeof ResizeObserver === 'undefined') return () => {}
  let frame = 0
  let first = true
  const observer = new ResizeObserver(() => {
    if (first) { first = false; return } // the first call is just the initial size
    cancelAnimationFrame(frame)
    frame = requestAnimationFrame(() => { map.invalidateSize(); onResize?.() })
  })
  observer.observe(box)
  return () => { cancelAnimationFrame(frame); observer.disconnect() }
}

// "Use two fingers to move the map", shown briefly when the map is touched with one finger while one-finger
// dragging is off (the same idea as Google Maps' cooperative gestures). Returns a cleanup function.
export function cooperativeTouchHint(box) {
  if (!box) return () => {}
  const hint = document.createElement('div')
  hint.className = 'map-touch-hint'
  hint.textContent = t('Use two fingers to move the map')
  hint.setAttribute('aria-hidden', 'true')
  box.appendChild(hint)
  let timer = 0
  const onTouch = (event) => {
    if (event.touches.length !== 1) { hint.classList.remove('show'); return }
    hint.classList.add('show')
    clearTimeout(timer)
    timer = setTimeout(() => hint.classList.remove('show'), 1400)
  }
  box.addEventListener('touchstart', onTouch, { passive: true })
  return () => { clearTimeout(timer); box.removeEventListener('touchstart', onTouch); hint.remove() }
}
