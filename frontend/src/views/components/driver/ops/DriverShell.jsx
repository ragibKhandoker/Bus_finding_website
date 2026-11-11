import React, { createContext, useCallback, useContext, useEffect, useState } from 'react'
import {
  BadgeCheck, BarChart3, Bell, BusFront, CalendarClock, CalendarDays, History, LayoutDashboard, ListChecks, LogOut, Map, Menu, Radio, UserRound, X,
} from 'lucide-react'
import { apiBlob } from '../../../../models/api'
import { t } from '../../../../models/i18n'
import { driverApi } from '../../../../models/driver'
import { WORK_STATUS_LABELS, fullDay } from '../../../../models/driverTrips'
import { cached, driverRequests, fetchInto, prefetchDriverConsole } from '../../../../models/driverRequests'
import { DriverVerificationStatusPage } from '../../../pages/DriverVerificationStatusPage'
import { SignInPrompt } from '../../common'
import { LoadingSkeleton, WorkingStatusPill } from './DriverWidgets'
import '../../../styles/driver.css'
import '../../../styles/driver-ops.css'

// The driver area's navigation, in the order of the sidebar.
export const DRIVER_NAV = [
  { page: 'Driver dashboard', icon: LayoutDashboard, label: 'Dashboard' },
  { page: 'Driver bus', icon: BusFront, label: 'My Assigned Bus' },
  { page: 'Driver schedule', icon: CalendarDays, label: "Today's Schedule" },
  { page: 'Driver upcoming', icon: CalendarClock, label: 'Upcoming Trips' },
  { page: 'Driver trips', icon: ListChecks, label: 'Manage Trips' },
  { page: 'Driver trip history', icon: History, label: 'Trip History' },
  { page: 'Driver routes', icon: Map, label: 'Assigned Routes' },
  { page: 'Driver working status', icon: Radio, label: 'Working Status' },
  { page: 'Driver statistics', icon: BarChart3, label: 'Performance & Statistics' },
]

// Whether this driver may use the operational pages: approved by an admin. Asked once per sign-in and then kept
// (refreshed quietly in the background on each page), so moving around the console never waits for it. The
// server still checks on every request.
export function useDriverAccess(token, enabled) {
  const hit = enabled ? cached(token, 'access') : undefined
  const [state, setState] = useState(() => (hit ? { status: hit.canAccessDashboard ? 'approved' : 'locked', info: hit } : { status: 'loading' }))
  useEffect(() => {
    if (!enabled) return undefined
    let live = true
    fetchInto(token, driverRequests.access(token)).then((info) => { if (live) setState({ status: info.canAccessDashboard ? 'approved' : 'locked', info }) })
      .catch((e) => { if (live) setState((s) => (s.status === 'approved' ? s : { status: 'error', error: t(e.message || 'Could not check your driver account.') })) })
    return () => { live = false }
  }, [token, enabled])
  return state
}

// Lets a page tell the layout to reload the working status shown in the header (after starting a trip, …).
export const DriverAreaContext = createContext({ refreshStatus: () => {}, status: null })
export const useDriverArea = () => useContext(DriverAreaContext)

// The driver's own profile picture (loaded with their token), or their initial when there is none.
export function DriverAvatar({ token, name, hasPhoto = true, size = 48 }) {
  const [src, setSrc] = useState('')
  useEffect(() => {
    if (!hasPhoto) return undefined
    let url = ''
    let live = true
    apiBlob('/api/driver-registration/profile-photo', { token }).then((blob) => { if (!live) return; url = URL.createObjectURL(blob); setSrc(url) }).catch(() => {})
    return () => { live = false; if (url) URL.revokeObjectURL(url) }
  }, [token, hasPhoto])
  return <span className="do-avatar" style={{ width: size, height: size, fontSize: size * 0.42 }}>{src ? <img src={src} alt="" /> : (name || '?')[0].toUpperCase()}</span>
}

export function DriverProfileCard({ token, driver, onProfile }) {
  return <div className="do-profile-card">
    <DriverAvatar token={token} name={driver.name} hasPhoto={driver.hasPhoto} size={44} />
    <div><b>{driver.name}</b><small><BadgeCheck size={12} /> {t('Verified driver')}</small></div>
    <button type="button" className="do-icon" onClick={onProfile} aria-label={t('My profile')}><UserRound size={16} /></button>
  </div>
}

