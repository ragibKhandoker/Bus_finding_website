import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  ArrowRight, BusFront, Calendar, Camera, Check, CircleAlert, Clock3, Copy, Edit3, ImagePlus, Loader2, MapPin, PackageSearch, Phone, Plus,
  RefreshCw, Search, ShieldCheck, Trash2, X,
} from 'lucide-react'
import { useBusRoutes } from '../../controllers/useBusRoutes'
import { formatDate, formatNumber, t } from '../../models/i18n'
import {
  ALL_STATUSES, CATEGORIES, CATEGORY_ICONS, OWNER_STATUSES, STATUS_LABELS, dhakaToday, emptyForm, formFromReport, formProblems, imageSrc,
  incidentText, lostFoundApi, readImage, toBody,
} from '../../models/lostFound'
import { searchKey } from '../components/BusNameSearch'
import { ChatWindow, ContactByMessage, ConversationList } from '../components/LostFoundChat'
import { RequireSignIn } from './MorePages'
import '../styles/lost-found.css'

// F17 Lost & Found: browse lost and found items reported on Dhaka buses, report one, and manage your own reports.
// Everything comes from the server; nothing on this page is made up.

const errorText = (error) => t(error?.message || 'Something went wrong. Please try again.')

function TypeBadge({ type }) {
  return <span className={`lf-type ${type}`}>{type === 'lost' ? t('LOST') : t('FOUND')}</span>
}

function StatusBadge({ status }) {
  return <span className={`lf-status ${status}`}>{t(STATUS_LABELS[status] || status)}</span>
}

function Modal({ title, onClose, children, wide = false, chat = false }) {
  useEffect(() => {
    const onKey = (event) => { if (event.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    const overflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => { document.removeEventListener('keydown', onKey); document.body.style.overflow = overflow }
  }, [onClose])
  return <div className="lf-backdrop" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose() }}>
    <div className={`lf-modal${wide ? ' wide' : ''}${chat ? ' chat' : ''}`} role="dialog" aria-modal="true" aria-label={title}>
      <header className="lf-modal-head"><h2>{title}</h2><button type="button" className="lf-icon" onClick={onClose} aria-label={t('Close')}><X size={18} /></button></header>
      <div className="lf-modal-body">{children}</div>
    </div>
  </div>
}

// ---- Card ----

export function LostItemCard({ report, onOpen }) {
  const [broken, setBroken] = useState(false)
  return <article className={`lf-card ${report.reportType}`} data-testid="lf-card">
    <button type="button" className="lf-card-image" onClick={() => onOpen(report)} aria-label={t('View details of {item}', { item: report.itemName })}>
      {report.imageUrl && !broken
        ? <img src={imageSrc(report.imageUrl)} alt="" loading="lazy" onError={() => setBroken(true)} />
        : <span className="lf-placeholder" aria-hidden="true">{CATEGORY_ICONS[report.category] || '📦'}</span>}
      <TypeBadge type={report.reportType} />
    </button>
    <div className="lf-card-body">
      <div className="lf-card-top"><h3>{report.itemName}</h3><StatusBadge status={report.status} /></div>
      <span className="lf-category">{t(report.category)}</span>
      <ul className="lf-facts">
        <li><BusFront size={14} /> <span>{t(report.busName)}{report.route ? <small> · {report.route}</small> : null}</span></li>
        <li><MapPin size={14} /> <span>{report.location}</span></li>
        <li><Calendar size={14} /> <span>{incidentText(report)}</span></li>
      </ul>
      <button type="button" className="lf-view" onClick={() => onOpen(report)}>{t('View details')} <ArrowRight size={14} /></button>
    </div>
  </article>
}

// ---- Bus picker: every Dhaka route from the route list ----

