import React, { useEffect, useId, useRef, useState } from 'react'
import {
  ArrowRight, ArrowUpDown, BadgeCheck, Bike, Car, CarTaxiFront, CheckCircle2, Clock3, Flag, Loader2, MapPin, MessageSquareText, Minus,
  Phone, Plus, ShieldBan, ShieldCheck, Star, UserRound, Users, X,
} from 'lucide-react'
import { formatDate, formatNumber, t } from '../../models/i18n'
import { money } from '../helpers'
import {
  CONTACTS, CONTACT_LABELS, EMPTY_RIDE, GENDERS, GENDER_LABELS, MAX_PARTICIPANTS, REPORT_REASONS, REQUEST_LABELS, STATE_LABELS,
  VEHICLES, VEHICLE_CAPACITY, departureParts, loadStops, mergePlaces, dhakaToday, errorText, formFromRide, formatDeparture, idOf, relativeDeparture, rideApi, rideBody,
  searchPlaces, validateRideForm,
} from '../../models/rides'

export const VEHICLE_ICONS = { Car, Bike, CNG: CarTaxiFront, Other: Car }

export function Avatar({ name, size = 'md', verified }) {
  const initials = (name || '?').trim().split(/\s+/).slice(0, 2).map((w) => w[0]?.toUpperCase()).join('') || '?'
  return <span className={`rs-avatar rs-avatar-${size}`} aria-hidden="true">{initials}{verified && <BadgeCheck className="rs-avatar-badge" size={14} />}</span>
}

export function StatusBadge({ state }) {
  return <span className={`rs-status rs-status-${(state || 'OPEN').toLowerCase()}`}>{t(STATE_LABELS[state] || state)}</span>
}

export function RequestBadge({ request }) {
  const state = request?.state || (request?.status === 'accepted' ? 'APPROVED' : String(request?.status || '').toUpperCase())
  return <span className={`rs-req rs-req-${state.toLowerCase()}`}>{t(REQUEST_LABELS[state] || state)}</span>
}

export function Stars({ value, count, compact }) {
  if (value == null) return <span className="rs-stars rs-stars-none">{compact ? t('New') : t('No reviews yet')}</span>
  return <span className="rs-stars" aria-label={t('{rating} out of 5 from {count} reviews', { rating: value, count: count ?? 0 })}>
    <Star size={13} className="rs-star-on" /> <b>{formatNumber(value)}</b>{count != null && <small>({formatNumber(count)})</small>}
  </span>
}

export function StarInput({ value, onChange, label }) {
  return <div className="rs-star-input" role="radiogroup" aria-label={label}>
    {[1, 2, 3, 4, 5].map((n) => <button type="button" key={n} role="radio" aria-checked={value === n} aria-label={t('{n} stars', { n })}
      className={n <= value ? 'on' : ''} onClick={() => onChange(n)}><Star size={22} /></button>)}
  </div>
}

// A dialog over the page: Escape or a click outside closes it (unless busy), focus starts inside it.
export function Modal({ title, subtitle, onClose, children, footer, wide, busy }) {
  const titleId = useId()
  const box = useRef(null)
  useEffect(() => {
    const onKey = (e) => { if (e.key === 'Escape' && !busy) onClose() }
    document.addEventListener('keydown', onKey)
    box.current?.querySelector('input, select, textarea, button:not(.rs-dialog-close)')?.focus()
    return () => document.removeEventListener('keydown', onKey)
  }, []) // eslint-disable-line react-hooks/exhaustive-deps
  return <div className="modal-backdrop rs-backdrop" onMouseDown={(e) => e.target === e.currentTarget && !busy && onClose()}>
    <div className={`rs-dialog${wide ? ' rs-dialog-wide' : ''}`} role="dialog" aria-modal="true" aria-labelledby={titleId} ref={box}>
      <div className="rs-dialog-head">
        <div><h2 id={titleId}>{title}</h2>{subtitle && <p>{subtitle}</p>}</div>
        <button type="button" className="rs-dialog-close" onClick={onClose} disabled={busy} aria-label={t('Close')}><X size={18} /></button>
      </div>
      <div className="rs-dialog-body">{children}</div>
      {footer && <div className="rs-dialog-foot">{footer}</div>}
    </div>
  </div>
}

