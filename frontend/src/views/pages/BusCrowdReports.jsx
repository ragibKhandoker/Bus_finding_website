import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  Armchair, ArrowLeft, BusFront, Check, ChevronLeft, ChevronRight, CircleAlert, ClipboardList, Eye, Filter, History, Info, Loader2, MapPin, Megaphone,
  Pencil, Radio, RefreshCw, Search, Trash2, UserRound, Users, UsersRound, X,
} from 'lucide-react'
import { formatNumber, t } from '../../models/i18n'
import { CROWD_LEVELS, FRESHNESS_FILTERS, SEAT_OPTIONS, SORTS, crowdApi, dayTime, editMinutesLeft, freshnessOf } from '../../models/crowdReports'
import { useBusRoutes } from '../../controllers/useBusRoutes'
import {
  BusCrowdReportForm, CrModal, CrowdReportCard, CrowdReportDetails, LiveBusTile, errorText, useNow,
} from '../components/CrowdReportUI'
import { RequireSignIn } from './MorePages'

// F19 Bus Crowd & Seat Availability: commuters report how full a Dhaka bus is; everyone can check before boarding.

const REFRESH_MS = 60_000

export const CROWD_NOTICE = 'These crowd updates are submitted by commuters and may not reflect the current condition of a bus. Always check the reported time before making your travel decision.'

function Notice() {
  return <p className="cr-notice" role="note"><Info size={16} /> <span>{t(CROWD_NOTICE)}</span></p>
}

// The submit / edit dialog, shared by both pages.
function ReportDialog({ token, report, onClose, onSaved }) {
  return <CrModal title={report ? t('Edit your crowd report') : t('Report current bus crowd')}
    subtitle={report ? t('Reports can be edited in the first 15 minutes.') : t('Tell other commuters how full the bus is right now.')} onClose={onClose} wide>
    <BusCrowdReportForm token={token} report={report} onCancel={onClose} onDone={onSaved} />
  </CrModal>
}

function DetailsDialog({ id, initial, token, now, onClose, onEdit, onDeleted, notify }) {
  const [state, setState] = useState(initial ? { status: 'ready', r: initial } : { status: 'loading' })
  const [busy, setBusy] = useState(false)
  useEffect(() => {
    let live = true
    crowdApi.get(id, token).then((res) => { if (live) setState({ status: 'ready', r: res.data }) })
      .catch((e) => { if (live) setState((s) => (s.r ? s : { status: 'error', error: errorText(e) })) })
    return () => { live = false }
  }, [id, token])
  const remove = async (r) => {
    setBusy(true)
    try { const res = await crowdApi.remove(r.id, token); notify(t(res.message || 'Report deleted.')); onDeleted(r) } catch (e) { notify(errorText(e), 'error'); setBusy(false) }
  }
  const r = state.r
  return <CrModal title={r ? t(r.busName) : t('Crowd report')} subtitle={r?.routeName} onClose={onClose}>
    {state.status === 'loading' ? <div className="cr-center"><Loader2 size={20} className="cr-spin" /> {t('Loading…')}</div>
      : state.status === 'error' ? <p className="cr-form-error" role="alert"><CircleAlert size={15} /> {state.error}</p>
        : <CrowdReportDetails report={r} now={now} busy={busy} onEdit={onEdit} onDelete={onDeleted ? remove : undefined} />}
  </CrModal>
}

function Pager({ page, totalPages, onPage }) {
  if (totalPages <= 1) return null
  return <nav className="cr-pager" aria-label={t('Pages')}>
    <button type="button" className="cr-btn ghost small" disabled={page <= 1} onClick={() => onPage(page - 1)}><ChevronLeft size={14} /> {t('Previous')}</button>
    <span>{t('Page {page} of {total}', { page: formatNumber(page), total: formatNumber(totalPages) })}</span>
    <button type="button" className="cr-btn ghost small" disabled={page >= totalPages} onClick={() => onPage(page + 1)}>{t('Next')} <ChevronRight size={14} /></button>
  </nav>
}

// ---- Live Bus Crowd Updates ----

