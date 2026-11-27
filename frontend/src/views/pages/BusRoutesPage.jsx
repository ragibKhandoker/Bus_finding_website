import React, { useCallback, useEffect, useState } from 'react'
import { ArrowLeft, ArrowRight, BusFront, ChevronDown, Map as MapIcon, MapPin, Route, Search } from 'lucide-react'
import { BusReviews } from '../components/Reviews'
import { BusPhoto } from '../components/BusPhoto'
import { useFavoriteStatus } from '../../controllers/useFavorites'
import { FavoriteButton } from './Favorites'
import { canFavourite, favouriteApi } from '../../models/favorites'
import { apiRequest as request } from '../../models/api'
import { formatNumber, t } from '../../models/i18n'
import { BUS_TYPES, coordinate, googleMapsPlace, googleMapsRouteDirections, money, routeLengthKm, sortedStops } from '../helpers'
import { errorText } from '../../models/messages'
import { findCachedRoute, routesFresh } from '../../models/busRoutes'
import { useBusRoutes } from '../../controllers/useBusRoutes'
import { usePersistentState } from '../../controllers/usePersistentState'
import { RouteTile, RoutesStatus } from '../components/BusRoutes'
import { PageHeading } from '../components/common'
import { VoiceSearchButton } from '../components/VoiceSearch'
import { readRouteSearch, splitFromTo } from '../../models/voiceQuery'
import { pageFromPath, previousUrl, routeFromPath } from '../../controllers/router'
import { PERSIST_PREFIX } from '../../controllers/usePersistentState'

export const ROUTES_PAGE_SIZE = 24
const ROUTES_KEPT = ['routes:query', 'routes:type', 'routes:limit']
const ROUTES_ENTRY_KEY = 'sc_routes_entry'

// Whether this tab was opened by reloading the page; only the first page shown after the reload counts.
let reloadPending = (() => {
  try { return performance.getEntriesByType('navigation')[0]?.type === 'reload' } catch { return false }
})()

// The search and filter are kept only for going back to the same list with the browser's Back or Forward (e.g.
// from a route's page). Opening Bus routes from the menu, a link or by reloading the page starts with a clear list.
export function forgetRoutesSearchUnlessReturning() {
  const reloaded = reloadPending
  reloadPending = false
  // Each visit marks its history entry; Back or Forward to that entry finds the mark, a new visit does not.
  const mark = window.history.state?.routesVisit
  let returning = false
  try { returning = Boolean(mark) && !reloaded && sessionStorage.getItem(ROUTES_ENTRY_KEY) === mark } catch { /* storage blocked */ }
  if (returning) return
  try { ROUTES_KEPT.forEach((key) => sessionStorage.removeItem(PERSIST_PREFIX + key)) } catch { /* nothing kept */ }
  const visit = `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 7)}`
  window.history.replaceState({ ...(window.history.state || {}), routesVisit: visit }, '', window.location.href)
  try { sessionStorage.setItem(ROUTES_ENTRY_KEY, visit) } catch { /* not remembered */ }
}