// Asks before a destructive action. withReason adds an optional reason box (for cancelling a ride). Once the action
// succeeds the dialog stays open and says so (doneTitle, doneMessage), until the user presses OK.
export function ConfirmDialog({ title, message, confirmLabel, danger, withReason, onConfirm, onClose, doneTitle, doneMessage }) {
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState(false)
  const [error, setError] = useState('')
  const ok = useRef(null)
  useEffect(() => { if (done) ok.current?.focus() }, [done])
  const go = async () => {
    setBusy(true); setError('')
    try { await onConfirm(reason.trim()); setDone(true) } catch (e) { setError(errorText(e)) } finally { setBusy(false) }
  }
  if (done) {
    return <Modal title={doneTitle || t('Done')} onClose={onClose} footer={
      <button type="button" ref={ok} className="button button-green button-small rs-ok" onClick={onClose}>{t('OK')}</button>}>
      <div className="rs-done"><span><CheckCircle2 size={26} /></span><p>{doneMessage || t('Your change was saved.')}</p></div>
    </Modal>
  }
  return <Modal title={title} onClose={onClose} busy={busy} footer={<>
    <button type="button" className="button button-outline button-small" onClick={onClose} disabled={busy}>{t('Keep it')}</button>
    <button type="button" className={`button button-small ${danger ? 'rs-button-danger' : 'button-green'}`} onClick={go} disabled={busy}>{busy && <Loader2 size={14} className="rs-spin" />} {confirmLabel}</button>
  </>}>
    <p className="rs-confirm-text">{message}</p>
    {withReason && <label className="rs-field"><span>{t('Reason (optional)')}</span>
      <textarea value={reason} maxLength={300} rows={3} onChange={(e) => setReason(e.target.value)} placeholder={t('Let your riders know why')} /></label>}
    {error && <p className="rs-form-error" role="alert">{error}</p>}
  </Modal>
}

// A Dhaka place picker: suggestions from the bus stops and the map search, limited to Dhaka City. Typing after a
// choice clears it, so a place is only accepted once it is picked (and has coordinates).
export function LocationField({ label, value, onChange, error, dot, placeholder }) {
  const id = useId()
  const [text, setText] = useState(value?.name || '')
  const [items, setItems] = useState([])
  const [open, setOpen] = useState(false)
  const [loading, setLoading] = useState(false)
  const [active, setActive] = useState(-1)
  const run = useRef(0)
  useEffect(() => { setText(value?.name || '') }, [value?.name])

  const search = (next) => {
    setText(next)
    onChange(next.trim() ? { name: next, lat: null, lng: null } : null)
    const current = ++run.current
    if (!next.trim()) { setItems([]); setOpen(false); setLoading(false); return }
    // Matching stops show at once; map places are added when they arrive.
    searchPlaces(next, {
      isStale: () => current !== run.current,
      onUpdate: (found, pending) => {
        if (current !== run.current) return
        setItems(found); setOpen(true); setActive(found.length ? 0 : -1); setLoading(pending)
      },
    })
  }
  // Picking ends any search still running, so its late answer cannot reopen the list or keep the spinner going.
  const pick = (place) => { run.current++; setLoading(false); setText(place.name); setOpen(false); setItems([]); onChange(place) }
  const onKey = (e) => {
    if (!open || !items.length) return
    if (e.key === 'ArrowDown') { e.preventDefault(); setActive((a) => (a + 1) % items.length) }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setActive((a) => (a - 1 + items.length) % items.length) }
    else if (e.key === 'Enter' && active >= 0) { e.preventDefault(); pick(items[active]) }
    else if (e.key === 'Escape') setOpen(false)
  }
  const picked = value && Number.isFinite(value.lat)
  return <div className={`rs-field rs-location${error ? ' invalid' : ''}`}>
    <label htmlFor={id}>{label}</label>
    <div className="rs-input-box">
      <i className={`rs-dot ${dot}`} aria-hidden="true" />
      <input id={id} role="combobox" aria-expanded={open} aria-controls={`${id}-list`} aria-autocomplete="list" autoComplete="off"
        aria-invalid={Boolean(error) || undefined} value={text} placeholder={placeholder} maxLength={200}
        onChange={(e) => search(e.target.value)} onKeyDown={onKey} onFocus={() => items.length && setOpen(true)} onBlur={() => window.setTimeout(() => setOpen(false), 150)} />
      {loading ? <Loader2 size={15} className="rs-spin rs-input-end" /> : picked && <MapPin size={15} className="rs-input-end rs-picked" aria-label={t('Location confirmed')} />}
    </div>
    {open && <ul className="rs-suggest" id={`${id}-list`} role="listbox">
      {items.length ? items.map((place, i) => <li key={`${place.name}-${i}`} role="option" aria-selected={i === active}
        className={i === active ? 'active' : ''} onMouseDown={(e) => { e.preventDefault(); pick(place) }}>
        <MapPin size={14} /><span><b>{place.name}</b><small>{t(place.kind === 'stop' ? 'Bus stop · Dhaka' : 'Place · Dhaka')}</small></span>
      </li>) : <li className="rs-suggest-empty">{loading ? t('Searching Dhaka places…') : t('No Dhaka place matches. Try a nearby area or bus stop.')}</li>}
    </ul>}
    {error && <em role="alert">{error}</em>}
  </div>
}

