import React, { useCallback, useEffect, useState } from 'react'
import { ArrowLeft, ArrowRight, BusFront, ChevronDown, Map as MapIcon, MapPin, Route, Search } from 'lucide-react'
import { BusReviews } from '../components/Reviews'
import { useFavoriteStatus } from '../../controllers/useFavorites'
import { FavoriteButton } from './Favorites'
import { apiRequest as request } from '../../models/api'
import { formatNumber, t } from '../../models/i18n'
import { BUS_TYPES, coordinate, money, routeLengthKm, sortedStops } from '../helpers'
import { errorText } from '../../models/messages'
import { findCachedRoute, routesFresh } from '../../models/busRoutes'
import { useBusRoutes } from '../../controllers/useBusRoutes'
import { RouteTile, RoutesStatus } from '../components/BusRoutes'
import { PageHeading } from '../components/common'

export const ROUTES_PAGE_SIZE = 24

// Every route stored in the database, searchable by bus, route number or stop and filterable by bus type.
export function BusRoutesPage({ navigate }) {
  const [state, load] = useBusRoutes()
  const [query, setQuery] = useState('')
  const [type, setType] = useState('all')
  const [limit, setLimit] = useState(ROUTES_PAGE_SIZE)
  const term = query.trim().toLowerCase()
  // Matches the English names stored in the database and their Bangla, so either language can be typed.
  const buses = state.status === 'ready' ? state.buses.filter((bus) => bus.routeNumber && (type === 'all' || bus.busType === type) &&
    (!term || [bus.busName, bus.operator, bus.routeNumber, ...(bus.stops || []).map((s) => s.name)]
      .some((text) => text && (text.toLowerCase().includes(term) || t(text).toLowerCase().includes(term))))) : []
  const filter = (next) => { setType(next); setLimit(ROUTES_PAGE_SIZE) }
  return <div className="page inner-page">
    <PageHeading eyebrow={t('ROUTE DATABASE')} title={t('Bus routes in Dhaka')} description={t('Every route in the database. Open a route to see its stops in order, the fare and where each stop is.')} icon={Route} />
    <section className="panel routes-toolbar">
      <label className="hp-route-search"><Search size={16} /><input value={query} onChange={(e) => { setQuery(e.target.value); setLimit(ROUTES_PAGE_SIZE) }} placeholder={t('Search a bus or a stop')} /></label>
      <div className="segmented routes-filter">{['all', ...BUS_TYPES].map((option) => <button key={option} className={type === option ? 'selected' : ''} onClick={() => filter(option)}>{option === 'all' ? t('All buses') : t(option)}</button>)}</div>
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
  const back = <button className="route-back" onClick={() => navigate('Bus routes')}><ArrowLeft size={15} /> {t('All bus routes')}</button>
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
  const planTrip = () => { sessionStorage.setItem('bus_query', JSON.stringify({ mode: 'route', from: first.name, to: last.name, auto: true })); navigate('Find a bus') }
  return <div className="page inner-page route-detail">
    {back}
    <section className="panel route-hero">
      <div className="route-hero-head">
        <span className="hp-route-no route-hero-no">{bus.routeNumber}</span>
        <div>
          <h1>{t(bus.busName)}</h1>
          <p>{[bus.operator, bus.busType, bus.frequency].filter(Boolean).map((text) => t(text)).join(' · ')}</p>
        </div>
        <FavoriteButton active={favorite.favorite} pending={favorite.pending} onClick={favorite.toggle} withLabel className="route-hero-favorite" />
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
        <a className="button button-outline button-small" href={`https://www.openstreetmap.org/?mlat=${first.lat}&mlon=${first.lng}#map=13/${first.lat}/${first.lng}`} target="_blank" rel="noreferrer"><MapIcon size={14} /> {t('Open on the map')}</a>
      </div>}
    </section>
    <section className="panel route-stops-panel">
      <div className="results-heading"><div><div className="eyebrow">{t('STOPS IN ORDER')}</div><h2>{t('Where this bus stops')}</h2></div><span className="result-count">{t('{count} stops', { count: stops.length })}</span></div>
      <ol className="hp-route-stops">
        {stops.map((stop) => <li key={`${stop.order}-${stop.name}`}>
          <span className="hp-stop-no">{formatNumber((stop.order ?? 0) + 1)}</span>
          <span className="hp-stop-name">{t(stop.name)}</span>
          <a href={`https://www.openstreetmap.org/?mlat=${stop.lat}&mlon=${stop.lng}#map=17/${stop.lat}/${stop.lng}`} target="_blank" rel="noreferrer" title={t('Open on the map')}>
            <MapPin size={12} /> {coordinate(stop.lat)}, {coordinate(stop.lng)}
          </a>
        </li>)}
      </ol>
      <p className="route-note">{t('Stop lists come from public listings and the locations are approximate.')}</p>
    </section>
    <section className="panel route-reviews"><BusReviews bus={bus} user={user} token={token} signedIn={signedIn} notify={notify} openAuth={openAuth} defaultOpen /></section>
  </div>
}
