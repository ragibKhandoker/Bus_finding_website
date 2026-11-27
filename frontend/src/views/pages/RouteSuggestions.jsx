import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  Calendar, Check, CheckCircle2, CircleAlert, Clock3, Eye, ImagePlus, Info, Loader2, MapPin, MapPinPlus, MessageSquareText, PencilLine, Plus,
  RefreshCw, Route as RouteIcon, Send, ShieldCheck, Trash2, X, XCircle,
} from 'lucide-react'
import { useBusRoutes } from '../../controllers/useBusRoutes'
import { formatNumber, t } from '../../models/i18n'
import { readImage } from '../../models/lostFound'
import {
  BUS_TYPES, BUS_TYPE_LABELS, CORRECTION_FIELDS, CORRECTION_LABELS, STATUSES, STATUS_LABELS, STOP_FIELDS, TYPES, TYPE_HELP, TYPE_LABELS,
  day, emptySuggestion, suggestionApi, suggestionPlace, suggestionProblems, toForm, toSuggestionBody,
} from '../../models/routeSuggestions'
import { CpModal, EvidenceGallery } from '../components/ComplaintUI'
import { BusSearch } from './Complaints'
import { RequireSignIn } from './MorePages'
import '../styles/route-suggestions.css'

// F20 Suggest a Bus Route or Stop: commuters send a missing route, a missing stop or a correction; an admin checks
// it before anything in the official bus data changes. The user follows each suggestion under My Suggestions.

const errorText = (error) => t(error?.message || 'Something went wrong. Please try again.')
const clean = (text) => (text || '').trim().replace(/\s+/g, ' ').toLowerCase()

const STATUS_ICONS = { PENDING: Clock3, APPROVED: CheckCircle2, REJECTED: XCircle }
// The complaint page's colours: amber for waiting, green for done, red for refused.
const STATUS_CLASS = { PENDING: 'pending', APPROVED: 'resolved', REJECTED: 'rejected' }
const TYPE_ICON_COMPONENTS = { NEW_ROUTE: RouteIcon, NEW_STOP: MapPinPlus, INCORRECT_INFORMATION: PencilLine }

export function TypeIcon({ type, size = 18 }) {
  const Icon = TYPE_ICON_COMPONENTS[type] || Info
  return <Icon size={size} aria-hidden="true" />
}

export function SuggestionStatusBadge({ status, large = false }) {
  const Icon = STATUS_ICONS[status] || Clock3
  return <span className={`cp-status ${STATUS_CLASS[status] || ''}${large ? ' large' : ''}`}><Icon size={large ? 15 : 13} /> {t(STATUS_LABELS[status] || status)}</span>
}

function Field({ label, htmlFor, error, hint, optional = false, children, wide = false }) {
  return <div className={`sug-field${error ? ' has-error' : ''}${wide ? ' wide' : ''}`}>
    <label htmlFor={htmlFor}>{label}{optional && <span className="sug-optional">{t('Optional')}</span>}</label>
    {children}
    {error ? <small className="sug-error" role="alert"><CircleAlert size={13} /> {error}</small> : hint ? <small className="sug-hint">{hint}</small> : null}
  </div>
}

// One part of the form: what it is about on the left, its fields on the right.
function Section({ title, description, children }) {
  return <section className="sug-section">
    <div className="sug-section-head"><h3>{title}</h3>{description && <p>{description}</p>}</div>
    <div className="sug-section-body">{children}</div>
  </section>
}

// Every Dhaka City bus by name with its routes and their stops (with ids and positions), and every stop name.
export function useSuggestionBuses() {
  const [routes] = useBusRoutes()
  return useMemo(() => {
    const byName = new Map()
    const stops = new Map()
    for (const b of routes.status === 'ready' ? routes.buses : []) {
      for (const s of b.stops || []) if (s.name && !stops.has(clean(s.name))) stops.set(clean(s.name), s.name)
      if (b.isDhakaCity === false) continue
      const name = (b.busName || '').trim()
      if (!name || !(b._id || b.id)) continue
      const stopList = (b.stops || []).map((s) => ({ id: s.id, name: s.name, lat: s.lat, lng: s.lng }))
      const entry = byName.get(name) || { name, routes: [] }
      entry.routes.push({
        id: b._id || b.id, number: b.routeNumber, busType: b.busType, stops: stopList.map((s) => s.name), stopList,
        label: `${b.routeNumber ? `${t('Route {number}', { number: b.routeNumber })}: ` : ''}${stopList.length ? `${t(stopList[0].name)} → ${t(stopList[stopList.length - 1].name)}` : ''}`,
      })
      byName.set(name, entry)
    }
    return {
      buses: [...byName.values()].sort((a, b) => a.name.localeCompare(b.name)),
      status: routes.status,
      stopNames: [...stops.values()].sort((a, b) => a.localeCompare(b)),
      knownStop: (name) => stops.has(clean(name)),
    }
  }, [routes])
}