// A search box for an area: as the user types it suggests places from the rides already listed (extra), Dhaka bus
// stops and the Dhaka map search. Unlike LocationField the typed text is kept as is, since the ride list matches
// on any part of a place's name.
export function PlaceSearchInput({ id: givenId, label, value, onChange, onPick, placeholder, dot, extra = [] }) {
  const ownId = useId()
  const id = givenId || ownId
  const [items, setItems] = useState([])
  const [open, setOpen] = useState(false)
  const [loading, setLoading] = useState(false)
  const [active, setActive] = useState(-1)
  const run = useRef(0)

  const suggest = (text) => {
    const current = ++run.current
    const q = text.trim().toLowerCase()
    if (!q) { setItems([]); setOpen(false); setLoading(false); return }
    const known = [...new Set(extra.filter(Boolean))].filter((name) => name.toLowerCase().includes(q) || t(name).toLowerCase().includes(q)).slice(0, 4)
      .map((name) => ({ name, kind: 'ride' }))
    setItems(known); setOpen(true); setActive(known.length ? 0 : -1)
    // Listed rides first, then stops at once, then map places when they arrive.
    searchPlaces(text, {
      isStale: () => current !== run.current,
      onUpdate: (found, pending) => {
        if (current !== run.current) return
        const merged = mergePlaces(known, found)
        setItems(merged); setOpen(true); setActive(merged.length ? 0 : -1); setLoading(pending)
      },
    })
  }
  const type = (text) => { onChange(text); suggest(text) }
  const pick = (place) => { run.current++; setLoading(false); onChange(place.name); onPick?.(place.name); setOpen(false); setItems([]) }
  const onKey = (e) => {
    if (!open || !items.length) return
    if (e.key === 'ArrowDown') { e.preventDefault(); setActive((a) => (a + 1) % items.length) }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setActive((a) => (a - 1 + items.length) % items.length) }
    else if (e.key === 'Enter' && active >= 0) { e.preventDefault(); pick(items[active]) }
    else if (e.key === 'Escape') setOpen(false)
  }
  const kindLabel = { ride: 'On a listed ride', stop: 'Bus stop · Dhaka', place: 'Place · Dhaka' }
  return <div className="rs-place">
    <label htmlFor={id}>{label}</label>
    <div className="rs-input-box">
      <i className={`rs-dot ${dot}`} aria-hidden="true" />
      <input id={id} role="combobox" aria-expanded={open} aria-controls={`${id}-list`} aria-autocomplete="list" autoComplete="off"
        value={value} placeholder={placeholder} maxLength={200} onChange={(e) => type(e.target.value)} onKeyDown={onKey}
        onFocus={() => value && suggest(value)} onBlur={() => window.setTimeout(() => setOpen(false), 150)} />
      {loading ? <Loader2 size={15} className="rs-spin rs-input-end" />
        : value && <button type="button" className="rs-input-clear" onClick={() => { onChange(''); onPick?.(''); setItems([]) }} aria-label={t('Clear {field}', { field: label })}><X size={14} /></button>}
    </div>
    {open && <ul className="rs-suggest" id={`${id}-list`} role="listbox">
      {items.length ? items.map((place, i) => <li key={`${place.kind}-${place.name}`} role="option" aria-selected={i === active}
        className={i === active ? 'active' : ''} onMouseDown={(e) => { e.preventDefault(); pick(place) }}>
        <MapPin size={14} /><span><b>{t(place.name)}</b><small>{t(kindLabel[place.kind] || 'Place · Dhaka')}</small></span>
      </li>) : <li className="rs-suggest-empty">{loading ? t('Searching Dhaka places…') : t('No Dhaka place matches. Try a nearby area or bus stop.')}</li>}
    </ul>}
  </div>
}

export function RideRoute({ ride, large }) {
  return <div className={`rs-route${large ? ' rs-route-large' : ''}`}>
    <span className="rs-route-line" aria-hidden="true"><i className="rs-dot start" /><i className="rs-route-track" /><i className="rs-dot end" /></span>
    <span className="rs-route-places">
      <span><small>{t('From')}</small><b>{t(ride.source)}</b></span>
      <span><small>{t('To')}</small><b>{t(ride.destination)}</b></span>
    </span>
  </div>
}