const DASH_TABS = [
  ['recent', 'Recently reported', Radio], ['LOW', 'Low crowd', UserRound], ['MEDIUM', 'Medium crowd', Users], ['HIGH', 'Highly crowded', UsersRound],
  ['seats', 'Seats available', Armchair],
]

function LiveDashboard({ live, now, onPick, onReport, onRefresh }) {
  const [tab, setTab] = useState('recent')
  // Reports age while the page is open: anything that has passed an hour drops out here at once.
  const current = useMemo(() => (live.items || []).filter((s) => freshnessOf(s.latest.observedAt, now) !== 'EXPIRED'), [live.items, now])
  const groups = useMemo(() => ({
    recent: current,
    LOW: current.filter((s) => s.latest.crowdLevel === 'LOW'),
    MEDIUM: current.filter((s) => s.latest.crowdLevel === 'MEDIUM'),
    HIGH: current.filter((s) => s.latest.crowdLevel === 'HIGH'),
    seats: current.filter((s) => s.latest.seatAvailability !== 'NO_SEATS'),
  }), [current])
  const shown = groups[tab]
  return <section className="cr-panel cr-live" aria-labelledby="cr-live-title">
    <header className="cr-panel-head">
      <div><span className="cr-live-dot" aria-hidden="true" /><h2 id="cr-live-title">{t('Live Bus Crowd Updates')}</h2>
        <p>{t('The latest report for each bus seen in the last hour. Older reports never appear here.')}</p></div>
      <div className="cr-head-actions">
        <button type="button" className="cr-btn ghost small" onClick={onRefresh} aria-label={t('Refresh')}><RefreshCw size={14} className={live.status === 'loading' ? 'cr-spin' : ''} /> {t('Refresh')}</button>
        <button type="button" className="cr-btn primary small" onClick={onReport}><Megaphone size={14} /> {t('Report Current Bus Crowd')}</button>
      </div>
    </header>
    <div className="cr-stats">
      {DASH_TABS.map(([key, label, Icon]) => <button key={key} type="button" aria-pressed={tab === key} className={`cr-stat ${key.toLowerCase()}${tab === key ? ' selected' : ''}`} onClick={() => setTab(key)}>
        <span className="cr-stat-icon"><Icon size={17} /></span><b>{formatNumber(groups[key].length)}</b><small>{t(label)}</small>
      </button>)}
    </div>
    {live.status === 'loading' && !live.items ? <div className="cr-skeletons">{[0, 1, 2, 3].map((i) => <i key={i} />)}</div>
      : live.status === 'error' && !live.items ? <p className="cr-form-error" role="alert"><CircleAlert size={15} /> {live.error} <button type="button" className="cr-link" onClick={onRefresh}>{t('Try again')}</button></p>
        : !shown.length ? <div className="cr-empty"><Radio size={26} /><b>{current.length ? t('No buses in this group right now') : t('No buses reported in the last hour')}</b>
          <span>{t('Seen a Dhaka bus just now? Your report helps the next commuter decide.')}</span>
          <button type="button" className="cr-btn primary small" onClick={onReport}><Megaphone size={14} /> {t('Report Current Bus Crowd')}</button></div>
          : <div className="cr-tiles">{shown.map((s) => <LiveBusTile key={s.busId} summary={s} now={now} onPick={onPick} />)}</div>}
  </section>
}

// ---- Recent reports with filters ----

const emptyFilters = { bus: '', busId: '', busLabel: '', stop: '', crowdLevel: '', seatAvailability: '', date: '', freshness: 'current', sort: 'recent' }

function useDebounced(value, ms = 350) {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => { const id = window.setTimeout(() => setDebounced(value), ms); return () => window.clearTimeout(id) }, [value, ms])
  return debounced
}

