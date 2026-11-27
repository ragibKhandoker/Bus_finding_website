import React, { useCallback, useEffect, useState } from 'react'
import {
  ArrowRight, BusFront, CalendarDays, CircleAlert, Clock, Eye, History, Loader2, MapPin, Pencil, Plus, Repeat, Route as RouteIcon, Save, Snowflake, Target,
  Trash2,
} from 'lucide-react'
import { useBusRoutes } from '../../controllers/useBusRoutes'
import { formatDateTime, formatNumber, t } from '../../models/i18n'
import {
  BUS_TYPES, BUS_TYPE_LABELS, DAYS, DAY_SHORT, MAX_PREFERENCES, PURPOSES, PURPOSE_ICON, daysLabel, emptyPreference, formatTime, preferenceProblems,
  toForm, toPreferenceBody, travelPreferenceApi,
} from '../../models/travelPreferences'
import { CpModal } from '../components/ComplaintUI'
import { BusSearch, useDhakaBuses } from './Complaints'
import { RequireSignIn } from './MorePages'
import '../styles/travel-preferences.css'

// F19 Travel preferences: a commuter saves the journeys they make often (Home → University, Home → Office, …),
// each as its own preference that can be viewed, edited and deleted on its own.

const errorText = (error) => t(error?.message || 'Something went wrong. Please try again.')

function Field({ label, htmlFor, error, hint, children, wide = false }) {
  return <div className={`cp-field${error ? ' has-error' : ''}${wide ? ' wide' : ''}`}>
    <label htmlFor={htmlFor}>{label}</label>
    {children}
    {error ? <small className="cp-error" role="alert">{error}</small> : hint ? <small className="cp-hint">{hint}</small> : null}
  </div>
}

// ---- The form (new or edit) ----

