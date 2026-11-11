import React, { useState } from 'react'
import { Flag, ListChecks, Navigation } from 'lucide-react'
import { t } from '../../../models/i18n'
import { clock, dhakaDate, driverTripApi, routeText } from '../../../models/driverTrips'
import { DriverLayout, useDriverArea } from '../../components/driver/ops/DriverShell'
import { EmptyState, ErrorBanner, LoadingSkeleton, Pager, SectionTitle, TripActions, TripStatusBadge } from '../../components/driver/ops/DriverWidgets'
import { useTripActions } from '../../components/driver/ops/TripActionModals'
import { driverRequests } from '../../../models/driverRequests'
import { useDriverData } from './useDriverData'

const TABS = [
  { id: 'today', label: 'Today', params: () => ({ date: dhakaDate(), sort: 'oldest' }) },
  { id: 'active', label: 'In progress', params: () => ({ status: 'IN_PROGRESS' }) },
  { id: 'scheduled', label: 'Scheduled', params: () => ({ status: 'SCHEDULED', sort: 'oldest' }) },
  { id: 'all', label: 'All trips', params: () => ({}) },
]

// /driver/trips: the trips to work on, with Start and Complete (each confirmed first).
export function DriverTripsPage(props) {
  return <DriverLayout page="Driver trips" title={t('Manage Trips')} {...props}><Body {...props} /></DriverLayout>
}

function Body({ token, navigate, notify }) {
  const [tab, setTab] = useState('today')
  const [page, setPage] = useState(1)
  const params = TABS.find((x) => x.id === tab).params()
  const [state, reload] = useDriverData(driverRequests.trips(token, tab, params, page), token)
  const [active, reloadActive] = useDriverData(driverRequests.activeTrip(token), token)
  const { refreshStatus } = useDriverArea()
  const actions = useTripActions({ token, notify, onDone: () => { reload(); reloadActive(); refreshStatus() } })
  const open = (trip) => navigate('Driver trip', { route: trip.id })
  const current = active.data?.data?.[0]
  const rows = state.data?.data || []
  return <div className="do-content">
    {current && <section className="do-live">
      <span className="do-live-pulse"><Navigation size={20} /></span>
      <div><small>{t('Trip in progress')}</small><b>{routeText(current)}</b><span>{t('Started {time}', { time: clock(current.actualDeparture) })}</span></div>
      <button type="button" className="button do-button-amber" onClick={() => actions.onComplete(current)}><Flag size={16} /> {t('Complete Trip')}</button>
    </section>}
    <section className="do-panel">
      <SectionTitle icon={ListChecks} title="Manage Trips" text="Start a trip at the first stop and complete it at the last one. Only one trip can be in progress." />
      <div className="do-tabs" role="tablist">{TABS.map((x) => <button key={x.id} type="button" role="tab" aria-selected={tab === x.id} className={tab === x.id ? 'selected' : ''}
        onClick={() => { setTab(x.id); setPage(1) }}>{t(x.label)}</button>)}</div>
      {state.status === 'loading' ? <LoadingSkeleton rows={4} /> : state.status === 'error' ? <ErrorBanner error={state.error} onRetry={reload} />
        : !rows.length ? <EmptyState icon={ListChecks} title="No trips here." text="Trips assigned to you by an administrator appear in this list." />
          : <div className="do-list">{rows.map((trip) => <article key={trip.id} className={`do-trip-row is-${trip.status.toLowerCase().replace(/_/g, '-')}`}>
            <div className="do-trip-when"><b>{clock(trip.scheduledDeparture)}</b><small>{trip.scheduleDate}</small></div>
            <div className="do-trip-main"><small>{t('Trip {n}', { n: trip.tripNumber })} · <span className="dv-mono">{trip.code}</span></small><b>{routeText(trip)}</b>
              <span>{[t(trip.busName), trip.routeNumber, trip.vehicle?.code].filter(Boolean).join(' · ')}</span>
              {trip.status === 'SCHEDULED' && !trip.canStart && <small className="do-muted">{trip.assignmentActive ? t('Start opens at {time}', { time: clock(trip.startOpensAt) }) : t('Your assignment to this bus is no longer active.')}</small>}</div>
            <div className="do-trip-side"><TripStatusBadge status={trip.status} /><TripActions trip={trip} onStart={actions.onStart} onComplete={actions.onComplete} onOpen={open} /></div>
          </article>)}</div>}
      <Pager page={page} totalPages={state.data?.totalPages || 1} onPage={setPage} />
    </section>
    {actions.dialog}
  </div>
}
