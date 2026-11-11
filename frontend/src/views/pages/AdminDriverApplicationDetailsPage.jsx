import React, { useCallback, useEffect, useMemo, useState } from 'react'
import {
  ArrowLeft, BadgeCheck, BusFront, CalendarClock, CircleAlert, CreditCard, FileImage, FileWarning, History, IdCard, Loader2, Lock, Search,
  ShieldCheck, UserRound, XCircle, ZoomIn,
} from 'lucide-react'
import { t } from '../../models/i18n'
import { DOCUMENTS, FIELD_LABELS, STATUS_LABELS, adminDriverApi, day, dayTime } from '../../models/driver'
import { useBusRoutes } from '../../controllers/useBusRoutes'
import { VerificationStatusBadge } from '../components/driver/VerificationStatusBadge'
import { ConfirmDialog, DocumentPreviewModal, RejectApplicationModal, ResubmissionRequestModal, usePrivateImage } from '../components/driver/DriverModals'
import { SignInPrompt } from '../components/common'
import '../styles/driver.css'

function Facts({ items }) {
  return <dl className="dv-facts">{items.filter(Boolean).map(([label, value, mono]) => <div key={label}><dt>{t(label)}</dt><dd className={mono ? 'dv-mono' : ''}>{value || '—'}</dd></div>)}</dl>
}

function DocumentTile({ doc, label, token, onOpen }) {
  const image = usePrivateImage(doc?.url, token)
  if (!doc) return <div className="dv-doc missing"><span className="dv-doc-thumb"><FileImage size={22} /></span><b>{t(label)}</b><small>{t('Not uploaded')}</small></div>
  return <button type="button" className="dv-doc" onClick={onOpen} aria-label={t('Open {doc}', { doc: t(label) })}>
    <span className="dv-doc-thumb">{image.status === 'ready' ? <img src={image.src} alt="" /> : image.status === 'error' ? <CircleAlert size={20} /> : <Loader2 size={18} className="dv-spin" />}
      <i><ZoomIn size={16} /></i></span>
    <b>{t(label)}</b><small>{day(doc.uploadedAt)}</small>
  </button>
}

