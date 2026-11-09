import React, { useCallback, useEffect, useMemo, useState } from 'react'
import {
  AlarmClock, ArrowDown, ArrowRight, ArrowUp, Bell, BellRing, BusFront, Check, CheckCheck, Clock3, FolderHeart, FolderPlus,
  ArrowUpDown, CalendarClock, GitCompareArrows, Heart, HeartOff, History, LineChart, Minus, Pencil, RefreshCw, Search, Trash2, X,
} from 'lucide-react'
import { formatDate, formatDateTime, formatNumber, t } from '../../models/i18n'
import { errorText } from '../../models/messages'
import { changed as notificationsChanged } from '../../models/notifications'
import { dhakaInputValue, dhakaParts, favouriteApi } from '../../models/favorites'
import { useFavoriteBuses } from '../../controllers/useFavorites'
import { useBusRoutes } from '../../controllers/useBusRoutes'
import { usePersistentState } from '../../controllers/usePersistentState'
import { Heading, RequireSignIn } from './MorePages'
import { StopSuggestions } from '../components/common'
import '../styles/favourites.css'

// The Smart Favourite Bus dashboard (Dhaka City buses only): favourites, collections, comparison, fare tracking,
// departure reminders, availability alerts, recently viewed buses and the module's notifications. Every figure
// comes from the server; nothing here is estimated or made up.

const money = (value) => (value == null ? '—' : `৳${formatNumber(value)}`)
const MAX_COMPARE = 3

const TABS = [
  ['favourites', 'Favourites', Heart],
  ['collections', 'Collections', FolderHeart],
  ['compare', 'Compare', GitCompareArrows],
  ['fares', 'Fare tracking', LineChart],
  ['reminders', 'Reminders', AlarmClock],
  ['alerts', 'Availability alerts', BellRing],
  ['recent', 'Recently viewed', History],
  ['notifications', 'Notifications', Bell],
]

// A list loaded from the server, with its status: loading | ready | an error message.
function useLoad(load, enabled) {
  const [state, setState] = useState({ status: enabled ? 'loading' : 'idle', data: null })
  const reload = useCallback(async () => {
    if (!enabled) return
    setState((s) => ({ ...s, status: s.data ? s.status : 'loading' }))
    try { setState({ status: 'ready', data: await load() }) } catch (e) { setState((s) => ({ ...s, status: errorText(e) })) }
  }, [load, enabled])
  useEffect(() => { reload() }, [reload])
  return [state, reload, setState]
}

const isError = (status) => status !== 'idle' && status !== 'loading' && status !== 'ready'

function Skeleton({ rows = 3 }) {
  return <div className="fd-skeleton" aria-busy="true" aria-label={t('Loading…')}>{Array.from({ length: rows }, (_, i) => <span key={i}><i /><b /><small /></span>)}</div>
}

function ErrorBox({ message, onRetry }) {
  return <div className="fd-error" role="alert"><span>{message}</span>{onRetry && <button type="button" className="link-button" onClick={onRetry}><RefreshCw size={13} /> {t('Try again')}</button>}</div>
}

function Empty({ icon: Icon, title, text, action }) {
  return <div className="fd-empty"><span><Icon size={22} /></span><b>{t(title)}</b>{text && <p>{t(text)}</p>}{action}</div>
}

function Section({ title, subtitle, actions, children }) {
  return <section className="extra-panel fd-section">
    <div className="fd-section-head"><div><h2>{t(title)}</h2>{subtitle && <p>{subtitle}</p>}</div>{actions}</div>
    {children}
  </section>
}

// ---------------- A. Favourite buses ----------------

