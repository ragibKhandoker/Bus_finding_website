import React, { useState } from 'react'
import { BusFront, CircleAlert, Clock3, Flag, Loader2, MapPin, Play, Timer } from 'lucide-react'
import { t } from '../../../../models/i18n'
import { clock, driverTripApi, minutes, routeText } from '../../../../models/driverTrips'
import { DvModal } from '../DriverModals'
import { useDriverArea } from './DriverShell'

function TripSummary({ trip }) {
  return <dl className="do-confirm-facts">
    <div><dt><MapPin size={14} /> {t('Route')}</dt><dd>{routeText(trip)}</dd></div>
    <div><dt><BusFront size={14} /> {t('Bus')}</dt><dd>{[t(trip.busName), trip.vehicle?.code].filter(Boolean).join(' · ')}</dd></div>
    <div><dt><Clock3 size={14} /> {t('Scheduled')}</dt><dd>{clock(trip.scheduledDeparture)} – {clock(trip.scheduledArrival)}</dd></div>
    {trip.actualDeparture && <div><dt><Timer size={14} /> {t('Started')}</dt><dd>{clock(trip.actualDeparture)} · {t('{d} so far', { d: minutes(Math.max(0, (Date.now() - new Date(trip.actualDeparture)) / 60000)) })}</dd></div>}
  </dl>
}

export function TripStartConfirmationModal({ trip, busy, error, onConfirm, onClose }) {
  return <DvModal title={t('Start this trip?')} subtitle={t('Trip {n} · {code}', { n: trip.tripNumber, code: trip.code })} onClose={onClose}>
    <p className="dv-modal-text"><Play size={16} /> {t('Start only when you are at the starting stop with the bus. Your status changes to On Trip.')}</p>
    <TripSummary trip={trip} />
    {error && <p className="dv-alert danger" role="alert"><CircleAlert size={16} /> {error}</p>}
    <div className="dv-modal-actions">
      <button type="button" className="button button-outline" onClick={onClose} disabled={busy}>{t('Not yet')}</button>
      <button type="button" className="button button-green" onClick={onConfirm} disabled={busy}>{busy ? <Loader2 size={15} className="dv-spin" /> : <Play size={15} />} {t('Start Trip')}</button>
    </div>
  </DvModal>
}

export function TripCompleteConfirmationModal({ trip, busy, error, onConfirm, onClose }) {
  return <DvModal title={t('Complete this trip?')} subtitle={t('Trip {n} · {code}', { n: trip.tripNumber, code: trip.code })} onClose={onClose} tone="warn">
    <p className="dv-modal-text"><Flag size={16} /> {t('Complete the trip when you have reached {stop}. This cannot be undone.', { stop: t(trip.endStop) })}</p>
    <TripSummary trip={trip} />
    {error && <p className="dv-alert danger" role="alert"><CircleAlert size={16} /> {error}</p>}
    <div className="dv-modal-actions">
      <button type="button" className="button button-outline" onClick={onClose} disabled={busy}>{t('Cancel')}</button>
      <button type="button" className="button do-button-amber" onClick={onConfirm} disabled={busy}>{busy ? <Loader2 size={15} className="dv-spin" /> : <Flag size={15} />} {t('Complete Trip')}</button>
    </div>
  </DvModal>
}

// Start/complete with a confirmation first. Returns the handlers for buttons and the dialog to render.
// onDone(trip) runs after the server accepted the change (reload the page's data there).
export function useTripActions({ token, notify, onDone }) {
  const { refreshStatus } = useDriverArea()
  const [pending, setPending] = useState(null) // { kind, trip }
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const close = () => { if (!busy) { setPending(null); setError('') } }
  const confirm = async () => {
    setBusy(true); setError('')
    try {
      const res = pending.kind === 'start' ? await driverTripApi.start(pending.trip.id, token) : await driverTripApi.complete(pending.trip.id, token)
      notify(t(res.message))
      setPending(null)
      refreshStatus()
      onDone?.(res.data)
    } catch (e) { setError(t(e.message || 'Something went wrong. Please try again.')) } finally { setBusy(false) }
  }
  const dialog = pending?.kind === 'start'
    ? <TripStartConfirmationModal trip={pending.trip} busy={busy} error={error} onConfirm={confirm} onClose={close} />
    : pending?.kind === 'complete' ? <TripCompleteConfirmationModal trip={pending.trip} busy={busy} error={error} onConfirm={confirm} onClose={close} /> : null
  return {
    onStart: (trip) => { setError(''); setPending({ kind: 'start', trip }) },
    onComplete: (trip) => { setError(''); setPending({ kind: 'complete', trip }) },
    dialog,
  }
}
