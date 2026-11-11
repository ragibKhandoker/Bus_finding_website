import React, { useCallback, useEffect, useMemo, useState } from 'react'
import { ArrowLeft, BusFront, CalendarPlus, CircleAlert, ClipboardList, Lock, Pencil, RefreshCw, Search, ShieldCheck, Users, XCircle } from 'lucide-react'
import { apiRequest } from '../../models/api'
import { formatNumber, t } from '../../models/i18n'
import { TRIP_STATUSES, TRIP_STATUS_LABELS, adminScheduleApi, clock, dateTime, dhakaDate, routeText } from '../../models/driverTrips'
import { useBusRoutes } from '../../controllers/useBusRoutes'
import { SignInPrompt } from '../components/common'
import { ConfirmDialog, DvModal } from '../components/driver/DriverModals'
import { EmptyState, LoadingSkeleton, Pager, TripStatusBadge, WorkingStatusPill } from '../components/driver/ops/DriverWidgets'
import '../styles/driver.css'
import '../styles/driver-ops.css'

// "08:30" in Dhaka time from an ISO time, for the edit form.
const hhmm = (iso) => new Intl.DateTimeFormat('en-GB', { timeZone: 'Asia/Dhaka', hour: '2-digit', minute: '2-digit', hour12: false }).format(new Date(iso))

// /admin/driver-operations: approved drivers' working status and today's progress; scheduling their trips
// (checked on the server for overlaps, assignment and vehicle conflicts); bus and vehicle assignments.
export function AdminDriverOperationsPage({ token, signedIn, isAdmin, openAuth, navigate, notify }) {
  const [ops, setOps] = useState({ status: 'loading', rows: [] })
  const [selected, setSelected] = useState('')
  const [search, setSearch] = useState('')
  const load = useCallback(() => adminScheduleApi.operations(token).then((res) => setOps({ status: 'ready', rows: res.data }))
    .catch((e) => setOps({ status: 'error', rows: [], error: t(e.message) })), [token])
  useEffect(() => { if (isAdmin) load() }, [isAdmin, load])

  if (!signedIn) return <div className="page inner-page dv-page"><SignInPrompt openAuth={openAuth} message={t('Sign in with an admin account to manage driver schedules.')} /></div>
  if (!isAdmin) return <div className="page inner-page dv-page"><div className="dv-panel dv-empty"><span><Lock size={22} /></span><b>{t('Admins only')}</b><small>{t('Your account does not have access to this page.')}</small></div></div>

  const rows = ops.rows.filter((r) => `${r.name} ${r.email}`.toLowerCase().includes(search.trim().toLowerCase()))
  const driver = ops.rows.find((r) => r.driverId === selected)
  const onTrip = ops.rows.filter((r) => r.activeTrip).length
  const working = ops.rows.filter((r) => ['AVAILABLE', 'ON_DUTY', 'ON_TRIP'].includes(r.workingStatus)).length
  const missed = ops.rows.reduce((n, r) => n + r.missedLast30Days, 0)

  return <div className="page inner-page dv-page do-admin">
    <section className="dv-hero">
      <div>
        <button type="button" className="dv-back" onClick={() => navigate('Admin')}><ArrowLeft size={15} /> {t('Admin panel')}</button>
        <span className="dv-eyebrow"><ShieldCheck size={14} /> {t('ADMIN · DRIVER OPERATIONS')}</span>
        <h1>{t('Driver schedules & trips.')}</h1>
        <p>{t('Assign buses, schedule duty trips, and follow each driver’s working status and trip progress.')}</p>
      </div>
      <div className="dv-hero-side"><button type="button" className="button adm-ghost" onClick={load}><RefreshCw size={15} /> {t('Refresh')}</button></div>
    </section>
    <div className="dv-stats do-admin-stats">
      {[[Users, 'Approved drivers', ops.rows.length, 'neutral'], [BusFront, 'Working now', working, 'green'], [ClipboardList, 'On a trip now', onTrip, 'blue'], [CircleAlert, 'Missed trips (30 days)', missed, missed ? 'red' : 'neutral']]
        .map(([Icon, label, value, tone]) => <div key={label} className={`dv-stat tone-${tone}`}><span className="dv-stat-icon"><Icon size={19} /></span><span><small>{t(label)}</small><b>{formatNumber(value)}</b></span></div>)}
    </div>
    <div className="do-admin-grid">
      <section className="dv-panel do-admin-list">
        <label className="dv-search"><Search size={16} aria-hidden="true" /><span className="sr-only">{t('Search')}</span><input value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('Search drivers')} /></label>
        {ops.status === 'loading' ? <LoadingSkeleton rows={4} /> : ops.status === 'error' ? <p className="dv-alert danger">{ops.error}</p>
          : !rows.length ? <EmptyState icon={Users} title="No approved drivers." text="Approve drivers in Driver verification first." />
            : <ul>{rows.map((r) => <li key={r.driverId}><button type="button" className={selected === r.driverId ? 'current' : ''} onClick={() => setSelected(r.driverId)}>
              <span className="dv-avatar">{r.name[0]?.toUpperCase()}</span>
              <span className="do-admin-driver"><b>{r.name}</b><small>{r.assignments.map((a) => a.busName).join(', ') || t('No bus assigned')}</small>
                <small>{t('Today {done}/{total}', { done: r.todayCompleted, total: r.todayTotal })}{r.nextTrip ? ` · ${t('next {time}', { time: clock(r.nextTrip.scheduledDeparture) })}` : ''}</small></span>
              <WorkingStatusPill status={r.workingStatus} />
            </button></li>)}</ul>}
      </section>
      <div className="do-admin-detail">
        {!driver ? <section className="dv-panel"><EmptyState icon={CalendarPlus} title="Choose a driver" text="Pick a driver on the left to assign a bus and schedule trips." /></section>
          : <DriverOps key={driver.driverId} driver={driver} token={token} notify={notify} onChanged={load} navigate={navigate} />}
      </div>
    </div>
  </div>
}

