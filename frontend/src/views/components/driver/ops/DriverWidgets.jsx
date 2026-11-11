import React from 'react'
import {
  ArrowRight, Ban, BusFront, CheckCircle2, CircleDot, Clock3, Coffee, Flag, MapPin, Moon, Navigation, OctagonAlert, Play, Route, ShieldOff, Timer, UserCheck,
} from 'lucide-react'
import { t } from '../../../../models/i18n'
import { TRIP_STATUS_LABELS, WORK_STATUS_HELP, WORK_STATUS_LABELS, clock, dayLabel, minutes, routeText } from '../../../../models/driverTrips'

// ---- Status indicators: always an icon and a word, never colour alone ----

const TRIP_ICONS = { SCHEDULED: Clock3, IN_PROGRESS: Navigation, COMPLETED: CheckCircle2, CANCELLED: Ban, MISSED: OctagonAlert }

export function TripStatusBadge({ status, large = false }) {
  const Icon = TRIP_ICONS[status] || Clock3
  return <span className={`do-trip-badge is-${String(status).toLowerCase().replace(/_/g, '-')}${large ? ' large' : ''}`}>
    <Icon size={large ? 15 : 12} aria-hidden="true" /> {t(TRIP_STATUS_LABELS[status] || status)}
  </span>
}

const WORK_ICONS = { AVAILABLE: UserCheck, ON_DUTY: CircleDot, ON_TRIP: Navigation, BREAK: Coffee, OFF_DUTY: Moon, UNAVAILABLE: ShieldOff }

export function WorkingStatusPill({ status, large = false }) {
  const Icon = WORK_ICONS[status] || Moon
  return <span className={`do-work is-${String(status).toLowerCase().replace(/_/g, '-')}${large ? ' large' : ''}`}>
    <i aria-hidden="true" /><Icon size={large ? 16 : 13} aria-hidden="true" /> {t(WORK_STATUS_LABELS[status] || status)}
  </span>
}

// The statuses a driver may pick. On Trip is shown but never selectable; nothing is selectable during a trip.
export function WorkingStatusSelector({ current, allowed = [], busy, onChoose }) {
  return <div className="do-status-grid" role="radiogroup" aria-label={t('Working status')}>
    {Object.keys(WORK_STATUS_LABELS).map((status) => {
      const Icon = WORK_ICONS[status]
      const selectable = allowed.includes(status) && status !== current
      return <button key={status} type="button" role="radio" aria-checked={current === status} disabled={!selectable || busy}
        className={`do-status-option is-${status.toLowerCase().replace(/_/g, '-')}${current === status ? ' selected' : ''}`} onClick={() => onChoose(status)}>
        <span className="do-status-icon"><Icon size={18} /></span>
        <b>{t(WORK_STATUS_LABELS[status])}</b>
        <small>{t(WORK_STATUS_HELP[status])}</small>
        {status === 'ON_TRIP' && <em>{t('Automatic')}</em>}
      </button>
    })}
  </div>
}

// ---- Layout pieces ----

export function EmptyState({ icon: Icon = Route, title, text, action }) {
  return <div className="do-empty"><span><Icon size={24} /></span><b>{t(title)}</b>{text && <small>{t(text)}</small>}{action}</div>
}

export function LoadingSkeleton({ rows = 3, cards = false }) {
  return <div className={`do-skeleton${cards ? ' cards' : ''}`} aria-busy="true" aria-label={t('Loading…')}>
    {Array.from({ length: rows }, (_, i) => <i key={i} />)}
  </div>
}

export function ErrorBanner({ error, onRetry }) {
  return <div className="dv-alert danger" role="alert"><OctagonAlert size={17} /><span>{error}</span>
    {onRetry && <button type="button" className="dv-chip" onClick={onRetry}>{t('Try again')}</button>}</div>
}

export function SectionTitle({ icon: Icon, title, text, action }) {
  return <div className="do-section-title">
    <div><h2>{Icon && <Icon size={18} />} {t(title)}</h2>{text && <p>{t(text)}</p>}</div>
    {action}
  </div>
}

