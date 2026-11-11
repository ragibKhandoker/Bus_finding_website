import React, { Suspense, lazy, useState } from 'react'
import { Map as MapIcon } from 'lucide-react'
import { t } from '../../../models/i18n'
import { driverTripApi } from '../../../models/driverTrips'
import { DriverLayout } from '../../components/driver/ops/DriverShell'
import { EmptyState, ErrorBanner, LoadingSkeleton, RouteInformationCard, SectionTitle, StopLine } from '../../components/driver/ops/DriverWidgets'
import { driverRequests } from '../../../models/driverRequests'
import { useDriverData } from './useDriverData'

const RouteMap = lazy(() => import('../../components/driver/ops/RouteMap'))

// /driver/routes: the Dhaka routes of the driver's active assignments and scheduled trips, from the existing route
// database, each with its stops and map. Read-only.
export function DriverAssignedRoutesPage(props) {
  return <DriverLayout page="Driver routes" title={t('Assigned Routes')} {...props}><Body {...props} /></DriverLayout>
}

function Body({ token }) {
  const [state, reload] = useDriverData(driverRequests.routes(token), token)
  const [open, setOpen] = useState(null)
  if (state.status === 'loading') return <LoadingSkeleton rows={3} />
  if (state.status === 'error') return <ErrorBanner error={state.error} onRetry={reload} />
  const routes = state.data
  const shown = open ?? routes[0]?.id
  return <div className="do-content">
    <section className="do-panel">
      <SectionTitle icon={MapIcon} title="Assigned Routes" text="Official routes cannot be changed by drivers." />
      {!routes.length ? <EmptyState icon={MapIcon} title="No routes yet." text="Routes appear once an administrator assigns you a bus or schedules your trips." />
        : <div className="do-list">{routes.map((r) => <RouteInformationCard key={r.id} route={r} open={shown === r.id} onToggle={() => setOpen(shown === r.id ? '' : r.id)}>
          <div className="do-route-grid">
            <div>
              <dl className="dv-facts">{[['Route name', t(r.busName)], ['Route number', r.routeNumber], ['Operator', r.operator], ['Starting stop', t(r.startStop)], ['Ending stop', t(r.endStop)],
                ['Total stops', r.totalStops], ['Route distance', r.distanceKm ? t('≈ {km} km (straight lines between stops)', { km: r.distanceKm }) : null],
                ['Runs', r.runsBothWays ? t('Both directions') : t('One way')], ['Frequency', r.frequency]].map(([label, value]) => <div key={label}><dt>{t(label)}</dt><dd>{value || '—'}</dd></div>)}</dl>
              <h3 className="do-h3">{t('All stops')}</h3>
              <StopLine stops={r.stops.map((s) => s.name)} compact />
            </div>
            <Suspense fallback={<LoadingSkeleton rows={1} />}><RouteMap stops={r.stops} /></Suspense>
          </div>
        </RouteInformationCard>)}</div>}
    </section>
  </div>
}