function FavouritesSection({ favorites, collections, selected, onSelect, onAddToCollection, navigate, onCompare }) {
  const { items, status } = favorites
  const city = items.filter((bus) => bus.isDhakaCity)
  const outside = items.filter((bus) => !bus.isDhakaCity)
  return <Section title="Favourite Dhaka buses" subtitle={t('Tick up to three buses to compare them.')}
    actions={selected.length >= 2 && <button type="button" className="extra-button" onClick={onCompare}><GitCompareArrows size={14} /> {t('Compare {count}', { count: selected.length })}</button>}>
    {isError(status) && <ErrorBox message={status} onRetry={favorites.reload} />}
    {status === 'loading' && !items.length ? <Skeleton />
      : !city.length && !outside.length ? <Empty icon={HeartOff} title="No favourite buses yet" text="Search for a Dhaka City bus and tap the heart on its card to save it here."
        action={<button type="button" className="extra-button" onClick={() => navigate('Find a bus')}><Search size={14} /> {t('Find a bus')}</button>} />
      : <div className="fav-grid">{city.map((bus) => <article key={bus.busId} className={`extra-panel fav-card${selected.includes(bus.busId) ? ' is-selected' : ''}`}>
        <div className="fav-card-head">
          <span className="fav-card-icon"><BusFront size={19} /></span>
          <div><h3>{t(bus.busName)}</h3><p>{[bus.operator, bus.busType && t(bus.busType)].filter(Boolean).map((text) => t(text)).join(' · ') || t('Dhaka City bus')}</p></div>
          {bus.routeNumber && <span className="route-badge">{bus.routeNumber}</span>}
        </div>
        {bus.firstStop && <div className="fav-route"><span><i className="start" />{t(bus.firstStop)}</span><ArrowRight size={13} /><span><i className="end" />{t(bus.lastStop)}</span></div>}
        <div className="fav-facts">
          {bus.stopCount != null && <span><small>{t('Stops')}</small><b>{formatNumber(bus.stopCount)}</b></span>}
          <span><small>{t('Fare from')}</small><b>{money(bus.base_fare)}</b></span>
          <span><small>{t('Saved on')}</small><b>{formatDate(bus.createdAt)}</b></span>
        </div>
        <div className="fd-card-tools">
          <label className="fd-check"><input type="checkbox" checked={selected.includes(bus.busId)} disabled={!selected.includes(bus.busId) && selected.length >= MAX_COMPARE} onChange={() => onSelect(bus.busId)} /> {t('Compare')}</label>
          {collections.length > 0 && <select className="fd-select" value="" aria-label={t('Add to a collection')} onChange={(e) => { if (e.target.value) onAddToCollection(e.target.value, bus.busId) }}>
            <option value="">{t('Add to collection…')}</option>
            {collections.map((c) => <option key={c._id} value={c._id}>{c.name}</option>)}
          </select>}
        </div>
        <div className="fav-actions">
          {bus.routeNumber && <button type="button" className="extra-button" onClick={() => navigate('Bus details', { route: bus.routeNumber })}>{t('View details')} <ArrowRight size={13} /></button>}
          <button type="button" className="extra-button secondary fav-remove" disabled={favorites.isPending(bus)} onClick={() => favorites.toggle(bus)}>
            {favorites.isPending(bus) ? <span className="favorite-spinner" /> : <HeartOff size={14} />} {t('Remove')}
          </button>
        </div>
      </article>)}</div>}
    {outside.length > 0 && <div className="fd-outside">
      <b>{t('Saved before the Dhaka City rule')}</b>
      <p>{t('These buses run outside Dhaka City, so the favourite bus tools no longer track them. You can remove them.')}</p>
      <ul>{outside.map((bus) => <li key={bus.busId}><span>{t(bus.busName)}{bus.routeNumber && ` · ${bus.routeNumber}`}</span>
        <button type="button" className="link-button" disabled={favorites.isPending(bus)} onClick={() => favorites.toggle(bus)}>{t('Remove')}</button></li>)}</ul>
    </div>}
  </Section>
}

// ---------------- B. Collections ----------------

function CollectionsSection({ state, reload, token, notify, favourites }) {
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)
  const [editing, setEditing] = useState(null) // { id, name }
  const list = state.data || []
  // Resolves to true when the server accepted the change.
  const run = async (work, success) => {
    if (busy) return false
    setBusy(true)
    try { await work(); if (success) notify(t(success)); await reload(); return true } catch (e) { notify(errorText(e)); return false } finally { setBusy(false) }
  }
  const create = async (e) => { e.preventDefault(); const clean = name.trim(); if (clean && await run(() => favouriteApi.createCollection(clean, token), 'Collection created.')) setName('') }
  const cityFavourites = favourites.filter((bus) => bus.isDhakaCity)
  return <Section title="Favourite collections" subtitle={t('Group your buses by trip, like “Office route” or “Mirpur to Motijheel”.')}>
    <form className="fd-inline-form" onSubmit={create}>
      <input value={name} onChange={(e) => setName(e.target.value)} maxLength={60} placeholder={t('New collection name')} aria-label={t('New collection name')} />
      <button type="submit" className="extra-button" disabled={busy || !name.trim()}><FolderPlus size={14} /> {t('Create')}</button>
    </form>
    {isError(state.status) && <ErrorBox message={state.status} onRetry={reload} />}
    {state.status === 'loading' && !list.length ? <Skeleton rows={2} />
      : !list.length ? <Empty icon={FolderHeart} title="No collections yet" text="Create one above, then add your favourite buses to it." />
      : <div className="fd-collections">{list.map((c) => <article key={c._id} className="fd-collection">
        <div className="fd-collection-head">
          {editing?.id === c._id
            ? <form className="fd-inline-form" onSubmit={(e) => { e.preventDefault(); run(() => favouriteApi.renameCollection(c._id, editing.name.trim(), token), 'Collection renamed.').then((ok) => ok && setEditing(null)) }}>
              <input autoFocus value={editing.name} maxLength={60} onChange={(e) => setEditing({ ...editing, name: e.target.value })} aria-label={t('Collection name')} />
              <button type="submit" className="extra-icon-button" disabled={busy || !editing.name.trim()} aria-label={t('Save')}><Check size={15} /></button>
              <button type="button" className="extra-icon-button" onClick={() => setEditing(null)} aria-label={t('Cancel')}><X size={15} /></button>
            </form>
            : <><div><b>{c.name}</b><small>{c.busCount === 1 ? t('1 bus') : t('{count} buses', { count: c.busCount })}</small></div>
              <span className="fd-row-tools">
                <button type="button" className="extra-icon-button" onClick={() => setEditing({ id: c._id, name: c.name })} aria-label={t('Rename {name}', { name: c.name })}><Pencil size={14} /></button>
                <button type="button" className="extra-icon-button" disabled={busy} onClick={() => window.confirm(t('Delete “{name}”? Its buses stay in your favourites.', { name: c.name })) && run(() => favouriteApi.deleteCollection(c._id, token), 'Collection deleted.')} aria-label={t('Delete {name}', { name: c.name })}><Trash2 size={14} /></button>
              </span></>}
        </div>
        {c.buses.length ? <ul className="fd-chips">{c.buses.map((bus) => <li key={bus.busId}>
          <span>{t(bus.busName)}{bus.routeNumber && <small> {bus.routeNumber}</small>}</span>
          {list.length > 1 && <select className="fd-select fd-select-small" value="" aria-label={t('Move {bus} to another collection', { bus: bus.busName })}
            onChange={(e) => e.target.value && run(() => favouriteApi.addToCollection(e.target.value, bus.busId, token, c._id), 'Bus moved to the collection.')}>
            <option value="">{t('Move to…')}</option>
            {list.filter((other) => other._id !== c._id).map((other) => <option key={other._id} value={other._id}>{other.name}</option>)}
          </select>}
          <button type="button" aria-label={t('Remove {bus} from {name}', { bus: bus.busName, name: c.name })} disabled={busy} onClick={() => run(() => favouriteApi.removeFromCollection(c._id, bus.busId, token))}><X size={12} /></button>
        </li>)}</ul> : <p className="fd-muted">{t('No buses in this collection yet.')}</p>}
        {cityFavourites.some((bus) => !c.buses.some((b) => b.busId === bus.busId)) && <select className="fd-select" value="" aria-label={t('Add a bus to {name}', { name: c.name })}
          onChange={(e) => e.target.value && run(() => favouriteApi.addToCollection(c._id, e.target.value, token), 'Bus added to the collection.')}>
          <option value="">{t('Add a favourite bus…')}</option>
          {cityFavourites.filter((bus) => !c.buses.some((b) => b.busId === bus.busId)).map((bus) => <option key={bus.busId} value={bus.busId}>{t(bus.busName)}{bus.routeNumber ? ` (${bus.routeNumber})` : ''}</option>)}
        </select>}
      </article>)}</div>}
  </Section>
}

