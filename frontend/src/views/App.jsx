import React, { useCallback, useEffect, useRef, useState } from 'react'
import { ArrowRight, BusFront, ChevronDown, CircleHelp, Languages, Menu, ShieldCheck, Users, Wallet, X } from 'lucide-react'
import { MapPage } from './pages/MorePages'
import { GpayPage } from './pages/Gpay'
import { VerificationPage } from './pages/Verification'
import { OffersPage } from './pages/Offers'
import { useUnreadCount } from '../controllers/useUnreadCount'
import { ADMIN_FOCUS_EVENT, useAdminAlerts } from '../controllers/useAdminAlerts'
import { AdminAlert } from './components/AdminAlert'
import { NotificationBell, NotificationsPage } from './pages/Notifications'
import { FriendsPage } from './pages/Friends'
import { ChatPage } from './pages/Chat'
import { RidePage } from './pages/RideShare'
import { ForcePasswordModal, RESET_STORAGE_KEY, SupportPage } from './pages/Support'
import { FavoritesPage } from './pages/Favorites'
import { UNAUTHORIZED_EVENT, apiRequest as request, isTokenExpired, tokenExpiry } from '../models/api'
import { formatNumber, getLang, setLang, t } from '../models/i18n'
import { getTheme, setTheme, syncThemeColor } from '../models/theme'
import { continueLastSession, endSession, forgetLastSession, loadSession, saveSessionUser, startSession } from '../models/session'
import { ADMIN_FOCUS_KEY, PAGE_LOADER_MS, PAGE_LOADER_NAMES, PAGE_PATHS, busPath, pageFromPath, routeFromPath } from '../controllers/router'
import { ThemeMenu } from './components/common'
import { BusDetailPage, BusRoutesPage } from './pages/BusRoutesPage'
import { HomePage } from './pages/HomePage'
import { BusPage } from './pages/BusPage'
import { TripsPage } from './pages/TripsPage'
import { WalletPage } from './pages/WalletPage'
import { ProfilePage } from './pages/ProfilePage'
import { AdminPage } from './pages/AdminPage'
import { AuthModal } from './components/AuthModal'
import { Toaster, useToasts } from './components/Toaster'
import { clearPersistentState } from '../controllers/usePersistentState'

export function PageLoader({ page }) {
  return (
    <div className="page-loader" role="status" aria-live="polite">
      <div className="page-loader-bar" aria-hidden="true" />
      <div className="page-loader-card">
        <div className="page-loader-scene" aria-hidden="true">
          <span className="page-loader-bus"><BusFront size={22} strokeWidth={2.4} /></span>
          <span className="page-loader-road" />
        </div>
        <b>{t('Opening {page}…', { page: t(PAGE_LOADER_NAMES[page] || page) })}</b>
        <span className="page-loader-dots" aria-hidden="true"><i /><i /><i /></span>
      </div>
    </div>
  )
}

