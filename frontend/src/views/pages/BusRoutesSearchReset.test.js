import { beforeEach, describe, expect, it } from 'vitest'
import { forgetRoutesSearchUnlessReturning } from './BusRoutesPage'
import { PERSIST_PREFIX } from '../../controllers/usePersistentState'

const typed = () => sessionStorage.getItem(PERSIST_PREFIX + 'routes:query')
const type = (text) => sessionStorage.setItem(PERSIST_PREFIX + 'routes:query', JSON.stringify(text))

describe('Bus routes search', () => {
  beforeEach(() => { sessionStorage.clear(); window.history.replaceState(null, '', '/') })

  it('is cleared when Bus routes is opened from a tab or a link', () => {
    window.history.pushState({ idx: 1 }, '', '/bus-routes')
    forgetRoutesSearchUnlessReturning()
    type('Raida')
    // Another tab, then Bus routes again: a new entry, so a clear list.
    window.history.pushState({ idx: 2 }, '', '/')
    window.history.pushState({ idx: 3 }, '', '/bus-routes')
    forgetRoutesSearchUnlessReturning()
    expect(typed()).toBeNull()
  })

  it('is kept when going back to the same list, e.g. from a route’s page', () => {
    window.history.pushState({ idx: 1 }, '', '/bus-routes')
    forgetRoutesSearchUnlessReturning()
    type('Raida')
    const listEntry = window.history.state
    // Opening a route and pressing Back brings the list's own entry back.
    window.history.pushState({ idx: 2 }, '', '/bus-routes/DBR-12')
    window.history.replaceState(listEntry, '', '/bus-routes')
    forgetRoutesSearchUnlessReturning()
    expect(JSON.parse(typed())).toBe('Raida')
  })
})