// ---------------- C. Comparison ----------------

const COMPARE_ROWS = [
  ['Bus name', (b) => t(b.busName)],
  ['Operator', (b) => b.operator || '—'],
  ['Starting point', (b) => t(b.startingPoint || '—')],
  ['Destination', (b) => t(b.destination || '—')],
  ['Dhaka City route', (b) => b.route.map((s) => t(s)).join(' → '), 'fd-route-cell'],
  ['Total stops', (b) => formatNumber(b.totalStops)],
  ['Ticket fare', (b) => <>{money(b.base_fare)} <small>{t('from, then {perKm}/km', { perKm: money(b.per_km_fare) })}</small></>],
  ['AC / Non-AC', (b) => b.airConditioning ? t(b.airConditioning) : <span className="fd-muted">{t('Not recorded')}</span>],
  ['Departure information', (b) => b.departureInfo ? t(b.departureInfo) : <span className="fd-muted">{t('No timetable in the route data')}</span>],
  ['Buses in service', (b) => formatNumber(b.vehiclesInService)],
  ['Seats free now', (b) => b.seatsFreeNow == null ? <span className="fd-muted">{t('No buses in service')}</span> : formatNumber(b.seatsFreeNow)],
  ['Route length (approx.)', (b) => b.routeLengthKm == null ? '—' : t('{km} km', { km: formatNumber(b.routeLengthKm, { maximumFractionDigits: 1 }) })],
  ['Your trip', (b) => b.trip ? <>{t('{from} → {to}', { from: t(b.trip.boarding), to: t(b.trip.alighting) })}<small>{t('{count} stops · {fare}', { count: b.trip.stopsToRide, fare: money(b.trip.fare) })}</small></>
    : b.servesDestination === false ? <span className="fd-muted">{t('Does not serve this trip')}</span> : '—'],
]