export function SeatMeter({ ride }) {
  const riders = Math.max(0, ride.maxParticipants - 1)
  const taken = riders - (ride.seatsLeft ?? 0)
  return <span className="rs-seats" aria-label={t('{left} of {total} seats available', { left: ride.seatsLeft, total: riders })}>
    {Array.from({ length: Math.min(riders, 9) }, (_, i) => <i key={i} className={i < taken ? 'taken' : ''} />)}
    <b>{ride.seatsLeft === 0 ? t('Full') : t('{n} left', { n: ride.seatsLeft })}</b>
  </span>
}

// One ride in the discovery list: when it leaves, the route, who hosts it, and the price with the next step.
// The buttons follow what the server says the viewer may do (viewer.canRequest…).
export function RideCard({ ride, onOpen, onJoin, onHost, onCancel }) {
  const VehicleIcon = VEHICLE_ICONS[ride.vehicleType] || Car
  const viewer = ride.viewer || {}
  const role = viewer.role
  const host = ride.host || { name: ride.userName }
  const when = departureParts(ride.departureAt)
  return <article className={`rs-card${ride.state && ride.state !== 'OPEN' ? ' rs-card-muted' : ''}`} data-testid="ride-card">
    <div className="rs-card-time">
      {when ? <><small>{when.day}</small><b>{when.time}</b><span>{relativeDeparture(ride.departureAt)}</span></>
        : <><small>{t('Departure')}</small><b className="rs-card-flex">{t('Flexible')}</b><span>{t('Ask the host')}</span></>}
    </div>
    <div className="rs-card-body">
      <div className="rs-card-route">
        <span className="rs-route-line" aria-hidden="true"><i className="rs-dot start" /><i className="rs-route-track" /><i className="rs-dot end" /></span>
        <span className="rs-route-places">
          <span><small className="sr-only">{t('From')}</small><b>{t(ride.source)}</b></span>
          <span><small className="sr-only">{t('To')}</small><b>{t(ride.destination)}</b></span>
        </span>
      </div>
      <div className="rs-card-meta">
        <button type="button" className="rs-host" onClick={() => onHost?.(host)} aria-label={t('View host profile of {name}', { name: host.name })}>
          <Avatar name={host.name} size="sm" verified={host.verified} />
          <b>{host.name || t('Dhaka commuter')}</b>
          <Stars value={host.rating} count={host.reviewCount} compact />
        </button>
        <span className="rs-card-tags">
          <span><VehicleIcon size={13} /> {t(ride.vehicleType || 'Other')}</span>
          <span><UserRound size={13} /> {t(GENDER_LABELS[ride.genderPreference] || 'Any gender')}</span>
          {ride.distance ? <span><MapPin size={13} /> {t('~{km} km', { km: formatNumber(ride.distance) })}</span> : null}
        </span>
      </div>
    </div>
    <div className="rs-card-side">
      <div className="rs-card-price">
        <StatusBadge state={ride.state} />
        {ride.costPerPerson == null ? <b className="rs-card-flex">{t('Ask the host')}</b> : <b>{money(ride.costPerPerson)}</b>}
        <small>{t('per person')}</small>
      </div>
      <SeatMeter ride={ride} />
      <div className="rs-card-actions">
        <button type="button" className="button button-outline button-small" onClick={() => onOpen(ride)}>{t('View details')}</button>
        {role === 'host' ? (viewer.canManage && onCancel
          ? <button type="button" className="button button-outline button-small rs-danger-text" onClick={() => onCancel(ride)}><X size={14} /> {t('Cancel ride')}</button>
          : <span className="rs-mine">{t('Your ride')}</span>)
          : role === 'approved' ? <RequestBadge request={{ state: 'APPROVED' }} />
            : role === 'pending' ? <RequestBadge request={{ state: 'PENDING' }} />
              : (viewer.canRequest || viewer.cannotRequestReason === 'signin') && ride.state === 'OPEN'
                ? <button type="button" className="button button-green button-small" onClick={() => onJoin(ride)}>{t('Request to join')} <ArrowRight size={14} /></button>
                : null}
      </div>
    </div>
  </article>
}

export function RideCardSkeleton() {
  return <div className="rs-card rs-skeleton" aria-hidden="true">
    <div className="rs-card-time"><i className="w60" /><i className="w90 tall-s" /></div>
    <div className="rs-card-body"><i className="w60" /><i className="w40" /><i className="w75" /></div>
    <div className="rs-card-side"><i className="w60 tall-s" /><i className="w90" /></div>
  </div>
}

// ---- Create and edit ----

