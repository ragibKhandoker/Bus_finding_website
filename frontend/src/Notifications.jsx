import React, { useCallback, useEffect, useRef, useState } from 'react'
import { AlertTriangle, Bell, Check, CheckCheck, ExternalLink, Gift, MapPin, RefreshCw, Siren, Trash2, UserPlus, UserRound, Users } from 'lucide-react'
import { apiRequest } from './api'
import { formatDateTime, formatNumber, t } from './i18n'
import { Feedback, Heading, Loading, RequireSignIn } from './MorePages'

// In-app notifications (the bell in the top bar and the Notifications page) and the SOS alert. Every call carries
// the sign-in token; the server only ever returns or changes the signed-in user's own notifications.

const errorText = (error) => t(error?.message || 'Something went wrong. Please try again.')
const idOf = (item) => String(item?._id || item?.id || '')
const PAGE_SIZE = 20
// How often the bell re-checks the unread count while the tab is visible.
const UNREAD_POLL_MS = 30000
// Fired after anything changes the unread count, so the bell updates at once.
export const NOTIFICATIONS_CHANGED = 'smartcommute:notifications-changed'
const changed = () => window.dispatchEvent(new Event(NOTIFICATIONS_CHANGED))

export const notificationApi = {
  list: (userId, token, page = 1) => apiRequest(`/api/notifications/user/${encodeURIComponent(userId)}?page=${page}&pageSize=${PAGE_SIZE}`, { token }),
  unread: (userId, token) => apiRequest(`/api/notifications/unread/${encodeURIComponent(userId)}`, { token }),
  markRead: (id, token) => apiRequest(`/api/notifications/read/${encodeURIComponent(id)}`, { token, method: 'PATCH' }),
  markAllRead: (userId, token) => apiRequest(`/api/notifications/read-all/${encodeURIComponent(userId)}`, { token, method: 'PATCH' }),
  remove: (id, token) => apiRequest(`/api/notifications/${encodeURIComponent(id)}`, { token, method: 'DELETE' }),
  clear: (userId, token) => apiRequest(`/api/notifications/clear/${encodeURIComponent(userId)}`, { token, method: 'DELETE' }),
  sos: (body, token) => apiRequest('/api/notifications/sos', { token, method: 'POST', body: JSON.stringify(body) }),
}

export const badgeText = (count) => count > 99 ? '99+' : formatNumber(count)

// The signed-in user's unread count: loaded on sign-in, re-checked every 30 seconds while the tab is visible, and
// at once whenever NOTIFICATIONS_CHANGED fires.
export function useUnreadCount({ token, user, signedIn }) {
  const userId = String(user?.id || user?._id || '')
  const [count, setCount] = useState(0)
  const refresh = useCallback(async () => {
    if (!signedIn || !userId) { setCount(0); return }
    try { setCount((await notificationApi.unread(userId, token)).count || 0) } catch { /* keep the last known count */ }
  }, [signedIn, userId, token])
  useEffect(() => {
    refresh()
    if (!signedIn) return undefined
    const timer = window.setInterval(() => { if (!document.hidden) refresh() }, UNREAD_POLL_MS)
    window.addEventListener(NOTIFICATIONS_CHANGED, refresh)
    window.addEventListener('focus', refresh)
    return () => { window.clearInterval(timer); window.removeEventListener(NOTIFICATIONS_CHANGED, refresh); window.removeEventListener('focus', refresh) }
  }, [refresh, signedIn])
  return count
}

export function NotificationBell({ count, onOpen }) {
  const label = count ? t('Notifications, {count} unread', { count }) : t('Notifications')
  return <button type="button" className="icon-button notification-bell" onClick={onOpen} title={label} aria-label={label}>
    <Bell size={18} aria-hidden="true" />
    {count > 0 && <span className="notification-badge" data-testid="unread-badge">{badgeText(count)}</span>}
  </button>
}

// Opens a page inside the app (e.g. "/friends") the same way the browser's back and forward buttons do.
const openInApp = (path) => { window.history.pushState({}, '', path); window.dispatchEvent(new PopStateEvent('popstate')) }
// Only Google Maps links (SOS locations) leave the app; anything else must be a path inside it.
const isMapLink = (url) => /^https:\/\/www\.google\.com\/maps\?q=-?\d/.test(url || '')
const isAppPath = (url) => /^\/[a-z0-9/-]*$/i.test(url || '')

