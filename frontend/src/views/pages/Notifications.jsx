import React, { useCallback, useEffect, useRef, useState } from 'react'
import { AlertTriangle, Bell, Check, CheckCheck, ExternalLink, Gift, MapPin, RefreshCw, Share2, Siren, Trash2, UserPlus, UserRound, Users } from 'lucide-react'
import { formatDateTime, formatNumber, getLang, t } from '../../models/i18n'
import { Feedback, Heading, Loading, RequireSignIn } from './MorePages'
import { errorText } from '../../models/messages'
import { changed, notificationApi } from '../../models/notifications'
import { usePersistentState } from '../../controllers/usePersistentState'
import { openPath } from '../../controllers/router'

export { errorText } from '../../models/messages'
export { PAGE_SIZE } from '../../models/notifications'
export { UNREAD_POLL_MS } from '../../models/notifications'
export { NOTIFICATIONS_CHANGED } from '../../models/notifications'
export { changed } from '../../models/notifications'
export { notificationApi } from '../../models/notifications'
export { useUnreadCount } from '../../controllers/useUnreadCount'

// In-app notifications (the bell in the top bar and the Notifications page) and the SOS alert. Every call carries
// the sign-in token; the server only ever returns or changes the signed-in user's own notifications.

export const idOf = (item) => String(item?._id || item?.id || '')

export const badgeText = (count) => count > 99 ? '99+' : formatNumber(count)

export function NotificationBell({ count, onOpen }) {
  const label = count ? t('Notifications, {count} unread', { count }) : t('Notifications')
  return <button type="button" className="icon-button notification-bell" onClick={onOpen} title={label} aria-label={label}>
    <Bell size={18} aria-hidden="true" />
    {count > 0 && <span className="notification-badge" data-testid="unread-badge">{badgeText(count)}</span>}
  </button>
}

// Opens a page inside the app (e.g. "/friends") as normal navigation: one new history entry, so Back returns here.
export const openInApp = openPath

// Only Google Maps links (SOS locations) leave the app; anything else must be a path inside it.
export const isMapLink = (url) => /^https:\/\/www\.google\.com\/maps\?q=-?\d/.test(url || '')

export const isAppPath = (url) => /^\/[a-z0-9/-]*$/i.test(url || '')

export const CATEGORY_ICONS = { friend: UserPlus, ride: Users, reward: Gift, sos: Siren, account: UserRound }

export const CATEGORY_LABELS = { friend: 'Friends', ride: 'Ride share', reward: 'Reward', sos: 'SOS', account: 'Account', general: 'Update' }

// The point in a Google Maps link like https://www.google.com/maps?q=23.81,90.41, or null.
const pointOf = (url) => {
  try {
    const [lat, lng] = (new URL(url).searchParams.get('q') || '').split(',').map(Number)
    return Number.isFinite(lat) && Number.isFinite(lng) && Math.abs(lat) <= 90 && Math.abs(lng) <= 180 ? { lat, lng } : null
  } catch { return null }
}

// A shared location (e.g. in an SOS): a small map showing the spot, its coordinates, and a clear button that
// opens it in Google Maps. The map is Google's key-free embed, loaded only when it scrolls into view.
export function SharedLocation({ url }) {
  const point = pointOf(url)
  const embed = point && `https://maps.google.com/maps?${new URLSearchParams({ q: `${point.lat},${point.lng}`, z: '16', hl: getLang() === 'bn' ? 'bn' : 'en', output: 'embed' })}`
  return <div className="shared-location" data-testid="shared-location">
    {embed && <iframe className="shared-location-map" title={t('Map of the shared location')} src={embed} loading="lazy" referrerPolicy="no-referrer-when-downgrade" />}
    <div className="shared-location-bar">
      <span className="shared-location-pin" aria-hidden="true"><MapPin size={16} /></span>
      <div className="shared-location-text">
        <b>{t('Shared location')}</b>
        {point && <small>{formatNumber(point.lat, { maximumFractionDigits: 5, useGrouping: false })}, {formatNumber(point.lng, { maximumFractionDigits: 5, useGrouping: false })}</small>}
      </div>
      <a className="shared-location-open" href={url} target="_blank" rel="noopener noreferrer">
        <MapPin size={14} aria-hidden="true" /> {t('Open location in Google Maps')} <ExternalLink size={13} aria-hidden="true" />
      </a>
    </div>
  </div>
}

