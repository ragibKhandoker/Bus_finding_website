import React, { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import { ArrowLeft, Check, CircleAlert, Clock3, Copy, Eye, EyeOff, Headset, Inbox, KeyRound, Lock, LogOut, Mail, RefreshCw, Send, ShieldAlert, ShieldCheck, Siren, UserRound, X } from 'lucide-react'
import { apiRequest } from '../../models/api'
import { formatDate, formatDateTime, formatNumber, formatTime, t } from '../../models/i18n'
import { usePersistentState } from '../../controllers/usePersistentState'
import { Heading, RequireSignIn } from './MorePages'
import '../styles/support.css'
import { POLL_MS, usePolling } from '../../controllers/usePolling'
import { ADMIN_ALERT_EVENT } from '../../controllers/useAdminAlerts'

export { POLL_MS } from '../../controllers/usePolling'
export { usePolling } from '../../controllers/usePolling'

// Forgot password (ask an admin for a temporary password), the forced password change that follows,
// and the message thread between users and admins.

export const MAX_SUPPORT_LENGTH = 2000

// The browser keeps the waiting request here; the secret is the only way to read the temporary password.
export const RESET_STORAGE_KEY = 'sc_password_request'

export const errorText = (error) => t(error?.message || 'Something went wrong. Please try again.')

export const post = (path, token, body) => apiRequest(path, { token, method: 'POST', ...(body ? { body: JSON.stringify(body) } : {}) })

export const readSaved = () => {
  try { const saved = JSON.parse(localStorage.getItem(RESET_STORAGE_KEY) || 'null'); return saved?.requestId && saved?.secret ? saved : null } catch { return null }
}

export const save = (value) => { try { value ? localStorage.setItem(RESET_STORAGE_KEY, JSON.stringify(value)) : localStorage.removeItem(RESET_STORAGE_KEY) } catch { /* not remembered */ } }

export async function copyText(text, notify) {
  try { await navigator.clipboard.writeText(text); notify?.(t('Copied.')) } catch { notify?.(t('Copy failed. Select the text and copy it manually.')) }
}

// ---- Forgot password, inside the sign-in form ----

// onUsePassword(email, password): fill the sign-in form with the temporary password.
export function ForgotPasswordPanel({ initialEmail = '', onUsePassword, onBack }) {
  const [saved, setSaved] = useState(readSaved)
  const [form, setForm] = useState({ email: initialEmail, message: '' })
  const [status, setStatus] = useState(null) // the request as the server reports it
  const [loading, setLoading] = useState(false)
  const [checking, setChecking] = useState(false)
  const [error, setError] = useState('')
  const [copied, setCopied] = useState(false)
  // The temporary password, kept only in this page's memory: it is made by the server when asked for, shown
  // here once, and never saved in the browser or the database.
  const [revealed, setRevealed] = useState(null)
  const [revealing, setRevealing] = useState(false)
  const reveal = async () => {
    setRevealing(true); setError('')
    try {
      const data = await post('/auth/forgot-password/reveal', null, { requestId: saved.requestId, secret: saved.secret })
      setRevealed({ password: data.temporaryPassword, expiresAt: data.expiresAt }); setCopied(false)
    } catch (e) { setError(errorText(e)) } finally { setRevealing(false) }
  }

  const forget = () => { save(null); setSaved(null); setStatus(null) }
  const check = useCallback(async (request = saved) => {
    if (!request) return
    setChecking(true)
    try {
      const data = await post('/auth/forgot-password/status', null, { requestId: request.requestId, secret: request.secret })
      setStatus(data.request); setError('')
      // Finished requests need nothing more from this browser.
      if (data.request.status === 'completed') { forget(); setForm((current) => ({ ...current, email: request.email || current.email })) }
    } catch (e) {
      if (/not found/i.test(e.message || '')) { forget(); setError(t('That request is no longer available. Please send a new one.')) } else setError(errorText(e))
    } finally { setChecking(false) }
  }, [saved])

  useEffect(() => { check() }, [check])
  // While waiting for the admin, look again now and then.
  useEffect(() => {
    if (!saved || (status && status.status !== 'pending')) return
    const timer = window.setInterval(() => check(), POLL_MS)
    return () => window.clearInterval(timer)
  }, [saved, status, check])

  const submit = async (event) => {
    event.preventDefault()
    const email = form.email.trim()
    if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email)) { setError(t('Please enter a valid email address.')); return }
    setLoading(true); setError('')
    try {
      const data = await post('/auth/forgot-password', null, { email, message: form.message.trim() || null })
      const next = { requestId: data.requestId, secret: data.secret, email }
      save(next); setSaved(next); setStatus(data.request)
    } catch (e) { setError(errorText(e)) } finally { setLoading(false) }
  }

  const back = <button type="button" className="sp-back" onClick={onBack}><ArrowLeft size={15} /> {t('Back to sign in')}</button>

  if (saved) {
    const state = status?.status || 'pending'
    return <div className="sp-forgot" data-testid="forgot-status">
      {back}
      {state === 'approved' || state === 'sent' ? <>
        <div className="sp-state ok"><span><KeyRound size={20} /></span><div><b>{t('The admin approved your request')}</b>
          <p>{revealed ? t('Sign in with it, then choose a new password straight away.') : t('Get your temporary password below. Only you can see it: it is not shown to the admin and is never stored.')}</p></div></div>
        {revealed ? <>
          <div className="sp-temp"><code data-testid="temporary-password">{revealed.password}</code>
            <button type="button" className="button button-dark button-small" onClick={async () => { await copyText(revealed.password); setCopied(true) }}>{copied ? <Check size={14} /> : <Copy size={14} />} {copied ? t('Copied') : t('Copy')}</button></div>
          <p className="sp-muted"><Lock size={14} /> {t('Shown only this once. Getting a new one replaces it.')}{revealed.expiresAt ? ` ${t('Works until {time}', { time: formatDateTime(revealed.expiresAt) })}` : ''}</p>
          <button type="button" className="button button-green auth-submit" onClick={() => onUsePassword(saved.email || status.email, revealed.password)}>{t('Sign in with this password')}</button>
        </> : <button type="button" className="button button-green auth-submit" onClick={reveal} disabled={revealing}><KeyRound size={15} /> {revealing ? t('Please wait…') : t('Show my temporary password')}</button>}
      </> : state === 'dismissed' ? <>
        <div className="sp-state bad"><span><CircleAlert size={20} /></span><div><b>{t('The admin could not reset your password')}</b><p>{status?.adminNote ? t('Reason: {reason}', { reason: status.adminNote }) : t('Please send a new request, or contact the admin another way.')}</p></div></div>
        <button type="button" className="button button-green auth-submit" onClick={forget}>{t('Send a new request')}</button>
      </> : state === 'expired' ? <>
        <div className="sp-state bad"><span><Clock3 size={20} /></span><div><b>{t('The temporary password has expired')}</b><p>{t('Send a new request and the admin will give you a new one.')}</p></div></div>
        <button type="button" className="button button-green auth-submit" onClick={forget}>{t('Send a new request')}</button>
      </> : <>
        <div className="sp-state wait"><span className="sp-pulse"><Clock3 size={20} /></span><div><b>{t('Request sent to the admin')}</b><p>{t('An admin will send you a temporary password. This page checks for it on its own — you can also close it and come back later.')}</p></div></div>
        {(saved.email || status?.email) && <p className="sp-muted"><Mail size={14} /> {saved.email || status?.email}{status?.createdAt ? ` · ${formatDateTime(status.createdAt)}` : ''}</p>}
        <div className="sp-row">
          <button type="button" className="button button-outline" onClick={() => check()} disabled={checking}><RefreshCw size={15} className={checking ? 'sp-spin' : ''} /> {checking ? t('Checking…') : t('Check now')}</button>
          <button type="button" className="sp-link" onClick={forget}>{t('Cancel this request')}</button>
        </div>
      </>}
      {error && <div className="auth-error" role="alert"><CircleAlert size={17} /><span>{error}</span></div>}
    </div>
  }

  return <form className="sp-forgot" onSubmit={submit} noValidate>
    {back}
    <div className="sp-intro"><KeyRound size={18} /><p>{t('Forgot your password? Ask the admin. They will send you a temporary password, and you will choose a new one after signing in.')}</p></div>
    <label className="auth-field"><span className="auth-label">{t('Email address')}</span>
      <span className="auth-input"><Mail size={17} aria-hidden="true" /><input type="email" autoComplete="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} placeholder="you@example.com" /></span></label>
    <label className="auth-field"><span className="auth-label">{t('Note for the admin (optional)')}</span>
      <textarea className="sp-textarea" rows={3} maxLength={500} value={form.message} onChange={(e) => setForm({ ...form, message: e.target.value })} placeholder={t('e.g. I cannot remember my password since changing phones.')} /></label>
    {error && <div className="auth-error" role="alert"><CircleAlert size={17} /><span>{error}</span></div>}
    <button className="button button-green auth-submit" disabled={loading}>{loading ? t('Sending…') : t('Send request to admin')} {!loading && <Send size={15} />}</button>
  </form>
}

