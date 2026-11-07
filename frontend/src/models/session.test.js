import { beforeEach, describe, expect, it } from 'vitest'
import { continueLastSession, endSession, lastSession, loadSession, saveSessionUser, startSession } from './session'

const admin = { _id: 'a1', name: 'Admin', role: 'admin' }
const rider = { _id: 'u1', name: 'Rider', role: 'commuter' }

// One jsdom window plays every tab. A tab is its window.name plus its sessionStorage.
const snapshot = () => ({ name: window.name, storage: { ...sessionStorage } })
const switchTo = (tab) => {
  window.name = tab.name
  sessionStorage.clear()
  Object.entries(tab.storage).forEach(([key, value]) => sessionStorage.setItem(key, value))
}
// A new tab: nothing of its own. copyFrom: a tab opened from a link or duplicated, which gets a copy of the
// opener's sessionStorage but not its window.name.
const openNewTab = (copyFrom) => switchTo({ name: '', storage: copyFrom ? copyFrom.storage : {} })

beforeEach(() => { localStorage.clear(); sessionStorage.clear(); window.name = '' })

describe('per-tab sign-in', () => {
  it('starts every new tab signed out, even one opened from the admin tab', () => {
    loadSession()
    startSession('admin-token', admin)
    const adminTab = snapshot()

    openNewTab()
    expect(loadSession()).toEqual({ token: null, user: null })

    openNewTab(adminTab) // e.g. a link opened in a new tab copies sessionStorage
    expect(loadSession()).toEqual({ token: null, user: null })
  })

  it('keeps each tab on its own account across refreshes', () => {
    loadSession()
    startSession('admin-token', admin)
    const adminTab = snapshot()

    openNewTab()
    loadSession()
    startSession('rider-token', rider)
    const riderTab = snapshot()

    switchTo(adminTab) // refresh the admin tab
    expect(loadSession()).toEqual({ token: 'admin-token', user: admin })
    switchTo(riderTab) // refresh the rider tab
    expect(loadSession()).toEqual({ token: 'rider-token', user: rider })
  })

  it('offers the most recent sign-in, and a tab that continues with it only signs itself out', () => {
    loadSession()
    startSession('admin-token', admin)

    openNewTab()
    loadSession()
    expect(lastSession()).toEqual({ token: 'admin-token', user: admin })
    expect(continueLastSession()).toEqual({ token: 'admin-token', user: admin })
    expect(loadSession().user).toEqual(admin) // and it stays after a refresh

    expect(endSession()).toEqual({ token: 'admin-token', owned: false })
    // The admin tab still uses that session, so it is still offered.
    expect(lastSession()).toEqual({ token: 'admin-token', user: admin })
  })

  it('a tab that signed in itself ends its session and stops offering it', () => {
    loadSession()
    startSession('admin-token', admin)
    expect(endSession()).toEqual({ token: 'admin-token', owned: true })
    expect(lastSession()).toBeNull()
  })

  it('profile updates only change the offer when it is the same sign-in', () => {
    loadSession()
    startSession('admin-token', admin)
    const adminTab = snapshot()
    openNewTab()
    loadSession()
    startSession('rider-token', rider)

    switchTo(adminTab)
    loadSession()
    saveSessionUser({ ...admin, name: 'Admin Renamed' })
    expect(loadSession().user.name).toBe('Admin Renamed')
    expect(lastSession().user).toEqual(rider)
  })
})
