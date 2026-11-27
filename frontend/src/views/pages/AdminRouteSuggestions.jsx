import React, { useCallback, useEffect, useState } from 'react'
import {
  CheckCircle2, ChevronLeft, ChevronRight, CircleAlert, Eye, Lightbulb, Loader2, Mail, MapPin, Phone, RefreshCw, Search, User, XCircle,
} from 'lucide-react'
import { formatDateTime, formatNumber, t } from '../../models/i18n'
import {
  BUS_TYPE_LABELS, CORRECTION_LABELS, STATUSES, STATUS_LABELS, TYPES, TYPE_LABELS, coordinateProblem, day, mapLink, mapSearchLink, suggestionApi, suggestionPlace,
  toNumber,
} from '../../models/routeSuggestions'
import { CpModal, EvidenceGallery } from '../components/ComplaintUI'
import { SuggestionStatusBadge, TypeIcon } from './RouteSuggestions'
import '../styles/route-suggestions.css'

// F20 for admins: every route & stop suggestion, filtered by status and type. Reviewing shows exactly what approving
// would add to or change in the official bus data, and asks for confirmation before anything is saved.

const errorText = (error) => t(error?.message || 'Something went wrong. Please try again.')
const coords = (lat, lng) => `${Number(lat).toFixed(5)}, ${Number(lng).toFixed(5)}`

// ---- Review window ----

function Detail({ label, children }) {
  return <div><dt>{label}</dt><dd>{children}</dd></div>
}

function SuggestionDetails({ s, token }) {
  return <>
    <div className="cp-details-top"><SuggestionStatusBadge status={s.status} large /><span className="cp-chip"><TypeIcon type={s.type} size={14} /> {t(TYPE_LABELS[s.type])}</span></div>
    <h4>{t('Description')}</h4>
    <p className="cp-description">{s.description}</p>
    <dl className="cp-dl">
      {s.type === 'NEW_ROUTE' && <>
        <Detail label={t('Bus name')}>{s.routeName}</Detail>
        <Detail label={t('Route number')}>{s.routeNumber || t('None given (one is made on approval)')}</Detail>
        <Detail label={t('Bus type')}>{t(BUS_TYPE_LABELS[s.busType] || s.busType)}</Detail>
        <Detail label={t('Start → end')}>{s.startLocation} → {s.endLocation}</Detail>
      </>}
      {s.type !== 'NEW_ROUTE' && <Detail label={t('Route')}>{s.relatedBusName ? `${t(s.relatedBusName)}${s.relatedRouteNumber ? ` (${s.relatedRouteNumber})` : ''}` : t('No longer in the list')}</Detail>}
      {s.type === 'NEW_STOP' && <Detail label={t('New stop')}>{s.busStopName}</Detail>}
      {s.type === 'INCORRECT_INFORMATION' && <>
        <Detail label={t('What is wrong')}>{t(CORRECTION_LABELS[s.correctionField] || s.correctionField)}</Detail>
        {s.relatedStopName && <Detail label={t('Stop')}>{t(s.relatedStopName)}</Detail>}
        {s.correctedValue && <Detail label={t('Correct value')}>{s.correctionField === 'BUS_TYPE' ? t(BUS_TYPE_LABELS[s.correctedValue] || s.correctedValue) : s.correctedValue}</Detail>}
      </>}
      {s.locationName && <Detail label={t('Location name')}>{s.locationName}</Detail>}
      {s.latitude != null && <Detail label={t('Coordinates')}><a href={mapLink(s.latitude, s.longitude)} target="_blank" rel="noreferrer"><MapPin size={13} /> {coords(s.latitude, s.longitude)}</a></Detail>}
      <Detail label={t('Submitted')}>{formatDateTime(s.createdAt)}</Detail>
      {s.reviewedAt && <Detail label={t('Reviewed')}>{formatDateTime(s.reviewedAt)}{s.reviewedByName ? ` · ${s.reviewedByName}` : ''}</Detail>}
    </dl>
    {s.type === 'NEW_ROUTE' && s.stops?.length > 0 && <>
      <h4>{t('Stops in order')}</h4>
      <ol className="sug-route-stops">{s.stops.map((x) => <li key={x.order}><b>{t(x.name)}</b>
        <a href={mapLink(x.lat, x.lng)} target="_blank" rel="noreferrer">{coords(x.lat, x.lng)}</a>
        <small className={x.isExisting ? 'known' : 'new'}>{x.isExisting ? t('Known stop') : t('New place')}</small></li>)}</ol>
    </>}
    <h4>{t('Photo')}</h4>
    {s.imageUrl ? <EvidenceGallery evidence={[{ id: 'photo', url: s.imageUrl }]} token={token} /> : <p className="cp-muted">{t('No photo was added.')}</p>}
  </>
}