function Stepper({ value, min, max, onChange, label }) {
  const n = Number(value) || min
  return <div className="rs-stepper" role="group" aria-label={label}>
    <button type="button" onClick={() => onChange(String(Math.max(min, n - 1)))} disabled={n <= min} aria-label={t('Fewer')}><Minus size={15} /></button>
    <input type="number" inputMode="numeric" min={min} max={max} value={value} onChange={(e) => onChange(e.target.value)} aria-label={label} />
    <button type="button" onClick={() => onChange(String(Math.min(max, n + 1)))} disabled={n >= max} aria-label={t('More')}><Plus size={15} /></button>
  </div>
}

function Segmented({ options, value, onChange, label, labels = {}, icons = {} }) {
  return <div className="rs-segmented" role="radiogroup" aria-label={label}>
    {options.map((option) => { const Icon = icons[option]; return <button type="button" role="radio" aria-checked={value === option} key={option}
      className={value === option ? 'on' : ''} onClick={() => onChange(option)}>{Icon && <Icon size={15} />}{t(labels[option] || option)}</button> })}
  </div>
}

export function RideFormModal({ ride, token, onSaved, onClose }) {
  const editing = Boolean(ride)
  const [form, setForm] = useState(() => editing ? formFromRide(ride) : { ...EMPTY_RIDE, date: dhakaToday() })
  // Fetch the Dhaka stops as the form opens, so the first suggestions are instant.
  useEffect(() => { loadStops().catch(() => {}) }, [])
  const [errors, setErrors] = useState({})
  const [serverError, setServerError] = useState('')
  const [saving, setSaving] = useState(false)
  const set = (field) => (value) => { setForm((f) => ({ ...f, [field]: value })); setErrors((e) => ({ ...e, [field]: undefined })); setServerError('') }
  const setVehicle = (vehicleType) => setForm((f) => {
    const cap = VEHICLE_CAPACITY[vehicleType]
    return { ...f, vehicleType, maxParticipants: Number(f.maxParticipants) > cap ? String(cap) : f.maxParticipants }
  })
  const cap = VEHICLE_CAPACITY[form.vehicleType] || MAX_PARTICIPANTS
  const swap = () => setForm((f) => ({ ...f, source: f.destination, destination: f.source }))

  const submit = async (event) => {
    event.preventDefault()
    if (saving) return
    const found = validateRideForm(form)
    setErrors(found)
    if (Object.keys(found).length) return
    setSaving(true)
    try {
      const data = editing ? await rideApi.update(idOf(ride), rideBody(form), token) : await rideApi.create(rideBody(form), token)
      onSaved(data.data || data.ridePost, editing)
    } catch (e) { setServerError(errorText(e)); setSaving(false) }
  }
  const field = (name, label, control, hint) => <label className={`rs-field${errors[name] ? ' invalid' : ''}`}>
    <span>{label}</span>{control}{hint && !errors[name] && <small>{hint}</small>}{errors[name] && <em role="alert">{errors[name]}</em>}
  </label>

  return <Modal wide title={editing ? t('Edit ride') : t('Offer a ride')} subtitle={t('Dhaka City only. Riders see this before they ask to join.')} onClose={onClose} busy={saving}
    footer={<>
      <button type="button" className="button button-outline" onClick={onClose} disabled={saving}>{t('Cancel')}</button>
      <button type="submit" form="rs-ride-form" className="button button-green" disabled={saving}>{saving && <Loader2 size={15} className="rs-spin" />} {editing ? t('Save changes') : t('Publish ride')}</button>
    </>}>
    <form id="rs-ride-form" className="rs-form" onSubmit={submit} noValidate>
      {serverError && <p className="rs-form-error" role="alert">{serverError}</p>}
      <section className="rs-form-section">
        <h3>{t('Route')}</h3>
        <div className="rs-route-fields">
          <LocationField label={t('Source location')} dot="start" value={form.source} onChange={set('source')} error={errors.source} placeholder={t('Search a Dhaka area or bus stop')} />
          <button type="button" className="rs-swap" onClick={swap} aria-label={t('Swap source and destination')}><ArrowUpDown size={15} /></button>
          <LocationField label={t('Destination')} dot="end" value={form.destination} onChange={set('destination')} error={errors.destination} placeholder={t('Where are you going?')} />
        </div>
      </section>
      <section className="rs-form-section rs-grid-2">
        <h3>{t('Departure')}</h3>
        {field('date', t('Departure date'), <input type="date" value={form.date} min={dhakaToday()} onChange={(e) => set('date')(e.target.value)} />)}
        {field('time', t('Departure time'), <input type="time" value={form.time} onChange={(e) => set('time')(e.target.value)} />, t('Bangladesh time (GMT+6)'))}
      </section>
      <section className="rs-form-section">
        <h3>{t('Ride')}</h3>
        <div className={`rs-field${errors.vehicleType ? ' invalid' : ''}`}><span>{t('Vehicle type')}</span>
          <Segmented label={t('Vehicle type')} options={VEHICLES} value={form.vehicleType} onChange={setVehicle} icons={VEHICLE_ICONS} /></div>
        <div className="rs-grid-2">
          <div className={`rs-field${errors.maxParticipants ? ' invalid' : ''}`}><span>{t('Maximum participants')}</span>
            <Stepper label={t('Maximum participants')} value={form.maxParticipants} min={2} max={cap} onChange={set('maxParticipants')} />
            {errors.maxParticipants ? <em role="alert">{errors.maxParticipants}</em> : <small>{t('Counting you · {n} seats for riders', { n: Math.max(0, Number(form.maxParticipants) - 1 || 0) })}</small>}
          </div>
          {field('costPerPerson', t('Estimated cost per person'), <span className="rs-money"><b>৳</b><input inputMode="decimal" value={form.costPerPerson} onChange={(e) => set('costPerPerson')(e.target.value)} placeholder="80" /></span>, t('0 if it is free'))}
        </div>
        <div className="rs-field"><span>{t('Gender preference')}</span>
          <Segmented label={t('Gender preference')} options={GENDERS} value={form.genderPreference} onChange={set('genderPreference')} labels={GENDER_LABELS} /></div>
      </section>
      <section className="rs-form-section">
        <h3>{t('Details')} <small>{t('optional')}</small></h3>
        {field('pickupDetails', t('Pickup details'), <input value={form.pickupDetails} maxLength={300} onChange={(e) => set('pickupDetails')(e.target.value)} placeholder={t('e.g. In front of Gate 2, Mirpur 10 roundabout')} />, t('Only approved riders see this.'))}
        <div className="rs-field"><span>{t('Contact preference')}</span>
          <Segmented label={t('Contact preference')} options={CONTACTS} value={form.contactPreference} onChange={set('contactPreference')} labels={CONTACT_LABELS} />
          <small>{t('Your phone number is shown only to riders you approve.')}</small></div>
        {field('instructions', t('Additional instructions'), <textarea rows={3} value={form.instructions} maxLength={500} onChange={(e) => set('instructions')(e.target.value)} placeholder={t('e.g. AC car, no smoking, one small bag each')} />)}
      </section>
    </form>
  </Modal>
}