function DriverOps({ driver, token, notify, onChanged, navigate }) {
  const [list, setList] = useState({ status: 'loading', rows: [], total: 0, totalPages: 1 })
  const [filter, setFilter] = useState({ from: dhakaDate(-1), to: '', status: '', page: 1 })
  const [editing, setEditing] = useState(null)
  const [closing, setClosing] = useState(null) // { trip, kind: 'cancel' | 'missed' }
  const load = useCallback(() => adminScheduleApi.list({ driverId: driver.driverId, ...filter, pageSize: 10 }, token)
    .then((res) => setList({ status: 'ready', rows: res.data, total: res.total, totalPages: res.totalPages }))
    .catch((e) => setList({ status: 'error', rows: [], error: t(e.message) })), [driver.driverId, filter, token])
  useEffect(() => { load() }, [load])
  const refresh = () => { load(); onChanged() }

  return <>
    <section className="dv-panel">
      <header className="do-admin-head">
        <div><h2>{driver.name}</h2><p>{driver.email} · {driver.phone} · <span className="dv-mono">{driver.applicationCode}</span></p></div>
        <WorkingStatusPill status={driver.workingStatus} large />
      </header>
      {driver.activeTrip && <p className="do-note">{t('On trip now: {route} since {time}.', { route: routeText(driver.activeTrip), time: clock(driver.activeTrip.actualDeparture) })}</p>}
      <Assignments driver={driver} token={token} notify={notify} onChanged={refresh} />
      <button type="button" className="dv-link" onClick={() => navigate('Driver application', { route: driver.driverId })}>{t('Open verification record')}</button>
    </section>
    {driver.assignments.length > 0 && <ScheduleForm driver={driver} token={token} notify={notify} onSaved={refresh} />}
    <section className="dv-panel">
      <h3><ClipboardList size={17} /> {t('Schedule & trips')} <span className="dv-count">{formatNumber(list.total)}</span></h3>
      <div className="dv-toolbar">
        <label className="dv-filter"><span>{t('From')}</span><input type="date" className="dv-input" value={filter.from} onChange={(e) => setFilter({ ...filter, from: e.target.value, page: 1 })} /></label>
        <label className="dv-filter"><span>{t('To')}</span><input type="date" className="dv-input" value={filter.to} onChange={(e) => setFilter({ ...filter, to: e.target.value, page: 1 })} /></label>
        <label className="dv-filter"><span>{t('Status')}</span><select className="dv-input" value={filter.status} onChange={(e) => setFilter({ ...filter, status: e.target.value, page: 1 })}>
          <option value="">{t('All')}</option>{TRIP_STATUSES.map((s) => <option key={s} value={s}>{t(TRIP_STATUS_LABELS[s])}</option>)}</select></label>
      </div>
      {list.status === 'loading' ? <LoadingSkeleton rows={3} /> : list.status === 'error' ? <p className="dv-alert danger">{list.error}</p>
        : !list.rows.length ? <EmptyState icon={CalendarPlus} title="No trips in this period." />
          : <div className="do-list">{list.rows.map((trip) => {
            const future = trip.status === 'SCHEDULED' && new Date(trip.scheduledDeparture) > new Date()
            return <article key={trip.id} className={`do-trip-row is-${trip.status.toLowerCase().replace(/_/g, '-')}`}>
              <div className="do-trip-when"><b>{clock(trip.scheduledDeparture)}</b><small>{trip.scheduleDate}</small></div>
              <div className="do-trip-main"><small>{t('Trip {n}', { n: trip.tripNumber })} · <span className="dv-mono">{trip.code}</span></small><b>{routeText(trip)}</b>
                <span>{[t(trip.busName), trip.vehicle?.code, t('arr. {time}', { time: clock(trip.scheduledArrival) })].filter(Boolean).join(' · ')}</span>
                {trip.actualDeparture && <small className="do-muted">{t('Actual {a} – {b}', { a: clock(trip.actualDeparture), b: trip.actualArrival ? clock(trip.actualArrival) : '…' })}</small>}
                {trip.cancelReason && <small className="do-warn">{trip.cancelReason}</small>}</div>
              <div className="do-trip-side"><TripStatusBadge status={trip.status} />
                <span className="do-trip-actions">
                  {future && <button type="button" className="dv-chip" onClick={() => setEditing(trip)}><Pencil size={13} /> {t('Edit')}</button>}
                  {trip.status === 'SCHEDULED' && <button type="button" className="dv-chip danger" onClick={() => setClosing({ trip, kind: 'cancel' })}><XCircle size={13} /> {t('Cancel')}</button>}
                  {trip.status === 'SCHEDULED' && !future && <button type="button" className="dv-chip danger" onClick={() => setClosing({ trip, kind: 'missed' })}>{t('Mark missed')}</button>}
                </span></div>
            </article>
          })}</div>}
      <Pager page={filter.page} totalPages={list.totalPages} onPage={(page) => setFilter({ ...filter, page })} />
    </section>
    {editing && <DvModal title={t('Edit trip')} subtitle={`${editing.code} · ${routeText(editing)}`} onClose={() => setEditing(null)}>
      <ScheduleForm driver={driver} token={token} notify={notify} trip={editing} onSaved={() => { setEditing(null); refresh() }} embedded />
    </DvModal>}
    {closing && <CloseTrip {...closing} token={token} notify={notify} onClose={() => setClosing(null)} onDone={() => { setClosing(null); refresh() }} />}
  </>
}