function ReportFilters({ filters, onChange, busNames }) {
  const set = (key, value) => onChange({ ...filters, [key]: value })
  const active = ['bus', 'busId', 'stop', 'crowdLevel', 'seatAvailability', 'date'].some((k) => filters[k]) || filters.freshness !== 'current' || filters.sort !== 'recent'
  return <div className="cr-filters" role="search" aria-label={t('Filter crowd reports')}>
    <label className="cr-filter wide"><span><Search size={13} /> {t('Bus name')}</span>
      <input list="cr-bus-names" value={filters.bus} onChange={(e) => onChange({ ...filters, bus: e.target.value, busId: '', busLabel: '' })} placeholder={t('e.g. Raida or 36')} />
      <datalist id="cr-bus-names">{busNames.map((n) => <option key={n} value={n} />)}</datalist></label>
    <label className="cr-filter"><span><MapPin size={13} /> {t('Bus stop')}</span>
      <input value={filters.stop} onChange={(e) => set('stop', e.target.value)} placeholder={t('e.g. Farmgate')} /></label>
    <label className="cr-filter"><span>{t('Crowd level')}</span>
      <select value={filters.crowdLevel} onChange={(e) => set('crowdLevel', e.target.value)}><option value="">{t('Any crowd')}</option>
        {CROWD_LEVELS.map((c) => <option key={c.value} value={c.value}>{t(c.label)}</option>)}</select></label>
    <label className="cr-filter"><span>{t('Seats')}</span>
      <select value={filters.seatAvailability} onChange={(e) => set('seatAvailability', e.target.value)}><option value="">{t('Any seats')}</option>
        {SEAT_OPTIONS.map((s) => <option key={s.value} value={s.value}>{t(s.label)}</option>)}</select></label>
    <label className="cr-filter"><span>{t('Observation date')}</span>
      <input type="date" value={filters.date} onChange={(e) => set('date', e.target.value)} /></label>
    <label className="cr-filter"><span>{t('Freshness')}</span>
      <select value={filters.freshness} onChange={(e) => set('freshness', e.target.value)}>{FRESHNESS_FILTERS.map(([v, l]) => <option key={v} value={v}>{t(l)}</option>)}</select></label>
    <label className="cr-filter"><span>{t('Sort by')}</span>
      <select value={filters.sort} onChange={(e) => set('sort', e.target.value)}>{SORTS.map(([v, l]) => <option key={v} value={v}>{t(l)}</option>)}</select></label>
    {active && <button type="button" className="cr-btn ghost small cr-clear" onClick={() => onChange(emptyFilters)}><X size={14} /> {t('Clear filters')}</button>}
  </div>
}

