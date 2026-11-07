import { useCallback, useEffect, useState } from 'react'
import { NOTIFICATIONS_CHANGED, UNREAD_POLL_MS, notificationApi } from '../models/notifications'

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