function CloseTrip({ trip, kind, token, notify, onClose, onDone }) {
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const go = async () => {
    if (reason.trim().length < 3) { setError(t('Give a short reason.')); return }
    setBusy(true); setError('')
    try {
      const res = kind === 'cancel' ? await adminScheduleApi.cancel(trip.id, reason.trim(), token) : await adminScheduleApi.markMissed(trip.id, reason.trim(), token)
      notify(t(res.message)); onDone()
    } catch (e) { setError(t(e.message)); setBusy(false) }
  }
  return <ConfirmDialog title={kind === 'cancel' ? t('Cancel this trip?') : t('Mark this trip as missed?')} tone="danger" busy={busy}
    confirmLabel={kind === 'cancel' ? t('Cancel trip') : t('Mark missed')} onConfirm={go} onClose={onClose}
    message={`${routeText(trip)} · ${trip.scheduleDate} ${clock(trip.scheduledDeparture)}. ${kind === 'cancel' ? t('The driver will be notified.') : ''}`}>
    <div className="dv-field"><label htmlFor="do-close-reason">{t('Reason')}</label>
      <textarea id="do-close-reason" className="dv-input dv-textarea" rows={3} value={reason} onChange={(e) => setReason(e.target.value)} /></div>
    {error && <p className="dv-alert danger" role="alert">{error}</p>}
  </ConfirmDialog>
}

