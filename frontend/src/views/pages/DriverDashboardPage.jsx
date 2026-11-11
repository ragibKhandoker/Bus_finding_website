import React from 'react'
import { BusFront, CalendarCheck2, CalendarClock, CalendarDays, Flag, Map, Navigation, Radio, Route, Trophy } from 'lucide-react'
import { formatNumber, t } from '../../models/i18n'
import { clock, dayLabel, driverTripApi, routeText, until } from '../../models/driverTrips'
import { DriverLayout, useDriverArea } from '../components/driver/ops/DriverShell'
import {
  AssignedBusCard, DashboardSummaryCard, ErrorBanner, LoadingSkeleton, SectionTitle, TodayScheduleTimeline, TripStatusBadge, UpcomingTripCard, WorkingStatusPill,
} from '../components/driver/ops/DriverWidgets'
import { useTripActions } from '../components/driver/ops/TripActionModals'
import { driverRequests } from '../../models/driverRequests'
import { useDriverData } from './driver/useDriverData'

// /driver/dashboard: everything a verified driver needs at a glance, from the database. Pending or rejected
// drivers see their verification status instead (DriverLayout).
export function DriverDashboardPage(props) {
  return <DriverLayout page="Driver dashboard" {...props}><DashboardBody {...props} /></DriverLayout>
}

