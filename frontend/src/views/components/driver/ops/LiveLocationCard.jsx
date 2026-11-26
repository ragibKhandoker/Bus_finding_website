import React, { useCallback, useEffect, useState } from 'react'
import { BusFront, CircleAlert, Crosshair, Loader2, MapPin, Navigation, Radio, RefreshCw, ShieldCheck, Square, WifiOff } from 'lucide-react'
import { t } from '../../../../models/i18n'
import { ago, exactTime, liveApi, startLiveShare, stopLiveShare, subscribeLiveShare } from '../../../../models/liveLocation'
import { TRIP_STATUS_LABELS } from '../../../../models/driverTrips'

// "Live Bus Location" on the driver dashboard: the bus and trip being shared, the GPS state, the current
// coordinates and the last update the server accepted. Sharing starts only from the driver's click.
export function LiveLocationCard({ token, notify, refreshKey }) {
  const [server, setServer] = useState({ status: 'loading' })
  const [share, setShare] = useState(null)
  const [now, setNow] = useState(Date.now())
  const [busy, setBusy] = useState(false)
  const load = useCallback(() => liveApi.me(token).then((res) => setServer({ status: 'ready', data: res.data })).catch((e) => setServer({ status: 'error', error: t(e.message) })), [token])
  useEffect(() => { load() }, [load, refreshKey])
  useEffect(() => subscribeLiveShare(setShare), [])
  useEffect(() => { const id = setInterval(() => setNow(Date.now()), 1000); return () => clearInterval(id) }, [])
  // While sharing, refresh the server's view now and then (last accepted update).
  useEffect(() => {
    if (share?.status !== 'sharing') return undefined
    const id = setInterval(load, 15000)
    return () => clearInterval(id)
  }, [share?.status, load])

  const d = server.data
  const sharingHere = share?.status === 'sharing'
  const requesting = share?.status === 'requesting'
  // The server still has sharing on, but this browser is not sending (page reloaded): offer to resume.
  const pausedElsewhere = d?.isSharing && !sharingHere && !requesting

  const start = async () => {
    setBusy(true)
    const ok = await startLiveShare(token)
    setBusy(false)
    if (ok) notify(t('Live location sharing started.'))
    load()
  }
  const stop = async () => {
    setBusy(true)
    await stopLiveShare()
    setBusy(false)
    notify(t('Live location sharing stopped.'))
    load()
  }

  if (server.status === 'loading') return <section className="do-panel do-live-card"><div className="do-skeleton"><i /></div></section>
  if (server.status === 'error') return <section className="do-panel do-live-card"><p className="dv-alert danger"><CircleAlert size={16} /> {server.error}</p></section>

  const lastAt = d.lastUpdatedAt || share?.lastServerAt
  const secondsAgo = lastAt ? Math.max(0, (now - new Date(lastAt).getTime()) / 1000) : null
  const fix = share?.fix
  const state = sharingHere ? (share.offline ? 'offline' : 'on') : requesting ? 'requesting' : pausedElsewhere ? 'paused' : 'off'
  const STATE = {
    on: { icon: Radio, label: 'Sharing live', tone: 'live' },
    offline: { icon: WifiOff, label: 'Offline — retrying', tone: 'delayed' },
    requesting: { icon: Loader2, label: 'Asking for location…', tone: 'delayed' },
    paused: { icon: Radio, label: 'Not sending from this device', tone: 'delayed' },
    off: { icon: Square, label: 'Not sharing', tone: 'off' },
  }[state]
  const StateIcon = STATE.icon

  return <section className={`do-panel do-live-card is-${STATE.tone}`} aria-labelledby="do-live-title">
    <header className="do-live-head">
      <div>
        <h2 id="do-live-title"><Navigation size={18} /> {t('Live Bus Location')}</h2>
        <p>{t('Share your GPS location so commuters can follow this bus on the map.')}</p>
      </div>
      <span className={`do-gps is-${STATE.tone}`}><i aria-hidden="true" /><StateIcon size={14} className={state === 'requesting' ? 'dv-spin' : ''} /> {t(STATE.label)}</span>
    </header>

    <dl className="do-live-facts">
      <div><dt><BusFront size={13} /> {t('Assigned bus')}</dt><dd>{d.bus ? t(d.bus.busName) : '—'}</dd></div>
      <div><dt>{t('Registration')}</dt><dd className="dv-mono">{d.bus?.registrationNumber || t('Not set')}</dd></div>
      <div><dt>{t('Route')}</dt><dd>{d.trip ? `${t(d.trip.from)} → ${t(d.trip.to)}` : d.bus?.routeNumber || '—'}</dd></div>
      <div><dt>{t('Trip status')}</dt><dd>{d.trip ? `${t('Trip {n}', { n: d.trip.tripNumber })} · ${t(TRIP_STATUS_LABELS[d.trip.status])}` : t('No trip in progress')}</dd></div>
      <div><dt><Crosshair size={13} /> {t('Current position')}</dt><dd className="dv-mono">{fix ? `${fix.lat.toFixed(5)}, ${fix.lng.toFixed(5)}` : d.isSharing && d.latitude != null ? `${d.latitude.toFixed(5)}, ${d.longitude.toFixed(5)}` : '—'}
        {fix?.accuracy != null && <small> ±{fix.accuracy} m</small>}</dd></div>
      <div><dt>{t('Last update')}</dt><dd>{lastAt ? <>{exactTime(lastAt)} <small>({ago(secondsAgo)})</small></> : '—'}</dd></div>
    </dl>

    {!d.canShare && <p className="do-note"><ShieldCheck size={15} /> {t(d.cannotShareReason)}</p>}
    {share?.error && <p className="dv-alert danger" role="alert"><CircleAlert size={16} /> {share.error}</p>}
    {share?.warning && <p className="do-live-warning" role="status"><CircleAlert size={15} /> {share.warning}</p>}
    {pausedElsewhere && <p className="do-live-warning"><CircleAlert size={15} /> {t('Sharing is on, but this device is not sending its location (the page was reloaded or opened elsewhere). Resume to keep the bus live, or stop it.')}</p>}

    <div className="do-live-actions">
      {!sharingHere && <button type="button" className="button button-green" disabled={!d.canShare || busy || requesting} onClick={start}>
        {busy || requesting ? <Loader2 size={16} className="dv-spin" /> : <MapPin size={16} />} {pausedElsewhere ? t('Resume Live Location') : t('Start Live Location')}</button>}
      {(sharingHere || pausedElsewhere || requesting) && <button type="button" className="button dv-button-danger" disabled={busy} onClick={stop}><Square size={15} /> {t('Stop Live Location')}</button>}
      <button type="button" className="dv-chip" onClick={load}><RefreshCw size={13} /> {t('Refresh')}</button>
    </div>
    <p className="do-muted"><ShieldCheck size={13} /> {t('Your location is shared only while you choose to and only during a trip. Commuters see the bus, never your name or phone. Sharing stops by itself when the trip is completed.')}</p>
  </section>
}