export function TravelPreferenceForm({ token, preference, onSaved, onCancel }) {
  const { buses, status } = useDhakaBuses({ cityOnly: true })
  const editing = Boolean(preference)
  const [form, setForm] = useState(() => (preference ? toForm(preference) : emptyPreference()))
  const [problems, setProblems] = useState({})
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const bus = buses.find((b) => b.name === form.busName)
  const route = bus?.routes.find((r) => r.id === form.busId)
  // Stops on the chosen route, offered as suggestions for the two ends (any place can still be typed).
  const stopSuggestions = route?.stops || []

  const set = (key, value) => { setForm((f) => ({ ...f, [key]: value })); setProblems((p) => ({ ...p, [key]: undefined })); setError('') }
  const pickBus = (b) => {
    setForm((f) => ({ ...f, busName: b?.name || '', busId: b?.routes.length === 1 ? b.routes[0].id : '' }))
    setProblems((p) => ({ ...p, busName: undefined, busId: undefined }))
  }
  // Choosing days also sets the weekly frequency to match; the user can still change the number afterwards.
  const toggleDay = (day) => {
    setForm((f) => {
      const travelDays = f.travelDays.includes(day) ? f.travelDays.filter((d) => d !== day) : [...f.travelDays, day]
      return { ...f, travelDays, weeklyFrequency: travelDays.length ? String(travelDays.length) : f.weeklyFrequency }
    })
    setProblems((p) => ({ ...p, travelDays: undefined, weeklyFrequency: undefined }))
  }

  const submit = async (event) => {
    event.preventDefault()
    if (busy) return
    const found = preferenceProblems(form)
    setProblems(found)
    if (Object.keys(found).length) { setError(t('Please fix the highlighted fields.')); return }
    setBusy(true); setError('')
    try {
      const body = toPreferenceBody(form)
      const result = editing ? await travelPreferenceApi.update(preference.id, body, token) : await travelPreferenceApi.create(body, token)
      onSaved(result)
    } catch (e) { setError(errorText(e)); setBusy(false) }
  }

  return <form className="cp-form tp-form" onSubmit={submit} noValidate aria-label={editing ? t('Edit travel preference') : t('New travel preference')}>
    <section className="cp-form-section">
      <h3><span>1</span> {t('Journey')}</h3>
      <div className="cp-grid tp-grid-2">
        <Field label={t('Preference name')} htmlFor="tp-name" error={problems.preferenceName} wide>
          <input id="tp-name" className="cp-text" value={form.preferenceName} maxLength={60} onChange={(e) => set('preferenceName', e.target.value)}
            placeholder={t('e.g. University Journey')} autoFocus={!editing} />
        </Field>
        <Field label={t('Start location')} htmlFor="tp-start" error={problems.startLocation}>
          <input id="tp-start" className="cp-text" list="tp-stops" value={form.startLocation} maxLength={100} onChange={(e) => set('startLocation', e.target.value)} placeholder={t('e.g. Mirpur 10')} />
        </Field>
        <Field label={t('Destination')} htmlFor="tp-destination" error={problems.destination}>
          <input id="tp-destination" className="cp-text" list="tp-stops" value={form.destination} maxLength={100} onChange={(e) => set('destination', e.target.value)} placeholder={t('e.g. Shahbag')} />
        </Field>
        <datalist id="tp-stops">{stopSuggestions.map((s) => <option key={s} value={s} />)}</datalist>
      </div>
      <p className="tp-label">{t('Travel purpose')}</p>
      <div className="cp-categories tp-purposes" role="radiogroup" aria-label={t('Travel purpose')}>
        {PURPOSES.map(([p, icon]) => <label key={p} className={`cp-category${form.travelPurpose === p ? ' selected' : ''}`}>
          <input type="radio" name="tp-purpose" value={p} checked={form.travelPurpose === p} onChange={() => set('travelPurpose', p)} />
          <span aria-hidden="true">{icon}</span><b>{t(p)}</b>
        </label>)}
      </div>
      {problems.travelPurpose && <small className="cp-error" role="alert">{problems.travelPurpose}</small>}
    </section>

    <section className="cp-form-section">
      <h3><span>2</span> {t('Bus')}</h3>
      <div className="cp-grid">
        <Field label={t('Frequently used Dhaka bus')} htmlFor="tp-bus" error={problems.busName} hint={t('Only Dhaka city buses are listed.')}>
          <BusSearch id="tp-bus" buses={buses} status={status} value={form.busName} onPick={pickBus} invalid={Boolean(problems.busName)} />
        </Field>
        <Field label={t('Bus route')} htmlFor="tp-route" error={problems.busId}>
          <select id="tp-route" className="cp-text" value={form.busId} disabled={!bus} onChange={(e) => set('busId', e.target.value)}>
            <option value="">{bus ? t('Choose the route') : t('Choose a bus first')}</option>
            {bus?.routes.map((r) => <option key={r.id} value={r.id}>{r.label || t('Route')}</option>)}
          </select>
        </Field>
        <Field label={t('Preferred bus type')} htmlFor="tp-bus-type" error={problems.busType}>
          <div className="tp-segmented" role="radiogroup" id="tp-bus-type" aria-label={t('Preferred bus type')}>
            {BUS_TYPES.map((type) => <label key={type} className={form.busType === type ? 'selected' : ''}>
              <input type="radio" name="tp-bus-type" value={type} checked={form.busType === type} onChange={() => set('busType', type)} />{t(type)}
            </label>)}
          </div>
        </Field>
      </div>
    </section>

    <section className="cp-form-section">
      <h3><span>3</span> {t('When')}</h3>
      <div className="cp-grid tp-grid-2">
        <Field label={t('Preferred travel time')} htmlFor="tp-time" error={problems.preferredTravelTime}>
          <input id="tp-time" type="time" className="cp-text" value={form.preferredTravelTime} onChange={(e) => set('preferredTravelTime', e.target.value)} />
        </Field>
        <Field label={t('Weekly travel frequency')} htmlFor="tp-frequency" error={problems.weeklyFrequency} hint={t('How many days a week you make this journey (1–7).')}>
          <select id="tp-frequency" className="cp-text" value={form.weeklyFrequency} onChange={(e) => set('weeklyFrequency', e.target.value)}>
            {[1, 2, 3, 4, 5, 6, 7].map((n) => <option key={n} value={String(n)}>{n === 1 ? t('1 day a week') : t('{n} days a week', { n: formatNumber(n) })}</option>)}
          </select>
        </Field>
        <Field label={t('Preferred travel days')} htmlFor="tp-days" error={problems.travelDays} wide>
          <div className="tp-days" id="tp-days" role="group" aria-label={t('Preferred travel days')}>
            {DAYS.map((d) => <label key={d} className={form.travelDays.includes(d) ? 'selected' : ''}>
              <input type="checkbox" checked={form.travelDays.includes(d)} onChange={() => toggleDay(d)} aria-label={t(d)} />
              <span aria-hidden="true">{t(DAY_SHORT[d])}</span>
            </label>)}
          </div>
        </Field>
      </div>
    </section>

    {error && <p className="cp-form-error" role="alert"><CircleAlert size={15} /> {error}</p>}
    <div className="cp-actions tp-actions">
      <button type="button" className="button button-outline" onClick={onCancel} disabled={busy}>{t('Cancel')}</button>
      <button type="submit" className="button cp-primary" disabled={busy} aria-busy={busy}>
        {busy ? <><Loader2 size={16} className="cp-spin" /> {t('Saving…')}</> : <><Save size={16} /> {editing ? t('Save changes') : t('Save preference')}</>}
      </button>
    </div>
  </form>
}

