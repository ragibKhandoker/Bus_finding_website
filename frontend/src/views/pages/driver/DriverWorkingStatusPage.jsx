import React, { useState } from 'react'
import { History, Radio } from 'lucide-react'
import { t } from '../../../models/i18n'
import { WORK_STATUS_LABELS, dateTime, driverTripApi } from '../../../models/driverTrips'
import { DriverLayout, useDriverArea } from '../../components/driver/ops/DriverShell'
import { ErrorBanner, LoadingSkeleton, SectionTitle, WorkingStatusPill, WorkingStatusSelector } from '../../components/driver/ops/DriverWidgets'
import { driverRequests } from '../../../models/driverRequests'
import { useDriverData } from './useDriverData'

const BY = { driver: 'by you', system: 'automatically', admin: 'by an admin' }

// /driver/working-status: the driver's availability. On Trip is set and cleared by starting and completing trips.
export function DriverWorkingStatusPage(props) {
  return <DriverLayout page="Driver working status" title={t('Working Status')} {...props}><Body {...props} /></DriverLayout>
}

function Body({ token, notify }) {
  const [state, reload] = useDriverData(driverRequests.workingStatus(token), token)
  const { refreshStatus } = useDriverArea()
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  if (state.status === 'loading') return <LoadingSkeleton rows={3} />
  if (state.status === 'error') return <ErrorBanner error={state.error} onRetry={reload} />
  const { data: current, history } = state.data
  const choose = async (status) => {
    setBusy(true); setError('')
    try { const res = await driverTripApi.setWorkingStatus(status, reason.trim() || null, token); notify(t(res.message)); setReason(''); reload(); refreshStatus() }
    catch (e) { setError(t(e.message)) } finally { setBusy(false) }
  }
  return <div className="do-content">
    <section className="do-panel">
      <SectionTitle icon={Radio} title="Working Status" text={current.changedAt ? t('Current: since {time}', { time: dateTime(current.changedAt) }) : t('Choose your status when your duty starts.')}
        action={<WorkingStatusPill status={current.status} large />} />
      {current.hasActiveTrip && <p className="do-note">{t('A trip is in progress. Your status stays On Trip until you complete it.')}</p>}
      <WorkingStatusSelector current={current.status} allowed={current.allowedManual} busy={busy} onChoose={choose} />
      <div className="dv-field"><label htmlFor="do-reason">{t('Note')} <em>{t('(optional)')}</em></label>
        <div className="dv-control"><input id="do-reason" className="dv-input" maxLength={300} value={reason} onChange={(e) => setReason(e.target.value)} placeholder={t('e.g. Lunch break at Farmgate')} /></div></div>
      {error && <p className="dv-alert danger" role="alert">{error}</p>}
      <ul className="do-rules">
        <li>{t('Starting a trip sets On Trip automatically; completing it sets On Duty (more trips today) or Available.')}</li>
        <li>{t('You cannot start trips while Off Duty, on Break or Unavailable.')}</li>
        <li>{t('You cannot go Off Duty while a trip is in progress.')}</li>
      </ul>
    </section>
    <section className="do-panel">
      <SectionTitle icon={History} title="Status history" />
      {!history.length ? <p className="do-muted">{t('No status changes yet.')}</p>
        : <ol className="dv-timeline">{history.map((h, i) => <li key={i} className={`do-hist is-${h.newStatus.toLowerCase().replace(/_/g, '-')}`}>
          <b>{h.previousStatus ? `${t(WORK_STATUS_LABELS[h.previousStatus])} → ` : ''}{t(WORK_STATUS_LABELS[h.newStatus])}</b>
          <small>{dateTime(h.changedAt)} · {t(BY[h.changedBy] || h.changedBy)}</small>{h.reason && <p>{h.reason}</p>}</li>)}</ol>}
    </section>
  </div>
}
