import React, { useCallback, useEffect, useState } from 'react'
import { ArrowRight, BadgeCheck, CalendarDays, Check, CircleAlert, Copy, KeyRound, Lock, LogOut, Inbox, LayoutDashboard, Mail, Siren, Pencil, Phone, Plus, Route, ShieldCheck, Users, X } from 'lucide-react'
import { apiRequest as request } from '../../models/api'
import { formatDate, formatNumber, t } from '../../models/i18n'
import { saveSessionUser } from '../../models/session'
import { errorText } from '../../models/messages'
import { PageHeading, SignInPrompt } from '../components/common'
import { BIO_LIMIT, DHAKA_AREAS, GENDERS, PROFILE_FIELDS, profileFormOf, profileProblem } from '../../models/user'
import { ADMIN_FOCUS_KEY } from '../../controllers/router'
import { changed } from '../../models/notifications'

export function ProfilePage({ user, token, signedIn, updateUser, signOut, openAuth, notify, navigate }) {
  const [editing, setEditing] = useState(false)
  const [form, setForm] = useState(() => profileFormOf(user))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [saved, setSaved] = useState(false)
  // The "profile updated" banner goes away by itself after a few seconds.
  useEffect(() => {
    if (!saved) return
    const timer = window.setTimeout(() => setSaved(false), 6000)
    return () => window.clearTimeout(timer)
  }, [saved])
  // Pick up fresh details (e.g. after /auth/validate) while the form is not being edited.
  useEffect(() => { if (!editing) setForm(profileFormOf(user)) }, [user, editing])
  const set = (key) => (event) => setForm({ ...form, [key]: event.target.value })
  const changed = PROFILE_FIELDS.some((key) => form[key].trim() !== profileFormOf(user)[key].trim())
  const cancel = () => { setEditing(false); setError(''); setForm(profileFormOf(user)) }
  const save = async (event) => {
    event.preventDefault()
    const problem = profileProblem(form)
    if (problem) { setError(problem); return }
    setSaving(true); setError('')
    try {
      const body = Object.fromEntries(PROFILE_FIELDS.map((key) => [key, form[key].trim()]))
      const data = await request('/auth/profile', { token, method: 'PUT', body: JSON.stringify(body) })
      const nextUser = { ...user, ...(data.user || body) }
      saveSessionUser(nextUser); updateUser(nextUser)
      setEditing(false); setSaved(true); notify(t('Your profile has been updated.'))
    } catch (e) { setError(errorText(e)) } finally { setSaving(false) }
  }
  const copyCode = async () => {
    try { await navigator.clipboard.writeText(user.friendCode); notify(t('Friend code copied.')) } catch { notify(t('Your friend code is {code}', { code: user.friendCode })) }
  }
  if (!signedIn) return <div className="page inner-page"><PageHeading eyebrow={t('YOUR ACCOUNT, YOUR WAY')} title={t('Your profile.')} description={t('Your details stay with you wherever you go.')} icon={Users} /><SignInPrompt openAuth={openAuth} message={t('Sign in to manage your profile.')} /></div>
  if (user.role?.toLowerCase() === 'admin') return <AdminProfilePage user={user} token={token} signOut={signOut} notify={notify} navigate={navigate} />

  const initials = (user.name || 'U').split(' ').filter(Boolean).slice(0, 2).map((part) => part[0].toUpperCase()).join('')
  const filled = PROFILE_FIELDS.filter((key) => key === 'gender' ? user.gender && user.gender !== 'Prefer not to say' : user[key]).length
  const completeness = Math.round((filled / PROFILE_FIELDS.length) * 100)
  const memberSince = formatDate(user.createdAt, { month: 'long', year: 'numeric' })
  const details = [
    ['Full name', user.name], ['Email address', user.email], ['Phone number', user.phone], ['Gender', user.gender && t(user.gender)],
    ['Home area', user.homeArea && t(user.homeArea)], ['Work or study area', user.workArea && t(user.workArea)],
  ]
  // An empty detail is a dashed "+ Not added" chip, so it never reads like a real value; it opens the edit form.
  const notAdded = <button type="button" className="pf-empty" title={t('Click to add')} onClick={() => { setSaved(false); setEditing(true) }}><Plus size={13} /> {t('Not added')}</button>

  return <div className="page inner-page profile-page">
    <PageHeading eyebrow={t('YOUR ACCOUNT, YOUR WAY')} title={t('Your profile.')} description={t('Keep your details up to date so riders and friends know who they are travelling with.')} icon={Users} />
    <div className="pf-layout">
      <aside className="pf-card">
        <div className="pf-cover" />
        <span className="pf-avatar">{initials}</span>
        <b className="pf-name">{user.name}</b>
        <span className="pf-email">{user.email}</span>
        <div className="pf-chips"><span className="pf-chip">{t(user.role || 'commuter')}</span>{user.pass && <span className="pf-chip verified"><BadgeCheck size={13} /> {t('Verified student')}</span>}{user.identityVerified && <span className="pf-chip verified"><ShieldCheck size={13} /> {t('NID verified')}</span>}</div>
        {!(user.pass && user.identityVerified) && navigate && <button type="button" className="link-button pf-verify-link" onClick={() => navigate('Verification')}>{t('Verify your NID or student pass')}</button>}
        {user.bio && <p className="pf-bio">{user.bio}</p>}
        <ul className="pf-facts">
          {(user.homeArea || user.workArea) && <li><Route size={15} /><span>{user.homeArea ? t(user.homeArea) : '—'} <ArrowRight size={12} /> {user.workArea ? t(user.workArea) : '—'}</span></li>}
          {user.phone && <li><Phone size={15} /><span>{user.phone}</span></li>}
          {memberSince && <li><CalendarDays size={15} /><span>{t('Member since {date}', { date: memberSince })}</span></li>}
        </ul>
        {user.friendCode && <button className="pf-code" onClick={copyCode} title={t('Copy friend code')}><span><small>{t('Friend code')}</small><b>{user.friendCode}</b></span><Copy size={15} /></button>}
        <div className="pf-progress">
          <div><span>{t('Profile completeness')}</span><b>{formatNumber(completeness)}%</b></div>
          <span className="pf-bar"><i style={{ width: `${completeness}%` }} /></span>
          {completeness < 100 && !editing && <button onClick={() => setEditing(true)}>{t('Complete your profile')} <ArrowRight size={13} /></button>}
        </div>
        <button className="pf-signout" onClick={() => signOut()}><LogOut size={16} /> {t('Sign out')}</button>
      </aside>

      <div className="pf-main">
        <section className="pf-panel">
          <header className="pf-panel-head">
            <div><h2>{t('Personal information')}</h2><p>{editing ? t('Update your details and save when you are done.') : t('This is how you appear to other SmartCommute users.')}</p></div>
            {!editing && <button className="button button-outline button-small" onClick={() => { setSaved(false); setEditing(true) }}><Pencil size={14} /> {t('Edit profile')}</button>}
          </header>
          {saved && !editing && <div className="pf-success" role="status"><span className="pf-success-icon"><Check size={16} /></span><div><b>{t('Your profile has been updated.')}</b><small>{t('Your new details are saved and visible on your profile.')}</small></div><button onClick={() => setSaved(false)} aria-label={t('Dismiss')}><X size={16} /></button></div>}
          {editing ? <form className="pf-form" noValidate onSubmit={save}>
            <label className="pf-field"><span>{t('Full name')}</span><input autoComplete="name" value={form.name} onChange={set('name')} maxLength={100} /></label>
            <label className="pf-field"><span>{t('Email address')}</span><input type="email" autoComplete="email" value={form.email} onChange={set('email')} maxLength={255} /></label>
            <label className="pf-field"><span>{t('Phone number')} <em>{t('Optional')}</em></span><input type="tel" autoComplete="tel" value={form.phone} onChange={set('phone')} placeholder={t('+880 1XXX-XXXXXX')} maxLength={20} /></label>
            <label className="pf-field"><span>{t('Gender')}</span><select value={form.gender} onChange={set('gender')}>{GENDERS.map((gender) => <option key={gender} value={gender}>{t(gender)}</option>)}</select></label>
            <label className="pf-field"><span>{t('Home area')} <em>{t('Optional')}</em></span><input list="pf-areas" value={form.homeArea} onChange={set('homeArea')} placeholder={t('e.g. Uttara')} maxLength={100} /></label>
            <label className="pf-field"><span>{t('Work or study area')} <em>{t('Optional')}</em></span><input list="pf-areas" value={form.workArea} onChange={set('workArea')} placeholder={t('e.g. Motijheel')} maxLength={100} /></label>
            <label className="pf-field pf-wide"><span>{t('Bio')} <em>{formatNumber(form.bio.length)}/{formatNumber(BIO_LIMIT)}</em></span><textarea rows={3} value={form.bio} onChange={set('bio')} maxLength={BIO_LIMIT} placeholder={t('A line about you and how you usually commute.')} /></label>
            <datalist id="pf-areas">{DHAKA_AREAS.map((area) => <option key={area} value={area} />)}</datalist>
            {error && <div className="auth-error pf-wide" role="alert"><CircleAlert size={17} /><span>{error}</span></div>}
            <div className="pf-actions pf-wide">
              <button type="button" className="button button-outline" onClick={cancel} disabled={saving}>{t('Cancel')}</button>
              <button type="submit" className="button button-green" disabled={saving || !changed}>{saving ? t('Saving…') : <><Check size={15} /> {t('Save changes')}</>}</button>
            </div>
          </form> : <dl className="pf-details">
            {details.map(([label, value]) => <div key={label}><dt>{t(label)}</dt><dd>{value || notAdded}</dd></div>)}
            <div className="pf-wide"><dt>{t('Bio')}</dt><dd>{user.bio || notAdded}</dd></div>
          </dl>}
        </section>
        <PasswordPanel token={token} notify={notify} />
      </div>
    </div>
  </div>
}