function BusPicker({ value, busName, onPick, invalid }) {
  const [routes] = useBusRoutes()
  const [text, setText] = useState(busName || '')
  const [open, setOpen] = useState(false)
  const box = useRef(null)
  useEffect(() => { setText(busName || '') }, [busName])
  useEffect(() => {
    if (!open) return
    const away = (event) => { if (!box.current?.contains(event.target)) setOpen(false) }
    document.addEventListener('pointerdown', away)
    return () => document.removeEventListener('pointerdown', away)
  }, [open])
  const all = useMemo(() => (routes.status === 'ready' ? routes.buses : []).map((bus) => ({
    id: bus._id || bus.id, name: bus.busName, route: bus.routeNumber,
    ends: bus.stops?.length ? `${t(bus.stops[0].name)} → ${t(bus.stops[bus.stops.length - 1].name)}` : '',
  })).filter((o) => o.id && o.name).sort((a, b) => a.name.localeCompare(b.name)), [routes])
  const list = useMemo(() => {
    const q = searchKey(text)
    if (!q || all.some((o) => o.id === value && o.name === text)) return all.slice(0, 60)
    return all.filter((o) => searchKey(o.name).includes(q) || searchKey(t(o.name)).includes(q) || searchKey(o.route).startsWith(q)).slice(0, 60)
  }, [all, text, value])
  return <div className="lf-picker" ref={box}>
    <span className={`lf-input${invalid ? ' invalid' : ''}`}><BusFront size={16} aria-hidden="true" />
      <input id="lf-bus" value={text} role="combobox" aria-expanded={open} aria-controls="lf-bus-list" autoComplete="off"
        placeholder={routes.status === 'loading' ? t('Loading Dhaka buses…') : t('Type a bus name or route number')} aria-invalid={invalid || undefined}
        onFocus={() => setOpen(true)} onChange={(e) => { setText(e.target.value); setOpen(true); if (value) onPick(null) }} />
    </span>
    {open && <ul className="lf-options" id="lf-bus-list" role="listbox" aria-label={t('Dhaka buses')}>
      {!list.length ? <li className="lf-option-note">{t('No Dhaka bus matches. Try another name or route number.')}</li>
        : list.map((o) => <li key={o.id} role="option" aria-selected={o.id === value} className={o.id === value ? 'selected' : ''}
          onPointerDown={(e) => e.preventDefault()} onClick={() => { onPick(o); setText(o.name); setOpen(false) }}>
          <b>{t(o.name)}</b><small>{o.route ? `${t('Route {number}', { number: o.route })}${o.ends ? ' · ' : ''}` : ''}{o.ends}</small>
        </li>)}
    </ul>}
  </div>
}

// ---- Form ----

function Field({ label, error, hint, htmlFor, children, optional = false }) {
  return <div className={`lf-field${error ? ' has-error' : ''}`}>
    <label htmlFor={htmlFor}>{label}{optional && <em> · {t('optional')}</em>}</label>
    {children}
    {error ? <small className="lf-error" role="alert">{error}</small> : hint ? <small className="lf-hint">{hint}</small> : null}
  </div>
}

