import { Users } from 'lucide-react'

// Every page has its own URL so the browser's back and forward buttons move between pages.
export const PAGE_PATHS = {
  'Home': '/', 'Find a bus': '/find-a-bus', 'Bus routes': '/bus-routes', 'Ride share': '/ride-share', 'My trips': '/my-trips', 'Wallet': '/wallet',
  'Profile': '/profile', 'Friends': '/friends', 'Messages': '/messages', 'Notifications': '/notifications', 'Offers': '/offers',
  'Map': '/map', 'Favourites': '/favourites', 'GPay': '/gpay', 'Verification': '/verification', 'Admin': '/admin', 'Support': '/support',
}

// Each route also has its own page, e.g. /bus-routes/DBR-12.
export const busPath = (routeNumber) => `/bus-routes/${encodeURIComponent(routeNumber)}`

export const routeFromPath = (path) => {
  const match = path.match(/^\/bus-routes\/([^/]+)\/?$/i)
  return match ? decodeURIComponent(match[1]) : null
}

export const pageFromPath = (path) => {
  if (routeFromPath(path)) return 'Bus details'
  const clean = path.replace(/\/+$/, '').toLowerCase() || '/'
  return Object.keys(PAGE_PATHS).find((page) => PAGE_PATHS[page] === clean) || 'Home'
}

// ---- Browser history ----
// Every in-app entry carries { idx, from } in history.state: idx is how many in-app steps lie behind it (0 = the
// page this tab opened on, e.g. a link opened directly), from is the address it was opened from. The browser
// keeps history.state across reloads, so both survive a refresh. Nothing here keeps a separate stack: the
// browser's own history is the only one.
export const historyIndex = () => Number(window.history.state?.idx) || 0
export const currentUrl = () => window.location.pathname + window.location.search
// The address the current entry was opened from inside the app, or '' when there is none.
export const previousUrl = () => (historyIndex() > 0 && window.history.state?.from) || ''

// A new history entry for a real move to another page (or another search). The page being left first saves how
// far down it was, so Back returns to the same spot. Moving to the address already shown adds nothing.
export function pushEntry(url, state = {}) {
  if (url === currentUrl()) return false
  const current = window.history.state || {}
  const from = currentUrl()
  window.history.replaceState({ ...current, scrollY: window.scrollY }, '', window.location.href)
  window.history.pushState({ ...state, idx: (Number(current.idx) || 0) + 1, from }, '', url)
  return true
}

// Changes the current entry's address (redirects, tidying the address, filters typed on a page) while keeping
// what the entry already holds (its place in the history, the saved scroll position…).
export function replaceEntry(url, state = {}) {
  window.history.replaceState({ ...(window.history.state || {}), ...state }, '', url)
}

// Opens an address inside the app (e.g. a notification's "/friends" link) as normal navigation.
export const NAVIGATE_EVENT = 'sc:navigate'
export const openPath = (path) => window.dispatchEvent(new CustomEvent(NAVIGATE_EVENT, { detail: path }))

// How long the page-change loading screen stays up.
export const PAGE_LOADER_MS = 550

// The name shown while a page loads, using the wording from the menus.
export const PAGE_LOADER_NAMES = {
  'Bus details': 'Bus routes', 'Wallet': 'My wallet', 'Profile': 'Account', 'Offers': 'Offers & rewards', 'Map': 'Map & fare calculator',
  'Favourites': 'Favourite buses', 'GPay': 'GPay & recharge', 'Verification': 'Account verification', 'Admin': 'Admin · Users', 'Support': 'Contact admin',
}

// Which Admin page section a profile shortcut should scroll to.
export const ADMIN_FOCUS_KEY = 'sc_admin_focus'
