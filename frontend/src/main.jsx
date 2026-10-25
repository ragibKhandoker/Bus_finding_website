import React, { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { createRoot } from 'react-dom/client'
import {
  ArrowDownUp, ArrowLeft, ArrowRight, ArrowUpRight, BadgeCheck, BusFront, CalendarDays, Check, ChevronDown,
  CircleAlert, CircleHelp, Clock3, Compass, Copy, CreditCard, Eye, EyeOff, Gift, GraduationCap, Heart, History, Home, KeyRound, Languages, Lock, LogOut,
  Mail, Map as MapIcon, MapPin, Menu, MessageCircle, Moon, MoonStar, Pencil, Phone, Plus, Route, Search, ShieldCheck, Smartphone, Sparkles, Sun, UserRound, Users, Wallet, X,
} from 'lucide-react'
import './theme.css'
import './style.css'
import './home.css'
import './profile.css'
import './admin.css'
import GoogleMapView, { mapsConfigured } from './GoogleMapView'
import { ActiveTripPanel, TripRow, useActiveTrip, useBoardControls, useTripChanges } from './Trips'
import { BusReviews, ChatPage, FriendsPage, GpayPage, MapPage, NotificationsPage, OffersPage, VerificationPage } from './MorePages'
import { API_BASE, UNAUTHORIZED_EVENT, apiRequest as request, isTokenExpired, tokenExpiry } from './api'
import { formatDate, formatNumber, getLang, setLang, t, toEnglish } from './i18n'
import { getTheme, setTheme, syncThemeColor } from './theme'

const money = (value) => `৳${formatNumber(value)}`
// Server messages are in English; the ones with a Bangla entry are shown translated.
const errorText = (error) => t(error?.message || 'Something went wrong. Please try again.')
const getStops = (bus) => Array.isArray(bus.stops) ? bus.stops.map((stop) => typeof stop === 'string' ? stop : stop.name).filter(Boolean) : []

// Every page has its own URL so the browser's back and forward buttons move between pages.
const PAGE_PATHS = {
  'Home': '/', 'Find a bus': '/find-a-bus', 'Bus routes': '/bus-routes', 'Ride share': '/ride-share', 'My trips': '/my-trips', 'Wallet': '/wallet',
  'Profile': '/profile', 'Friends': '/friends', 'Messages': '/messages', 'Notifications': '/notifications', 'Offers': '/offers',
  'Map': '/map', 'GPay': '/gpay', 'Verification': '/verification', 'Admin': '/admin',
}
// Each route also has its own page, e.g. /bus-routes/DBR-12.
const busPath = (routeNumber) => `/bus-routes/${encodeURIComponent(routeNumber)}`
const routeFromPath = (path) => {
  const match = path.match(/^\/bus-routes\/([^/]+)\/?$/i)
  return match ? decodeURIComponent(match[1]) : null
}
const pageFromPath = (path) => {
  if (routeFromPath(path)) return 'Bus details'
  const clean = path.replace(/\/+$/, '').toLowerCase() || '/'
  return Object.keys(PAGE_PATHS).find((page) => PAGE_PATHS[page] === clean) || 'Home'
}

function App() {
  // A saved token that has already expired is dropped before the first render.
  const [token, setToken] = useState(() => {
    const saved = localStorage.getItem('sc_token')
    if (saved && isTokenExpired(saved)) { localStorage.removeItem('sc_token'); localStorage.removeItem('sc_user'); return null }
    return saved
  })
  const [user, setUser] = useState(() => { try { return token ? JSON.parse(localStorage.getItem('sc_user') || 'null') : null } catch { return null } })
  const [page, setPage] = useState(() => pageFromPath(window.location.pathname))
  // The route shown on the 'Bus details' page.
  const [routeNumber, setRouteNumber] = useState(() => routeFromPath(window.location.pathname))
  const [mobileMenu, setMobileMenu] = useState(false)
  const [moreMenu, setMoreMenu] = useState(false)
  const [toast, setToast] = useState('')
  const [authMode, setAuthMode] = useState('')
  // Re-renders the whole app in the chosen language; t() reads the language set by setLang.
  const [lang, setLangState] = useState(getLang)
  const toggleLang = () => { const next = lang === 'bn' ? 'en' : 'bn'; setLang(next); setLangState(next) }
  const [theme, setThemeState] = useState(getTheme)
  const changeTheme = (next) => { setTheme(next); setThemeState(next) }
  useEffect(() => { syncThemeColor() }, [])

  const notify = useCallback((message) => {
    setToast(message)
    window.setTimeout(() => setToast(''), 3600)
  }, [])
  // Shows a page and records it in the browser history; replace swaps the current entry instead of adding one.
  // 'Bus details' also takes the route to show.
  const showPage = useCallback((target, { replace = false, route = null } = {}) => {
    const path = target === 'Bus details' && route ? busPath(route) : PAGE_PATHS[target] || '/'
    if (window.location.pathname !== path) window.history[replace ? 'replaceState' : 'pushState']({ page: target }, '', path)
    setPage(target)
    setRouteNumber(route)
    setMobileMenu(false)
    setMoreMenu(false)
  }, [])
  // An unknown address shows the home page, so put the matching URL in the address bar.
  useEffect(() => {
    if (routeFromPath(window.location.pathname)) return
    const path = PAGE_PATHS[pageFromPath(window.location.pathname)]
    if (window.location.pathname !== path) window.history.replaceState({ page: 'Home' }, '', path)
  }, [])
  // Back and forward: show whichever page the browser moved to.
  useEffect(() => {
    const onPop = () => {
      setPage(pageFromPath(window.location.pathname))
      setRouteNumber(routeFromPath(window.location.pathname))
      setMobileMenu(false)
      setMoreMenu(false)
      setAuthMode('')
      window.scrollTo(0, 0)
    }
    window.addEventListener('popstate', onPop)
    return () => window.removeEventListener('popstate', onPop)
  }, [])
  // revoke: also end the session on the server. Skipped when the server already rejected the token.
  const signOut = useCallback((message = t('You have been signed out.'), { revoke = true } = {}) => {
    const current = localStorage.getItem('sc_token')
    if (revoke && current) {
      request('/auth/logout', { token: current, method: 'POST', ignoreUnauthorized: true }).catch(() => {})
    }
    localStorage.removeItem('sc_token')
    localStorage.removeItem('sc_user')
    setToken(null)
    setUser(null)
    showPage('Home')
    notify(message)
  }, [notify, showPage])
  // Any request that comes back 401 with our token means the sign-in expired or the account is gone.
  useEffect(() => {
    const expired = () => signOut(t('Your session has expired. Please sign in again.'), { revoke: false })
    window.addEventListener(UNAUTHORIZED_EVENT, expired)
    return () => window.removeEventListener(UNAUTHORIZED_EVENT, expired)
  }, [signOut])
  // Sign out by itself the moment the token expires, even if no request is made.
  useEffect(() => {
    const expiry = token && tokenExpiry(token)
    if (!expiry) return
    // setTimeout overflows past ~24.8 days, so cap the wait; the effect re-arms when the token changes.
    const timer = window.setTimeout(() => signOut(t('Your session has expired. Please sign in again.'), { revoke: false }), Math.min(Math.max(expiry - Date.now(), 0), 2 ** 31 - 1))
    return () => window.clearTimeout(timer)
  }, [token, signOut])
  // Keep other open tabs in step when this tab signs in or out.
  useEffect(() => {
    const sync = (event) => {
      if (event.key !== 'sc_token') return
      if (!event.newValue) { setToken(null); setUser(null); showPage('Home'); return }
      setToken(event.newValue)
      try { setUser(JSON.parse(localStorage.getItem('sc_user') || 'null')) } catch { setUser(null) }
    }
    window.addEventListener('storage', sync)
    return () => window.removeEventListener('storage', sync)
  }, [showPage])
  // Check a saved sign-in once on load and refresh the stored profile from the server.
  useEffect(() => {
    if (!token) return
    request('/auth/validate', { token }).then((data) => {
      if (data.user) { localStorage.setItem('sc_user', JSON.stringify(data.user)); setUser(data.user) }
    }).catch(() => {})
  }, []) // eslint-disable-line react-hooks/exhaustive-deps
  const signedIn = Boolean(token && user)
  const isAdmin = signedIn && user.role?.toLowerCase() === 'admin'
  const navigate = (target, options) => {
    showPage(target, options)
    window.scrollTo(0, 0)
  }

  return (
    <div className="app-shell">
      <header className="topbar">
        <button className="brand" onClick={() => navigate('Home')} aria-label={t('SmartCommute home')}>
          <span className="brand-icon"><BusFront size={19} strokeWidth={2.5} /></span>
          <span>smart<span className="brand-accent">commute</span><small>{t('DHAKA, MADE EASIER')}</small></span>
        </button>
        <button className="mobile-menu" onClick={() => setMobileMenu(!mobileMenu)} aria-label={t('Toggle navigation')}>
          {mobileMenu ? <X /> : <Menu />}
        </button>
        <nav className={mobileMenu ? 'nav-links open' : 'nav-links'}>
          {['Home', 'Find a bus', 'Bus routes', 'Ride share', 'My trips'].map((item) => (
            <button key={item} className={page === item || (item === 'Bus routes' && page === 'Bus details') ? 'nav-link active' : 'nav-link'} onClick={() => navigate(item)}>{t(item)}</button>
          ))}
          <div className="more-nav-wrap">
            <button className={['Friends', 'Messages', 'Notifications', 'Offers', 'Map', 'GPay', 'Verification', 'Admin'].includes(page) ? 'nav-link active' : 'nav-link'} aria-expanded={moreMenu} onClick={() => setMoreMenu(!moreMenu)}>{t('More')} <ChevronDown size={13} /></button>
            {moreMenu && <div className="more-nav-menu">{[['Friends','Friends'],['Messages','Messages'],['Notifications','Notifications'],['Offers','Offers & rewards'],['Map','Explore map'],['GPay','GPay & recharge'],['Verification','Student verification'], ...(isAdmin ? [['Admin', 'Admin · Users']] : [])].map(([target,label]) => <button key={target} className={page === target ? 'current' : ''} onClick={() => navigate(target)}>{t(label)}<ArrowRight size={13} /></button>)}</div>}
          </div>
        </nav>
        <div className="top-actions">
          <ThemeMenu theme={theme} onChange={changeTheme} />
          <button className="lang-toggle" onClick={toggleLang} title={lang === 'bn' ? 'Switch to English' : 'বাংলায় দেখুন'} aria-label={lang === 'bn' ? 'Switch to English' : 'বাংলায় দেখুন'}>
            <Languages size={16} /><span className="lang-full">{lang === 'bn' ? 'English' : 'বাংলা'}</span><span className="lang-short">{lang === 'bn' ? 'EN' : 'বাং'}</span>
          </button>
          {signedIn ? (
            <>
              <button className="icon-button" title={t('My wallet')} onClick={() => navigate('Wallet')}><Wallet size={18} /></button>
              <button className="profile-chip" onClick={() => navigate('Profile')}>
                <span className="avatar">{user.name?.[0]?.toUpperCase() || 'U'}</span>
                <span>{user.name?.split(' ')[0] || t('Account')}</span><ChevronDown size={14} />
              </button>
            </>
          ) : <button className="button button-dark button-small" onClick={() => setAuthMode('login')}>{t('Sign in')} <ArrowRight size={15} /></button>}
        </div>
      </header>

      <main>
        {page === 'Home' && <HomePage user={user} token={token} signedIn={signedIn} navigate={navigate} openAuth={() => setAuthMode('login')} />}
        {page === 'Find a bus' && <BusPage token={token} signedIn={signedIn} user={user} notify={notify} navigate={navigate} openAuth={() => setAuthMode('login')} />}
        {page === 'Bus routes' && <BusRoutesPage navigate={navigate} />}
        {page === 'Bus details' && <BusDetailPage key={routeNumber} routeNumber={routeNumber} user={user} signedIn={signedIn} notify={notify} navigate={navigate} />}
        {page === 'Ride share' && <RidePage token={token} user={user} signedIn={signedIn} notify={notify} openAuth={() => setAuthMode('login')} />}
        {page === 'My trips' && <TripsPage token={token} signedIn={signedIn} notify={notify} navigate={navigate} openAuth={() => setAuthMode('login')} />}
        {page === 'Wallet' && <WalletPage token={token} signedIn={signedIn} openAuth={() => setAuthMode('login')} />}
        {page === 'Profile' && <ProfilePage user={user} token={token} signedIn={signedIn} updateUser={setUser} signOut={signOut} openAuth={() => setAuthMode('login')} notify={notify} />}
        {page === 'Friends' && <FriendsPage token={token} user={user} signedIn={signedIn} openAuth={() => setAuthMode('login')} onChat={(friend) => { sessionStorage.setItem('chat_contact_id', friend.id || friend._id); navigate('Messages') }} notify={notify} />}
        {page === 'Messages' && <ChatPage token={token} user={user} signedIn={signedIn} openAuth={() => setAuthMode('login')} notify={notify} />}
        {page === 'Notifications' && <NotificationsPage user={user} signedIn={signedIn} openAuth={() => setAuthMode('login')} notify={notify} />}
        {page === 'Offers' && <OffersPage token={token} signedIn={signedIn} openAuth={() => setAuthMode('login')} notify={notify} />}
        {page === 'Map' && <MapPage token={token} user={user} signedIn={signedIn} openAuth={() => setAuthMode('login')} notify={notify} />}
        {page === 'GPay' && <GpayPage token={token} signedIn={signedIn} openAuth={() => setAuthMode('login')} notify={notify} />}
        {page === 'Verification' && <VerificationPage token={token} user={user} signedIn={signedIn} openAuth={() => setAuthMode('login')} notify={notify} />}
        {page === 'Admin' && <AdminPage token={token} signedIn={signedIn} isAdmin={isAdmin} openAuth={() => setAuthMode('admin')} notify={notify} />}
      </main>

      <footer className="site-footer">
        <div className="site-footer-inner">
          <div className="site-footer-about">
            <span className="footer-brand"><span className="brand-icon"><BusFront size={16} strokeWidth={2.5} /></span> {t('SmartCommute')}</span>
            <p>{t('Bus routes, ride sharing and a travel wallet for everyday commuters in Dhaka.')}</p>
            <button onClick={() => navigate('Profile')}><CircleHelp size={14} /> {t('Help & account')}</button>
          </div>
          {[
            ['Commute', [['Find a bus', 'Find a bus'], ['Ride share', 'Ride share'], ['Map', 'Explore map'], ['My trips', 'My trips']]],
            ['Payments', [['Wallet', 'Travel wallet'], ['GPay', 'GPay & recharge'], ['Offers', 'Offers & rewards']]],
            ['Community', [['Friends', 'Friends'], ['Messages', 'Messages'], ['Notifications', 'Notifications'], ['Verification', 'Student verification']]],
          ].map(([heading, links]) => <nav key={heading} className="site-footer-col" aria-label={t(heading)}><b>{t(heading)}</b>{links.map(([target, label]) => <button key={target} onClick={() => navigate(target)}>{t(label)}</button>)}</nav>)}
        </div>
        <div className="site-footer-bottom"><span>© {formatNumber(new Date().getFullYear(), { useGrouping: false })} {t('SmartCommute Dhaka')}</span><span>{t('Making every trip a little simpler.')}</span></div>
      </footer>
      {toast && <div className="toast"><Check size={16} /> {toast}</div>}
      {authMode && <AuthModal mode={authMode} onMode={setAuthMode} onClose={() => setAuthMode('')} onSuccess={(nextUser, nextToken, { admin = false } = {}) => {
        localStorage.setItem('sc_user', JSON.stringify(nextUser))
        localStorage.setItem('sc_token', nextToken)
        setToken(nextToken)
        setUser(nextUser)
        setAuthMode('')
        if (admin) navigate('Admin')
        notify(nextUser.name ? t('Welcome, {name}!', { name: nextUser.name.split(' ')[0] }) : t('Welcome!'))
      }} />}
    </div>
  )
}

// swatch: a preview of each theme's own colours, so it is styled inline and does not follow the current theme.
const THEME_OPTIONS = [
  { value: 'light', icon: Sun, label: 'Light', text: 'Bright and clear, for daytime', swatch: { background: '#ffffff', color: '#d98a00', borderColor: '#e2e9e4' } },
  { value: 'dark', icon: Moon, label: 'Dark', text: 'Dim colours, easy on the eyes', swatch: { background: '#1a1e1c', color: '#cbe6db', borderColor: '#36403b' } },
  { value: 'night', icon: MoonStar, label: 'Night', text: 'Deep blue with warm text for late hours', swatch: { background: '#0d111e', color: '#ded4bf', borderColor: '#2b324c' } },
]

function ThemeMenu({ theme, onChange }) {
  const [open, setOpen] = useState(false)
  const ref = useRef(null)
  // Clicking anywhere else or pressing Escape closes the menu.
  useEffect(() => {
    if (!open) return
    const close = (event) => { if (event.type === 'keydown' ? event.key === 'Escape' : !ref.current?.contains(event.target)) setOpen(false) }
    document.addEventListener('mousedown', close)
    document.addEventListener('keydown', close)
    return () => { document.removeEventListener('mousedown', close); document.removeEventListener('keydown', close) }
  }, [open])
  const Current = THEME_OPTIONS.find((option) => option.value === theme)?.icon || Sun
  return <div className="theme-switch" ref={ref}>
    <button className="theme-toggle" aria-haspopup="menu" aria-expanded={open} title={t('Change theme')} aria-label={t('Change theme')} onClick={() => setOpen(!open)}><Current size={17} /></button>
    {open && <div className="theme-menu" role="menu" aria-label={t('Theme')}>
      <small className="theme-menu-title">{t('Theme')}</small>
      {THEME_OPTIONS.map(({ value, icon: Icon, label, text, swatch }) => <button key={value} role="menuitemradio" aria-checked={theme === value} className={theme === value ? 'selected' : ''} onClick={() => { onChange(value); setOpen(false) }}>
        <span className="theme-swatch" style={swatch}><Icon size={16} /></span>
        <span><b>{t(label)}</b><small>{t(text)}</small></span>
        {theme === value && <Check size={16} className="theme-check" />}
      </button>)}
    </div>}
  </div>
}

// Shortcuts shown under the home search; each one runs the same search as typing the stops in.

// Secondary tools listed on the home page; each opens its own page, which handles sign-in itself.
const HOME_TOOLS = [
  { target: 'Map', icon: MapIcon, title: 'Explore map', text: 'See stops and places around you.' },
  { target: 'Offers', icon: Gift, title: 'Offers & rewards', text: 'Discounts and points on every ride.' },
  { target: 'GPay', icon: Smartphone, title: 'GPay & recharge', text: 'Top up your travel balance in seconds.' },
  { target: 'Verification', icon: GraduationCap, title: 'Student verification', text: 'Unlock student fares with your ID.' },
  { target: 'Friends', icon: UserRound, title: 'Friends', text: 'Add commuters with a friend code.' },
  { target: 'Messages', icon: MessageCircle, title: 'Messages', text: 'Coordinate pickups with your ride crew.' },
]

// Route and stop totals stored in the database. Shown to every visitor, signed in or not.
function BusStatsCard({ stats, onRetry, onSeeAll }) {
  if (stats.status === 'error') {
    return <div className="hp-stats is-error" role="status">
      <CircleAlert size={18} />
      <span><b>{t('Route count unavailable')}</b><small>{t('Could not reach the bus database.')}</small></span>
      <button type="button" onClick={onRetry}>{t('Try again')}</button>
    </div>
  }
  const loading = stats.status === 'loading'
  const value = (number) => loading ? <i className="hp-stat-skeleton" aria-hidden="true" /> : formatNumber(number)
  return <div className="hp-stats" aria-busy={loading} aria-live="polite">
    <div className="hp-stat"><span className="hp-stat-icon"><BusFront size={18} /></span><span><b>{value(stats.routes)}</b><small>{t('Bus routes')}</small></span></div>
    <div className="hp-stat"><span className="hp-stat-icon"><MapPin size={18} /></span><span><b>{value(stats.stops)}</b><small>{t('Bus stops')}</small></span></div>
    {loading
      ? <small className="hp-stats-source">{t('Loading…')}</small>
      : <button type="button" className="hp-stats-source hp-stats-link" onClick={onSeeAll}>{t('See all routes and stops')} <ArrowRight size={14} /></button>}
  </div>
}

const coordinate = (value) => formatNumber(value, { minimumFractionDigits: 4, maximumFractionDigits: 4, useGrouping: false })

const sortedStops = (bus) => [...(bus?.stops || [])].sort((a, b) => (a.order ?? 0) - (b.order ?? 0))

// Length along the stops in km, stop to stop in straight lines, so it is an estimate of the road distance.
const routeLengthKm = (stops) => stops.slice(1).reduce((sum, stop, i) => {
  const prev = stops[i], rad = Math.PI / 180
  const a = Math.sin((stop.lat - prev.lat) * rad / 2) ** 2 + Math.cos(prev.lat * rad) * Math.cos(stop.lat * rad) * Math.sin((stop.lng - prev.lng) * rad / 2) ** 2
  return sum + 12742 * Math.asin(Math.sqrt(a))
}, 0)

const BUS_TYPES = ['general', 'semi-seating', 'seating']

// The route list is fetched once per visit and shared by every page. A copy is kept in localStorage so a
// returning visitor sees the routes at once while the fresh list loads in the background.
const ROUTES_STORAGE_KEY = 'sc_bus_routes'
let routesCache = (() => {
  try { const saved = JSON.parse(localStorage.getItem(ROUTES_STORAGE_KEY) || 'null'); return Array.isArray(saved) ? saved : null } catch { return null }
})()
let routesRequest = null
let routesFresh = false
const fetchBusRoutes = () => {
  routesRequest ??= request('/bus/all').then((data) => {
    routesCache = Array.isArray(data.data) ? data.data : []
    routesFresh = true
    try { localStorage.setItem(ROUTES_STORAGE_KEY, JSON.stringify(routesCache)) } catch { /* just not kept for next time */ }
    return routesCache
  }).finally(() => { routesRequest = null })
  return routesRequest
}
const findCachedRoute = (routeNumber) => routesCache?.find((bus) => bus.routeNumber?.toLowerCase() === routeNumber?.toLowerCase())

// Every route with its stops, from the database. status: 'loading', 'ready' or 'error'.
function useBusRoutes() {
  const [routes, setRoutes] = useState(() => routesCache ? { status: 'ready', buses: routesCache } : { status: 'loading' })
  const load = useCallback(() => {
    if (routesFresh) { setRoutes({ status: 'ready', buses: routesCache }); return }
    if (!routesCache) setRoutes({ status: 'loading' })
    fetchBusRoutes()
      .then((buses) => setRoutes({ status: 'ready', buses }))
      .catch(() => { if (!routesCache) setRoutes({ status: 'error' }) })
  }, [])
  useEffect(() => { load() }, [load])
  return [routes, load]
}

// Loading, error and empty states shared by the route lists; null once there are routes to show.
function RoutesStatus({ state, onRetry }) {
  if (state.status === 'loading') return <div className="empty-state"><span className="spinner" /> {t('Loading bus routes…')}</div>
  if (state.status === 'error') return <div className="empty-state panel"><span className="empty-icon"><BusFront size={22} /></span><b>{t('Bus routes could not be loaded')}</b><span>{t('Could not reach the bus database.')}</span><button className="button button-outline button-small" onClick={onRetry}>{t('Try again')}</button></div>
  if (!state.buses.length) return <div className="empty-state panel"><span className="empty-icon"><BusFront size={22} /></span><b>{t('No bus routes yet')}</b></div>
  return null
}

// A route in a list: where it runs and how many stops. Opens the route's own page.
function RouteTile({ bus, onOpen }) {
  const stops = sortedStops(bus)
  const first = stops[0]?.name, last = stops[stops.length - 1]?.name
  return <button type="button" className="hp-route hp-route-tile" onClick={() => onOpen(bus)}>
    <span className="hp-route-head">
      {bus.routeNumber && <span className="hp-route-no">{bus.routeNumber}</span>}
      <span><b>{t(bus.busName)}</b><small>{[bus.operator, bus.busType].filter(Boolean).map((text) => t(text)).join(' · ')}</small></span>
    </span>
    {first && <span className="hp-route-ends"><span><i className="start" />{t(first)}</span><ArrowRight size={14} /><span><i className="end" />{t(last)}</span></span>}
    <span className="hp-route-foot">{t('{count} stops', { count: stops.length })}<span>{t('View details')} <ArrowRight size={14} /></span></span>
  </button>
}

// Well-known routes shown on the home page; the full list has its own page.
const FEATURED_ROUTES = ['Raida', 'Turag', 'Victor Classic', 'Projapati', 'Bihanga', 'Akash']
const FEATURED_COUNT = 6

function RoutePreview({ state, onRetry, navigate }) {
  const buses = state.status === 'ready' ? state.buses.filter((bus) => bus.routeNumber) : []
  const featured = FEATURED_ROUTES.map((name) => buses.find((bus) => bus.busName === name)).filter(Boolean)
  const shown = [...new Set([...featured, ...buses])].slice(0, FEATURED_COUNT)
  return <section className="hp-section">
    <div className="hp-section-head hp-head-row">
      <div>
        <span className="hp-kicker">{t('Route database')}</span>
        <h2>{t('Popular bus routes')}</h2>
        <p>{t('A few well-known routes. Open one to see every stop, the fare and where it runs.')}</p>
      </div>
      {state.status === 'ready' && <button className="button button-outline button-small" onClick={() => navigate('Bus routes')}>{t('See all {count} routes', { count: buses.length })} <ArrowRight size={14} /></button>}
    </div>
    {RoutesStatus({ state, onRetry }) || <div className="hp-routes hp-routes-3">{shown.map((bus) => <RouteTile key={bus._id} bus={bus} onOpen={() => navigate('Bus details', { route: bus.routeNumber })} />)}</div>}
  </section>
}

const ROUTES_PAGE_SIZE = 24

// Every route stored in the database, searchable by bus, route number or stop and filterable by bus type.
function BusRoutesPage({ navigate }) {
  const [state, load] = useBusRoutes()
  const [query, setQuery] = useState('')
  const [type, setType] = useState('all')
  const [limit, setLimit] = useState(ROUTES_PAGE_SIZE)
  const term = query.trim().toLowerCase()
  // Matches the English names stored in the database and their Bangla, so either language can be typed.
  const buses = state.status === 'ready' ? state.buses.filter((bus) => bus.routeNumber && (type === 'all' || bus.busType === type) &&
    (!term || [bus.busName, bus.operator, bus.routeNumber, ...(bus.stops || []).map((s) => s.name)]
      .some((text) => text && (text.toLowerCase().includes(term) || t(text).toLowerCase().includes(term))))) : []
  const filter = (next) => { setType(next); setLimit(ROUTES_PAGE_SIZE) }
  return <div className="page inner-page">
    <PageHeading eyebrow={t('ROUTE DATABASE')} title={t('Bus routes in Dhaka')} description={t('Every route in the database. Open a route to see its stops in order, the fare and where each stop is.')} icon={Route} />
    <section className="panel routes-toolbar">
      <label className="hp-route-search"><Search size={16} /><input value={query} onChange={(e) => { setQuery(e.target.value); setLimit(ROUTES_PAGE_SIZE) }} placeholder={t('Search a bus or a stop')} /></label>
      <div className="segmented routes-filter">{['all', ...BUS_TYPES].map((option) => <button key={option} className={type === option ? 'selected' : ''} onClick={() => filter(option)}>{option === 'all' ? t('All buses') : t(option)}</button>)}</div>
    </section>
    <section className="results-area">
      {RoutesStatus({ state, onRetry: load }) || <>
        <div className="results-heading"><div><div className="eyebrow">{t('ALL ROUTES')}</div><h2>{term ? t('Routes matching “{query}”', { query: query.trim() }) : t('Bus routes')}</h2></div><span className="result-count">{t('{count} routes', { count: buses.length })}</span></div>
        {buses.length
          ? <div className="hp-routes hp-routes-3">{buses.slice(0, limit).map((bus) => <RouteTile key={bus._id} bus={bus} onOpen={() => navigate('Bus details', { route: bus.routeNumber })} />)}</div>
          : <div className="empty-state panel"><b>{t('No route matches “{query}”', { query: query.trim() })}</b><span>{t('Try another bus name or stop.')}</span></div>}
        {buses.length > limit && <div className="routes-more"><button className="button button-outline" onClick={() => setLimit(limit + ROUTES_PAGE_SIZE)}>{t('Show more routes ({count} left)', { count: buses.length - limit })} <ChevronDown size={15} /></button></div>}
      </>}
    </section>
  </div>
}

// One route's own page: what it is, the fare, and every stop in order with its location.
function BusDetailPage({ routeNumber, user, signedIn, notify, navigate }) {
  // A route already in the shared list shows at once; otherwise only this route is fetched.
  const [state, setState] = useState(() => findCachedRoute(routeNumber) ? { status: 'ready', bus: findCachedRoute(routeNumber) } : { status: 'loading' })
  const load = useCallback(() => {
    if (routesFresh && findCachedRoute(routeNumber)) return
    if (!findCachedRoute(routeNumber)) setState({ status: 'loading' })
    request(`/bus/route/${encodeURIComponent(routeNumber)}`)
      .then((data) => setState({ status: 'ready', bus: data.data }))
      .catch((error) => { if (!findCachedRoute(routeNumber)) setState({ status: 'error', message: errorText(error) }) })
  }, [routeNumber])
  useEffect(() => { load() }, [load])
  const back = <button className="route-back" onClick={() => navigate('Bus routes')}><ArrowLeft size={15} /> {t('All bus routes')}</button>
  if (state.status !== 'ready') {
    return <div className="page inner-page">{back}
      {state.status === 'loading'
        ? <div className="empty-state"><span className="spinner" /> {t('Loading bus routes…')}</div>
        : <div className="empty-state panel"><span className="empty-icon"><BusFront size={22} /></span><b>{t('Bus route could not be loaded')}</b><span>{state.message}</span><button className="button button-outline button-small" onClick={load}>{t('Try again')}</button></div>}
    </div>
  }
  const { bus } = state
  const stops = sortedStops(bus)
  const first = stops[0], last = stops[stops.length - 1]
  const km = routeLengthKm(stops)
  const planTrip = () => { sessionStorage.setItem('bus_query', JSON.stringify({ mode: 'route', from: first.name, to: last.name, auto: true })); navigate('Find a bus') }
  return <div className="page inner-page route-detail">
    {back}
    <section className="panel route-hero">
      <div className="route-hero-head">
        <span className="hp-route-no route-hero-no">{bus.routeNumber}</span>
        <div>
          <h1>{t(bus.busName)}</h1>
          <p>{[bus.operator, bus.busType, bus.frequency].filter(Boolean).map((text) => t(text)).join(' · ')}</p>
        </div>
      </div>
      {first && <div className="hp-route-ends route-hero-ends"><span><i className="start" />{t(first.name)}</span><ArrowRight size={16} /><span><i className="end" />{t(last.name)}</span></div>}
      <div className="route-facts">
        <span><small>{t('Stops')}</small><b>{formatNumber(stops.length)}</b></span>
        <span><small>{t('Route length')}</small><b>{t('~{km} km', { km: Math.round(km) })}</b></span>
        <span><small>{t('Fare from')}</small><b>{money(bus.base_fare)}</b></span>
        <span><small>{t('Per km')}</small><b>{money(bus.per_km_fare)}</b></span>
      </div>
      {first && <div className="route-hero-actions">
        <button className="button button-green button-small" onClick={planTrip}><Search size={14} /> {t('Search buses on this route')}</button>
        <a className="button button-outline button-small" href={`https://www.openstreetmap.org/?mlat=${first.lat}&mlon=${first.lng}#map=13/${first.lat}/${first.lng}`} target="_blank" rel="noreferrer"><MapIcon size={14} /> {t('Open on the map')}</a>
      </div>}
    </section>
    <section className="panel route-stops-panel">
      <div className="results-heading"><div><div className="eyebrow">{t('STOPS IN ORDER')}</div><h2>{t('Where this bus stops')}</h2></div><span className="result-count">{t('{count} stops', { count: stops.length })}</span></div>
      <ol className="hp-route-stops">
        {stops.map((stop) => <li key={`${stop.order}-${stop.name}`}>
          <span className="hp-stop-no">{formatNumber((stop.order ?? 0) + 1)}</span>
          <span className="hp-stop-name">{t(stop.name)}</span>
          <a href={`https://www.openstreetmap.org/?mlat=${stop.lat}&mlon=${stop.lng}#map=17/${stop.lat}/${stop.lng}`} target="_blank" rel="noreferrer" title={t('Open on the map')}>
            <MapPin size={12} /> {coordinate(stop.lat)}, {coordinate(stop.lng)}
          </a>
        </li>)}
      </ol>
      <p className="route-note">{t('Stop lists come from public listings and the locations are approximate.')}</p>
    </section>
    <section className="panel route-reviews"><BusReviews bus={bus} user={user} signedIn={signedIn} notify={notify} /></section>
  </div>
}

function HomePage({ user, token, signedIn, navigate, openAuth }) {
  const [routes, loadRoutes] = useBusRoutes()
  // The hero buttons show a brief loading state, then open their page.
  const [opening, setOpening] = useState('')
  const openTimer = useRef(0)
  useEffect(() => () => window.clearTimeout(openTimer.current), [])
  const openPage = (target) => {
    setOpening(target)
    openTimer.current = window.setTimeout(() => navigate(target), 600)
  }
  const busStats = routes.status === 'ready'
    ? { status: 'ready', routes: routes.buses.length, stops: new Set(routes.buses.flatMap((bus) => (bus.stops || []).map((stop) => stop.name))).size }
    : { status: routes.status }
  const busCount = busStats.status === 'ready' ? busStats.routes : null
  const firstName = user?.name?.split(' ')[0]
  const pageRef = useRef(null)
  useScrollMotion(pageRef)
  return (
    <div className="home-page" ref={pageRef}>
      <section className="hp-hero">
        <div className="hp-hero-inner">
          <div className="hp-hero-copy">
            <span className="hp-badge"><span className="hp-badge-dot" /> {signedIn ? (firstName ? t('Welcome back, {name}', { name: firstName }) : t('Welcome back')) : t('Built for Dhaka commuters')}</span>
            <h1>{signedIn ? <>{t('Where are you')} <span>{t('heading today?')}</span></> : <>{t('Your daily commute,')} <span>{t('sorted.')}</span></>}</h1>
            <p>{t('Find the right bus across Dhaka, share a ride with people going your way, and keep your fares and trips in one place.')}</p>

            <div className="hp-hero-actions">
              <button type="button" className={`button button-green hp-hero-cta${opening === 'Find a bus' ? ' is-loading' : ''}`} disabled={Boolean(opening)} aria-busy={opening === 'Find a bus'} onClick={() => openPage('Find a bus')}>{opening === 'Find a bus' ? <><span className="button-spinner" aria-hidden="true" /> {t('Opening…')}</> : <><Search size={18} /> {t('Find your bus')}</>}</button>
              <button type="button" className={`button button-outline hp-hero-secondary${opening === 'Bus routes' ? ' is-loading' : ''}`} disabled={Boolean(opening)} aria-busy={opening === 'Bus routes'} onClick={() => openPage('Bus routes')}>{opening === 'Bus routes' ? <><span className="button-spinner dark" aria-hidden="true" /> {t('Opening…')}</> : <><Route size={17} /> {t('Browse all routes')}</>}</button>
            </div>

            <div className="hp-hero-stats">
              <span><b>{busStats.status === 'ready' ? formatNumber(busStats.routes) : '—'}</b><small>{t('Bus routes')}</small></span>
              <span><b>{busStats.status === 'ready' ? formatNumber(busStats.stops) : '—'}</b><small>{t('Bus stops')}</small></span>
              <span><b><MapIcon size={18} /> {t('Live map')}</b><small>{t('Distance & travel time')}</small></span>
            </div>

            <ul className="hp-trust">
              <li><ShieldCheck size={15} /> {t('Secure account sign-in')}</li>
              <li><Route size={15} /> {busCount === null ? t('City-wide bus routes') : t('{count} bus routes', { count: busCount })}</li>
              <li><Users size={15} /> {t('Community ride sharing')}</li>
            </ul>
          </div>

          <div className="hp-preview" aria-hidden="true">
            <div className="hp-map">
              <RouteMap />
              <span className="hp-map-tag hp-tag-start"><i /> {t('Uttara')}</span>
              <span className="hp-map-tag hp-tag-end"><i /> {t('Motijheel')}</span>
            </div>
            <div className="hp-float hp-float-trip">
              <div className="hp-float-head"><span className="hp-bus-chip"><BusFront size={15} /></span><div><b>{t('Uttara → Motijheel')}</b><small>{t('Recommended route')}</small></div><span className="hp-live">{t('LIVE')}</span></div>
              <div className="hp-trip-stats">
                <span><small>{t('Fare')}</small><b>{money(30)}</b></span>
                <span><small>{t('Stops')}</small><b>{formatNumber(14)}</b></span>
                <span><small>{t('Type')}</small><b>{t('AC Bus')}</b></span>
              </div>
              <div className="hp-trip-line"><i /><span /><i /></div>
            </div>
            <div className="hp-float hp-float-ride"><span className="hp-ride-avatars"><i>R</i><i>N</i></span><div><b>{t('Ride share nearby')}</b><small>{t('Banani → Gulshan 2 · 2 seats')}</small></div></div>
          </div>
        </div>
      </section>

      <div className="page">
        <section className="hp-section">
          <div className="hp-section-head hp-head-row hp-services-head">
            <div>
              <span className="hp-kicker">{t('Core services')}</span>
              <h2>{t('Everything your commute needs')}</h2>
              <p>{t('Three tools that cover the everyday trip, from finding a route to paying for it.')}</p>
            </div>
            <BusStatsCard stats={busStats} onRetry={loadRoutes} onSeeAll={() => navigate('Bus routes')} />
          </div>
          <div className="hp-services">
            <button className="hp-service" onClick={() => navigate('Find a bus')}>
              <span className="hp-service-icon green"><BusFront size={22} /></span>
              <b>{t('Find your bus')}</b>
              <p>{t('Search by start and destination, or by bus name. Compare fares, stops and operators, then save your favourites.')}</p>
              <span className="hp-service-link">{busCount === null ? t('Search routes') : t('Browse {count} buses', { count: busCount })} <ArrowRight size={15} /></span>
            </button>
            <button className="hp-service" onClick={() => navigate('Ride share')}>
              <span className="hp-service-icon orange"><Users size={22} /></span>
              <b>{t('Ride together')}</b>
              <p>{t('Join commuters heading the same way or post your own ride. Accept requests and split the cost.')}</p>
              <span className="hp-service-link">{t('Browse ride shares')} <ArrowRight size={15} /></span>
            </button>
            <button className="hp-service" onClick={() => signedIn ? navigate('Wallet') : openAuth()}>
              <span className="hp-service-icon violet"><Wallet size={22} /></span>
              <b>{t('Travel wallet')}</b>
              <p>{t('Check your balance, log the trips you take and keep a clear record of what you spend on travel.')}</p>
              <span className="hp-service-link">{signedIn ? t('Open your wallet') : t('Sign in to get started')} <ArrowRight size={15} /></span>
            </button>
          </div>
        </section>

        <RoutePreview state={routes} onRetry={loadRoutes} navigate={navigate} />

        <section className="hp-section hp-steps-section">
          <div className="hp-section-head">
            <span className="hp-kicker">{t('How it works')}</span>
            <h2>{t('From search to seat in three steps')}</h2>
          </div>
          <ol className="hp-steps">
            <li><span className="hp-step-no">{formatNumber(1)}</span><Search size={20} /><b>{t('Search your route')}</b><p>{t('Enter where you are and where you want to go.')}</p></li>
            <li><span className="hp-step-no">{formatNumber(2)}</span><Clock3 size={20} /><b>{t('Pick the best option')}</b><p>{t('Compare buses by fare, type and stops, or join a shared ride.')}</p></li>
            <li><span className="hp-step-no">{formatNumber(3)}</span><History size={20} /><b>{t('Ride and keep track')}</b><p>{t('Log the trip and see it in your history and wallet.')}</p></li>
          </ol>
        </section>

        <section className="hp-section">
          <div className="hp-section-head hp-head-row">
            <div><span className="hp-kicker">{t('More tools')}</span><h2>{t('Do more with SmartCommute')}</h2></div>
            <button className="button button-outline button-small" onClick={() => navigate('Map')}>{t('Open the map')} <ArrowUpRight size={14} /></button>
          </div>
          <div className="hp-tools">
            {HOME_TOOLS.map(({ target, icon: Icon, title, text }) => <button key={target} className="hp-tool" onClick={() => navigate(target)}><span className="hp-tool-icon"><Icon size={18} /></span><span><b>{t(title)}</b><small>{t(text)}</small></span><ArrowRight size={15} className="hp-tool-arrow" /></button>)}
          </div>
        </section>

        <section className="hp-cta">
          <div>
            <h2>{signedIn ? t('Ready for your next trip?') : t('Start commuting smarter today')}</h2>
            <p>{signedIn ? t('Search a route, or look back at the journeys you have logged.') : t('Create a free account to search buses, share rides and manage your wallet.')}</p>
          </div>
          <div className="hp-cta-actions">
            {signedIn
              ? <><button className="button button-light" onClick={() => navigate('Find a bus')}>{t('Plan a trip')} <ArrowRight size={15} /></button><button className="button button-ghost" onClick={() => navigate('My trips')}>{t('View my trips')}</button></>
              : <><button className="button button-light" onClick={openAuth}>{t('Get started — it\'s free')} <ArrowRight size={15} /></button><button className="button button-ghost" onClick={() => navigate('Find a bus')}>{t('Search buses')}</button></>}
          </div>
        </section>
      </div>
    </div>
  )
}

// Home page scroll motion: each section rises into view the first time it is scrolled to, and the hero
// preview drifts at its own pace while the hero is on screen. Skipped when the device asks for reduced motion.
function useScrollMotion(pageRef) {
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

// Stylised street map behind the home page route preview. Purely decorative.
function RouteMap() {
  const route = 'M92 372 L92 300 Q92 282 110 278 L214 256 Q232 252 236 234 L250 168 Q254 150 272 146 L384 126 Q402 122 408 104 L428 48'
  return <svg viewBox="0 0 520 420" preserveAspectRatio="xMidYMid slice" className="hp-map-svg">
    <rect width="520" height="420" fill="#eef2ec" />
    <path d="M-10 330 C80 300 120 360 210 340 S360 300 530 340 L530 420 L-10 420Z" fill="#d6e7f0" />
    <path d="M-10 330 C80 300 120 360 210 340 S360 300 530 340" stroke="#c4dbe8" strokeWidth="3" fill="none" />
    <rect x="300" y="190" width="96" height="70" rx="10" fill="#d9ead5" />
    <rect x="30" y="70" width="70" height="90" rx="10" fill="#d9ead5" />
    <g fill="#e3e8e1">
      {[[130, 40, 80, 60], [230, 30, 90, 50], [130, 120, 70, 70], [30, 190, 120, 50], [170, 280, 90, 30], [440, 160, 70, 90], [300, 30, 60, 60], [420, 270, 90, 40]].map(([x, y, w, h]) => <rect key={`${x}-${y}`} x={x} y={y} width={w} height={h} rx="6" />)}
    </g>
    <g stroke="#ffffff" strokeLinecap="round" fill="none">
      <path d="M0 110 L520 70" strokeWidth="14" /><path d="M0 250 L520 210" strokeWidth="12" />
      <path d="M118 0 L96 420" strokeWidth="12" /><path d="M262 0 L238 330" strokeWidth="10" />
      <path d="M410 0 L380 330" strokeWidth="10" /><path d="M0 170 L520 140" strokeWidth="6" />
      <path d="M180 0 L170 330" strokeWidth="6" /><path d="M330 0 L310 330" strokeWidth="6" />
    </g>
    <path d={route} stroke="#ffffff" strokeWidth="11" fill="none" strokeLinecap="round" strokeLinejoin="round" />
    <path d={route} stroke="#19865e" strokeWidth="6" fill="none" strokeLinecap="round" strokeLinejoin="round" />
    <path d={route} stroke="#7fd3ad" strokeWidth="2" fill="none" strokeDasharray="2 12" strokeLinecap="round" className="hp-route-flow" />
    {[[92, 330], [150, 270], [236, 234], [250, 168], [330, 136], [408, 104]].map(([cx, cy]) => <circle key={`${cx}-${cy}`} cx={cx} cy={cy} r="5" fill="#fff" stroke="#19865e" strokeWidth="3" />)}
    <circle cx="92" cy="372" r="9" fill="#19865e" stroke="#fff" strokeWidth="4" />
    <circle cx="428" cy="48" r="9" fill="#e5533d" stroke="#fff" strokeWidth="4" />
    <g className="hp-bus-marker" transform="translate(250 200)"><circle r="18" fill="#19865e" opacity=".18" className="hp-pulse" /><circle r="10" fill="#19865e" stroke="#fff" strokeWidth="3" /></g>
  </svg>
}

// Every stop name in the database, offered as suggestions in the place inputs (input list="stop-suggestions").
// Stop names must match the database for route search, so picking one avoids spelling misses.
function StopSuggestions() {
  const [routes] = useBusRoutes()
  const names = useMemo(() => routes.status === 'ready'
    ? [...new Set(routes.buses.flatMap((bus) => (bus.stops || []).map((stop) => stop.name)))].sort((a, b) => a.localeCompare(b))
    : [], [routes])
  return <datalist id="stop-suggestions">{names.map((name) => <option key={name} value={t(name)} />)}</datalist>
}

function PageHeading({ eyebrow, title, description, icon: Icon }) {
  return <div className="page-heading"><span className="heading-icon">{Icon && <Icon size={21} />}</span><div><div className="eyebrow">{eyebrow}</div><h1>{title}</h1><p>{description}</p></div></div>
}

// "1 hr 5 min" / "25 min", in the current language.
const formatDuration = (minutes) => {
  const total = Math.max(1, Math.round(minutes))
  const hours = Math.floor(total / 60), mins = total % 60
  return hours ? (mins ? t('{h} hr {m} min', { h: hours, m: mins }) : t('{h} hr', { h: hours })) : t('{m} min', { m: mins })
}

// Google Maps directions between two places, opened in a new tab (or the Google Maps app on phones).
const googleMapsDirections = (from, to) => `https://www.google.com/maps/dir/?${new URLSearchParams({
  api: '1', origin: `${from}, Dhaka, Bangladesh`, destination: `${to}, Dhaka, Bangladesh`, travelmode: 'driving',
})}`

// Where the distance came from (route-estimate "source").
const ESTIMATE_SOURCES = {
  'google-routes': 'By road, from Google Maps',
  openstreetmap: 'By road, from OpenStreetMap',
  approximate: 'Approximate: road service unavailable',
}

// The searched trip: both places, Google's road distance and travel time, and a link to Google Maps.
// The distance is for the road between the two places, which is not the same as the bus's path or its stops.
function TripSummary({ trip, estimate, onRetry }) {
  return <section className="panel trip-summary">
    <div className="trip-places">
      <span><i className="start" /><small>{t('Start')}</small><b>{t(trip.start.name)}</b></span>
      <ArrowRight size={16} />
      <span><i className="end" /><small>{t('Destination')}</small><b>{t(trip.end.name)}</b></span>
    </div>
    <div className="trip-figures">
      {estimate.status === 'loading' && <span className="trip-figure"><small>{t('Estimated distance')}</small><b><i className="hp-stat-skeleton" aria-hidden="true" /></b></span>}
      {estimate.status === 'ready' && <>
        <span className="trip-figure"><small>{t('Estimated distance')}</small><b>{t('{km} km', { km: formatNumber(estimate.data.distanceKm, { maximumFractionDigits: 1 }) })}</b><em>{t(ESTIMATE_SOURCES[estimate.data.source] || ESTIMATE_SOURCES['google-routes'])}</em></span>
        {estimate.data.durationMinutes > 0 && <span className="trip-figure"><small>{t('Estimated travel time')}</small><b>{formatDuration(estimate.data.durationMinutes)}</b><em>{estimate.data.source === 'google-routes'
          ? (estimate.data.durationWithoutTrafficMinutes ? t('{time} without traffic', { time: formatDuration(estimate.data.durationWithoutTrafficMinutes) }) : t('Driving, with current traffic'))
          : estimate.data.source === 'approximate' ? t('Rough estimate') : t('Driving, without traffic')}</em></span>}
      </>}
      {estimate.status === 'error' && <span className="trip-figure trip-unavailable"><small>{t('Estimated distance')}</small><b>{t('Temporarily unavailable')}</b><em>{estimate.message} <button type="button" onClick={onRetry}>{t('Try again')}</button></em></span>}
      <a className="button button-outline button-small trip-gmaps" href={googleMapsDirections(trip.start.name, trip.end.name)} target="_blank" rel="noreferrer"><MapIcon size={14} /> {t('Open in Google Maps')} <ArrowUpRight size={13} /></a>
    </div>
  </section>
}

function BusPage({ token, signedIn, user, notify, navigate, openAuth }) {
  // A search started from another page (home or a route page); it runs once when the page opens.
  const savedQuery = useMemo(() => {
    try { return JSON.parse(sessionStorage.getItem('bus_query') || '{}') } catch { return {} }
  }, [])
  // Cleared after the first render, not while reading it: React's development mode runs that read twice.
  useEffect(() => { sessionStorage.removeItem('bus_query') }, [])
  const [from, setFrom] = useState(savedQuery.from || '')
  const [to, setTo] = useState(savedQuery.to || '')
  const [name, setName] = useState(savedQuery.name || '')
  const [mode, setMode] = useState(savedQuery.mode === 'name' ? 'name' : 'route')
  const [buses, setBuses] = useState([])
  const [loading, setLoading] = useState(false)
  const [searched, setSearched] = useState(null) // the search shown: { mode, from, to, name }
  const [error, setError] = useState('')
  const [favorites, setFavorites] = useState([])
  const [trip, setTrip] = useState(null) // { start, end }: the stops the searched places resolved to
  const [estimate, setEstimate] = useState({ status: 'idle' })
  const [selectedId, setSelectedId] = useState(null)
  const activeTrip = useActiveTrip(token, signedIn).trip

  useEffect(() => {
    if (!signedIn || !user?.id) return
    request(`/api/favbus/user/${encodeURIComponent(user.id)}`, { token })
      .then((data) => setFavorites((data.data || []).map((favorite) => String(favorite.busId))))
      .catch(() => {})
  }, [signedIn, token, user?.id])

  // Road distance and time from Google, through the backend, which holds the server key.
  const loadEstimate = useCallback((start, end) => {
    setEstimate({ status: 'loading' })
    request(`/bus/route-estimate?${new URLSearchParams({ startLocation: start, endLocation: end })}`, { token })
      .then((data) => setEstimate({ status: 'ready', data: data.data }))
      .catch((e) => setEstimate({ status: 'error', message: errorText(e) }))
  }, [token])

  const search = async (event) => {
    event?.preventDefault()
    if (!signedIn) return openAuth()
    const start = toEnglish(from.trim()), end = toEnglish(to.trim()), busName = toEnglish(name.trim())
    if (mode === 'route' && !start) { setError(t('Enter your starting point.')); return }
    if (mode === 'route' && !end) { setError(t('Enter your destination.')); return }
    if (mode === 'name' && !busName) { setError(t('Enter a bus name to search.')); return }
    setLoading(true); setError(''); setSearched({ mode, from: start, to: end, name: busName }); setSelectedId(null); setTrip(null)
    const shownUntil = Date.now() + MIN_LOADER_MS
    if (mode === 'route') loadEstimate(start, end); else setEstimate({ status: 'idle' })
    try {
      const result = await request(mode === 'route'
        ? `/bus/search-by-route?${new URLSearchParams({ startLocation: start, endLocation: end })}`
        : `/bus/search-by-name?${new URLSearchParams({ busName })}`, { token })
      setBuses(result.data || [])
      setSelectedId(result.data?.[0]?._id || null)
      if (result.start && result.end) setTrip({ start: result.start, end: result.end })
    } catch (e) {
      setBuses([]); setError(errorText(e)); setEstimate({ status: 'idle' })
    } finally {
      await new Promise((resolve) => window.setTimeout(resolve, Math.max(0, shownUntil - Date.now())))
      setLoading(false)
    }
  }
  // Run a search handed over from another page, once signed in.
  const autoSearched = useRef(false)
  useEffect(() => {
    if (autoSearched.current || !savedQuery.auto || !signedIn) return
    autoSearched.current = true
    search()
  }, [signedIn]) // eslint-disable-line react-hooks/exhaustive-deps

  const saveTrip = async (bus) => {
    if (!signedIn) return openAuth()
    const distance = estimate.status === 'ready' ? estimate.data.distanceKm : 0
    try {
      await request('/trip/add', { token, method: 'POST', body: JSON.stringify({ busId: bus._id, busName: bus.busName, distance, fare: Number(bus.base_fare || 0), source: bus.match?.boarding.name || from, destination: bus.match?.alighting.name || to }) })
      notify(t('Trip added to your history.'))
    } catch (e) { notify(errorText(e)) }
  }
  const toggleFavorite = async (bus) => {
    if (!signedIn) return openAuth()
    const isSaved = favorites.includes(bus._id)
    try {
      await request(isSaved ? '/api/favbus/remove' : '/api/favbus/add', { token, method: isSaved ? 'DELETE' : 'POST', body: JSON.stringify({ userId: user.id, busId: bus._id, busName: bus.busName, routeNumber: bus.routeNumber, operator: bus.operator }) })
      setFavorites((current) => isSaved ? current.filter((id) => id !== bus._id) : [...current, bus._id])
      notify(isSaved ? t('Removed from saved buses.') : t('Bus saved to favorites.'))
    } catch (e) { notify(errorText(e)) }
  }

  // The selected bus on the map: its whole route in travel direction, and the stops ridden.
  const selected = buses.find((bus) => bus._id === selectedId)
  const mapStops = useMemo(() => {
    const stops = sortedStops(selected)
    return selected?.match?.direction === 'return' ? stops.reverse() : stops
  }, [selected])
  const mapSegment = useMemo(() => selected?.match?.segment || [], [selected])
  // The map stays mounted across searches: Google bills each new map load.
  const showMap = Boolean(searched && !error && (searched.mode === 'route' || loading || buses.length))

  return <div className="page inner-page bus-page">
    <PageHeading eyebrow={t('GET THERE WITH EASE')} title={t('Find your bus.')} description={t('Search Dhaka bus routes by the places you are going—or by bus name.')} icon={BusFront} />
    <section className="panel bus-search-panel">
      <div className="segmented"><button className={mode === 'route' ? 'selected' : ''} onClick={() => { setMode('route'); setError('') }}><MapPin size={15} /> {t('Search by route')}</button><button className={mode === 'name' ? 'selected' : ''} onClick={() => { setMode('name'); setError('') }}><Search size={15} /> {t('Search by bus')}</button></div>
      <StopSuggestions />
      <form className="bus-search-form" onSubmit={search}>
        {mode === 'route' ? <>
          <label className="field"><small>{t('STARTING POINT')}</small><span><span className="input-dot" /><input value={from} onChange={(e) => setFrom(e.target.value)} placeholder={t('e.g. Uttara')} list="stop-suggestions" /></span></label>
          <button type="button" className="swap-button" onClick={() => { setFrom(to); setTo(from) }} aria-label={t('Swap locations')}><ArrowDownUp size={15} /></button>
          <label className="field"><small>{t('DESTINATION')}</small><span><MapPin size={16} /><input value={to} onChange={(e) => setTo(e.target.value)} placeholder={t('e.g. Motijheel')} list="stop-suggestions" /></span></label>
        </> : <label className="field wide"><small>{t('BUS NAME')}</small><span><Search size={16} /><input value={name} onChange={(e) => setName(e.target.value)} placeholder={t('e.g. BRTC, Raida')} /></span></label>}
        <button className={`button button-green${loading ? ' is-loading' : ''}`} type="submit" disabled={loading} aria-busy={loading}>{loading ? <><span className="button-spinner" aria-hidden="true" /> {t('Searching…')}</> : <><Search size={16} /> {t('Search buses')}</>}</button>
      </form>
      {error && <div className="inline-error" role="alert">{error}</div>}
      {!signedIn && <div className="signin-hint"><ShieldCheck size={15} /> {t('Bus search requires an account.')} <button onClick={openAuth}>{t('Sign in')}</button> {t('to find routes.')}</div>}
    </section>
    <ActiveTripPanel token={token} signedIn={signedIn} notify={notify} />
    {searched?.mode === 'route' && trip && <TripSummary trip={trip} estimate={estimate} onRetry={() => loadEstimate(searched.from, searched.to)} />}
    {searched && <section className={`results-area${showMap ? ' results-with-map' : ''}`}>
      <div className="results-list">
        <div className="results-heading"><div><div className="eyebrow">{searched.mode === 'route' ? t('YOUR ROUTE') : t('BUS SEARCH')}</div><h2>{searched.mode === 'route' ? t('{from} to {to}', { from: t(trip?.start.name || searched.from), to: t(trip?.end.name || searched.to) }) : t('Buses matching “{name}”', { name: searched.name })}</h2></div>{!loading && !error && <span className="result-count">{buses.length === 1 ? t('{count} bus found', { count: 1 }) : t('{count} buses found', { count: buses.length })}</span>}</div>
        {loading ? <BusSearchLoader mode={searched.mode} />
          : error ? <div className="empty-state"><span className="empty-icon"><CircleAlert size={23} /></span><b>{t('Search could not be completed')}</b><span>{error}</span></div>
          : buses.length ? <div className="bus-list">{buses.map((bus) => <BusCard key={bus._id} bus={bus} user={user} signedIn={signedIn} selected={bus._id === selectedId} onSelect={() => setSelectedId(bus._id)} favorite={favorites.includes(bus._id)} onFavorite={() => toggleFavorite(bus)} onAddTrip={() => saveTrip(bus)} boarding={{ token, signedIn, openAuth, notify, activeTrip }} onDetails={bus.routeNumber ? () => navigate('Bus details', { route: bus.routeNumber }) : null} notify={notify} />)}</div>
          : <div className="empty-state"><span className="empty-icon"><BusFront size={23} /></span><b>{t('No buses found just yet')}</b><span>{searched.mode === 'route' ? t('No bus in the database stops at both places. Try a nearby stop.') : t('Try another bus name or route number.')}</span></div>}
      </div>
      {showMap && <aside className="results-map">
        <GoogleMapView start={trip?.start} end={trip?.end} roadPath={estimate.status === 'ready' ? estimate.data.polyline : null} busStops={mapStops} segment={mapSegment} />
        {mapsConfigured
          ? <ul className="gmap-legend">
            {trip && <li><i className="legend-road" /> {t('Google driving route (estimate)')}</li>}
            {selected && <li><i className="legend-ride" /> {t('{bus}: stops you ride', { bus: t(selected.busName) })}</li>}
            {selected && <li><i className="legend-bus" /> {t('Rest of the bus route')}</li>}
          </ul>
          : <p className="gmap-note">{trip ? t('Google Maps driving route between your places. A bus may take other roads; its stops are listed on each card.') : t('Google Maps route between the first and last stop of the selected bus.')}</p>}
      </aside>}
    </section>}
    {!searched && <section className="suggestion"><span className="suggestion-icon"><Compass size={19} /></span><div><b>{t('Start with a familiar landmark')}</b><p>{t('Try popular Dhaka stops like Farmgate, Mohakhali, Gulistan, or Uttara.')}</p></div><span className="suggestion-pills"><button onClick={() => { setMode('route'); setFrom('Farmgate'); setTo('Uttara') }}>{t('Farmgate')}</button><button onClick={() => { setMode('route'); setFrom('Gulistan'); setTo('Uttara') }}>{t('Gulistan')}</button></span></section>}
  </div>
}

// Shown while a search runs: a bus driving past stops that light up in turn, with a line saying what is
// being checked. Purely visual; the steps are not tied to real progress.
// Results usually come back from the server's memory in a few milliseconds; without a minimum the loader only
// flickers, so it stays up at least this long.
const MIN_LOADER_MS = 1200

const LOADER_STEPS = {
  route: ['Checking every bus route…', 'Matching your stops in order…', 'Working out distance and time…'],
  name: ['Checking every bus route…', 'Matching the bus name…', 'Getting the stops ready…'],
}

function BusSearchLoader({ mode }) {
  const steps = LOADER_STEPS[mode] || LOADER_STEPS.route
  const [step, setStep] = useState(0)
  useEffect(() => {
    const timer = window.setInterval(() => setStep((current) => (current + 1) % steps.length), 1100)
    return () => window.clearInterval(timer)
  }, [steps.length])
  return <div className="bus-loader" role="status" aria-live="polite">
    <div className="bus-loader-scene" aria-hidden="true">
      <span className="bus-loader-road" />
      <span className="bus-loader-stop" /><span className="bus-loader-stop" /><span className="bus-loader-stop" /><span className="bus-loader-stop" />
      <span className="bus-loader-bus"><BusFront size={26} strokeWidth={2.2} /></span>
    </div>
    <b>{t('Finding your buses…')}</b>
    <span className="bus-loader-step" key={step}>{t(steps[step])}</span>
  </div>
}

// One search result: name, route number and stops, and for a route search where to board and get off.
function BusCard({ bus, user, signedIn, selected, onSelect, favorite, onFavorite, onAddTrip, boarding, onDetails, notify }) {
  const board = useBoardControls({ bus, ...boarding })
  const stops = getStops(bus)
  const match = bus.match
  return <article className={`bus-result panel${selected ? ' selected' : ''}`}>
    <span className="bus-result-icon"><BusFront size={20} /></span><div className="bus-result-main">
      <div className="bus-title"><h3>{bus.busName ? t(bus.busName) : t('City bus')}</h3>{bus.routeNumber && <span className="route-badge">{bus.routeNumber}</span>}{match?.direction === 'return' && <span className="route-badge return-badge">{t('Return trip')}</span>}</div>
      <div className="bus-details"><span>{bus.operator ? t(bus.operator) : t('Dhaka transit')}</span>{bus.busType && <><i /> <span>{t(bus.busType)}</span></>}{bus.frequency && <><i /> <span>{t(bus.frequency)}</span></>}</div>
      {match
        ? <><div className="stop-line"><span>{t(match.boarding.name)}</span><span className="stop-track" /><span>{t(match.alighting.name)}</span></div>
          <div className="stop-count">{t('Board at {from}, get off at {to}', { from: t(match.boarding.name), to: t(match.alighting.name) })} · {match.stopsToRide === 1 ? t('1 stop to ride') : t('{count} stops to ride', { count: match.stopsToRide })} · {t('{count} stops on this route', { count: stops.length })}</div></>
        : <>{stops.length > 0 && <div className="stop-line"><span>{t(stops[0])}</span><span className="stop-track" /><span>{t(stops[stops.length - 1])}</span></div>}
          <div className="stop-count">{t('{count} stops on this route', { count: stops.length })}</div></>}
      <div className="bus-card-actions">
        <button className={selected ? 'bus-map-toggle active' : 'bus-map-toggle'} onClick={onSelect} aria-pressed={selected}><MapIcon size={13} /> {selected ? t('Shown on map') : t('Show on map')}</button>
        {onDetails && <button className="bus-details-link" onClick={onDetails}>{t('View details')} <ArrowRight size={12} /></button>}
      </div>
    </div><div className="bus-result-side"><button className={favorite ? 'favorite saved' : 'favorite'} onClick={onFavorite} title={t('Save bus')}><Heart size={17} fill={favorite ? 'currentColor' : 'none'} /></button><span className="fare-label">{t('STARTS FROM')}</span><b>{money(bus.base_fare)}</b><small>{t('per ride')}</small>{board.button}<button className="button button-outline button-small" onClick={onAddTrip} title={t('Add a trip you already took to your history')}>{t('Log trip')}</button></div>{board.form}<BusReviews bus={bus} user={user} signedIn={signedIn} notify={notify} />
  </article>
}

function RidePage({ token, user, signedIn, notify, openAuth }) {
  const [rides, setRides] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [showForm, setShowForm] = useState(false)
  const [search, setSearch] = useState('')
  const [form, setForm] = useState({ source: '', destination: '', maxParticipants: 4 })
  const load = useCallback(async () => {
    setLoading(true)
    try { const data = await request('/api/rideshare'); setRides(data.ridePosts || data.data || data || []); setError('') }
    catch (e) { setRides([]); setError(errorText(e)) } finally { setLoading(false) }
  }, [])
  useEffect(() => { load() }, [load])
  const createRide = async (event) => {
    event.preventDefault()
    if (!signedIn) return openAuth()
    try {
      await request('/api/rideshare', { method: 'POST', body: JSON.stringify({ ...form, maxParticipants: Number(form.maxParticipants), userId: user.id, userName: user.name, gender: user.gender || 'Prefer not to say' }) })
      setShowForm(false); setForm({ source: '', destination: '', maxParticipants: 4 }); notify(t('Your ride share is live.')); load()
    } catch (e) { notify(errorText(e)) }
  }
  const requestRide = async (ride) => {
    if (!signedIn) return openAuth()
    try {
      await request('/api/riderequests/send', { method: 'POST', body: JSON.stringify({ ridePostId: ride._id, requesterId: user.id, requesterName: user.name, requesterGender: user.gender || 'Prefer not to say' }) })
      notify(t('Your request has been sent.'))
      return true
    } catch (e) { notify(errorText(e)); return false }
  }
  const filtered = rides.filter((ride) => `${ride.source} ${ride.destination} ${ride.userName}`.toLowerCase().includes(search.toLowerCase()))
  return <div className="page inner-page">
    <PageHeading eyebrow={t('GOOD COMPANY, BETTER RIDES')} title={t('Share the journey.')} description={t('Find people heading your way. Split the ride, share the road.')} icon={Users} />
    <section className="rides-hero"><span className="rides-hero-icon"><Users size={22} /></span><div><b>{t('Going the same way?')}</b><p>{t('Join a fellow commuter or post your ride so others can come along.')}</p></div><button className="button button-dark" onClick={() => signedIn ? setShowForm(!showForm) : openAuth()}><Plus size={16} /> {t('Post a ride')}</button><div className="rides-hero-shape" /></section>
    {showForm && <form className="panel create-ride-form" onSubmit={createRide}><div className="form-head"><div><b>{t('Post a ride share')}</b><span>{t('Tell other commuters where you are going.')}</span></div><button className="icon-button" type="button" onClick={() => setShowForm(false)} aria-label={t('Close')}><X size={18} /></button></div><div className="form-fields"><label className="field"><small>{t('FROM')}</small><input required value={form.source} onChange={(e) => setForm({ ...form, source: e.target.value })} placeholder={t('Starting point')} /></label><label className="field"><small>{t('TO')}</small><input required value={form.destination} onChange={(e) => setForm({ ...form, destination: e.target.value })} placeholder={t('Destination')} /></label><label className="field participant-field"><small>{t('MAX PEOPLE')}</small><select value={form.maxParticipants} onChange={(e) => setForm({ ...form, maxParticipants: e.target.value })}>{[2, 3, 4, 5, 6, 7, 8, 9].map((number) => <option key={number} value={number}>{t('{count} people', { count: number })}</option>)}</select></label></div><button className="button button-green" type="submit">{t('Publish ride')} <ArrowRight size={15} /></button></form>}
    <div className="ride-toolbar"><div><div className="eyebrow">{t('COMMUNITY RIDES')}</div><h2>{t('Rides around Dhaka')} <span className="count-bubble">{formatNumber(rides.length)}</span></h2></div><label className="ride-search"><Search size={15} /><input value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('Search a destination')} /></label></div>
    {loading ? <div className="empty-state"><span className="spinner" /> {t('Finding available rides…')}</div> : filtered.length ? <div className="ride-grid">{filtered.map((ride) => <RideCard key={ride._id} ride={ride} token={token} user={user} signedIn={signedIn} onRequest={() => requestRide(ride)} notify={notify} onRefresh={load} />)}</div> : <div className="empty-state panel"><span className="empty-icon"><Users size={22} /></span><b>{error ? t('Could not load rides') : t('No rides on this route yet')}</b><span>{error || (rides.length ? t('Try searching for another destination.') : t('Be the first to post a ride and find your travel crew.'))}</span>{error ? <button className="button button-outline button-small" onClick={load}>{t('Try again')}</button> : !showForm && <button className="button button-green button-small" onClick={() => signedIn ? setShowForm(true) : openAuth()}><Plus size={14} /> {t('Post the first ride')}</button>}</div>}
  </div>
}

function RideCard({ ride, token, user, signedIn, onRequest, notify, onRefresh }) {
  const isMine = user?.id === ride.userId || user?.id === ride.userId?._id
  const [requestSent, setRequestSent] = useState(false)
  const [showRequests, setShowRequests] = useState(false)
  const [requests, setRequests] = useState([])
  const [requestError, setRequestError] = useState('')
  const viewRequests = async () => {
    setShowRequests(!showRequests)
    if (showRequests) return
    try { const data = await request(`/api/riderequests/ride/${ride._id}`, { token }); setRequests(data.data || []) }
    catch (e) { setRequestError(errorText(e)) }
  }
  const resolveRequest = async (item, accepted) => {
    try {
      await request(`/api/riderequests/${accepted ? 'accept' : 'reject'}/${item._id}`, { token, method: 'PUT', ...(accepted ? { body: JSON.stringify({ ridePostId: ride._id }) } : {}) })
      notify(accepted ? t('Ride request accepted.') : t('Ride request declined.'))
      const data = await request(`/api/riderequests/ride/${ride._id}`, { token })
      setRequests(data.data || [])
      onRefresh()
    } catch (e) { setRequestError(errorText(e)) }
  }
  return <><article className="ride-card panel"><div className="ride-card-top"><span className="ride-pulse"><span /></span><span>{ride.status ? t(ride.status) : t('Open ride')}</span><span className="ride-seats"><Users size={13} /> {t('Up to {count}', { count: ride.maxParticipants || 4 })}</span></div><div className="ride-route"><div><span className="route-origin" /><b>{t(ride.source)}</b></div><span className="route-connector" /><div><span className="route-destination" /><b>{t(ride.destination)}</b></div></div><div className="ride-card-footer"><span className="host-avatar">{ride.userName?.[0]?.toUpperCase() || 'R'}</span><span className="host-info"><b>{ride.userName || t('Dhaka commuter')}</b><small>{ride.gender ? `${t(ride.gender)} · ` : ''}{t('Ride host')}</small></span>{isMine ? <button className="button button-outline button-small" onClick={async () => { try { await request(`/api/rideshare/${ride._id}`, { method: 'DELETE' }); notify(t('Ride post removed.')); onRefresh() } catch (e) { notify(errorText(e)) } }}>{t('Your post')}</button> : <button className="button button-dark button-small" disabled={requestSent} onClick={async () => { const sent = await onRequest(); if (sent) setRequestSent(true) }}>{requestSent ? <><Check size={14} /> {t('Requested')}</> : <>{t('Join ride')} <ArrowRight size={14} /></>}</button>}</div></article>
    {isMine && <div className="ride-owner-actions"><button className="button button-outline button-small" onClick={viewRequests}>{showRequests ? t('Hide requests') : t('View ride requests')} <ArrowRight size={13} /></button>{showRequests && <div className="ride-request-list">{requestError && <small className="ride-request-error">{requestError}</small>}{requests.length ? requests.map((item) => <div className="ride-request-row" key={item._id}><span><b>{item.requesterName || t('Commuter')}</b><small>{item.requesterGender ? t(item.requesterGender) : ''} · {t(item.status || 'pending')}</small></span>{item.status === 'pending' && <><button className="button button-green button-small" onClick={() => resolveRequest(item, true)}>{t('Accept')}</button><button className="button button-outline button-small" onClick={() => resolveRequest(item, false)}>{t('Decline')}</button></>}</div>) : !requestError && <small className="ride-request-error">{t('No requests yet.')}</small>}</div>}</div>}
  </>
}

function TripsPage({ token, signedIn, openAuth, notify, navigate }) {
  const [trips, setTrips] = useState([])
  const [loading, setLoading] = useState(signedIn)
  const [error, setError] = useState('')
  // Reloads on open and whenever a trip is boarded or ended, so a finished trip shows up straight away.
  const load = useCallback(() => {
    if (!signedIn) return
    setLoading((current) => current || !trips.length)
    request('/trip/user', { token })
      .then((data) => { setTrips(data.data || []); setError('') })
      .catch((e) => setError(errorText(e)))
      .finally(() => setLoading(false))
  }, [token, signedIn]) // eslint-disable-line react-hooks/exhaustive-deps
  useTripChanges(load)
  const completed = trips.filter((trip) => trip.status !== 'active')
  const totals = { trips: completed.length, fare: completed.reduce((sum, trip) => sum + Number(trip.fare || 0), 0), gems: trips.reduce((sum, trip) => sum + Number(trip.gemsEarned || 0), 0) }
  return <div className="page inner-page trips-page">
    <PageHeading eyebrow={t('YOUR JOURNEY, ALL IN ONE PLACE')} title={t('Trip history.')} description={t('A little record of all the places you have been.')} icon={History} />
    {!signedIn ? <SignInPrompt openAuth={openAuth} message={t('Sign in to see your recent journeys.')} /> : <>
      <ActiveTripPanel token={token} signedIn={signedIn} notify={notify} />
      {loading ? <div className="empty-state"><span className="spinner" /> {t('Loading your trips…')}</div>
        : error ? <div className="empty-state panel"><span className="empty-icon"><History size={22} /></span><b>{t('Trip history unavailable')}</b><span>{error}</span><button className="button button-outline button-small" onClick={load}>{t('Try again')}</button></div>
        : trips.length ? <>
          <div className="trip-totals">
            <span><small>{t('Completed trips')}</small><b>{formatNumber(totals.trips)}</b></span>
            <span><small>{t('Spent on fares')}</small><b>{money(totals.fare)}</b></span>
            <span><small>{t('Gems earned')}</small><b>{formatNumber(totals.gems)}</b></span>
          </div>
          <div className="trip-list panel">{trips.map((trip) => <TripRow key={trip._id} trip={trip} />)}</div>
        </>
        : <div className="empty-state panel"><span className="empty-icon"><History size={22} /></span><b>{t('Your next trip starts here')}</b><span>{t('Board a bus from the search results and your trips will show up here.')}</span><button className="button button-green button-small" onClick={() => navigate('Find a bus')}><Search size={14} /> {t('Find a bus')}</button></div>}
    </>}
  </div>
}

function WalletPage({ token, signedIn, openAuth }) {
  const [wallet, setWallet] = useState(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(signedIn)
  const load = useCallback(() => {
    if (!signedIn) return
    setLoading(true)
    request('/wallet/balance', { token }).then((data) => setWallet(data.wallet || data.data || data)).catch((e) => setError(errorText(e))).finally(() => setLoading(false))
  }, [signedIn, token])
  useTripChanges(load)
  const balance = wallet?.balance ?? wallet?.wallet?.balance ?? 0
  return <div className="page inner-page"><PageHeading eyebrow={t('YOUR COMMUTE COMPANION')} title={t('Travel wallet.')} description={t('Keep your travel balance and recent journeys close at hand.')} icon={Wallet} />
    {!signedIn ? <SignInPrompt openAuth={openAuth} message={t('Sign in to check your SmartCommute wallet.')} /> : loading ? <div className="empty-state"><span className="spinner" /> {t('Checking your balance…')}</div> : error ? <div className="empty-state panel"><span className="empty-icon"><Wallet size={22} /></span><b>{t('Wallet unavailable')}</b><span>{error}</span><button className="button button-outline button-small" onClick={load}>{t('Try again')}</button></div> : <><section className="wallet-overview"><div className="wallet-card-large"><div className="wallet-card-head"><span className="wallet-mark"><BusFront size={18} /></span><span>{t('SMARTCOMMUTE WALLET')}</span><CreditCard size={20} /></div><div className="balance-caption">{t('AVAILABLE BALANCE')}</div><div className="balance-amount">{money(balance)}</div><div className="wallet-card-foot"><span>{t('BDT · Bangladeshi Taka')}</span><span>•••• {String(wallet?._id || '4829').slice(-4)}</span></div><div className="wallet-card-orb" /></div><div className="wallet-note panel wallet-gems"><span className="service-icon mint"><Sparkles size={19} /></span><small>{t('GEMS')}</small><b className="wallet-gems-count">{formatNumber(wallet?.gems ?? 0)}</b><p>{t('You earn gems for every trip you complete. End a trip at your stop to collect them.')}</p></div></section><div className="wallet-hint"><Clock3 size={16} /><span><b>{t('Easy to keep track.')}</b> {t('Your trip history keeps a record of each commute you log.')}</span></div></>}
  </div>
}

// Fields the profile form edits, in the shape PUT /auth/profile accepts.
const PROFILE_FIELDS = ['name', 'email', 'phone', 'gender', 'homeArea', 'workArea', 'bio']
const profileFormOf = (user) => Object.fromEntries(PROFILE_FIELDS.map((key) => [key, user?.[key] || (key === 'gender' ? 'Prefer not to say' : '')]))
const GENDERS = ['Prefer not to say', 'Male', 'Female', 'Other']
const DHAKA_AREAS = ['Uttara', 'Mirpur', 'Mohakhali', 'Banani', 'Gulshan', 'Badda', 'Rampura', 'Farmgate', 'Dhanmondi', 'Mohammadpur', 'Motijheel', 'Gulistan', 'Shahbagh', 'Tejgaon', 'Bashundhara', 'Jatrabari', 'Old Dhaka', 'Savar']
const BIO_LIMIT = 200

// Same rules the server applies to PUT /auth/profile.
function profileProblem({ name, email, phone, bio }) {
  if (name.trim().length < 2 || name.trim().length > 100) return t('Name must be between 2 and 100 characters.')
  if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email.trim())) return t('Please enter a valid email address.')
  if (phone.trim() && !/^\+?[0-9][0-9 -]{5,18}$/.test(phone.trim())) return t('Please enter a valid phone number.')
  if (bio.length > BIO_LIMIT) return t('Bio must be at most {count} characters.', { count: BIO_LIMIT })
  return ''
}