// Every route stored in the database, searchable by bus, route number or stop and filterable by bus type.
export function BusRoutesPage({ navigate }) {
  const [state, load] = useBusRoutes()
  // The search, the filter and how many routes are shown are kept, so Back from a route's page lands on the
  // same list; a fresh visit (a tab, a link, a reload) clears them first. Runs before they are read.
  useState(forgetRoutesSearchUnlessReturning)
  const [query, setQuery] = usePersistentState('routes:query', '')
  const [type, setType] = usePersistentState('routes:type', 'all')
  const [limit, setLimit] = usePersistentState('routes:limit', ROUTES_PAGE_SIZE)
  const term = query.trim().toLowerCase()
  // "Uttara → Motijheel" (or "Uttara to Motijheel", "উত্তরা থেকে মতিঝিল"): the routes stopping at both places.
  const pair = term ? splitFromTo(term) : null
  const has = (text, part) => text && (text.toLowerCase().includes(part) || t(text).toLowerCase().includes(part))
  // Matches the English names stored in the database and their Bangla, so either language can be typed.
  const buses = state.status === 'ready' ? state.buses.filter((bus) => bus.routeNumber && (type === 'all' || bus.busType === type) &&
    (!term || [bus.busName, bus.operator, bus.routeNumber, ...(bus.stops || []).map((s) => s.name)].some((text) => has(text, term)) ||
      (pair && [pair.from, pair.to].every((part) => (bus.stops || []).some((s) => has(s.name, part)))))) : []
  // A search said out loud: a bus, a route number, a stop, or two places.
  const [voiceSlot, setVoiceSlot] = useState(null) // where the voice card shows: under the search box
  const onVoice = (readings) => {
    const said = readRouteSearch(readings, state.status === 'ready' ? state.buses : [])
    if (!said) return null
    // Nothing said fits a bus, route or stop: keep the list as it is rather than search for the wrong words.
    if (said.kind === 'text') return t('No bus, route or stop matched “{said}”. Try again.', { said: said.said })
    const next = said.kind === 'route' ? `${t(said.from)} → ${t(said.to)}` : said.kind === 'number' ? said.text : t(said.text)
    setQuery(next); setLimit(ROUTES_PAGE_SIZE)
    return t('Showing routes for “{query}”', { query: next })
  }
  const filter = (next) => { setType(next); setLimit(ROUTES_PAGE_SIZE) }
  return <div className="page inner-page">
    <PageHeading eyebrow={t('ROUTE DATABASE')} title={t('Bus routes in Dhaka')} description={t('Every route in the database. Open a route to see its stops in order, the fare and where each stop is.')} icon={Route} />
    <section className="panel routes-toolbar">
      <label className="hp-route-search"><Search size={16} /><input value={query} onChange={(e) => { setQuery(e.target.value); setLimit(ROUTES_PAGE_SIZE) }} placeholder={t('Search a bus or a stop')} /><VoiceSearchButton slot={voiceSlot} onResult={onVoice} examples={['Raida', 'DBR 12', 'Uttara to Motijheel']} /></label>
      <div className="segmented routes-filter">{['all', ...BUS_TYPES].map((option) => <button key={option} className={type === option ? 'selected' : ''} onClick={() => filter(option)}>{option === 'all' ? t('All buses') : t(option)}</button>)}</div>
      <div className="voice-slot" ref={setVoiceSlot} />
    </section>
    <section className="results-area">
      {RoutesStatus({ state, onRetry: load }) || <>
        <div className="results-heading"><div><div className="eyebrow">{t('ALL ROUTES')}</div><h2>{term ? t('Routes matching “{query}”', { query: query.trim() }) : t('Bus routes')}</h2></div><span className="result-count">{t('{count} routes', { count: buses.length })}</span></div>
        {buses.length
          ? <div className="hp-routes hp-routes-3">{buses.slice(0, limit).map((bus) => <RouteTile key={bus._id} bus={bus} onOpen={() => navigate('Bus details', { route: bus.routeNumber })} />)}</div>
          : <div className="empty-state panel"><b>{t('No route matches “{query}”', { query: query.trim() })}</b><span>{t('Try another bus name or stop.')}</span></div>}
        {buses.length > limit && <div className="routes-more"><button className="button button-outline" onClick={() => setLimit(limit + ROUTES_PAGE_SIZE)}>{t('Show more routes ({count} left)', { count: buses.length - limit })} <ChevronDown size={15} /></button></div>}
      </>}
    </section>
  </div>
}

