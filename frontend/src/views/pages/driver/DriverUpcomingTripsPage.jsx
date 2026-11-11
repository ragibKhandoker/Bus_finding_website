import React, { useState } from 'react'
import { CalendarClock } from 'lucide-react'
import { t } from '../../../models/i18n'
import { dhakaDate, driverTripApi } from '../../../models/driverTrips'
import { DriverLayout, useDriverArea } from '../../components/driver/ops/DriverShell'
import { EmptyState, ErrorBanner, LoadingSkeleton, SectionTitle, UpcomingTripCard } from '../../components/driver/ops/DriverWidgets'
import { useTripActions } from '../../components/driver/ops/TripActionModals'
import { driverRequests } from '../../../models/driverRequests'
import { useDriverData } from './useDriverData'

const RANGES = [['today', 'Today'], ['tomorrow', 'Tomorrow'], ['week', 'Next 7 days'], ['custom', 'Custom range']]

// /driver/upcoming-trips: the driver's own future trips, soonest first.
export function DriverUpcomingTripsPage(props) {
  return <DriverLayout page="Driver upcoming" title={t('Upcoming Trips')} {...props}><Body {...props} /></DriverLayout>
}

function Body({ token, navigate, notify }) {
  const [range, setRange] = useState('week')
  const [from, setFrom] = useState(dhakaDate())
  const [to, setTo] = useState(dhakaDate(14))
  const params = range === 'custom' ? { from, to } : { range }
  const [state, reload] = useDriverData(driverRequests.upcoming(token, params), token)
  const { refreshStatus } = useDriverArea()
  const actions = useTripActions({ token, notify, onDone: () => { reload(); refreshStatus() } })
  const trips = state.data?.data || []
  return <div className="do-content">
    <section className="do-panel">
      <SectionTitle icon={CalendarClock} title="Upcoming Trips" text={state.data ? t('{n} trips', { n: state.data.total }) : ''} />
      <div className="do-toolbar">
        <div className="do-tabs" role="tablist">{RANGES.map(([id, label]) => <button key={id} type="button" role="tab" aria-selected={range === id} className={range === id ? 'selected' : ''} onClick={() => setRange(id)}>{t(label)}</button>)}</div>
        {range === 'custom' && <div className="do-daynav">
          <label className="dv-filter"><span>{t('From')}</span><input type="date" className="dv-input" value={from} onChange={(e) => setFrom(e.target.value)} /></label>
          <label className="dv-filter"><span>{t('To')}</span><input type="date" className="dv-input" value={to} min={from} onChange={(e) => setTo(e.target.value)} /></label>
        </div>}
      </div>
      {state.status === 'loading' ? <LoadingSkeleton rows={3} /> : state.status === 'error' ? <ErrorBanner error={state.error} onRetry={reload} />
        : !trips.length ? <EmptyState icon={CalendarClock} title="No upcoming trips in this period." text="New trips assigned by an administrator will appear here, and you will be notified." />
          : <div className="do-list">{trips.map((trip) => <UpcomingTripCard key={trip.id} trip={trip} onStart={actions.onStart} onOpen={(x) => navigate('Driver trip', { route: x.id })} />)}</div>}
    </section>
    {actions.dialog}
  </div>
}