export function NotificationRow({ item, busy, onRead, onDelete }) {
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
        {isAppPath(item.actionUrl) && <button type="button" className="link-button" onClick={() => { if (!item.isRead) onRead(item); openInApp(item.actionUrl) }}>{t('Open')}</button>}
      </div>
      {isMapLink(item.actionUrl) && <SharedLocation url={item.actionUrl} />}
    </div>
    <div className="notification-row-actions">
      {!item.isRead && <button type="button" className="extra-icon-button" disabled={busy} onClick={() => onRead(item)} aria-label={t('Mark "{title}" as read', { title: item.title })} title={t('Mark as read')}><Check size={15} /></button>}
      <button type="button" className="extra-icon-button danger" disabled={busy} onClick={() => onDelete(item)} aria-label={t('Delete "{title}"', { title: item.title })} title={t('Delete')}><Trash2 size={15} /></button>
    </div>
  </div>
}

export function NotificationsPage({ token, user, signedIn, openAuth, notify, navigate }) {
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
      <SosAlert token={token} signedIn={signedIn} openAuth={openAuth} notify={notify} navigate={navigate} />
    </div>
  </section>
}

export const SOS_KINDS = [['General', 'Any help'], ['Medical', 'Medical'], ['Safety', 'Safety / police'], ['Fire', 'Fire']]