function RecentReports({ token, now, refreshKey, filters, setFilters, onOpen, listRef }) {
  const [routes] = useBusRoutes()
  const busNames = useMemo(() => [...new Set((routes.status === 'ready' ? routes.buses : []).map((b) => (b.busName || '').trim()).filter(Boolean))].sort(), [routes])
  const [page, setPage] = useState(1)
  const [state, setState] = useState({ status: 'loading', items: [], total: 0, totalPages: 0 })
  const bus = useDebounced(filters.bus)
  const stop = useDebounced(filters.stop)
  const params = useMemo(() => ({
    bus: filters.busId ? '' : bus.trim(), stop: stop.trim(), crowdLevel: filters.crowdLevel, seatAvailability: filters.seatAvailability, date: filters.date,
    freshness: filters.freshness, sort: filters.sort, pageSize: 12,
  }), [bus, stop, filters.busId, filters.crowdLevel, filters.seatAvailability, filters.date, filters.freshness, filters.sort])
  useEffect(() => { setPage(1) }, [params, filters.busId])
  const load = useCallback(async (quiet = false) => {
    if (!quiet) setState((s) => ({ ...s, status: 'loading' }))
    try {
      const query = { ...params, page }
      const res = filters.busId ? await crowdApi.forBus(filters.busId, query, token) : await crowdApi.list(query, token)
      setState({ status: 'ready', items: res.data || [], total: res.total || 0, totalPages: res.totalPages || 0 })
    } catch (e) { setState((s) => ({ ...s, status: 'error', error: errorText(e) })) }
  }, [params, page, filters.busId, token])
  useEffect(() => { load() }, [load, refreshKey])
  useEffect(() => { const id = window.setInterval(() => load(true), REFRESH_MS); return () => window.clearInterval(id) }, [load])
  const showingHistory = filters.freshness === 'EXPIRED' || filters.freshness === 'all'
  return <section className="cr-panel" aria-labelledby="cr-recent-title" ref={listRef}>
    <header className="cr-panel-head"><div><h2 id="cr-recent-title">{t('Recent crowd reports')}</h2>
      <p>{state.status === 'ready' ? t('{count} reports match', { count: formatNumber(state.total) }) : t('Reports from commuters, newest observation first.')}</p></div></header>
    <ReportFilters filters={filters} onChange={setFilters} busNames={busNames} />
    {filters.busId && <div className="cr-chip-row"><span className="cr-chip"><BusFront size={13} /> {t('Showing {bus}', { bus: filters.busLabel || t('one bus') })}
      <button type="button" onClick={() => setFilters({ ...filters, busId: '', busLabel: '' })} aria-label={t('Show all buses')}><X size={13} /></button></span></div>}
    {showingHistory && <p className="cr-history-note"><History size={14} /> {t('Expired reports are history only. They do not show how the bus is now.')}</p>}
    {state.status === 'loading' && !state.items.length ? <div className="cr-skeletons">{[0, 1, 2].map((i) => <i key={i} />)}</div>
      : state.status === 'error' ? <p className="cr-form-error" role="alert"><CircleAlert size={15} /> {state.error} <button type="button" className="cr-link" onClick={() => load()}>{t('Try again')}</button></p>
        : !state.items.length ? <div className="cr-empty"><Filter size={26} /><b>{t('No reports match')}</b>
          <span>{filters.freshness === 'current' ? t('Nobody has reported a matching bus in the last hour. Try "All reports" to see older ones.') : t('Try other filters.')}</span></div>
          : <div className={`cr-cards${state.status === 'loading' ? ' is-refreshing' : ''}`}>{state.items.map((r) => <CrowdReportCard key={r.id} report={r} now={now} onOpen={onOpen} />)}</div>}
    <Pager page={page} totalPages={state.totalPages} onPage={(p) => { setPage(p); listRef.current?.scrollIntoView?.({ behavior: 'smooth', block: 'start' }) }} />
  </section>
}

// ---- The main page ----

