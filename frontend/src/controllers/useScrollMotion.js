import { useLayoutEffect } from 'react'

// Home page scroll motion: each section rises into view the first time it is scrolled to, and the hero
// preview drifts at its own pace while the hero is on screen. Skipped when the device asks for reduced motion.
export function useScrollMotion(pageRef) {
  // Runs before the first paint so sections below the fold never flash in and then hide again.
  useLayoutEffect(() => {
    const page = pageRef.current
    if (!page || !('IntersectionObserver' in window) || matchMedia('(prefers-reduced-motion: reduce)').matches) return
    page.classList.add('hp-motion')
    const observer = new IntersectionObserver((entries) => {
      entries.forEach((entry) => {
        if (!entry.isIntersecting) return
        entry.target.classList.add('is-in')
        observer.unobserve(entry.target)
      })
    }, { rootMargin: '0px 0px -12% 0px' })
    page.querySelectorAll('.hp-section, .hp-cta').forEach((section) => observer.observe(section))

    const hero = page.querySelector('.hp-hero')
    const preview = page.querySelector('.hp-preview')
    let frame = 0
    const onScroll = () => {
      if (frame) return
      frame = requestAnimationFrame(() => {
        frame = 0
        if (hero && preview) preview.style.setProperty('--hp-y', `${Math.min(window.scrollY, hero.offsetHeight)}px`)
      })
    }
    window.addEventListener('scroll', onScroll, { passive: true })
    return () => {
      observer.disconnect()
      window.removeEventListener('scroll', onScroll)
      cancelAnimationFrame(frame)
      page.classList.remove('hp-motion')
    }
  }, [pageRef])
}