const CATEGORY_ICONS = { friend: UserPlus, ride: Users, reward: Gift, sos: Siren, account: UserRound }
const CATEGORY_LABELS = { friend: 'Friends', ride: 'Ride share', reward: 'Reward', sos: 'SOS', account: 'Account', general: 'Update' }

function NotificationRow({ item, busy, onRead, onDelete }) {
  const Icon = CATEGORY_ICONS[item.category] || (item.type === 'sos' ? Siren : Bell)
  const sos = item.category === 'sos' || item.type === 'sos'
  return <div className={`notification-row${item.isRead ? '' : ' unread'}${sos ? ' sos' : ''}`} data-testid="notification">
    <span className={`notification-dot ${item.category || 'general'}`} aria-hidden="true"><Icon size={16} /></span>
    <div className="notification-copy">
      <small className="notification-kind">{t(CATEGORY_LABELS[item.category] || 'Update')}{!item.isRead && <b className="notification-new"> · {t('New')}</b>}</small>
      <b>{item.title}</b>
      <p>{item.message}</p>
      <div className="notification-meta">
        <small><time dateTime={item.createdAt}>{formatDateTime(item.createdAt)}</time></small>
        {isMapLink(item.actionUrl) && <a className="notification-map" href={item.actionUrl} target="_blank" rel="noopener noreferrer"><MapPin size={13} aria-hidden="true" /> {t('Open location in Google Maps')} <ExternalLink size={12} aria-hidden="true" /></a>}
        {isAppPath(item.actionUrl) && <button type="button" className="link-button" onClick={() => { if (!item.isRead) onRead(item); openInApp(item.actionUrl) }}>{t('Open')}</button>}
      </div>
    </div>
    <div className="notification-row-actions">
      {!item.isRead && <button type="button" className="extra-icon-button" disabled={busy} onClick={() => onRead(item)} aria-label={t('Mark "{title}" as read', { title: item.title })} title={t('Mark as read')}><Check size={15} /></button>}
      <button type="button" className="extra-icon-button danger" disabled={busy} onClick={() => onDelete(item)} aria-label={t('Delete "{title}"', { title: item.title })} title={t('Delete')}><Trash2 size={15} /></button>
    </div>
  </div>
}

