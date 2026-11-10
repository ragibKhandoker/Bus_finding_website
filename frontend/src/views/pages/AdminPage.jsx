import React, { useCallback, useEffect, useState } from 'react'
import { ArrowRight, BadgeCheck, BellRing, Check, CircleAlert, Clock3, ClipboardList, Copy, Gift, GraduationCap, History, KeyRound, Lock, Inbox, MessageCircle, Siren, Search, ShieldCheck, UserRound, Users } from 'lucide-react'
import { AdminSupportInbox, PasswordRequestsCard } from './Support'
import { AdminPromotions } from './AdminPromotions'
import { AdminComplaints } from './AdminComplaints'
import { apiRequest as request } from '../../models/api'
import { formatDate, formatNumber, formatTime, t } from '../../models/i18n'
import { errorText } from '../../models/messages'
import { PageHeading, SignInPrompt } from '../components/common'
import { ADMIN_FOCUS_KEY } from '../../controllers/router'
import { ADMIN_ALERT_EVENT, ADMIN_FOCUS_EVENT, desktopAlertsState, enableDesktopAlerts } from '../../controllers/useAdminAlerts'
import { greeting, shortDate } from '../helpers'

// How many verification requests wait for review, with a link to the review queue on the Verification page.
export function AdminVerificationCard({ token, navigate, pending: knownPending }) {
  const [pending, setPending] = useState(knownPending ?? null)
  useEffect(() => {
    request('/verify/all?status=pending&pageSize=1', { token }).then((data) => setPending(data.pending ?? data.total ?? 0)).catch(() => setPending(knownPending ?? null))
  }, [token]) // eslint-disable-line react-hooks/exhaustive-deps
  const waiting = pending > 0
  return <section className="pf-panel adm-verify" id="admin-verifications" aria-labelledby="adm-verify-title">
    <header className="ad-toolbar">
      <div><h2 id="adm-verify-title"><BadgeCheck size={18} /> {t('Account verification')} {waiting && <span className="ad-count">{formatNumber(pending)}</span>}</h2>
        <p>{t('Users send a student pass or NID. Approving marks their account as verified.')}</p></div>
    </header>
    <div className={`adm-verify-body${waiting ? ' waiting' : ''}`}>
      <span className="adm-verify-icon">{waiting ? <Clock3 size={26} /> : <Check size={26} />}</span>
      <div>
        <b>{pending === null ? t('NID and student pass requests from users.') : waiting ? t('{count} requests waiting for review', { count: pending }) : t('No submissions are waiting')}</b>
        <ul>
          <li><GraduationCap size={15} /> {t('Student pass: approve to give student fares')}</li>
          <li><ShieldCheck size={15} /> {t('NID: approve to confirm the person’s identity')}</li>
          <li><Lock size={15} /> {t('Documents are private: only the user and admins can see them')}</li>
        </ul>
      </div>
      <button type="button" className="button button-green" onClick={() => navigate('Verification')}>{t('Review requests')} <ArrowRight size={15} /></button>
    </div>
  </section>
}

// The Admin page's sections, in tab order. focus: the section id a profile shortcut asks for.
export const ADMIN_TABS = [
  { id: 'requests', focus: 'sp-requests-title', icon: KeyRound, label: 'Password requests' },
  { id: 'inbox', focus: 'sp-inbox-title', icon: Inbox, label: 'Support inbox' },
  { id: 'verifications', focus: 'admin-verifications', icon: BadgeCheck, label: 'Verifications' },
  { id: 'users', focus: 'admin-users', icon: Users, label: 'Users' },
  { id: 'complaints', focus: 'admin-complaints', icon: ClipboardList, label: 'Complaint Management' },
  { id: 'offers', focus: 'admin-offers', icon: Gift, label: 'Offers & rewards' },
]

export const USER_ROLE_FILTERS = [['all', 'All'], ['commuter', 'Commuters'], ['driver', 'Drivers'], ['admin', 'Admins']]