// One stop of a new route on the stop timeline. Known stops are suggested while typing; a new place needs only its name.
function StopInput({ id, label, kind, value, onName, onRemove, error, knownStop }) {
  const known = knownStop(value)
  return <li className={`sug-stop ${kind}${error ? ' has-error' : ''}`}>
    <span className="sug-stop-dot" aria-hidden="true" />
    <div className="sug-stop-body">
      <label htmlFor={id}>{label}</label>
      <div className="sug-stop-line">
        <input id={id} className="cp-text" list="sug-known-stops" value={value} maxLength={100} onChange={(e) => onName(e.target.value)} placeholder={t('Search or type a stop name')} />
        {onRemove && <button type="button" className="sug-icon-button" onClick={onRemove} aria-label={t('Remove {name}', { name: value || label })}><X size={15} /></button>}
      </div>
      {error ? <small className="sug-error" role="alert"><CircleAlert size={13} /> {error}</small>
        : value.trim().length > 1 && (known ? <small className="sug-hint sug-known"><Check size={12} /> {t('Existing stop')}</small>
          : <small className="sug-hint">{t('New place. Mention where it is in the description.')}</small>)}
    </div>
  </li>
}

// ---- The form ----

const DETAIL_TEXT = {
  NEW_ROUTE: ['Route details', 'The bus operator and every stop from the first to the last, in order.'],
  NEW_STOP: ['Stop details', 'The route the stop belongs to and where the bus stops.'],
  INCORRECT_INFORMATION: ['Correction', 'Pick the route and the detail that is wrong, then give the correct value.'],
}