export function DashboardSummaryCard({ icon: Icon, tone = 'green', label, value, sub, children, action, onAction }) {
  return <section className={`do-card tone-${tone}`}>
    <header><span className="do-card-icon"><Icon size={19} /></span><small>{t(label)}</small></header>
    {value !== undefined && <b className="do-card-value">{value}</b>}
    {sub && <p className="do-card-sub">{sub}</p>}
    {children}
    {action && <button type="button" className="do-card-action" onClick={onAction}>{t(action)} <ArrowRight size={14} /></button>}
  </section>
}

// ---- Bus and route ----

export function AssignedBusCard({ assignment, route, onDetails, compact = false }) {
  if (!assignment) return <EmptyState icon={BusFront} title="No bus has been assigned to your account yet." text="An administrator assigns your bus. You will get a notification when they do." />
  return <div className={`do-bus${compact ? ' compact' : ''}`}>
    <div className="do-bus-plate"><BusFront size={compact ? 20 : 26} /><span>{assignment.vehicleCode || t('Any vehicle')}</span></div>
    <div className="do-bus-info">
      <b>{t(assignment.busName)}</b>
      <small>{[assignment.routeNumber, assignment.operator, t(assignment.busType || route?.busType || '')].filter(Boolean).join(' · ')}</small>
      {route && <span className="do-bus-route"><MapPin size={13} /> {t(route.startStop)} → {t(route.endStop)}</span>}
      <span className={`do-assign-state ${assignment.isActive ? 'on' : 'off'}`}>{assignment.isActive ? t('Active assignment') : t('Assignment ended')}</span>
    </div>
    {onDetails && <button type="button" className="button button-outline button-small" onClick={onDetails}>{t('View Bus Details')} <ArrowRight size={14} /></button>}
  </div>
}

export function RouteInformationCard({ route, open, onToggle, children }) {
  return <article className={`do-route${open ? ' open' : ''}`}>
    <button type="button" className="do-route-head" onClick={onToggle} aria-expanded={open}>
      <span className="do-route-num">{route.routeNumber || '—'}</span>
      <span className="do-route-name"><b>{t(route.busName)}</b><small>{t(route.startStop)} → {t(route.endStop)}</small></span>
      <span className="do-route-facts"><em>{t('{n} stops', { n: route.totalStops })}</em>{route.distanceKm > 0 && <em>{t('≈ {km} km', { km: route.distanceKm })}</em>}</span>
    </button>
    {open && <div className="do-route-body">{children}</div>}
  </article>
}

export function StopLine({ stops, compact = false }) {
  return <ol className={`do-stops${compact ? ' compact' : ''}`}>
    {stops.map((name, i) => <li key={`${name}-${i}`} className={i === 0 ? 'first' : i === stops.length - 1 ? 'last' : ''}><i aria-hidden="true" /><span>{t(name)}</span></li>)}
  </ol>
}

// ---- Trips ----

export function TripActions({ trip, onStart, onComplete, onOpen }) {
  return <span className="do-trip-actions">
    {trip.canStart && <button type="button" className="button button-green button-small" onClick={() => onStart(trip)}><Play size={14} /> {t('Start Trip')}</button>}
    {trip.canComplete && <button type="button" className="button do-button-amber button-small" onClick={() => onComplete(trip)}><Flag size={14} /> {t('Complete Trip')}</button>}
    {onOpen && <button type="button" className="button button-outline button-small" onClick={() => onOpen(trip)}>{t('Details')}</button>}
  </span>
}

// A day's trips as a vertical timeline, earliest first.
export function TodayScheduleTimeline({ trips, onStart, onComplete, onOpen }) {
  if (!trips.length) return <EmptyState icon={Clock3} title="No trips scheduled for this day." text="Your duty schedule is set by an administrator." />
  return <ol className="do-timeline">
    {trips.map((trip) => <li key={trip.id} className={`is-${trip.status.toLowerCase().replace(/_/g, '-')}`}>
      <div className="do-timeline-time"><b>{clock(trip.scheduledDeparture)}</b><small>{clock(trip.scheduledArrival)}</small></div>
      <span className="do-timeline-dot" aria-hidden="true" />
      <div className="do-timeline-card">
        <header>
          <span><small>{t('Trip {n}', { n: trip.tripNumber })} · <span className="dv-mono">{trip.code}</span></small><b>{routeText(trip)}</b></span>
          <TripStatusBadge status={trip.status} />
        </header>
        <p>{[t(trip.busName), trip.routeNumber, trip.vehicle?.code].filter(Boolean).join(' · ')}</p>
        <p className="do-muted"><Timer size={13} /> {t('Planned {d}', { d: minutes(trip.plannedMinutes) })}
          {trip.actualDeparture && <> · {t('Started {time}', { time: clock(trip.actualDeparture) })}</>}
          {trip.actualArrival && <> · {t('Arrived {time}', { time: clock(trip.actualArrival) })}</>}</p>
        {trip.status === 'SCHEDULED' && !trip.canStart && trip.assignmentActive && <p className="do-muted">{t('Start opens at {time}', { time: clock(trip.startOpensAt) })}</p>}
        {!trip.assignmentActive && trip.status === 'SCHEDULED' && <p className="do-warn">{t('Your assignment to this bus is no longer active.')}</p>}
        <TripActions trip={trip} onStart={onStart} onComplete={onComplete} onOpen={onOpen} />
      </div>
    </li>)}
  </ol>
}

