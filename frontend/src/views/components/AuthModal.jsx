import React, { useEffect, useMemo, useRef, useState } from 'react'
import { ArrowRight, BusFront, Check, ChevronDown, CircleAlert, Eye, EyeOff, Home, KeyRound, Lock, Mail, ShieldCheck, UserRound, Users, X } from 'lucide-react'
import { ForgotPasswordPanel, RESET_STORAGE_KEY } from '../pages/Support'
import { API_BASE, apiRequest as request, isTokenExpired } from '../../models/api'
import { t } from '../../models/i18n'
import { lastSession } from '../../models/session'
import { errorText } from '../../models/messages'
import { GENDERS, signupProblem } from '../../models/user'

// Label, icon-led input box and an optional hint line, shared by every field in the sign-in / sign-up form.
export function AuthField({ label, icon: Icon, hint, tone, children, action }) {
  return <label className={`auth-field${tone ? ` is-${tone}` : ''}`}>
    <span className="auth-label">{label}</span>
    <span className="auth-input">{Icon && <Icon size={17} aria-hidden="true" />}{children}{action}</span>
    {hint && <span className="auth-hint">{hint}</span>}
  </label>
}

// A styled drop-down list in place of the browser's own <select>, so it matches the form.
// options: [{ value, label, icon? }]. Keyboard: arrows/Home/End move, Enter/Space picks, Escape/Tab close.
export function SelectMenu({ value, options, onChange, labelledBy, icon: Icon }) {
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  const root = useRef(null)
  const list = useRef(null)
  const listId = useRef(`select-${Math.random().toString(36).slice(2, 9)}`).current
  const selectedIndex = Math.max(0, options.findIndex((option) => option.value === value))
  const selected = options[selectedIndex]
  const openMenu = () => { setActive(selectedIndex); setOpen(true) }
  const choose = (index) => { onChange(options[index].value); setOpen(false); root.current?.querySelector('button')?.focus() }
  // Close when clicking anywhere outside.
  useEffect(() => {
    if (!open) return
    const onDown = (event) => { if (!root.current?.contains(event.target)) setOpen(false) }
    document.addEventListener('mousedown', onDown)
    return () => document.removeEventListener('mousedown', onDown)
  }, [open])
  // Keep the highlighted option in view.
  useEffect(() => { if (open) list.current?.children[active]?.scrollIntoView?.({ block: 'nearest' }) }, [open, active])
  const onKeyDown = (event) => {
    const last = options.length - 1
    const moves = { ArrowDown: Math.min(active + 1, last), ArrowUp: Math.max(active - 1, 0), Home: 0, End: last }
    if (!open) {
      if (['ArrowDown', 'ArrowUp', 'Enter', ' '].includes(event.key)) { event.preventDefault(); openMenu() }
      return
    }
    if (event.key in moves) { event.preventDefault(); setActive(moves[event.key]) }
    else if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); choose(active) }
    // Escape closes only the list, not the dialog around it.
    else if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); setOpen(false) }
    else if (event.key === 'Tab') setOpen(false)
  }
  return <div className={`select-menu${open ? ' is-open' : ''}`} ref={root}>
    <button type="button" className="auth-input select-menu-button" aria-haspopup="listbox" aria-expanded={open} aria-controls={listId}
      aria-labelledby={labelledBy} aria-activedescendant={open ? `${listId}-${active}` : undefined}
      onClick={() => open ? setOpen(false) : openMenu()} onKeyDown={onKeyDown}>
      {Icon && <Icon size={17} aria-hidden="true" />}
      <span className="select-menu-value">{selected?.label}</span>
      <ChevronDown size={16} className="select-menu-arrow" aria-hidden="true" />
    </button>
    {open && <ul className="select-menu-list" role="listbox" id={listId} ref={list} aria-labelledby={labelledBy}>
      {options.map((option, index) => {
        const OptionIcon = option.icon
        return <li key={option.value} id={`${listId}-${index}`} role="option" aria-selected={index === selectedIndex}
          className={`${index === active ? 'is-active' : ''}${index === selectedIndex ? ' is-selected' : ''}`}
          onMouseEnter={() => setActive(index)} onMouseDown={(event) => event.preventDefault()} onClick={() => choose(index)}>
          {OptionIcon && <span className="select-menu-icon"><OptionIcon size={15} /></span>}
          <span>{option.label}</span>
          {index === selectedIndex && <Check size={16} className="select-menu-check" aria-hidden="true" />}
        </li>
      })}
    </ul>}
  </div>
}