export function NotificationsPage({ token, user, signedIn, openAuth, notify }) {
  const userId = String(user?.id || user?._id || '')
  const [items, setItems] = useState([])
  const [unread, setUnread] = useState(0)
  const [page, setPage] = useState(1)
  const [totalPages, setTotalPages] = useState(1)
  const [status, setStatus] = useState('loading') // loading | ready | error
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(() => new Set()) // notification ids, or 'all'
  const [loadingMore, setLoadingMore] = useState(false)

  const load = useCallback(async () => {
    if (!signedIn || !userId) return
    setStatus('loading')
    try {
      const data = await notificationApi.list(userId, token)
      setItems(data.data || []); setUnread(data.unreadCount ?? 0); setPage(1); setTotalPages(data.totalPages || 1)
      setStatus('ready'); setError('')
    } catch (e) { setStatus('error'); setError(errorText(e)) }
  }, [signedIn, userId, token])
  useEffect(() => { load() }, [load])

  const loadMore = async () => {
    if (loadingMore) return
    setLoadingMore(true)
    try {
      const data = await notificationApi.list(userId, token, page + 1)
      setItems((current) => [...current, ...(data.data || []).filter((n) => !current.some((c) => idOf(c) === idOf(n)))])
      setPage(page + 1); setTotalPages(data.totalPages || 1); setUnread(data.unreadCount ?? unread)
    } catch (e) { setError(errorText(e)) } finally { setLoadingMore(false) }
  }

  const run = async (key, action) => {
    if (busy.has(key) || busy.has('all')) return
    setBusy((current) => new Set(current).add(key)); setError('')
    try { await action(); changed() } catch (e) { setError(errorText(e)) } finally { setBusy((current) => { const next = new Set(current); next.delete(key); return next }) }
  }
  const markRead = (item) => run(idOf(item), async () => {
    const data = await notificationApi.markRead(idOf(item), token)
    setItems((current) => current.map((n) => idOf(n) === idOf(item) ? { ...n, isRead: true } : n))
    setUnread(data.unreadCount ?? Math.max(0, unread - 1))
  })
  const remove = (item) => run(idOf(item), async () => {
    const data = await notificationApi.remove(idOf(item), token)
    setItems((current) => current.filter((n) => idOf(n) !== idOf(item)))
    setUnread(data.unreadCount ?? unread)
  })
  const markAll = () => run('all', async () => {
    await notificationApi.markAllRead(userId, token)
    setItems((current) => current.map((n) => ({ ...n, isRead: true }))); setUnread(0)
    notify(t('Notifications marked as read.'))
  })
  const clear = () => {
    if (!window.confirm(t('Delete all your notifications? This cannot be undone.'))) return
    run('all', async () => {
      await notificationApi.clear(userId, token)
      setItems([]); setUnread(0); setTotalPages(1)
      notify(t('Notifications cleared.'))
    })
  }

  const heading = <Heading icon={Bell} eyebrow={t('UPDATES FOR YOU')} title={t('Notifications')} subtitle={t('Friend requests, ride updates, rewards and SOS alerts from the people you know.')} />
  if (!signedIn) return <section className="extra-page">{heading}<RequireSignIn openAuth={openAuth} /></section>

  const allBusy = busy.has('all')
  return <section className="extra-page notifications-page">
    {heading}
    <Feedback error={status === 'ready' ? error : ''} />
    <div className="notifications-layout">
      <article className="extra-panel notification-panel">
        <div className="extra-panel-title">
          <div><small>{t('YOUR INBOX')}</small><h2>{t('Notifications')} {status === 'ready' && <span data-testid="page-unread">{t('{count} unread', { count: unread })}</span>}</h2></div>
          <div className="notification-actions">
            <button type="button" className="extra-icon-button" onClick={load} aria-label={t('Refresh')} title={t('Refresh')}><RefreshCw size={15} /></button>
            <button type="button" className="extra-button secondary" disabled={allBusy || !unread} onClick={markAll}><CheckCheck size={14} /> {t('Mark all read')}</button>
            <button type="button" className="extra-button secondary danger" disabled={allBusy || !items.length} onClick={clear}><Trash2 size={14} /> {t('Clear all')}</button>
          </div>
        </div>
        {status === 'loading' ? <Loading label="Loading notifications…" />
          : status === 'error' ? <div className="notification-empty"><AlertTriangle size={22} /><b>{t('Could not load your notifications.')}</b><span>{error}</span><button type="button" className="extra-button" onClick={load}><RefreshCw size={14} /> {t('Try again')}</button></div>
            : items.length ? <>
              {items.map((item) => <NotificationRow key={idOf(item)} item={item} busy={busy.has(idOf(item)) || allBusy} onRead={markRead} onDelete={remove} />)}
              {page < totalPages && <button type="button" className="extra-button secondary notification-more" onClick={loadMore} disabled={loadingMore}>{loadingMore ? t('Loading…') : t('Show older notifications')}</button>}
            </>
              : <div className="notification-empty"><Bell size={22} /><b>{t('You are all caught up.')}</b><span>{t('Friend requests, ride updates, rewards and SOS alerts will appear here.')}</span></div>}
      </article>
      <SosAlert token={token} signedIn={signedIn} openAuth={openAuth} notify={notify} />
    </div>
  </section>
}

const SOS_KINDS = [['General', 'Any help'], ['Medical', 'Medical'], ['Safety', 'Safety / police'], ['Fire', 'Fire']]
const LOCATION_ERRORS = {
  1: 'Location permission was denied, so no alert was sent. Allow location for this site in your browser settings and try again.',
  2: 'Your location could not be found, so no alert was sent. Check that location is turned on and try again.',
  3: 'Finding your location took too long, so no alert was sent. Try again, ideally near a window or outdoors.',
}

