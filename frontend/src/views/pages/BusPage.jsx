import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Maximize2, Minimize2, ArrowDownUp, ArrowRight, ArrowUpRight, BusFront, CircleAlert, Compass, Map as MapIcon, MapPin, Search, ShieldCheck } from 'lucide-react'
import GoogleMapView, { mapsConfigured } from '../components/GoogleMapView'
import { useActiveTrip } from '../../controllers/useTrips'
import { ActiveTripPanel, useBoardControls } from '../components/Trips'
import { BusReviews } from '../components/Reviews'
import { useFavoriteBuses } from '../../controllers/useFavorites'
import { FavoriteButton } from './Favorites'
import { canFavourite } from '../../models/favorites'
import { apiRequest as request } from '../../models/api'
import { formatNumber, t, toEnglish } from '../../models/i18n'
import { formatDuration, getStops, googleMapsDirections, isPhone, money, sortedStops } from '../helpers'
import { errorText } from '../../models/messages'
import { PageHeading, StopSuggestions } from '../components/common'
import { BusNameSearch } from '../components/BusNameSearch'
import { pushEntry, replaceEntry } from '../../controllers/router'

// Where the distance came from (route-estimate "source").
export const ESTIMATE_SOURCES = {
  'google-routes': 'By road, from Google Maps',
  openstreetmap: 'By road, from OpenStreetMap',
  approximate: 'Approximate: road service unavailable',
}

// The searched trip: both places, Google's road distance and travel time, and a link to Google Maps.
// The distance is for the road between the two places, which is not the same as the bus's path or its stops.
export function TripSummary({ trip, estimate, onRetry }) {
  return <section className="panel trip-summary">
    <div className="trip-places">
      <span><i className="start" /><small>{t('Start')}</small><b>{t(trip.start.name)}</b></span>
      <ArrowRight size={16} />
      <span><i className="end" /><small>{t('Destination')}</small><b>{t(trip.end.name)}</b></span>
    </div>
    <div className="trip-figures">
      {estimate.status === 'loading' && <span className="trip-figure"><small>{t('Estimated distance')}</small><b><i className="hp-stat-skeleton" aria-hidden="true" /></b></span>}
      {estimate.status === 'ready' && <>
        <span className="trip-figure"><small>{t('Estimated distance')}</small><b>{t('{km} km', { km: formatNumber(estimate.data.distanceKm, { maximumFractionDigits: 1 }) })}</b><em>{t(ESTIMATE_SOURCES[estimate.data.source] || ESTIMATE_SOURCES['google-routes'])}</em></span>
        {estimate.data.durationMinutes > 0 && <span className="trip-figure"><small>{t('Estimated travel time')}</small><b>{formatDuration(estimate.data.durationMinutes)}</b><em>{estimate.data.source === 'google-routes'
          ? (estimate.data.durationWithoutTrafficMinutes ? t('{time} without traffic', { time: formatDuration(estimate.data.durationWithoutTrafficMinutes) }) : t('Driving, with current traffic'))
          : estimate.data.source === 'approximate' ? t('Rough estimate') : t('Driving, without traffic')}</em></span>}
      </>}
      {estimate.status === 'error' && <span className="trip-figure trip-unavailable"><small>{t('Estimated distance')}</small><b>{t('Temporarily unavailable')}</b><em>{estimate.message} <button type="button" onClick={onRetry}>{t('Try again')}</button></em></span>}
      <a className="button button-outline button-small trip-gmaps" href={googleMapsDirections(trip.start, trip.end)} target="_blank" rel="noreferrer"><MapIcon size={14} /> {isPhone() ? t('Open in Google Maps app') : t('Open in Google Maps')} <ArrowUpRight size={13} /></a>
    </div>
  </section>
}

// The search shown is kept in the address (/find-a-bus?mode=name&name=Victor), so going back to this page, or
// reloading it, brings the same results back. Returns { mode, from, to, name } or null.
export function searchFromUrl(search = window.location.search) {
  const params = new URLSearchParams(search)
  const mode = params.get('mode')
  if (mode === 'name' && params.get('name')?.trim()) return { mode, name: params.get('name').trim() }
  if (mode === 'route' && params.get('from')?.trim() && params.get('to')?.trim()) return { mode, from: params.get('from').trim(), to: params.get('to').trim() }
  return null
}
export const searchUrl = ({ mode, from, to, name }) => `/find-a-bus?${new URLSearchParams(mode === 'name' ? { mode, name } : { mode, from, to })}`