export function LostFoundForm({ token, initial, editing, onDone, onCancel }) {
  const [form, setForm] = useState(initial)
  const [problems, setProblems] = useState({})
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [imageError, setImageError] = useState('')
  const fileInput = useRef(null)
  const set = (key) => (e) => { setForm((f) => ({ ...f, [key]: e.target.value })); setProblems((p) => ({ ...p, [key]: undefined })) }
  const lost = form.reportType === 'lost'

  const pickImage = async (event) => {
    const file = event.target.files?.[0]
    event.target.value = ''
    if (!file) return
    try {
      const data = await readImage(file)
      setForm((f) => ({ ...f, image: data, imagePreview: data, removeImage: false }))
      setImageError('')
    } catch (e) { setImageError(e.message) }
  }
  const dropImage = () => setForm((f) => ({ ...f, image: '', imagePreview: '', removeImage: Boolean(editing) }))

  const submit = async (event) => {
    event.preventDefault()
    if (busy) return
    const found = formProblems(form)
    setProblems(found)
    if (Object.keys(found).length) { setError(t('Please fix the highlighted fields.')); return }
    setBusy(true); setError('')
    try {
      const result = editing ? await lostFoundApi.update(editing.id, toBody(form), token) : await lostFoundApi.create(toBody(form), token)
      onDone(result)
    } catch (e) { setError(errorText(e)); setBusy(false) }
  }

  return <form className="lf-form" onSubmit={submit} noValidate>
    <div className="lf-switch" role="radiogroup" aria-label={t('Report type')}>
      {[['lost', 'I lost something', 'Tell people what you lost'], ['found', 'I found something', 'Help the owner get it back']].map(([value, title, text]) =>
        <label key={value} className={`lf-switch-option ${value}${form.reportType === value ? ' selected' : ''}`}>
          <input type="radio" name="lf-type" value={value} checked={form.reportType === value} onChange={set('reportType')} />
          <b>{t(title)}</b><small>{t(text)}</small>
        </label>)}
    </div>

    <div className="lf-grid">
      <Field label={t('Item name')} htmlFor="lf-item" error={problems.itemName}>
        <input id="lf-item" className="lf-text" value={form.itemName} onChange={set('itemName')} maxLength={100} placeholder={t('e.g. Black Samsung phone')} />
      </Field>
      <Field label={t('Category')} htmlFor="lf-category" error={problems.category}>
        <select id="lf-category" className="lf-text" value={form.category} onChange={set('category')}>
          <option value="">{t('Choose a category')}</option>
          {CATEGORIES.map((c) => <option key={c} value={c}>{CATEGORY_ICONS[c]} {t(c)}</option>)}
        </select>
      </Field>
      <Field label={t('Bus and route')} htmlFor="lf-bus" error={problems.busId} hint={t('Only Dhaka city buses are listed.')}>
        <BusPicker value={form.busId} busName={form.busName} invalid={Boolean(problems.busId)}
          onPick={(o) => { setForm((f) => ({ ...f, busId: o?.id || '', busName: o?.name || f.busName })); setProblems((p) => ({ ...p, busId: undefined })) }} />
      </Field>
      <Field label={lost ? t('Where did you lose it?') : t('Where did you find it?')} htmlFor="lf-location" error={problems.location}>
        <input id="lf-location" className="lf-text" value={form.location} onChange={set('location')} maxLength={200} placeholder={t('e.g. Back seat, near Farmgate stop')} />
      </Field>
      <Field label={t('Date')} htmlFor="lf-date" error={problems.incidentDate}>
        <input id="lf-date" type="date" className="lf-text" value={form.incidentDate} max={dhakaToday()} onChange={set('incidentDate')} />
      </Field>
      <Field label={t('Time')} htmlFor="lf-time" error={problems.incidentTime} optional>
        <input id="lf-time" type="time" className="lf-text" value={form.incidentTime} onChange={set('incidentTime')} />
      </Field>
      <Field label={t('Description')} htmlFor="lf-description" error={problems.description} hint={`${formatNumber(form.description.trim().length)}/1000 · ${t('Colour, brand, marks, what was inside…')}`}>
        <textarea id="lf-description" className="lf-text" rows={4} value={form.description} onChange={set('description')} maxLength={1000}
          placeholder={lost ? t('e.g. Black phone with a blue cover, a crack on the top corner.') : t('e.g. Brown leather wallet. Describe it, but keep a detail back so the owner can prove it is theirs.')} />
      </Field>
      <Field label={t('Contact information')} htmlFor="lf-contact" error={problems.contactInformation} hint={t('Shown to signed-in SmartCommute users so they can reach you.')}>
        <input id="lf-contact" className="lf-text" value={form.contactInformation} onChange={set('contactInformation')} maxLength={150} placeholder={t('e.g. 01712-345678')} />
      </Field>
      <Field label={t('Picture')} htmlFor="lf-image" error={imageError} hint={t('JPG, PNG or WEBP, up to 5 MB.')} optional>
        <input ref={fileInput} id="lf-image" type="file" accept="image/jpeg,image/png,image/webp" onChange={pickImage} hidden />
        {form.imagePreview
          ? <div className="lf-preview"><img src={form.imagePreview} alt={t('Preview of the picture')} />
            <div><button type="button" className="button button-outline button-small" onClick={() => fileInput.current?.click()}><Camera size={14} /> {t('Change')}</button>
              <button type="button" className="button button-outline button-small" onClick={dropImage}><Trash2 size={14} /> {t('Remove')}</button></div></div>
          : <button type="button" className="lf-upload" onClick={() => fileInput.current?.click()}><ImagePlus size={22} /><b>{t('Add a picture')}</b><small>{t('A clear photo helps people recognise the item.')}</small></button>}
      </Field>
      {editing && <Field label={t('Status')} htmlFor="lf-status">
        <select id="lf-status" className="lf-text" value={form.status} onChange={set('status')}>
          {(OWNER_STATUSES.includes(form.status) ? OWNER_STATUSES : [form.status, ...OWNER_STATUSES]).map((s) => <option key={s} value={s}>{t(STATUS_LABELS[s])}</option>)}
        </select>
      </Field>}
    </div>

    {error && <p className="lf-form-error" role="alert"><CircleAlert size={15} /> {error}</p>}
    <div className="lf-actions">
      <button type="button" className="button button-outline" onClick={onCancel} disabled={busy}>{t('Cancel')}</button>
      <button type="submit" className={`button button-green${lost ? '' : ''}`} disabled={busy} aria-busy={busy}>
        {busy ? <><Loader2 size={15} className="lf-spin" /> {t('Saving…')}</> : <><Check size={15} /> {editing ? t('Save changes') : t('Submit report')}</>}
      </button>
    </div>
  </form>
}