// suggestion: when given, the form edits that (pending) suggestion instead of sending a new one.
export function RouteSuggestionForm({ token, onSubmitted, suggestion = null, onCancel }) {
  const editing = Boolean(suggestion)
  const { buses, status, stopNames, knownStop } = useSuggestionBuses()
  const [form, setForm] = useState(() => (suggestion ? toForm(suggestion) : emptySuggestion()))
  const [problems, setProblems] = useState({})
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [imageError, setImageError] = useState('')
  const fileInput = useRef(null)
  const bus = buses.find((b) => b.name === form.busName)
  const route = bus?.routes.find((r) => r.id === form.busId)
  const stop = route?.stopList.find((s) => s.id === form.relatedBusStopId)

  const set = (key, value) => { setForm((f) => ({ ...f, [key]: value })); setProblems((p) => ({ ...p, [key]: undefined })); setError('') }
  const setMany = (values, keys = Object.keys(values)) => {
    setForm((f) => ({ ...f, ...values }))
    setProblems((p) => ({ ...p, ...Object.fromEntries(keys.map((k) => [k, undefined])) }))
    setError('')
  }
  const chooseType = (type) => { setForm((f) => ({ ...emptySuggestion(type), title: f.title, description: f.description, image: f.image, hasPhoto: f.hasPhoto, removePhoto: f.removePhoto })); setProblems({}); setError('') }
  const pickBus = (b) => setMany({ busName: b?.name || '', busId: b?.routes.length === 1 ? b.routes[0].id : '', relatedBusStopId: '', correctedValue: '' }, ['busName', 'busId'])
  const setStop = (i, change) => {
    setForm((f) => ({ ...f, middleStops: f.middleStops.map((s, n) => (n === i ? { ...s, ...change } : s)) }))
    setProblems((p) => ({ ...p, [`stop-${i}`]: undefined }))
  }

  const addImage = async (event) => {
    const file = event.target.files?.[0]
    event.target.value = ''
    setImageError('')
    try { const data = await readImage(file); setForm((f) => ({ ...f, image: { data, name: file.name } })) } catch (e) { setImageError(e.message) }
  }

  const reset = () => { setForm(emptySuggestion(form.type)); setProblems({}); setError(''); setImageError('') }

  const submit = async (event) => {
    event.preventDefault()
    if (busy) return
    const found = suggestionProblems(form)
    setProblems(found)
    if (Object.keys(found).length) { setError(t('Please fix the highlighted fields.')); return }
    setBusy(true); setError('')
    try {
      const body = toSuggestionBody(form)
      const result = editing ? await suggestionApi.update(suggestion.id, body, token) : await suggestionApi.create(body, token)
      if (!editing) setForm(emptySuggestion(form.type))
      setProblems({})
      onSubmitted(result)
    } catch (e) { setError(errorText(e)) } finally { setBusy(false) }
  }

  const busPicker = <div className="sug-grid">
    <Field label={t('Bus name')} htmlFor="sug-bus" error={problems.busName} hint={t('Only Dhaka city buses are listed.')}>
      <BusSearch id="sug-bus" buses={buses} status={status} value={form.busName} onPick={pickBus} invalid={Boolean(problems.busName)} />
    </Field>
    <Field label={t('Bus route')} htmlFor="sug-route" error={problems.busId}>
      <select id="sug-route" className="cp-text" value={form.busId} disabled={!bus} onChange={(e) => setMany({ busId: e.target.value, relatedBusStopId: '' }, ['busId'])}>
        <option value="">{bus ? t('Choose the route') : t('Choose a bus first')}</option>
        {bus?.routes.map((r) => <option key={r.id} value={r.id}>{r.label || t('Route')}</option>)}
      </select>
    </Field>
  </div>

  const current = form.correctionField === 'BUS_NAME' ? form.busName
    : form.correctionField === 'ROUTE_NUMBER' ? route?.number
      : form.correctionField === 'BUS_TYPE' ? route?.busType && t(BUS_TYPE_LABELS[route.busType] || route.busType)
        : form.correctionField === 'STOP_NAME' ? stop?.name
          : form.correctionField === 'STOP_LOCATION' && stop ? `${stop.lat?.toFixed?.(5)}, ${stop.lng?.toFixed?.(5)}` : ''
  const [detailTitle, detailText] = DETAIL_TEXT[form.type]

  return <form className="sug-form" onSubmit={submit} noValidate aria-label={editing ? t('Edit suggestion') : t('New suggestion')}>
    <datalist id="sug-known-stops">{stopNames.map((s) => <option key={s} value={s} />)}</datalist>

    <Section title={t('Suggestion type')} description={t('What would you like to tell us about?')}>
      <div className="sug-types" role="radiogroup" aria-label={t('Suggestion type')}>
        {TYPES.map((type) => <label key={type} className={`sug-type${form.type === type ? ' selected' : ''}`}>
          <input type="radio" name="sug-type" value={type} checked={form.type === type} onChange={() => chooseType(type)} />
          <span className="sug-type-icon"><TypeIcon type={type} /></span>
          <span className="sug-type-text"><b>{t(TYPE_LABELS[type])}</b><small>{t(TYPE_HELP[type])}</small></span>
          <span className="sug-radio" aria-hidden="true" />
        </label>)}
      </div>
    </Section>

    <Section title={t(detailTitle)} description={t(detailText)}>
      {form.type === 'NEW_ROUTE' && <>
        <div className="sug-grid sug-grid-3">
          <Field label={t('Bus name')} htmlFor="sug-route-name" error={problems.routeName}>
            <input id="sug-route-name" className="cp-text" value={form.routeName} maxLength={100} onChange={(e) => set('routeName', e.target.value)} placeholder={t('e.g. Raida Paribahan')} />
          </Field>
          <Field label={t('Route number')} htmlFor="sug-route-number" error={problems.routeNumber} optional>
            <input id="sug-route-number" className="cp-text" value={form.routeNumber} maxLength={20} onChange={(e) => set('routeNumber', e.target.value)} placeholder={t('e.g. 36')} />
          </Field>
          <Field label={t('Bus type')} htmlFor="sug-bus-type">
            <select id="sug-bus-type" className="cp-text" value={form.busType} onChange={(e) => set('busType', e.target.value)}>
              {BUS_TYPES.map((x) => <option key={x} value={x}>{t(BUS_TYPE_LABELS[x])}</option>)}
            </select>
          </Field>
        </div>
        <div className="sug-field wide">
          <span className="sug-label">{t('Stops')}</span>
          <ol className="sug-timeline">
            <StopInput id="sug-start" kind="start" label={t('Start location')} value={form.startLocation} knownStop={knownStop} error={problems.startLocation}
              onName={(v) => set('startLocation', v)} />
            {form.middleStops.map((s, i) => <StopInput key={i} id={`sug-stop-${i}`} kind="middle" label={t('Stop {n}', { n: formatNumber(i + 2) })} value={s.name} knownStop={knownStop}
              error={problems[`stop-${i}`]} onName={(v) => setStop(i, { name: v })}
              onRemove={() => setForm((f) => ({ ...f, middleStops: f.middleStops.filter((_, n) => n !== i) }))} />)}
            <li className="sug-stop add">
              <span className="sug-stop-dot" aria-hidden="true" />
              <button type="button" className="sug-ghost" onClick={() => setForm((f) => ({ ...f, middleStops: [...f.middleStops, { name: '' }] }))}>
                <Plus size={15} /> {t('Add a stop in between')}</button>
            </li>
            <StopInput id="sug-end" kind="end" label={t('End location')} value={form.endLocation} knownStop={knownStop} error={problems.endLocation}
              onName={(v) => set('endLocation', v)} />
          </ol>
          {problems.middleStops ? <small className="sug-error" role="alert">{problems.middleStops}</small>
            : <small className="sug-hint">{t('Pick existing stops from the suggestions where you can. Our team confirms the exact location of any new place.')}</small>}
        </div>
      </>}

      {form.type === 'NEW_STOP' && <>
        {busPicker}
        <Field label={t('Bus stop name')} htmlFor="sug-stop-name" error={problems.busStopName} wide>
          <input id="sug-stop-name" className="cp-text" value={form.busStopName} maxLength={100} onChange={(e) => set('busStopName', e.target.value)} placeholder={t('e.g. Bijoy Sarani')} />
        </Field>
        <Field label={t('Where is the stop?')} htmlFor="sug-where" error={problems.locationName} wide hint={t('A landmark or the stops it is between.')}>
          <input id="sug-where" className="cp-text" value={form.locationName} maxLength={200} onChange={(e) => set('locationName', e.target.value)}
            placeholder={t('e.g. In front of the Bijoy Sarani signal, between Farmgate and Jahangir Gate')} />
        </Field>
      </>}

      {form.type === 'INCORRECT_INFORMATION' && <>
        {busPicker}
        <div className="sug-grid">
          <Field label={t('What is wrong?')} htmlFor="sug-field" error={problems.correctionField}>
            <select id="sug-field" className="cp-text" value={form.correctionField} disabled={!route}
              onChange={(e) => setMany({ correctionField: e.target.value, correctedValue: '', relatedBusStopId: STOP_FIELDS.includes(e.target.value) ? form.relatedBusStopId : '' }, ['correctionField', 'correctedValue'])}>
              <option value="">{route ? t('Choose what is wrong') : t('Choose a route first')}</option>
              {CORRECTION_FIELDS.map((f) => <option key={f} value={f}>{t(CORRECTION_LABELS[f])}</option>)}
            </select>
          </Field>
          {STOP_FIELDS.includes(form.correctionField) && <Field label={t('Which stop?')} htmlFor="sug-wrong-stop" error={problems.relatedBusStopId}>
            <select id="sug-wrong-stop" className="cp-text" value={form.relatedBusStopId} onChange={(e) => set('relatedBusStopId', e.target.value)}>
              <option value="">{t('Choose the stop')}</option>
              {route?.stopList.filter((s) => s.id).map((s) => <option key={s.id} value={s.id}>{t(s.name)}</option>)}
            </select>
          </Field>}
        </div>
        {form.correctionField && current ? <div className="sug-compare">
          <p className="sug-current"><span>{t('Shown now')}</span>{current}</p>
          <span className="sug-compare-arrow" aria-hidden="true">→</span>
          <p className="sug-correct-label"><span>{t('Should be')}</span>{t('Enter it below')}</p>
        </div> : null}
        {form.correctionField === 'BUS_TYPE' && <Field label={t('Correct bus type')} htmlFor="sug-correct" error={problems.correctedValue} wide>
          <select id="sug-correct" className="cp-text" value={form.correctedValue} onChange={(e) => set('correctedValue', e.target.value)}>
            <option value="">{t('Choose the bus type')}</option>
            {BUS_TYPES.map((x) => <option key={x} value={x}>{t(BUS_TYPE_LABELS[x])}</option>)}
          </select>
        </Field>}
        {['BUS_NAME', 'ROUTE_NUMBER', 'STOP_NAME'].includes(form.correctionField) && <Field label={form.correctionField === 'ROUTE_NUMBER' ? t('Correct route number') : t('Correct name')}
          htmlFor="sug-correct" error={problems.correctedValue} wide>
          <input id="sug-correct" className="cp-text" value={form.correctedValue} maxLength={form.correctionField === 'ROUTE_NUMBER' ? 20 : 100} onChange={(e) => set('correctedValue', e.target.value)} />
        </Field>}
        {form.correctionField === 'STOP_LOCATION' && <Field label={t('Where is the stop actually?')} htmlFor="sug-where" error={problems.locationName} wide
          hint={t('A landmark, or how far and in which direction from where it is shown now.')}>
          <input id="sug-where" className="cp-text" value={form.locationName} maxLength={200} onChange={(e) => set('locationName', e.target.value)}
            placeholder={t('e.g. About 100 m north, in front of the mosque')} />
        </Field>}
      </>}
    </Section>

    <Section title={t('Description')} description={t('A clear summary and how you know helps the review go faster.')}>
      <Field label={t('Title')} htmlFor="sug-title" error={problems.title} wide>
        <input id="sug-title" className="cp-text" value={form.title} maxLength={150} onChange={(e) => set('title', e.target.value)}
          placeholder={form.type === 'NEW_ROUTE' ? t('e.g. New bus from Mirpur 12 to Motijheel') : form.type === 'NEW_STOP' ? t('e.g. Missing stop at Bijoy Sarani') : t('e.g. Wrong stop name at Farmgate')} />
      </Field>
      <Field label={t('Description')} htmlFor="sug-description" error={problems.description} wide>
        <textarea id="sug-description" className="cp-text" rows={5} maxLength={2000} value={form.description} onChange={(e) => set('description', e.target.value)}
          placeholder={t('What did you notice, and how do you know? For example, you take this bus every day.')} />
        <span className="sug-counter" aria-hidden="true">{formatNumber(form.description.trim().length)} / {formatNumber(2000)}</span>
      </Field>
    </Section>

    <Section title={t('Additional information')} description={t('Optional, but useful for the reviewer.')}>
      {form.type !== 'NEW_STOP' && form.correctionField !== 'STOP_LOCATION' && <Field label={t('Location')} htmlFor="sug-location-name" error={problems.locationName} optional wide>
        <input id="sug-location-name" className="cp-text" value={form.locationName} maxLength={200} onChange={(e) => set('locationName', e.target.value)} placeholder={t('e.g. Near Farmgate footbridge')} />
      </Field>}
      <div className="sug-field wide">
        <span className="sug-label">{t('Photo')}<span className="sug-optional">{t('Optional')}</span></span>
        <input ref={fileInput} type="file" accept="image/jpeg,image/png,image/webp" hidden onChange={addImage} aria-label={t('Add a photo')} />
        {form.image ? <div className="sug-file">
          <img src={form.image.data} alt={t('Photo preview')} />
          <span><b>{form.image.name}</b><small>{form.hasPhoto ? t('Replaces the current photo') : t('Attached')}</small></span>
          <button type="button" className="sug-icon-button" onClick={() => set('image', null)} aria-label={t('Remove photo')}><X size={15} /></button>
        </div> : form.hasPhoto && !form.removePhoto ? <div className="sug-file no-preview">
          <span className="sug-file-icon"><ImagePlus size={18} /></span>
          <span><b>{t('Current photo')}</b><small>{t('Already attached to this suggestion')}</small></span>
          <span className="sug-file-actions">
            <button type="button" className="sug-ghost" onClick={() => fileInput.current?.click()}>{t('Replace')}</button>
            <button type="button" className="sug-icon-button" onClick={() => set('removePhoto', true)} aria-label={t('Remove photo')}><X size={15} /></button>
          </span>
        </div> : <button type="button" className="sug-dropzone" onClick={() => fileInput.current?.click()}>
          <ImagePlus size={18} /><span><b>{t('Add a photo')}</b><small>{t('JPG, PNG or WEBP, up to 5 MB')}</small></span></button>}
        {imageError && <small className="sug-error" role="alert"><CircleAlert size={13} /> {imageError}</small>}
      </div>
    </Section>

    <div className="sug-actions">
      <p className="sug-note"><ShieldCheck size={15} /> {t('An admin checks every suggestion before the bus information changes.')}</p>
      {error && <p className="sug-error sug-form-error" role="alert"><CircleAlert size={14} /> {error}</p>}
      <div className="sug-action-buttons">
        {editing ? <button type="button" className="sug-ghost" onClick={onCancel} disabled={busy}>{t('Cancel')}</button>
          : <button type="button" className="sug-ghost" onClick={reset} disabled={busy}>{t('Clear form')}</button>}
        <button type="submit" className="sug-primary" disabled={busy} aria-busy={busy}>
          {busy ? <><Loader2 size={16} className="cp-spin" /> {editing ? t('Saving…') : t('Sending…')}</>
            : editing ? <><Check size={15} /> {t('Save changes')}</> : <><Send size={15} /> {t('Submit suggestion')}</>}
        </button>
      </div>
    </div>
  </form>
}

