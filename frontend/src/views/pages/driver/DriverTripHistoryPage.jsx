import React, { useEffect, useState } from 'react'
import { History, Search } from 'lucide-react'
import { t } from '../../../models/i18n'
import { TRIP_STATUS_LABELS, driverTripApi } from '../../../models/driverTrips'
import { DriverLayout } from '../../components/driver/ops/DriverShell'
import { ErrorBanner, LoadingSkeleton, Pager, SectionTitle, TripHistoryTable } from '../../components/driver/ops/DriverWidgets'
import { driverRequests } from '../../../models/driverRequests'
import { useDriverData } from './useDriverData'

// /driver/trip-history: the driver's own finished trips (completed, cancelled, missed), newest first.
export function DriverTripHistoryPage(props) {
  return <DriverLayout page="Driver trip history" title={t('Trip History')} {...props}><Body {...props} /></DriverLayout>
}

function Body({ token, navigate }) {
  const [typed, setTyped] = useState('')
  const [f, setF] = useState({ search: '', from: '', to: '', status: '', page: 1 })
  useEffect(() => {
    const timer = setTimeout(() => setF((x) => (x.search === typed.trim() ? x : { ...x, search: typed.trim(), page: 1 })), 300)
    return () => clearTimeout(timer)
  }, [typed])
  const [state, reload] = useDriverData(driverRequests.history(token, f), token)
  const change = (patch) => setF((x) => ({ ...x, ...patch, page: patch.page ?? 1 }))
  return <div className="do-content">
    <section className="do-panel">
      <SectionTitle icon={History} title="Trip History" text={state.data ? t('{n} trips', { n: state.data.total }) : ''} />
      <div className="dv-toolbar">
        <label className="dv-search"><Search size={16} aria-hidden="true" /><span className="sr-only">{t('Search')}</span>
          <input value={typed} onChange={(e) => setTyped(e.target.value)} placeholder={t('Search by bus, route or stop')} /></label>
        <label className="dv-filter"><span>{t('From')}</span><input type="date" className="dv-input" value={f.from} max={f.to || undefined} onChange={(e) => change({ from: e.target.value })} /></label>
        <label className="dv-filter"><span>{t('To')}</span><input type="date" className="dv-input" value={f.to} min={f.from || undefined} onChange={(e) => change({ to: e.target.value })} /></label>
        <label className="dv-filter"><span>{t('Status')}</span><select className="dv-input" value={f.status} onChange={(e) => change({ status: e.target.value })}>
          <option value="">{t('All')}</option>{['COMPLETED', 'CANCELLED', 'MISSED'].map((s) => <option key={s} value={s}>{t(TRIP_STATUS_LABELS[s])}</option>)}</select></label>
      </div>
      {state.status === 'loading' ? <LoadingSkeleton rows={5} /> : state.status === 'error' ? <ErrorBanner error={state.error} onRetry={reload} />
        : <TripHistoryTable trips={state.data.data} onOpen={(trip) => navigate('Driver trip', { route: trip.id })} />}
      <Pager page={f.page} totalPages={state.data?.totalPages || 1} onPage={(page) => change({ page })} />
    </section>
  </div>
}