// ---- After signing in with a temporary password: choose a new one before anything else ----

export function ForcePasswordModal({ token, user, initialPassword = '', onDone, onSignOut }) {
  const [form, setForm] = useState({ current: initialPassword, next: '', confirm: '' })
  const [show, setShow] = useState(false)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const rules = [
    { ok: form.next.length >= 6, text: 'At least 6 characters' },
    { ok: /[A-Za-z]/.test(form.next), text: 'At least one letter' },
    { ok: /\d/.test(form.next), text: 'At least one number' },
    { ok: Boolean(form.next) && form.next !== form.current, text: 'Different from the temporary password' },
  ]
  const submit = async (event) => {
    event.preventDefault()
    if (!form.current) { setError(t('Please enter the temporary password.')); return }
    if (!rules.every((rule) => rule.ok)) { setError(t('Please choose a password that meets every rule.')); return }
    if (form.next !== form.confirm) { setError(t('Passwords do not match')); return }
    setLoading(true); setError('')
    try {
      const data = await apiRequest('/auth/password', { token, method: 'PUT', body: JSON.stringify({ currentPassword: form.current, newPassword: form.next }) })
      onDone(data.user || { ...user, mustChangePassword: false })
    } catch (e) { setError(errorText(e)) } finally { setLoading(false) }
  }
  const type = show ? 'text' : 'password'
  const eye = <button type="button" className="auth-eye" onClick={() => setShow(!show)} aria-label={show ? t('Hide password') : t('Show password')}>{show ? <EyeOff size={17} /> : <Eye size={17} />}</button>
  return <div className="modal-backdrop sp-force-backdrop">
    <section className="auth-modal panel sp-force" role="dialog" aria-modal="true" aria-labelledby="sp-force-title">
      <header className="auth-header"><span className="auth-mark sp-force-mark"><ShieldCheck size={22} /></span>
        <div><h2 id="sp-force-title">{t('Choose a new password')}</h2><p>{t('You signed in with a temporary password from the admin. Set your own password to continue.')}</p></div></header>
      <form className="auth-form" onSubmit={submit} noValidate>
        {!initialPassword && <label className="auth-field"><span className="auth-label">{t('Temporary password')}</span>
          <span className="auth-input"><KeyRound size={17} aria-hidden="true" /><input type={type} autoComplete="current-password" value={form.current} onChange={(e) => setForm({ ...form, current: e.target.value })} />{eye}</span></label>}
        <label className="auth-field"><span className="auth-label">{t('New password')}</span>
          <span className="auth-input"><Lock size={17} aria-hidden="true" /><input type={type} autoComplete="new-password" value={form.next} onChange={(e) => setForm({ ...form, next: e.target.value })} placeholder={t('Create a password')} />{initialPassword && eye}</span></label>
        <ul className="auth-rules" aria-label={t('Password requirements')}>
          {rules.map((rule) => <li key={rule.text} className={rule.ok ? 'ok' : ''}>{rule.ok ? <Check size={14} /> : <span className="auth-rule-dot" />}{t(rule.text)}</li>)}
        </ul>
        <label className="auth-field"><span className="auth-label">{t('Confirm password')}</span>
          <span className="auth-input"><Lock size={17} aria-hidden="true" /><input type={type} autoComplete="new-password" value={form.confirm} onChange={(e) => setForm({ ...form, confirm: e.target.value })} placeholder={t('Type the same password again')} /></span></label>
        {error && <div className="auth-error" role="alert"><CircleAlert size={17} /><span>{error}</span></div>}
        <button className="button button-green auth-submit" disabled={loading}>{loading ? t('Please wait…') : t('Save new password')}</button>
      </form>
      <div className="auth-switch">{t('Signed in as {email}.', { email: user?.email || '' })} <button type="button" onClick={onSignOut}><LogOut size={13} /> {t('Sign out')}</button></div>
    </section>
  </div>
}

