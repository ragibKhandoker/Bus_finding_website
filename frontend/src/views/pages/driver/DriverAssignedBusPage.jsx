import React from 'react'
import { BusFront, Lock } from 'lucide-react'
import { t } from '../../../models/i18n'
import { dateTime, driverTripApi } from '../../../models/driverTrips'
import { DriverLayout } from '../../components/driver/ops/DriverShell'
import { AssignedBusCard, EmptyState, ErrorBanner, LoadingSkeleton, SectionTitle, StopLine } from '../../components/driver/ops/DriverWidgets'
import { driverRequests } from '../../../models/driverRequests'
import { useDriverData } from './useDriverData'

// /driver/bus: the buses an administrator assigned to the driver. Read-only: only admins change assignments.
export function DriverAssignedBusPage(props) {
  return <DriverLayout page="Driver bus" title={t('My Assigned Bus')} {...props}><Body {...props} /></DriverLayout>
}

function Body({ token, navigate }) {
  const [state, reload] = useDriverData(driverRequests.bus(token), token)
  if (state.status === 'loading') return <LoadingSkeleton rows={4} />
  if (state.status === 'error') return <ErrorBanner error={state.error} onRetry={reload} />
  const active = state.data.filter((x) => x.assignment.isActive)
  const ended = state.data.filter((x) => !x.assignment.isActive)
  return <div className="do-content">
    <p className="do-note"><Lock size={15} /> {t('Bus assignments are managed by administrators. You cannot change your assigned bus or its details.')}</p>
    {!active.length && <section className="do-panel"><EmptyState icon={BusFront} title="No bus has been assigned to your account yet." text="An administrator assigns your bus. You will get a notification when they do." /></section>}
    {active.map(({ assignment: a, route, vehicle }) => <section key={a.id} className="do-panel">
      <SectionTitle icon={BusFront} title={t(a.busName)} text={a.routeNumber} />
      <AssignedBusCard assignment={a} route={route} />
      <div className="do-two">
        <dl className="dv-facts">
          {[['Bus ID', <span key="i" className="dv-mono">{a.busId}</span>], ['Bus registration number', vehicle?.code || t('Not set by the admin')], ['Bus operator', a.operator],
            ['Bus type', t(vehicle?.busType || route.busType)], ['Seating capacity', vehicle?.capacity ? t('{n} passengers', { n: vehicle.capacity }) : null],
            ['Route number', route.routeNumber], ['Route name', `${t(route.startStop)} → ${t(route.endStop)}`], ['Assignment date', dateTime(a.assignedAt)],
            ['Assignment status', a.isActive ? t('Active') : t('Ended')]].map(([label, value]) => <div key={label}><dt>{t(label)}</dt><dd>{value || '—'}</dd></div>)}
        </dl>
        <div><h3 className="do-h3">{t('Stops ({n})', { n: route.totalStops })}</h3><StopLine stops={route.stops.map((s) => s.name)} compact />
          <button type="button" className="dv-link" onClick={() => navigate('Driver routes')}>{t('Open route map')}</button></div>
      </div>
    </section>)}
    {ended.length > 0 && <section className="do-panel"><SectionTitle title="Earlier assignments" />
      <ul className="do-ended">{ended.map(({ assignment: a }) => <li key={a.id}><b>{t(a.busName)}</b><small>{a.routeNumber} · {dateTime(a.assignedAt)} – {dateTime(a.endedAt)}</small></li>)}</ul></section>}
  </div>
}