function DashboardBody({ token, navigate, notify }) {
  const [state, reload] = useDriverData(driverRequests.dashboard(token), token)
  const { refreshStatus } = useDriverArea()
  const actions = useTripActions({ token, notify, onDone: () => { reload(); refreshStatus() } })
  if (state.status === 'loading') return <LoadingSkeleton rows={6} cards />
  if (state.status === 'error') return <ErrorBanner error={state.error} onRetry={reload} />
  const d = state.data
  const main = d.assignments[0]
  const next = d.upcoming.trips[0]
  const open = (trip) => navigate('Driver trip', { route: trip.id })
  const s = d.schedule

  return <div className="do-content">
    {d.activeTrip && <section className="do-live" aria-live="polite">
      <span className="do-live-pulse"><Navigation size={20} /></span>
      <div><small>{t('Trip in progress')} · {t('Trip {n}', { n: d.activeTrip.tripNumber })}</small><b>{routeText(d.activeTrip)}</b>
        <span>{t('Started {time}', { time: clock(d.activeTrip.actualDeparture) })} · {t('expected arrival {time}', { time: clock(d.activeTrip.scheduledArrival) })}</span></div>
      <button type="button" className="button do-button-amber" onClick={() => actions.onComplete(d.activeTrip)}><Flag size={16} /> {t('Complete Trip')}</button>
    </section>}

    <div className="do-cards">
      <DashboardSummaryCard icon={BusFront} label="Assigned Bus" action={main ? 'View Bus Details' : undefined} onAction={() => navigate('Driver bus')}>
        {main ? <div className="do-card-bus">
          <b>{t(main.assignment.busName)}</b>
          <span className="do-plate">{main.assignment.vehicleCode || t('Vehicle not set')}</span>
          <small>{[main.assignment.operator, t(main.assignment.busType || main.route.busType)].filter(Boolean).join(' · ')}</small>
          <small>{main.route.routeNumber} · {t(main.route.startStop)} → {t(main.route.endStop)}</small>
          <span className="do-assign-state on">{t('Active assignment')}</span>
        </div> : <p className="do-card-sub">{t('No bus has been assigned to your account yet.')}</p>}
      </DashboardSummaryCard>

      <DashboardSummaryCard icon={CalendarDays} tone="blue" label="Today's Schedule" value={formatNumber(s.total)} action="View Full Schedule" onAction={() => navigate('Driver schedule')}
        sub={s.total ? t('{done} completed · {left} remaining', { done: s.completed, left: s.remaining }) : t('No trips scheduled today.')}>
        {s.total > 0 && <div className="do-card-split"><span><small>{t('First duty')}</small><b>{clock(s.firstDeparture)}</b></span><span><small>{t('Last trip')}</small><b>{clock(s.lastDeparture)}</b></span></div>}
        {s.total > 0 && <div className="do-progress" aria-label={t('{done} of {total} trips done', { done: s.completed, total: s.total })}><i style={{ width: `${(s.completed / s.total) * 100}%` }} /></div>}
      </DashboardSummaryCard>

      <DashboardSummaryCard icon={Trophy} tone="amber" label="Total Completed Trips" value={formatNumber(d.completed.total)} action="View Trip History" onAction={() => navigate('Driver trip history')}
        sub={d.completed.completionRate == null ? t('Completion rate appears after your first finished trip.') : t('{rate}% completion rate', { rate: d.completed.completionRate })}>
        <div className="do-card-split"><span><small>{t('Last 7 days')}</small><b>{formatNumber(d.completed.thisWeek)}</b></span><span><small>{t('This month')}</small><b>{formatNumber(d.completed.thisMonth)}</b></span></div>
      </DashboardSummaryCard>

      <DashboardSummaryCard icon={CalendarClock} tone="violet" label="Upcoming Trips" value={next ? clock(next.scheduledDeparture) : undefined} action="View Upcoming Trips" onAction={() => navigate('Driver upcoming')}
        sub={next ? `${dayLabel(next.scheduleDate)} · ${until(next.scheduledDeparture)}` : t('No upcoming trips.')}>
        {next && <div className="do-card-next"><b>{routeText(next)}</b><small>{[t(next.busName), next.vehicle?.code].filter(Boolean).join(' · ')}</small><TripStatusBadge status={next.status} /></div>}
        {d.upcoming.total > 1 && <small className="do-muted">{t('{n} more scheduled', { n: d.upcoming.total - 1 })}</small>}
      </DashboardSummaryCard>

      <DashboardSummaryCard icon={Map} tone="teal" label="Assigned Routes" value={formatNumber(d.routes.length)} action={d.routes.length ? 'View Route Details' : undefined} onAction={() => navigate('Driver routes')}>
        {d.routes.slice(0, 2).map((r) => <div key={r.id} className="do-card-route"><span className="do-route-num small">{r.routeNumber}</span>
          <span><b>{t(r.startStop)} → {t(r.endStop)}</b><small>{t('{n} stops', { n: r.totalStops })}{r.distanceKm ? ` · ≈ ${r.distanceKm} km` : ''}</small></span></div>)}
        {!d.routes.length && <p className="do-card-sub">{t('Routes appear once a bus is assigned.')}</p>}
      </DashboardSummaryCard>

      <DashboardSummaryCard icon={Radio} tone="slate" label="Working Status" action="Change Status" onAction={() => navigate('Driver working status')}>
        <WorkingStatusPill status={d.workingStatus.status} large />
        <p className="do-card-sub">{d.workingStatus.changedAt ? t('Since {time}', { time: clock(d.workingStatus.changedAt) }) : t('Set your status when your duty starts.')}</p>
        {['OFF_DUTY', 'BREAK', 'UNAVAILABLE'].includes(d.workingStatus.status) && s.remaining > 0 && <p className="do-warn">{t('Set Available or On Duty to start your trips.')}</p>}
      </DashboardSummaryCard>
    </div>

    <div className="do-two">
      <section className="do-panel">
        <SectionTitle icon={CalendarCheck2} title="Today's duty" text={t('{n} trips · times in Dhaka time', { n: s.total })}
          action={<button type="button" className="dv-link" onClick={() => navigate('Driver schedule')}>{t('Full schedule')}</button>} />
        <TodayScheduleTimeline trips={s.trips} onStart={actions.onStart} onComplete={actions.onComplete} onOpen={open} />
      </section>
      <div className="do-stack">
        <section className="do-panel">
          <SectionTitle icon={BusFront} title="My Assigned Bus" />
          <AssignedBusCard assignment={main?.assignment} route={main?.route} compact onDetails={main ? () => navigate('Driver bus') : undefined} />
        </section>
        <section className="do-panel">
          <SectionTitle icon={Route} title="Next trips" action={d.upcoming.total > 0 && <button type="button" className="dv-link" onClick={() => navigate('Driver upcoming')}>{t('See all')}</button>} />
          {d.upcoming.trips.length ? <div className="do-list">{d.upcoming.trips.slice(0, 3).map((trip) => <UpcomingTripCard key={trip.id} trip={trip} onOpen={open} onStart={actions.onStart} />)}</div>
            : <p className="do-muted">{t('No upcoming trips.')}</p>}
          {d.completed.total > 0 && <p className="do-muted">{t('Total time driven appears in Performance & Statistics.')} <button type="button" className="dv-link" onClick={() => navigate('Driver statistics')}>{t('Open')}</button></p>}
        </section>
      </div>
    </div>
    {actions.dialog}
  </div>
}
