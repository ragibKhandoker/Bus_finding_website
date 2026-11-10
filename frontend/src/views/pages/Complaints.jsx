import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  BusFront, Calendar, Check, CircleAlert, ClipboardList, Eye, FileWarning, ImagePlus, Loader2, MapPin, MessageSquareText, Route as RouteIcon,
  Send, ShieldCheck, Trash2, X,
} from 'lucide-react'
import { useBusRoutes } from '../../controllers/useBusRoutes'
import { formatNumber, t } from '../../models/i18n'
import {
  CATEGORIES, CATEGORY_ICON, MAX_EVIDENCE, STATUSES, STATUS_LABELS, complaintApi, complaintProblems, day, dayTime, dhakaToday, emptyComplaint,
  toComplaintBody,
} from '../../models/complaints'
import { readImage } from '../../models/lostFound'
import { searchKey } from '../components/BusNameSearch'
import { CpModal, EvidenceGallery, StatusBadge, Timeline } from '../components/ComplaintUI'
import { RequireSignIn } from './MorePages'

// F18 Complaint & Feedback: report a problem with a Dhaka bus, then follow it until an admin resolves it.

const errorText = (error) => t(error?.message || 'Something went wrong. Please try again.')

function Field({ label, htmlFor, error, hint, optional = false, children, wide = false }) {
  return <div className={`cp-field${error ? ' has-error' : ''}${wide ? ' wide' : ''}`}>
    <label htmlFor={htmlFor}>{label}{optional && <em> · {t('optional')}</em>}</label>
    {children}
    {error ? <small className="cp-error" role="alert">{error}</small> : hint ? <small className="cp-hint">{hint}</small> : null}
  </div>
}

// Every Dhaka bus by name, each with its routes (one route = one bus row) and their stops, from the route list.
function useDhakaBuses() {
  const [routes] = useBusRoutes()
  const buses = useMemo(() => {
    const byName = new Map()
    for (const b of routes.status === 'ready' ? routes.buses : []) {
      const name = (b.busName || '').trim()
      if (!name || !(b._id || b.id)) continue
      const stops = (b.stops || []).map((s) => s.name)
      const entry = byName.get(name) || { name, routes: [] }
      entry.routes.push({ id: b._id || b.id, number: b.routeNumber, stops, label: `${b.routeNumber ? `${t('Route {number}', { number: b.routeNumber })}: ` : ''}${stops.length ? `${t(stops[0])} → ${t(stops[stops.length - 1])}` : ''}` })
      byName.set(name, entry)
    }
    return [...byName.values()].sort((a, b) => a.name.localeCompare(b.name))
  }, [routes])
  return { buses, status: routes.status }
}

function BusSearch({ buses, status, value, onPick, invalid }) {
  const [text, setText] = useState(value || '')
  const [open, setOpen] = useState(false)
  const box = useRef(null)
  useEffect(() => { setText(value || '') }, [value])
  useEffect(() => {
    if (!open) return
    const away = (e) => { if (!box.current?.contains(e.target)) setOpen(false) }
    document.addEventListener('pointerdown', away)
    return () => document.removeEventListener('pointerdown', away)
  }, [open])
  const list = useMemo(() => {
    const q = searchKey(text)
    if (!q || buses.some((b) => b.name === text)) return buses.slice(0, 80)
    return buses.filter((b) => searchKey(b.name).includes(q) || searchKey(t(b.name)).includes(q) || b.routes.some((r) => searchKey(r.number).startsWith(q))).slice(0, 80)
  }, [buses, text])
  return <div className="cp-picker" ref={box}>
    <span className={`cp-input${invalid ? ' invalid' : ''}`}><BusFront size={16} aria-hidden="true" />
      <input id="cp-bus" value={text} role="combobox" aria-expanded={open} aria-controls="cp-bus-list" autoComplete="off" aria-invalid={invalid || undefined}
        placeholder={status === 'loading' ? t('Loading Dhaka buses…') : status === 'error' ? t('Could not load the buses. Refresh the page.') : t('Search a bus, e.g. Raida or 36')}
        onFocus={() => setOpen(true)} onChange={(e) => { setText(e.target.value); setOpen(true); if (value) onPick(null) }} />
    </span>
    {open && status === 'ready' && <ul className="cp-options" id="cp-bus-list" role="listbox" aria-label={t('Dhaka buses')}>
      {!list.length ? <li className="cp-option-note">{t('No Dhaka bus matches. Try another name or route number.')}</li>
        : list.map((b) => <li key={b.name} role="option" aria-selected={b.name === value} className={b.name === value ? 'selected' : ''}
          onPointerDown={(e) => e.preventDefault()} onClick={() => { onPick(b); setText(b.name); setOpen(false) }}>
          <b>{t(b.name)}</b><small>{b.routes.length === 1 ? b.routes[0].label : t('{count} routes', { count: b.routes.length })}</small></li>)}
    </ul>}
  </div>
}