// Gets the browser's current position; rejects with a readable message. Never falls back to a made-up location.
export function currentPosition(geo = typeof navigator !== 'undefined' ? navigator.geolocation : undefined) {
  return new Promise((resolve, reject) => {
    if (!geo) { reject(new Error(t('This browser cannot share your location, so no alert was sent.'))); return }
    geo.getCurrentPosition(
      ({ coords }) => Number.isFinite(coords?.latitude) && Number.isFinite(coords?.longitude)
        ? resolve({ latitude: coords.latitude, longitude: coords.longitude })
        : reject(new Error(t(LOCATION_ERRORS[2]))),
      (error) => reject(new Error(t(LOCATION_ERRORS[error?.code] || LOCATION_ERRORS[2]))),
      { enableHighAccuracy: true, timeout: 15000, maximumAge: 0 },
    )
  })
}

// The SOS alert: choose what help you need, confirm, then the browser is asked for your location and the alert
// goes to your friends and ride companions. It is an in-app alert only.
export function SosAlert({ token, signedIn, openAuth, notify }) {
  const [step, setStep] = useState('idle') // idle | confirm | locating | sending | sent | error
  const [kind, setKind] = useState('General')
  const [note, setNote] = useState('')
  const [message, setMessage] = useState('')
  const sendingRef = useRef(false)

  const start = () => { if (!signedIn) { openAuth(); return } setStep('confirm'); setMessage('') }
  const send = async () => {
    if (sendingRef.current) return
    sendingRef.current = true
    try {
      setStep('locating'); setMessage('')
      const position = await currentPosition()
      setStep('sending')
      const data = await notificationApi.sos({ ...position, serviceName: kind === 'General' ? '' : kind, message: note.trim() || undefined }, token)
      const sent = data.count === 1 ? t('SOS sent to 1 person.') : t('SOS sent to {count} people.', { count: data.count })
      setStep('sent'); setMessage(sent)
      notify(sent)
      setNote('')
    } catch (e) {
      setStep('error'); setMessage(errorText(e))
    } finally { sendingRef.current = false }
  }

  const working = step === 'locating' || step === 'sending'
  return <article className="extra-panel sos-panel" aria-live="polite">
    <div>
      <small>{t('NEED URGENT HELP?')}</small>
      <h2>{t('SOS alert')}</h2>
      <p>{t('Sends an alert with a Google Maps link to your current location to your friends and the people on your open ride shares. Nobody else sees it.')}</p>
      <p className="sos-warning"><AlertTriangle size={13} aria-hidden="true" /> {t('This does not contact emergency services. In danger, call 999.')}</p>
    </div>
    {step === 'idle' || step === 'sent' ? <>
      {step === 'sent' && <p className="sos-result success" role="status"><Check size={14} aria-hidden="true" /> {message}</p>}
      <button type="button" className="extra-button sos-button" onClick={start}><Siren size={15} aria-hidden="true" /> {t('Send SOS')}</button>
    </> : <div className="sos-confirm">
      <fieldset disabled={working}>
        <legend>{t('What help do you need?')}</legend>
        <div className="sos-kinds">{SOS_KINDS.map(([value, label]) => <label key={value} className={kind === value ? 'active' : ''}>
          <input type="radio" name="sos-kind" value={value} checked={kind === value} onChange={() => setKind(value)} /> {t(label)}
        </label>)}</div>
        <label className="sos-note"><span>{t('Short note (optional)')}</span><input value={note} maxLength={200} onChange={(e) => setNote(e.target.value)} placeholder={t('e.g. On the bus near Farmgate')} /></label>
      </fieldset>
      <p className="sos-permission"><MapPin size={13} aria-hidden="true" /> {t('Your browser will ask to share your location. The alert is only sent once your real location is found.')}</p>
      {step === 'error' && <p className="sos-result error" role="alert">{message}</p>}
      <div className="sos-confirm-actions">
        <button type="button" className="extra-button sos-button" onClick={send} disabled={working}>
          {step === 'locating' ? t('Finding your location…') : step === 'sending' ? t('Sending SOS…') : step === 'error' ? t('Try again') : t('Yes, send SOS now')}
        </button>
        <button type="button" className="extra-button secondary" onClick={() => { setStep('idle'); setMessage('') }} disabled={working}>{t('Cancel')}</button>
      </div>
    </div>}
  </article>
}
