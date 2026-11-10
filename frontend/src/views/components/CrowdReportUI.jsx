import React, { useEffect, useMemo, useRef, useState } from 'react'
import {
  Armchair, ArrowRight, BusFront, Calendar, Check, CircleAlert, Clock3, Eye, Loader2, MapPin, MessageSquareText, Navigation, Pencil, Route as RouteIcon,
  History, LocateFixed, Minus, Plus, Send, ShieldCheck, Trash2, UserRound, Users, UsersRound, X,
} from 'lucide-react'
import { useBusRoutes } from '../../controllers/useBusRoutes'
import { formatNumber, t } from '../../models/i18n'
import {
  CROWD_BY_VALUE, CROWD_LEVELS, FRESHNESS, SEATS_BY_VALUE, SEAT_OPTIONS, agoText, clock, crowdApi, crowdProblems, dayTime, dhakaDate, dhakaTime, dhakaToMillis,
  editMinutesLeft, emptyCrowdForm, formFromReport, freshnessOf, groupBuses, toCrowdBody,
} from '../../models/crowdReports'
import { searchKey } from './BusNameSearch'
import '../styles/crowd.css'

// F19: the pieces of the crowd report pages: badges, the report card, the dialog and the report form.

export const errorText = (error) => t(error?.message || 'Something went wrong. Please try again.')

// The current time, ticking, so freshness labels move on by themselves ("Very Recent" turns "Recent"…).
export function useNow(intervalMs = 30_000) {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), intervalMs)
    return () => window.clearInterval(id)
  }, [intervalMs])
  return now
}

const CROWD_ICONS = { LOW: UserRound, MEDIUM: Users, HIGH: UsersRound }

// Three bars that fill up with the crowd level.
export function CrowdMeter({ level }) {
  const filled = { LOW: 1, MEDIUM: 2, HIGH: 3 }[level] || 0
  return <span className={`cr-meter ${CROWD_BY_VALUE[level]?.tone || ''}`} aria-hidden="true">{[1, 2, 3].map((i) => <i key={i} className={i <= filled ? 'on' : ''} />)}</span>
}

export function CrowdBadge({ level, muted = false }) {
  const c = CROWD_BY_VALUE[level]
  if (!c) return null
  const Icon = CROWD_ICONS[level]
  return <span className={`cr-badge ${muted ? 'old' : c.tone}`} data-testid="crowd-badge"><Icon size={13} /> {t('{level} crowd', { level: t(c.label) })}</span>
}

export function SeatBadge({ value, count, muted = false }) {
  const s = SEATS_BY_VALUE[value]
  if (!s) return null
  const text = value === 'NO_SEATS' ? t('No seats') : count ? t('{label} · {count} free', { label: t(s.label), count: formatNumber(count) }) : t('{label} seats', { label: t(s.label) })
  return <span className={`cr-badge ${muted ? 'old' : s.tone}`} data-testid="seat-badge"><Armchair size={13} /> {text}</span>
}

export function FreshnessBadge({ observedAt, now }) {
  const f = freshnessOf(observedAt, now)
  return <span className={`cr-fresh ${FRESHNESS[f].tone}`} data-testid="freshness"><i aria-hidden="true" /> {t(FRESHNESS[f].label)}</span>
}