function CompareSection({ favourites, selected, onSelect, token }) {
  const [trip, setTrip] = usePersistentState('fav:compare-trip', { from: '', to: '' })
  const [result, setResult] = useState({ status: 'idle', data: null })
  const city = favourites.filter((bus) => bus.isDhakaCity)
  const compare = async (e) => {
    e?.preventDefault()
    if (selected.length < 2) return
    setResult((r) => ({ ...r, status: 'loading' }))
    try { setResult({ status: 'ready', data: (await favouriteApi.compare(selected, { from: trip.from.trim() || undefined, to: trip.to.trim() || undefined }, token)).data }) }
    catch (err) { setResult({ status: errorText(err), data: null }) }
  }
  const data = result.data
  const h = data?.highlights || {}
  const badges = (id) => [
    h.lowestFareBusId === id && ['Lowest fare', 'fare'],
    h.shortestRouteBusId === id && ['Shortest route', 'short'],
    h.mostSuitableBusId === id && ['Best for your trip', 'best'],
  ].filter(Boolean)
  return <Section title="Compare Dhaka buses" subtitle={t('Choose two or three favourite buses. Only figures stored for each bus are shown.')}>
    {city.length < 2 ? <Empty icon={GitCompareArrows} title="Save at least two buses to compare" text="Favourite two or three Dhaka City buses first." />
      : <form className="fd-compare-form" onSubmit={compare}>
        <div className="fd-pick">{city.map((bus) => <label key={bus.busId} className={`fd-pill${selected.includes(bus.busId) ? ' is-on' : ''}`}>
          <input type="checkbox" checked={selected.includes(bus.busId)} disabled={!selected.includes(bus.busId) && selected.length >= MAX_COMPARE} onChange={() => onSelect(bus.busId)} />
          {t(bus.busName)}{bus.routeNumber && <small>{bus.routeNumber}</small>}
        </label>)}</div>
        <StopSuggestions />
        <div className="fd-trip">
          <div className="fd-stop-pair">
            <Field label="Starting stop (optional)" dot="start">
              <input value={trip.from} onChange={(e) => setTrip({ ...trip, from: e.target.value })} list="stop-suggestions" placeholder={t('e.g. Mirpur 10')} aria-label={t('From')} />
            </Field>
            <SwapButton disabled={!trip.from && !trip.to} onClick={() => setTrip({ from: trip.to, to: trip.from })} />
            <Field label="Destination stop (optional)" dot="end">
              <input value={trip.to} onChange={(e) => setTrip({ ...trip, to: e.target.value })} list="stop-suggestions" placeholder={t('e.g. Motijheel')} aria-label={t('To')} />
            </Field>
          </div>
          <button type="submit" className="extra-button" disabled={selected.length < 2 || result.status === 'loading'}>
            {result.status === 'loading' ? <span className="favorite-spinner" /> : <GitCompareArrows size={14} />} {t('Compare')}
          </button>
        </div>
      </form>}
    {isError(result.status) && <ErrorBox message={result.status} onRetry={compare} />}
    {result.status === 'loading' && !data && <Skeleton rows={4} />}
    {data && <>
      {(data.unresolved?.from || data.unresolved?.to) && <p className="fd-note">{t('No stop on these buses matches “{place}”.', { place: data.unresolved.from || data.unresolved.to })}</p>}
      {h.mostSuitableReason && <p className="fd-note fd-note-good"><Check size={14} /> {t(h.mostSuitableReason)}</p>}
      <div className="fd-table-wrap"><table className="fd-compare-table">
        <thead><tr><th scope="col"><span className="sr-only">{t('Detail')}</span></th>{data.buses.map((b) => <th key={b.busId} scope="col">
          <b>{t(b.busName)}</b>{b.routeNumber && <small>{b.routeNumber}</small>}
          <span className="fd-badges">{badges(b.busId).map(([label, kind]) => <i key={kind} className={`fd-badge ${kind}`}>{t(label)}</i>)}</span>
        </th>)}</tr></thead>
        <tbody>{COMPARE_ROWS.map(([label, value, cls]) => <tr key={label}><th scope="row">{t(label)}</th>{data.buses.map((b) => <td key={b.busId} className={cls}>{value(b)}</td>)}</tr>)}</tbody>
      </table></div>
      <ul className="fd-footnotes">{data.notes.map((note) => <li key={note}>{t(note)}</li>)}</ul>
    </>}
  </Section>
}

// ---------------- D. Fare tracking ----------------

function FaresSection({ state, reload, token }) {
  const [open, setOpen] = useState({}) // busId -> { status, rows }
  const toggle = async (busId) => {
    if (open[busId]) { setOpen(({ [busId]: _, ...rest }) => rest); return }
    setOpen((o) => ({ ...o, [busId]: { status: 'loading', rows: [] } }))
    try { const data = await favouriteApi.priceHistory(busId, token); setOpen((o) => ({ ...o, [busId]: { status: 'ready', rows: data.data } })) }
    catch (e) { setOpen((o) => ({ ...o, [busId]: { status: errorText(e), rows: [] } })) }
  }
  const rows = state.data || []
  const Direction = ({ value }) => value === 'increased' ? <span className="fd-change up"><ArrowUp size={13} /> {t('Increased')}</span>
    : value === 'decreased' ? <span className="fd-change down"><ArrowDown size={13} /> {t('Decreased')}</span>
    : value === 'changed' ? <span className="fd-change"><LineChart size={13} /> {t('Per-km fare changed')}</span>
    : <span className="fd-change same"><Minus size={13} /> {t('No change yet')}</span>
  return <Section title="Fare tracking" subtitle={t('Fares come from the bus records. A change is listed only when a bus’s fare actually changes.')}>
    {isError(state.status) && <ErrorBox message={state.status} onRetry={reload} />}
    {state.status === 'loading' && !rows.length ? <Skeleton />
      : !rows.length ? <Empty icon={LineChart} title="Nothing to track yet" text="Fares of your favourite Dhaka buses will show here." />
      : <div className="fd-list">{rows.map((row) => <div key={row.busId} className="fd-fare-row">
        <div className="fd-fare-main">
          <div><b>{t(row.busName)}</b>{row.routeNumber && <small>{row.routeNumber}</small>}</div>
          <div className="fd-fare-figures">
            <span><small>{t('Previous')}</small><b>{money(row.previous_base_fare)}</b></span>
            <span><small>{t('Current')}</small><b>{money(row.current_base_fare)}</b></span>
            <span><small>{t('Change')}</small><b className={row.change > 0 ? 'up' : row.change < 0 ? 'down' : ''}>{row.change == null ? '—' : `${row.change > 0 ? '+' : row.change < 0 ? '−' : ''}${money(Math.abs(row.change))}`}</b></span>
          </div>
          <Direction value={row.direction} />
          <button type="button" className="link-button" aria-expanded={Boolean(open[row.busId])} onClick={() => toggle(row.busId)}>{open[row.busId] ? t('Hide history') : t('Fare history')}</button>
        </div>
        {open[row.busId] && <div className="fd-history">
          {open[row.busId].status === 'loading' ? <Skeleton rows={1} /> : isError(open[row.busId].status) ? <ErrorBox message={open[row.busId].status} />
            : <ol>{open[row.busId].rows.map((h) => <li key={h.recordedAt}>
              <time>{formatDateTime(h.recordedAt)}</time>
              {h.change === 'baseline' ? <span>{t('Tracking started at {fare}', { fare: money(h.base_fare) })}</span>
                : <span>{t('{from} → {to}', { from: money(h.previous_base_fare ?? h.base_fare), to: money(h.base_fare) })} <Direction value={h.change} /></span>}
            </li>)}</ol>}
        </div>}
      </div>)}</div>}
    <p className="fd-muted fd-small">{t('Tracking since: the first time one of your buses was saved. Per-km fares are tracked too.')}</p>
  </Section>
}