function ProfilePage({ user, token, signedIn, updateUser, signOut, openAuth, notify }) {
  const [editing, setEditing] = useState(false)
  const [form, setForm] = useState(() => profileFormOf(user))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [saved, setSaved] = useState(false)
  // The "profile updated" banner goes away by itself after a few seconds.
  useEffect(() => {
    if (!saved) return
    const timer = window.setTimeout(() => setSaved(false), 6000)
    return () => window.clearTimeout(timer)
  }, [saved])
  // Pick up fresh details (e.g. after /auth/validate) while the form is not being edited.
  useEffect(() => { if (!editing) setForm(profileFormOf(user)) }, [user, editing])
  const set = (key) => (event) => setForm({ ...form, [key]: event.target.value })
  const changed = PROFILE_FIELDS.some((key) => form[key].trim() !== profileFormOf(user)[key].trim())
  const cancel = () => { setEditing(false); setError(''); setForm(profileFormOf(user)) }
  const save = async (event) => {
    event.preventDefault()
    const problem = profileProblem(form)
    if (problem) { setError(problem); return }
    setSaving(true); setError('')
    try {
      const body = Object.fromEntries(PROFILE_FIELDS.map((key) => [key, form[key].trim()]))
      const data = await request('/auth/profile', { token, method: 'PUT', body: JSON.stringify(body) })
      const nextUser = { ...user, ...(data.user || body) }
      localStorage.setItem('sc_user', JSON.stringify(nextUser)); updateUser(nextUser)
      setEditing(false); setSaved(true); notify(t('Your profile has been updated.'))
    } catch (e) { setError(errorText(e)) } finally { setSaving(false) }
  }
  const copyCode = async () => {
    try { await navigator.clipboard.writeText(user.friendCode); notify(t('Friend code copied.')) } catch { notify(t('Your friend code is {code}', { code: user.friendCode })) }
  }
  if (!signedIn) return <div className="page inner-page"><PageHeading eyebrow={t('YOUR ACCOUNT, YOUR WAY')} title={t('Your profile.')} description={t('Your details stay with you wherever you go.')} icon={Users} /><SignInPrompt openAuth={openAuth} message={t('Sign in to manage your profile.')} /></div>

  const initials = (user.name || 'U').split(' ').filter(Boolean).slice(0, 2).map((part) => part[0].toUpperCase()).join('')
  const filled = PROFILE_FIELDS.filter((key) => key === 'gender' ? user.gender && user.gender !== 'Prefer not to say' : user[key]).length
  const completeness = Math.round((filled / PROFILE_FIELDS.length) * 100)
  const memberSince = formatDate(user.createdAt, { month: 'long', year: 'numeric' })
  const details = [
    ['Full name', user.name], ['Email address', user.email], ['Phone number', user.phone], ['Gender', user.gender && t(user.gender)],
    ['Home area', user.homeArea && t(user.homeArea)], ['Work or study area', user.workArea && t(user.workArea)],
  ]
  // An empty detail is a dashed "+ Not added" chip, so it never reads like a real value; it opens the edit form.
  const notAdded = <button type="button" className="pf-empty" title={t('Click to add')} onClick={() => { setSaved(false); setEditing(true) }}><Plus size={13} /> {t('Not added')}</button>

  return <div className="page inner-page profile-page">
    <PageHeading eyebrow={t('YOUR ACCOUNT, YOUR WAY')} title={t('Your profile.')} description={t('Keep your details up to date so riders and friends know who they are travelling with.')} icon={Users} />
    <div className="pf-layout">
      <aside className="pf-card">
        <div className="pf-cover" />
        <span className="pf-avatar">{initials}</span>
        <b className="pf-name">{user.name}</b>
        <span className="pf-email">{user.email}</span>
        <div className="pf-chips"><span className="pf-chip">{t(user.role || 'commuter')}</span>{user.pass && <span className="pf-chip verified"><BadgeCheck size={13} /> {t('Verified student')}</span>}</div>
        {user.bio && <p className="pf-bio">{user.bio}</p>}
        <ul className="pf-facts">
          {(user.homeArea || user.workArea) && <li><Route size={15} /><span>{user.homeArea ? t(user.homeArea) : '—'} <ArrowRight size={12} /> {user.workArea ? t(user.workArea) : '—'}</span></li>}
          {user.phone && <li><Phone size={15} /><span>{user.phone}</span></li>}
          {memberSince && <li><CalendarDays size={15} /><span>{t('Member since {date}', { date: memberSince })}</span></li>}
        </ul>
        {user.friendCode && <button className="pf-code" onClick={copyCode} title={t('Copy friend code')}><span><small>{t('Friend code')}</small><b>{user.friendCode}</b></span><Copy size={15} /></button>}
        <div className="pf-progress">
          <div><span>{t('Profile completeness')}</span><b>{formatNumber(completeness)}%</b></div>
          <span className="pf-bar"><i style={{ width: `${completeness}%` }} /></span>
          {completeness < 100 && !editing && <button onClick={() => setEditing(true)}>{t('Complete your profile')} <ArrowRight size={13} /></button>}
        </div>
        <button className="pf-signout" onClick={() => signOut()}><LogOut size={16} /> {t('Sign out')}</button>
      </aside>

      <div className="pf-main">
        <section className="pf-panel">
          <header className="pf-panel-head">
            <div><h2>{t('Personal information')}</h2><p>{editing ? t('Update your details and save when you are done.') : t('This is how you appear to other SmartCommute users.')}</p></div>
            {!editing && <button className="button button-outline button-small" onClick={() => { setSaved(false); setEditing(true) }}><Pencil size={14} /> {t('Edit profile')}</button>}
          </header>
          {saved && !editing && <div className="pf-success" role="status"><span className="pf-success-icon"><Check size={16} /></span><div><b>{t('Your profile has been updated.')}</b><small>{t('Your new details are saved and visible on your profile.')}</small></div><button onClick={() => setSaved(false)} aria-label={t('Dismiss')}><X size={16} /></button></div>}
          {editing ? <form className="pf-form" noValidate onSubmit={save}>
            <label className="pf-field"><span>{t('Full name')}</span><input autoComplete="name" value={form.name} onChange={set('name')} maxLength={100} /></label>
            <label className="pf-field"><span>{t('Email address')}</span><input type="email" autoComplete="email" value={form.email} onChange={set('email')} maxLength={255} /></label>
            <label className="pf-field"><span>{t('Phone number')} <em>{t('Optional')}</em></span><input type="tel" autoComplete="tel" value={form.phone} onChange={set('phone')} placeholder={t('+880 1XXX-XXXXXX')} maxLength={20} /></label>
            <label className="pf-field"><span>{t('Gender')}</span><select value={form.gender} onChange={set('gender')}>{GENDERS.map((gender) => <option key={gender} value={gender}>{t(gender)}</option>)}</select></label>
            <label className="pf-field"><span>{t('Home area')} <em>{t('Optional')}</em></span><input list="pf-areas" value={form.homeArea} onChange={set('homeArea')} placeholder={t('e.g. Uttara')} maxLength={100} /></label>
            <label className="pf-field"><span>{t('Work or study area')} <em>{t('Optional')}</em></span><input list="pf-areas" value={form.workArea} onChange={set('workArea')} placeholder={t('e.g. Motijheel')} maxLength={100} /></label>
            <label className="pf-field pf-wide"><span>{t('Bio')} <em>{formatNumber(form.bio.length)}/{formatNumber(BIO_LIMIT)}</em></span><textarea rows={3} value={form.bio} onChange={set('bio')} maxLength={BIO_LIMIT} placeholder={t('A line about you and how you usually commute.')} /></label>
            <datalist id="pf-areas">{DHAKA_AREAS.map((area) => <option key={area} value={area} />)}</datalist>
            {error && <div className="auth-error pf-wide" role="alert"><CircleAlert size={17} /><span>{error}</span></div>}
            <div className="pf-actions pf-wide">
              <button type="button" className="button button-outline" onClick={cancel} disabled={saving}>{t('Cancel')}</button>
              <button type="submit" className="button button-green" disabled={saving || !changed}>{saving ? t('Saving…') : <><Check size={15} /> {t('Save changes')}</>}</button>
            </div>
          </form> : <dl className="pf-details">
            {details.map(([label, value]) => <div key={label}><dt>{t(label)}</dt><dd>{value || notAdded}</dd></div>)}
            <div className="pf-wide"><dt>{t('Bio')}</dt><dd>{user.bio || notAdded}</dd></div>
          </dl>}
        </section>
        <PasswordPanel token={token} notify={notify} />
      </div>
    </div>
  </div>
}