// ---- The form ----

export function ComplaintForm({ token, onSubmitted }) {
  const { buses, status } = useDhakaBuses()
  const [form, setForm] = useState(emptyComplaint)
  const [problems, setProblems] = useState({})
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [imageError, setImageError] = useState('')
  const fileInput = useRef(null)
  const bus = buses.find((b) => b.name === form.busName)
  const route = bus?.routes.find((r) => r.id === form.busId)
  const set = (key, value) => { setForm((f) => ({ ...f, [key]: value })); setProblems((p) => ({ ...p, [key]: undefined })); setError('') }

  const pickBus = (b) => {
    setForm((f) => ({ ...f, busName: b?.name || '', busId: b?.routes.length === 1 ? b.routes[0].id : '', stopName: '' }))
    setProblems((p) => ({ ...p, busName: undefined, busId: undefined }))
  }
  const addImages = async (event) => {
    const files = [...(event.target.files || [])]
    event.target.value = ''
    setImageError('')
    for (const file of files) {
      if (form.evidence.length >= MAX_EVIDENCE) { setImageError(t('Add at most {count} pictures.', { count: MAX_EVIDENCE })); break }
      try {
        const data = await readImage(file)
        setForm((f) => (f.evidence.length >= MAX_EVIDENCE ? f : { ...f, evidence: [...f.evidence, { id: `${Date.now()}-${Math.random()}`, data, name: file.name }] }))
      } catch (e) { setImageError(e.message) }
    }
  }

  const submit = async (event) => {
    event.preventDefault()
    if (busy) return
    const found = complaintProblems(form)
    setProblems(found)
    if (Object.keys(found).length) { setError(t('Please fix the highlighted fields.')); return }
    setBusy(true); setError('')
    try {
      const result = await complaintApi.create(toComplaintBody(form), token)
      setForm(emptyComplaint())
      onSubmitted(result)
    } catch (e) { setError(errorText(e)) } finally { setBusy(false) }
  }

  return <form className="cp-form" onSubmit={submit} noValidate aria-label={t('New complaint')}>
    <section className="cp-form-section">
      <h3><span>1</span> {t('Which bus?')}</h3>
      <div className="cp-grid">
        <Field label={t('Bus name')} htmlFor="cp-bus" error={problems.busName} hint={t('Only Dhaka city buses are listed.')}>
          <BusSearch buses={buses} status={status} value={form.busName} onPick={pickBus} invalid={Boolean(problems.busName)} />
        </Field>
        <Field label={t('Bus route')} htmlFor="cp-route" error={problems.busId}>
          <select id="cp-route" className="cp-text" value={form.busId} disabled={!bus} onChange={(e) => { set('busId', e.target.value); set('stopName', '') }}>
            <option value="">{bus ? t('Choose the route') : t('Choose a bus first')}</option>
            {bus?.routes.map((r) => <option key={r.id} value={r.id}>{r.label || t('Route')}</option>)}
          </select>
        </Field>
        <Field label={t('Bus stop')} htmlFor="cp-stop" optional hint={t('The stop where it happened, if it was at one.')}>
          <select id="cp-stop" className="cp-text" value={form.stopName} disabled={!route} onChange={(e) => {
            const stop = e.target.value
            setForm((f) => ({ ...f, stopName: stop, incidentLocation: f.incidentLocation || (stop ? t(stop) : '') }))
          }}>
            <option value="">{route ? t('Not at a stop / not sure') : t('Choose a route first')}</option>
            {route?.stops.map((s) => <option key={s} value={s}>{t(s)}</option>)}
          </select>
        </Field>
      </div>
    </section>

    <section className="cp-form-section">
      <h3><span>2</span> {t('What went wrong?')}</h3>
      <div className="cp-categories" role="radiogroup" aria-label={t('Complaint category')}>
        {CATEGORIES.map(([c, icon]) => <label key={c} className={`cp-category${form.category === c ? ' selected' : ''}`}>
          <input type="radio" name="cp-category" value={c} checked={form.category === c} onChange={() => set('category', c)} />
          <span aria-hidden="true">{icon}</span><b>{t(c)}</b>
        </label>)}
      </div>
      {problems.category && <small className="cp-error" role="alert">{problems.category}</small>}
      <div className="cp-grid">
        <Field label={t('Subject')} htmlFor="cp-subject" error={problems.subject} wide>
          <input id="cp-subject" className="cp-text" value={form.subject} maxLength={150} onChange={(e) => set('subject', e.target.value)} placeholder={t('e.g. Conductor charged 40 taka for a 20 taka trip')} />
        </Field>
        <Field label={t('What happened?')} htmlFor="cp-description" error={problems.description} wide
          hint={`${formatNumber(form.description.trim().length)}/2000 · ${t('Include the time, what was said or done, and the bus number plate if you saw it.')}`}>
          <textarea id="cp-description" className="cp-text" rows={5} maxLength={2000} value={form.description} onChange={(e) => set('description', e.target.value)}
            placeholder={t('Describe what happened, step by step.')} />
        </Field>
      </div>
    </section>

    <section className="cp-form-section">
      <h3><span>3</span> {t('When and where?')}</h3>
      <div className="cp-grid">
        <Field label={t('Incident date')} htmlFor="cp-date" error={problems.incidentDate}>
          <input id="cp-date" type="date" className="cp-text" value={form.incidentDate} max={dhakaToday()} onChange={(e) => set('incidentDate', e.target.value)} />
        </Field>
        <Field label={t('Incident location')} htmlFor="cp-location" error={problems.incidentLocation}>
          <input id="cp-location" className="cp-text" value={form.incidentLocation} maxLength={200} onChange={(e) => set('incidentLocation', e.target.value)} placeholder={t('e.g. Near Farmgate footbridge')} />
        </Field>
      </div>
    </section>

    <section className="cp-form-section">
      <h3><span>4</span> {t('Evidence')} <em>· {t('optional')}</em></h3>
      <p className="cp-muted">{t('Photos or screenshots help us act faster. JPG, PNG or WEBP, up to 5 MB each, at most {count}. Only you and SmartCommute admins can see them.', { count: MAX_EVIDENCE })}</p>
      <input ref={fileInput} type="file" accept="image/jpeg,image/png,image/webp" multiple hidden onChange={addImages} aria-label={t('Add evidence pictures')} />
      <div className="cp-uploads">
        {form.evidence.map((e) => <div key={e.id} className="cp-upload-item"><img src={e.data} alt={t('Evidence preview')} />
          <button type="button" className="cp-icon small" onClick={() => setForm((f) => ({ ...f, evidence: f.evidence.filter((x) => x.id !== e.id) }))} aria-label={t('Remove picture')}><X size={14} /></button></div>)}
        {form.evidence.length < MAX_EVIDENCE && <button type="button" className="cp-upload" onClick={() => fileInput.current?.click()}><ImagePlus size={22} /><b>{t('Add pictures')}</b><small>{t('{count} of {max}', { count: form.evidence.length, max: MAX_EVIDENCE })}</small></button>}
      </div>
      {imageError && <small className="cp-error" role="alert">{imageError}</small>}
    </section>

    {error && <p className="cp-form-error" role="alert"><CircleAlert size={15} /> {error}</p>}
    <div className="cp-actions">
      <p className="cp-muted"><ShieldCheck size={14} /> {t('Your name and contact are only shared with SmartCommute admins.')}</p>
      <button type="submit" className="button cp-primary" disabled={busy} aria-busy={busy}>
        {busy ? <><Loader2 size={16} className="cp-spin" /> {form.evidence.length ? t('Uploading and submitting…') : t('Submitting…')}</> : <><Send size={16} /> {t('Submit complaint')}</>}
      </button>
    </div>
  </form>
}

