import React, { useCallback, useEffect, useRef, useState } from 'react'
import {
  ArrowRightLeft, BadgeCheck, CalendarDays, ChevronLeft, ChevronRight, Lock, MessageSquareText, Plus, RefreshCw, Search, ShieldCheck,
  UserCheck, Users, X,
} from 'lucide-react'
import { formatNumber, t } from '../../models/i18n'
import { apiRequest } from '../../models/api'
import { usePersistentState } from '../../controllers/usePersistentState'
import { EMPTY_FILTERS, GENDERS, GENDER_LABELS, SORTS, VEHICLES, activeFilterCount, dhakaToday, errorText, idOf, loadStops, rideApi, searchQuery } from '../../models/rides'

// Results already loaded in this tab, by search, so coming back to the page or to a filter is instant.
const resultCache = new Map()
export const forgetRideResults = () => resultCache.clear()
import { ConfirmDialog, HostProfileModal, JoinRequestModal, PlaceSearchInput, RideCard, RideCardSkeleton, RideFormModal } from '../components/RideShareUI'
import '../styles/rides.css'

// Ride share discovery (F11): OPEN Dhaka rides that have not left yet, filtered, sorted and paged on the server
// from the real database. Posting a ride, asking to join and everything after happen through the API; the server
// decides who may do what, so the buttons here only follow what it says (ride.viewer).

// How long typed filters wait after the last keystroke before searching.
const FILTER_DELAY_MS = 400

// The ride search, laid out as one form: where from and to (with suggestions), the day, then the ride preferences
// and the order. Search applies everything at once; typing applies after a short pause.
function SearchForm({ filters, onChange, onSearch, onClear, places, onWantPlaces }) {
  const set = (key) => (e) => onChange({ ...filters, [key]: e.target.value })
  const setText = (key) => (value) => onChange({ ...filters, [key]: value })
  const pickText = (key) => (value) => onSearch({ ...filters, [key]: value })
  const swap = () => onSearch({ ...filters, source: filters.destination, destination: filters.source })
  const count = activeFilterCount(filters)
  return <form className="rs-search-form" role="search" aria-label={t('Find a ride')} onSubmit={(e) => { e.preventDefault(); onSearch() }}>
    <div className="rs-search-form-head">
      <div><h2>{t('Find a ride')}</h2><p>{t('Tell us your route. Only rides inside Dhaka City are shown.')}</p></div>
      {count > 0 && <button type="button" className="rs-clear" onClick={onClear}><X size={14} /> {t('Clear filters')} <i>{formatNumber(count)}</i></button>}
    </div>

    <fieldset className="rs-form-row rs-form-route" onFocus={onWantPlaces}>
      <legend className="sr-only">{t('Route')}</legend>
      <PlaceSearchInput id="rs-from" label={t('From')} dot="start" value={filters.source} onChange={setText('source')} onPick={pickText('source')} placeholder={t('Leaving from, e.g. Mirpur 10')} extra={places.from} />
      <button type="button" className="rs-search-swap" onClick={swap} aria-label={t('Swap source and destination')}><ArrowRightLeft size={15} /></button>
      <PlaceSearchInput id="rs-to" label={t('To')} dot="end" value={filters.destination} onChange={setText('destination')} onPick={pickText('destination')} placeholder={t('Going to, e.g. Motijheel')} extra={places.to} />
      <label className="rs-place rs-form-date"><span>{t('Departure date')}</span>
        <span className="rs-input-box"><CalendarDays size={15} className="rs-input-start" /><input type="date" min={dhakaToday()} value={filters.date} onChange={set('date')} aria-label={t('Departure date')} /></span></label>
    </fieldset>

    <fieldset className="rs-form-row rs-form-prefs">
      <legend>{t('Ride preferences')}</legend>
      <label className="rs-place"><span>{t('Gender preference')}</span>
        <select value={filters.gender} onChange={set('gender')} aria-label={t('Gender preference')}>
          <option value="all">{t('Any gender')}</option>{GENDERS.filter((g) => g !== 'Any').map((g) => <option key={g} value={g}>{t(GENDER_LABELS[g])}</option>)}
          <option value="Any">{t('Open to everyone')}</option></select></label>
      <label className="rs-place"><span>{t('Vehicle type')}</span>
        <select value={filters.vehicle} onChange={set('vehicle')} aria-label={t('Vehicle type')}>
          <option value="all">{t('All vehicles')}</option>{VEHICLES.map((v) => <option key={v} value={v}>{t(v)}</option>)}</select></label>
      <label className="rs-place"><span>{t('Available seats')}</span>
        <select value={filters.minSeats} onChange={set('minSeats')} aria-label={t('Available seats')}>
          <option value="">{t('Any seats')}</option>{[1, 2, 3, 4].map((n) => <option key={n} value={n}>{t('{n}+ seats', { n })}</option>)}</select></label>
      <div className="rs-place rs-form-cost"><span>{t('Cost per person (৳)')}</span>
        <div><input inputMode="numeric" value={filters.minCost} onChange={set('minCost')} placeholder={t('Min')} aria-label={t('Minimum cost')} />
          <i aria-hidden="true">–</i>
          <input inputMode="numeric" value={filters.maxCost} onChange={set('maxCost')} placeholder={t('Max')} aria-label={t('Maximum cost')} /></div></div>
      <label className="rs-place"><span>{t('Sort by')}</span>
        <select value={filters.sort} onChange={set('sort')} aria-label={t('Sort by')}>
          {SORTS.map(([value, label]) => <option key={value} value={value}>{t(label)}</option>)}</select></label>
    </fieldset>

    <div className="rs-search-form-foot">
      <ul className="rs-trust">
        <li><ShieldCheck size={14} /> {t('Dhaka City rides only')}</li>
        <li><UserCheck size={14} /> {t('Hosts approve every rider')}</li>
        <li><Lock size={14} /> {t('Private chat and contact details')}</li>
      </ul>
      <button type="submit" className="button button-green rs-search-go"><Search size={16} /> {t('Search rides')}</button>
    </div>
  </form>
}