// ---- Details ----

function ReportDetails({ id, token, isAdmin, onClose, onEdit, onDeleted, notify, onOpenChat }) {
  const [state, setState] = useState({ status: 'loading' })
  const [confirm, setConfirm] = useState(false)
  const [busy, setBusy] = useState(false)
  const [adminStatus, setAdminStatus] = useState('')
  const [note, setNote] = useState('')
  const load = useCallback(async () => {
    try { const res = await lostFoundApi.get(id, token); setState({ status: 'ready', report: res.data, matches: res.matches || [] }); setAdminStatus(res.data.status) } catch (e) { setState({ status: 'error', error: errorText(e) }) }
  }, [id, token])
  useEffect(() => { load() }, [load])
  const r = state.report
  const remove = async () => {
    setBusy(true)
    try { const res = await lostFoundApi.remove(r.id, token); notify(t(res.message || 'Report deleted.')); onDeleted() } catch (e) { notify(errorText(e), 'error'); setBusy(false) }
  }
  const saveStatus = async () => {
    setBusy(true)
    try { const res = await lostFoundApi.setStatus(r.id, adminStatus, note.trim() || null, token); notify(t(res.message)); setNote(''); await load(); onDeleted(false) } catch (e) { notify(errorText(e), 'error') } finally { setBusy(false) }
  }
  const copy = async () => {
    try { await navigator.clipboard.writeText(r.contactInformation); notify(t('Contact copied.')) } catch { notify(t('Could not copy. Select the text and copy it yourself.'), 'error') }
  }
  const phone = r?.contactInformation.replace(/[^\d+]/g, '')
  return <Modal title={r ? r.itemName : t('Report')} onClose={onClose} wide>
    {state.status === 'loading' ? <div className="lf-center"><Loader2 size={20} className="lf-spin" /> {t('Loading…')}</div>
      : state.status === 'error' ? <p className="lf-form-error" role="alert"><CircleAlert size={15} /> {state.error}</p>
        : <div className="lf-details">
          <div className="lf-details-media">
            {r.imageUrl ? <img src={imageSrc(r.imageUrl)} alt={r.itemName} /> : <span className="lf-placeholder big" aria-hidden="true">{CATEGORY_ICONS[r.category] || '📦'}</span>}
          </div>
          <div className="lf-details-info">
            <div className="lf-badges"><TypeBadge type={r.reportType} /><StatusBadge status={r.status} /><span className="lf-category">{t(r.category)}</span></div>
            <dl className="lf-dl">
              <div><dt><BusFront size={14} /> {t('Bus')}</dt><dd>{t(r.busName)}{r.route && <small>{r.route}</small>}</dd></div>
              <div><dt><MapPin size={14} /> {r.reportType === 'lost' ? t('Lost near') : t('Found near')}</dt><dd>{r.location}</dd></div>
              <div><dt><Calendar size={14} /> {t('When')}</dt><dd>{incidentText(r)}</dd></div>
              <div><dt><Clock3 size={14} /> {t('Reported')}</dt><dd>{formatDate(r.createdAt, { day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit' })} · {t('by {name}', { name: r.reporterName })}</dd></div>
            </dl>
            <p className="lf-description">{r.description}</p>
            {r.adminNote && <p className="lf-note"><ShieldCheck size={14} /> {t('Admin note')}: {r.adminNote}</p>}
            {!r.isMine && <div className="lf-contact">
              <b>{r.reportType === 'lost' ? t('Found it? Contact the owner') : t('Is it yours? Contact the finder')}</b>
              <ContactByMessage report={r} token={token} onOpenChat={onOpenChat} />
              <small className="lf-or">{t('Or use the contact they gave:')}</small>
              <span className="lf-contact-value">{r.contactInformation}</span>
              <div>{phone.length >= 8 && <a className="button button-green button-small" href={`tel:${phone}`}><Phone size={14} /> {t('Call')}</a>}
                <button type="button" className="button button-outline button-small" onClick={copy}><Copy size={14} /> {t('Copy')}</button></div>
              <small>{t('Meet in a busy public place, and ask the other person to describe the item before handing it over.')}</small>
            </div>}
            {r.isMine && <div className="lf-owner-chats">
              <b>{t('Messages about this report')}</b>
              <ConversationList token={token} reportId={r.id} onOpen={onOpenChat} />
            </div>}
            {r.canManage && <div className="lf-manage">
              {confirm ? <div className="lf-confirm" role="alertdialog" aria-label={t('Delete report')}>
                <p>{t('Delete this report? This cannot be undone.')}</p>
                <div><button type="button" className="button button-outline button-small" onClick={() => setConfirm(false)} disabled={busy}>{t('Cancel')}</button>
                  <button type="button" className="button button-small lf-danger" onClick={remove} disabled={busy}>{busy ? <Loader2 size={14} className="lf-spin" /> : <Trash2 size={14} />} {t('Yes, delete')}</button></div>
              </div> : <div className="lf-manage-row">
                {r.isMine && <button type="button" className="button button-outline button-small" onClick={() => onEdit(r)}><Edit3 size={14} /> {t('Edit')}</button>}
                <button type="button" className="button button-outline button-small lf-danger-outline" onClick={() => setConfirm(true)}><Trash2 size={14} /> {t('Delete')}</button>
              </div>}
              {isAdmin && <div className="lf-admin">
                <b><ShieldCheck size={14} /> {t('Admin review')}</b>
                <div><select value={adminStatus} onChange={(e) => setAdminStatus(e.target.value)} aria-label={t('Status')}>{ALL_STATUSES.map((s) => <option key={s} value={s}>{t(STATUS_LABELS[s])}</option>)}</select>
                  <input value={note} onChange={(e) => setNote(e.target.value)} maxLength={300} placeholder={t('Note for the reporter (optional)')} aria-label={t('Note for the reporter')} />
                  <button type="button" className="button button-green button-small" onClick={saveStatus} disabled={busy}>{t('Save')}</button></div>
              </div>}
            </div>}
          </div>
          {state.matches.length > 0 && <div className="lf-matches">
            <h3><PackageSearch size={16} /> {r.reportType === 'lost' ? t('Found items that may be yours') : t('Lost reports that may match')}</h3>
            <ul>{state.matches.map((m) => <li key={m.id}><TypeBadge type={m.reportType} /><span><b>{m.itemName}</b><small>{t(m.busName)} · {m.location} · {incidentText(m)}</small></span>
              <span className="lf-match-contact">{m.contactInformation}</span></li>)}</ul>
          </div>}
        </div>}
  </Modal>
}

// ---- My reports ----

export function MyLostFoundReports({ token, onOpen, onEdit, refreshKey }) {
  const [state, setState] = useState({ status: 'loading', items: [] })
  const load = useCallback(async () => {
    setState((s) => ({ ...s, status: s.items.length ? 'ready' : 'loading' }))
    try { const res = await lostFoundApi.my(token); setState({ status: 'ready', items: res.data || [] }) } catch (e) { setState({ status: 'error', items: [], error: errorText(e) }) }
  }, [token])
  useEffect(() => { load() }, [load, refreshKey])
  if (state.status === 'loading') return <div className="lf-skeletons">{[0, 1, 2].map((i) => <i key={i} />)}</div>
  if (state.status === 'error') return <p className="lf-form-error" role="alert"><CircleAlert size={15} /> {state.error} <button type="button" className="lf-link" onClick={load}>{t('Try again')}</button></p>
  if (!state.items.length) return <div className="lf-empty"><PackageSearch size={26} /><b>{t('You have not made any reports yet')}</b><span>{t('Lost or found something on a bus? Report it and it will appear here.')}</span></div>
  return <ul className="lf-mine">{state.items.map((r) => <li key={r.id} data-testid="lf-mine">
    <span className="lf-mine-icon" aria-hidden="true">{r.imageUrl ? <img src={imageSrc(r.imageUrl)} alt="" /> : CATEGORY_ICONS[r.category] || '📦'}</span>
    <span className="lf-mine-text"><span className="lf-badges"><TypeBadge type={r.reportType} /><StatusBadge status={r.status} /></span>
      <b>{r.itemName}</b><small>{t(r.busName)} · {r.location} · {incidentText(r)}</small></span>
    <span className="lf-mine-actions">
      <button type="button" className="button button-outline button-small" onClick={() => onOpen(r)}>{t('View')}</button>
      <button type="button" className="button button-outline button-small" onClick={() => onEdit(r)} aria-label={t('Edit {item}', { item: r.itemName })}><Edit3 size={14} /> {t('Edit')}</button>
    </span>
  </li>)}</ul>
}

// ---- Page ----

const VIEWS = [['all', 'All reports'], ['lost', 'Lost items'], ['found', 'Found items'], ['mine', 'My reports'], ['messages', 'Messages']]

export function LostFoundPage({ token, signedIn, user, openAuth, notify = () => {} }) {
  const isAdmin = user?.role === 'admin'
  const [view, setView] = useState('all')
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [category, setCategory] = useState('')
  const [list, setList] = useState({ status: 'loading', items: [], total: 0, page: 1 })
  const [openId, setOpenId] = useState(null)
  const [form, setForm] = useState(null) // { initial, editing }
  const [done, setDone] = useState(null)
  const [refreshKey, setRefreshKey] = useState(0)
  const [chat, setChat] = useState(null) // the conversation open in the chat window
  const [unread, setUnread] = useState(0)

  // How many messages wait to be read, checked now and every 30 seconds.
  useEffect(() => {
    if (!signedIn) return
    let live = true
    const check = () => lostFoundApi.conversations(token).then((res) => { if (live) setUnread(res.unread || 0) }).catch(() => {})
    check()
    const id = window.setInterval(check, 30000)
    return () => { live = false; window.clearInterval(id) }
  }, [signedIn, token, refreshKey])
  // A notification link (/lost-found?chat=...) opens that conversation.
  useEffect(() => {
    const id = new URLSearchParams(window.location.search).get('chat')
    if (signedIn && id && /^[0-9a-f-]{36}$/i.test(id)) { setView('messages'); setChat({ id }) }
  }, [signedIn])
  const openChat = (c) => { setOpenId(null); setChat(c) }

  useEffect(() => { const id = window.setTimeout(() => setQuery(search.trim()), 300); return () => window.clearTimeout(id) }, [search])
  const load = useCallback(async (page = 1) => {
    if (!signedIn || view === 'mine') return
    if (page === 1) setList((l) => ({ ...l, status: 'loading' }))
    try {
      const res = await lostFoundApi.all({ type: view === 'all' ? '' : view, category, search: query, page, pageSize: 12 }, token)
      setList((l) => ({ status: 'ready', items: page === 1 ? res.data : [...l.items, ...res.data], total: res.total, page }))
    } catch (e) { setList({ status: 'error', items: [], total: 0, page: 1, error: errorText(e) }) }
  }, [signedIn, view, category, query, token])
  useEffect(() => { load(1) }, [load])

  const startReport = (type) => { setDone(null); setForm({ initial: emptyForm(type), editing: null }) }
  const startEdit = (report) => { setOpenId(null); setDone(null); setForm({ initial: formFromReport(report), editing: report }) }
  const finished = (result) => {
    setForm(null)
    setDone(result)
    setRefreshKey((k) => k + 1)
    load(1)
  }
  const changed = (close = true) => { if (close) setOpenId(null); setRefreshKey((k) => k + 1); load(1) }

  const hero = <section className="lf-hero">
    <div className="lf-hero-text">
      <span className="lf-eyebrow">{t('LOST & FOUND · DHAKA CITY BUSES')}</span>
      <h1>{t('Lost something on a bus?')}</h1>
      <p>{t('Report what you lost or found on a Dhaka bus, and help each other get things back.')}</p>
    </div>
    {signedIn && <div className="lf-hero-actions">
      <button type="button" className="button lf-btn-lost" onClick={() => startReport('lost')}><Search size={16} /> {t('I lost something')}</button>
      <button type="button" className="button lf-btn-found" onClick={() => startReport('found')}><Plus size={16} /> {t('I found something')}</button>
    </div>}
  </section>

  if (!signedIn) return <div className="page inner-page lf-page">{hero}<RequireSignIn openAuth={openAuth} /></div>

  return <div className="page inner-page lf-page">
    {hero}
    <ol className="lf-how">
      <li><span>1</span><b>{t('Report it')}</b><small>{t('Tell us what, which bus and where.')}</small></li>
      <li><span>2</span><b>{t('Others see it')}</b><small>{t('Passengers check the list and possible matches.')}</small></li>
      <li><span>3</span><b>{t('Get in touch')}</b><small>{t('Message each other here, return the item and mark it resolved.')}</small></li>
    </ol>

    {done && <div className="lf-success" role="status" data-testid="lf-success">
      <span><Check size={20} /></span>
      <div><b>{t(done.message || 'Report saved.')}</b><small>{t('You can see and edit it under My reports.')}</small></div>
      <button type="button" className="button button-outline button-small" onClick={() => { setView('mine'); setDone(null) }}>{t('My reports')}</button>
      <button type="button" className="lf-icon" onClick={() => setDone(null)} aria-label={t('Close')}><X size={16} /></button>
    </div>}

    <nav className="lf-tabs" role="tablist" aria-label={t('Lost & Found sections')}>
      {VIEWS.map(([id, label]) => <button key={id} type="button" role="tab" aria-selected={view === id} className={`${view === id ? 'selected' : ''} ${id}`} onClick={() => setView(id)}>
        {t(label)}{id === 'messages' && unread > 0 && <em className="lf-tab-count" aria-label={t('{count} unread', { count: unread })}>{formatNumber(unread)}</em>}</button>)}
    </nav>

    {view === 'messages'
      ? <section className="lf-panel"><ConversationList token={token} onOpen={openChat} refreshKey={refreshKey} onCount={setUnread} /></section>
      : view === 'mine'
      ? <section className="lf-panel"><MyLostFoundReports token={token} onOpen={(r) => setOpenId(r.id)} onEdit={startEdit} refreshKey={refreshKey} /></section>
      : <section className="lf-panel">
        <div className="lf-filters">
          <label className="lf-search"><Search size={16} aria-hidden="true" /><input value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('Search item, bus, route or place')} aria-label={t('Search reports')} />
            {search && <button type="button" onClick={() => setSearch('')} aria-label={t('Clear search')}><X size={14} /></button>}</label>
          <div className="lf-chips" role="group" aria-label={t('Category')}>
            {['', ...CATEGORIES].map((c) => <button key={c || 'all'} type="button" aria-pressed={category === c} className={category === c ? 'selected' : ''} onClick={() => setCategory(c)}>
              {c ? `${CATEGORY_ICONS[c]} ${t(c)}` : t('All categories')}</button>)}
          </div>
        </div>
        {list.status === 'loading' ? <div className="lf-grid-cards">{[0, 1, 2].map((i) => <div key={i} className="lf-card-skeleton" aria-hidden="true" />)}</div>
          : list.status === 'error' ? <p className="lf-form-error" role="alert"><CircleAlert size={15} /> {list.error} <button type="button" className="lf-link" onClick={() => load(1)}><RefreshCw size={13} /> {t('Try again')}</button></p>
            : !list.items.length ? <div className="lf-empty"><PackageSearch size={26} /><b>{query || category ? t('No reports match your search') : t('No reports yet')}</b>
              <span>{query || category ? t('Try other words or another category.') : t('When someone reports a lost or found item, it shows up here.')}</span></div>
              : <>
                <p className="lf-count">{t('{count} reports', { count: formatNumber(list.total) })}</p>
                <div className="lf-grid-cards">{list.items.map((r) => <LostItemCard key={r.id} report={r} onOpen={(x) => setOpenId(x.id)} />)}</div>
                {list.items.length < list.total && <button type="button" className="button button-outline lf-more" onClick={() => load(list.page + 1)}>{t('Show more')}</button>}
              </>}
      </section>}

    {form && <Modal title={form.editing ? t('Edit report') : form.initial.reportType === 'lost' ? t('Report a lost item') : t('Report a found item')} onClose={() => setForm(null)}>
      <LostFoundForm token={token} initial={form.initial} editing={form.editing} onDone={finished} onCancel={() => setForm(null)} />
    </Modal>}
    {openId && <ReportDetails id={openId} token={token} isAdmin={isAdmin} notify={notify} onClose={() => setOpenId(null)} onEdit={startEdit} onDeleted={changed} onOpenChat={openChat} />}
    {chat && <Modal chat title={chat.itemName ? t('Messages · {item}', { item: chat.itemName }) : t('Messages')} onClose={() => { setChat(null); setRefreshKey((k) => k + 1) }}>
      <ChatWindow key={chat.id || chat.report?.id} chat={chat} token={token} />
    </Modal>}
  </div>
}
