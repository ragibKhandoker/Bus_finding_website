import React, { useState } from 'react'
import { CalendarDays, ChevronLeft, ChevronRight } from 'lucide-react'
import { t } from '../../../models/i18n'
import { TRIP_STATUSES, TRIP_STATUS_LABELS, clock, dayLabel, dhakaDate, driverTripApi, fullDay, routeText } from '../../../models/driverTrips'
import { DriverLayout, useDriverArea } from '../../components/driver/ops/DriverShell'
import { ErrorBanner, LoadingSkeleton, SectionTitle, TodayScheduleTimeline } from '../../components/driver/ops/DriverWidgets'
import { useTripActions } from '../../components/driver/ops/TripActionModals'
import { driverRequests } from '../../../models/driverRequests'
import { useDriverData } from './useDriverData'

const shift = (date, days) => new Date(new Date(`${date}T12:00:00Z`).getTime() + days * 86_400_000).toISOString().slice(0, 10)

// /driver/schedule: one day's duty as a timeline (today by default). Earlier and later days, and a status filter.
// The schedule is set by an administrator; drivers can only start and complete their trips.
export function DriverSchedulePage(props) {
  return <DriverLayout page="Driver schedule" title={t("Today's Schedule")} {...props}><Body {...props} /></DriverLayout>
}

function Body({ token, navigate, notify }) {
  const today = dhakaDate()
  const [date, setDate] = useState(today)
  const [status, setStatus] = useState('')
  const [state, reload] = useDriverData(driverRequests.day(token, date, status), token)
  const { refreshStatus } = useDriverArea()
  const actions = useTripActions({ token, notify, onDone: () => { reload(); refreshStatus() } })
  const trips = state.data?.data || []
  // When the chosen day is empty, point to the next trip (often scheduled for another day).
  const [next] = useDriverData(driverRequests.nextTrip(token), token)
  const nextTrip = next.data && next.data.scheduleDate !== date ? next.data : null
  return <div className="do-content">
    <section className="do-panel">
      <SectionTitle icon={CalendarDays} title={date === today ? "Today's Schedule" : date < today ? 'Previous schedule' : 'Upcoming schedule'} text={fullDay(date)} />
      <div className="do-toolbar">
        <div className="do-daynav">
          <button type="button" className="do-icon" onClick={() => setDate(shift(date, -1))} aria-label={t('Previous day')}><ChevronLeft size={16} /></button>
          <input type="date" className="dv-input" value={date} onChange={(e) => e.target.value && setDate(e.target.value)} aria-label={t('Schedule date')} />
          <button type="button" className="do-icon" onClick={() => setDate(shift(date, 1))} aria-label={t('Next day')}><ChevronRight size={16} /></button>
          {date !== today && <button type="button" className="dv-chip" onClick={() => setDate(today)}>{t('Today')}</button>}
        </div>
        <label className="dv-filter"><span>{t('Status')}</span>
          <select className="dv-input" value={status} onChange={(e) => setStatus(e.target.value)}>
            <option value="">{t('All statuses')}</option>{TRIP_STATUSES.map((s) => <option key={s} value={s}>{t(TRIP_STATUS_LABELS[s])}</option>)}
          </select></label>
      </div>
      {state.status === 'loading' ? <LoadingSkeleton rows={3} /> : state.status === 'error' ? <ErrorBanner error={state.error} onRetry={reload} />
        : <TodayScheduleTimeline trips={trips} onStart={actions.onStart} onComplete={actions.onComplete} onOpen={(trip) => navigate('Driver trip', { route: trip.id })} />}
      {state.status === 'ready' && !trips.length && nextTrip && <div className="do-note">
        <span>{t('Your next trip is on {day} at {time}: {route}.', { day: dayLabel(nextTrip.scheduleDate), time: clock(nextTrip.scheduledDeparture), route: routeText(nextTrip) })}</span>
        <button type="button" className="dv-chip" onClick={() => { setStatus(''); setDate(nextTrip.scheduleDate) }}>{t('Show that day')}</button>
      </div>}
      {state.status === 'ready' && !trips.length && status && <p className="do-muted">{t('A status filter is on. Choose All statuses to see every trip of the day.')}</p>}
      <p className="do-muted">{t('Departure times, routes and duties are set by an administrator. Contact the admin if something needs to change.')}</p>
    </section>
    {actions.dialog}
  </div>
}