function useVehicles(busId, token) {
  const [vehicles, setVehicles] = useState([])
  useEffect(() => {
    if (!busId) { setVehicles([]); return }
    apiRequest(`/api/admin/buses/${busId}/vehicles`, { token }).then((res) => setVehicles(res.data)).catch(() => setVehicles([]))
  }, [busId, token])
  return vehicles
}

function Assignments({ driver, token, notify, onChanged }) {
  const [routes] = useBusRoutes()
  const [search, setSearch] = useState('')
  const [busId, setBusId] = useState('')
  const [vehicleId, setVehicleId] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [ending, setEnding] = useState(null)
  const vehicles = useVehicles(busId, token)
  const matches = useMemo(() => {
    const q = search.trim().toLowerCase()
    return routes.status === 'ready' && q.length >= 2 ? routes.buses.filter((b) => `${b.busName} ${b.routeNumber || ''}`.toLowerCase().includes(q)).slice(0, 6) : []
  }, [routes, search])
  const run = async (fn) => {
    setBusy(true); setError('')
    try { const res = await fn(); notify(t(res.message)); setBusId(''); setVehicleId(''); setSearch(''); setEnding(null); onChanged() } catch (e) { setError(t(e.message)); setEnding(null) } finally { setBusy(false) }
  }
  const chosen = routes.buses?.find((b) => (b._id || b.id) === busId)
  return <div className="do-assign">
    <h3><BusFront size={17} /> {t('Bus assignments')}</h3>
    {driver.assignments.length ? <ul className="dv-assignments">{driver.assignments.map((a) => <li key={a.id}>
      <span><b>{t(a.busName)}</b><small>{a.routeNumber} · {a.vehicleCode ? t('vehicle {code}', { code: a.vehicleCode }) : t('no vehicle set')} · {t('since {date}', { date: dateTime(a.assignedAt) })}</small></span>
      <AssignmentVehicle assignment={a} token={token} busy={busy} onSave={(vid) => run(() => adminScheduleApi.setVehicle(a.id, vid || null, token))} />
      <button type="button" className="dv-chip danger" disabled={busy} onClick={() => setEnding(a)}>{t('End')}</button>
    </li>)}</ul> : <p className="dv-small warn">{t('No bus assigned. Assign one before scheduling trips.')}</p>}
    {!busId ? <div className="dv-field"><label htmlFor="do-assign-search">{t('Assign a bus')}</label>
      <div className="dv-control"><Search size={15} aria-hidden="true" /><input id="do-assign-search" className="dv-input" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('Search the Dhaka route list')} /></div>
      {matches.length > 0 && <ul className="dv-bus-results">{matches.map((b) => <li key={b._id || b.id}><button type="button" onClick={() => setBusId(b._id || b.id)}><BusFront size={15} /><span><b>{t(b.busName)}</b><small>{b.routeNumber}</small></span></button></li>)}</ul>}
    </div> : <div className="do-assign-pick">
      <b>{t(chosen?.busName)} <small>{chosen?.routeNumber}</small></b>
      <select className="dv-input" value={vehicleId} onChange={(e) => setVehicleId(e.target.value)} aria-label={t('Vehicle')}>
        <option value="">{t('Any vehicle on this route')}</option>{vehicles.map((v) => <option key={v.id} value={v.id}>{v.code} · {t(v.busType)}{v.status !== 'active' ? ` (${v.status})` : ''}</option>)}</select>
      <button type="button" className="button button-green button-small" disabled={busy} onClick={() => run(() => adminScheduleApi.assign({ driverId: driver.driverId, busId, individualBusId: vehicleId || null }, token))}>{t('Assign bus')}</button>
      <button type="button" className="dv-chip" onClick={() => setBusId('')}>{t('Cancel')}</button>
    </div>}
    {error && <p className="dv-alert danger" role="alert">{error}</p>}
    {ending && <ConfirmDialog title={t('End this assignment?')} tone="danger" busy={busy} confirmLabel={t('End assignment')} onClose={() => setEnding(null)}
      message={t('{name} will no longer drive {bus}. Their future trips on it will be cancelled.', { name: driver.name, bus: t(ending.busName) })}
      onConfirm={() => run(() => adminScheduleApi.endAssignment(ending.id, token))} />}
  </div>
}