// ---------------- E. Departure reminders ----------------

const LEAD_CHOICES = [[15, '15 minutes before'], [30, '30 minutes before'], [60, '1 hour before']]

// A labelled form field: the label above, an icon (or a start/end dot) inside the box. The control inside keeps its
// own aria-label, so its accessible name stays exactly the label.
function Field({ label, icon: Icon, dot, children, className = '' }) {
  return <label className={`fd-input${className ? ` ${className}` : ''}`}>
    <small>{t(label)}</small>
    <span className="fd-input-box">{dot ? <i className={`fd-input-dot ${dot}`} aria-hidden="true" /> : Icon && <Icon size={15} aria-hidden="true" />}{children}</span>
  </label>
}

// The bus's stops in route order, numbered as on the route page. Disabled until a bus is chosen.
function StopSelect({ stops, value, onChange, label, dot, exclude }) {
  return <Field label={label} dot={dot}>
    <select value={value} onChange={(e) => onChange(e.target.value)} aria-label={t(label)} required disabled={!stops.length}>
      <option value="">{t(stops.length ? 'Select a stop' : 'Choose a bus first')}</option>
      {stops.map((s, i) => <option key={`${s.order}-${s.name}`} value={s.name} disabled={s.name === exclude}>{`${formatNumber((s.order ?? i) + 1)} · ${t(s.name)}`}</option>)}
    </select>
  </Field>
}

function SwapButton({ onClick, disabled }) {
  return <button type="button" className="fd-swap" onClick={onClick} disabled={disabled} aria-label={t('Swap starting and destination stops')} title={t('Swap')}><ArrowUpDown size={15} /></button>
}

function BusSelect({ favourites, value, onChange }) {
  return <Field label="Favourite bus" icon={BusFront}>
    <select value={value} onChange={(e) => onChange(e.target.value)} aria-label={t('Favourite bus')} required>
      <option value="">{t('Choose a favourite bus')}</option>
      {favourites.map((bus) => <option key={bus.busId} value={bus.busId}>{t(bus.busName)}{bus.routeNumber ? ` (${bus.routeNumber})` : ''}</option>)}
    </select>
  </Field>
}