// Results and road estimates already fetched in this tab: going Back (or Forward) to a search shows them again
// without asking the server, as long as they are recent. A new search from the form always asks again.
export const RESULTS_FRESH_MS = 5 * 60 * 1000
const resultCache = new Map()
const estimateCache = new Map()
const recent = (cache, key) => { const hit = cache.get(key); return hit && Date.now() - hit.at < RESULTS_FRESH_MS ? hit.data : null }

// The search the inputs describe, in English, or the message to show when something is missing.
const readQuery = ({ mode, from = '', to = '', name = '' }) => {
  const query = { mode: mode === 'name' ? 'name' : 'route', from: toEnglish(from.trim()), to: toEnglish(to.trim()), name: toEnglish(name.trim()) }
  if (query.mode === 'route' && !query.from) return { error: t('Enter your starting point.') }
  if (query.mode === 'route' && !query.to) return { error: t('Enter your destination.') }
  if (query.mode === 'name' && !query.name) return { error: t('Enter a bus name to search.') }
  return { query }
}

export function BusPage({ token, signedIn, user, notify, navigate: goTo, openAuth }) {
  // A search started from another page (home or a route page) runs once when the page opens; otherwise a search
  // kept in the address (coming back to this page) is shown again.
  const savedQuery = useMemo(() => {
    try {
      const handed = JSON.parse(sessionStorage.getItem('bus_query') || '{}')
      if (handed.auto) return handed
    } catch { /* fall through to the address */ }
    const fromUrl = searchFromUrl()
    return fromUrl ? { ...fromUrl, auto: true, restoring: true } : {}
  }, [])
  // Where the reader was (the selected bus and how far down), saved when they leave for another page.
  const restoreView = useRef(savedQuery.restoring ? window.history.state?.busView || null : null)
  // Leaving for another page: remember the selected bus and the scroll position in this history entry.
  const navigate = (...args) => {
    if (window.location.pathname === '/find-a-bus') {
      window.history.replaceState({ ...(window.history.state || {}), busView: { selectedId, scrollY: window.scrollY } }, '', window.location.href)
    }
    goTo(...args)
  }
  // Cleared after the first render, not while reading it: React's development mode runs that read twice.
  useEffect(() => { sessionStorage.removeItem('bus_query') }, [])
  const [from, setFrom] = useState(savedQuery.from || '')
  const [to, setTo] = useState(savedQuery.to || '')
  const [name, setName] = useState(savedQuery.name || '')
  const [mode, setMode] = useState(savedQuery.mode === 'name' ? 'name' : 'route')
  const [buses, setBuses] = useState([])
  const [loading, setLoading] = useState(false)
  const [searched, setSearched] = useState(null) // the search shown: { mode, from, to, name }
  const [error, setError] = useState('')
  const [trip, setTrip] = useState(null) // { start, end }: the stops the searched places resolved to
  const [estimate, setEstimate] = useState({ status: 'idle' })
  const [selectedId, setSelectedId] = useState(null)
  // The route map can fill the whole screen, so directions are easy to follow on a phone.
  const [mapFull, setMapFull] = useState(false)
  useEffect(() => {
    if (!mapFull) return undefined
    const overflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    const onKey = (event) => { if (event.key === 'Escape') setMapFull(false) }
    window.addEventListener('keydown', onKey)
    return () => { document.body.style.overflow = overflow; window.removeEventListener('keydown', onKey) }
  }, [mapFull])
  // On phones the map sits above the list, out of sight while choosing a bus further down: bring it into view.
  const mapRef = useRef(null)
  const showBusOnMap = (busId) => {
    setSelectedId(busId)
    const box = mapRef.current
    if (!box || typeof window.matchMedia !== 'function' || !window.matchMedia('(max-width: 980px)').matches) return
    const top = box.getBoundingClientRect().top
    if (top < 70 || top > window.innerHeight * 0.4) {
      window.scrollTo({ top: window.scrollY + top - 80, behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' })
    }
  }
  const activeTrip = useActiveTrip(token, signedIn).trip

  // One request for the whole list of favourites, so each card knows its heart state without asking the server.
  const favorites = useFavoriteBuses({ token, user, signedIn, notify, openAuth })

  // Each search started counts up, so an answer that arrives after a newer search (or after Back) is ignored.
  const searchId = useRef(0)

  // Road distance and time from Google, through the backend, which holds the server key. reuse: coming back to a
  // search, show the estimate already fetched for it.
  const loadEstimate = useCallback((start, end, { reuse = false } = {}) => {
    const key = `${start}|${end}`, id = searchId.current
    const kept = reuse && recent(estimateCache, key)
    if (kept) { setEstimate({ status: 'ready', data: kept }); return }
    setEstimate({ status: 'loading' })
    request(`/bus/route-estimate?${new URLSearchParams({ startLocation: start, endLocation: end })}`, { token })
      .then((data) => { estimateCache.set(key, { data: data.data, at: Date.now() }); if (id === searchId.current) setEstimate({ status: 'ready', data: data.data }) })
      .catch((e) => { if (id === searchId.current) setEstimate({ status: 'error', message: errorText(e) }) })
  }, [token])

  // Shows the results for a search. How it enters the browser history:
  //  - a search typed into the form is a new page of results: a new entry, so Back returns to what was shown
  //    before it (the empty form or the previous search); searching again for the same thing adds nothing;
  //  - a search handed over from another page (a route's "Search buses on this route") replaces the entry, since
  //    the empty form was never seen;
  //  - restoring (Back, Forward or a reload) only shows the entry the browser is already on.
  const runSearch = async (query, { restoring = false, handed = false } = {}) => {
    const id = ++searchId.current
    if (window.location.pathname === '/find-a-bus') {
      const url = searchUrl(query)
      if (restoring) replaceEntry(url)
      else if (handed) replaceEntry(url, { page: 'Find a bus', busView: undefined })
      else pushEntry(url, { page: 'Find a bus' })
    }
    setError(''); setSearched(query); setSelectedId(null); setTrip(null)
    const show = (result) => {
      setBuses(result.data || [])
      // Back on this page: select the bus that was selected before leaving, if it is still in the results.
      const keep = restoring && restoreView.current?.selectedId
      setSelectedId((keep && result.data?.some((bus) => bus._id === keep) ? keep : result.data?.[0]?._id) || null)
      if (result.start && result.end) setTrip({ start: result.start, end: result.end })
    }
    const key = searchUrl(query)
    if (query.mode === 'route') loadEstimate(query.from, query.to, { reuse: restoring }); else setEstimate({ status: 'idle' })
    // Coming back to results already fetched: show them at once, with no request and no "searching" animation.
    const kept = restoring && recent(resultCache, key)
    if (kept) { show(kept); setLoading(false); return }
    setLoading(true)
    const shownUntil = Date.now() + (restoring ? 0 : MIN_LOADER_MS)
    try {
      const result = await request(query.mode === 'route'
        ? `/bus/search-by-route?${new URLSearchParams({ startLocation: query.from, endLocation: query.to })}`
        : `/bus/search-by-name?${new URLSearchParams({ busName: query.name })}`, { token })
      resultCache.set(key, { data: result, at: Date.now() })
      if (id === searchId.current) show(result)
    } catch (e) {
      if (id === searchId.current) { setBuses([]); setError(errorText(e)); setEstimate({ status: 'idle' }) }
    } finally {
      await new Promise((resolve) => window.setTimeout(resolve, Math.max(0, shownUntil - Date.now())))
      if (id === searchId.current) setLoading(false)
    }
  }
  // A bus picked from the suggestions: search for it straight away.
  const pickBus = (picked) => {
    if (!signedIn) return openAuth()
    const { query, error: problem } = readQuery({ mode: 'name', from, to, name: picked })
    if (problem) { setError(problem); return }
    runSearch(query)
  }
  const search = (event) => {
    event?.preventDefault?.()
    if (!signedIn) return openAuth()
    const { query, error: problem } = readQuery({ mode, from, to, name })
    if (problem) { setError(problem); return }
    runSearch(query)
  }
  // Run a search handed over from another page, or kept in the address, once signed in.
  const autoSearched = useRef(false)
  useEffect(() => {
    if (autoSearched.current || !savedQuery.auto || !signedIn) return
    autoSearched.current = true
    const { query, error: problem } = readQuery(savedQuery)
    if (problem) { setError(problem); return }
    runSearch(query, savedQuery.restoring ? { restoring: true } : { handed: true })
  }, [signedIn]) // eslint-disable-line react-hooks/exhaustive-deps
  // Back and Forward between searches (or back to the empty form) stay on this page: show the search the browser
  // moved to.
  const latest = useRef({})
  latest.current = { runSearch, signedIn, searched }
  useEffect(() => {
    const onPop = () => {
      if (window.location.pathname !== '/find-a-bus') return
      const query = searchFromUrl()
      const shown = latest.current.searched
      if (query && shown && searchUrl(query) === searchUrl(shown)) return
      if (!query) {
        searchId.current++
        setSearched(null); setBuses([]); setError(''); setTrip(null); setSelectedId(null); setEstimate({ status: 'idle' }); setLoading(false)
        return
      }
      setMode(query.mode); setFrom(query.from || ''); setTo(query.to || ''); setName(query.name || '')
      if (!latest.current.signedIn) return
      restoreView.current = window.history.state?.busView || null
      latest.current.runSearch(query, { restoring: true })
    }
    window.addEventListener('popstate', onPop)
    return () => window.removeEventListener('popstate', onPop)
  }, [])
  // Keep the selected bus and scroll position in this history entry as they change, so they come back however the
  // reader leaves (a card's link, the menu, the notification bell…). Saved once scrolling settles.
  useEffect(() => {
    if (!searched || loading) return
    let timer = 0
    const save = () => {
      if (window.location.pathname !== '/find-a-bus') return
      window.history.replaceState({ ...(window.history.state || {}), busView: { selectedId, scrollY: window.scrollY } }, '', window.location.href)
    }
    const onScroll = () => { window.clearTimeout(timer); timer = window.setTimeout(save, 250) }
    save()
    window.addEventListener('scroll', onScroll, { passive: true })
    return () => { window.clearTimeout(timer); window.removeEventListener('scroll', onScroll) }
  }, [searched, loading, selectedId])
  // Once the restored results are on screen, scroll back to where the reader was.
  useEffect(() => {
    const view = restoreView.current
    if (loading || !view || !searched) return
    restoreView.current = null
    const frame = window.requestAnimationFrame(() => window.scrollTo(0, view.scrollY || 0))
    return () => window.cancelAnimationFrame(frame)
  }, [loading, searched])

  const saveTrip = async (bus) => {
    if (!signedIn) return openAuth()
    const distance = estimate.status === 'ready' ? estimate.data.distanceKm : 0
    try {
      await request('/trip/add', { token, method: 'POST', body: JSON.stringify({ busId: bus._id, busName: bus.busName, distance, fare: Number(bus.base_fare || 0), source: bus.match?.boarding.name || from, destination: bus.match?.alighting.name || to }) })
      notify(t('Trip added to your history.'))
    } catch (e) { notify(errorText(e)) }
  }

  // The selected bus on the map: its whole route in travel direction, and the stops ridden.
  const selected = buses.find((bus) => bus._id === selectedId)
  const mapStops = useMemo(() => {
    const stops = sortedStops(selected)
    return selected?.match?.direction === 'return' ? stops.reverse() : stops
  }, [selected])
  const mapSegment = useMemo(() => selected?.match?.segment || [], [selected])
  // The map stays mounted across searches: Google bills each new map load.
  const showMap = Boolean(searched && !error && (searched.mode === 'route' || loading || buses.length))

  return <div className="page inner-page bus-page">
    <PageHeading eyebrow={t('GET THERE WITH EASE')} title={t('Find your bus.')} description={t('Search Dhaka bus routes by the places you are going—or by bus name.')} icon={BusFront} />
    <section className="panel bus-search-panel">
      <div className="segmented"><button className={mode === 'route' ? 'selected' : ''} onClick={() => { setMode('route'); setError('') }}><MapPin size={15} /> {t('Search by route')}</button><button className={mode === 'name' ? 'selected' : ''} onClick={() => { setMode('name'); setError('') }}><Search size={15} /> {t('Search by bus')}</button></div>
      <StopSuggestions />
      <form className="bus-search-form" onSubmit={search}>
        {mode === 'route' ? <>
          <label className="field"><small>{t('STARTING POINT')}</small><span><span className="input-dot" /><input value={from} onChange={(e) => setFrom(e.target.value)} placeholder={t('e.g. Uttara')} list="stop-suggestions" /></span></label>
          <button type="button" className="swap-button" onClick={() => { setFrom(to); setTo(from) }} aria-label={t('Swap locations')}><ArrowDownUp size={15} /></button>
          <label className="field"><small>{t('DESTINATION')}</small><span><MapPin size={16} /><input value={to} onChange={(e) => setTo(e.target.value)} placeholder={t('e.g. Motijheel')} list="stop-suggestions" /></span></label>
        </> : <BusNameSearch value={name} onChange={setName} onPick={pickBus} />}
        <button className={`button button-green${loading ? ' is-loading' : ''}`} type="submit" disabled={loading} aria-busy={loading}>{loading ? <><span className="button-spinner" aria-hidden="true" /> {t('Searching…')}</> : <><Search size={16} /> {t('Search buses')}</>}</button>
      </form>
      {error && <div className="inline-error" role="alert">{error}</div>}
      {!signedIn && <div className="signin-hint"><ShieldCheck size={15} /> {t('Bus search requires an account.')} <button onClick={openAuth}>{t('Sign in')}</button> {t('to find routes.')}</div>}
    </section>
    <ActiveTripPanel token={token} signedIn={signedIn} notify={notify} />
    {searched?.mode === 'route' && trip && <TripSummary trip={trip} estimate={estimate} onRetry={() => loadEstimate(searched.from, searched.to)} />}
    {searched && <section className={`results-area${showMap ? ' results-with-map' : ''}`}>
      <div className="results-list">
        <div className="results-heading"><div><div className="eyebrow">{searched.mode === 'route' ? t('YOUR ROUTE') : t('BUS SEARCH')}</div><h2>{searched.mode === 'route' ? t('{from} to {to}', { from: t(trip?.start.name || searched.from), to: t(trip?.end.name || searched.to) }) : t('Buses matching “{name}”', { name: searched.name })}</h2></div>{!loading && !error && <span className="result-count">{buses.length === 1 ? t('{count} bus found', { count: 1 }) : t('{count} buses found', { count: buses.length })}</span>}</div>
        {loading ? <BusSearchLoader mode={searched.mode} />
          : error ? <div className="empty-state"><span className="empty-icon"><CircleAlert size={23} /></span><b>{t('Search could not be completed')}</b><span>{error}</span></div>
          : buses.length ? <div className="bus-list">{buses.map((bus) => <BusCard key={bus._id} bus={bus} user={user} token={token} signedIn={signedIn} openAuth={openAuth} selected={bus._id === selectedId} onSelect={() => showBusOnMap(bus._id)} favorite={favorites.isFavorite(bus)} favoritePending={favorites.isPending(bus)} onFavorite={() => favorites.toggle(bus)} onAddTrip={() => saveTrip(bus)} boarding={{ token, signedIn, openAuth, notify, activeTrip }} onDetails={bus.routeNumber ? () => navigate('Bus details', { route: bus.routeNumber }) : null} notify={notify} />)}</div>
          : <div className="empty-state"><span className="empty-icon"><BusFront size={23} /></span><b>{t('No buses found just yet')}</b><span>{searched.mode === 'route' ? t('No bus in the database stops at both places. Try a nearby stop.') : t('Try another bus name or route number.')}</span></div>}
      </div>
      {showMap && <aside className={`results-map${mapFull ? ' is-full' : ''}`} ref={mapRef}>
        <div className="results-map-bar">
          <b>{trip ? `${t(trip.start.name)} → ${t(trip.end.name)}` : t('Route map')}</b>
          <button type="button" className="map-expand" onClick={() => setMapFull(!mapFull)} aria-pressed={mapFull}>
            {mapFull ? <><Minimize2 size={15} /> {t('Close')}</> : <><Maximize2 size={15} /> {t('Full screen')}</>}</button>
        </div>
        <GoogleMapView start={trip?.start} end={trip?.end} roadPath={estimate.status === 'ready' ? estimate.data.polyline : null} busStops={mapStops} segment={mapSegment} />
        {mapsConfigured
          ? <ul className="gmap-legend">
            {trip && <li><i className="legend-road" /> {t('Google driving route (estimate)')}</li>}
            {selected && <li><i className="legend-ride" /> {t('{bus}: stops you ride', { bus: t(selected.busName) })}</li>}
            {selected && <li><i className="legend-bus" /> {t('Rest of the bus route')}</li>}
          </ul>
          : <p className="gmap-note">{trip ? t('Google Maps driving route between your places. A bus may take other roads; its stops are listed on each card.') : t('Google Maps route between the first and last stop of the selected bus.')}</p>}
      </aside>}
    </section>}
    {!searched && <section className="suggestion"><span className="suggestion-icon"><Compass size={19} /></span><div><b>{t('Start with a familiar landmark')}</b><p>{t('Try popular Dhaka stops like Farmgate, Mohakhali, Gulistan, or Uttara.')}</p></div><span className="suggestion-pills"><button onClick={() => { setMode('route'); setFrom('Farmgate'); setTo('Uttara') }}>{t('Farmgate')}</button><button onClick={() => { setMode('route'); setFrom('Gulistan'); setTo('Uttara') }}>{t('Gulistan')}</button></span></section>}
  </div>
}

// Shown while a search runs: a bus driving past stops that light up in turn, with a line saying what is
// being checked. Purely visual; the steps are not tied to real progress.
// Results usually come back from the server's memory in a few milliseconds; without a minimum the loader only
// flickers, so it stays up at least this long.
export const MIN_LOADER_MS = 1200

export const LOADER_STEPS = {
  route: ['Checking every bus route…', 'Matching your stops in order…', 'Working out distance and time…'],
  name: ['Checking every bus route…', 'Matching the bus name…', 'Getting the stops ready…'],
}

export function BusSearchLoader({ mode }) {
  const steps = LOADER_STEPS[mode] || LOADER_STEPS.route
  const [step, setStep] = useState(0)
  useEffect(() => {
    const timer = window.setInterval(() => setStep((current) => (current + 1) % steps.length), 1100)
    return () => window.clearInterval(timer)
  }, [steps.length])
  return <div className="bus-loader" role="status" aria-live="polite">
    <div className="bus-loader-scene" aria-hidden="true">
      <span className="bus-loader-road" />
      <span className="bus-loader-stop" /><span className="bus-loader-stop" /><span className="bus-loader-stop" /><span className="bus-loader-stop" />
      <span className="bus-loader-bus"><BusFront size={26} strokeWidth={2.2} /></span>
    </div>
    <b>{t('Finding your buses…')}</b>
    <span className="bus-loader-step" key={step}>{t(steps[step])}</span>
  </div>
}

// One search result: name, route number and stops, and for a route search where to board and get off.
export function BusCard({ bus, user, token, signedIn, openAuth, selected, onSelect, favorite, favoritePending, onFavorite, onAddTrip, boarding, onDetails, notify }) {
  const board = useBoardControls({ bus, ...boarding })
  const stops = getStops(bus)
  const match = bus.match
  return <article className={`bus-result panel${selected ? ' selected' : ''}`}>
    <span className="bus-result-icon"><BusFront size={20} /></span><div className="bus-result-main">
      <div className="bus-title"><h3>{bus.busName ? t(bus.busName) : t('City bus')}</h3>{bus.routeNumber && <span className="route-badge">{bus.routeNumber}</span>}{match?.direction === 'return' && <span className="route-badge return-badge">{t('Return trip')}</span>}</div>
      <div className="bus-details"><span>{bus.operator ? t(bus.operator) : t('Dhaka transit')}</span>{bus.busType && <><i /> <span>{t(bus.busType)}</span></>}{bus.frequency && <><i /> <span>{t(bus.frequency)}</span></>}</div>
      {match
        ? <><div className="stop-line"><span>{t(match.boarding.name)}</span><span className="stop-track" /><span>{t(match.alighting.name)}</span></div>
          <div className="stop-count">{t('Board at {from}, get off at {to}', { from: t(match.boarding.name), to: t(match.alighting.name) })} · {match.stopsToRide === 1 ? t('1 stop to ride') : t('{count} stops to ride', { count: match.stopsToRide })} · {t('{count} stops on this route', { count: stops.length })}</div></>
        : <>{stops.length > 0 && <div className="stop-line"><span>{t(stops[0])}</span><span className="stop-track" /><span>{t(stops[stops.length - 1])}</span></div>}
          <div className="stop-count">{t('{count} stops on this route', { count: stops.length })}</div></>}
      <div className="bus-card-actions">
        <button className={selected ? 'bus-map-toggle active' : 'bus-map-toggle'} onClick={onSelect} aria-pressed={selected}><MapIcon size={13} /> {selected ? t('Shown on map') : t('Show on map')}</button>
        {onDetails && <button className="bus-details-link" onClick={onDetails}>{t('View details')} <ArrowRight size={12} /></button>}
      </div>
    </div><div className="bus-result-side">{canFavourite(bus) && <FavoriteButton active={favorite} pending={favoritePending} onClick={onFavorite} />}<span className="fare-label">{t('STARTS FROM')}</span><b>{money(bus.base_fare)}</b><small>{t('per ride')}</small>{board.button}<button className="button button-outline button-small" onClick={onAddTrip} title={t('Add a trip you already took to your history')}>{t('Log trip')}</button></div>{board.form}<BusReviews bus={bus} user={user} token={token} signedIn={signedIn} notify={notify} openAuth={openAuth} />
  </article>
}
