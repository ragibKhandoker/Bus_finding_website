import React, { useCallback, useEffect, useRef, useState } from 'react'
import { CheckCircle2, CircleAlert, Info, X } from 'lucide-react'
import { t } from '../../models/i18n'

// Pop-up notifications in the top-right corner, under the header. Several can show at once (newest on top);
// each closes by itself after a few seconds, unless the pointer is over it, and can be closed with ×.

export const TOAST_MS = 4500
const MAX_TOASTS = 4
const KINDS = {
  success: { icon: CheckCircle2, title: 'Success' },
  info: { icon: Info, title: 'Notice' },
  error: { icon: CircleAlert, title: 'Something went wrong' },
}

let nextId = 0

// notify(message, kind = 'success') adds a toast; kind is 'success', 'info' or 'error'.
export function useToasts() {
  const [toasts, setToasts] = useState([])
  const dismiss = useCallback((id) => setToasts((list) => list.map((toast) => toast.id === id ? { ...toast, leaving: true } : toast)), [])
  const remove = useCallback((id) => setToasts((list) => list.filter((toast) => toast.id !== id)), [])
  const notify = useCallback((message, kind = 'success') => {
    if (!message) return
    const id = ++nextId
    setToasts((list) => [{ id, message, kind: KINDS[kind] ? kind : 'success' }, ...list.filter((toast) => toast.message !== message)].slice(0, MAX_TOASTS))
  }, [])
  return { toasts, notify, dismiss, remove }
}

function Toast({ toast, onDismiss, onRemove }) {
  const { icon: Icon, title } = KINDS[toast.kind]
  const [paused, setPaused] = useState(false)
  const remaining = useRef(TOAST_MS)
  const startedAt = useRef(0)

  // Count down only while not paused; hovering keeps the toast up and resumes where it stopped.
  useEffect(() => {
    if (paused || toast.leaving) return
    startedAt.current = Date.now()
    const timer = window.setTimeout(() => onDismiss(toast.id), remaining.current)
    return () => { window.clearTimeout(timer); remaining.current -= Date.now() - startedAt.current }
  }, [paused, toast.leaving, toast.id, onDismiss])
  // After the exit animation, take it out of the list.
  useEffect(() => {
    if (!toast.leaving) return
    const timer = window.setTimeout(() => onRemove(toast.id), 220)
    return () => window.clearTimeout(timer)
  }, [toast.leaving, toast.id, onRemove])

  return <div className={`toast-card is-${toast.kind}${toast.leaving ? ' is-leaving' : ''}${paused ? ' is-paused' : ''}`}
    role={toast.kind === 'error' ? 'alert' : 'status'} data-testid="toast"
    onMouseEnter={() => setPaused(true)} onMouseLeave={() => setPaused(false)} onFocus={() => setPaused(true)} onBlur={() => setPaused(false)}>
    <span className="toast-icon" aria-hidden="true"><Icon size={18} /></span>
    <div className="toast-body">
      <b>{t(title)}</b>
      <p className="toast-message">{toast.message}</p>
    </div>
    <button type="button" className="toast-close" onClick={() => onDismiss(toast.id)} aria-label={t('Close notification')}><X size={15} /></button>
    <i className="toast-progress" style={{ animationDuration: `${TOAST_MS}ms` }} aria-hidden="true" />
  </div>
}

export function Toaster({ toasts, onDismiss, onRemove }) {
  return <div className="toast-stack" aria-live="polite" aria-relevant="additions">
    {toasts.map((toast) => <Toast key={toast.id} toast={toast} onDismiss={onDismiss} onRemove={onRemove} />)}
  </div>
}