function Review({ id, token, onClose, onChanged, notify }) {
  const [state, setState] = useState({ status: 'loading' })
  const [preview, setPreview] = useState(null)
  const [step, setStep] = useState('') // '' · 'APPROVED' · 'REJECTED'
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  useEffect(() => {
    suggestionApi.get(id, token).then((res) => {
      setState({ status: 'ready', s: res.data })
      if (res.data.status === 'PENDING') suggestionApi.preview(id, token).then((p) => setPreview(p.data)).catch((e) => setPreview({ canApprove: false, problem: errorText(e), changes: [] }))
    }).catch((e) => setState({ status: 'error', error: errorText(e) }))
  }, [id, token])
  const s = state.s
  // Coordinates for the places the user only described, keyed by stop order ('self' for the suggestion's own place).
  const [points, setPoints] = useState({})
  const needs = preview?.needsLocations || []
  const keyOf = (n) => (n.order ?? 'self')
  const setPoint = (key, field, value) => { setPoints((p) => ({ ...p, [key]: { ...p[key], [field]: value } })); setError('') }
  const confirm = async () => {
    if (step === 'REJECTED' && note.trim().length < 5) { setError(t('Tell the user why (at least 5 characters).')); return }
    let extra = {}
    if (step === 'APPROVED' && needs.length) {
      for (const n of needs) {
        const problem = coordinateProblem(points[keyOf(n)]?.lat, points[keyOf(n)]?.lng, true)
        if (problem) { setError(`${n.name}: ${problem}`); return }
      }
      const self = needs.find((n) => n.order == null)
      extra = self
        ? { latitude: toNumber(points.self.lat), longitude: toNumber(points.self.lng) }
        : { stopLocations: needs.map((n) => ({ order: n.order, lat: toNumber(points[n.order].lat), lng: toNumber(points[n.order].lng) })) }
    }
    setBusy(true); setError('')
    try {
      const res = await suggestionApi.review(id, step, note.trim() || null, token, extra)
      notify(t(res.message))
      onChanged(res.data)
    } catch (e) { setError(errorText(e)); setBusy(false) }
  }
  return <CpModal title={s ? s.title : t('Suggestion')} subtitle={s?.code} onClose={busy ? () => {} : onClose} wide>
    {state.status === 'loading' ? <div className="cp-center"><Loader2 size={20} className="cp-spin" /> {t('Loading…')}</div>
      : state.status === 'error' ? <p className="cp-form-error" role="alert"><CircleAlert size={15} /> {state.error}</p>
        : <div className="cp-details">
          <div className="cp-details-main"><SuggestionDetails s={s} token={token} /></div>
          <aside className="cp-details-side">
            <h4>{t('Sent by')}</h4>
            {s.user ? <ul className="cp-facts sug-user">
              <li><User size={14} /> <span>{s.user.name}</span></li>
              <li><Mail size={14} /> <a href={`mailto:${s.user.email}`}>{s.user.email}</a></li>
              {s.user.phone && <li><Phone size={14} /> <span>{s.user.phone}</span></li>}
            </ul> : <p className="cp-muted">—</p>}
            {s.status !== 'PENDING' ? <>
              <h4>{t('Review')}</h4>
              {s.adminReviewNote ? <p className="cp-feedback"><span>{s.adminReviewNote}</span></p> : <p className="cp-muted">{t('No note.')}</p>}
              {s.appliedChange && <p className="sug-applied"><CheckCircle2 size={14} /> <span>{s.appliedChange}</span></p>}
            </> : <>
              <h4>{t('If you approve')}</h4>
              {!preview ? <div className="cp-center"><Loader2 size={16} className="cp-spin" /> {t('Checking against the bus data…')}</div>
                : preview.canApprove ? <ul className="sug-changes" data-testid="sug-changes">{preview.changes.map((c) => <li key={c}>{c}</li>)}</ul>
                  : <p className="cp-form-error" role="alert"><CircleAlert size={15} /> {t('It cannot be approved: {problem}', { problem: preview.problem })}</p>}
              {!step ? <div className="sug-review-buttons">
                <button type="button" className="button cp-primary" disabled={!preview?.canApprove} onClick={() => { setStep('APPROVED'); setNote(''); setError('') }}><CheckCircle2 size={16} /> {t('Approve')}</button>
                <button type="button" className="button cp-danger" disabled={!preview || preview.problem?.includes('own suggestion')} onClick={() => { setStep('REJECTED'); setNote(''); setError('') }}><XCircle size={16} /> {t('Reject')}</button>
              </div> : <div className={`sug-confirm ${step === 'APPROVED' ? 'approve' : 'reject'}`} role="alertdialog" aria-label={step === 'APPROVED' ? t('Confirm approval') : t('Confirm rejection')}>
                <b>{step === 'APPROVED' ? t('Approve and update the official bus data?') : t('Reject this suggestion?')}</b>
                <p>{step === 'APPROVED' ? t('The changes listed above are saved at once and everyone sees them.') : t('The bus data is not changed. The user sees your reason.')}</p>
                {step === 'APPROVED' && needs.length > 0 && <div className="sug-needs">
                  <b>{t('Confirm the location')}</b>
                  <p>{t('The user described these places in words. Find each on the map and enter its coordinates.')}</p>
                  {needs.map((n) => <div key={keyOf(n)} className="sug-need">
                    <span className="sug-need-name">{n.name}{n.description && <small>{n.description}</small>}
                      <a href={mapSearchLink(n.description || n.name)} target="_blank" rel="noreferrer"><MapPin size={12} /> {t('Search on map')}</a></span>
                    <span className="sug-need-inputs">
                      <input className="cp-text" inputMode="decimal" placeholder="23.7509" aria-label={t('Latitude of {name}', { name: n.name })}
                        value={points[keyOf(n)]?.lat || ''} onChange={(e) => setPoint(keyOf(n), 'lat', e.target.value)} />
                      <input className="cp-text" inputMode="decimal" placeholder="90.3935" aria-label={t('Longitude of {name}', { name: n.name })}
                        value={points[keyOf(n)]?.lng || ''} onChange={(e) => setPoint(keyOf(n), 'lng', e.target.value)} />
                    </span>
                  </div>)}
                </div>}
                <label htmlFor="sug-note">{step === 'APPROVED' ? t('Note to the user (optional)') : t('Reason for rejecting')}</label>
                <textarea id="sug-note" className="cp-text" rows={3} maxLength={1000} value={note} onChange={(e) => { setNote(e.target.value); setError('') }}
                  placeholder={step === 'APPROVED' ? t('e.g. Checked on the map. Thank you!') : t('e.g. This stop is already listed as Farmgate.')} />
                {error && <p className="cp-form-error" role="alert"><CircleAlert size={15} /> {error}</p>}
                <div><button type="button" className="button button-outline button-small" onClick={() => setStep('')} disabled={busy}>{t('Cancel')}</button>
                  <button type="button" className={`button button-small ${step === 'APPROVED' ? 'cp-primary' : 'cp-danger'}`} onClick={confirm} disabled={busy}>
                    {busy ? <Loader2 size={14} className="cp-spin" /> : step === 'APPROVED' ? <CheckCircle2 size={14} /> : <XCircle size={14} />} {step === 'APPROVED' ? t('Yes, approve') : t('Yes, reject')}</button></div>
              </div>}
            </>}
          </aside>
        </div>}
  </CpModal>
}