// ---- One suggestion ----

const STEP_TEXT = { APPROVED: 'Approved and published', REJECTED: 'Not accepted' }

// Everything about one of the user's own suggestions, loaded fresh from the server.
export function SuggestionDetailsDialog({ suggestion, token, onClose, onEdit, onDelete, onMissing }) {
  const [state, setState] = useState({ status: 'loading', s: suggestion })
  useEffect(() => {
    let live = true
    suggestionApi.get(suggestion.id, token)
      .then((res) => { if (live) setState({ status: 'ready', s: res.data }) })
      .catch((e) => {
        if (!live) return
        if (e?.status === 404) onMissing(suggestion)
        else setState({ status: 'error', s: suggestion, error: errorText(e) })
      })
    return () => { live = false }
  }, [suggestion, token, onMissing])
  const s = state.s
  const route = s.relatedBusName ? `${t(s.relatedBusName)}${s.relatedRouteNumber ? ` (${t('Route {number}', { number: s.relatedRouteNumber })})` : ''}` : t('No longer in the list')
  const rows = s.type === 'NEW_ROUTE'
    ? [[t('Bus name'), s.routeName], [t('Route number'), s.routeNumber || t('Not given')], [t('Bus type'), t(BUS_TYPE_LABELS[s.busType] || s.busType)],
      [t('Start → end'), [s.startLocation, s.endLocation].filter(Boolean).map((x) => t(x)).join(' → ')]]
    : s.type === 'NEW_STOP'
      ? [[t('Route'), route], [t('Bus stop name'), s.busStopName], [t('Where is the stop?'), s.locationName]]
      : [[t('Route'), route], [t('What is wrong?'), t(CORRECTION_LABELS[s.correctionField] || s.correctionField)],
        ...(s.relatedStopName ? [[t('Stop'), t(s.relatedStopName)]] : []),
        s.correctionField === 'STOP_LOCATION' ? [t('Where is the stop actually?'), s.locationName]
          : [t('Correct value'), s.correctionField === 'BUS_TYPE' ? t(BUS_TYPE_LABELS[s.correctedValue] || s.correctedValue) : s.correctedValue]]
  const extraPlace = s.type !== 'NEW_STOP' && s.correctionField !== 'STOP_LOCATION' ? s.locationName : null
  const reviewed = s.status !== 'PENDING'

  return <CpModal title={s.title} subtitle={s.code} onClose={onClose} wide>
    {state.status === 'error' && <p className="cp-form-error" role="alert"><CircleAlert size={15} /> {state.error}</p>}
    <div className="sug-details" data-testid="sug-details">
      <div className="sug-details-main">
        <div className="sug-details-top"><SuggestionStatusBadge status={s.status} large /><span className="sug-card-type"><TypeIcon type={s.type} size={15} /> {t(TYPE_LABELS[s.type] || s.type)}</span></div>
        {s.adminReviewNote && <p className="cp-feedback"><MessageSquareText size={15} /> <span><b>{t('Admin note')}:</b> {s.adminReviewNote}</span></p>}
        {s.status === 'APPROVED' && s.appliedChange && <p className="sug-applied"><CheckCircle2 size={14} /> <span>{s.appliedChange}</span></p>}
        <h4>{t('Description')}</h4>
        <p className="sug-details-text">{s.description}</p>
        <h4>{t('Details')}</h4>
        <dl className="sug-dl">
          {rows.filter(([, v]) => v).map(([k, v]) => <div key={k}><dt>{k}</dt><dd>{v}</dd></div>)}
          {extraPlace && <div><dt>{t('Location')}</dt><dd>{extraPlace}</dd></div>}
        </dl>
        {s.type === 'NEW_ROUTE' && s.stops?.length > 0 && <>
          <h4>{t('Stops in order')}</h4>
          <ol className="sug-detail-stops">{s.stops.map((x, i) => <li key={x.order} className={i === 0 ? 'start' : i === s.stops.length - 1 ? 'end' : ''}>
            <span>{t(x.name)}</span><small>{x.isExisting ? t('Existing stop') : t('New place')}</small></li>)}</ol>
        </>}
        <h4>{t('Photo')}</h4>
        {s.imageUrl ? <EvidenceGallery evidence={[{ id: 'photo', url: s.imageUrl }]} token={token} /> : <p className="sug-hint">{t('No photo was added.')}</p>}
      </div>
      <aside className="sug-details-side">
        <h4>{t('Progress')}</h4>
        <ol className="sug-progress">
          <li className="done"><b>{t('Submitted')}</b><small>{day(s.createdAt)}</small></li>
          <li className={reviewed ? `done ${s.status.toLowerCase()}` : 'current'}>
            <b>{reviewed ? t(STEP_TEXT[s.status]) : t('Waiting for admin review')}</b>
            <small>{reviewed ? day(s.reviewedAt) : t('An admin checks it against the bus data.')}</small></li>
        </ol>
        {state.status === 'loading' && <p className="sug-hint"><Loader2 size={13} className="cp-spin" /> {t('Checking for the latest status…')}</p>}
        {s.status === 'PENDING' && s.canDelete && <div className="sug-details-actions">
          <button type="button" className="sug-ghost" onClick={() => onEdit(s)}><PencilLine size={14} /> {t('Edit suggestion')}</button>
          <button type="button" className="sug-ghost danger" onClick={() => onDelete(s)}><Trash2 size={14} /> {t('Delete suggestion')}</button>
        </div>}
        {s.status !== 'PENDING' && <p className="sug-hint">{t('Reviewed suggestions can no longer be edited or deleted.')}</p>}
      </aside>
    </div>
  </CpModal>
}