function Pagination({ page, totalPages, onPage }) {
  if (totalPages <= 1) return null
  const pages = Array.from({ length: totalPages }, (_, i) => i + 1).filter((p) => p === 1 || p === totalPages || Math.abs(p - page) <= 1)
  return <nav className="rs-pagination" aria-label={t('Pages')}>
    <button type="button" onClick={() => onPage(page - 1)} disabled={page <= 1} aria-label={t('Previous page')}><ChevronLeft size={16} /></button>
    {pages.map((p, i) => <React.Fragment key={p}>
      {i > 0 && p - pages[i - 1] > 1 && <span>…</span>}
      <button type="button" className={p === page ? 'on' : ''} aria-current={p === page ? 'page' : undefined} onClick={() => onPage(p)}>{formatNumber(p)}</button>
    </React.Fragment>)}
    <button type="button" onClick={() => onPage(page + 1)} disabled={page >= totalPages} aria-label={t('Next page')}><ChevronRight size={16} /></button>
  </nav>
}

// How ride share works and how to stay safe; sits beside the results so a short list never leaves the page empty.
function Guide({ onOffer }) {
  const steps = [
    [Search, 'Find or offer a ride', 'Search by area and day, or post the empty seats in your own ride.'],
    [UserCheck, 'Host approves riders', 'Send a request. The host checks your profile and accepts riders one by one.'],
    [MessageSquareText, 'Plan in the ride chat', 'Approved riders and the host agree on the pickup in a private chat.'],
  ]
  return <aside className="rs-guide">
    <section className="rs-guide-card">
      <h3>{t('How ride share works')}</h3>
      <ol>{steps.map(([Icon, title, text], i) => <li key={title}><span><Icon size={16} /></span><div><b>{`${formatNumber(i + 1)}. ${t(title)}`}</b><p>{t(text)}</p></div></li>)}</ol>
      <button type="button" className="button button-green button-small" onClick={onOffer}><Plus size={14} /> {t('Offer a ride')}</button>
    </section>
    <section className="rs-guide-card rs-guide-safe">
      <h3><ShieldCheck size={17} /> {t('Ride safely')}</h3>
      <ul>
        <li><BadgeCheck size={14} /> {t('Prefer verified hosts and check their reviews.')}</li>
        <li><Lock size={14} /> {t('Phone numbers and pickup points are shown only after approval.')}</li>
        <li><Users size={14} /> {t('Report or block anyone who makes you uncomfortable.')}</li>
      </ul>
    </section>
  </aside>
}