export function UpcomingTripCard({ trip, onOpen, onStart }) {
  return <article className="do-upcoming">
    <div className="do-upcoming-date"><small>{dayLabel(trip.scheduleDate)}</small><b>{clock(trip.scheduledDeparture)}</b><em>{t('arr. {time}', { time: clock(trip.scheduledArrival) })}</em></div>
    <div className="do-upcoming-body">
      <small>{t('Trip {n}', { n: trip.tripNumber })} · <span className="dv-mono">{trip.code}</span></small>
      <b>{routeText(trip)}</b>
      <span>{[t(trip.busName), trip.routeNumber, trip.vehicle?.code].filter(Boolean).join(' · ')}</span>
    </div>
    <div className="do-upcoming-side"><TripStatusBadge status={trip.status} />
      <span className="do-trip-actions">{trip.canStart && onStart && <button type="button" className="button button-green button-small" onClick={() => onStart(trip)}><Play size={14} /> {t('Start')}</button>}
        <button type="button" className="button button-outline button-small" onClick={() => onOpen(trip)}>{t('View Details')}</button></span>
    </div>
  </article>
}

export function TripHistoryTable({ trips, onOpen }) {
  if (!trips.length) return <EmptyState icon={CheckCircle2} title="No trips found." text="Completed, cancelled and missed trips appear here." />
  return <div className="do-table" role="table" aria-label={t('Trip history')}>
    <div className="do-tr do-thead" role="row">{['Date', 'Bus', 'Route', 'Scheduled', 'Actual', 'Duration', 'Status', ''].map((h, i) =>
      <span key={i} role="columnheader">{h ? t(h) : <span className="sr-only">{t('Action')}</span>}</span>)}</div>
    {trips.map((trip) => <div className="do-tr" role="row" key={trip.id}>
      <span role="cell" data-label={t('Date')}><b>{dayLabel(trip.scheduleDate)}</b><small>{t('Trip {n}', { n: trip.tripNumber })}</small></span>
      <span role="cell" data-label={t('Bus')}><b>{t(trip.busName)}</b><small>{trip.vehicle?.code || trip.routeNumber}</small></span>
      <span role="cell" data-label={t('Route')}>{routeText(trip)}</span>
      <span role="cell" data-label={t('Scheduled')}>{clock(trip.scheduledDeparture)}</span>
      <span role="cell" data-label={t('Actual')}>{trip.actualDeparture ? `${clock(trip.actualDeparture)} – ${clock(trip.actualArrival)}` : '—'}</span>
      <span role="cell" data-label={t('Duration')}>{minutes(trip.durationMinutes)}</span>
      <span role="cell" data-label={t('Status')}><TripStatusBadge status={trip.status} /></span>
      <span role="cell" className="do-td-action"><button type="button" className="button button-outline button-small" onClick={() => onOpen(trip)}>{t('Details')}</button></span>
    </div>)}
  </div>
}

export function Pager({ page, totalPages, onPage }) {
  if (totalPages <= 1) return null
  return <nav className="dv-pager" aria-label={t('Pages')}>
    <button type="button" className="dv-chip" disabled={page <= 1} onClick={() => onPage(page - 1)}>{t('Previous')}</button>
    <span>{t('Page {p} of {n}', { p: page, n: totalPages })}</span>
    <button type="button" className="dv-chip" disabled={page >= totalPages} onClick={() => onPage(page + 1)}>{t('Next')}</button>
  </nav>
}