export function DriverSidebar({ page, navigate, signOut, open, onClose, driver, token }) {
  const go = (target) => { onClose(); navigate(target) }
  return <>
    {open && <div className="do-scrim" onClick={onClose} aria-hidden="true" />}
    <aside className={`do-sidebar${open ? ' open' : ''}`} aria-label={t('Driver navigation')}>
      <div className="do-sidebar-head">
        <span className="do-sidebar-brand"><BusFront size={18} /> {t('Driver Console')}</span>
        <button type="button" className="do-icon do-sidebar-close" onClick={onClose} aria-label={t('Close menu')}><X size={16} /></button>
      </div>
      {driver && <DriverProfileCard token={token} driver={driver} onProfile={() => go('Profile')} />}
      <nav>
        {DRIVER_NAV.map(({ page: target, icon: Icon, label }) => {
          const current = page === target || (target === 'Driver trips' && page === 'Driver trip')
          return <button key={target} type="button" className={current ? 'current' : ''} aria-current={current ? 'page' : undefined} onClick={() => go(target)}>
            <Icon size={17} /> <span>{t(label)}</span>
          </button>
        })}
        <hr />
        <button type="button" onClick={() => go('Profile')}><UserRound size={17} /> <span>{t('My Profile')}</span></button>
        <button type="button" className="do-logout" onClick={() => { onClose(); signOut() }}><LogOut size={17} /> <span>{t('Logout')}</span></button>
      </nav>
    </aside>
  </>
}

export function DriverHeader({ driver, token, status, navigate, onMenu, title }) {
  const first = (driver?.name || '').split(' ')[0]
  return <header className="do-header">
    <button type="button" className="do-icon do-menu" onClick={onMenu} aria-label={t('Open menu')}><Menu size={18} /></button>
    {driver && <DriverAvatar token={token} name={driver.name} hasPhoto={driver.hasPhoto} size={52} />}
    <div className="do-header-text">
      <small>{fullDay(new Date().toISOString())}</small>
      <h1>{title || t('Welcome back, {name}!', { name: first || t('driver') })}</h1>
      <span className="do-header-meta"><span className="do-verified"><BadgeCheck size={14} /> {t('Verified Driver')}</span>
        {status && <WorkingStatusPill status={status} />}</span>
    </div>
    <div className="do-header-actions">
      <button type="button" className="do-icon" onClick={() => navigate('Notifications')} aria-label={t('Notifications')}><Bell size={17} /></button>
      <button type="button" className="do-icon" onClick={() => navigate('Profile')} aria-label={t('My profile')}><UserRound size={17} /></button>
    </div>
  </header>
}

// Every driver page sits in this layout. Pending, rejected and resubmission drivers see their verification
// status instead; commuters and admins are told the area is for drivers.
export function DriverLayout({ page, token, user, signedIn, openAuth, navigate, notify, signOut, title, children }) {
  const [menu, setMenu] = useState(false)
  const access = useDriverAccess(token, signedIn && user?.role === 'driver')
  const [status, setStatus] = useState(() => cached(token, 'working-status')?.data?.status || null)
  const refreshStatus = useCallback((fresh = true) => fetchInto(token, driverRequests.workingStatus(token), { fresh }).then((res) => setStatus(res.data.status)).catch(() => {}), [token])
  useEffect(() => {
    if (access.status !== 'approved') return undefined
    refreshStatus(false)
    // Load every other console page in the background, so the next click shows its data immediately.
    const timer = window.setTimeout(() => prefetchDriverConsole(token), 400)
    return () => window.clearTimeout(timer)
  }, [access.status, refreshStatus, token])
  const driver = user ? { name: user.name } : null
  if (!signedIn) return <div className="page inner-page dv-page"><SignInPrompt openAuth={openAuth} message={t('Sign in with your driver account to open the driver dashboard.')} /></div>
  if (user?.role !== 'driver' || access.status === 'locked') {
    return <DriverVerificationStatusPage token={token} user={user} signedIn={signedIn} openAuth={openAuth} navigate={navigate} notify={notify} />
  }
  return <div className="page inner-page do-page">
    <div className="do-shell">
      <DriverSidebar page={page} navigate={navigate} signOut={signOut} open={menu} onClose={() => setMenu(false)} driver={driver} token={token} />
      <div className="do-main">
        <DriverHeader driver={driver} token={token} status={status} navigate={navigate} onMenu={() => setMenu(true)} title={title} />
        {access.status === 'loading' ? <LoadingSkeleton rows={4} />
          : access.status === 'error' ? <div className="dv-alert danger" role="alert"><span>{access.error}</span></div>
            : <DriverAreaContext.Provider value={{ refreshStatus, status }}>{children}</DriverAreaContext.Provider>}
      </div>
    </div>
  </div>
}

export { WORK_STATUS_LABELS }