function RemindersSection({ state, reload, token, notify, favourites, stopsOf }) {
  const blank = () => ({ id: null, busId: '', stopName: '', departureAt: dhakaInputValue(new Date(Date.now() + 3600 * 1000)), leadMinutes: 15 })
  const [form, setForm] = useState(blank)
  const [busy, setBusy] = useState(false)
  const city = favourites.filter((bus) => bus.isDhakaCity)
  const save = async (e) => {
    e.preventDefault()
    if (busy) return
    setBusy(true)
    const body = { busId: form.busId, stopName: form.stopName, departureAt: form.departureAt, leadMinutes: Number(form.leadMinutes) }
    try {
      if (form.id) await favouriteApi.updateReminder(form.id, body, token); else await favouriteApi.createReminder(body, token)
      notify(t(form.id ? 'Reminder updated.' : 'Reminder saved.')); setForm(blank()); await reload()
    } catch (err) { notify(errorText(err)) } finally { setBusy(false) }
  }
  const act = async (work, message) => { try { await work(); if (message) notify(t(message)); await reload() } catch (e) { notify(errorText(e)) } }
  const list = state.data || []
  const statusLabel = { upcoming: 'Upcoming', sent: 'Sent', past: 'Departed', disabled: 'Off' }
  return <Section title="Departure reminders" subtitle={t('The route data has no timetable, so set your own departure time. All times are Bangladesh time (GMT+6).')}>
    {city.length === 0 ? <Empty icon={AlarmClock} title="Save a Dhaka City bus first" text="Reminders are for your favourite buses." />
      : <form className="fd-form fd-form-reminder" onSubmit={save}>
        <BusSelect favourites={city} value={form.busId} onChange={(busId) => setForm({ ...form, busId, stopName: '' })} />
        <StopSelect stops={stopsOf(form.busId)} value={form.stopName} onChange={(stopName) => setForm({ ...form, stopName })} label="Stop you leave from" dot="start" />
        <Field label="Departure (Bangladesh time)" icon={CalendarClock}>
          <input type="datetime-local" value={form.departureAt} min={dhakaInputValue()} onChange={(e) => setForm({ ...form, departureAt: e.target.value })} aria-label={t('Departure (Bangladesh time)')} required />
        </Field>
        <Field label="Remind me" icon={Bell}>
          <select value={form.leadMinutes} onChange={(e) => setForm({ ...form, leadMinutes: e.target.value })} aria-label={t('Remind me')}>
            {LEAD_CHOICES.map(([value, label]) => <option key={value} value={value}>{t(label)}</option>)}
          </select>
        </Field>
        <div className="fd-form-actions">
          <button type="submit" className="extra-button" disabled={busy || !form.busId || !form.stopName}>{busy ? <span className="favorite-spinner" /> : <AlarmClock size={14} />} {t(form.id ? 'Save changes' : 'Add reminder')}</button>
          {form.id && <button type="button" className="extra-button secondary" onClick={() => setForm(blank())}>{t('Cancel')}</button>}
        </div>
      </form>}
    {isError(state.status) && <ErrorBox message={state.status} onRetry={reload} />}
    {state.status === 'loading' && !list.length ? <Skeleton rows={2} />
      : list.length > 0 && <div className="fd-list">{list.map((r) => {
        const when = dhakaParts(r.departureAtLocal), remind = dhakaParts(r.remindAtLocal)
        return <div key={r._id} className={`fd-reminder ${r.status}`}>
          <span className="fd-reminder-time"><Clock3 size={15} /><b>{when?.time}</b><small>{when && formatDate(when.date, { day: 'numeric', month: 'short', timeZone: 'UTC' })}</small></span>
          <div><b>{t(r.busName)}</b>{r.routeNumber && <small> {r.routeNumber}</small>}
            <p>{t('From {stop} · reminder at {time}', { stop: t(r.stopName), time: remind?.time || '' })}</p></div>
          <i className={`fd-status ${r.status}`}>{t(statusLabel[r.status] || r.status)}</i>
          <span className="fd-row-tools">
            {r.status !== 'past' && <label className="fd-switch" title={t(r.isEnabled ? 'Turn off' : 'Turn on')}>
              <input type="checkbox" checked={r.isEnabled} onChange={() => act(() => favouriteApi.updateReminder(r._id, { isEnabled: !r.isEnabled }, token))} aria-label={t('Reminder on')} /><span /></label>}
            <button type="button" className="extra-icon-button" aria-label={t('Edit reminder')} onClick={() => setForm({ id: r._id, busId: r.busId, stopName: r.stopName, departureAt: r.departureAtLocal.slice(0, 16), leadMinutes: r.leadMinutes })}><Pencil size={14} /></button>
            <button type="button" className="extra-icon-button" aria-label={t('Delete reminder')} onClick={() => act(() => favouriteApi.deleteReminder(r._id, token), 'Reminder deleted.')}><Trash2 size={14} /></button>
          </span>
        </div>
      })}</div>}
  </Section>
}

// ---------------- Availability alerts ----------------

function AlertsSection({ state, reload, token, notify, favourites, stopsOf }) {
  const [form, setForm] = useState({ busId: '', fromStop: '', toStop: '' })
  const [busy, setBusy] = useState(false)
  const city = favourites.filter((bus) => bus.isDhakaCity)
  const list = state.data?.data || []
  const save = async (e) => {
    e.preventDefault()
    if (busy) return
    setBusy(true)
    try { await favouriteApi.createAlert(form, token); notify(t('Availability alert saved.')); setForm({ busId: '', fromStop: '', toStop: '' }); await reload() }
    catch (err) { notify(errorText(err)) } finally { setBusy(false) }
  }
  const act = async (work, message) => { try { await work(); if (message) notify(t(message)); await reload() } catch (e) { notify(errorText(e)) } }
  return <Section title="Bus availability alerts" subtitle={t('Get a notification when your bus fills up or seats free up again.')}>
    <p className="fd-note">{t('Seats come from riders boarding and leaving through SmartCommute, on the route’s buses in service. Alerts for a travel date need a seat-booking system, which the app does not have yet.')}</p>
    {city.length === 0 ? <Empty icon={BellRing} title="Save a Dhaka City bus first" text="Alerts are for your favourite buses." />
      : <form className="fd-form fd-form-alert" onSubmit={save}>
        <BusSelect favourites={city} value={form.busId} onChange={(busId) => setForm({ busId, fromStop: '', toStop: '' })} />
        <div className="fd-stop-pair">
          <StopSelect stops={stopsOf(form.busId)} value={form.fromStop} onChange={(fromStop) => setForm({ ...form, fromStop })} label="Starting stop" dot="start" exclude={form.toStop} />
          <SwapButton disabled={!form.fromStop && !form.toStop} onClick={() => setForm({ ...form, fromStop: form.toStop, toStop: form.fromStop })} />
          <StopSelect stops={stopsOf(form.busId)} value={form.toStop} onChange={(toStop) => setForm({ ...form, toStop })} label="Destination stop" dot="end" exclude={form.fromStop} />
        </div>
        <div className="fd-form-actions"><button type="submit" className="extra-button" disabled={busy || !form.busId || !form.fromStop || !form.toStop}>{busy ? <span className="favorite-spinner" /> : <BellRing size={14} />} {t('Add alert')}</button></div>
      </form>}
    {isError(state.status) && <ErrorBox message={state.status} onRetry={reload} />}
    {state.status === 'loading' && !list.length ? <Skeleton rows={2} />
      : list.length > 0 && <div className="fd-list">{list.map((a) => <div key={a._id} className="fd-alert-row">
        <div><b>{t(a.busName)}</b>{a.routeNumber && <small> {a.routeNumber}</small>}<p>{t('{from} → {to}', { from: t(a.fromStop), to: t(a.toStop) })}</p></div>
        <span className="fd-seats">{a.lastSeatsFree == null ? '—' : a.lastSeatsFree === 0 ? t('Full now') : t('{count} seats free', { count: a.lastSeatsFree })}</span>
        <span className="fd-row-tools">
          <label className="fd-switch" title={t(a.isActive ? 'Turn off' : 'Turn on')}><input type="checkbox" checked={a.isActive} onChange={() => act(() => favouriteApi.updateAlert(a._id, { isActive: !a.isActive }, token))} aria-label={t('Alert on')} /><span /></label>
          <button type="button" className="extra-icon-button" aria-label={t('Delete alert')} onClick={() => act(() => favouriteApi.deleteAlert(a._id, token), 'Availability alert deleted.')}><Trash2 size={14} /></button>
        </span>
      </div>)}</div>}
  </Section>
}