// ---- The conversation itself, shared by users and admins ----

// draftKey: names the unsent message kept when leaving the page (one per conversation).
export function Conversation({ messages, viewerIsAdmin, status, onRetry, onSend, otherName, placeholder, draftKey = null }) {
  const [text, setText] = usePersistentState(draftKey, '')
  const [emergency, setEmergency] = useState(false)
  const [sending, setSending] = useState(false)
  const [error, setError] = useState('')
  const scroller = useRef(null)
  useLayoutEffect(() => { if (scroller.current) scroller.current.scrollTop = scroller.current.scrollHeight }, [messages.length])
  const send = async (event) => {
    event.preventDefault()
    const clean = text.trim()
    if (!clean) { setError(t('Please write a message.')); return }
    setSending(true); setError('')
    try { await onSend(clean, emergency); setText(''); setEmergency(false) } catch (e) { setError(errorText(e)) } finally { setSending(false) }
  }
  let lastDay = ''
  return <div className="sp-conversation">
    <div className="sp-messages" ref={scroller} role="log" aria-live="polite" aria-label={t('Messages with {name}', { name: otherName })}>
      {status === 'loading' && !messages.length ? <div className="sp-empty"><span className="spinner" /></div>
        : status === 'error' ? <div className="sp-empty"><p>{t('Could not load messages.')}</p><button type="button" className="button button-outline button-small" onClick={onRetry}><RefreshCw size={14} /> {t('Try again')}</button></div>
          : !messages.length ? <div className="sp-empty"><Headset size={26} /><p>{placeholder}</p></div>
            : messages.map((message) => {
              const mine = message.fromAdmin === viewerIsAdmin
              const day = formatDate(message.createdAt)
              const divider = day !== lastDay ? <div className="sp-day" key={`day-${message._id}`}><span>{day}</span></div> : null
              lastDay = day
              return <React.Fragment key={message._id}>{divider}
                <div className={`sp-bubble${mine ? ' mine' : ''}${message.emergency ? ' emergency' : ''}`} data-testid="support-message">
                  {message.emergency && <span className="sp-tag"><Siren size={12} /> {t('Emergency')}</span>}
                  {!mine && <b className="sp-author">{message.fromAdmin ? t('Admin') : message.senderName}</b>}
                  <p>{message.text}</p>
                  <time>{formatTime(message.createdAt)}{mine && message.readAt ? <> · <Check size={11} /> {t('Read')}</> : ''}</time>
                </div>
              </React.Fragment>
            })}
    </div>
    <form className={`sp-compose${emergency ? ' is-emergency' : ''}`} onSubmit={send}>
      <label className="sr-only" htmlFor="sp-text">{t('Write a message…')}</label>
      <textarea id="sp-text" rows={2} maxLength={MAX_SUPPORT_LENGTH} value={text} onChange={(e) => setText(e.target.value)} placeholder={t('Write a message…')}
        onKeyDown={(e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); send(e) } }} />
      <div className="sp-compose-bar">
        <label className={`sp-emergency${emergency ? ' on' : ''}`}><input type="checkbox" checked={emergency} onChange={(e) => setEmergency(e.target.checked)} /><Siren size={15} /> {t('Mark as emergency')}</label>
        <span className="sp-count">{formatNumber(text.length)}/{formatNumber(MAX_SUPPORT_LENGTH)}</span>
        <button className={`button ${emergency ? 'sp-send-emergency' : 'button-green'} button-small`} disabled={sending}>{sending ? t('Sending…') : emergency ? t('Send emergency') : t('Send')} {!sending && <Send size={14} />}</button>
      </div>
      {error && <div className="auth-error" role="alert"><CircleAlert size={17} /><span>{error}</span></div>}
    </form>
  </div>
}