// One route's own page: what it is, the fare, and every stop in order with its location.
export function BusDetailPage({ routeNumber, token, user, signedIn, notify, navigate, openAuth }) {
  // A route already in the shared list shows at once; otherwise only this route is fetched.
  const [state, setState] = useState(() => findCachedRoute(routeNumber) ? { status: 'ready', bus: findCachedRoute(routeNumber) } : { status: 'loading' })
  const load = useCallback(() => {
    if (routesFresh && findCachedRoute(routeNumber)) return
    if (!findCachedRoute(routeNumber)) setState({ status: 'loading' })
    request(`/bus/route/${encodeURIComponent(routeNumber)}`)
      .then((data) => setState({ status: 'ready', bus: data.data }))
      .catch((error) => { if (!findCachedRoute(routeNumber)) setState({ status: 'error', message: errorText(error) }) })
  }, [routeNumber])
  useEffect(() => { load() }, [load])
  const favorite = useFavoriteStatus({ busId: state.bus?._id, token, signedIn, notify, openAuth })
  // Opening a Dhaka City bus adds it to the user's recently viewed buses (the server refuses any other).
  const viewedId = state.bus && signedIn && canFavourite(state.bus) ? state.bus._id : null
  useEffect(() => { if (viewedId) favouriteApi.recordView(viewedId, token).catch(() => {}) }, [viewedId, token])
  // Opened from the list: step back to it, exactly like the browser's Back button (same search, same scroll
  // position, and no extra entry, so Forward still leads here). Opened directly or from another page: open the list.
  const backToList = () => {
    const from = previousUrl().split('?')[0]
    if (from && pageFromPath(from) === 'Bus routes' && !routeFromPath(from)) window.history.back()
    else navigate('Bus routes')
  }
  const back = <button className="route-back" onClick={backToList}><ArrowLeft size={15} /> {t('All bus routes')}</button>
  if (state.status !== 'ready') {
    return <div className="page inner-page">{back}
      {state.status === 'loading'
        ? <div className="empty-state"><span className="spinner" /> {t('Loading bus routes…')}</div>
        : <div className="empty-state panel"><span className="empty-icon"><BusFront size={22} /></span><b>{t('Bus route could not be loaded')}</b><span>{state.message}</span><button className="button button-outline button-small" onClick={load}>{t('Try again')}</button></div>}
    </div>
  }
  const { bus } = state
  const stops = sortedStops(bus)
  const first = stops[0], last = stops[stops.length - 1]
  const km = routeLengthKm(stops)
  const directions = googleMapsRouteDirections(stops)
  const planTrip = () => { sessionStorage.setItem('bus_query', JSON.stringify({ mode: 'route', from: first.name, to: last.name, auto: true })); navigate('Find a bus') }
  return <div className="page inner-page route-detail">
    {back}
    <section className="panel route-hero has-photo">
      <BusPhoto bus={bus} isAdmin={signedIn && user?.role?.toLowerCase() === 'admin'} token={token} notify={notify} />
      <div className="route-hero-main">
      <div className="route-hero-head">
        <span className="hp-route-no route-hero-no">{bus.routeNumber}</span>
        <div>
          <h1>{t(bus.busName)}</h1>
          <p>{[bus.operator, bus.busType, bus.frequency].filter(Boolean).map((text) => t(text)).join(' · ')}</p>
        </div>
        {canFavourite(bus)
          ? <FavoriteButton active={favorite.favorite} pending={favorite.pending} onClick={favorite.toggle} withLabel className="route-hero-favorite" />
          : <span className="route-outside-city" title={t('Favourite buses are for routes inside Dhaka City.')}>{t('Outside Dhaka City')}</span>}
      </div>
      {first && <div className="hp-route-ends route-hero-ends"><span><i className="start" />{t(first.name)}</span><ArrowRight size={16} /><span><i className="end" />{t(last.name)}</span></div>}
      <div className="route-facts">
        <span><small>{t('Stops')}</small><b>{formatNumber(stops.length)}</b></span>
        <span><small>{t('Route length')}</small><b>{t('~{km} km', { km: Math.round(km) })}</b></span>
        <span><small>{t('Fare from')}</small><b>{money(bus.base_fare)}</b></span>
        <span><small>{t('Per km')}</small><b>{money(bus.per_km_fare)}</b></span>
      </div>
      {first && <div className="route-hero-actions">
        <button className="button button-green button-small" onClick={planTrip}><Search size={14} /> {t('Search buses on this route')}</button>
        {directions && <a className="button button-outline button-small" href={directions} target="_blank" rel="noreferrer"><MapIcon size={14} /> {t('Open on the map')}</a>}
      </div>}
      </div>
    </section>
    <section className="panel route-stops-panel">
      <div className="results-heading"><div><div className="eyebrow">{t('STOPS IN ORDER')}</div><h2>{t('Where this bus stops')}</h2></div><span className="result-count">{t('{count} stops', { count: stops.length })}</span></div>
      <ol className="hp-route-stops">
        {stops.map((stop) => <li key={`${stop.order}-${stop.name}`}>
          <span className="hp-stop-no">{formatNumber((stop.order ?? 0) + 1)}</span>
          <span className="hp-stop-name">{t(stop.name)}</span>
          <a href={googleMapsPlace(stop)} target="_blank" rel="noreferrer" title={t('Open on the map')}>
            <MapPin size={12} /> {coordinate(stop.lat)}, {coordinate(stop.lng)}
          </a>
        </li>)}
      </ol>
      <p className="route-note">{t('Stop lists come from public listings and the locations are approximate.')}</p>
    </section>
    <section className="panel route-reviews"><BusReviews bus={bus} user={user} token={token} signedIn={signedIn} notify={notify} openAuth={openAuth} defaultOpen /></section>
  </div>
}
