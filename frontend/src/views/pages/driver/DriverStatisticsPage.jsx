import React from 'react'
import { Ban, BarChart3, CheckCircle2, ClipboardList, Gauge, OctagonAlert, Timer, TrendingUp } from 'lucide-react'
import { formatNumber, t } from '../../../models/i18n'
import { dayLabel, driverTripApi, minutes } from '../../../models/driverTrips'
import { DriverLayout } from '../../components/driver/ops/DriverShell'
import { ErrorBanner, LoadingSkeleton, SectionTitle } from '../../components/driver/ops/DriverWidgets'
import { DriverStatisticsChart, StatusDistribution } from '../../components/driver/ops/DriverStatisticsChart'
import { driverRequests } from '../../../models/driverRequests'
import { useDriverData } from './useDriverData'

// /driver/statistics: the driver's own performance, from their trip records (each trip counted once).
export function DriverStatisticsPage(props) {
  return <DriverLayout page="Driver statistics" title={t('Performance & Statistics')} {...props}><Body {...props} /></DriverLayout>
}

function Body({ token }) {
  const [state, reload] = useDriverData(driverRequests.stats(token), token)
  if (state.status === 'loading') return <LoadingSkeleton rows={4} cards />
  if (state.status === 'error') return <ErrorBanner error={state.error} onRetry={reload} />
  const s = state.data
  const tiles = [
    [ClipboardList, 'Total assigned trips', formatNumber(s.totalAssigned), 'slate'],
    [CheckCircle2, 'Completed trips', formatNumber(s.completed), 'green'],
    [Ban, 'Cancelled trips', formatNumber(s.cancelled), 'slate'],
    [OctagonAlert, 'Missed trips', formatNumber(s.missed), 'red'],
    [Gauge, 'Completion rate', s.completionRate == null ? '—' : `${formatNumber(s.completionRate)}%`, 'blue'],
    [Timer, 'Total driving time', minutes(s.totalDrivingMinutes), 'amber'],
    [TrendingUp, 'Trips in the last 7 days', formatNumber(s.tripsThisWeek), 'teal'],
    [BarChart3, 'Trips this month', formatNumber(s.tripsThisMonth), 'violet'],
  ]
  const detail = (r) => <>{t('{n} completed', { n: r.completed })}{r.missed ? ` · ${t('{n} missed', { n: r.missed })}` : ''}{r.cancelled ? ` · ${t('{n} cancelled', { n: r.cancelled })}` : ''}</>
  return <div className="do-content">
    <div className="do-tiles">{tiles.map(([Icon, label, value, tone]) => <div key={label} className={`do-tile tone-${tone}`}><span><Icon size={18} /></span><div><small>{t(label)}</small><b>{value}</b></div></div>)}</div>
    <p className="do-muted">{t('Completion rate = completed ÷ (completed + missed). Cancelled trips are not counted against you.')}</p>
    <div className="do-two">
      <section className="do-panel"><SectionTitle icon={BarChart3} title="Completed trips by day" text="Last 7 days" />
        <DriverStatisticsChart title="Completed trips per day" rows={s.daily} labelOf={(r, short) => (short ? dayLabel(r.date).split(',')[0] : dayLabel(r.date))} detailOf={detail} /></section>
      <section className="do-panel"><SectionTitle icon={ClipboardList} title="Trip status distribution" text="All your trips" />
        <StatusDistribution counts={{ COMPLETED: s.completed, MISSED: s.missed, CANCELLED: s.cancelled, SCHEDULED: s.scheduled, IN_PROGRESS: s.inProgress }} /></section>
    </div>
    <div className="do-two">
      <section className="do-panel"><SectionTitle icon={TrendingUp} title="Weekly trip performance" text="Completed trips, last 8 weeks" />
        <DriverStatisticsChart title="Completed trips per week" rows={s.weekly} labelOf={(r, short) => (short ? r.label.split(' – ')[1] : r.label)}
          detailOf={(r) => <>{detail(r)} · {minutes(r.drivingMinutes)}</>} /></section>
      <section className="do-panel"><SectionTitle icon={BarChart3} title="Monthly trip performance" text="Completed trips, last 6 months" />
        <DriverStatisticsChart title="Completed trips per month" rows={s.monthly} labelOf={(r, short) => (short ? r.label.split(' ')[0] : r.label)}
          detailOf={(r) => <>{detail(r)} · {minutes(r.drivingMinutes)}</>} /></section>
    </div>
  </div>
}