// ---- Joining ----

export function JoinRequestModal({ ride, token, onSent, onClose }) {
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const send = async () => {
    setBusy(true); setError('')
    try { const data = await rideApi.join(idOf(ride), note.trim(), token); onSent(data.data) } catch (e) { setError(errorText(e)); setBusy(false) }
  }
  return <Modal title={t('Request to join this ride?')} subtitle={t('The host decides. You will be notified when they answer.')} onClose={onClose} busy={busy}
    footer={<>
      <button type="button" className="button button-outline button-small" onClick={onClose} disabled={busy}>{t('Cancel')}</button>
      <button type="button" className="button button-green button-small" onClick={send} disabled={busy}>{busy && <Loader2 size={14} className="rs-spin" />} {t('Send request')}</button>
    </>}>
    <div className="rs-join-summary">
      <RideRoute ride={ride} />
      <dl>
        <div><dt>{t('Departure')}</dt><dd>{formatDeparture(ride.departureAt)}</dd></div>
        <div><dt>{t('Cost per person')}</dt><dd>{ride.costPerPerson == null ? '—' : money(ride.costPerPerson)}</dd></div>
        <div><dt>{t('Seats left')}</dt><dd>{formatNumber(ride.seatsLeft)}</dd></div>
        <div><dt>{t('Host')}</dt><dd>{ride.host?.name}</dd></div>
      </dl>
    </div>
    <label className="rs-field"><span>{t('Note to the host (optional)')}</span>
      <textarea rows={2} maxLength={200} value={note} onChange={(e) => setNote(e.target.value)} placeholder={t('e.g. I will wait at the main gate')} /></label>
    {error && <p className="rs-form-error" role="alert">{error}</p>}
  </Modal>
}

// ---- People ----