function PasswordPanel({ token, notify }) {
  const [form, setForm] = useState({ currentPassword: '', newPassword: '', confirmPassword: '' })
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const set = (key) => (event) => setForm({ ...form, [key]: event.target.value })
  const submit = async (event) => {
    event.preventDefault()
    const problem = !form.currentPassword ? 'Please enter your current password.'
      : form.newPassword.length < 6 ? 'New password must be at least 6 characters.'
      : !/[A-Za-z]/.test(form.newPassword) || !/\d/.test(form.newPassword) ? 'New password must contain at least one letter and one number.'
      : form.newPassword !== form.confirmPassword ? 'New passwords do not match.' : ''
    if (problem) { setError(t(problem)); return }
    setSaving(true); setError('')
    try {
      await request('/auth/password', { token, method: 'PUT', body: JSON.stringify({ currentPassword: form.currentPassword, newPassword: form.newPassword }) })
      setForm({ currentPassword: '', newPassword: '', confirmPassword: '' }); notify(t('Password updated.'))
    } catch (e) { setError(errorText(e)) } finally { setSaving(false) }
  }
  return <section className="pf-panel">
    <header className="pf-panel-head"><div><h2>{t('Password & security')}</h2><p>{t('Use at least 6 characters with a letter and a number.')}</p></div><span className="pf-panel-icon"><KeyRound size={18} /></span></header>
    <form className="pf-form" noValidate onSubmit={submit}>
      <label className="pf-field pf-wide"><span>{t('Current password')}</span><input type="password" autoComplete="current-password" value={form.currentPassword} onChange={set('currentPassword')} /></label>
      <label className="pf-field"><span>{t('New password')}</span><input type="password" autoComplete="new-password" value={form.newPassword} onChange={set('newPassword')} /></label>
      <label className="pf-field"><span>{t('Confirm new password')}</span><input type="password" autoComplete="new-password" value={form.confirmPassword} onChange={set('confirmPassword')} /></label>
      {error && <div className="auth-error pf-wide" role="alert"><CircleAlert size={17} /><span>{error}</span></div>}
      <div className="pf-actions pf-wide"><button className="button button-dark" disabled={saving}>{saving ? t('Updating…') : t('Update password')}</button></div>
    </form>
  </section>
}