// ---------------- Recently viewed ----------------

function RecentSection({ state, reload, token, notify, navigate }) {
  const list = state.data || []
  const clear = async () => { try { await favouriteApi.clearRecentlyViewed(token); notify(t('Viewing history cleared.')); await reload() } catch (e) { notify(errorText(e)) } }
  return <Section title="Recently viewed Dhaka buses" subtitle={t('The last 10 Dhaka City buses you opened.')}
    actions={list.length > 0 && <button type="button" className="extra-button secondary" onClick={clear}><Trash2 size={14} /> {t('Clear history')}</button>}>
    {isError(state.status) && <ErrorBox message={state.status} onRetry={reload} />}
    {state.status === 'loading' && !list.length ? <Skeleton />
      : !list.length ? <Empty icon={History} title="Nothing viewed yet" text="Open a Dhaka City bus’s page and it will appear here." />
      : <div className="fd-list">{list.map((bus) => <button type="button" key={bus.busId} className="fd-recent-row" onClick={() => bus.routeNumber && navigate('Bus details', { route: bus.routeNumber })}>
        <span className="fav-card-icon"><BusFront size={16} /></span>
        <span><b>{t(bus.busName)}</b>{bus.routeNumber && <small> {bus.routeNumber}</small>}
          <em>{t('{from} → {to}', { from: t(bus.startingStop || ''), to: t(bus.destination || '') })} · {t('{count} stops', { count: bus.route.length })}</em></span>
        <time>{formatDateTime(bus.viewedAt)}</time>
        <ArrowRight size={14} />
      </button>)}</div>}
  </Section>
}

// ---------------- F. Notifications ----------------

export const FAVOURITE_CATEGORY_LABELS = {
  'bus-favourite': 'Favourites', 'bus-fare': 'Fare', 'bus-availability': 'Availability', 'bus-reminder': 'Reminder', 'bus-route': 'Route update',
}

function NotificationsSection({ state, reload, token, notify }) {
  const items = state.data?.data || []
  const unread = state.data?.unreadCount || 0
  const act = async (work) => { try { await work(); notificationsChanged(); await reload() } catch (e) { notify(errorText(e)) } }
  return <Section title="Bus notifications" subtitle={unread ? t('{count} unread', { count: unread }) : t('All caught up')}
    actions={unread > 0 && <button type="button" className="extra-button secondary" onClick={() => act(() => favouriteApi.readAllNotifications(token))}><CheckCheck size={14} /> {t('Mark all as read')}</button>}>
    {isError(state.status) && <ErrorBox message={state.status} onRetry={reload} />}
    {state.status === 'loading' && !items.length ? <Skeleton />
      : !items.length ? <Empty icon={Bell} title="No bus notifications" text="Fare changes, route updates, reminders and alerts for your favourite buses will appear here." />
      : <div className="fd-list">{items.map((n) => <div key={n._id} className={`fd-notification ${n.type}${n.isRead ? '' : ' unread'}`}>
        <span className={`fd-dot ${n.category}`} aria-hidden="true" />
        <div><small>{t(FAVOURITE_CATEGORY_LABELS[n.category] || 'Update')} · <time>{formatDateTime(n.createdAt)}</time></small><b>{t(n.title)}</b><p>{n.message}</p></div>
        <span className="fd-row-tools">
          {!n.isRead && <button type="button" className="extra-icon-button" aria-label={t('Mark as read')} onClick={() => act(() => favouriteApi.readNotification(n._id, token))}><Check size={14} /></button>}
          <button type="button" className="extra-icon-button" aria-label={t('Delete notification')} onClick={() => act(() => favouriteApi.deleteNotification(n._id, token))}><Trash2 size={14} /></button>
        </span>
      </div>)}</div>}
  </Section>
}

// ---------------- The dashboard ----------------

