import React from 'react'
import { ArrowLeft, BusFront, Clock3, MapPin, Route, Timer, UserRound } from 'lucide-react'
import { t } from '../../../models/i18n'
import { TRIP_STATUS_LABELS, clock, dateTime, driverTripApi, fullDay, minutes, routeText } from '../../../models/driverTrips'
import { DriverLayout, useDriverArea } from '../../components/driver/ops/DriverShell'
import { ErrorBanner, LoadingSkeleton, StopLine, TripActions, TripStatusBadge } from '../../components/driver/ops/DriverWidgets'
import { useTripActions } from '../../components/driver/ops/TripActionModals'
import { useDriverData } from './useDriverData'

function Facts({ items }) {
  return <dl className="dv-facts">{items.map(([label, value]) => <div key={label}><dt>{t(label)}</dt><dd>{value || '—'}</dd></div>)}</dl>
}

// /driver/trips/{id}: one of the driver's own trips. Another driver's trip is not found (the server checks).
export function DriverTripDetailsPage(props) {
  return <DriverLayout page="Driver trip" title={t('Trip details')} {...props}><Body {...props} /></DriverLayout>
}

function Body({ tripId, token, navigate, notify }) {
  const [state, reload] = useDriverData({ key: `trip:${tripId}`, load: () => driverTripApi.trip(tripId, token).then((r) => r.data) }, token)
  const { refreshStatus } = useDriverArea()
  const actions = useTripActions({ token, notify, onDone: () => { reload(); refreshStatus() } })
  const back = <button type="button" className="dv-back dark" onClick={() => navigate('Driver trips')}><ArrowLeft size={15} /> {t('All trips')}</button>
  if (state.status === 'loading') return <div className="do-content">{back}<LoadingSkeleton rows={4} /></div>
  if (state.status === 'error') return <div className="do-content">{back}<ErrorBanner error={state.error} onRetry={reload} /></div>
  const trip = state.data
  return <div className="do-content">
    {back}
    <section className={`do-trip-hero is-${trip.status.toLowerCase().replace(/_/g, '-')}`}>
      <div><small>{t('Trip {n}', { n: trip.tripNumber })} · <span className="dv-mono">{trip.code}</span> · {fullDay(trip.scheduleDate)}</small>
        <h2>{routeText(trip)}</h2><p>{[t(trip.busName), trip.routeNumber, trip.direction === 'RETURN' ? t('Return trip') : t('Outbound trip')].filter(Boolean).join(' · ')}</p></div>
      <div className="do-trip-hero-side"><TripStatusBadge status={trip.status} large /><TripActions trip={trip} onStart={actions.onStart} onComplete={actions.onComplete} /></div>
    </section>
    {trip.cancelReason && <div className={`dv-reason ${trip.status === 'CANCELLED' ? 'danger' : 'warn'}`}><b>{t(TRIP_STATUS_LABELS[trip.status])}</b><p>{trip.cancelReason}</p></div>}
    {trip.status === 'SCHEDULED' && !trip.canStart && <p className="do-note"><Clock3 size={15} /> {trip.assignmentActive ? t('You can start this trip from {time}.', { time: clock(trip.startOpensAt) }) : t('Your assignment to this bus is no longer active, so this trip cannot be started.')}</p>}
    <div className="do-two">
      <section className="do-panel"><h3 className="do-h3"><Clock3 size={17} /> {t('Times')}</h3>
        <Facts items={[['Scheduled departure', dateTime(trip.scheduledDeparture)], ['Scheduled arrival', dateTime(trip.scheduledArrival)],
          ['Actual departure', trip.actualDeparture && dateTime(trip.actualDeparture)], ['Actual arrival', trip.actualArrival && dateTime(trip.actualArrival)],
          ['Planned duration', minutes(trip.plannedMinutes)], ['Trip duration', trip.durationMinutes != null ? minutes(trip.durationMinutes) : null], ['Current status', t(TRIP_STATUS_LABELS[trip.status])]]} />
      </section>
      <div className="do-stack">
        <section className="do-panel"><h3 className="do-h3"><BusFront size={17} /> {t('Bus information')}</h3>
          <Facts items={[['Bus', t(trip.busName)], ['Route number', trip.routeNumber], ['Operator', trip.operator], ['Vehicle', trip.vehicle?.code], ['Bus type', t(trip.busType)]]} /></section>
        <section className="do-panel"><h3 className="do-h3"><UserRound size={17} /> {t('Driver')}</h3>
          <Facts items={[['Name', trip.driver?.name], ['Phone', trip.driver?.phone], ['Trip ID', <span key="id" className="dv-mono">{trip.id}</span>]]} /></section>
      </div>
    </div>
    <section className="do-panel"><h3 className="do-h3"><Route size={17} /> {t('Assigned route')} <small className="do-muted"><MapPin size={13} /> {t('{n} stops', { n: trip.stops?.length || 0 })}</small></h3>
      <StopLine stops={trip.stops || [trip.startStop, trip.endStop]} />
      {trip.notes && <p className="do-note"><Timer size={15} /> {trip.notes}</p>}</section>
    {actions.dialog}
  </div>
}