// ---- My complaints ----

export function ComplaintStatusCard({ complaint: c, onOpen }) {
  return <article className={`cp-card ${c.status.toLowerCase()}`} data-testid="cp-card">
    <header><span className="cp-code">{c.code}</span><StatusBadge status={c.status} /></header>
    <h3>{c.subject}</h3>
    <ul className="cp-facts">
      <li><BusFront size={14} /> <span>{t(c.busName)}{c.routeName ? <small> · {c.routeName}</small> : null}</span></li>
      <li><span aria-hidden="true">{CATEGORY_ICON[c.category]}</span> <span>{t(c.category)}</span></li>
      <li><Calendar size={14} /> <span>{t('Happened {date}', { date: day(c.incidentDate) })} · {t('Sent {date}', { date: day(c.createdAt) })}</span></li>
    </ul>
    {c.adminNotes && <p className="cp-feedback"><MessageSquareText size={14} /> <span><b>{t('Admin feedback')}:</b> {c.adminNotes}</span></p>}
    <footer><small>{t('Updated {date}', { date: dayTime(c.updatedAt) })}</small>
      <button type="button" className="button button-outline button-small" onClick={() => onOpen(c)}><Eye size={14} /> {t('View details')}</button></footer>
  </article>
}

function ComplaintDetails({ id, token, onClose, onDeleted, notify }) {
  const [state, setState] = useState({ status: 'loading' })
  const [confirm, setConfirm] = useState(false)
  const [busy, setBusy] = useState(false)
  useEffect(() => {
    complaintApi.get(id, token).then((res) => setState({ status: 'ready', c: res.data })).catch((e) => setState({ status: 'error', error: errorText(e) }))
  }, [id, token])
  const c = state.c
  const remove = async () => {
    setBusy(true)
    try { const res = await complaintApi.remove(c.id, token); notify(t(res.message || 'Complaint deleted.')); onDeleted() } catch (e) { notify(errorText(e), 'error'); setBusy(false) }
  }
  return <CpModal title={c ? c.subject : t('Complaint')} subtitle={c?.code} onClose={onClose} wide>
    {state.status === 'loading' ? <div className="cp-center"><Loader2 size={20} className="cp-spin" /> {t('Loading…')}</div>
      : state.status === 'error' ? <p className="cp-form-error" role="alert"><CircleAlert size={15} /> {state.error}</p>
        : <div className="cp-details">
          <div className="cp-details-main">
            <div className="cp-details-top"><StatusBadge status={c.status} large /><span className="cp-chip">{CATEGORY_ICON[c.category]} {t(c.category)}</span></div>
            {c.adminNotes && <div className="cp-feedback big"><MessageSquareText size={16} /><div><b>{t('Feedback from SmartCommute')}</b><p>{c.adminNotes}</p></div></div>}
            <dl className="cp-dl">
              <div><dt><BusFront size={14} /> {t('Bus')}</dt><dd>{t(c.busName)}</dd></div>
              <div><dt><RouteIcon size={14} /> {t('Route')}</dt><dd>{c.routeName || '—'}</dd></div>
              {c.stopName && <div><dt><MapPin size={14} /> {t('Stop')}</dt><dd>{t(c.stopName)}</dd></div>}
              <div><dt><MapPin size={14} /> {t('Location')}</dt><dd>{c.incidentLocation}</dd></div>
              <div><dt><Calendar size={14} /> {t('Incident date')}</dt><dd>{day(c.incidentDate)}</dd></div>
              <div><dt><Calendar size={14} /> {t('Submitted')}</dt><dd>{dayTime(c.createdAt)}</dd></div>
            </dl>
            <h4>{t('Description')}</h4>
            <p className="cp-description">{c.description}</p>
            <h4>{t('Evidence')}</h4>
            <EvidenceGallery evidence={c.evidence} token={token} />
          </div>
          <aside className="cp-details-side">
            <h4>{t('Progress')}</h4>
            <Timeline history={c.history} status={c.status} />
            {c.canDelete && (confirm ? <div className="cp-confirm" role="alertdialog" aria-label={t('Delete complaint')}>
              <p>{t('Delete this complaint? This cannot be undone.')}</p>
              <div><button type="button" className="button button-outline button-small" onClick={() => setConfirm(false)} disabled={busy}>{t('Cancel')}</button>
                <button type="button" className="button button-small cp-danger" onClick={remove} disabled={busy}>{busy ? <Loader2 size={14} className="cp-spin" /> : <Trash2 size={14} />} {t('Yes, delete')}</button></div>
            </div> : <button type="button" className="button button-outline button-small cp-danger-outline" onClick={() => setConfirm(true)}><Trash2 size={14} /> {t('Delete complaint')}</button>)}
            {!c.canDelete && <p className="cp-muted">{t('Complaints can be deleted only while they are pending.')}</p>}
          </aside>
        </div>}
  </CpModal>
}