export function FavouriteDashboard({ token, user, signedIn, openAuth, notify, navigate }) {
  const favorites = useFavoriteBuses({ token, user, signedIn, notify, openAuth })
  const [tab, setTab] = usePersistentState('fav:tab', 'favourites')
  const [selected, setSelected] = usePersistentState('fav:compare', [])
  const [routes] = useBusRoutes()

  const on = Boolean(signedIn && token)
  const [summary, reloadSummary] = useLoad(useCallback(async () => (await favouriteApi.dashboard(token)).data, [token]), on)
  const [collections, reloadCollections] = useLoad(useCallback(async () => (await favouriteApi.collections(token)).data, [token]), on)
  const [fares, reloadFares] = useLoad(useCallback(async () => (await favouriteApi.priceTracking(token)).data, [token]), on && tab === 'fares')
  const [reminders, reloadReminders] = useLoad(useCallback(async () => (await favouriteApi.reminders(token)).data, [token]), on)
  const [alerts, reloadAlerts] = useLoad(useCallback(() => favouriteApi.alerts(token), [token]), on)
  const [recent, reloadRecent] = useLoad(useCallback(async () => (await favouriteApi.recentlyViewed(token)).data, [token]), on && tab === 'recent')
  const [notes, reloadNotes] = useLoad(useCallback(() => favouriteApi.notifications(token), [token]), on)

  // Anything that changes a count on the overview cards reloads them.
  const items = favorites.items
  useEffect(() => { if (on && favorites.status === 'ready') { reloadSummary(); reloadCollections(); reloadNotes() } }, [items.length]) // eslint-disable-line react-hooks/exhaustive-deps
  // A bus removed from favourites can no longer be compared.
  useEffect(() => {
    if (favorites.status !== 'ready') return
    const ids = new Set(items.filter((bus) => bus.isDhakaCity).map((bus) => bus.busId))
    if (selected.some((id) => !ids.has(id))) setSelected(selected.filter((id) => ids.has(id)))
  }, [items, favorites.status]) // eslint-disable-line react-hooks/exhaustive-deps

  const stopsOf = useMemo(() => {
    const byId = new Map((routes.buses || []).map((bus) => [bus._id, bus]))
    return (busId) => [...(byId.get(busId)?.stops || [])].sort((a, b) => (a.order ?? 0) - (b.order ?? 0))
  }, [routes.buses])

  const select = (busId) => setSelected(selected.includes(busId) ? selected.filter((id) => id !== busId) : selected.length >= MAX_COMPARE ? selected : [...selected, busId])
  const addToCollection = async (collectionId, busId) => {
    try { await favouriteApi.addToCollection(collectionId, busId, token); notify(t('Bus added to the collection.')); reloadCollections(); reloadSummary() }
    catch (e) { notify(errorText(e)) }
  }
  const after = (reload) => async () => { await reload(); reloadSummary(); reloadNotes() }

  const heading = <Heading icon={Heart} eyebrow={t('DHAKA CITY BUSES')} title={t('Favourite buses')} subtitle={t('Your saved Dhaka City buses: group them, compare them, follow their fares and get reminders.')} />
  if (!signedIn) return <section className="extra-page">{heading}<RequireSignIn openAuth={openAuth} /></section>

  const s = summary.data
  const cards = [
    ['Favourite Dhaka buses', s?.favouriteBuses, Heart, 'favourites'],
    ['Collections', s?.collections, FolderHeart, 'collections'],
    ['Upcoming reminders', s?.upcomingReminders, AlarmClock, 'reminders'],
    ['Active alerts', s?.activeAlerts, BellRing, 'alerts'],
  ]
  const unreadNotes = notes.data?.unreadCount || 0
  return <section className="extra-page fd-page">{heading}
    <div className="fd-overview">{cards.map(([label, value, Icon, target]) => <button type="button" key={label} className={`fd-stat${tab === target ? ' is-current' : ''}`} onClick={() => setTab(target)}>
      <span className="fd-stat-icon"><Icon size={18} /></span>
      <span><small>{t(label)}</small>{value == null ? <i className="fd-stat-skeleton" aria-label={t('Loading…')} /> : <b>{formatNumber(value)}</b>}</span>
    </button>)}</div>
    {isError(summary.status) && <ErrorBox message={summary.status} onRetry={reloadSummary} />}

    <div className="fd-tabs" role="tablist" aria-label={t('Favourite bus tools')}>{TABS.map(([id, label, Icon]) => <button type="button" role="tab" key={id} aria-selected={tab === id} className={tab === id ? 'is-current' : ''} onClick={() => setTab(id)}>
      <Icon size={14} /> {t(label)}{id === 'notifications' && unreadNotes > 0 && <i className="fd-tab-badge">{formatNumber(unreadNotes)}</i>}{id === 'compare' && selected.length > 0 && <i className="fd-tab-badge">{formatNumber(selected.length)}</i>}
    </button>)}</div>

    <div className="fd-panel" role="tabpanel" key={tab}>
      {tab === 'favourites' && <FavouritesSection favorites={favorites} collections={collections.data || []} selected={selected} onSelect={select} onAddToCollection={addToCollection} navigate={navigate} onCompare={() => setTab('compare')} />}
      {tab === 'collections' && <CollectionsSection state={collections} reload={after(reloadCollections)} token={token} notify={notify} favourites={items} />}
      {tab === 'compare' && <CompareSection favourites={items} selected={selected} onSelect={select} token={token} />}
      {tab === 'fares' && <FaresSection state={fares} reload={reloadFares} token={token} />}
      {tab === 'reminders' && <RemindersSection state={reminders} reload={after(reloadReminders)} token={token} notify={notify} favourites={items} stopsOf={stopsOf} />}
      {tab === 'alerts' && <AlertsSection state={alerts} reload={after(reloadAlerts)} token={token} notify={notify} favourites={items} stopsOf={stopsOf} />}
      {tab === 'recent' && <RecentSection state={recent} reload={reloadRecent} token={token} notify={notify} navigate={navigate} />}
      {tab === 'notifications' && <NotificationsSection state={notes} reload={after(reloadNotes)} token={token} notify={notify} />}
    </div>
  </section>
}
