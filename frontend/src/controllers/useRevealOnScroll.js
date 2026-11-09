import { useEffect, useLayoutEffect } from 'react'

// Cards and panels that rise gently into view the first time they are scrolled to, on every page. Only blocks
// that start below the screen are hidden: whatever is already on screen shows as it is, so nothing flickers.
// Cards added later (a list that finishes loading, more results) are picked up too. The home page has its own
// motion (useScrollMotion) and is left alone. Skipped when the device asks for reduced motion, and where the
// browser lacks the observers (nothing is hidden then).
export const REVEAL_TARGETS = '.panel,.extra-panel,.bus-result,.ride-card,.route-bus,.hp-route,.ap-task,.adm-kpi,.review-item,.empty-state'

export function useRevealOnScroll(containerRef, pageKey) {
  useLayoutEffect(() => {
    const root = containerRef.current
    if (!root || !('IntersectionObserver' in window) || !('MutationObserver' in window) || matchMedia('(prefers-reduced-motion: reduce)').matches) return
    const seen = new WeakSet()
    // Cards that come into view together appear one after another, a few frames apart.
    let order = 0, orderReset = 0
    const reveal = (el) => {
      el.style.animationDelay = `${Math.min(order++, 5) * 70}ms`
      window.clearTimeout(orderReset)
      orderReset = window.setTimeout(() => { order = 0 }, 120)
      el.classList.remove('reveal-pending')
      el.classList.add('reveal-in')
      const done = (event) => {
        if (event.target !== el) return
        el.classList.remove('reveal-in')
        el.style.animationDelay = ''
        el.removeEventListener('animationend', done)
      }
      el.addEventListener('animationend', done)
    }
    const io = new IntersectionObserver((entries) => {
      entries.forEach((entry) => {
        if (!entry.isIntersecting) return
        io.unobserve(entry.target)
        reveal(entry.target)
      })
    }, { rootMargin: '0px 0px -40px 0px' })
    const scan = () => {
      root.querySelectorAll(REVEAL_TARGETS).forEach((el) => {
        if (seen.has(el)) return
        seen.add(el)
        // One movement per block: a card inside a panel moves with its panel.
        if (el.closest('.hp-motion') || el.parentElement?.closest(REVEAL_TARGETS)) return
        if (el.getBoundingClientRect().top < window.innerHeight) return
        el.classList.add('reveal-pending')
        io.observe(el)
      })
    }
    scan()
    // Runs before the browser paints the change, so a new card never shows and then hides again.
    const mo = new MutationObserver(scan)
    mo.observe(root, { childList: true, subtree: true })
    return () => {
      mo.disconnect()
      io.disconnect()
      window.clearTimeout(orderReset)
      root.querySelectorAll('.reveal-pending,.reveal-in').forEach((el) => { el.classList.remove('reveal-pending', 'reveal-in'); el.style.animationDelay = '' })
    }
  }, [containerRef, pageKey])
}

// The sticky header gets a soft shadow once the page is scrolled, so it reads as floating above the content.
export function useHeaderShadow(headerRef) {
  useEffect(() => {
    const header = headerRef.current
    if (!header) return
    let frame = 0
    const update = () => { frame = 0; header.classList.toggle('is-scrolled', window.scrollY > 4) }
    const onScroll = () => { if (!frame) frame = window.requestAnimationFrame(update) }
    update()
    window.addEventListener('scroll', onScroll, { passive: true })
    return () => { window.cancelAnimationFrame(frame); window.removeEventListener('scroll', onScroll) }
  }, [headerRef])
}