export function MyComplaintsSection({ token, refreshKey, onOpen, onNew }) {
  const [state, setState] = useState({ status: 'loading', items: [] })
  const [filter, setFilter] = useState('all')
  const load = useCallback(async () => {
    try { const res = await complaintApi.my(token); setState({ status: 'ready', items: res.data || [] }) } catch (e) { setState({ status: 'error', items: [], error: errorText(e) }) }
  }, [token])
  useEffect(() => { load() }, [load, refreshKey])
  const counts = Object.fromEntries(STATUSES.map((s) => [s, state.items.filter((c) => c.status === s).length]))
  const shown = filter === 'all' ? state.items : state.items.filter((c) => c.status === filter)
  return <section className="cp-panel" aria-labelledby="cp-my-title">
    <header className="cp-panel-head"><div><h2 id="cp-my-title">{t('My complaints')}</h2><p>{t('Follow each complaint until it is resolved.')}</p></div></header>
    <div className="cp-filter" role="group" aria-label={t('Filter by status')}>
      <button type="button" aria-pressed={filter === 'all'} className={filter === 'all' ? 'selected' : ''} onClick={() => setFilter('all')}>{t('All')} <span>{formatNumber(state.items.length)}</span></button>
      {STATUSES.map((s) => <button key={s} type="button" aria-pressed={filter === s} className={`${filter === s ? 'selected' : ''} ${s.toLowerCase()}`} onClick={() => setFilter(s)}>{t(STATUS_LABELS[s])} <span>{formatNumber(counts[s])}</span></button>)}
    </div>
    {state.status === 'loading' ? <div className="cp-skeletons">{[0, 1, 2].map((i) => <i key={i} />)}</div>
      : state.status === 'error' ? <p className="cp-form-error" role="alert"><CircleAlert size={15} /> {state.error} <button type="button" className="cp-link" onClick={load}>{t('Try again')}</button></p>
        : !shown.length ? <div className="cp-empty"><ClipboardList size={28} /><b>{state.items.length ? t('No complaints with this status') : t('You have not made any complaints')}</b>
          <span>{t('If something goes wrong on a Dhaka bus, tell us and we will look into it.')}</span>
          {!state.items.length && <button type="button" className="button cp-primary button-small" onClick={onNew}><FileWarning size={14} /> {t('Make a complaint')}</button>}</div>
          : <div className="cp-cards">{shown.map((c) => <ComplaintStatusCard key={c.id} complaint={c} onOpen={onOpen} />)}</div>}
  </section>
}