// One report. Expired reports are shown greyed out and say plainly that they are not the bus's current state.
export function CrowdReportCard({ report: r, now, onOpen, children }) {
  const expired = freshnessOf(r.observedAt, now) === 'EXPIRED'
  const tone = expired ? 'old' : CROWD_BY_VALUE[r.crowdLevel]?.tone
  return <article className={`cr-card ${tone}${expired ? ' expired' : ''}`} data-testid="crowd-card">
    <header>
      <span className="cr-card-bus"><span className="cr-card-icon"><BusFront size={17} /></span>
        <span><b>{t(r.busName)}</b><small>{r.routeName || (r.routeNumber ? t('Route {number}', { number: r.routeNumber }) : '')}</small></span></span>
      <FreshnessBadge observedAt={r.observedAt} now={now} />
    </header>
    <div className="cr-card-badges"><CrowdMeter level={expired ? '' : r.crowdLevel} /><CrowdBadge level={r.crowdLevel} muted={expired} /><SeatBadge value={r.seatAvailability} count={r.availableSeatCount} muted={expired} /></div>
    <ul className="cr-facts">
      <li><MapPin size={14} /> <span>{t('At {stop}', { stop: t(r.stopName) })}{r.towardsStop ? <small> · {t('towards {stop}', { stop: t(r.towardsStop) })}</small> : null}</span></li>
      <li><Clock3 size={14} /> <span>{t('Seen at {time}', { time: clock(r.observedAt) })} · <b>{agoText(r.observedAt, now)}</b></span></li>
      {r.standingPassengerCount != null && <li><Users size={14} /> <span>{t('About {count} standing', { count: formatNumber(r.standingPassengerCount) })}</span></li>}
    </ul>
    {expired && <p className="cr-expired-note"><CircleAlert size={13} /> {t('Over an hour old. This may not be the bus’s current condition.')}</p>}
    {(onOpen || children) && <footer>{children}{onOpen && <button type="button" className="cr-btn ghost small" onClick={() => onOpen(r)}><Eye size={14} /> {t('Details')}</button>}</footer>}
  </article>
}

export function CrModal({ title, subtitle, onClose, children, wide = false }) {
  useEffect(() => {
    const onKey = (event) => { if (event.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    const overflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => { document.removeEventListener('keydown', onKey); document.body.style.overflow = overflow }
  }, [onClose])
  return <div className="cr-backdrop" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose() }}>
    <div className={`cr-modal${wide ? ' wide' : ''}`} role="dialog" aria-modal="true" aria-label={title}>
      <header className="cr-modal-head"><div><h2>{title}</h2>{subtitle && <small>{subtitle}</small>}</div>
        <button type="button" className="cr-icon" onClick={onClose} aria-label={t('Close')}><X size={18} /></button></header>
      <div className="cr-modal-body">{children}</div>
    </div>
  </div>
}

// Everything in one report, for the details dialog.
export function CrowdReportDetails({ report: r, now, onEdit, onDelete, busy = false }) {
  const [confirm, setConfirm] = useState(false)
  const expired = freshnessOf(r.observedAt, now) === 'EXPIRED'
  const minutesLeft = editMinutesLeft(r, now)
  return <div className="cr-details">
    <div className="cr-details-top"><FreshnessBadge observedAt={r.observedAt} now={now} /><span className="cr-ago">{agoText(r.observedAt, now)}</span></div>
    {expired && <p className="cr-expired-note big"><CircleAlert size={15} /> {t('This report is over an hour old and is kept only as history. Do not treat it as the current condition of the bus.')}</p>}
    <div className="cr-details-badges"><CrowdBadge level={r.crowdLevel} muted={expired} /><SeatBadge value={r.seatAvailability} count={r.availableSeatCount} muted={expired} /></div>
    <dl className="cr-dl">
      <div><dt><BusFront size={14} /> {t('Bus')}</dt><dd>{t(r.busName)}</dd></div>
      <div><dt><RouteIcon size={14} /> {t('Route')}</dt><dd>{r.routeName || '—'}</dd></div>
      <div><dt><MapPin size={14} /> {t('Seen at stop')}</dt><dd>{t(r.stopName)}</dd></div>
      <div><dt><Navigation size={14} /> {t('Direction')}</dt><dd>{r.towardsStop ? t('Towards {stop}', { stop: t(r.towardsStop) }) : '—'}</dd></div>
      <div><dt><Clock3 size={14} /> {t('Observed')}</dt><dd>{dayTime(r.observedAt)}</dd></div>
      <div><dt><Calendar size={14} /> {t('Submitted')}</dt><dd>{dayTime(r.createdAt)}{r.updatedAt && r.updatedAt !== r.createdAt && new Date(r.updatedAt) - new Date(r.createdAt) > 1000 ? <small> · {t('edited')}</small> : null}</dd></div>
      <div><dt><Armchair size={14} /> {t('Free seats')}</dt><dd>{r.availableSeatCount != null ? formatNumber(r.availableSeatCount) : t('Not given')}</dd></div>
      <div><dt><Users size={14} /> {t('Standing passengers')}</dt><dd>{r.standingPassengerCount != null ? t('About {count}', { count: formatNumber(r.standingPassengerCount) }) : t('Not given')}</dd></div>
    </dl>
    {r.additionalComments && <div className="cr-comment"><MessageSquareText size={15} /><p>{r.additionalComments}</p></div>}
    <p className="cr-muted">{r.isMine ? t('You submitted this report.') : t('Reported by a SmartCommute commuter.')}</p>
    {r.isMine && (onEdit || onDelete) && <div className="cr-details-actions">
      {onEdit && (minutesLeft > 0
        ? <button type="button" className="cr-btn primary small" onClick={() => onEdit(r)}><Pencil size={14} /> {t('Edit · {count} min left', { count: formatNumber(minutesLeft) })}</button>
        : <span className="cr-muted">{t('Reports can be edited only in the first 15 minutes.')}</span>)}
      {onDelete && (confirm
        ? <span className="cr-confirm" role="alertdialog" aria-label={t('Delete report')}>{t('Delete this report?')}
          <button type="button" className="cr-btn ghost small" onClick={() => setConfirm(false)} disabled={busy}>{t('Cancel')}</button>
          <button type="button" className="cr-btn danger small" onClick={() => onDelete(r)} disabled={busy}>{busy ? <Loader2 size={14} className="cr-spin" /> : <Trash2 size={14} />} {t('Yes, delete')}</button></span>
        : <button type="button" className="cr-btn danger-outline small" onClick={() => setConfirm(true)}><Trash2 size={14} /> {t('Delete')}</button>)}
    </div>}
  </div>
}