// The sign-in tabs: each account signs in only through its own role.
const SIGN_IN_ROLES = [
  { value: 'commuter', label: 'Commuter', icon: Users },
  { value: 'driver', label: 'Driver', icon: BusFront },
  { value: 'admin', label: 'Admin', icon: ShieldCheck },
]
const WRONG_ROLE = {
  commuter: 'This is a commuter account. Choose Commuter to sign in.',
  driver: 'This is a driver account. Choose Driver to sign in.',
  admin: 'This is an admin account. Choose Admin to sign in.',
}

export function AuthModal({ mode, onMode, onClose, onSuccess, onContinue, onRegister, onRegisterDriver }) {
  const [form, setForm] = useState({ name: '', email: '', password: '', confirmPassword: '', gender: 'Prefer not to say', role: 'commuter' })
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const [showPassword, setShowPassword] = useState(false)
  const [forgot, setForgot] = useState(false)
  // A user's "forgot password" form replaces the sign-in form; admins only get a note.
  const userForgot = forgot && mode === 'login'
  // A request sent earlier from this browser that the user may want to check on.
  const waitingRequest = useMemo(() => { try { return Boolean(localStorage.getItem(RESET_STORAGE_KEY)) } catch { return false } }, [forgot])
  // The most recent sign-in from another tab, offered with one click. Nothing is shared unless it is chosen.
  const lastAccount = useMemo(() => { const last = lastSession(); return last && !isTokenExpired(last.token) ? last.user : null }, [])
  const signup = mode === 'signup'
  const admin = mode === 'admin'
  // The role chosen on the sign-in form. An account can only sign in through the role it registered with
  // (the server refuses anything else): commuters as Commuter, drivers as Driver, admins as Admin.
  const [loginRole, setLoginRole] = useState('commuter')
  const signInRole = admin ? 'admin' : loginRole
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
      const role = signup ? form.role : signInRole
      if (!data?.token) data = await request('/auth/login', { method: 'POST', body: JSON.stringify({ email, password: form.password, role }) })
      // The server already refuses the wrong role; this also covers an older server that ignores it, by ending
      // the session it just opened.
      if (data.user?.role && data.user.role.toLowerCase() !== role) {
        request('/auth/logout', { token: data.token, method: 'POST', ignoreUnauthorized: true }).catch(() => {})
        setError(t(WRONG_ROLE[data.user.role.toLowerCase()] || WRONG_ROLE.commuter))
        return
      }
      // Signing in with a temporary password: pass it on so the new-password form does not ask for it again.
      onSuccess(data.user, data.token, { admin, password: data.user?.mustChangePassword ? form.password : '' })
    } catch (e) { setError(errorText(e)) } finally { setLoading(false) }
  }
  const changeMode = (next) => { setError(''); setShowPassword(false); setForgot(false); onMode(next) }
  const chooseRole = (role) => { if (role === 'admin') { changeMode('admin'); return } setLoginRole(role); changeMode('login') }
  // "Create one" opens the account-type choice (commuter or driver) when the app provides it.
  const switchMode = () => (!signup && onRegister ? onRegister() : changeMode(signup ? 'login' : 'signup'))
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
          <h2 id="auth-title">{signup ? t('Create your commuter account') : userForgot ? t('Reset your password') : admin ? t('Admin sign in') : t('Welcome back')}</h2>
          <p>{signup ? t('All fields are required.') : userForgot ? t('The admin will send you a temporary password.') : admin ? t('Sign in with an admin account to manage users.') : loginRole === 'driver' ? t('Sign in with your driver account.') : t('Sign in with your commuter account.')}</p>
        </div>
      </header>

      {userForgot ? <ForgotPasswordPanel initialEmail={form.email.trim()} onBack={() => setForgot(false)}
        onUsePassword={(email, password) => { setForm((current) => ({ ...current, email, password })); setShowPassword(false); setError(''); setForgot(false) }} /> : <>
      {!signup && <div className="auth-tabs" role="tablist" aria-label={t('Sign in as')}>
        {SIGN_IN_ROLES.map(({ value, label, icon: Icon }) => <button key={value} type="button" role="tab" aria-selected={signInRole === value}
          className={signInRole === value ? 'selected' : ''} onClick={() => chooseRole(value)}><Icon size={16} /> {t(label)}</button>)}
      </div>}

      {!signup && lastAccount && onContinue && (lastAccount.role || 'commuter').toLowerCase() === signInRole && <>
        <button type="button" className="auth-continue" onClick={onContinue}>
          <span className={`avatar${lastAccount.role === 'admin' ? ' is-admin' : ''}`}>{lastAccount.role === 'admin' ? <ShieldCheck size={16} /> : (lastAccount.name?.[0] || 'U').toUpperCase()}</span>
          <span><b>{t('Continue as {name}', { name: lastAccount.name || lastAccount.email })}</b><small>{lastAccount.email} · {t(lastAccount.role || 'commuter')}</small></span>
          <ArrowRight size={16} />
        </button>
        <div className="auth-or"><span>{t('or sign in with another account')}</span></div>
      </>}
      {!signup && !admin && waitingRequest && <button type="button" className="auth-waiting" onClick={() => setForgot(true)}>
        <KeyRound size={16} /><span>{t('Your password request is waiting. See its status')}</span><ArrowRight size={14} /></button>}

      <form noValidate onSubmit={submit} className="auth-form">
        {signup && <>
          <AuthField label={t('Full name')} icon={UserRound}>
            <input autoComplete="name" value={form.name} onChange={set('name')} placeholder={t('e.g. Rahim Ahmed')} />
          </AuthField>
          {/* This form makes commuter accounts only; drivers register through the driver form with documents (F21). */}
          {onRegisterDriver && <button type="button" className="auth-waiting" onClick={onRegisterDriver}>
            <BusFront size={16} /><span>{t('Are you a bus driver? Register as a driver')}</span><ArrowRight size={14} /></button>}
          <div className="auth-row">
            <div className="auth-field">
              <span className="auth-label" id="auth-gender-label">{t('Gender')}</span>
              <SelectMenu labelledBy="auth-gender-label" icon={UserRound} value={form.gender} onChange={(gender) => setForm((current) => ({ ...current, gender }))}
                options={GENDERS.map((gender) => ({ value: gender, label: t(gender) }))} />
            </div>
          </div>
        </>}

        <AuthField label={t('Email address')} icon={Mail}>
          <input type="email" autoComplete="email" value={form.email} onChange={set('email')} placeholder={admin ? 'admin@smartcommute.local' : 'you@example.com'} />
        </AuthField>

        <AuthField label={t('Password')} icon={Lock} action={eye}>
          <input type={showPassword ? 'text' : 'password'} autoComplete={signup ? 'new-password' : 'current-password'} value={form.password} onChange={set('password')} placeholder={signup ? t('Create a password') : t('Enter your password')} />
        </AuthField>
        {!signup && <button type="button" className="auth-forgot" aria-expanded={admin ? forgot : undefined} onClick={() => setForgot(!forgot)}>{t('Forgot password?')}</button>}
        {admin && forgot && <div className="auth-forgot-info" role="note"><KeyRound size={17} /><span>
          <b>{t('The admin password is set on the server.')}</b> {t('Find it under Admin → Password in the backend\'s appsettings.Local.json.')}</span></div>}
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
      </>}
      <small className="auth-api">{t('Connected to')} {API_BASE}</small>
    </section>
  </div>
}