export function SuggestionStatusCard({ suggestion: s, onView, onEdit, onDelete }) {
  const place = suggestionPlace(s)
  return <article className={`sug-card ${(STATUS_CLASS[s.status] || '').toLowerCase()}`} data-testid="sug-card">
    <header>
      <span className="sug-card-type"><TypeIcon type={s.type} size={15} /> {t(TYPE_LABELS[s.type] || s.type)}</span>
      <SuggestionStatusBadge status={s.status} />
    </header>
    <h3>{s.title}</h3>
    <ul className="sug-card-facts">
      {s.correctionField && <li><PencilLine size={14} /> <span>{t(CORRECTION_LABELS[s.correctionField])}</span></li>}
      {place && <li><MapPin size={14} /> <span>{place}</span></li>}
      <li><Calendar size={14} /> <span>{t('Sent {date}', { date: day(s.createdAt) })}{s.reviewedAt ? ` · ${t('Reviewed {date}', { date: day(s.reviewedAt) })}` : ''}</span></li>
    </ul>
    <p className="sug-description">{s.description}</p>
    {s.adminReviewNote && <p className="cp-feedback"><MessageSquareText size={14} /> <span><b>{t('Admin note')}:</b> {s.adminReviewNote}</span></p>}
    {s.status === 'APPROVED' && s.appliedChange && <p className="sug-applied"><CheckCircle2 size={14} /> <span>{s.appliedChange}</span></p>}
    <footer>
      <span className="sug-code">{s.code}</span>
      <span className="sug-card-actions">
        {s.status === 'PENDING' && s.canDelete && <button type="button" className="sug-ghost danger" onClick={() => onDelete(s)}
          aria-label={t('Delete suggestion {code}', { code: s.code })}><Trash2 size={14} /> {t('Delete')}</button>}
        {s.status === 'PENDING' && s.canDelete && <button type="button" className="sug-ghost small" onClick={() => onEdit(s)}
          aria-label={t('Edit suggestion {code}', { code: s.code })}><PencilLine size={14} /> {t('Edit')}</button>}
        <button type="button" className="sug-ghost view" onClick={() => onView(s)} aria-label={t('View details of {code}', { code: s.code })}>
          <Eye size={14} /> {t('View details')}</button>
      </span>
    </footer>
  </article>
}