// ---- The form ----

function Field({ label, htmlFor, error, hint, optional = false, children, wide = false }) {
  return <div className={`cr-field${error ? ' has-error' : ''}${wide ? ' wide' : ''}`}>
    <label htmlFor={htmlFor}>{label}{optional && <em> · {t('optional')}</em>}</label>
    {children}
    {error ? <small className="cr-error" role="alert">{error}</small> : hint ? <small className="cr-hint">{hint}</small> : null}
  </div>
}

// A searchable list of the Dhaka buses from the database. Only a bus picked from the list counts.
function BusPicker({ buses, status, value, onPick, invalid }) {
  const [text, setText] = useState(value ? t(value) : '')
  const [open, setOpen] = useState(false)
  const box = useRef(null)
  useEffect(() => { setText(value ? t(value) : '') }, [value])
  useEffect(() => {
    if (!open) return
    const away = (e) => { if (!box.current?.contains(e.target)) setOpen(false) }
    document.addEventListener('pointerdown', away)
    return () => document.removeEventListener('pointerdown', away)
  }, [open])
  const list = useMemo(() => {
    const q = searchKey(text)
    if (!q || (value && text === t(value))) return buses.slice(0, 80)
    return buses.filter((b) => searchKey(b.name).includes(q) || searchKey(t(b.name)).includes(q) || b.routes.some((r) => searchKey(r.number).startsWith(q))).slice(0, 80)
  }, [buses, text, value])
  return <div className="cr-picker" ref={box}>
    <span className={`cr-input${invalid ? ' invalid' : ''}`}><BusFront size={16} aria-hidden="true" />
      <input id="cr-bus" value={text} role="combobox" aria-expanded={open} aria-controls="cr-bus-list" autoComplete="off" aria-invalid={invalid || undefined}
        placeholder={status === 'loading' ? t('Loading Dhaka buses…') : status === 'error' ? t('Could not load the buses. Refresh the page.') : t('Search a bus, e.g. Raida or 36')}
        onFocus={() => setOpen(true)} onChange={(e) => { setText(e.target.value); setOpen(true); if (value) onPick(null) }} />
    </span>
    {open && status === 'ready' && <ul className="cr-options" id="cr-bus-list" role="listbox" aria-label={t('Dhaka buses')}>
      {!list.length ? <li className="cr-option-note">{t('No Dhaka bus matches. Try another name or route number.')}</li>
        : list.map((b) => <li key={b.name} role="option" aria-selected={b.name === value} className={b.name === value ? 'selected' : ''}
          onPointerDown={(e) => e.preventDefault()} onClick={() => { onPick(b); setText(t(b.name)); setOpen(false) }}>
          <b>{t(b.name)}</b><small>{b.routes.length === 1 ? b.routes[0].label : t('{count} routes', { count: b.routes.length })}</small></li>)}
    </ul>}
  </div>
}