// The full application for an admin: every detail, the private documents, the decision buttons, the audit
// history and (once approved) the bus assignment.
export function AdminDriverApplicationDetailsPage({ driverId, token, signedIn, isAdmin, openAuth, navigate, notify }) {
  const [state, setState] = useState({ status: 'loading' })
  const [preview, setPreview] = useState(null)
  const [dialog, setDialog] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const load = useCallback(() => adminDriverApi.get(driverId, token).then((res) => setState({ status: 'ready', a: res.data }))
    .catch((e) => setState({ status: 'error', error: t(e.message || 'Could not load the application.') })), [driverId, token])
  useEffect(() => { if (isAdmin) load() }, [isAdmin, load])

  const act = async (action, ...args) => {
    setBusy(true); setError('')
    try {
      const res = await adminDriverApi[action](driverId, ...args, token)
      setState({ status: 'ready', a: res.data }); setDialog('')
      notify(t(res.message))
    } catch (e) { setError(t(e.message || 'Something went wrong. Please try again.')); setDialog('') } finally { setBusy(false) }
  }

  const back = <button type="button" className="dv-back dark" onClick={() => navigate('Driver verification')}><ArrowLeft size={15} /> {t('All driver applications')}</button>
  if (!signedIn) return <div className="page inner-page dv-page"><SignInPrompt openAuth={openAuth} message={t('Sign in with an admin account to review driver applications.')} /></div>
  if (!isAdmin) return <div className="page inner-page dv-page"><div className="dv-panel dv-empty"><span><Lock size={22} /></span><b>{t('Admins only')}</b><small>{t('Your account does not have access to this page.')}</small></div></div>
  if (state.status === 'loading') return <div className="page inner-page dv-page">{back}<div className="dv-panel dv-center"><Loader2 size={20} className="dv-spin" /> {t('Loading application…')}</div></div>
  if (state.status === 'error') return <div className="page inner-page dv-page">{back}<div className="dv-alert danger" role="alert"><CircleAlert size={18} /><span>{state.error}</span></div></div>

  const a = state.a
  const can = (s) => a.allowedActions.includes(s)
  const docOf = (type) => a.documents.find((d) => d.type === type)
  const photo = docOf('profile_photo')

  return <div className="page inner-page dv-page dv-details">
    {back}
    <header className="dv-details-head">
      <ProfilePhoto doc={photo} token={token} name={a.fullName} />
      <div>
        <small className="dv-mono">{a.applicationCode}</small>
        <h1>{a.fullName}</h1>
        <p>{a.email} · {a.phone} · {t('submitted {date}', { date: dayTime(a.submittedAt) })}</p>
      </div>
      <VerificationStatusBadge status={a.verificationStatus} large />
    </header>

    {error && <div className="dv-alert danger" role="alert"><CircleAlert size={18} /><span>{error}</span></div>}
    {a.verificationStatus === 'REJECTED' && a.rejectionReason && <div className="dv-reason danger"><b>{t('Rejection reason')}</b><p>{a.rejectionReason}</p></div>}
    {a.verificationStatus === 'RESUBMISSION_REQUIRED' && <div className="dv-reason warn"><b>{t('Waiting for the driver to correct')}</b><p>{a.resubmissionReason}</p>
      <div className="dv-tags">{a.requestedFields.map((f) => <span key={f}>{t(FIELD_LABELS[f] || f)}</span>)}</div></div>}

    <div className="dv-details-grid">
      <div className="dv-details-main">
        <section className="dv-panel"><h3><UserRound size={17} /> {t('Personal information')}</h3>
          <Facts items={[['Name', a.fullName], ['Email', a.email], ['Phone', a.phone], ['Date of birth', `${day(a.dateOfBirth)} (${t('{n} years', { n: a.age })})`], ['Gender', t(a.gender)],
            ['Address', `${a.presentAddress}, ${a.area}, ${t(a.district)}`], ['Emergency contact', a.emergencyContact]]} /></section>
        <div className="dv-two">
          <section className="dv-panel"><h3><CreditCard size={17} /> {t('Driving information')}</h3>
            <Facts items={[['Licence number', a.licenseNumberMasked, true], ['Licence type', t(a.licenseType)], ['Expiry date', day(a.licenseExpiryDate)],
              ['Driving experience', t('{n} years', { n: a.drivingExperienceYears })]]} />
            {a.licenseType === 'Other' && <p className="dv-small warn"><FileWarning size={14} /> {t('“Other” licence type: check it is valid for driving a bus.')}</p>}</section>
          <section className="dv-panel"><h3><IdCard size={17} /> {t('Identity information')}</h3>
            <Facts items={[['NID number', a.nidNumberMasked, true]]} />
            <p className="dv-small"><Lock size={14} /> {t('Numbers are stored encrypted. Compare the last digits with the document photos.')}</p></section>
        </div>
        <section className="dv-panel"><h3><FileImage size={17} /> {t('Documents')}</h3>
          <div className="dv-doc-grid">{DOCUMENTS.filter((d) => d.type !== 'profile_photo').map((d) => <DocumentTile key={d.type} doc={docOf(d.type)} label={d.label} token={token}
            onOpen={() => setPreview({ doc: docOf(d.type), label: d.label })} />)}</div>
          <p className="dv-small"><ShieldCheck size={14} /> {t('Documents are private. They are loaded with your admin sign-in and never cached.')}</p></section>
        <section className="dv-panel"><h3><BusFront size={17} /> {t('Employment information')} <em className="dv-claim">{t('claim')}</em></h3>
          <Facts items={[['Claimed bus', `${t(a.claimedBusName)}${a.claimedBusInDatabase ? ` · ${t('found in bus list')}` : ` · ${t('not in bus list')}`}`], ['Bus operator', a.claimedOperatorName],
            ['Route', a.claimedRoute], ['Registration number', a.busRegistrationNumber], ['Employment type', t(a.employmentType)]]} /></section>
      </div>

      <aside className="dv-details-side">
        <section className="dv-panel dv-decide">
          <h3><ShieldCheck size={17} /> {t('Decision')}</h3>
          {a.allowedActions.length === 0 ? <p className="dv-next">{a.verificationStatus === 'APPROVED' ? t('This driver is approved.') : a.verificationStatus === 'REJECTED' ? t('This application was rejected.') : t('No action is available right now.')}</p>
            : <div className="dv-decide-buttons">
              {can('UNDER_REVIEW') && <button type="button" className="button button-outline" disabled={busy} onClick={() => act('startReview')}><Search size={15} /> {t('Start review')}</button>}
              {can('APPROVED') && <button type="button" className="button button-green" disabled={busy} onClick={() => setDialog('approve')}><BadgeCheck size={15} /> {t('Approve driver')}</button>}
              {can('RESUBMISSION_REQUIRED') && <button type="button" className="button dv-button-warn" disabled={busy} onClick={() => setDialog('resubmit')}><FileWarning size={15} /> {t('Request resubmission')}</button>}
              {can('REJECTED') && <button type="button" className="button dv-button-danger" disabled={busy} onClick={() => setDialog('reject')}><XCircle size={15} /> {t('Reject application')}</button>}
            </div>}
          {a.verificationStatus === 'RESUBMISSION_REQUIRED' && <p className="dv-small">{t('Approval opens again once the driver resubmits.')}</p>}
        </section>
        {a.verificationStatus === 'APPROVED' && <BusAssignment a={a} token={token} notify={notify} onChanged={load} />}
        <section className="dv-panel"><h3><History size={17} /> {t('Verification history')}</h3>
          <ol className="dv-timeline">{[...a.history].reverse().map((h) => <li key={h.id} className={`to-${h.newStatus.toLowerCase().replace(/_/g, '-')}`}>
            <b>{h.previousStatus ? `${t(STATUS_LABELS[h.previousStatus])} → ` : ''}{t(STATUS_LABELS[h.newStatus] || h.newStatus)}</b>
            <small>{dayTime(h.createdAt)} · {h.byAdmin ? t('by {name}', { name: h.adminName || t('an admin') }) : t('by the driver')}</small>
            {h.reason && <p>{h.reason}</p>}
          </li>)}</ol>
        </section>
      </aside>
    </div>

    {preview && <DocumentPreviewModal document={preview.doc} label={preview.label} token={token} onClose={() => setPreview(null)} />}
    {dialog === 'approve' && <ConfirmDialog title={t('Approve this driver?')} busy={busy} confirmLabel={t('Yes, approve')}
      message={t('{name} will get access to the driver dashboard. Bus operations still need a bus assignment.', { name: a.fullName })}
      onConfirm={() => act('approve', '')} onClose={() => setDialog('')} />}
    {dialog === 'reject' && <RejectApplicationModal name={a.fullName} busy={busy} onSubmit={(reason) => act('reject', reason)} onClose={() => setDialog('')} />}
    {dialog === 'resubmit' && <ResubmissionRequestModal name={a.fullName} busy={busy} onSubmit={(reason, fields) => act('requestResubmission', reason, fields)} onClose={() => setDialog('')} />}
  </div>
}