const shortDate = (value) => formatDate(value) || '—'

function AdminPage({ token, signedIn, isAdmin, openAuth, notify }) {
  const [search, setSearch] = useState('')
  const [users, setUsers] = useState([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  // { user, stage: 'confirm' | 'working' | 'done', password }
  const [reset, setReset] = useState(null)
  const load = useCallback(async (term) => {
    setLoading(true)
    try {
      const data = await request(`/admin/users?${new URLSearchParams({ search: term, limit: '100' })}`, { token })
      setUsers(data.data || []); setError('')
    } catch (e) { setUsers([]); setError(errorText(e)) } finally { setLoading(false) }
  }, [token])
  // Wait for the admin to stop typing before searching.
  useEffect(() => {
    if (!isAdmin) return
    const timer = window.setTimeout(() => load(search.trim()), 300)
    return () => window.clearTimeout(timer)
  }, [search, isAdmin, load])
  const confirmReset = async () => {
    setReset({ ...reset, stage: 'working' })
    try {
      const data = await request(`/admin/users/${reset.user.id}/reset-password`, { token, method: 'POST' })
      setReset({ user: reset.user, stage: 'done', password: data.temporaryPassword })
    } catch (e) { setReset(null); notify(errorText(e)) }
  }
  const copyPassword = async () => {
    try { await navigator.clipboard.writeText(reset.password); notify(t('Temporary password copied.')) } catch { notify(t('Copy failed. Select the password and copy it manually.')) }
  }
  const heading = <PageHeading eyebrow={t('ADMIN')} title={t('User accounts.')} description={t('Find a user and reset their password when they can no longer sign in.')} icon={ShieldCheck} />
  if (!signedIn) return <div className="page inner-page">{heading}<SignInPrompt openAuth={openAuth} message={t('Sign in with an admin account to manage users.')} /></div>
  if (!isAdmin) return <div className="page inner-page">{heading}<div className="empty-state panel"><span className="empty-icon"><Lock size={22} /></span><b>{t('Admins only')}</b><span>{t('Your account does not have access to this page.')}</span></div></div>

  return <div className="page inner-page">
    {heading}
    <section className="pf-panel ad-panel">
      <header className="ad-toolbar">
        <div><h2>{t('All users')} <span className="ad-count">{formatNumber(users.length)}</span></h2><p>{t('Resetting a password signs the user out on every device.')}</p></div>
        <label className="ad-search"><Search size={16} /><input value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('Search by name or email')} /></label>
      </header>
      {error && <div className="auth-error" role="alert"><CircleAlert size={17} /><span>{error}</span></div>}
      {loading && !users.length ? <div className="empty-state"><span className="spinner" /> {t('Loading users…')}</div>
        : !users.length ? !error && <div className="empty-state"><span className="empty-icon"><Users size={22} /></span><b>{t('No users found')}</b><span>{search ? t('Try a different name or email.') : t('No accounts have been created yet.')}</span></div>
        : <div className="ad-table" role="table">
          <div className="ad-row ad-head" role="row"><span role="columnheader">{t('User')}</span><span role="columnheader">{t('Role')}</span><span role="columnheader">{t('Joined')}</span><span role="columnheader">{t('Last sign-in')}</span><span role="columnheader"><span className="sr-only">{t('Actions')}</span></span></div>
          {users.map((item) => <div className="ad-row" role="row" key={item.id}>
            <span className="ad-user" role="cell"><span className="ad-avatar">{item.name?.[0]?.toUpperCase() || 'U'}</span><span><b>{item.name}</b><small>{item.email}</small></span></span>
            <span role="cell"><span className={`pf-chip ${item.role === 'admin' ? 'ad-admin' : ''}`}>{t(item.role)}</span></span>
            <span role="cell" className="ad-meta"><small>{t('Joined')}</small>{shortDate(item.createdAt)}</span>
            <span role="cell" className="ad-meta"><small>{t('Last sign-in')}</small>{item.lastLoginAt ? shortDate(item.lastLoginAt) : t('Never')}</span>
            <span role="cell" className="ad-action">{item.role === 'admin'
              ? <small className="ad-note">{t('Set in server config')}</small>
              : <button className="button button-outline button-small" onClick={() => setReset({ user: item, stage: 'confirm' })}><KeyRound size={14} /> {t('Reset password')}</button>}</span>
          </div>)}
        </div>}
    </section>

    {reset && <div className="modal-backdrop" onMouseDown={(event) => event.target === event.currentTarget && reset.stage !== 'working' && setReset(null)}>
      <section className="panel ad-dialog" role="dialog" aria-modal="true" aria-labelledby="ad-dialog-title">
        {reset.stage === 'done' ? <>
          <span className="pf-success-icon ad-dialog-icon"><Check size={18} /></span>
          <h2 id="ad-dialog-title">{t('Password reset')}</h2>
          <p>{t('Give this temporary password to {name} ({email}). It is shown only once.', { name: reset.user.name, email: reset.user.email })}</p>
          <div className="ad-password"><code>{reset.password}</code><button className="button button-dark button-small" onClick={copyPassword}><Copy size={14} /> {t('Copy')}</button></div>
          <p className="ad-hint">{t('Ask them to sign in with it and set a new password from Profile → Password & security.')}</p>
          <div className="pf-actions"><button className="button button-green" onClick={() => setReset(null)}>{t('Done')}</button></div>
        </> : <>
          <span className="pf-panel-icon ad-dialog-icon"><KeyRound size={18} /></span>
          <h2 id="ad-dialog-title">{t('Reset password?')}</h2>
          <p>{t('A new temporary password will be created for {name} ({email}). Their current password will stop working and they will be signed out on all devices.', { name: reset.user.name, email: reset.user.email })}</p>
          <div className="pf-actions">
            <button className="button button-outline" onClick={() => setReset(null)} disabled={reset.stage === 'working'}>{t('Cancel')}</button>
            <button className="button button-dark" onClick={confirmReset} disabled={reset.stage === 'working'}>{reset.stage === 'working' ? t('Resetting…') : t('Reset password')}</button>
          </div>
        </>}
      </section>
    </div>}
  </div>
}

function SignInPrompt({ openAuth, message }) {
  return <div className="signin-prompt panel"><span className="service-icon mint"><ShieldCheck size={20} /></span><div><b>{t('Good to have you here.')}</b><p>{message}</p></div><button className="button button-dark button-small" onClick={openAuth}>{t('Sign in')} <ArrowRight size={14} /></button></div>
}

// Same rules the server applies to /auth/signup, checked first so the user gets instant feedback.
function signupProblem({ name, email, password, confirmPassword, role }) {
  if (name.length < 2 || name.length > 100) return 'Name must be between 2 and 100 characters.'
  if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email)) return 'Please enter a valid email address.'
  if (password.length < 6) return 'Password must be at least 6 characters.'
  if (!/[A-Za-z]/.test(password) || !/\d/.test(password)) return 'Password must contain at least one letter and one number.'
  if (password !== confirmPassword) return 'Passwords do not match.'
  if (!['commuter', 'driver'].includes(role)) return 'Role must be commuter or driver.'
  return ''
}