export function AdminPage({ user, token, signedIn, isAdmin, openAuth, notify, navigate }) {
  const [search, setSearch] = useState('')
  const [roleFilter, setRoleFilter] = useState('all')
  const [users, setUsers] = useState([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  // A user to open (or start) a conversation with in the support inbox.
  const [messageUser, setMessageUser] = useState(null)
  // Which section is showing. Chosen from a profile shortcut, or else from what needs attention first.
  // Only read here (React may run this twice); the shortcut is cleared by the effect below.
  const [tab, setTab] = useState(() => {
    let focus = ''
    try { focus = sessionStorage.getItem(ADMIN_FOCUS_KEY) || '' } catch { /* no shortcut */ }
    return ADMIN_TABS.find((item) => item.focus === focus)?.id || null
  })
  useEffect(() => { try { sessionStorage.removeItem(ADMIN_FOCUS_KEY) } catch { /* nothing saved */ } }, [])
  const [overview, setOverview] = useState({ data: null, status: 'loading', updatedAt: null })
  const [refreshing, setRefreshing] = useState(false)
  const loadOverview = useCallback(async () => {
    try {
      const result = await request('/admin/overview', { token })
      setOverview({ data: result.data, status: 'ready', updatedAt: new Date() })
    } catch { setOverview((current) => ({ ...current, status: 'error' })) }
  }, [token])
  useEffect(() => {
    if (!isAdmin) return
    loadOverview()
    const timer = window.setInterval(loadOverview, 20000)
    // Something new arrived (useAdminAlerts): refresh the numbers now; "View" on an alert opens its section.
    const onFocus = (event) => { const next = ADMIN_TABS.find((item) => item.focus === event.detail)?.id; if (next) setTab(next) }
    window.addEventListener(ADMIN_ALERT_EVENT, loadOverview)
    window.addEventListener(ADMIN_FOCUS_EVENT, onFocus)
    return () => {
      window.clearInterval(timer)
      window.removeEventListener(ADMIN_ALERT_EVENT, loadOverview)
      window.removeEventListener(ADMIN_FOCUS_EVENT, onFocus)
    }
  }, [isAdmin, loadOverview])
  const [desktopAlerts, setDesktopAlerts] = useState(desktopAlertsState)
  const turnOnDesktopAlerts = async () => {
    const result = await enableDesktopAlerts()
    setDesktopAlerts(result)
    notify(result === 'granted' ? t('Desktop alerts are on.') : t('Desktop alerts are blocked. Allow notifications for this site in your browser settings.'))
  }
  const stats = overview.data
  // No section picked yet: open the one that needs attention, most urgent first.
  useEffect(() => {
    if (tab || overview.status === 'loading') return
    if (!stats) { setTab('users'); return }
    setTab(stats.emergencyMessages ? 'inbox' : stats.pendingPasswordRequests ? 'requests' : stats.unreadMessages ? 'inbox' : stats.pendingVerifications ? 'verifications' : 'users')
  }, [tab, overview.status, stats])

  const load = useCallback(async (term) => {
    setLoading(true)
    try {
      const data = await request(`/admin/users?${new URLSearchParams({ search: term, limit: '200' })}`, { token })
      setUsers(data.data || []); setError('')
    } catch (e) { setUsers([]); setError(errorText(e)) } finally { setLoading(false) }
  }, [token])
  // Wait for the admin to stop typing before searching.
  useEffect(() => {
    if (!isAdmin) return
    const timer = window.setTimeout(() => load(search.trim()), 300)
    return () => window.clearTimeout(timer)
  }, [search, isAdmin, load])
  const refresh = async () => { setRefreshing(true); await Promise.all([loadOverview(), load(search.trim())]); setRefreshing(false) }
  const messageTo = (item) => { setMessageUser(item); setTab('inbox') }

  const heading = <PageHeading eyebrow={t('ADMIN')} title={t('Admin panel.')} description={t('Answer password requests and messages, review verifications and manage user accounts.')} icon={ShieldCheck} />
  if (!signedIn) return <div className="page inner-page">{heading}<SignInPrompt openAuth={openAuth} message={t('Sign in with an admin account to manage users.')} /></div>
  if (!isAdmin) return <div className="page inner-page">{heading}<div className="empty-state panel"><span className="empty-icon"><Lock size={22} /></span><b>{t('Admins only')}</b><span>{t('Your account does not have access to this page.')}</span></div></div>

  const number = (value) => stats ? formatNumber(value || 0) : '–'
  const waiting = stats ? stats.pendingPasswordRequests + stats.unreadMessages + stats.pendingVerifications : 0
  const counts = { requests: stats?.pendingPasswordRequests, inbox: stats?.unreadMessages, verifications: stats?.pendingVerifications, users: stats?.totalUsers }
  const kpis = [
    { tab: 'users', icon: Users, label: 'Total users', value: number(stats?.totalUsers), note: stats ? t('{count} new this week', { count: stats.newThisWeek }) : '', tone: 'neutral' },
    { tab: 'requests', icon: KeyRound, label: 'Password requests', value: number(stats?.pendingPasswordRequests), note: t('Waiting for a temporary password'), tone: stats?.pendingPasswordRequests ? 'warn' : 'ok' },
    { tab: 'inbox', icon: stats?.emergencyMessages ? Siren : Inbox, label: 'Unread messages', value: number(stats?.unreadMessages),
      note: stats?.emergencyMessages ? t('{count} emergency messages waiting', { count: stats.emergencyMessages }) : t('From users to the admins'), tone: stats?.emergencyMessages ? 'danger' : stats?.unreadMessages ? 'warn' : 'ok' },
    { tab: 'verifications', icon: BadgeCheck, label: 'Pending verifications', value: number(stats?.pendingVerifications), note: t('Student pass and NID'), tone: stats?.pendingVerifications ? 'warn' : 'ok' },
  ]
  const shown = roleFilter === 'all' ? users : users.filter((item) => item.role === roleFilter)
  const firstName = (user?.name || '').split(' ')[0]

  return <div className="page inner-page admin-page">
    <section className="adm-hero">
      <div className="adm-hero-text">
        <span className="adm-eyebrow"><ShieldCheck size={14} /> {t('ADMIN PANEL')}</span>
        <h1>{t('Admin panel.')}</h1>
        <p>{t(greeting())}{firstName ? `, ${firstName}` : ''} · {formatDate(new Date(), { weekday: 'long', day: 'numeric', month: 'long' })}</p>
        <span className={`adm-status${overview.status === 'error' ? ' error' : waiting ? ' busy' : ''}`}>
          <i />{overview.status === 'error' ? t('Could not load the latest numbers.') : !stats ? t('Loading…') : waiting ? t('{count} items need your attention.', { count: waiting }) : t('Nothing is waiting. All caught up!')}
        </span>
      </div>
      <div className="adm-hero-actions">
        {desktopAlerts === 'default' && <button type="button" className="button adm-ghost" onClick={turnOnDesktopAlerts}><BellRing size={15} /> {t('Turn on desktop alerts')}</button>}
        <button type="button" className="button adm-ghost" onClick={refresh} disabled={refreshing}><History size={15} /> {refreshing ? t('Refreshing…') : t('Refresh')}</button>
        <button type="button" className="button button-light" onClick={() => navigate('Profile')}><UserRound size={15} /> {t('My profile')}</button>
      </div>
      {overview.updatedAt && <small className="adm-updated">{t('Updated {time}', { time: formatTime(overview.updatedAt) })}</small>}
    </section>

    <div className="adm-kpis">
      {kpis.map((kpi) => {
        const Icon = kpi.icon
        return <button key={kpi.tab} type="button" className={`adm-kpi ${kpi.tone}${tab === kpi.tab ? ' current' : ''}`} onClick={() => setTab(kpi.tab)} data-testid="admin-kpi">
          <span className="adm-kpi-icon"><Icon size={19} /></span>
          <span className="adm-kpi-text"><small>{t(kpi.label)}</small><b>{kpi.value}</b><em>{kpi.note}</em></span>
        </button>
      })}
    </div>

    <nav className="adm-tabs" role="tablist" aria-label={t('Admin sections')}>
      {ADMIN_TABS.map((item) => {
        const Icon = item.icon
        const count = counts[item.id]
        const alert = item.id === 'inbox' && stats?.emergencyMessages > 0
        return <button key={item.id} type="button" role="tab" id={`adm-tab-${item.id}`} aria-selected={tab === item.id} aria-controls="adm-panel"
          className={`adm-tab${tab === item.id ? ' selected' : ''}`} onClick={() => setTab(item.id)}>
          <Icon size={16} /> <span>{t(item.label)}</span>
          {stats && (item.id === 'users' || count > 0) && <span className={`adm-tab-count${alert ? ' alert' : item.id === 'users' ? ' muted' : ''}`}>{formatNumber(count || 0)}</span>}
        </button>
      })}
    </nav>

    <div className="adm-panel" id="adm-panel" role="tabpanel" aria-labelledby={tab ? `adm-tab-${tab}` : undefined}>
      {!tab ? <div className="pf-panel adm-loading"><span className="spinner" /></div> : <>
        {tab === 'requests' && <PasswordRequestsCard token={token} notify={(message) => { notify(message); loadOverview() }} />}
        {tab === 'inbox' && <AdminSupportInbox token={token} startWith={messageUser} onStarted={() => setMessageUser(null)} />}
        {tab === 'verifications' && <AdminVerificationCard token={token} navigate={navigate} pending={stats?.pendingVerifications} />}
        {tab === 'complaints' && <AdminComplaints token={token} notify={notify} />}
        {tab === 'offers' && <AdminPromotions token={token} notify={notify} />}
        {tab === 'users' && <section className="pf-panel ad-panel" id="admin-users">
          <header className="ad-toolbar">
            <div><h2><Users size={18} /> {t('All users')} <span className="ad-count">{formatNumber(shown.length)}</span></h2><p>{t('Message a user. Passwords are never shown: a user who forgot theirs sends a request that you approve.')}</p></div>
            <label className="ad-search"><Search size={16} /><input value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('Search by name or email')} /></label>
          </header>
          <div className="adm-filters" role="group" aria-label={t('Filter by role')}>
            {USER_ROLE_FILTERS.map(([value, label]) => <button key={value} type="button" aria-pressed={roleFilter === value} className={roleFilter === value ? 'selected' : ''} onClick={() => setRoleFilter(value)}>
              {t(label)} <span>{formatNumber(value === 'all' ? users.length : users.filter((item) => item.role === value).length)}</span></button>)}
          </div>
          {error && <div className="auth-error" role="alert"><CircleAlert size={17} /><span>{error}</span></div>}
          {loading && !users.length ? <div className="empty-state"><span className="spinner" /> {t('Loading users…')}</div>
            : !shown.length ? !error && <div className="empty-state"><span className="empty-icon"><Users size={22} /></span><b>{t('No users found')}</b><span>{search ? t('Try a different name or email.') : roleFilter !== 'all' ? t('No accounts with this role yet.') : t('No accounts have been created yet.')}</span></div>
            : <div className="ad-table" role="table">
              <div className="ad-row ad-head" role="row"><span role="columnheader">{t('User')}</span><span role="columnheader">{t('Role')}</span><span role="columnheader">{t('Joined')}</span><span role="columnheader">{t('Last sign-in')}</span><span role="columnheader"><span className="sr-only">{t('Actions')}</span></span></div>
              {shown.map((item) => <div className="ad-row" role="row" key={item.id}>
                <span className="ad-user" role="cell"><span className={`ad-avatar role-${item.role}`}>{item.role === 'admin' ? <ShieldCheck size={16} /> : item.name?.[0]?.toUpperCase() || 'U'}</span><span><b>{item.name}</b><small>{item.email}</small></span></span>
                <span role="cell"><span className={`adm-role role-${item.role}`}>{t(item.role)}</span></span>
                <span role="cell" className="ad-meta"><small>{t('Joined')}</small>{shortDate(item.createdAt)}</span>
                <span role="cell" className="ad-meta"><small>{t('Last sign-in')}</small>{item.lastLoginAt ? shortDate(item.lastLoginAt) : <em className="adm-never">{t('Never')}</em>}</span>
                <span role="cell" className="ad-action">{item.role === 'admin'
                  ? <small className="ad-note">{t('Set in server config')}</small>
                  : <span className="ad-buttons">
                    <button className="button button-outline button-small" onClick={() => messageTo(item)} aria-label={t('Message {name}', { name: item.name })}><MessageCircle size={14} /> {t('Message')}</button>
                  </span>}</span>
              </div>)}
            </div>}
        </section>}
      </>}
    </div>

  </div>
}