function ProfilePhoto({ doc, token, name }) {
  const image = usePrivateImage(doc?.url, token)
  return <span className="dv-details-avatar">{doc && image.status === 'ready' ? <img src={image.src} alt={t('Profile picture of {name}', { name })} /> : (name || '?')[0].toUpperCase()}</span>
}

// Only admins assign buses, and only to approved drivers. The driver's claimed bus is offered first.
function BusAssignment({ a, token, notify, onChanged }) {
  const [routes] = useBusRoutes()
  const [search, setSearch] = useState('')
  const [busy, setBusy] = useState('')
  const [error, setError] = useState('')
  const [confirm, setConfirm] = useState(null)
  const active = a.assignments.filter((x) => x.isActive)
  const matches = useMemo(() => {
    if (routes.status !== 'ready') return []
    const q = search.trim().toLowerCase()
    const list = q.length >= 2 ? routes.buses.filter((b) => `${b.busName} ${b.routeNumber || ''}`.toLowerCase().includes(q))
      : a.claimedBusId ? routes.buses.filter((b) => (b._id || b.id) === a.claimedBusId) : []
    return list.filter((b) => !active.some((x) => x.busId === (b._id || b.id))).slice(0, 5)
  }, [routes, search, a.claimedBusId, active])
  const run = async (fn, key) => {
    setBusy(key); setError('')
    try { const res = await fn(); notify(t(res.message)); setConfirm(null); setSearch(''); onChanged() } catch (e) { setError(t(e.message)); setConfirm(null) } finally { setBusy('') }
  }
  return <section className="dv-panel">
    <h3><BusFront size={17} /> {t('Bus assignment')}</h3>
    {active.length === 0 ? <p className="dv-small warn"><FileWarning size={14} /> {t('Not assigned. The driver cannot use bus operations yet.')}</p>
      : <ul className="dv-assignments">{active.map((x) => <li key={x.id}><span><b>{t(x.busName)}</b><small>{x.routeNumber} · {t('since {date}', { date: day(x.assignedAt) })}</small></span>
        <button type="button" className="dv-chip danger" disabled={busy === x.id} onClick={() => setConfirm({ kind: 'end', x })}>{t('End')}</button></li>)}</ul>}
    <div className="dv-field">
      <label htmlFor="dv-assign-search">{t('Assign a bus')}</label>
      <div className="dv-control"><Search size={15} aria-hidden="true" /><input id="dv-assign-search" className="dv-input" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('Search the bus list')} /></div>
    </div>
    {matches.length > 0 && <ul className="dv-bus-results">{matches.map((b) => <li key={b._id || b.id}>
      <button type="button" disabled={Boolean(busy)} onClick={() => setConfirm({ kind: 'assign', b })}><BusFront size={15} /><span><b>{t(b.busName)}</b><small>{b.routeNumber}{(b._id || b.id) === a.claimedBusId ? ` · ${t('claimed by driver')}` : ''}</small></span></button>
    </li>)}</ul>}
    {error && <small className="dv-error" role="alert">{error}</small>}
    {confirm?.kind === 'assign' && <ConfirmDialog title={t('Assign {bus}?', { bus: t(confirm.b.busName) })} busy={busy === 'assign'} confirmLabel={t('Assign bus')}
      message={t('{name} will be able to use bus operations for this bus.', { name: a.fullName })}
      onConfirm={() => run(() => adminDriverApi.assignBus(a.driverId, confirm.b._id || confirm.b.id, token), 'assign')} onClose={() => setConfirm(null)} />}
    {confirm?.kind === 'end' && <ConfirmDialog title={t('End this assignment?')} tone="danger" busy={busy === confirm.x.id} confirmLabel={t('End assignment')}
      message={t('{name} will no longer be assigned to {bus}.', { name: a.fullName, bus: t(confirm.x.busName) })}
      onConfirm={() => run(() => adminDriverApi.endAssignment(a.driverId, confirm.x.id, token), confirm.x.id)} onClose={() => setConfirm(null)} />}
    <p className="dv-small"><CalendarClock size={14} /> {t('Assignments are recorded with your admin account.')}</p>
  </section>
}