export function BusCrowdReportPage({ token, signedIn, openAuth, notify = () => {}, navigate = () => {} }) {
  const now = useNow()
  const [live, setLive] = useState({ status: 'loading', items: null })
  const [refreshKey, setRefreshKey] = useState(0)
  const [filters, setFilters] = useState(emptyFilters)
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState(null)
  const [opened, setOpened] = useState(null)
  const [done, setDone] = useState(null)
  const listRef = useRef(null)

  const loadLive = useCallback(async () => {
    setLive((s) => ({ ...s, status: 'loading' }))
    try { const res = await crowdApi.live(token); setLive({ status: 'ready', items: res.data || [] }) } catch (e) { setLive((s) => ({ ...s, status: 'error', error: errorText(e) })) }
  }, [token])
  useEffect(() => { loadLive() }, [loadLive, refreshKey])
  useEffect(() => { const id = window.setInterval(loadLive, REFRESH_MS); return () => window.clearInterval(id) }, [loadLive])

  const report = () => (signedIn ? setFormOpen(true) : openAuth())
  const saved = (result) => {
    setFormOpen(false); setEditing(null); setOpened(null)
    setDone(result.data)
    notify(t(result.message || 'Report submitted.'))
    setRefreshKey((k) => k + 1)
  }
  const pickBus = (s) => {
    setFilters({ ...emptyFilters, busId: s.busId, busLabel: `${t(s.busName)}${s.routeNumber ? ` (${s.routeNumber})` : ''}` })
    listRef.current?.scrollIntoView?.({ behavior: 'smooth', block: 'start' })
  }
  const currentCount = (live.items || []).filter((s) => freshnessOf(s.latest.observedAt, now) !== 'EXPIRED').length

  return <div className="page inner-page cr-page">
    <section className="cr-hero">
      <div className="cr-hero-text">
        <span className="cr-eyebrow"><span className="cr-live-dot" aria-hidden="true" /> {t('LIVE · REPORTED BY DHAKA COMMUTERS')}</span>
        <h1>{t('Bus Crowd & Seat Availability')}</h1>
        <p>{t('See how full a Dhaka bus is and whether it has seats before you wait for it — and help others by reporting the bus you are on or just saw.')}</p>
        <div className="cr-hero-actions">
          <button type="button" className="cr-btn light big" onClick={report}><Megaphone size={17} /> {t('Submit Crowd Report')}</button>
          <button type="button" className="cr-btn glass" onClick={() => (signedIn ? navigate('My crowd reports') : openAuth())}><ClipboardList size={16} /> {t('My Reports')}</button>
        </div>
      </div>
      <div className="cr-hero-card" aria-label={t('Right now')}>
        <span>{t('Right now')}</span>
        <b>{live.items ? formatNumber(currentCount) : '–'}</b>
        <small>{t('buses reported in the last hour')}</small>
        <ul className="cr-legend">
          <li className="good"><i /> {t('Low · seats free')}</li><li className="warn"><i /> {t('Medium · limited')}</li><li className="bad"><i /> {t('High · no seats')}</li>
        </ul>
      </div>
    </section>

    <Notice />

    {done && <div className="cr-success" role="status" data-testid="cr-success">
      <span><Check size={20} /></span>
      <div><b>{t('Report submitted — thank you!')}</b><small>{t('{bus} at {stop} is now visible to other commuters.', { bus: t(done.busName), stop: t(done.stopName) })}</small></div>
      {signedIn && <button type="button" className="cr-btn ghost small" onClick={() => navigate('My crowd reports')}>{t('View in My Reports')}</button>}
      <button type="button" className="cr-icon" onClick={() => setDone(null)} aria-label={t('Close')}><X size={16} /></button>
    </div>}

    <LiveDashboard live={live} now={now} onPick={pickBus} onReport={report} onRefresh={loadLive} />
    <RecentReports token={token} now={now} refreshKey={refreshKey} filters={filters} setFilters={setFilters} onOpen={(r) => setOpened(r)} listRef={listRef} />

    {formOpen && <ReportDialog token={token} onClose={() => setFormOpen(false)} onSaved={saved} />}
    {editing && <ReportDialog token={token} report={editing} onClose={() => setEditing(null)} onSaved={saved} />}
    {opened && <DetailsDialog id={opened.id} initial={opened} token={token} now={now} notify={notify} onClose={() => setOpened(null)}
      onEdit={(r) => { setOpened(null); setEditing(r) }} onDeleted={() => { setOpened(null); setRefreshKey((k) => k + 1) }} />}
  </div>
}

// ---- My Reports ----