function AssignmentVehicle({ assignment, token, busy, onSave }) {
  const vehicles = useVehicles(assignment.busId, token)
  return <select className="dv-input do-mini-select" value={assignment.vehicleId || ''} disabled={busy} onChange={(e) => onSave(e.target.value)} aria-label={t('Vehicle')}>
    <option value="">{t('Any vehicle')}</option>{vehicles.map((v) => <option key={v.id} value={v.id}>{v.code}</option>)}</select>
}

// The first slot a new trip can sensibly start: 15 minutes from now (Dhaka time), rounded up to 5 minutes, for an
// hour. That falls inside the driver's start window, so a trip scheduled for testing can be started at once. Late
// in the evening (arrival would pass midnight) it is tomorrow at 08:00 instead.
export function nextSlot(now = Date.now()) {
  const dhaka = new Date(now + 6 * 3_600_000)
  let mins = dhaka.getUTCHours() * 60 + dhaka.getUTCMinutes() + 15
  mins = Math.ceil(mins / 5) * 5
  const pad = (m) => `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`
  if (mins + 60 >= 24 * 60) return { date: dhakaDate(1, now), departureTime: '08:00', arrivalTime: '09:00' }
  return { date: dhakaDate(0, now), departureTime: pad(mins), arrivalTime: pad(mins + 60) }
}