// An admin's profile is their control panel: account details, what is waiting for them, and shortcuts to the
// admin tools. The commuter-only parts (friend code, verification, home and work area) do not apply to admins.
export function AdminProfilePage({ user, token, signOut, notify, navigate }) {
  const [overview, setOverview] = useState({ data: null, status: 'loading' })
  const load = useCallback(async () => {
    try { const result = await request('/admin/overview', { token }); setOverview({ data: result.data, status: 'ready' }) } catch { setOverview((current) => ({ ...current, status: 'error' })) }
  }, [token])
  useEffect(() => {
    load()
    const timer = window.setInterval(load, 30000)
    return () => window.clearInterval(timer)
  }, [load])
  const open = (section) => { try { sessionStorage.setItem(ADMIN_FOCUS_KEY, section) } catch { /* scrolls to the top instead */ } navigate('Admin') }
  const data = overview.data
  const count = (value) => data ? formatNumber(value) : '–'
  const initials = (user.name || 'A').split(' ').filter(Boolean).slice(0, 2).map((part) => part[0].toUpperCase()).join('')
  const waiting = data ? data.pendingPasswordRequests + data.unreadMessages + data.pendingVerifications : 0
  const tasks = [
    { section: 'sp-inbox-title', icon: data?.emergencyMessages ? Siren : Inbox, title: 'Support inbox', value: data?.unreadMessages, urgent: data?.emergencyMessages > 0,
      text: data?.emergencyMessages ? t('{count} emergency messages waiting', { count: data.emergencyMessages }) : t('Unread messages from users') },
    { section: 'sp-requests-title', icon: KeyRound, title: 'Forgot-password requests', value: data?.pendingPasswordRequests, text: t('Users waiting for a temporary password') },
    { section: 'admin-verifications', icon: BadgeCheck, title: 'Verifications', value: data?.pendingVerifications, text: t('Student pass and NID submissions to review') },
    { section: 'admin-users', icon: Users, title: 'User accounts', value: data?.totalUsers, neutral: true, text: t('Search users and message them') },
  ]

  return <div className="page inner-page profile-page admin-profile">
    <PageHeading eyebrow={t('ADMIN PANEL')} title={t('Admin profile.')} description={t('Your administrator account and everything that is waiting for you.')} icon={ShieldCheck} />
    <div className="pf-layout">
      <aside className="pf-card ap-card">
        <div className="pf-cover ap-cover" />
        <span className="pf-avatar ap-avatar">{initials}<i><ShieldCheck size={14} /></i></span>
        <b className="pf-name">{user.name}</b>
        <span className="pf-email">{user.email}</span>
        <div className="pf-chips"><span className="pf-chip ap-role"><ShieldCheck size={13} /> {t('Administrator')}</span></div>
        <ul className="pf-facts">
          <li><Mail size={15} /><span>{user.email}</span></li>
          {user.createdAt && <li><CalendarDays size={15} /><span>{t('Admin since {date}', { date: formatDate(user.createdAt, { month: 'long', year: 'numeric' }) })}</span></li>}
          <li><Lock size={15} /><span>{t('Full access to user accounts and support')}</span></li>
        </ul>
        <button className="button button-dark ap-open" onClick={() => navigate('Admin')}><LayoutDashboard size={16} /> {t('Open admin panel')}</button>
        <button className="pf-signout" onClick={() => signOut()}><LogOut size={16} /> {t('Sign out')}</button>
      </aside>

      <div className="pf-main">
        <section className="pf-panel">
          <header className="pf-panel-head">
            <div><h2>{t('Today at a glance')}</h2><p>{overview.status === 'error' ? t('Could not load the latest numbers.') : waiting ? t('{count} items need your attention.', { count: waiting }) : data ? t('Nothing is waiting. All caught up!') : t('Loading…')}</p></div>
            {overview.status === 'error' && <button className="button button-outline button-small" onClick={load}>{t('Try again')}</button>}
          </header>
          <div className="ap-stats">
            <div><small>{t('Total users')}</small><b>{count(data?.totalUsers)}</b><span>{data ? t('{count} new this week', { count: data.newThisWeek }) : ''}</span></div>
            <div><small>{t('Commuters')}</small><b>{count(data?.commuters)}</b></div>
            <div><small>{t('Drivers')}</small><b>{count(data?.drivers)}</b></div>
            <div><small>{t('Admins')}</small><b>{count(data?.admins)}</b></div>
          </div>
          <div className="ap-tasks">
            {tasks.map((task) => {
              const Icon = task.icon
              const active = !task.neutral && task.value > 0
              return <button key={task.section} type="button" className={`ap-task${active ? ' active' : ''}${task.urgent ? ' urgent' : ''}`} onClick={() => open(task.section)} data-testid="admin-task">
                <span className="ap-task-icon"><Icon size={19} /></span>
                <span className="ap-task-main"><b>{t(task.title)}</b><small>{task.text}</small></span>
                <span className="ap-task-count">{data ? formatNumber(task.value || 0) : '–'}</span>
                <ArrowRight size={15} className="ap-task-arrow" />
              </button>
            })}
          </div>
        </section>
        <PasswordPanel token={token} notify={notify} />
      </div>
    </div>
  </div>
}