function DeleteDialog({ suggestion, token, onClose, onDeleted }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const remove = async () => {
    setBusy(true); setError('')
    try { onDeleted(await suggestionApi.remove(suggestion.id, token)) } catch (e) { setError(errorText(e)); setBusy(false) }
  }
  return <CpModal title={t('Delete suggestion')} subtitle={suggestion.code} onClose={busy ? () => {} : onClose}>
    <div className="cp-confirm" role="alertdialog" aria-label={t('Delete suggestion')}>
      <p>{t('Delete "{title}"? It has not been reviewed yet. This cannot be undone.', { title: suggestion.title })}</p>
      {error && <p className="cp-form-error" role="alert"><CircleAlert size={15} /> {error}</p>}
      <div><button type="button" className="button button-outline button-small" onClick={onClose} disabled={busy}>{t('Cancel')}</button>
        <button type="button" className="button button-small cp-danger" onClick={remove} disabled={busy}>{busy ? <Loader2 size={14} className="cp-spin" /> : <Trash2 size={14} />} {t('Yes, delete')}</button></div>
    </div>
  </CpModal>
}

// ---- My Suggestions ----

export function MySuggestionsSection({ token, refreshKey, onNew, notify }) {
  const [state, setState] = useState({ status: 'loading', items: [] })
  const [filter, setFilter] = useState('all')
  const [refreshing, setRefreshing] = useState(false)
  const [deleting, setDeleting] = useState(null)
  const [viewing, setViewing] = useState(null)
  // Delete from the details window: close it and ask for confirmation.
  const deleteFromDetails = useCallback((s) => { setViewing(null); setDeleting(s) }, [])
  const [editing, setEditing] = useState(null)
  const editFromDetails = useCallback((s) => { setViewing(null); setEditing(s) }, [])
  const saved = (res) => {
    setState((now) => ({ ...now, items: now.items.map((x) => (x.id === res.data.id ? res.data : x)) }))
    setEditing(null)
    notify(t(res.message || 'Suggestion updated.'))
  }
  // Deleted elsewhere (another tab or device): drop it from the list.
  const missing = useCallback((s) => {
    setViewing(null)
    setState((now) => ({ ...now, items: now.items.filter((x) => x.id !== s.id) }))
    notify(t('This suggestion no longer exists.'), 'error')
  }, [notify])
  const load = useCallback(async () => {
    setRefreshing(true)
    try { const res = await suggestionApi.my(token); setState({ status: 'ready', items: res.data || [] }) }
    catch (e) { setState((now) => (now.status === 'ready' ? now : { status: 'error', items: [], error: errorText(e) })) }
    finally { setRefreshing(false) }
  }, [token])
  useEffect(() => { load() }, [load, refreshKey])
  const counts = Object.fromEntries(STATUSES.map((s) => [s, state.items.filter((x) => x.status === s).length]))
  const shown = filter === 'all' ? state.items : state.items.filter((x) => x.status === filter)
  const deleted = (res) => {
    setState((now) => ({ ...now, items: now.items.filter((x) => x.id !== deleting.id) }))
    setDeleting(null)
    notify(t(res.message || 'Suggestion deleted.'))
  }
  return <section className="sug-panel" aria-labelledby="sug-my-title">
    <header className="sug-panel-head"><div><h2 id="sug-my-title">{t('My Suggestions')}</h2><p>{t('Track the review status of everything you have submitted.')}</p></div>
      <button type="button" className="sug-ghost" onClick={load} disabled={refreshing}>{refreshing ? <Loader2 size={14} className="cp-spin" /> : <RefreshCw size={14} />} {t('Refresh')}</button></header>
    <div className="sug-filter" role="group" aria-label={t('Filter by status')}>
      <button type="button" aria-pressed={filter === 'all'} className={filter === 'all' ? 'selected' : ''} onClick={() => setFilter('all')}>{t('All')} <span>{formatNumber(state.items.length)}</span></button>
      {STATUSES.map((s) => <button key={s} type="button" aria-pressed={filter === s} className={filter === s ? 'selected' : ''} onClick={() => setFilter(s)}>{t(STATUS_LABELS[s])} <span>{formatNumber(counts[s])}</span></button>)}
    </div>
    {state.status === 'loading' ? <div className="cp-skeletons">{[0, 1, 2].map((i) => <i key={i} />)}</div>
      : state.status === 'error' ? <p className="cp-form-error" role="alert"><CircleAlert size={15} /> {state.error} <button type="button" className="cp-link" onClick={load}>{t('Try again')}</button></p>
        : !shown.length ? <div className="sug-empty"><RouteIcon size={22} /><b>{state.items.length ? t('No suggestions with this status') : t('You have not sent any suggestions yet')}</b>
          <span>{t('Missing routes, missing stops and corrections you submit will appear here.')}</span>
          {!state.items.length && <button type="button" className="sug-primary small" onClick={onNew}>{t('Make a suggestion')}</button>}</div>
          : <div className="sug-cards">{shown.map((s) => <SuggestionStatusCard key={s.id} suggestion={s} onView={setViewing} onEdit={setEditing} onDelete={setDeleting} />)}</div>}
    {viewing && <SuggestionDetailsDialog suggestion={viewing} token={token} onClose={() => setViewing(null)} onEdit={editFromDetails} onDelete={deleteFromDetails} onMissing={missing} />}
    {editing && <CpModal title={t('Edit suggestion')} subtitle={editing.code} onClose={() => setEditing(null)} wide>
      <div className="sug-edit"><RouteSuggestionForm key={editing.id} token={token} suggestion={editing} onSubmitted={saved} onCancel={() => setEditing(null)} /></div>
    </CpModal>}
    {deleting && <DeleteDialog suggestion={deleting} token={token} onClose={() => setDeleting(null)} onDeleted={deleted} />}
  </section>
}