const validCosts = (f) => [f.minCost, f.maxCost].every((v) => v === '' || /^\d+(\.\d+)?$/.test(String(v).trim()))
  && !(f.minCost !== '' && f.maxCost !== '' && Number(f.minCost) > Number(f.maxCost))

export function RidePage({ token, signedIn, notify, navigate, openAuth }) {
  const [filters, setFilters] = usePersistentState('rides:filters', EMPTY_FILTERS)
  const [applied, setApplied] = useState(filters)
  const [page, setPage] = usePersistentState('rides:page', 1)
  const [state, setState] = useState({ status: 'loading', items: [], total: 0, totalPages: 0 })
  const [creating, setCreating] = useState(false)
  const [joining, setJoining] = useState(null)
  const [hostId, setHostId] = useState(null)
  const [cancelling, setCancelling] = useState(null)
  const run = useRef(0)
  // Place names of the open rides, for the From/To suggestions: fetched only when the user starts on those fields,
  // so opening the page costs one request, not two.
  const [places, setPlaces] = useState({ from: [], to: [] })
  const placesAsked = useRef(false)
  const wantPlaces = useCallback(() => {
    if (placesAsked.current) return
    placesAsked.current = true
    loadStops().catch(() => {})
    apiRequest('/api/rideshare?pageSize=100').then((d) => setPlaces({
      from: (d.data || []).map((r) => r.source), to: (d.data || []).map((r) => r.destination),
    })).catch(() => { placesAsked.current = false })
  }, [])

  // Typed filters wait for typing to stop; Search and the selects apply at once.
  useEffect(() => {
    const timer = window.setTimeout(() => { if (validCosts(filters)) setApplied(filters) }, FILTER_DELAY_MS)
    return () => window.clearTimeout(timer)
  }, [filters])
  const firstApply = useRef(true)
  useEffect(() => { if (firstApply.current) { firstApply.current = false; return } setPage(1) }, [applied]) // eslint-disable-line react-hooks/exhaustive-deps

  const load = useCallback(async () => {
    const current = ++run.current
    // A search already seen in this tab shows at once and is refreshed behind it.
    const key = `${signedIn ? 'in' : 'out'}:${searchQuery(applied, page)}`
    const seen = resultCache.get(key)
    setState((s) => seen ? { ...seen, status: 'refreshing' } : { ...s, status: 'loading' })
    try {
      const data = await rideApi.search(applied, page, signedIn ? token : undefined)
      if (current !== run.current) return
      const fresh = { status: 'ready', items: data.data || [], total: data.total ?? 0, totalPages: data.totalPages ?? 0 }
      resultCache.set(key, fresh)
      if (resultCache.size > 30) resultCache.delete(resultCache.keys().next().value)
      setState(fresh)
    } catch (e) {
      if (current !== run.current) return
      // Keep showing what was there when only the refresh failed.
      setState((s) => s.status === 'refreshing' ? { ...s, status: 'ready' } : { status: 'error', items: [], total: 0, totalPages: 0, message: errorText(e) })
    }
  }, [applied, page, token, signedIn])
  useEffect(() => { load() }, [load])

  const applyNow = (next = filters) => { if (validCosts(next)) setApplied(next) }
  const change = (next) => { setFilters(next); if (['gender', 'vehicle', 'minSeats', 'date', 'sort'].some((k) => next[k] !== filters[k])) applyNow(next) }
  const clear = () => { setFilters(EMPTY_FILTERS); setApplied(EMPTY_FILTERS) }
  const offer = () => { if (!signedIn) { openAuth(); return } setCreating(true) }
  const open = (ride) => navigate('Ride details', { route: idOf(ride) })
  const join = (ride) => { if (!signedIn) { openAuth(); return } setJoining(ride) }
  const sent = () => {
    notify(t('Request sent. The host will be notified.'))
    setState((s) => ({ ...s, items: s.items.map((r) => idOf(r) === idOf(joining) ? { ...r, viewer: { ...r.viewer, role: 'pending', canRequest: false } } : r) }))
    setJoining(null)
  }
  const created = (ride) => {
    resultCache.clear()
    setCreating(false)
    notify(t('Your ride is live. Riders can now ask to join.'))
    if (ride) navigate('Ride details', { route: idOf(ride) })
  }
  const toPage = (p) => { setPage(p); window.scrollTo({ top: 0, behavior: 'smooth' }) }
  const filtered = activeFilterCount(applied) > 0

  return <div className="page inner-page rs-page">
    <section className="rs-hero">
      <div className="rs-hero-top">
        <div className="rs-hero-text">
          <span className="rs-eyebrow">{t('RIDE SHARE · DHAKA CITY')}</span>
          <h1>{t('Share the journey.')}</h1>
          <p>{t('Find people heading your way inside Dhaka. Split the cost, share the road.')}</p>
        </div>
        <div className="rs-hero-actions">
          {signedIn && <button type="button" className="button rs-button-ghost" onClick={() => navigate('My rides')}>{t('My rides')}</button>}
          <button type="button" className="button rs-button-light" onClick={offer}><Plus size={16} /> {t('Offer a ride')}</button>
        </div>
      </div>
    </section>
    <SearchForm filters={filters} onChange={change} onSearch={(next) => { const value = next || filters; setFilters(value); applyNow(value) }} onClear={clear} places={places} onWantPlaces={wantPlaces} />
    {!validCosts(filters) && <p className="rs-form-error" role="alert">{t('Enter a cost range in taka, with the minimum not above the maximum.')}</p>}

    <div className="rs-layout">
      <section className="rs-results" aria-live="polite">
        <div className="rs-results-head">
          <h2>{t('Available rides')}</h2>
          {(state.status === 'ready' || state.status === 'refreshing') && <span className="rs-count">{state.total === 1 ? t('1 open ride') : t('{count} open rides', { count: state.total })}</span>}
        </div>
        {state.status === 'loading' && !state.items.length
          ? <div className="rs-list">{Array.from({ length: 3 }, (_, i) => <RideCardSkeleton key={i} />)}</div>
          : state.status === 'error'
            ? <div className="rs-empty"><span><RefreshCw size={22} /></span><b>{t('Could not load rides')}</b><p>{state.message}</p><button type="button" className="button button-outline button-small" onClick={load}>{t('Try again')}</button></div>
            : state.items.length
              ? <><div className={`rs-list${state.status === 'loading' ? ' rs-loading' : ''}`}>{state.items.map((ride) => <RideCard key={idOf(ride)} ride={ride} onOpen={open} onJoin={join} onHost={(h) => setHostId(h.id)} onCancel={setCancelling} />)}</div>
                <Pagination page={page} totalPages={state.totalPages} onPage={toPage} /></>
              : <div className="rs-empty"><span><Users size={22} /></span>
                <b>{filtered ? t('No rides match these filters') : t('No open rides right now')}</b>
                <p>{filtered ? t('Try another date, a wider cost range or fewer filters.') : t('Be the first to offer a ride and find your travel crew.')}</p>
                {filtered ? <button type="button" className="button button-outline button-small" onClick={clear}>{t('Clear filters')}</button>
                  : <button type="button" className="button button-green button-small" onClick={offer}><Plus size={14} /> {t('Offer a ride')}</button>}
              </div>}
      </section>
      <Guide onOffer={offer} />
    </div>

    {creating && <RideFormModal token={token} onSaved={created} onClose={() => setCreating(false)} />}
    {joining && <JoinRequestModal ride={joining} token={token} onSent={sent} onClose={() => setJoining(null)} />}
    {cancelling && <ConfirmDialog danger withReason title={t('Cancel this ride?')}
      message={t('Ride from {source} to {destination}. Everyone who asked or was approved is notified, and the ride stays in My rides.', { source: t(cancelling.source), destination: t(cancelling.destination) })}
      confirmLabel={t('Cancel ride')} doneTitle={t('Ride cancelled')} doneMessage={t('Everyone who asked or was approved has been notified.')}
      onConfirm={async (reason) => {
        await rideApi.cancel(idOf(cancelling), reason, token)
        resultCache.clear()
        setState((s) => ({ ...s, items: s.items.filter((r) => idOf(r) !== idOf(cancelling)), total: Math.max(0, s.total - 1) }))
      }}
      onClose={() => setCancelling(null)} />}
    {hostId && <HostProfileModal userId={hostId} token={token} signedIn={signedIn} notify={notify} onClose={() => setHostId(null)} />}
  </div>
}