export function SupportPage({ token, user, signedIn, openAuth, navigate }) {
  const isAdmin = signedIn && user?.role?.toLowerCase() === 'admin'
  const [data, status, reload, setData] = usePolling(async () => {
    if (!signedIn || isAdmin) return { messages: [] }
    const result = await apiRequest('/support', { token })
    if (result.unread) post('/support/read', token).catch(() => {})
    return result
  }, [token, signedIn, isAdmin])
  const heading = <Heading icon={Headset} eyebrow={t('HELP & SUPPORT')} title={t('Contact admin')} subtitle={t('Message the SmartCommute admins. Mark it as an emergency if you need help right away.')} />
  if (!signedIn) return <section className="extra-page">{heading}<RequireSignIn openAuth={openAuth} /></section>
  if (isAdmin) return <section className="extra-page">{heading}<div className="sp-note"><Inbox size={18} /><span>{t('Admins answer users from the Support inbox on the Admin page.')}</span><button type="button" className="button button-dark button-small" onClick={() => navigate('Admin')}>{t('Open Admin')}</button></div></section>
  const messages = data?.messages || []
  const send = async (text, emergency) => {
    const result = await post('/support/messages', token, { text, emergency })
    setData({ ...(data || {}), messages: [...messages, result.message] })
  }
  return <section className="extra-page sp-page">
    {heading}
    <div className="sp-note emergency"><ShieldAlert size={18} /><span>{t('In danger right now? Call 999 first. Emergency messages here reach the admins quickly but are not an emergency service.')}</span></div>
    <div className="panel sp-panel">
      <header className="sp-head"><span className="sp-avatar admin"><ShieldCheck size={18} /></span><div><b>{t('SmartCommute admin')}</b><small>{t('Usually replies within a day')}</small></div></header>
      <Conversation messages={messages} viewerIsAdmin={false} status={status} onRetry={() => reload()} onSend={send} otherName={t('Admin')} draftKey="support:draft"
        placeholder={t('No messages yet. Ask the admin anything about your account, trips or payments.')} />
    </div>
  </section>
}