// ---- Page ----

export function RouteSuggestionPage({ token, signedIn, openAuth, notify = () => {} }) {
  const [tab, setTab] = useState('new')
  const [refreshKey, setRefreshKey] = useState(0)
  const [done, setDone] = useState(null)
  const submitted = (result) => {
    setDone(result)
    setRefreshKey((k) => k + 1)
    notify(t('Suggestion submitted.'))
    window.scrollTo?.({ top: 0, behavior: 'smooth' })
  }
  const header = <header className="sug-header">
    <p className="sug-kicker">{t('Community · Bus information')}</p>
    <h1>{t('Suggest a Bus Route or Stop')}</h1>
    <p>{t('Report a missing route or stop, or correct existing information. Every submission is reviewed by an admin before it is published.')}</p>
  </header>
  if (!signedIn) return <div className="page inner-page cp-page sug-page">{header}<RequireSignIn openAuth={openAuth} /></div>
  return <div className="page inner-page cp-page sug-page">
    {header}
    <nav className="sug-tabs" role="tablist" aria-label={t('Suggestion sections')}>
      <button type="button" role="tab" aria-selected={tab === 'new'} className={tab === 'new' ? 'selected' : ''} onClick={() => setTab('new')}>{t('New suggestion')}</button>
      <button type="button" role="tab" aria-selected={tab === 'mine'} className={tab === 'mine' ? 'selected' : ''} onClick={() => setTab('mine')}>{t('My Suggestions')}</button>
    </nav>
    {done && <div className="sug-banner" role="status" data-testid="sug-success">
      <CheckCircle2 size={18} />
      <div><b>{t('Suggestion submitted')}</b><small>{t(done.message)}</small></div>
      <button type="button" className="sug-ghost" onClick={() => { setTab('mine'); setDone(null) }}>{t('View status')}</button>
      <button type="button" className="sug-icon-button" onClick={() => setDone(null)} aria-label={t('Close')}><X size={15} /></button>
    </div>}
    {tab === 'new'
      ? <div className="sug-panel"><RouteSuggestionForm token={token} onSubmitted={submitted} /></div>
      : <MySuggestionsSection token={token} refreshKey={refreshKey} onNew={() => setTab('new')} notify={notify} />}
  </div>
}