// ---- Page ----

export function ComplaintPage({ token, signedIn, openAuth, notify = () => {} }) {
  const [tab, setTab] = useState('new')
  const [refreshKey, setRefreshKey] = useState(0)
  const [openId, setOpenId] = useState(null)
  const [done, setDone] = useState(null)
  const submitted = (result) => {
    setDone(result)
    setRefreshKey((k) => k + 1)
    notify(t(result.message || 'Complaint submitted.'))
    window.scrollTo?.({ top: 0, behavior: 'smooth' })
  }
  const hero = <section className="cp-hero">
    <div><span className="cp-eyebrow">{t('COMPLAINT & FEEDBACK · DHAKA CITY BUSES')}</span>
      <h1>{t('Had a problem on a bus?')}</h1>
      <p>{t('Report overcharging, misbehaviour, unsafe driving or any other problem on a Dhaka bus. We review every complaint and tell you what we did.')}</p></div>
    <ol className="cp-hero-steps">
      <li><b>1</b><span>{t('You report')}</span></li><li><b>2</b><span>{t('We review')}</span></li><li><b>3</b><span>{t('You get feedback')}</span></li>
    </ol>
  </section>
  if (!signedIn) return <div className="page inner-page cp-page">{hero}<RequireSignIn openAuth={openAuth} /></div>
  return <div className="page inner-page cp-page">
    {hero}
    <nav className="cp-tabs" role="tablist" aria-label={t('Complaint sections')}>
      <button type="button" role="tab" aria-selected={tab === 'new'} className={tab === 'new' ? 'selected' : ''} onClick={() => setTab('new')}><FileWarning size={16} /> {t('New complaint')}</button>
      <button type="button" role="tab" aria-selected={tab === 'mine'} className={tab === 'mine' ? 'selected' : ''} onClick={() => setTab('mine')}><ClipboardList size={16} /> {t('My complaints')}</button>
    </nav>
    {done && <div className="cp-success" role="status" data-testid="cp-success">
      <span><Check size={20} /></span>
      <div><b>{t('Complaint submitted')}</b><small>{t(done.message)}</small></div>
      <button type="button" className="button button-outline button-small" onClick={() => { setTab('mine'); setDone(null) }}>{t('Track it')}</button>
      <button type="button" className="cp-icon" onClick={() => setDone(null)} aria-label={t('Close')}><X size={16} /></button>
    </div>}
    {tab === 'new'
      ? <section className="cp-panel"><header className="cp-panel-head"><div><h2>{t('Tell us what happened')}</h2><p>{t('It takes about two minutes. Fields marked optional can be left empty.')}</p></div></header>
        <ComplaintForm token={token} onSubmitted={submitted} /></section>
      : <MyComplaintsSection token={token} refreshKey={refreshKey} onOpen={(c) => setOpenId(c.id)} onNew={() => setTab('new')} />}
    {openId && <ComplaintDetails id={openId} token={token} notify={notify} onClose={() => setOpenId(null)} onDeleted={() => { setOpenId(null); setRefreshKey((k) => k + 1) }} />}
  </div>
}
