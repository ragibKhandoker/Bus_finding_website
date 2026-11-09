import { useLayoutEffect } from 'react'

// Home page scroll motion: each section fades in where it stands the first time it is scrolled to. Nothing moves
// while scrolling (no sliding, no parallax), so the page stays steady. Skipped when the device asks for reduced
// motion.
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
    return () => {
      observer.disconnect()
      page.classList.remove('hp-motion')
    }
  }, [pageRef])
}