// ---- One saved preference ----

export function TravelPreferenceCard({ preference: p, onView, onEdit, onDelete }) {
  return <article className="cp-card tp-card" data-testid="tp-card">
    <header>
      <span className="tp-card-icon" aria-hidden="true">{PURPOSE_ICON[p.travelPurpose] || '📍'}</span>
      <div className="tp-card-title"><h3>{p.preferenceName}</h3><span className="cp-chip">{t(p.travelPurpose)}</span></div>
    </header>
    <p className="tp-route"><b>{p.startLocation}</b><ArrowRight size={15} aria-label={t('to')} /><b>{p.destination}</b></p>
    <ul className="cp-facts tp-facts">
      <li><BusFront size={14} /> <span>{t(p.frequentlyUsedBus)}{p.busRouteNumber ? <small> · {t('Route {number}', { number: p.busRouteNumber })}</small> : null}</span></li>
      <li><Clock size={14} /> <span>{formatTime(p.preferredTravelTime)}</span></li>
      <li><CalendarDays size={14} /> <span>{daysLabel(p.travelDays)}</span></li>
      <li><Repeat size={14} /> <span>{p.weeklyFrequency === 1 ? t('1 day a week') : t('{n} days a week', { n: formatNumber(p.weeklyFrequency) })}</span></li>
      <li><Snowflake size={14} /> <span>{t(BUS_TYPE_LABELS[p.busType] || p.busType)}</span></li>
    </ul>
    <footer>
      <button type="button" className="button button-small cp-primary tp-view" onClick={() => onView(p)} aria-label={t('View details of {name}', { name: p.preferenceName })}><Eye size={14} /> {t('View details')}</button>
      <button type="button" className="button button-outline button-small" onClick={() => onEdit(p)} aria-label={t('Edit {name}', { name: p.preferenceName })}><Pencil size={14} /> {t('Edit')}</button>
      <button type="button" className="button button-outline button-small cp-danger-outline" onClick={() => onDelete(p)} aria-label={t('Delete {name}', { name: p.preferenceName })}><Trash2 size={14} /> {t('Delete')}</button>
    </footer>
  </article>
}

// ---- Details of one preference ----

const sameStop = (a, b) => (a || '').trim().toLowerCase() === (b || '').trim().toLowerCase()