export function PasswordPanel({ token, notify }) {
  const [form, setForm] = useState({ currentPassword: '', newPassword: '', confirmPassword: '' })
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const set = (key) => (event) => setForm({ ...form, [key]: event.target.value })
  const submit = async (event) => {
    event.preventDefault()
    const problem = !form.currentPassword ? 'Please enter your current password.'
      : form.newPassword.length < 6 ? 'New password must be at least 6 characters.'
      : !/[A-Za-z]/.test(form.newPassword) || !/\d/.test(form.newPassword) ? 'New password must contain at least one letter and one number.'
      : form.newPassword !== form.confirmPassword ? 'New passwords do not match.' : ''
    if (problem) { setError(t(problem)); return }
    setSaving(true); setError('')
    try {
      await request('/auth/password', { token, method: 'PUT', body: JSON.stringify({ currentPassword: form.currentPassword, newPassword: form.newPassword }) })
      setForm({ currentPassword: '', newPassword: '', confirmPassword: '' }); notify(t('Password updated.'))
    } catch (e) { setError(errorText(e)) } finally { setSaving(false) }
  }
  return <section className="pf-panel">
    <header className="pf-panel-head"><div><h2>{t('Password & security')}</h2><p>{t('Use at least 6 characters with a letter and a number.')}</p></div><span className="pf-panel-icon"><KeyRound size={18} /></span></header>
    <form className="pf-form" noValidate onSubmit={submit}>
      <label className="pf-field pf-wide"><span>{t('Current password')}</span><input type="password" autoComplete="current-password" value={form.currentPassword} onChange={set('currentPassword')} /></label>
      <label className="pf-field"><span>{t('New password')}</span><input type="password" autoComplete="new-password" value={form.newPassword} onChange={set('newPassword')} /></label>
      <label className="pf-field"><span>{t('Confirm new password')}</span><input type="password" autoComplete="new-password" value={form.confirmPassword} onChange={set('confirmPassword')} /></label>
      {error && <div className="auth-error pf-wide" role="alert"><CircleAlert size={17} /><span>{error}</span></div>}
      <div className="pf-actions pf-wide"><button className="button button-dark" disabled={saving}>{saving ? t('Updating…') : t('Update password')}</button></div>
    </form>
  </section>
}