function ChoiceCards({ name, options, value, onChange, icon, label, error }) {
  return <>
    <div className="cr-choices" role="radiogroup" aria-label={label}>
      {options.map((o) => {
        const Icon = icon(o.value)
        return <label key={o.value} className={`cr-choice ${o.tone}${value === o.value ? ' selected' : ''}`}>
          <input type="radio" name={name} value={o.value} checked={value === o.value} onChange={() => onChange(o.value)} />
          <span className="cr-choice-icon"><Icon size={22} /></span>
          <span className="cr-choice-text"><b>{t(o.label)}</b><small>{t(o.description)}</small></span>
          <span className="cr-choice-check" aria-hidden="true"><Check size={14} /></span>
        </label>
      })}
    </div>
    {error && <small className="cr-error" role="alert">{error}</small>}
  </>
}

const SEAT_ICONS = { AVAILABLE: Armchair, LIMITED: Armchair, NO_SEATS: X }

// The buses this person reported last, kept in this browser only, so a regular bus is one tap away.
const RECENT_KEY = 'sc_crowd_recent_buses'
const readRecent = () => { try { const list = JSON.parse(localStorage.getItem(RECENT_KEY) || '[]'); return Array.isArray(list) ? list : [] } catch { return [] } }
const rememberBus = (busName, busId) => {
  try { localStorage.setItem(RECENT_KEY, JSON.stringify([{ busName, busId }, ...readRecent().filter((b) => b.busId !== busId)].slice(0, 4))) } catch { /* just not remembered */ }
}

// Straight-line distance in metres.
const metres = (a, b) => {
  const rad = (d) => (d * Math.PI) / 180
  const h = Math.sin(rad(b.lat - a.lat) / 2) ** 2 + Math.cos(rad(a.lat)) * Math.cos(rad(b.lat)) * Math.sin(rad(b.lng - a.lng) / 2) ** 2
  return 2 * 6_371_000 * Math.asin(Math.sqrt(h))
}

const TIME_CHIPS = [[0, 'Just now'], [5, '5 min ago'], [15, '15 min ago'], [30, '30 min ago']]

// A number box with − and + buttons, never below 0.
function Stepper({ id, value, onChange, max, disabled, placeholder, label }) {
  const n = String(value ?? '').trim() === '' ? null : Number(value)
  const step = (d) => onChange(String(Math.min(max, Math.max(0, (Number.isFinite(n) ? n : 0) + d))))
  return <span className={`cr-stepper${disabled ? ' disabled' : ''}`}>
    <button type="button" onClick={() => step(-1)} disabled={disabled || !n} aria-label={t('Decrease {label}', { label })}><Minus size={16} /></button>
    <input id={id} type="number" inputMode="numeric" min="0" max={max} step="1" className="cr-text" value={value} disabled={disabled} placeholder={placeholder}
      onKeyDown={(e) => { if (['-', '+', 'e', 'E', '.'].includes(e.key)) e.preventDefault() }} onChange={(e) => onChange(e.target.value)} />
    <button type="button" onClick={() => step(1)} disabled={disabled || n >= max} aria-label={t('Increase {label}', { label })}><Plus size={16} /></button>
  </span>
}

function StepTitle({ number, done, children }) {
  return <h3><span className={done ? 'done' : ''} aria-hidden="true">{done ? <Check size={14} strokeWidth={3} /> : number}</span> {children}
    {done && <em className="cr-done-tag">{t('Done')}</em>}</h3>
}