// Everything about one preference, loaded fresh from the server, with the stops of its bus route.
export function TravelPreferenceDetails({ preference, token, onClose, onEdit, onDelete, onMissing }) {
  const [state, setState] = useState({ status: 'loading', p: preference })
  const [routes] = useBusRoutes()
  useEffect(() => {
    let live = true
    travelPreferenceApi.get(preference.id, token)
      .then((res) => { if (live) setState({ status: 'ready', p: res.data }) })
      .catch((e) => {
        if (!live) return
        if (e?.status === 404) { onMissing(preference); return }
        setState({ status: 'error', p: preference, error: errorText(e) })
      })
    return () => { live = false }
  }, [preference, token, onMissing])
  const p = state.p
  const bus = routes.status === 'ready' ? routes.buses.find((b) => (b._id || b.id) === p.busId) : null
  const stops = (bus?.stops || []).map((s) => s.name)
  const frequency = p.weeklyFrequency === 1 ? t('1 day a week') : t('{n} days a week', { n: formatNumber(p.weeklyFrequency) })

  return <CpModal title={p.preferenceName} subtitle={`${PURPOSE_ICON[p.travelPurpose] || '📍'} ${t(p.travelPurpose)}`} onClose={onClose} wide>
    {state.status === 'error' && <p className="cp-form-error" role="alert"><CircleAlert size={15} /> {state.error}</p>}
    <div className="cp-details tp-details" data-testid="tp-details">
      <div className="cp-details-main">
        <p className="tp-route tp-route-big"><MapPin size={16} /><b>{p.startLocation}</b><ArrowRight size={16} aria-label={t('to')} /><Target size={16} /><b>{p.destination}</b></p>
        <dl className="cp-dl">
          <div><dt><BusFront size={14} /> {t('Frequently used Dhaka bus')}</dt><dd>{t(p.frequentlyUsedBus)}{p.busRouteNumber ? ` · ${t('Route {number}', { number: p.busRouteNumber })}` : ''}</dd></div>
          <div><dt><Snowflake size={14} /> {t('Preferred bus type')}</dt><dd>{t(BUS_TYPE_LABELS[p.busType] || p.busType)}</dd></div>
          <div><dt><Clock size={14} /> {t('Preferred travel time')}</dt><dd>{formatTime(p.preferredTravelTime)}</dd></div>
          <div><dt><Repeat size={14} /> {t('Weekly travel frequency')}</dt><dd>{frequency}</dd></div>
        </dl>
        <h4>{t('Preferred travel days')}</h4>
        <ul className="tp-week" aria-label={t('Preferred travel days')}>
          {DAYS.map((d) => { const on = p.travelDays.includes(d)
            return <li key={d} className={on ? 'on' : ''}><span aria-hidden="true">{t(DAY_SHORT[d])}</span><span className="sr-only">{t(d)}: {on ? t('Yes') : t('No')}</span></li> })}
        </ul>
        <h4>{t('Bus route stops')}</h4>
        {routes.status === 'loading' ? <div className="cp-center"><Loader2 size={18} className="cp-spin" /> {t('Loading…')}</div>
          : !stops.length ? <p className="cp-muted">{p.busId ? t('The stops of this route are not available right now.') : t('This bus route is no longer in the list. Edit the preference to choose another bus.')}</p>
            : <ol className="tp-stops">{stops.map((s, i) => {
              const mark = sameStop(s, p.startLocation) ? 'start' : sameStop(s, p.destination) ? 'end' : ''
              return <li key={`${s}-${i}`} className={mark}><span>{t(s)}</span>{mark && <small>{mark === 'start' ? t('Your start') : t('Your destination')}</small>}</li>
            })}</ol>}
      </div>
      <aside className="cp-details-side">
        <h4>{t('Record')}</h4>
        <dl className="cp-dl tp-dl-single">
          <div><dt><CalendarDays size={14} /> {t('Created')}</dt><dd>{formatDateTime(p.createdAt)}</dd></div>
          <div><dt><History size={14} /> {t('Last updated')}</dt><dd>{formatDateTime(p.updatedAt)}</dd></div>
        </dl>
        {state.status === 'loading' && <p className="cp-muted"><Loader2 size={13} className="cp-spin" /> {t('Checking for the latest details…')}</p>}
        <div className="tp-detail-actions">
          <button type="button" className="button button-outline button-small" onClick={() => onEdit(p)}><Pencil size={14} /> {t('Edit')}</button>
          <button type="button" className="button button-outline button-small cp-danger-outline" onClick={() => onDelete(p)}><Trash2 size={14} /> {t('Delete')}</button>
        </div>
      </aside>
    </div>
  </CpModal>
}

function DeleteDialog({ preference, token, onClose, onDeleted }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const remove = async () => {
    setBusy(true); setError('')
    try { const res = await travelPreferenceApi.remove(preference.id, token); onDeleted(res) } catch (e) { setError(errorText(e)); setBusy(false) }
  }
  return <CpModal title={t('Delete travel preference')} subtitle={preference.preferenceName} onClose={busy ? () => {} : onClose}>
    <div className="cp-confirm tp-confirm" role="alertdialog" aria-label={t('Delete travel preference')}>
      <p>{t('Delete "{name}"? Your other travel preferences stay as they are. This cannot be undone.', { name: preference.preferenceName })}</p>
      {error && <p className="cp-form-error" role="alert"><CircleAlert size={15} /> {error}</p>}
      <div><button type="button" className="button button-outline button-small" onClick={onClose} disabled={busy}>{t('Cancel')}</button>
        <button type="button" className="button button-small cp-danger" onClick={remove} disabled={busy}>{busy ? <Loader2 size={14} className="cp-spin" /> : <Trash2 size={14} />} {t('Yes, delete')}</button></div>
    </div>
  </CpModal>
}

// ---- Page ----

