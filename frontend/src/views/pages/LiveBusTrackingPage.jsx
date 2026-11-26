import React, { Suspense, lazy, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { ArrowRight, BusFront, ChevronDown, CircleAlert, Clock3, Crosshair, Flag, Gauge, Loader2, LocateFixed, MapPin, Navigation, Radio, Route, Search, SignalLow, WifiOff, X } from 'lucide-react'
import { formatNumber, t } from '../../models/i18n'
import { FRESHNESS, ago, exactTime, freshnessOf, liveApi } from '../../models/liveLocation'
import { useBusRoutes } from '../../controllers/useBusRoutes'
import { SignInPrompt } from '../components/common'
import '../styles/live-tracking.css'

const LiveBusMap = lazy(() => import('../components/LiveBusMap'))

const ACTIVE_EVERY_MS = 8000
const SELECTED_EVERY_MS = 5000
const FRESH_ICONS = { live: Radio, delayed: SignalLow, unavailable: WifiOff }

// Runs fn now and every ms while the tab is visible; stops when hidden and catches up when shown again.
function usePolling(fn, ms, enabled = true) {
  const ref = useRef(fn)
  ref.current = fn
  useEffect(() => {
    if (!enabled) return undefined
    let timer = 0
    const tick = () => { if (document.visibilityState !== 'hidden') ref.current() }
    tick()
    timer = setInterval(tick, ms)
    const onShow = () => { if (document.visibilityState === 'visible') ref.current() }
    document.addEventListener('visibilitychange', onShow)
    return () => { clearInterval(timer); document.removeEventListener('visibilitychange', onShow) }
  }, [ms, enabled])
}

// A clock in step with the server, so "12 s ago" is right even if the phone's clock is off.
function useServerClock() {
  const offset = useRef(0)
  const [now, setNow] = useState(Date.now())
  useEffect(() => { const id = setInterval(() => setNow(Date.now()), 1000); return () => clearInterval(id) }, [])
  return { now: now + offset.current, sync: (serverTime) => { if (serverTime) offset.current = Date.parse(serverTime) - Date.now() } }
}

// compact: a short word for tight lists ("Last known" instead of "Location unavailable").
const SHORT = { live: 'Live', delayed: 'Delayed', unavailable: 'Last known' }
export function FreshnessBadge({ freshness, large = false, compact = false }) {
  const Icon = FRESH_ICONS[freshness] || WifiOff
  const label = compact ? SHORT[freshness] : FRESHNESS[freshness]?.label
  return <span className={`lb-fresh is-${freshness}${large ? ' large' : ''}${compact ? ' compact' : ''}`} title={t(FRESHNESS[freshness]?.text || '')}>
    <i aria-hidden="true" />{!compact && <Icon size={large ? 15 : 12} aria-hidden="true" />} {t(label || 'Location unavailable')}</span>
}

const readUrl = () => { const q = new URLSearchParams(window.location.search); return q.get('bus') ? { busId: q.get('bus'), session: q.get('session') || '' } : null }

// /live-tracking: follow a Dhaka bus live. Buses appear while their driver shares GPS during a trip.
export function LiveBusTrackingPage({ token, signedIn, openAuth }) {
  const [search, setSearch] = useState('')
  const [active, setActive] = useState({ status: 'loading', list: [], settings: undefined })
  const [selected, setSelected] = useState(readUrl)
  // Opened from a link (?bus=…): loading at once, not an empty card.
  const [detail, setDetail] = useState(() => (readUrl() ? { status: 'loading' } : { status: 'idle' }))
  const [follow, setFollow] = useState(true)
  const [recenter, setRecenter] = useState(0)
  const [showStops, setShowStops] = useState(false)
  const [listOpen, setListOpen] = useState(() => !readUrl())
  const [routes] = useBusRoutes()
  const clock = useServerClock()

  const loadActive = useCallback(() => liveApi.active('', token)
    .then((res) => { clock.sync(res.serverTime); setActive({ status: 'ready', list: res.data, settings: res.settings }) })
    .catch((e) => setActive((a) => ({ ...a, status: a.list.length ? 'ready' : 'error', error: t(e.message) }))), [token]) // eslint-disable-line react-hooks/exhaustive-deps
  usePolling(loadActive, ACTIVE_EVERY_MS, signedIn)

  const loadDetail = useCallback(() => {
    if (!selected) return Promise.resolve()
    return liveApi.progress(selected.busId, selected.session, token)
      .then((res) => { clock.sync(res.serverTime); setDetail({ status: 'ready', data: res.data, key: `${selected.busId}:${selected.session}` }) })
      .catch((e) => setDetail((d) => (d.data ? { ...d, warning: t(e.message) } : { status: 'error', error: t(e.message) })))
  }, [selected, token]) // eslint-disable-line react-hooks/exhaustive-deps
  usePolling(loadDetail, SELECTED_EVERY_MS, signedIn && Boolean(selected))

  const choose = (busId, session = '') => {
    setSelected({ busId, session })
    setDetail({ status: 'loading' })
    setFollow(true)
    setListOpen(false)
    const q = new URLSearchParams({ bus: busId, ...(session ? { session } : {}) })
    window.history.replaceState(window.history.state, '', `/live-tracking?${q}`)
  }
  const clear = () => { setSelected(null); setDetail({ status: 'idle' }); setListOpen(true); window.history.replaceState(window.history.state, '', '/live-tracking') }

  const settings = active.settings
  const secondsAgoOf = (iso) => (iso ? Math.max(0, (clock.now - Date.parse(iso)) / 1000) : null)
  const term = search.trim().toLowerCase()
  const liveMatches = active.list.filter((b) => !term || `${b.busName} ${b.routeNumber || ''} ${b.registrationNumber || ''}`.toLowerCase().includes(term))
  const liveRoutes = new Set(active.list.map((b) => b.busId))
  const otherMatches = term.length >= 2 && routes.status === 'ready'
    ? routes.buses.filter((b) => !liveRoutes.has(b._id) && `${b.busName} ${b.routeNumber || ''}`.toLowerCase().includes(term)).slice(0, 8) : []

  const data = detail.data
  const bus = data?.bus
  const freshness = bus?.isSharing ? freshnessOf(secondsAgoOf(bus.lastUpdatedAt), settings) : 'unavailable'
  const position = bus?.isSharing && bus.latitude != null ? { lat: bus.latitude, lng: bus.longitude, heading: bus.heading, freshness } : null
  const stops = useMemo(() => data?.route?.stops || [], [data?.route?.stops?.length, data?.route?.id, data?.route?.direction]) // eslint-disable-line react-hooks/exhaustive-deps
  const others = useMemo(() => active.list.filter((b) => b.sessionId !== bus?.sessionId && b.latitude != null)
    .map((b) => ({ id: b.sessionId, busId: b.vehicleId || b.busId, session: b.sessionId, lat: b.latitude, lng: b.longitude, freshness: freshnessOf(secondsAgoOf(b.lastUpdatedAt), settings), label: `${t(b.busName)} ${b.routeNumber || ''}` })),
  [active.list, bus?.sessionId]) // eslint-disable-line react-hooks/exhaustive-deps
  const p = data?.progress

  if (!signedIn) return <div className="page inner-page lb-page"><SignInPrompt openAuth={openAuth} message={t('Sign in to follow Dhaka buses live on the map.')} /></div>

  return <div className="page inner-page lb-page">
    <section className="lb-hero">
      <div>
        <span className="lb-eyebrow"><Radio size={14} /> {t('LIVE BUS TRACKING')}</span>
        <h1>{t('Where is my bus?')}</h1>
        <p>{t('Follow Dhaka buses on the map while their drivers share live GPS during a trip.')}</p>
      </div>
      <div className="lb-hero-stat"><b>{formatNumber(active.list.length)}</b><small>{t('buses live now')}</small></div>
    </section>

    <div className={`lb-layout${selected ? ' has-selection' : ''}`}>
      <aside className={`lb-panel lb-list${listOpen ? ' open' : ''}`}>
        <button type="button" className="lb-list-toggle" onClick={() => setListOpen(!listOpen)} aria-expanded={listOpen}>
          <Search size={15} /> {selected && bus ? t('Change bus') : t('Find a bus')} <ChevronDown size={15} />
        </button>
        <div className="lb-list-body">
          <label className="lb-search"><Search size={16} aria-hidden="true" /><span className="sr-only">{t('Search buses')}</span>
            <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('Bus name, route or registration no.')} />
            {search && <button type="button" onClick={() => setSearch('')} aria-label={t('Clear')}><X size={14} /></button>}</label>
          {active.status === 'loading' ? <div className="lb-center"><Loader2 size={18} className="dv-spin" /> {t('Finding live buses…')}</div>
            : active.status === 'error' ? <p className="dv-alert danger"><CircleAlert size={15} /> {active.error}</p>
              : <>
                <p className="lb-list-head">{t('Live now')} <span>{formatNumber(liveMatches.length)}</span></p>
                {liveMatches.length === 0 && <p className="lb-empty">{term ? t('No live bus matches “{q}”.', { q: search.trim() }) : t('No bus is sharing its location right now. Buses appear here while their driver is on a trip.')}</p>}
                <ul className="lb-buses">{liveMatches.map((b) => {
                  const f = freshnessOf(secondsAgoOf(b.lastUpdatedAt), settings)
                  const current = bus?.sessionId === b.sessionId
                  return <li key={b.sessionId}><button type="button" className={current ? 'current' : ''} onClick={() => choose(b.vehicleId || b.busId, b.sessionId)}>
                    <span className={`lb-bus-icon is-${f}`}><BusFront size={16} /></span>
                    <span className="lb-bus-text"><b>{t(b.busName)} {b.routeNumber && <em>{b.routeNumber}</em>}</b>
                      <small>{t(b.startStop)} → {t(b.endStop)}{b.registrationNumber ? ` · ${b.registrationNumber}` : ''}</small></span>
                    <FreshnessBadge freshness={f} compact />
                  </button></li>
                })}</ul>
                {otherMatches.length > 0 && <>
                  <p className="lb-list-head">{t('Other buses (not live now)')}</p>
                  <ul className="lb-buses">{otherMatches.map((b) => <li key={b._id}><button type="button" onClick={() => choose(b._id)}>
                    <span className="lb-bus-icon is-off"><BusFront size={16} /></span>
                    <span className="lb-bus-text"><b>{t(b.busName)} {b.routeNumber && <em>{b.routeNumber}</em>}</b><small>{t('See the route; no live location')}</small></span>
                  </button></li>)}</ul>
                </>}
              </>}
        </div>
      </aside>

      <section className="lb-main">
        <div className="lb-panel lb-map-card">
          <div className="lb-map-bar">
            {bus ? <span className="lb-map-title"><BusFront size={16} /> <b>{t(bus.busName)}</b> {bus.routeNumber && <em>{bus.routeNumber}</em>}</span>
              : <span className="lb-map-title"><Navigation size={16} /> <b>{t('All live buses')}</b></span>}
            <span className="lb-map-tools">
              {bus && <FreshnessBadge freshness={freshness} />}
              {position && <button type="button" className={`lb-tool${follow ? ' on' : ''}`} onClick={() => setFollow(!follow)} aria-pressed={follow} title={t('Keep the bus in view')}><Crosshair size={15} /> <span>{t('Follow')}</span></button>}
              {position && <button type="button" className="lb-tool" onClick={() => setRecenter((n) => n + 1)} title={t('Centre on the bus')}><LocateFixed size={15} /> <span>{t('Centre')}</span></button>}
              {selected && <button type="button" className="lb-tool" onClick={clear} aria-label={t('Close bus')}><X size={15} /></button>}
            </span>
          </div>
          <Suspense fallback={<div className="lb-map lb-center"><Loader2 size={18} className="dv-spin" /> {t('Loading map…')}</div>}>
            <LiveBusMap stops={stops} bus={position} others={others} follow={follow} recenter={recenter} onPick={(o) => choose(o.busId, o.session)} />
          </Suspense>
          {bus && bus.isSharing && freshness !== 'live' && <p className={`lb-stale is-${freshness}`} role="status"><CircleAlert size={15} />
            {freshness === 'delayed' ? t('The location is a little old ({ago}). The bus may have moved since.', { ago: ago(secondsAgoOf(bus.lastUpdatedAt)) })
              : t('No update for {ago}. This is the last known location; the bus is probably not there any more.', { ago: ago(secondsAgoOf(bus.lastUpdatedAt)) })}</p>}
          {bus && !bus.isSharing && <p className="lb-stale is-unavailable"><WifiOff size={15} /> {t('This bus is not sharing its live location right now. Its route is shown on the map.')}</p>}
        </div>

        {selected && detail.status === 'error' && <div className="lb-panel lb-progress"><p className="dv-alert danger"><CircleAlert size={15} /> {detail.error}</p></div>}
        {selected && detail.status !== 'error' && <div className="lb-panel lb-progress">
          {detail.status === 'loading' || !data ? <div className="lb-center"><Loader2 size={18} className="dv-spin" /> {t('Loading bus…')}</div>
            : <>
                <header className="lb-progress-head">
                  <div><small>{t('Route')}</small><b>{t(data.route.start || '')} <ArrowRight size={14} /> {t(data.route.destination || '')}</b>
                    <span>{[bus.registrationNumber, bus.operator, t(bus.busType)].filter(Boolean).join(' · ')}</span></div>
                  <FreshnessBadge freshness={freshness} large />
                </header>
                {p ? <>
                  <div className="lb-where">
                    <div><small><MapPin size={13} /> {t('Current location')}</small>
                      <b>{p.currentStop ? t('At {stop}', { stop: t(p.currentStop.name) }) : p.lastPassedStop && p.nextStop ? t('Between {a} and {b}', { a: t(p.lastPassedStop.name), b: t(p.nextStop.name) }) : t('On the route')}</b></div>
                    <div><small><Flag size={13} /> {t('Next stop')}</small><b>{p.nextStop ? t(p.nextStop.name) : t('Final stop reached')}</b></div>
                  </div>
                  <div className="lb-bar" role="progressbar" aria-valuenow={p.percent} aria-valuemin={0} aria-valuemax={100} aria-label={t('Route progress')}>
                    <i style={{ width: `${p.percent}%` }} /><span className="lb-bar-bus" style={{ left: `${p.percent}%` }}><BusFront size={13} /></span>
                  </div>
                  <div className="lb-bar-labels"><span><b>{formatNumber(p.percent)}%</b> {t('done')}</span><span>{t('≈ {km} km left', { km: formatNumber(p.remainingKm) })} · {t('{n} stops left', { n: p.remainingStopCount })}</span></div>
                  {!p.onRoute && <p className="lb-stale is-delayed"><CircleAlert size={15} /> {t('The bus is about {m} m from its route (a detour or GPS error), so progress is a rough guess.', { m: formatNumber(p.offRouteMeters) })}</p>}
                  <p className="lb-estimate">{t('Progress is estimated from the route’s stops (straight lines between them); real roads are a little longer.')}</p>
                  {p.remainingStops.length > 0 && <div className="lb-stops">
                    <button type="button" className="lb-stops-toggle" onClick={() => setShowStops(!showStops)} aria-expanded={showStops}><Route size={14} /> {t('Remaining stops ({n})', { n: p.remainingStopCount })} <ChevronDown size={14} /></button>
                    {showStops && <ol>{p.remainingStops.map((s, i) => <li key={`${s.order}-${s.name}`} className={i === 0 ? 'next' : ''}><i aria-hidden="true" />{t(s.name)}<small>{t('{km} km', { km: formatNumber(Math.max(0, s.kmFromStart - p.travelledKm), { maximumFractionDigits: 1 }) })}</small></li>)}</ol>}
                  </div>}
                </> : <p className="lb-empty">{bus.isSharing ? t('Waiting for the first location from this bus…') : t('Live progress appears while the bus is sharing its location.')}</p>}
                <dl className="lb-facts">
                  <div><dt><Clock3 size={13} /> {t('Last updated')}</dt><dd>{bus.lastUpdatedAt ? <>{exactTime(bus.lastUpdatedAt)} <small>({ago(secondsAgoOf(bus.lastUpdatedAt))})</small></> : '—'}</dd></div>
                  {bus.speed != null && bus.isSharing && <div><dt><Gauge size={13} /> {t('Speed')}</dt><dd>{t('{n} km/h', { n: formatNumber(bus.speed) })}</dd></div>}
                  {bus.accuracy != null && bus.isSharing && <div><dt><Crosshair size={13} /> {t('GPS accuracy')}</dt><dd>±{formatNumber(bus.accuracy)} m</dd></div>}
                </dl>
              </>}
        </div>}
      </section>
    </div>
  </div>
}