// New report, or editing one (report given). The submit button is locked while sending, so a double click sends once;
// the server also refuses the same sighting twice.
export function BusCrowdReportForm({ token, report = null, onDone, onCancel }) {
  const [routes] = useBusRoutes()
  const buses = useMemo(() => groupBuses(routes.status === 'ready' ? routes.buses : []), [routes])
  const [form, setForm] = useState(() => (report ? formFromReport(report) : emptyCrowdForm()))
  const [problems, setProblems] = useState({})
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [locating, setLocating] = useState(false)
  const [locateNote, setLocateNote] = useState('')
  const [recent] = useState(readRecent)
  const sending = useRef(false)
  const formRef = useRef(null)
  const bus = buses.find((b) => b.name === form.busName)
  const route = bus?.routes.find((r) => r.id === form.busId)
  const stop = route?.stops.find((s) => s.id === form.busStopId)
  const set = (key, value) => { setForm((f) => ({ ...f, [key]: value })); setProblems((p) => ({ ...p, [key]: undefined })); setError('') }

  const pickBus = (b) => {
    setForm((f) => ({ ...f, busName: b?.name || '', busId: b?.routes.length === 1 ? b.routes[0].id : '', busStopId: '', travelDirection: '' }))
    setProblems((p) => ({ ...p, busName: undefined, busId: undefined }))
    setLocateNote('')
  }
  const pickRoute = (id) => { setForm((f) => ({ ...f, busId: id, busStopId: '', travelDirection: '' })); setProblems((p) => ({ ...p, busId: undefined })); setLocateNote('') }
  const pickRecent = (r) => {
    const b = buses.find((x) => x.name === r.busName)
    if (!b) return
    const known = b.routes.some((x) => x.id === r.busId)
    setForm((f) => ({ ...f, busName: b.name, busId: known ? r.busId : b.routes.length === 1 ? b.routes[0].id : '', busStopId: '', travelDirection: '' }))
    setProblems((p) => ({ ...p, busName: undefined, busId: undefined }))
  }
  const setTimeAgo = (minutes) => {
    const at = Date.now() - minutes * 60_000
    setForm((f) => ({ ...f, observationDate: dhakaDate(at), observationTime: dhakaTime(at) }))
    setProblems((p) => ({ ...p, observationDate: undefined, observationTime: undefined }))
  }
  const minutesAgo = Math.round((Date.now() - dhakaToMillis(form.observationDate, form.observationTime)) / 60_000)

  // Picks the stop on this route closest to the phone's position.
  const nearestStop = () => {
    const located = route?.stops.filter((s) => Number.isFinite(s.lat) && Number.isFinite(s.lng))
    if (!located?.length) return
    if (!navigator.geolocation) { setLocateNote(t('Location is not available in this browser. Choose the stop from the list.')); return }
    setLocating(true); setLocateNote('')
    navigator.geolocation.getCurrentPosition((pos) => {
      const here = { lat: pos.coords.latitude, lng: pos.coords.longitude }
      const best = located.map((s) => ({ s, d: metres(here, s) })).sort((a, b) => a.d - b.d)[0]
      setLocating(false)
      if (best.d > 3000) { setLocateNote(t('You seem to be far from this route. Choose the stop from the list.')); return }
      set('busStopId', best.s.id)
      setLocateNote(t('Picked {stop}, about {distance} m away.', { stop: t(best.s.name), distance: formatNumber(Math.round(best.d / 10) * 10) }))
    }, () => { setLocating(false); setLocateNote(t('Could not get your location. Choose the stop from the list.')) }, { enableHighAccuracy: true, timeout: 10_000, maximumAge: 60_000 })
  }

  const steps = {
    bus: Boolean(form.busName && form.busId && form.busStopId && form.travelDirection),
    crowd: Boolean(form.crowdLevel),
    seats: Boolean(form.seatAvailability),
    time: Boolean(form.observationDate && form.observationTime) && !crowdProblems(form).observationTime && !crowdProblems(form).observationDate,
  }
  const doneCount = Object.values(steps).filter(Boolean).length

  const submit = async (event) => {
    event.preventDefault()
    if (sending.current) return
    const found = crowdProblems(form)
    setProblems(found)
    if (Object.keys(found).length) {
      setError(t('Please fix the highlighted fields.'))
      window.setTimeout(() => formRef.current?.querySelector('.has-error, .cr-error')?.scrollIntoView?.({ behavior: 'smooth', block: 'center' }), 0)
      return
    }
    sending.current = true
    setBusy(true); setError('')
    try {
      const result = report ? await crowdApi.update(report.id, toCrowdBody(form), token) : await crowdApi.create(toCrowdBody(form), token)
      rememberBus(form.busName, form.busId)
      if (!report) setForm(emptyCrowdForm())
      onDone(result)
    } catch (e) { setError(errorText(e)) } finally { sending.current = false; setBusy(false) }
  }

  const directions = route ? [
    { value: 'FORWARD', to: route.last, from: route.first },
    ...(route.runsBothWays ? [{ value: 'RETURN', to: route.first, from: route.last }] : []),
  ] : []
  const recentShown = !form.busName && recent.filter((r) => buses.some((b) => b.name === r.busName))
  const canLocate = route?.stops.some((s) => Number.isFinite(s.lat) && Number.isFinite(s.lng))

  return <form className="cr-form" ref={formRef} onSubmit={submit} noValidate aria-label={report ? t('Edit crowd report') : t('New crowd report')}>
    <div className="cr-progress" aria-label={t('{done} of {total} steps done', { done: doneCount, total: 4 })}>
      <div className="cr-progress-text"><b>{doneCount === 4 ? t('All set! Review and submit.') : t('{done} of {total} steps done', { done: formatNumber(doneCount), total: formatNumber(4) })}</b>
        <small>{t('Takes less than a minute')}</small></div>
      <div className="cr-progress-bar" aria-hidden="true"><i style={{ width: `${(doneCount / 4) * 100}%` }} /></div>
    </div>

    <section className={`cr-form-section${steps.bus ? ' is-done' : ''}`}>
      <StepTitle number={1} done={steps.bus}>{t('Which bus did you see?')}</StepTitle>
      {recentShown?.length > 0 && <div className="cr-recent"><span><History size={13} /> {t('Your recent buses')}</span>
        {recentShown.map((r) => { const rt = buses.find((b) => b.name === r.busName)?.routes.find((x) => x.id === r.busId)
          return <button key={r.busId} type="button" className="cr-pill" onClick={() => pickRecent(r)}><BusFront size={13} /> {t(r.busName)}{rt?.number ? <em>{rt.number}</em> : null}</button> })}
      </div>}
      <div className="cr-grid">
        <Field label={t('Bus name')} htmlFor="cr-bus" error={problems.busName} hint={t('Only Dhaka city buses from our database are listed.')}>
          <BusPicker buses={buses} status={routes.status} value={form.busName} onPick={pickBus} invalid={Boolean(problems.busName)} />
        </Field>
        <Field label={t('Bus route')} htmlFor="cr-route" error={problems.busId}>
          <select id="cr-route" className="cr-text" value={form.busId} disabled={!bus} onChange={(e) => pickRoute(e.target.value)}>
            <option value="">{bus ? t('Choose the route') : t('Choose a bus first')}</option>
            {bus?.routes.map((r) => <option key={r.id} value={r.id}>{r.label || t('Route')}</option>)}
          </select>
        </Field>
        <Field label={t('Current bus stop')} htmlFor="cr-stop" error={problems.busStopId}
          hint={locateNote || (canLocate ? <button type="button" className="cr-link cr-locate" onClick={nearestStop} disabled={locating}>
            {locating ? <Loader2 size={12} className="cr-spin" /> : <LocateFixed size={12} />} {locating ? t('Finding you…') : t('Use my nearest stop')}</button> : t('Where you saw the bus.'))}>
          <select id="cr-stop" className="cr-text" value={form.busStopId} disabled={!route} onChange={(e) => { set('busStopId', e.target.value); setLocateNote('') }}>
            <option value="">{route ? (route.stops.length ? t('Choose the stop') : t('Loading stops…')) : t('Choose a route first')}</option>
            {route?.stops.map((s) => <option key={s.id} value={s.id}>{t(s.name)}</option>)}
          </select>
        </Field>
      </div>
      <div className={`cr-field${problems.travelDirection ? ' has-error' : ''}`}>
        <span className="cr-label" id="cr-direction-label">{t('Travel direction')}</span>
        <div className="cr-directions" role="radiogroup" aria-labelledby="cr-direction-label">
          {!route ? <span className="cr-muted">{t('Choose a route to see its directions.')}</span> : directions.map((d) =>
            <label key={d.value} className={`cr-direction${form.travelDirection === d.value ? ' selected' : ''}`}>
              <input type="radio" name="cr-direction" value={d.value} checked={form.travelDirection === d.value} onChange={() => set('travelDirection', d.value)} />
              <Navigation size={16} /><span><b>{t('Towards {stop}', { stop: t(d.to) })}</b><small>{t('from {stop}', { stop: t(d.from) })}</small></span>
            </label>)}
        </div>
        {problems.travelDirection && <small className="cr-error" role="alert">{problems.travelDirection}</small>}
      </div>
    </section>

    <section className={`cr-form-section${steps.crowd ? ' is-done' : ''}`}>
      <StepTitle number={2} done={steps.crowd}>{t('How crowded was it?')}</StepTitle>
      <ChoiceCards name="cr-crowd" label={t('Crowd level')} options={CROWD_LEVELS} value={form.crowdLevel} onChange={(v) => set('crowdLevel', v)} icon={(v) => CROWD_ICONS[v]} error={problems.crowdLevel} />
    </section>

    <section className={`cr-form-section${steps.seats ? ' is-done' : ''}`}>
      <StepTitle number={3} done={steps.seats}>{t('Were seats available?')}</StepTitle>
      <ChoiceCards name="cr-seats" label={t('Seat availability')} options={SEAT_OPTIONS} value={form.seatAvailability}
        onChange={(v) => { set('seatAvailability', v); if (v === 'NO_SEATS') set('availableSeatCount', '') }} icon={(v) => SEAT_ICONS[v]} error={problems.seatAvailability} />
    </section>

    <section className={`cr-form-section${steps.time ? ' is-done' : ''}`}>
      <StepTitle number={4} done={steps.time}>{t('When did you see it?')}</StepTitle>
      <div className="cr-time-chips" role="group" aria-label={t('Quick time')}>
        {TIME_CHIPS.map(([m, label]) => <button key={m} type="button" className={`cr-pill${Math.abs(minutesAgo - m) <= 1 ? ' selected' : ''}`} onClick={() => setTimeAgo(m)}><Clock3 size={13} /> {t(label)}</button>)}
      </div>
      <div className="cr-grid">
        <Field label={t('Observation date')} htmlFor="cr-date" error={problems.observationDate}>
          <input id="cr-date" type="date" className="cr-text" value={form.observationDate} max={dhakaDate()} onChange={(e) => set('observationDate', e.target.value)} />
        </Field>
        <Field label={t('Observation time')} htmlFor="cr-time" error={problems.observationTime} hint={t('Or pick a quick time above.')}>
          <input id="cr-time" type="time" className="cr-text" value={form.observationTime} onChange={(e) => set('observationTime', e.target.value)} />
        </Field>
      </div>
      <details className="cr-extra" open={Boolean(report && (form.availableSeatCount !== '' || form.standingPassengerCount !== '' || form.additionalComments)) || undefined}>
        <summary><Plus size={14} /> {t('Add more details')} <em>· {t('optional, but helpful')}</em></summary>
        <div className="cr-grid">
          <Field label={t('Available seats')} htmlFor="cr-seat-count" optional error={problems.availableSeatCount}>
            <Stepper id="cr-seat-count" label={t('Available seats')} value={form.availableSeatCount} max={100} disabled={form.seatAvailability === 'NO_SEATS'}
              placeholder={form.seatAvailability === 'NO_SEATS' ? t('No seats') : t('e.g. 4')} onChange={(v) => set('availableSeatCount', v)} />
          </Field>
          <Field label={t('Standing passengers (approx.)')} htmlFor="cr-standing" optional error={problems.standingPassengerCount}>
            <Stepper id="cr-standing" label={t('Standing passengers')} value={form.standingPassengerCount} max={200} placeholder={t('e.g. 10')} onChange={(v) => set('standingPassengerCount', v)} />
          </Field>
          <Field label={t('Additional comments')} htmlFor="cr-comments" optional wide error={problems.additionalComments}
            hint={`${formatNumber(form.additionalComments.trim().length)}/500 · ${t('e.g. ladies seats full, AC not working, bus moving slowly.')}`}>
            <textarea id="cr-comments" className="cr-text" rows={3} maxLength={500} value={form.additionalComments} onChange={(e) => set('additionalComments', e.target.value)}
              placeholder={t('Anything else other commuters should know?')} />
          </Field>
        </div>
      </details>
    </section>

    {(form.busName || form.crowdLevel || form.seatAvailability) && <div className="cr-preview" aria-label={t('Preview')}>
      <span className="cr-preview-label"><Eye size={13} /> {t('Other commuters will see')}</span>
      <div className="cr-preview-body">
        <span className="cr-card-icon"><BusFront size={17} /></span>
        <div><b>{form.busName ? t(form.busName) : t('Choose a bus')}</b>
          <small>{stop ? t('At {stop}', { stop: t(stop.name) }) : t('Choose a stop')}{form.travelDirection && route ? ` · ${t('towards {stop}', { stop: t(form.travelDirection === 'FORWARD' ? route.last : route.first) })}` : ''}</small></div>
        <span className="cr-preview-badges"><CrowdBadge level={form.crowdLevel} /><SeatBadge value={form.seatAvailability} count={Number(form.availableSeatCount) || null} /></span>
      </div>
    </div>}

    {error && <p className="cr-form-error" role="alert"><CircleAlert size={15} /> {error}</p>}
    <div className="cr-actions">
      <p className="cr-muted"><ShieldCheck size={14} /> {t('Your name is never shown with your report.')}</p>
      <div>
        {onCancel && <button type="button" className="cr-btn ghost" onClick={onCancel} disabled={busy}>{t('Cancel')}</button>}
        <button type="submit" className="cr-btn primary big" disabled={busy} aria-busy={busy}>
          {busy ? <><Loader2 size={16} className="cr-spin" /> {t('Submitting…')}</> : report ? <><Check size={16} /> {t('Save changes')}</> : <><Send size={16} /> {t('Submit Report')}</>}
        </button>
      </div>
    </div>
  </form>
}

// A tile for one route in the live dashboard: its latest current report.
export function LiveBusTile({ summary: s, now, onPick }) {
  const r = s.latest
  return <button type="button" className={`cr-tile ${CROWD_BY_VALUE[r.crowdLevel]?.tone || ''}`} onClick={() => onPick?.(s)} data-testid="live-tile">
    <span className="cr-tile-top"><b>{t(s.busName)}</b>{s.routeNumber && <em>{s.routeNumber}</em>}</span>
    <span className="cr-tile-badges"><CrowdBadge level={r.crowdLevel} /><SeatBadge value={r.seatAvailability} count={r.availableSeatCount} /></span>
    <small><MapPin size={12} /> {t('At {stop}', { stop: t(r.stopName) })}{r.towardsStop ? ` → ${t(r.towardsStop)}` : ''}</small>
    <small className="cr-tile-time"><Clock3 size={12} /> {agoText(r.observedAt, now)}{s.reportCount > 1 ? ` · ${t('{count} reports', { count: formatNumber(s.reportCount) })}` : ''}</small>
    <span className="cr-tile-go" aria-hidden="true"><ArrowRight size={14} /></span>
  </button>
}