export function App() {
  // This tab's own sign-in (see session.js). One that has already expired is dropped before the first render.
  const [initialSession] = useState(() => {
    const session = loadSession()
    if (session.token && isTokenExpired(session.token)) { endSession(); return { token: null, user: null } }
    return session
  })
  const [token, setToken] = useState(initialSession.token)
  const [user, setUser] = useState(initialSession.user)
  const [page, setPage] = useState(() => pageFromPath(window.location.pathname))
  // The route shown on the 'Bus details' page.
  const [routeNumber, setRouteNumber] = useState(() => routeFromPath(window.location.pathname))
  const [mobileMenu, setMobileMenu] = useState(false)
  const [moreMenu, setMoreMenu] = useState(false)
  // Close the More menu on a click/tap outside it or on Escape.
  const moreRef = useRef(null)
  useEffect(() => {
    if (!moreMenu) return
    const onPointer = (e) => { if (moreRef.current && !moreRef.current.contains(e.target)) setMoreMenu(false) }
    const onKey = (e) => { if (e.key === 'Escape') setMoreMenu(false) }
    document.addEventListener('pointerdown', onPointer)
    document.addEventListener('keydown', onKey)
    return () => { document.removeEventListener('pointerdown', onPointer); document.removeEventListener('keydown', onKey) }
  }, [moreMenu])
  const { toasts, notify, dismiss: dismissToast, remove: removeToast } = useToasts()
  const [authMode, setAuthMode] = useState('')
  // The temporary password just used to sign in, kept in memory only so the new-password form can use it.
  const [temporaryPassword, setTemporaryPassword] = useState('')
  // Re-renders the whole app in the chosen language; t() reads the language set by setLang.
  const [lang, setLangState] = useState(getLang)
  const toggleLang = () => { const next = lang === 'bn' ? 'en' : 'bn'; setLang(next); setLangState(next) }
  const [theme, setThemeState] = useState(getTheme)
  const changeTheme = (next) => { setTheme(next); setThemeState(next) }
  useEffect(() => { syncThemeColor() }, [])

  // Shows a page and records it in the browser history; replace swaps the current entry instead of adding one.
  // 'Bus details' also takes the route to show.
  const showPage = useCallback((target, { replace = false, route = null } = {}) => {
    const path = target === 'Bus details' && route ? busPath(route) : PAGE_PATHS[target] || '/'
    if (window.location.pathname !== path) {
      // Remember how far down this page was, so Back returns to the same spot.
      if (!replace) window.history.replaceState({ ...(window.history.state || {}), scrollY: window.scrollY }, '', window.location.href)
      window.history[replace ? 'replaceState' : 'pushState']({ page: target }, '', path)
    }
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
    let timers = []
    const onPop = (event) => {
      setPage(pageFromPath(window.location.pathname))
      setRouteNumber(routeFromPath(window.location.pathname))
      setMobileMenu(false)
      setMoreMenu(false)
      setAuthMode('')
      window.scrollTo(0, 0)
      // Back to a page left part-way down: return to that spot. Pages fill in a moment later (lists load,
      // kept work is restored), so try again as the page grows.
      timers.forEach((id) => window.clearTimeout(id))
      const y = Number(event?.state?.scrollY)
      timers = y > 0 ? [60, 250, PAGE_LOADER_MS + 80, PAGE_LOADER_MS + 500].map((ms) => window.setTimeout(() => window.scrollTo(0, y), ms)) : []
    }
    window.addEventListener('popstate', onPop)
    return () => { timers.forEach((id) => window.clearTimeout(id)); window.removeEventListener('popstate', onPop) }
  }, [])
  // Signs this tab out; other tabs keep their own sign-in. revoke: also end the session on the server, which is
  // skipped when the server already rejected the token, and when this tab only borrowed another tab's session.
  const signOut = useCallback((message = t('You have been signed out.'), { revoke = true } = {}) => {
    const { token: current, owned } = endSession()
    // Work kept on pages (searches, drafts, forms) belongs to this account; the next person on this tab starts clean.
    clearPersistentState()
    if (revoke && owned && current) {
      request('/auth/logout', { token: current, method: 'POST', ignoreUnauthorized: true }).catch(() => {})
    }
    // The server no longer accepts it, so do not offer it as "Continue as …" either.
    if (!revoke) forgetLastSession(current)
    setToken(null)
    setUser(null)
    showPage('Home')
    notify(message, 'info')
  }, [notify, showPage])
  // "Continue as …" in the sign-in form: use the most recent sign-in in this tab too.
  const continueAs = useCallback(() => {
    const last = continueLastSession()
    if (!last) return
    setToken(last.token)
    setUser(last.user)
    setAuthMode('')
    notify(t('Welcome, {name}!', { name: (last.user.name || '').split(' ')[0] }))
    // Pick up fresh details; a session that has ended comes back 401 and signs this tab out again.
    request('/auth/validate', { token: last.token }).then((data) => { if (data.user) { saveSessionUser(data.user); setUser(data.user) } }).catch(() => {})
  }, [notify])
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
  // Tabs do not follow each other's sign-in: each tab keeps the account it was signed in with, so one tab can
  // be the admin panel while another is a normal user. If another tab ends a session this tab shares, the
  // server rejects the token and the 401 handler above signs this tab out.
  // Check a saved sign-in once on load and refresh the stored profile from the server.
  useEffect(() => {
    if (!token) return
    request('/auth/validate', { token }).then((data) => {
      if (data.user) { saveSessionUser(data.user); setUser(data.user) }
    }).catch(() => {})
  }, []) // eslint-disable-line react-hooks/exhaustive-deps
  const signedIn = Boolean(token && user)
  const isAdmin = signedIn && user.role?.toLowerCase() === 'admin'
  const unreadCount = useUnreadCount({ token, user, signedIn })
  const navigate = (target, options) => {
    showPage(target, options)
    window.scrollTo(0, 0)
  }
  // Admins hear about new password requests, messages and verifications at once, on any page.
  const [adminAlert, dismissAdminAlert] = useAdminAlerts({ token, isAdmin })
  const openAdminAlert = () => {
    const focus = adminAlert?.focus
    dismissAdminAlert()
    if (page === 'Admin') { window.dispatchEvent(new CustomEvent(ADMIN_FOCUS_EVENT, { detail: focus })); return }
    try { sessionStorage.setItem(ADMIN_FOCUS_KEY, focus || '') } catch { /* opens on the default section */ }
    navigate('Admin')
  }
  // A short loading screen each time the page changes (tabs, links, back and forward), then the page fades in.
  const pageKey = page === 'Bus details' ? `${page}:${routeNumber}` : page
  const [loadingPage, setLoadingPage] = useState('')
  const shownKey = useRef(pageKey)
  useEffect(() => {
    if (shownKey.current === pageKey) return
    shownKey.current = pageKey
    setLoadingPage(page)
    const timer = window.setTimeout(() => setLoadingPage(''), PAGE_LOADER_MS)
    return () => window.clearTimeout(timer)
  }, [pageKey]) // eslint-disable-line react-hooks/exhaustive-deps

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
          <div className="more-nav-wrap" ref={moreRef}>
            <button className={['Friends', 'Messages', 'Notifications', 'Offers', 'Map', 'Favourites', 'Wallet', 'GPay', 'Verification', 'Admin'].includes(page) ? 'nav-link active' : 'nav-link'} aria-expanded={moreMenu} onClick={() => setMoreMenu(!moreMenu)}>{t('More')} <ChevronDown size={13} /></button>
            {moreMenu && <div className="more-nav-menu">{[['Friends','Friends'],['Messages','Messages'],['Notifications','Notifications'],['Offers','Offers & rewards'],['Map','Map & fare calculator'],['Favourites','Favourite buses'],['Wallet','My wallet'],['GPay','GPay & recharge'],['Verification','Account verification'], ['Support','Contact admin'], ...(isAdmin ? [['Admin', 'Admin · Users']] : [])].map(([target,label]) => <button key={target} className={page === target ? 'current' : ''} onClick={() => navigate(target)}>{t(label)}<ArrowRight size={13} /></button>)}</div>}
          </div>
        </nav>
        <div className="top-actions">
          <ThemeMenu theme={theme} onChange={changeTheme} />
          <button className="lang-toggle" onClick={toggleLang} title={lang === 'bn' ? 'Switch to English' : 'বাংলায় দেখুন'} aria-label={lang === 'bn' ? 'Switch to English' : 'বাংলায় দেখুন'}>
            <Languages size={16} /><span className="lang-full">{lang === 'bn' ? 'English' : 'বাংলা'}</span><span className="lang-short">{lang === 'bn' ? 'EN' : 'বাং'}</span>
          </button>
          {signedIn ? (
            <>
              <NotificationBell count={unreadCount} onOpen={() => navigate('Notifications')} />
              {!isAdmin && <button className="icon-button" title={t('My wallet')} onClick={() => navigate('Wallet')}><Wallet size={18} /></button>}
              <button className={`profile-chip${isAdmin ? ' is-admin' : ''}`} onClick={() => navigate('Profile')} title={isAdmin ? t('Admin profile') : undefined}>
                <span className="avatar">{isAdmin ? <ShieldCheck size={15} /> : user.name?.[0]?.toUpperCase() || 'U'}</span>
                <span>{isAdmin ? t('Admin') : user.name?.split(' ')[0] || t('Account')}</span><ChevronDown size={14} />
              </button>
            </>
          ) : <button className="button button-dark button-small" onClick={() => setAuthMode('login')}>{t('Sign in')} <ArrowRight size={15} /></button>}
        </div>
      </header>

      {loadingPage && <PageLoader page={loadingPage} />}
      <main key={pageKey} className={loadingPage ? 'page-view is-loading' : 'page-view'}>
        {page === 'Home' && <HomePage user={user} token={token} signedIn={signedIn} navigate={navigate} openAuth={() => setAuthMode('login')} />}
        {page === 'Find a bus' && <BusPage token={token} signedIn={signedIn} user={user} notify={notify} navigate={navigate} openAuth={() => setAuthMode('login')} />}
        {page === 'Bus routes' && <BusRoutesPage navigate={navigate} />}
        {page === 'Bus details' && <BusDetailPage key={routeNumber} routeNumber={routeNumber} token={token} user={user} signedIn={signedIn} notify={notify} navigate={navigate} openAuth={() => setAuthMode('login')} />}
        {page === 'Ride share' && <RidePage token={token} user={user} signedIn={signedIn} notify={notify} openAuth={() => setAuthMode('login')} />}
        {page === 'My trips' && <TripsPage token={token} signedIn={signedIn} notify={notify} navigate={navigate} openAuth={() => setAuthMode('login')} />}
        {page === 'Wallet' && <WalletPage token={token} signedIn={signedIn} openAuth={() => setAuthMode('login')} notify={notify} />}
        {page === 'Profile' && <ProfilePage user={user} token={token} signedIn={signedIn} navigate={navigate} updateUser={setUser} signOut={signOut} openAuth={() => setAuthMode('login')} notify={notify} />}
        {page === 'Friends' && <FriendsPage token={token} signedIn={signedIn} openAuth={() => setAuthMode('login')} onChat={(friend) => { navigate('Messages'); window.history.replaceState({ page: 'Messages' }, '', `/messages?with=${encodeURIComponent(friend.id || friend._id)}`) }} notify={notify} />}
        {page === 'Messages' && <ChatPage token={token} user={user} signedIn={signedIn} openAuth={() => setAuthMode('login')} onFindFriends={() => navigate('Friends')} />}
        {page === 'Notifications' && <NotificationsPage token={token} user={user} signedIn={signedIn} openAuth={() => setAuthMode('login')} notify={notify} navigate={navigate} />}
        {page === 'Offers' && <OffersPage token={token} signedIn={signedIn} openAuth={() => setAuthMode('login')} notify={notify} />}
        {page === 'Favourites' && <FavoritesPage token={token} user={user} signedIn={signedIn} openAuth={() => setAuthMode('login')} notify={notify} navigate={navigate} />}
        {page === 'Map' && <MapPage token={token} user={user} signedIn={signedIn} openAuth={() => setAuthMode('login')} notify={notify} navigate={navigate} />}
        {page === 'GPay' && <GpayPage token={token} signedIn={signedIn} openAuth={() => setAuthMode('login')} notify={notify} />}
        {page === 'Verification' && <VerificationPage token={token} user={user} signedIn={signedIn} openAuth={() => setAuthMode('login')} notify={notify} />}
        {page === 'Admin' && <AdminPage user={user} token={token} signedIn={signedIn} isAdmin={isAdmin} navigate={navigate} openAuth={() => setAuthMode('admin')} notify={notify} />}
        {page === 'Support' && <SupportPage token={token} user={user} signedIn={signedIn} openAuth={() => setAuthMode('login')} navigate={navigate} />}
      </main>

      <footer className="site-footer">
        <div className="site-footer-inner">
          <div className="site-footer-about">
            <span className="footer-brand"><span className="brand-icon"><BusFront size={16} strokeWidth={2.5} /></span> {t('SmartCommute')}</span>
            <p>{t('Bus routes, ride sharing and a travel wallet for everyday commuters in Dhaka.')}</p>
            <button onClick={() => navigate('Support')}><CircleHelp size={14} /> {t('Contact admin')}</button>
          </div>
          {[
            ['Commute', [['Find a bus', 'Find a bus'], ['Ride share', 'Ride share'], ['Map', 'Map & fare calculator'], ['Favourites', 'Favourite buses'], ['My trips', 'My trips']]],
            ['Payments', [['Wallet', 'Travel wallet'], ['GPay', 'GPay & recharge'], ['Offers', 'Offers & rewards']]],
            ['Community', [['Friends', 'Friends'], ['Messages', 'Messages'], ['Notifications', 'Notifications'], ['Verification', 'Account verification']]],
          ].map(([heading, links]) => <nav key={heading} className="site-footer-col" aria-label={t(heading)}><b>{t(heading)}</b>{links.map(([target, label]) => <button key={target} onClick={() => navigate(target)}>{t(label)}</button>)}</nav>)}
        </div>
        <div className="site-footer-bottom"><span>© {formatNumber(new Date().getFullYear(), { useGrouping: false })} {t('SmartCommute Dhaka')}</span><span>{t('Making every trip a little simpler.')}</span></div>
      </footer>
      <Toaster toasts={toasts} onDismiss={dismissToast} onRemove={removeToast} />
      {isAdmin && <AdminAlert alert={adminAlert} onView={openAdminAlert} onClose={dismissAdminAlert} />}
      {authMode && <AuthModal mode={authMode} onMode={setAuthMode} onClose={() => setAuthMode('')} onContinue={continueAs} onSuccess={(nextUser, nextToken, { admin = false, password = '' } = {}) => {
        // Signing in here changes only this tab; other tabs keep their own account.
        startSession(nextToken, nextUser)
        setToken(nextToken)
        setUser(nextUser)
        setTemporaryPassword(password)
        setAuthMode('')
        if (admin) navigate('Admin')
        if (!nextUser.mustChangePassword) notify(nextUser.name ? t('Welcome, {name}!', { name: nextUser.name.split(' ')[0] }) : t('Welcome!'))
      }} />}
      {/* Signed in with a temporary password from an admin: a new password comes before anything else. */}
      {signedIn && user.mustChangePassword && <ForcePasswordModal token={token} user={user} initialPassword={temporaryPassword}
        onSignOut={() => { setTemporaryPassword(''); signOut() }}
        onDone={(nextUser) => {
          const updated = { ...user, ...nextUser, mustChangePassword: false }
          saveSessionUser(updated)
          setUser(updated)
          setTemporaryPassword('')
          try { localStorage.removeItem(RESET_STORAGE_KEY) } catch { /* nothing saved */ }
          notify(t('Your new password is saved. Welcome back!'))
        }} />}
    </div>
  )
}