// Create (or, with trip, edit) one trip. The server checks the rules again.
function ScheduleForm({ driver, token, notify, trip, onSaved, embedded = false }) {
  const first = driver.assignments.find((a) => a.busId === trip?.routeId) || driver.assignments[0]
  const [form, setForm] = useState(() => {
    const slot = nextSlot()
    return {
      busId: trip?.routeId || first?.busId || '', individualBusId: trip?.vehicle?.id || '', date: trip?.scheduleDate || slot.date,
      departureTime: trip ? hhmm(trip.scheduledDeparture) : slot.departureTime, arrivalTime: trip ? hhmm(trip.scheduledArrival) : slot.arrivalTime,
      direction: trip?.direction || 'FORWARD', notes: trip?.notes || '',
    }
  })
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const vehicles = useVehicles(form.busId, token)
  const set = (key) => (e) => setForm({ ...form, [key]: e.target.value })
  const save = async (event) => {
    event.preventDefault()
    if (!form.departureTime || !form.arrivalTime) { setError(t('Enter both the departure and the expected arrival time.')); return }
    if (form.arrivalTime <= form.departureTime) { setError(t('The expected arrival must be after the departure.')); return }
    setBusy(true); setError('')
    const body = { driverId: driver.driverId, ...form, individualBusId: form.individualBusId || null, notes: form.notes || null }
    try {
      const res = trip ? await adminScheduleApi.update(trip.id, body, token) : await adminScheduleApi.create(body, token)
      notify(t(res.message)); onSaved()
      // Ready for the next trip: it leaves when this one arrives, back the other way, for the same length of time.
      if (!trip) setForm((f) => {
        const toMin = (hhmm) => Number(hhmm.slice(0, 2)) * 60 + Number(hhmm.slice(3, 5))
        const pad = (m) => `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`
        const length = Math.max(15, toMin(f.arrivalTime) - toMin(f.departureTime))
        const nextArrival = toMin(f.arrivalTime) + length
        return { ...f, departureTime: f.arrivalTime, arrivalTime: nextArrival < 24 * 60 ? pad(nextArrival) : '', direction: f.direction === 'FORWARD' ? 'RETURN' : 'FORWARD', notes: '' }
      })
    } catch (e) { setError(t(e.message)) } finally { setBusy(false) }
  }
  const content = <form className="do-schedule-form" onSubmit={save} noValidate>
    {!embedded && <h3><CalendarPlus size={17} /> {t('Schedule a trip')}</h3>}
    <div className="dv-grid">
      <label className="dv-field"><span className="do-label">{t('Bus / route')}</span><select className="dv-input" value={form.busId} onChange={(e) => setForm({ ...form, busId: e.target.value, individualBusId: '' })}>
        {driver.assignments.map((a) => <option key={a.busId} value={a.busId}>{t(a.busName)} · {a.routeNumber}</option>)}</select></label>
      <label className="dv-field"><span className="do-label">{t('Vehicle')}</span><select className="dv-input" value={form.individualBusId} onChange={set('individualBusId')}>
        <option value="">{t('Assignment default')}</option>{vehicles.map((v) => <option key={v.id} value={v.id}>{v.code} · {t(v.busType)}</option>)}</select></label>
      <label className="dv-field"><span className="do-label">{t('Date')}</span><input type="date" className="dv-input" min={dhakaDate()} value={form.date} onChange={set('date')} /></label>
      <label className="dv-field"><span className="do-label">{t('Direction')}</span><select className="dv-input" value={form.direction} onChange={set('direction')}>
        <option value="FORWARD">{t('Outbound (first stop → last stop)')}</option><option value="RETURN">{t('Return (last stop → first stop)')}</option></select></label>
      <label className="dv-field"><span className="do-label">{t('Departure (Dhaka time)')}</span><input type="time" className="dv-input" value={form.departureTime} onChange={set('departureTime')} /></label>
      <label className="dv-field"><span className="do-label">{t('Expected arrival')}</span><input type="time" className="dv-input" value={form.arrivalTime} onChange={set('arrivalTime')} /></label>
      <label className="dv-field wide"><span className="do-label">{t('Note for the driver')} <em>{t('(optional)')}</em></span><input className="dv-input" maxLength={500} value={form.notes} onChange={set('notes')} /></label>
    </div>
    {error && <p className="dv-alert danger" role="alert"><CircleAlert size={15} /> {error}</p>}
    <div className="dv-modal-actions"><button type="submit" className="button button-green" disabled={busy}>{trip ? t('Save changes') : t('Schedule trip')}</button></div>
    <p className="dv-small">{t('The server checks that the driver and vehicle are free at that time and that the bus is still assigned.')}</p>
  </form>
  return embedded ? content : <section className="dv-panel">{content}</section>
}