export function ReportModal({ ride, user, token, onClose, notify }) {
  const [reason, setReason] = useState('')
  const [description, setDescription] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const send = async () => {
    if (!reason) { setError(t('Choose a reason.')); return }
    if (reason === 'other' && !description.trim()) { setError(t('Please describe the problem.')); return }
    setBusy(true); setError('')
    try {
      const body = { reason, description: description.trim() || null }
      const data = ride ? await rideApi.reportRide(idOf(ride), body, token) : await rideApi.reportUser(user.id, body, token)
      notify(t(data.message || 'Thanks. An admin will review your report.'))
      onClose()
    } catch (e) { setError(errorText(e)); setBusy(false) }
  }
  return <Modal title={ride ? t('Report this ride') : t('Report {name}', { name: user?.name })} subtitle={t('Reports go to the SmartCommute admins. The person is not told who reported them.')} onClose={onClose} busy={busy}
    footer={<>
      <button type="button" className="button button-outline button-small" onClick={onClose} disabled={busy}>{t('Cancel')}</button>
      <button type="button" className="button button-small rs-button-danger" onClick={send} disabled={busy}>{busy && <Loader2 size={14} className="rs-spin" />} <Flag size={14} /> {t('Send report')}</button>
    </>}>
    <div className="rs-reasons" role="radiogroup" aria-label={t('Reason')}>
      {REPORT_REASONS.map(([value, label]) => <label key={value} className={reason === value ? 'on' : ''}>
        <input type="radio" name="report-reason" value={value} checked={reason === value} onChange={() => setReason(value)} /> {t(label)}</label>)}
    </div>
    <label className="rs-field"><span>{t('What happened?')}{reason !== 'other' && <small> · {t('optional')}</small>}</span>
      <textarea rows={3} maxLength={1000} value={description} onChange={(e) => setDescription(e.target.value)} /></label>
    {error && <p className="rs-form-error" role="alert">{error}</p>}
  </Modal>
}

// actions: optional buttons shown under the profile (the host's Accept / Reject for a request).
export function HostProfileModal({ userId, token, signedIn, notify, onClose, actions }) {
  const [state, setState] = useState({ status: 'loading' })
  const [reporting, setReporting] = useState(false)
  const [busy, setBusy] = useState(false)
  const load = () => rideApi.profile(userId, token).then((d) => setState({ status: 'ready', profile: d.data })).catch((e) => setState({ status: 'error', message: errorText(e) }))
  useEffect(() => { load() }, [userId]) // eslint-disable-line react-hooks/exhaustive-deps
  const p = state.profile
  const toggleBlock = async () => {
    setBusy(true)
    try { const d = p.blockedByMe ? await rideApi.unblock(userId, token) : await rideApi.block(userId, token); notify(t(d.message)); await load() }
    catch (e) { notify(errorText(e)) } finally { setBusy(false) }
  }
  if (reporting) return <ReportModal user={{ id: userId, name: p?.name }} token={token} notify={notify} onClose={() => setReporting(false)} />
  return <Modal title={t('Rider profile')} onClose={onClose} wide>
    {state.status === 'loading' ? <div className="rs-center"><Loader2 className="rs-spin" /> {t('Loading profile…')}</div>
      : state.status === 'error' ? <p className="rs-form-error">{state.message}</p>
        : <div className="rs-profile">
          <div className="rs-profile-head">
            <Avatar name={p.name} size="lg" verified={p.verified} />
            <div><h3>{p.name}{p.verified && <BadgeCheck size={16} className="rs-verified" aria-label={t('Verified')} />}</h3>
              <p>{[p.gender && p.gender !== 'Prefer not to say' ? t(p.gender) : null, t('Member since {date}', { date: formatDate(p.memberSince, { month: 'short', year: 'numeric' }) })].filter(Boolean).join(' · ')}</p></div>
          </div>
          {(p.identityVerified || p.verified) && <div className="rs-profile-badges">
            {p.identityVerified && <span><ShieldCheck size={13} /> {t('NID verified')}</span>}
            {p.verified && <span><BadgeCheck size={13} /> {t('Verified student')}</span>}
          </div>}
          {p.bio && <p className="rs-profile-bio">{p.bio}</p>}
          <div className="rs-profile-stats">
            <span><b>{p.rating == null ? '—' : formatNumber(p.rating)}</b><small>{t('Rating')}</small></span>
            <span><b>{formatNumber(p.reviewCount)}</b><small>{t('Reviews')}</small></span>
            <span><b>{formatNumber(p.completedRides)}</b><small>{t('Completed rides')}</small></span>
            <span><b>{formatNumber(p.hostedRides)}</b><small>{t('Rides hosted')}</small></span>
          </div>
          <h4>{t('Reviews')}</h4>
          {p.reviews.length ? <ul className="rs-review-list">{p.reviews.map((r) => <li key={r._id}>
            <div><Avatar name={r.reviewerName} size="sm" /><b>{r.reviewerName}</b><span className="rs-review-stars">{'★'.repeat(r.rating)}<i>{'★'.repeat(5 - r.rating)}</i></span></div>
            {r.comment && <p>{r.comment}</p>}
            <small>{r.rideSource && `${t(r.rideSource)} → ${t(r.rideDestination)} · `}{formatDate(r.createdAt)}</small>
          </li>)}</ul> : <p className="rs-muted">{t('No reviews yet.')}</p>}
          {actions && <div className="rs-profile-decide">{actions}</div>}
          {signedIn && !p.isMe && <div className="rs-profile-actions">
            <button type="button" className="button button-outline button-small" onClick={() => setReporting(true)}><Flag size={14} /> {t('Report')}</button>
            <button type="button" className="button button-outline button-small" onClick={toggleBlock} disabled={busy}><ShieldBan size={14} /> {p.blockedByMe ? t('Unblock') : t('Block')}</button>
          </div>}
        </div>}
  </Modal>
}

