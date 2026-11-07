// The sign-in for this browser tab.
//
// Each tab has its own sign-in, kept in sessionStorage, which survives a refresh of that tab. A new tab always
// starts signed out, so one tab can stay in the admin panel while another signs in as a normal user.
// localStorage only remembers the most recent sign-in, which the sign-in form offers as "Continue as …" in a
// new tab; nothing is signed in until the user chooses it.
//
// Browsers copy sessionStorage into a tab opened from a link, and into a duplicated tab. To tell a refresh
// (keep the sign-in) from a new or copied tab (start fresh), each tab gets an id stored both in window.name,
// which belongs to that one tab and survives a refresh, and in sessionStorage. They only match in the tab
// that wrote them.

const TOKEN = 'sc_token'
const USER = 'sc_user'
const TAB = 'sc_tab_id'
// Set when this tab continued with another tab's sign-in rather than signing in itself.
const BORROWED = 'sc_session_borrowed'
const TAB_PREFIX = 'sc-tab-'

const read = (storage, key) => { try { return storage.getItem(key) } catch { return null } }
const write = (storage, key, value) => { try { value === null ? storage.removeItem(key) : storage.setItem(key, value) } catch { /* storage blocked */ } }
const parse = (text) => { try { return JSON.parse(text || 'null') } catch { return null } }
const clearTab = () => [TOKEN, USER, BORROWED].forEach((key) => write(sessionStorage, key, null))

// Makes sure the sign-in in sessionStorage was written by this tab; a new or copied tab starts empty.
function claimTab() {
  const name = typeof window.name === 'string' && window.name.startsWith(TAB_PREFIX) ? window.name : ''
  if (name && read(sessionStorage, TAB) === name) return
  clearTab()
  const id = `${TAB_PREFIX}${Date.now().toString(36)}${Math.random().toString(36).slice(2, 10)}`
  try { window.name = id } catch { /* the tab just starts fresh on every load */ }
  write(sessionStorage, TAB, id)
}

// This tab's own sign-in, or none for a new tab.
export function loadSession() {
  claimTab()
  const token = read(sessionStorage, TOKEN)
  return { token: token || null, user: token ? parse(read(sessionStorage, USER)) : null }
}

// The most recent sign-in from any tab, offered as "Continue as …" in a tab that is signed out.
export function lastSession() {
  const token = read(localStorage, TOKEN)
  const user = token ? parse(read(localStorage, USER)) : null
  return token && user ? { token, user } : null
}

// Signed in from this tab: it owns the session, and it becomes the one offered to new tabs.
export function startSession(token, user) {
  const text = JSON.stringify(user)
  write(sessionStorage, TOKEN, token)
  write(sessionStorage, USER, text)
  write(sessionStorage, BORROWED, null)
  write(localStorage, TOKEN, token)
  write(localStorage, USER, text)
}

// "Continue as …": this tab uses the most recent sign-in, shared with the tab that made it.
export function continueLastSession() {
  const last = lastSession()
  if (!last) return null
  write(sessionStorage, TOKEN, last.token)
  write(sessionStorage, USER, JSON.stringify(last.user))
  write(sessionStorage, BORROWED, '1')
  return last
}

// Updated profile details for this tab (and for the "Continue as …" offer, when it is the same sign-in).
export function saveSessionUser(user) {
  const token = read(sessionStorage, TOKEN)
  const text = JSON.stringify(user)
  write(sessionStorage, USER, text)
  if (token && read(localStorage, TOKEN) === token) write(localStorage, USER, text)
}

// Signs this tab out. Returns the token and whether this tab owned it, so the caller only ends the session
// on the server when no other tab is relying on it.
export function endSession() {
  const token = read(sessionStorage, TOKEN)
  const owned = read(sessionStorage, BORROWED) !== '1'
  clearTab()
  // Do not offer a sign-in that has just been closed.
  if (token && owned && read(localStorage, TOKEN) === token) {
    write(localStorage, TOKEN, null)
    write(localStorage, USER, null)
  }
  return { token, owned }
}

// Forgets the "Continue as …" offer when its session no longer works.
export function forgetLastSession(token) {
  if (token && read(localStorage, TOKEN) === token) {
    write(localStorage, TOKEN, null)
    write(localStorage, USER, null)
  }
}