// ---- Admins: forgot-password requests ----

export function PasswordRequestsCard({ token, notify }) {
  const [filter, setFilter] = useState('pending')
  const [state, setState] = useState({ items: [], pending: 0, status: 'loading', error: '' })
  // { request, stage: 'confirm' | 'working' | 'done' | 'dismiss', password, reason }
  const [dialog, setDialog] = useState(null)
  const load = useCallback(async () => {
    try {
      const data = await apiRequest(`/admin/password-requests?status=${filter}`, { token })
      setState({ items: data.data || [], pending: data.pending || 0, status: 'ready', error: '' })
    } catch (e) { setState((current) => ({ ...current, status: 'error', error: errorText(e) })) }
  }, [token, filter])
  useEffect(() => {
    load()
    const timer = window.setInterval(load, POLL_MS)
    // A new request was just noticed (useAdminAlerts): show it now instead of at the next check.
    window.addEventListener(ADMIN_ALERT_EVENT, load)
    return () => { window.clearInterval(timer); window.removeEventListener(ADMIN_ALERT_EVENT, load) }
  }, [load])

  const send = async () => {
    setDialog({ ...dialog, stage: 'working' })
    try {
      await post(`/admin/password-requests/${dialog.request._id}/approve`, token)
      setDialog({ request: dialog.request, stage: 'done' })
      load()
    } catch (e) { setDialog(null); notify(errorText(e)); load() }
  }
  const dismiss = async () => {
    const { request, reason } = dialog
    setDialog({ ...dialog, stage: 'working' })
    try { await post(`/admin/password-requests/${request._id}/dismiss`, token, { reason: reason?.trim() || null }); setDialog(null); notify(t('Request dismissed.')); load() } catch (e) { setDialog(null); notify(errorText(e)) }
  }
  const STATUS_LABELS = { pending: 'Waiting', approved: 'Approved', sent: 'Approved', expired: 'Expired', completed: 'Completed', dismissed: 'Dismissed' }

  return <section className="pf-panel sp-admin-card" aria-labelledby="sp-requests-title">
    <header className="ad-toolbar">
      <div><h2 id="sp-requests-title"><KeyRound size={18} /> {t('Forgot-password requests')} {state.pending > 0 && <span className="ad-count sp-alert-count">{formatNumber(state.pending)}</span>}</h2>
        <p>{t('Approve a request to let the user get a temporary password on the sign-in form. You never see their password, and it is stored only as a bcrypt hash.')}</p></div>
      <div className="sp-filter" role="tablist" aria-label={t('Show')}>{[['pending', 'Waiting'], ['all', 'All']].map(([value, label]) =>
        <button key={value} type="button" role="tab" aria-selected={filter === value} className={filter === value ? 'selected' : ''} onClick={() => setFilter(value)}>{t(label)}</button>)}</div>
    </header>
    {state.status === 'error' && <div className="auth-error" role="alert"><CircleAlert size={17} /><span>{state.error}</span></div>}
    {state.status === 'loading' ? <div className="empty-state"><span className="spinner" /></div>
      : !state.items.length ? <div className="empty-state sp-small-empty"><span className="empty-icon"><Check size={20} /></span><b>{filter === 'pending' ? t('No one is waiting') : t('No requests yet')}</b></div>
        : <ul className="sp-requests">{state.items.map((item) => <li key={item._id} className={`sp-request ${item.status}`} data-testid="password-request">
          <span className="ad-avatar">{item.user?.name?.[0]?.toUpperCase() || 'U'}</span>
          <div className="sp-request-main">
            <b>{item.user?.name || t('Deleted account')}</b><small>{item.user?.email}</small>
            {item.note && <p className="sp-request-note">“{item.note}”</p>}
            <span className="sp-request-meta"><Clock3 size={12} /> {formatDateTime(item.createdAt)}{item.status !== 'pending' && <> · <span className={`sp-status ${item.status}`}>{t(STATUS_LABELS[item.status] || item.status)}</span></>}{item.adminNote && <> · {item.adminNote}</>}</span>
          </div>
          {item.status === 'pending' && <div className="sp-request-actions">
            <button type="button" className="button button-green button-small" onClick={() => setDialog({ request: item, stage: 'confirm' })}><Check size={14} /> {t('Approve')}</button>
            <button type="button" className="button button-outline button-small" onClick={() => setDialog({ request: item, stage: 'dismiss', reason: '' })}>{t('Dismiss')}</button>
          </div>}
        </li>)}</ul>}

    {dialog && <div className="modal-backdrop" onMouseDown={(event) => event.target === event.currentTarget && dialog.stage !== 'working' && setDialog(null)}>
      <section className="panel ad-dialog" role="dialog" aria-modal="true" aria-labelledby="sp-dialog-title">
        {dialog.stage === 'done' ? <>
          <span className="pf-success-icon ad-dialog-icon"><Check size={18} /></span>
          <h2 id="sp-dialog-title">{t('Request approved')}</h2>
          <p>{t('{name} can now get a temporary password from the sign-in form (Forgot password?). Only they see it; it works for 24 hours.', { name: dialog.request.user?.name || '' })}</p>
          <div className="pf-actions"><button className="button button-green" onClick={() => setDialog(null)}>{t('Done')}</button></div>
        </> : dialog.stage === 'dismiss' || (dialog.stage === 'working' && 'reason' in dialog) ? <>
          <span className="pf-panel-icon ad-dialog-icon"><X size={18} /></span>
          <h2 id="sp-dialog-title">{t('Dismiss this request?')}</h2>
          <p>{t('{name} will see that the request was turned down, with your reason.', { name: dialog.request.user?.name || '' })}</p>
          <textarea className="sp-textarea sp-reason" rows={2} maxLength={300} value={dialog.reason || ''} onChange={(e) => setDialog({ ...dialog, reason: e.target.value })} placeholder={t('Reason (optional)')} />
          <div className="pf-actions">
            <button className="button button-outline" onClick={() => setDialog(null)} disabled={dialog.stage === 'working'}>{t('Cancel')}</button>
            <button className="button button-dark" onClick={dismiss} disabled={dialog.stage === 'working'}>{dialog.stage === 'working' ? t('Please wait…') : t('Dismiss')}</button>
          </div>
        </> : <>
          <span className="pf-panel-icon ad-dialog-icon"><KeyRound size={18} /></span>
          <h2 id="sp-dialog-title">{t('Approve this request?')}</h2>
          <p>{t('{name} ({email}) will be able to get a temporary password from the sign-in form. Make sure the request really comes from them. When they collect it, their current password stops working and they are signed out everywhere.', { name: dialog.request.user?.name || '', email: dialog.request.user?.email || '' })}</p>
          <div className="pf-actions">
            <button className="button button-outline" onClick={() => setDialog(null)} disabled={dialog.stage === 'working'}>{t('Cancel')}</button>
            <button className="button button-green" onClick={send} disabled={dialog.stage === 'working'}>{dialog.stage === 'working' ? t('Please wait…') : t('Approve')}</button>
          </div>
        </>}
      </section>
    </div>}
  </section>
}

