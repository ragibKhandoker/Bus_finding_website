import React from 'react'
import { ArrowRight, BusFront, CircleAlert, MapPin } from 'lucide-react'
import { formatNumber, t } from '../../models/i18n'
import { sortedStops } from '../helpers'

// Route and stop totals stored in the database. Shown to every visitor, signed in or not.
export function BusStatsCard({ stats, onRetry, onSeeAll }) {
  if (stats.status === 'error') {
    return <div className="hp-stats is-error" role="status">
      <CircleAlert size={18} />
      <span><b>{t('Route count unavailable')}</b><small>{t('Could not reach the bus database.')}</small></span>
      <button type="button" onClick={onRetry}>{t('Try again')}</button>
    </div>
  }
  const loading = stats.status === 'loading'
  const value = (number) => loading ? <i className="hp-stat-skeleton" aria-hidden="true" /> : formatNumber(number)
  return <div className="hp-stats" aria-busy={loading} aria-live="polite">
    <div className="hp-stat"><span className="hp-stat-icon"><BusFront size={18} /></span><span><b>{value(stats.routes)}</b><small>{t('Bus routes')}</small></span></div>
    <div className="hp-stat"><span className="hp-stat-icon"><MapPin size={18} /></span><span><b>{value(stats.stops)}</b><small>{t('Bus stops')}</small></span></div>
    {loading
      ? <small className="hp-stats-source">{t('Loading…')}</small>
      : <button type="button" className="hp-stats-source hp-stats-link" onClick={onSeeAll}>{t('See all routes and stops')} <ArrowRight size={14} /></button>}
  </div>
}

// Loading, error and empty states shared by the route lists; null once there are routes to show.
export function RoutesStatus({ state, onRetry }) {
  if (state.status === 'loading') return <div className="empty-state"><span className="spinner" /> {t('Loading bus routes…')}</div>
  if (state.status === 'error') return <div className="empty-state panel"><span className="empty-icon"><BusFront size={22} /></span><b>{t('Bus routes could not be loaded')}</b><span>{t('Could not reach the bus database.')}</span><button className="button button-outline button-small" onClick={onRetry}>{t('Try again')}</button></div>
  if (!state.buses.length) return <div className="empty-state panel"><span className="empty-icon"><BusFront size={22} /></span><b>{t('No bus routes yet')}</b></div>
  return null
}

// A route in a list: where it runs and how many stops. Opens the route's own page.
export function RouteTile({ bus, onOpen }) {
  const stops = sortedStops(bus)
  const first = stops[0]?.name, last = stops[stops.length - 1]?.name
  return <button type="button" className="hp-route hp-route-tile" onClick={() => onOpen(bus)}>
    <span className="hp-route-head">
      {bus.routeNumber && <span className="hp-route-no">{bus.routeNumber}</span>}
      <span><b>{t(bus.busName)}</b><small>{[bus.operator, bus.busType].filter(Boolean).map((text) => t(text)).join(' · ')}</small></span>
    </span>
    {first && <span className="hp-route-ends"><span><i className="start" />{t(first)}</span><ArrowRight size={14} /><span><i className="end" />{t(last)}</span></span>}
    <span className="hp-route-foot">{t('{count} stops', { count: stops.length })}<span>{t('View details')} <ArrowRight size={14} /></span></span>
  </button>
}

// Well-known routes shown on the home page; the full list has its own page.
export const FEATURED_ROUTES = ['Raida', 'Turag', 'Victor Classic', 'Projapati', 'Bihanga', 'Akash']

export const FEATURED_COUNT = 6

export function RoutePreview({ state, onRetry, navigate }) {
  const buses = state.status === 'ready' ? state.buses.filter((bus) => bus.routeNumber) : []
  const featured = FEATURED_ROUTES.map((name) => buses.find((bus) => bus.busName === name)).filter(Boolean)
  const shown = [...new Set([...featured, ...buses])].slice(0, FEATURED_COUNT)
  return <section className="hp-section">
    <div className="hp-section-head hp-head-row">
      <div>
        <span className="hp-kicker">{t('Route database')}</span>
        <h2>{t('Popular bus routes')}</h2>
        <p>{t('A few well-known routes. Open one to see every stop, the fare and where it runs.')}</p>
      </div>
      {state.status === 'ready' && <button className="button button-outline button-small" onClick={() => navigate('Bus routes')}>{t('See all {count} routes', { count: buses.length })} <ArrowRight size={14} /></button>}
    </div>
    {RoutesStatus({ state, onRetry }) || <div className="hp-routes hp-routes-3">{shown.map((bus) => <RouteTile key={bus._id} bus={bus} onOpen={() => navigate('Bus details', { route: bus.routeNumber })} />)}</div>}
  </section>
}
