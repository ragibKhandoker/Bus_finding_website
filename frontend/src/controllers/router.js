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

// How long the page-change loading screen stays up.
export const PAGE_LOADER_MS = 550

// The name shown while a page loads, using the wording from the menus.
export const PAGE_LOADER_NAMES = {
  'Bus details': 'Bus routes', 'Wallet': 'My wallet', 'Profile': 'Account', 'Offers': 'Offers & rewards', 'Map': 'Map & fare calculator',
  'Favourites': 'Favourite buses', 'GPay': 'GPay & recharge', 'Verification': 'Account verification', 'Admin': 'Admin · Users', 'Support': 'Contact admin',
}

// Which Admin page section a profile shortcut should scroll to.
export const ADMIN_FOCUS_KEY = 'sc_admin_focus'