export const LOCATION_ERRORS = {
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
// Before it is pressed, it says how many people it would reach (and offers to add friends when nobody). Once the
// real location is found it is shown, with a Google Maps link to share by hand, whether or not an
// (The site never places phone calls itself: there is deliberately no "call 999" button.)
// alert could be sent, and handed to onLocated (the map page puts it on the map).
// navigate: optional, for the "Add friends" button.
const mapsLink = ({ latitude, longitude }) => `https://www.google.com/maps?q=${latitude.toFixed(6)},${longitude.toFixed(6)}`

function MyLocation({ position, notify }) {
  const link = mapsLink(position)
  const share = async () => {
    const text = t('My current location')
    try {
      if (navigator.share) { await navigator.share({ title: text, text, url: link }); return }
      await navigator.clipboard.writeText(link)
      notify?.(t('Location link copied. Paste it in a message to someone you trust.'))
    } catch { /* the share sheet was closed */ }
  }
  return <div className="sos-location" data-testid="sos-location">
    <div className="sos-location-head">
      <span className="sos-location-dot" aria-hidden="true"><MapPin size={15} /></span>
      <div><b>{t('Your current location')}</b><small>{formatNumber(position.latitude, { maximumFractionDigits: 5, useGrouping: false })}, {formatNumber(position.longitude, { maximumFractionDigits: 5, useGrouping: false })}</small></div>
    </div>
    <div className="sos-location-actions">
      <a className="extra-button secondary" href={link} target="_blank" rel="noreferrer"><ExternalLink size={13} aria-hidden="true" /> {t('Open in Google Maps')}</a>
      <button type="button" className="extra-button secondary" onClick={share}><Share2 size={13} aria-hidden="true" /> {typeof navigator !== 'undefined' && navigator.share ? t('Share location') : t('Copy location link')}</button>
    </div>
    {/* Advice only, as plain text: the site never places a call itself. */}
    <p className="sos-emergency-note" data-testid="sos-emergency-note"><AlertTriangle size={15} aria-hidden="true" /> <span><b>{t('In an emergency, call 999.')}</b> {t('Call it yourself from your phone; this website does not call anyone.')}</span></p>
  </div>
}

export function SosAlert({ token, signedIn, openAuth, notify, onLocated, navigate }) {
  const [step, setStep] = useState('idle') // idle | confirm | locating | sending | sent | nobody | error
  // What help is needed, the note and the location found are kept when leaving the page and coming back.
  const [kind, setKind] = usePersistentState('sos:kind', 'General')
  const [note, setNote] = usePersistentState('sos:note', '')
  const [message, setMessage] = useState('')
  const [position, setPosition] = usePersistentState('sos:position', null)
  const [reach, setReach] = useState(null) // how many people an SOS would reach; null while unknown
  const sendingRef = useRef(false)

  useEffect(() => {
    if (!signedIn) { setReach(null); return }
    let active = true
    notificationApi.sosRecipients(token).then((data) => { if (active) setReach(Number(data.count) || 0) }).catch(() => { if (active) setReach(null) })
    return () => { active = false }
  }, [signedIn, token])

  const found = (spot) => { setPosition(spot); onLocated?.(spot) }
  const start = () => { if (!signedIn) { openAuth(); return } setStep('confirm'); setMessage('') }
  const send = async () => {
    if (sendingRef.current) return
    sendingRef.current = true
    try {
      setStep('locating'); setMessage('')
      const spot = await currentPosition()
      found(spot)
      setStep('sending')
      const data = await notificationApi.sos({ ...spot, serviceName: kind === 'General' ? '' : kind, message: note.trim() || undefined }, token)
      const sent = data.count === 1 ? t('SOS sent to 1 person.') : t('SOS sent to {count} people.', { count: data.count })
      setStep('sent'); setMessage(sent); setReach(data.count)
      notify(sent)
      setNote('')
    } catch (e) {
      // 409: nobody to alert. The location was still found, so it stays on screen to share by hand.
      setStep(e?.status === 409 ? 'nobody' : 'error'); setMessage(errorText(e))
      if (e?.status === 409) setReach(0)
    } finally { sendingRef.current = false }
  }
  // Only find and show the location, without sending anything (for when nobody would receive an alert).
  const showLocation = async () => {
    if (!signedIn) { openAuth(); return }
    setStep('locating'); setMessage('')
    try { found(await currentPosition()); setStep('idle') } catch (e) { setStep('error'); setMessage(errorText(e)) }
  }

  const working = step === 'locating' || step === 'sending'
  const confirming = ['confirm', 'locating', 'sending', 'error'].includes(step)
  const addFriends = navigate && <button type="button" className="extra-button secondary" onClick={() => navigate('Friends')}><UserPlus size={13} aria-hidden="true" /> {t('Add friends')}</button>
  return <article className="extra-panel sos-panel" aria-live="polite">
    <div className="sos-intro">
      <div className="tool-heading"><span className="tool-icon is-sos" aria-hidden="true"><Siren size={19} /></span><div><small>{t('NEED URGENT HELP?')}</small><h2>{t('SOS alert')}</h2></div></div>
      <p>{t('Sends an alert with a Google Maps link to your current location to your friends and the people on your open ride shares. Nobody else sees it.')}</p>
      <p className="sos-warning"><AlertTriangle size={13} aria-hidden="true" /> {t('This does not contact emergency services. In danger, call 999.')}</p>
    </div>

    {signedIn && reach === 0 && step !== 'nobody' && <div className="sos-reach is-none" data-testid="sos-reach">
      <Users size={15} aria-hidden="true" />
      <span>{t('No one would receive an SOS yet. Add friends so it reaches someone you trust.')}</span>
      {addFriends}
    </div>}
    {signedIn && reach > 0 && !confirming && <p className="sos-reach" data-testid="sos-reach"><Users size={14} aria-hidden="true" /> {reach === 1 ? t('Your SOS will reach 1 person.') : t('Your SOS will reach {count} people.', { count: reach })}</p>}

    {step === 'nobody' && <div className="sos-result error" role="alert">
      <span>{message}</span>
      {addFriends}
    </div>}
    {position && !working && <MyLocation position={position} notify={notify} />}

    {!confirming ? <>
      {step === 'sent' && <p className="sos-result success" role="status"><Check size={14} aria-hidden="true" /> {message}</p>}
      <div className="sos-main-actions">
        <button type="button" className="extra-button sos-button" onClick={start}><Siren size={15} aria-hidden="true" /> {t('Send SOS')}</button>
        {signedIn && !position && <button type="button" className="extra-button secondary" onClick={showLocation}><MapPin size={14} aria-hidden="true" /> {t('Show my location')}</button>}
      </div>
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