// ---- The admin section ----

export function AdminRouteSuggestions({ token, notify = () => {} }) {
  const [status, setStatus] = useState('PENDING')
  const [type, setType] = useState('')
  const [search, setSearch] = useState('')
  const [typed, setTyped] = useState('')
  const [page, setPage] = useState(1)
  const [state, setState] = useState({ status: 'loading', items: [], counts: null })
  const [openId, setOpenId] = useState(null)
  const load = useCallback(async () => {
    setState((now) => ({ ...now, status: now.items.length ? 'refreshing' : 'loading' }))
    try {
      const res = await suggestionApi.all({ status, type, search, page, pageSize: 12 }, token)
      setState({ status: 'ready', items: res.data || [], counts: res.counts, total: res.total, totalPages: res.totalPages })
    } catch (e) { setState({ status: 'error', items: [], counts: null, error: errorText(e) }) }
  }, [status, type, search, page, token])
  useEffect(() => { load() }, [load])
  useEffect(() => { const timer = window.setTimeout(() => { setSearch(typed.trim()); setPage(1) }, 400); return () => window.clearTimeout(timer) }, [typed])
  const counts = state.counts
  const tabs = [['PENDING', counts?.pending], ['APPROVED', counts?.approved], ['REJECTED', counts?.rejected], ['', counts && counts.pending + counts.approved + counts.rejected]]
  return <section className="pf-panel cp-page sug-admin" id="admin-route-suggestions" aria-labelledby="sug-admin-title">
    <header className="cp-panel-head sug-panel-head">
      <div><h2 id="sug-admin-title"><Lightbulb size={18} /> {t('Route & Stop Suggestions')} {counts?.pending > 0 && <span className="ad-count">{formatNumber(counts.pending)}</span>}</h2>
        <p>{t('Check what commuters suggest. Approving updates the official bus routes and stops; rejecting changes nothing.')}</p></div>
      <button type="button" className="button button-outline button-small" onClick={load} disabled={state.status === 'loading' || state.status === 'refreshing'}><RefreshCw size={14} /> {t('Refresh')}</button>
    </header>
    <div className="cp-filter" role="group" aria-label={t('Filter by status')}>
      {tabs.map(([value, count]) => <button key={value || 'all'} type="button" aria-pressed={status === value} className={status === value ? 'selected' : ''}
        onClick={() => { setStatus(value); setPage(1) }}>{value ? t(STATUS_LABELS[value]) : t('All')} <span>{count == null ? '…' : formatNumber(count)}</span></button>)}
    </div>
    <div className="sug-admin-filters">
      <select className="cp-text" value={type} onChange={(e) => { setType(e.target.value); setPage(1) }} aria-label={t('Suggestion type')}>
        <option value="">{t('All types')}</option>{TYPES.map((x) => <option key={x} value={x}>{t(TYPE_LABELS[x])}</option>)}
      </select>
      <span className="cp-input"><Search size={15} /><input value={typed} onChange={(e) => setTyped(e.target.value)} placeholder={t('Search RS-code, title or user')} aria-label={t('Search suggestions')} /></span>
    </div>
    {state.status === 'loading' ? <div className="cp-skeletons">{[0, 1, 2].map((i) => <i key={i} />)}</div>
      : state.status === 'error' ? <p className="cp-form-error" role="alert"><CircleAlert size={15} /> {state.error} <button type="button" className="cp-link" onClick={load}>{t('Try again')}</button></p>
        : !state.items.length ? <div className="cp-empty"><Lightbulb size={28} /><b>{status === 'PENDING' ? t('No suggestions are waiting') : t('No suggestions found')}</b><span>{t('New suggestions from commuters appear here.')}</span></div>
          : <div className="sug-admin-list">{state.items.map((s) => <article key={s.id} className="sug-admin-row" data-testid="sug-admin-row">
            <span className="sug-admin-icon"><TypeIcon type={s.type} /></span>
            <div className="sug-admin-main"><b>{s.title}</b>
              <small>{s.code} · {t(TYPE_LABELS[s.type])} · {suggestionPlace(s) || '—'}</small>
              <small>{t('By {name}', { name: s.user?.name || '—' })} · {day(s.createdAt)}</small></div>
            <SuggestionStatusBadge status={s.status} />
            <button type="button" className="button button-small cp-primary" onClick={() => setOpenId(s.id)} aria-label={t('Review {code}', { code: s.code })}>
              <Eye size={14} /> {s.status === 'PENDING' ? t('Review') : t('View')}</button>
          </article>)}</div>}
    {state.totalPages > 1 && <nav className="sug-pages" aria-label={t('Pages')}>
      <button type="button" className="cp-icon" disabled={page <= 1} onClick={() => setPage((p) => p - 1)} aria-label={t('Previous page')}><ChevronLeft size={16} /></button>
      <span>{t('Page {page} of {total}', { page: formatNumber(page), total: formatNumber(state.totalPages) })}</span>
      <button type="button" className="cp-icon" disabled={page >= state.totalPages} onClick={() => setPage((p) => p + 1)} aria-label={t('Next page')}><ChevronRight size={16} /></button>
    </nav>}
    {openId && <Review id={openId} token={token} notify={notify} onClose={() => setOpenId(null)} onChanged={() => { setOpenId(null); load() }} />}
  </section>
}
