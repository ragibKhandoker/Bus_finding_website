import { useCallback, useEffect, useRef, useState } from 'react'
import { apiRequest } from '../models/api'
import { t } from '../models/i18n'
import { changed as notificationsChanged } from '../models/notifications'

// Live alerts for a signed-in admin, on every page: a new forgot-password request, a new (or emergency) message
// from a user, or a new verification request. Checked every few seconds and whenever the tab comes back.
// Each new item shows a pop-up (see AdminAlert), plays a short sound, refreshes the bell and the Admin page,
// shows the waiting count in the tab title and, when allowed, sends a desktop notification.

export const ADMIN_POLL_MS = 10000
// Fired when something new arrives, so open admin lists reload at once.
export const ADMIN_ALERT_EVENT = 'smartcommute:admin-alert'
// Asks the Admin page to show one section (detail: the section's focus id).
export const ADMIN_FOCUS_EVENT = 'smartcommute:admin-focus'

const BASE_TITLE = typeof document !== 'undefined' ? document.title : ''
const showTitleCount = (count) => { document.title = count > 0 ? `(${count}) ${BASE_TITLE}`.trim() : BASE_TITLE }

// A short two-note chime. Browsers only allow sound after the user has interacted with the page; if not, it is
// silently skipped.
function chime(urgent) {
  try {
    const Context = window.AudioContext || window.webkitAudioContext
    if (!Context) return
    const audio = new Context()
    const notes = urgent ? [880, 660, 880] : [660, 880]
    notes.forEach((frequency, i) => {
      const osc = audio.createOscillator(), gain = audio.createGain()
      osc.frequency.value = frequency
      osc.connect(gain); gain.connect(audio.destination)
      const start = audio.currentTime + i * 0.16
      gain.gain.setValueAtTime(0.0001, start)
      gain.gain.exponentialRampToValueAtTime(0.18, start + 0.02)
      gain.gain.exponentialRampToValueAtTime(0.0001, start + 0.14)
      osc.start(start); osc.stop(start + 0.15)
    })
    window.setTimeout(() => audio.close?.(), 1000)
  } catch { /* no sound */ }
}

export const desktopAlertsSupported = () => typeof window !== 'undefined' && 'Notification' in window
export const desktopAlertsState = () => desktopAlertsSupported() ? Notification.permission : 'unsupported'
export const enableDesktopAlerts = async () => desktopAlertsSupported() ? Notification.requestPermission() : 'unsupported'

function desktopAlert(alert) {
  if (desktopAlertsState() !== 'granted' || !document.hidden) return
  try { new Notification(alert.title, { body: alert.text, tag: `smartcommute-${alert.kind}` }) } catch { /* not shown */ }
}

// What is new since the last check, most urgent first; null when nothing is.
async function describeNew(previous, current, token) {
  if (current.emergencyMessages > previous.emergencyMessages || current.unreadMessages > previous.unreadMessages) {
    const urgent = current.emergencyMessages > previous.emergencyMessages
    const inbox = await apiRequest('/admin/support', { token }).catch(() => null)
    const thread = inbox?.data?.find((item) => urgent ? item.emergencyUnread > 0 : item.unread > 0)
    const name = thread?.user?.name || t('A user')
    const text = thread?.lastMessage?.text || ''
    if (current.pendingPasswordRequests <= previous.pendingPasswordRequests || urgent) {
      return urgent
        ? { kind: 'emergency', focus: 'sp-inbox-title', title: t('Emergency message from {name}', { name }), text }
        : { kind: 'message', focus: 'sp-inbox-title', title: t('New message from {name}', { name }), text }
    }
  }
  if (current.pendingPasswordRequests > previous.pendingPasswordRequests) {
    const list = await apiRequest('/admin/password-requests?status=pending', { token }).catch(() => null)
    const newest = list?.data?.[0]
    return {
      kind: 'password', focus: 'sp-requests-title', title: t('New password request'),
      text: newest?.user ? t('{name} ({email}) forgot their password and is waiting for a temporary one.', { name: newest.user.name, email: newest.user.email })
        : t('A user forgot their password and is waiting for a temporary one.'),
    }
  }
  if ((current.pendingDriverApplications || 0) > (previous.pendingDriverApplications || 0)) {
    return { kind: 'driver', focus: 'admin-drivers', title: t('New driver application'), text: t('A bus driver submitted documents for verification.') }
  }
  if (current.pendingVerifications > previous.pendingVerifications) {
    return { kind: 'verification', focus: 'admin-verifications', title: t('New verification request'), text: t('A user sent a student pass or NID to review.') }
  }
  return null
}

// Returns [alert, dismiss, waiting]: the newest alert to show (or null), a way to close it, and how many items
// are waiting for an admin.
export function useAdminAlerts({ token, isAdmin }) {
  const [alert, setAlert] = useState(null)
  const [waiting, setWaiting] = useState(0)
  const last = useRef(null)
  const busy = useRef(false)
  const check = useCallback(async () => {
    if (!isAdmin || !token || busy.current) return
    busy.current = true
    try {
      const { data } = await apiRequest('/admin/overview', { token })
      const previous = last.current
      last.current = data
      const count = data.pendingPasswordRequests + data.unreadMessages + data.pendingVerifications + (data.pendingDriverApplications || 0)
      setWaiting(count)
      showTitleCount(count)
      if (!previous) return // first check after sign-in: nothing is "new" yet
      const found = await describeNew(previous, data, token)
      if (!found) return
      const next = { ...found, id: Date.now() }
      setAlert(next)
      chime(next.kind === 'emergency')
      desktopAlert(next)
      notificationsChanged()
      window.dispatchEvent(new Event(ADMIN_ALERT_EVENT))
    } catch { /* try again next time */ } finally { busy.current = false }
  }, [token, isAdmin])

  useEffect(() => {
    last.current = null
    if (!isAdmin) { setAlert(null); setWaiting(0); showTitleCount(0); return undefined }
    check()
    const timer = window.setInterval(check, ADMIN_POLL_MS)
    const onVisible = () => { if (!document.hidden) check() }
    window.addEventListener('focus', check)
    document.addEventListener('visibilitychange', onVisible)
    return () => {
      window.clearInterval(timer)
      window.removeEventListener('focus', check)
      document.removeEventListener('visibilitychange', onVisible)
      showTitleCount(0)
    }
  }, [check, isAdmin])

  return [alert, () => setAlert(null), waiting]
}