// ---- Admins: messages with users ----

// startWith: a user (from the user list) to open a conversation with, even if none exists yet.
export function AdminSupportInbox({ token, startWith, onStarted }) {
  const [selected, setSelected] = useState(null) // { _id, name, email }
  const [threads, threadsStatus, reloadThreads] = usePolling(() => apiRequest('/admin/support', { token }), [token])
  const [thread, threadStatus, reloadThread, setThread] = usePolling(async () => {
    if (!selected) return null
    const data = await apiRequest(`/admin/support/${selected._id}`, { token })
    if (data.messages?.some((m) => !m.fromAdmin && !m.readAt)) post(`/admin/support/${selected._id}/read`, token).then(() => reloadThreads(true)).catch(() => {})
    return data
  }, [token, selected?._id])
  const box = useRef(null)
  // A new message was just noticed (useAdminAlerts): reload the list and the open conversation now.
  useEffect(() => {
    const onAlert = () => { reloadThreads(true); reloadThread(true) }
    window.addEventListener(ADMIN_ALERT_EVENT, onAlert)
    return () => window.removeEventListener(ADMIN_ALERT_EVENT, onAlert)
  }, [reloadThreads, reloadThread])
  useEffect(() => {
    if (!startWith) return
    setSelected({ _id: startWith._id || startWith.id, name: startWith.name, email: startWith.email })
    onStarted?.()
    box.current?.scrollIntoView?.({ behavior: 'smooth', block: 'start' })
  }, [startWith]) // eslint-disable-line react-hooks/exhaustive-deps

  const list = threads?.data || []
  const send = async (text, emergency) => {
    const result = await post(`/admin/support/${selected._id}`, token, { text, emergency })
    setThread({ ...(thread || {}), messages: [...(thread?.messages || []), result.message] })
    reloadThreads(true)
  }
  return <section className="pf-panel sp-admin-card" ref={box} aria-labelledby="sp-inbox-title">
    <header className="ad-toolbar">
      <div><h2 id="sp-inbox-title"><Inbox size={18} /> {t('Support inbox')}
        {threads?.emergencyUnread > 0 ? <span className="ad-count sp-alert-count"><Siren size={12} /> {formatNumber(threads.emergencyUnread)}</span>
          : threads?.unread > 0 && <span className="ad-count">{formatNumber(threads.unread)}</span>}</h2>
        <p>{t('Messages from users. Emergencies are listed first. Use Message on a user below to start a new conversation.')}</p></div>
    </header>
    <div className={`sp-inbox${selected ? ' has-selection' : ''}`}>
      <aside className="sp-threads" aria-label={t('Conversations')}>
        {threadsStatus === 'loading' && !list.length ? <div className="sp-empty"><span className="spinner" /></div>
          : threadsStatus === 'error' && !list.length ? <div className="sp-empty"><p>{t('Could not load messages.')}</p><button type="button" className="button button-outline button-small" onClick={() => reloadThreads()}><RefreshCw size={14} /> {t('Try again')}</button></div>
            : !list.length && !selected ? <div className="sp-empty"><Inbox size={24} /><p>{t('No messages from users yet.')}</p></div>
              : <>
                {selected && !list.some((item) => item.user._id === selected._id) && <button type="button" className="sp-thread current"><span className="ad-avatar">{selected.name?.[0]?.toUpperCase() || 'U'}</span><span><b>{selected.name}</b><small>{t('New conversation')}</small></span></button>}
                {list.map((item) => <button key={item.user._id} type="button" data-testid="support-thread"
                  className={`sp-thread${selected?._id === item.user._id ? ' current' : ''}${item.emergencyUnread ? ' emergency' : item.unread ? ' unread' : ''}`}
                  onClick={() => setSelected({ _id: item.user._id, name: item.user.name, email: item.user.email })}>
                  <span className="ad-avatar">{item.user.name?.[0]?.toUpperCase() || 'U'}</span>
                  <span className="sp-thread-main"><span className="sp-thread-top"><b>{item.user.name}</b><time>{formatTime(item.lastMessage.createdAt)}</time></span>
                    <small>{item.lastMessage.emergency && <Siren size={11} />} {item.lastMessage.fromAdmin ? `${t('You')}: ` : ''}{item.lastMessage.text}</small></span>
                  {item.unread > 0 && <span className={`sp-badge${item.emergencyUnread ? ' emergency' : ''}`}>{formatNumber(item.unread)}</span>}
                </button>)}
              </>}
      </aside>
      <div className="sp-thread-view">
        {selected ? <>
          <header className="sp-head"><button type="button" className="icon-button sp-mobile-back" onClick={() => setSelected(null)} aria-label={t('Back')}><ArrowLeft size={18} /></button>
            <span className="sp-avatar"><UserRound size={18} /></span><div><b>{selected.name}</b><small>{selected.email}</small></div></header>
          <Conversation messages={thread?.messages || []} viewerIsAdmin status={threadStatus} onRetry={() => reloadThread()} onSend={send} otherName={selected.name} draftKey={`support:admin-draft:${selected._id}`}
            placeholder={t('No messages yet. Write the first one.')} />
        </> : <div className="sp-empty sp-pick"><Headset size={28} /><p>{t('Choose a conversation to read and reply.')}</p></div>}
      </div>
    </div>
  </section>
}