// The WhatsApp logo shape (a speech bubble with a phone), drawn here so no brand icon package is needed.
function WhatsAppIcon({ size = 16 }) {
  return <svg width={size} height={size} viewBox="0 0 24 24" aria-hidden="true" fill="currentColor">
    <path d="M12 2a10 10 0 0 0-8.6 15.1L2 22l5-1.3A10 10 0 1 0 12 2Zm0 18.2c-1.5 0-3-.4-4.3-1.2l-.3-.2-3 .8.8-2.9-.2-.3A8.2 8.2 0 1 1 12 20.2Zm4.5-6.1c-.2-.1-1.5-.7-1.7-.8-.2-.1-.4-.1-.6.1l-.8 1c-.1.2-.3.2-.5.1a6.7 6.7 0 0 1-3.3-2.9c-.2-.4.2-.4.7-1.3.1-.2 0-.3 0-.4l-.8-1.8c-.2-.5-.4-.4-.6-.4h-.5c-.2 0-.4.1-.7.3-.2.3-.9.9-.9 2.2s.9 2.5 1 2.7c.1.2 1.8 2.8 4.4 3.9 1.6.7 2.3.8 3.1.6.5-.1 1.5-.6 1.7-1.2.2-.6.2-1.1.2-1.2-.1-.1-.3-.2-.5-.3Z" />
  </svg>
}

// Opens a real WhatsApp chat (wa.me) with the ride written in the first message. Without a number it says plainly
// that the person has no WhatsApp: the number is the one they saved on their profile as their WhatsApp.
export function WhatsAppButton({ number, name, ride, compact }) {
  // In lists (compact) a missing number is said in the person's subtitle (see WhatsAppNote), so nothing is drawn here.
  if (!number && compact) return null
  if (!number) {
    return <span className={`rs-wa-none${compact ? ' compact' : ''}`} title={t('{name} has not added a WhatsApp number.', { name: name || t('This person') })}>
      <WhatsAppIcon size={14} /> {t('This number has no WhatsApp')}</span>
  }
  const when = ride?.departureAt ? ` (${formatDeparture(ride.departureAt)})` : ''
  const text = ride ? t('Hi {name}, this is about our SmartCommute ride from {source} to {destination}{when}.', { name: name || '', source: t(ride.source), destination: t(ride.destination), when }) : ''
  const href = `https://wa.me/${String(number).replace(/\D/g, '')}${text ? `?text=${encodeURIComponent(text)}` : ''}`
  const label = t('Message {name} on WhatsApp', { name: name || '' })
  return <a className={`rs-wa${compact ? ' compact' : ''}`} href={href} target="_blank" rel="noopener noreferrer" aria-label={label} title={label}>
    <WhatsAppIcon size={compact ? 17 : 16} />{!compact && ` ${t('Message on WhatsApp')}`}</a>
}

// " · No WhatsApp" after a person's role in a list, when the viewer may contact them but they saved no number.
export function WhatsAppNote({ visible, number }) {
  return visible && !number ? <span className="rs-wa-tag"> · {t('No WhatsApp')}</span> : null
}

// How an approved rider reaches the host outside the app: WhatsApp and phone, as the host allowed.
export function ContactCard({ ride }) {
  const contact = ride.contact
  if (!contact) return null
  if (contact.chatOnly) return <p className="rs-contact-note">{t('The host prefers the in-app ride chat.')}</p>
  return <div className="rs-contact-row">
    <WhatsAppButton number={contact.whatsApp} name={ride.host?.name} ride={ride} />
    {contact.phone && <a className="rs-contact" href={`tel:${contact.phone}`}><Phone size={15} /> {contact.phone}</a>}
  </div>
}

export const ChatIcon = MessageSquareText