export function TravelPreferencePage({ token, signedIn, openAuth, notify = () => {} }) {
  const [state, setState] = useState({ status: 'loading', items: [] })
  const [editing, setEditing] = useState(null) // null: closed · 'new' · a preference
  const [deleting, setDeleting] = useState(null)
  const [viewing, setViewing] = useState(null)
  // From the details window, Edit and Delete close it and open their own window.
  const editFromDetails = useCallback((p) => { setViewing(null); setEditing(p) }, [])
  const deleteFromDetails = useCallback((p) => { setViewing(null); setDeleting(p) }, [])
  // Deleted somewhere else (another tab or device): drop it from the list.
  const missing = useCallback((p) => {
    setViewing(null)
    setState((now) => ({ ...now, items: now.items.filter((x) => x.id !== p.id) }))
    notify(t('This travel preference no longer exists.'), 'error')
  }, [notify])

  const load = useCallback(async () => {
    if (!signedIn) return
    try { const res = await travelPreferenceApi.my(token); setState({ status: 'ready', items: res.data || [] }) }
    catch (e) { setState((now) => (now.status === 'ready' ? now : { status: 'error', items: [], error: errorText(e) })) }
  }, [token, signedIn])
  useEffect(() => { load() }, [load])

  const saved = (result) => {
    const item = result.data
    setState((now) => ({
      status: 'ready',
      items: now.items.some((p) => p.id === item.id) ? now.items.map((p) => (p.id === item.id ? item : p)) : [...now.items, item],
    }))
    setEditing(null)
    notify(t(result.message || 'Travel preference saved.'))
  }
  const deleted = (result) => {
    setState((now) => ({ ...now, items: now.items.filter((p) => p.id !== deleting.id) }))
    setDeleting(null)
    notify(t(result.message || 'Travel preference deleted.'))
  }

  const hero = <section className="cp-hero tp-hero">
    <div><span className="cp-eyebrow">{t('MY COMMUTE · TRAVEL PREFERENCES')}</span>
      <h1>{t('Your everyday journeys')}</h1>
      <p>{t('Save the trips you make often (to university, office, coaching or anywhere else) with your usual bus, time and days. Add as many as you need and change them any time.')}</p></div>
  </section>
  if (!signedIn) return <div className="page inner-page cp-page tp-page">{hero}<RequireSignIn openAuth={openAuth} /></div>

  const { items } = state
  const full = items.length >= MAX_PREFERENCES
  const addButton = (label) => <button type="button" className="button cp-primary" onClick={() => setEditing('new')} disabled={full}
    title={full ? t('You can save up to {count} travel preferences.', { count: MAX_PREFERENCES }) : undefined}><Plus size={16} /> {label}</button>

  return <div className="page inner-page cp-page tp-page">
    {hero}
    <section className="cp-panel" aria-labelledby="tp-title">
      <header className="cp-panel-head tp-panel-head">
        <div><h2 id="tp-title">{t('My travel preferences')}</h2>
          <p>{state.status === 'ready' && items.length
            ? t('{count} of {max} saved. Each journey is kept and edited on its own.', { count: formatNumber(items.length), max: formatNumber(MAX_PREFERENCES) })
            : t('Each journey is kept and edited on its own.')}</p></div>
        {state.status === 'ready' && items.length > 0 && addButton(t('Add travel preference'))}
      </header>
      {state.status === 'loading' ? <div className="cp-skeletons" aria-label={t('Loading…')}>{[0, 1, 2].map((i) => <i key={i} />)}</div>
        : state.status === 'error' ? <p className="cp-form-error" role="alert"><CircleAlert size={15} /> {state.error} <button type="button" className="cp-link" onClick={load}>{t('Try again')}</button></p>
          : !items.length ? <div className="cp-empty tp-empty"><RouteIcon size={28} /><b>{t('No travel preferences yet')}</b>
            <span>{t('Add the journeys you make often, like Home → University or Home → Office, so SmartCommute knows how you travel.')}</span>
            {addButton(t('Add your first preference'))}</div>
            : <div className="cp-cards tp-cards">{items.map((p) => <TravelPreferenceCard key={p.id} preference={p} onView={setViewing} onEdit={setEditing} onDelete={setDeleting} />)}</div>}
    </section>
    {editing && <CpModal title={editing === 'new' ? t('Add travel preference') : t('Edit travel preference')} subtitle={editing === 'new' ? null : editing.preferenceName}
      onClose={() => setEditing(null)} wide>
      <TravelPreferenceForm key={editing === 'new' ? 'new' : editing.id} token={token} preference={editing === 'new' ? null : editing} onSaved={saved} onCancel={() => setEditing(null)} />
    </CpModal>}
    {viewing && <TravelPreferenceDetails preference={viewing} token={token} onClose={() => setViewing(null)} onEdit={editFromDetails} onDelete={deleteFromDetails} onMissing={missing} />}
    {deleting && <DeleteDialog preference={deleting} token={token} onClose={() => setDeleting(null)} onDeleted={deleted} />}
  </div>
}