// Label, icon-led input box and an optional hint line, shared by every field in the sign-in / sign-up form.
function AuthField({ label, icon: Icon, hint, tone, children, action }) {
  return <label className={`auth-field${tone ? ` is-${tone}` : ''}`}>
    <span className="auth-label">{label}</span>
    <span className="auth-input">{Icon && <Icon size={17} aria-hidden="true" />}{children}{action}</span>
    {hint && <span className="auth-hint">{hint}</span>}
  </label>
}

// text is shown as a tooltip on the role toggle.
const ROLE_OPTIONS = [
  { value: 'commuter', title: 'Commuter', text: 'I travel by bus around the city' },
  { value: 'driver', title: 'Driver', text: 'I drive a bus or offer rides' },
]

function AuthModal({ mode, onMode, onClose, onSuccess }) {
  const [form, setForm] = useState({ name: '', email: '', password: '', confirmPassword: '', gender: 'Prefer not to say', role: 'commuter' })
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const [showPassword, setShowPassword] = useState(false)
  const [forgot, setForgot] = useState(false)
  const signup = mode === 'signup'
  const admin = mode === 'admin'
  const set = (key) => (event) => setForm({ ...form, [key]: event.target.value })
  const rules = [
    { ok: form.password.length >= 6, text: 'At least 6 characters' },
    { ok: /[A-Za-z]/.test(form.password), text: 'At least one letter' },
    { ok: /\d/.test(form.password), text: 'At least one number' },
  ]
  const confirmTone = !form.confirmPassword ? '' : form.confirmPassword === form.password ? 'ok' : 'bad'
  const submit = async (event) => {
    event.preventDefault()
    const name = form.name.trim()
    const email = form.email.trim()
    const problem = signup ? signupProblem({ ...form, name, email }) : (!email ? 'Please enter your email address.' : !form.password ? 'Please enter your password.' : '')
    if (problem) { setError(t(problem)); return }
    setLoading(true); setError('')
    try {
      // Sign-up signs the new account straight in; log in only if an older server sent no token.
      let data = signup ? await request('/auth/signup', { method: 'POST', body: JSON.stringify({ name, email, gender: form.gender, role: form.role, password: form.password }) }) : null
      if (!data?.token) data = await request('/auth/login', { method: 'POST', body: JSON.stringify({ email, password: form.password }) })
      // The admin tab only lets admin accounts in; end the session the login just opened for anyone else.
      if (admin && data.user?.role?.toLowerCase() !== 'admin') {
        request('/auth/logout', { token: data.token, method: 'POST', ignoreUnauthorized: true }).catch(() => {})
        setError(t('This account does not have admin access. Use the User tab to sign in.'))
        return
      }
      onSuccess(data.user, data.token, { admin })
    } catch (e) { setError(errorText(e)) } finally { setLoading(false) }
  }
  const changeMode = (next) => { setError(''); setShowPassword(false); setForgot(false); onMode(next) }
  const switchMode = () => changeMode(signup ? 'login' : 'signup')
  // While the form is open the page behind it does not scroll, and Escape closes it.
  useEffect(() => {
    const previous = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    const onKey = (event) => { if (event.key === 'Escape') onClose() }
    window.addEventListener('keydown', onKey)
    return () => { document.body.style.overflow = previous; window.removeEventListener('keydown', onKey) }
  }, [onClose])
  const eye = <button type="button" className="auth-eye" onClick={() => setShowPassword(!showPassword)} aria-label={showPassword ? t('Hide password') : t('Show password')}>{showPassword ? <EyeOff size={17} /> : <Eye size={17} />}</button>

  return <div className="modal-backdrop" onMouseDown={(event) => event.target === event.currentTarget && onClose()}>
    <section className="auth-modal panel" role="dialog" aria-modal="true" aria-labelledby="auth-title">
      <button className="modal-close icon-button" onClick={onClose} aria-label={t('Close')}><X size={19} /></button>
      <header className="auth-header">
        <span className={`auth-mark${admin ? ' is-admin' : ''}`}>{admin ? <ShieldCheck size={22} /> : <BusFront size={22} />}</span>
        <div>
          <h2 id="auth-title">{signup ? t('Create your account') : admin ? t('Admin sign in') : t('Welcome back')}</h2>
          <p>{signup ? t('All fields are required.') : admin ? t('Sign in with an admin account to manage users.') : t('Sign in to continue to SmartCommute.')}</p>
        </div>
      </header>

      {!signup && <div className="auth-tabs" role="tablist" aria-label={t('Sign in as')}>
        <button type="button" role="tab" aria-selected={!admin} className={admin ? '' : 'selected'} onClick={() => changeMode('login')}><UserRound size={16} /> {t('User')}</button>
        <button type="button" role="tab" aria-selected={admin} className={admin ? 'selected' : ''} onClick={() => changeMode('admin')}><ShieldCheck size={16} /> {t('Admin')}</button>
      </div>}

      <form noValidate onSubmit={submit} className="auth-form">
        {signup && <>
          <AuthField label={t('Full name')} icon={UserRound}>
            <input autoComplete="name" value={form.name} onChange={set('name')} placeholder={t('e.g. Rahim Ahmed')} />
          </AuthField>
          <div className="auth-row">
            <fieldset className="auth-roles">
              <legend className="auth-label">{t('I am a')}</legend>
              <div>{ROLE_OPTIONS.map((option) => <label key={option.value} className={`auth-role${form.role === option.value ? ' selected' : ''}`} title={t(option.text)}>
                <input type="radio" name="role" value={option.value} checked={form.role === option.value} onChange={set('role')} />
                {option.value === 'driver' ? <BusFront size={16} /> : <Users size={16} />}<b>{t(option.title)}</b>
              </label>)}</div>
            </fieldset>
            <AuthField label={t('Gender')}>
              <select value={form.gender} onChange={set('gender')}>{GENDERS.map((gender) => <option key={gender} value={gender}>{t(gender)}</option>)}</select>
              <ChevronDown size={16} className="auth-select-arrow" aria-hidden="true" />
            </AuthField>
          </div>
        </>}

        <AuthField label={t('Email address')} icon={Mail}>
          <input type="email" autoComplete="email" value={form.email} onChange={set('email')} placeholder={admin ? 'admin@smartcommute.local' : 'you@example.com'} />
        </AuthField>

        <AuthField label={t('Password')} icon={Lock} action={eye}>
          <input type={showPassword ? 'text' : 'password'} autoComplete={signup ? 'new-password' : 'current-password'} value={form.password} onChange={set('password')} placeholder={signup ? t('Create a password') : t('Enter your password')} />
        </AuthField>
        {!signup && <button type="button" className="auth-forgot" aria-expanded={forgot} onClick={() => setForgot(!forgot)}>{t('Forgot password?')}</button>}
        {!signup && forgot && <div className="auth-forgot-info" role="note"><KeyRound size={17} /><span>{admin
          ? <><b>{t('The admin password is set on the server.')}</b> {t('Find it under Admin → Password in the backend\'s appsettings.Local.json.')}</>
          : <><b>{t('Ask an admin to reset it.')}</b> {t('Contact the SmartCommute admin with the email you signed up with. They will give you a temporary password. After signing in, change it from Profile → Password & security.')}</>}</span></div>}
        {signup && <ul className="auth-rules" aria-label={t('Password requirements')}>
          {rules.map((rule) => <li key={rule.text} className={rule.ok ? 'ok' : ''}>{rule.ok ? <Check size={14} /> : <span className="auth-rule-dot" />}{t(rule.text)}</li>)}
        </ul>}

        {signup && <AuthField label={t('Confirm password')} icon={Lock} tone={confirmTone}
          hint={confirmTone === 'ok' ? <><Check size={14} /> {t('Passwords match')}</> : confirmTone === 'bad' ? <><X size={14} /> {t('Passwords do not match')}</> : ''}>
          <input type={showPassword ? 'text' : 'password'} autoComplete="new-password" value={form.confirmPassword} onChange={set('confirmPassword')} placeholder={t('Type the same password again')} aria-invalid={confirmTone === 'bad'} />
        </AuthField>}

        {error && <div className="auth-error" role="alert"><CircleAlert size={17} /><span>{error}</span></div>}
        <button className={`button ${admin ? 'button-dark' : 'button-green'} auth-submit`} disabled={loading}>{loading ? t('Please wait…') : signup ? t('Create account') : admin ? t('Sign in as admin') : t('Sign in')} {!loading && <ArrowRight size={16} />}</button>
      </form>

      {admin
        ? <div className="auth-switch">{t('Not an admin?')} <button onClick={() => changeMode('login')}>{t('Sign in as a user')}</button></div>
        : <div className="auth-switch">{signup ? t('Already have an account?') : t('Don\'t have an account?')} <button onClick={switchMode}>{signup ? t('Sign in') : t('Create one')}</button></div>}
      <small className="auth-api">{t('Connected to')} {API_BASE}</small>
    </section>
  </div>
}

createRoot(document.getElementById('root')).render(<React.StrictMode><App /></React.StrictMode>)