export function MyCrowdReportsPage({ token, signedIn, openAuth, notify = () => {}, navigate = () => {} }) {
  const now = useNow(15_000)
  const [state, setState] = useState({ status: 'loading', items: [], total: 0, totalPages: 0, page: 1 })
  const [opened, setOpened] = useState(null)
  const [editing, setEditing] = useState(null)
  const [creating, setCreating] = useState(false)
  const [deleting, setDeleting] = useState(null)
  const load = useCallback(async (page = 1) => {
    try {
      const res = await crowdApi.my(token, page)
      setState((s) => ({ status: 'ready', items: page === 1 ? res.data || [] : [...s.items, ...(res.data || [])], total: res.total || 0, totalPages: res.totalPages || 0, page }))
    } catch (e) { setState((s) => ({ ...s, status: 'error', error: errorText(e) })) }
  }, [token])
  useEffect(() => { if (signedIn) load() }, [load, signedIn])

  const heading = <header className="cr-my-head">
    <button type="button" className="cr-back" onClick={() => navigate('Crowd reports')}><ArrowLeft size={15} /> {t('Bus Crowd & Seat Availability')}</button>
    <div className="cr-my-title"><span className="cr-my-icon"><ClipboardList size={22} /></span>
      <div><h1>{t('My crowd reports')}</h1><p>{t('Every report you have submitted. You can edit a report in its first 15 minutes, and delete it any time.')}</p></div>
      {signedIn && <button type="button" className="cr-btn primary" onClick={() => setCreating(true)}><Megaphone size={15} /> {t('Report Current Bus Crowd')}</button>}
    </div>
  </header>
  if (!signedIn) return <div className="page inner-page cr-page">{heading}<RequireSignIn openAuth={openAuth} /></div>

  const saved = (result) => {
    setEditing(null); setCreating(false); setOpened(null)
    notify(t(result.message || 'Report saved.'))
    load()
  }
  const remove = async (r) => {
    setDeleting(r.id)
    try { const res = await crowdApi.remove(r.id, token); notify(t(res.message || 'Report deleted.')); setOpened(null); load() } catch (e) { notify(errorText(e), 'error') } finally { setDeleting(null) }
  }
  const currentCount = state.items.filter((r) => freshnessOf(r.observedAt, now) !== 'EXPIRED').length
  const editableCount = state.items.filter((r) => editMinutesLeft(r, now) > 0).length

  return <div className="page inner-page cr-page">
    {heading}
    <div className="cr-my-stats">
      <div><b>{formatNumber(state.total)}</b><small>{t('Reports submitted')}</small></div>
      <div className="good"><b>{formatNumber(currentCount)}</b><small>{t('Still current (under 1 hour)')}</small></div>
      <div className="fresh"><b>{formatNumber(editableCount)}</b><small>{t('Can still be edited')}</small></div>
    </div>
    <section className="cr-panel" aria-label={t('My crowd reports')}>
      {state.status === 'loading' ? <div className="cr-skeletons">{[0, 1, 2].map((i) => <i key={i} />)}</div>
        : state.status === 'error' && !state.items.length ? <p className="cr-form-error" role="alert"><CircleAlert size={15} /> {state.error} <button type="button" className="cr-link" onClick={() => load()}>{t('Try again')}</button></p>
          : !state.items.length ? <div className="cr-empty"><ClipboardList size={26} /><b>{t('You have not submitted any crowd reports')}</b>
            <span>{t('Next time you are on a bus or see one at a stop, tell others how full it is.')}</span>
            <button type="button" className="cr-btn primary small" onClick={() => setCreating(true)}><Megaphone size={14} /> {t('Report Current Bus Crowd')}</button></div>
            : <div className="cr-cards">{state.items.map((r) => {
              const left = editMinutesLeft(r, now)
              const expired = freshnessOf(r.observedAt, now) === 'EXPIRED'
              return <CrowdReportCard key={r.id} report={r} now={now}>
                <span className={`cr-status ${left > 0 ? 'editable' : 'locked'}`} data-testid="report-status">
                  {left > 0 ? t('Editable · {count} min left', { count: formatNumber(left) }) : expired ? t('History · locked') : t('Live · locked')}</span>
                <small className="cr-submitted">{t('Submitted {date}', { date: dayTime(r.createdAt) })}</small>
                <span className="cr-card-actions">
                  <button type="button" className="cr-icon small" onClick={() => setOpened(r)} aria-label={t('View report details')} title={t('Details')}><Eye size={14} /></button>
                  {left > 0 && <button type="button" className="cr-icon small" onClick={() => setEditing(r)} aria-label={t('Edit report')} title={t('Edit')}><Pencil size={14} /></button>}
                  <button type="button" className="cr-icon small danger" disabled={deleting === r.id} onClick={() => setOpened(r)} aria-label={t('Delete report')} title={t('Delete')}>
                    {deleting === r.id ? <Loader2 size={14} className="cr-spin" /> : <Trash2 size={14} />}</button>
                </span>
              </CrowdReportCard>
            })}</div>}
      {state.page < state.totalPages && <div className="cr-more"><button type="button" className="cr-btn ghost" onClick={() => load(state.page + 1)}>{t('Show older reports')}</button></div>}
    </section>
    <Notice />

    {(creating || editing) && <ReportDialog token={token} report={editing} onClose={() => { setCreating(false); setEditing(null) }} onSaved={saved} />}
    {opened && <CrModal title={t(opened.busName)} subtitle={opened.routeName} onClose={() => setOpened(null)}>
      <CrowdReportDetails report={opened} now={now} busy={deleting === opened.id} onEdit={(r) => { setOpened(null); setEditing(r) }} onDelete={remove} />
    </CrModal>}
  </div>
}
