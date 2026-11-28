import React, { useCallback, useEffect, useRef, useState } from 'react'
import { AlertTriangle, ArrowRight, BusFront, CheckCircle2, Clock3, History, MapPin, Play, QrCode, RotateCcw, ScanLine, ShieldCheck, Users } from 'lucide-react'
import { formatTime, t } from '../../models/i18n'
import { errorText } from '../../models/messages'
import { announceTripChange, destinationsFrom, stopsOf } from '../../models/trips'
import { NEAR_STOP_KM, currentPoint, nearestStop, qrApi, tokenFromScan } from '../../models/busQr'
import { QrScanner } from '../components/QrScanner'
import { StopSelect, money } from '../components/Trips'
import { PageHeading, SignInPrompt } from '../components/common'
import '../styles/qr-boarding.css'

// Boarding by QR code: scan the code inside the bus, check the bus and route the server found, confirm, and the
// trip starts. Opening /scan?c=<token> (a phone's camera app reading the printed code) skips the camera step.
// Steps: scan → checking → confirm → boarding → started (or problem, with a way back to scanning).
export function ScanBoardPage({ token, signedIn, openAuth, navigate, notify }) {
  const linked = useRef(typeof window === 'undefined' ? '' : new URLSearchParams(window.location.search).get('c') || '')
  // Opened from a scanned link: go straight to checking it, without turning the camera on.
  const [step, setStep] = useState(() => (linked.current ? 'checking' : 'scan'))
  const [scan, setScan] = useState(null) // the server's answer to verify-qr
  const [problem, setProblem] = useState('')
  const [boarding, setBoarding] = useState('')
  const [destination, setDestination] = useState('')
  const [formError, setFormError] = useState('')
  const [trip, setTrip] = useState(null)
  const [autoStop, setAutoStop] = useState('') // the boarding stop filled in from the location
  const point = useRef(null)
  const locating = useRef(null) // the location request started when the page opened

  // Ask for the location once, in the background: it fills in the stop you are at, and is saved with the trip.
  useEffect(() => {
    if (!signedIn) return
    locating.current = currentPoint().then((p) => { if (p) point.current = p; return p })
  }, [signedIn])
  // The location, waiting at most a few seconds for the request already under way.
  const whereAmI = () => point.current
    ? Promise.resolve(point.current)
    : Promise.race([locating.current || currentPoint(), new Promise((resolve) => setTimeout(() => resolve(null), 2500))])

  const verify = useCallback(async (text) => {
    const code = tokenFromScan(text)
    if (!code) { setProblem(t('This is not a SmartCommute bus QR code. Scan the code shown inside the bus.')); setStep('problem'); return }
    setStep('checking'); setProblem('')
    try {
      const data = await qrApi.verify(token, code, await whereAmI())
      const result = data.data
      setScan(result)
      setBoarding(result.nearestStop || '')
      setAutoStop(result.nearestStop || '')
      setDestination('')
      setFormError('')
      setStep('confirm')
    } catch (e) {
      setProblem(errorText(e))
      setStep('problem')
    }
  }, [token])

  // A link from a phone's camera app: check it straight away (after signing in), then drop it from the address bar.
  useEffect(() => {
    if (!signedIn || !linked.current) return
    const code = linked.current
    linked.current = ''
    try { window.history.replaceState(window.history.state, '', window.location.pathname) } catch { /* ignore */ }
    verify(code)
  }, [signedIn, verify])

  // A location that arrives after the scan still fills in the stop you are at, unless you already chose one.
  useEffect(() => {
    if (!scan || scan.nearestStop || !locating.current) return undefined
    let live = true
    locating.current.then((here) => {
      const near = nearestStop(scan.route.stops, here)
      if (!live || !near || near.km > NEAR_STOP_KM) return
      setBoarding((current) => {
        if (current) return current
        setAutoStop(near.name)
        return near.name
      })
    })
    return () => { live = false }
  }, [scan])

  const again = () => { setScan(null); setProblem(''); setTrip(null); setStep('scan') }

  const confirm = async (event) => {
    event.preventDefault()
    if (!boarding) { setFormError(t('Choose the stop where you are getting on.')); return }
    setStep('boarding'); setFormError('')
    try {
      const data = await qrApi.board(token, { scanTicket: scan.scanTicket, boardingStop: boarding, destinationStop: destination || null }, point.current)
      setTrip(data.data)
      setStep('started')
      notify?.(t('You boarded {bus} at {stop}. Have a good trip!', { bus: t(data.data.busName), stop: t(data.data.source) }))
      announceTripChange()
    } catch (e) {
      const message = errorText(e)
      // An expired or used scan cannot be fixed on this screen: scan again. Anything else (a stop) can.
      if (e?.status === 410 || e?.status === 404 || /scan/i.test(message)) { setProblem(message); setStep('problem') }
      else { setFormError(message); setStep('confirm') }
    }
  }

  if (!signedIn) {
    return <div className="page inner-page qrs-page">
      <PageHeading eyebrow={t('BOARD WITH A SCAN')} title={t('Scan the bus QR code.')} description={t('Scan the code inside the bus to start your trip. No need to search for the bus.')} icon={QrCode} />
      <SignInPrompt openAuth={openAuth} message={linked.current ? t('Sign in to board the bus you scanned.') : t('Sign in to scan a bus QR code and start your trip.')} />
    </div>
  }

  return <div className="page inner-page qrs-page">
    <PageHeading eyebrow={t('BOARD WITH A SCAN')} title={t('Scan the bus QR code.')} description={t('Scan the code inside the bus to start your trip. No need to search for the bus.')} icon={QrCode} />
    <ol className="qrs-steps" aria-label={t('Steps')}>
      {[['scan', t('Scan')], ['confirm', t('Confirm')], ['started', t('On board')]].map(([key, label], index) => {
        const order = { scan: 0, checking: 0, problem: 0, confirm: 1, boarding: 1, started: 2 }[step]
        return <li key={key} className={index < order ? 'done' : index === order ? 'current' : ''}><span>{index + 1}</span>{label}</li>
      })}
    </ol>

    <section className="qrs-card panel" aria-live="polite">
      {step === 'scan' && <QrScanner onResult={verify} />}

      {step === 'checking' && <div className="qrs-status">
        <span className="qrs-status-icon is-busy"><ScanLine size={26} /></span>
        <b>{t('Checking the code…')}</b>
        <span>{t('Making sure this bus is in service.')}</span>
      </div>}

      {step === 'problem' && <div className="qrs-status is-problem" role="alert">
        <span className="qrs-status-icon is-problem"><AlertTriangle size={26} /></span>
        <b>{t('This code could not be used')}</b>
        <span>{problem}</span>
        <button type="button" className="button button-green" onClick={again}><RotateCcw size={15} /> {t('Scan again')}</button>
      </div>}

      {(step === 'confirm' || step === 'boarding') && scan && <ConfirmBoarding scan={scan} boarding={boarding} autoStop={autoStop} destination={destination}
        setBoarding={(next) => { setBoarding(next); setFormError(''); if (!destinationsFrom(stopsOf(scan.route), next, scan.route.runsBothWays).includes(destination)) setDestination('') }}
        setDestination={setDestination} error={formError} saving={step === 'boarding'} onConfirm={confirm} onCancel={again} navigate={navigate} />}

      {step === 'started' && trip && <div className="qrs-status is-done">
        <span className="qrs-status-icon is-done"><CheckCircle2 size={30} /></span>
        <b>{t('Trip started!')}</b>
        <span>{t('You are on {bus}', { bus: t(trip.busName) })}{trip.busCode ? ` · ${trip.busCode}` : ''}</span>
        <div className="qrs-started-facts">
          <span><MapPin size={14} /> {t('Got on at {stop}', { stop: t(trip.source) })}</span>
          <span><Clock3 size={14} /> {t('since {time}', { time: formatTime(trip.startedAt) })}</span>
          {trip.destination && <span><ArrowRight size={14} /> {t('Getting off at')} {t(trip.destination)}</span>}
        </div>
        <p className="qrs-note">{t('When you get off, open My trips and tap End trip. Your fare is worked out and gems are added to your wallet.')}</p>
        <div className="qrs-actions">
          <button type="button" className="button button-green" onClick={() => navigate('My trips')}><History size={15} /> {t('View my trip')}</button>
          <button type="button" className="button button-outline" onClick={() => navigate('Home')}>{t('Done')}</button>
        </div>
      </div>}
    </section>
    {step === 'scan' && <p className="qrs-privacy"><ShieldCheck size={14} /> {t('The camera is only used to read the code. Nothing is recorded or uploaded.')}</p>}
  </div>
}

// "Are you boarding this bus?": the bus and route from the server, where you get on and (optionally) off.
function ConfirmBoarding({ scan, boarding, autoStop, destination, setBoarding, setDestination, error, saving, onConfirm, onCancel, navigate }) {
  const { route, vehicle, activeTrip, onThisBus } = scan
  const stops = stopsOf(route)
  const first = stops[0]
  const last = stops[stops.length - 1]
  const full = vehicle.seatsFree <= 0
  const elsewhere = activeTrip && !onThisBus

  return <form className="qrs-confirm" onSubmit={onConfirm}>
    <div className="qrs-found"><CheckCircle2 size={16} /> {t('Bus found')}</div>
    <div className="qrs-bus">
      <span className="qrs-bus-icon"><BusFront size={24} /></span>
      <div className="qrs-bus-main">
        <b className="qrs-bus-name">{t(route.busName)}</b>
        <div className="qrs-badges">
          {route.routeNumber && <span className="qrs-badge">{route.routeNumber}</span>}
          <span className="qrs-badge is-code">{vehicle.busCode}</span>
          {route.busType && <span className="qrs-badge is-soft">{t(route.busType)}</span>}
        </div>
        {route.operator && <small>{t(route.operator)}</small>}
      </div>
    </div>
    <div className="qrs-route-line"><span>{t(first)}</span><i aria-hidden="true" /><span>{t(last)}</span></div>
    <div className="qrs-facts">
      <span><small>{t('Starts from')}</small><b>{money(route.base_fare)}</b></span>
      <span><small>{t('Stops')}</small><b>{stops.length}</b></span>
      <span className={full ? 'is-warn' : ''}><small>{t('Seats free')}</small><b><Users size={14} /> {vehicle.seatsFree}/{vehicle.totalPassengerCapacity}</b></span>
    </div>

    {onThisBus ? <div className="qrs-callout is-ok">
      <b>{t('You are already on this bus.')}</b>
      <span>{t('Your trip started at {stop}. End it from My trips when you get off.', { stop: t(activeTrip.source) })}</span>
      <button type="button" className="button button-green button-small" onClick={() => navigate('My trips')}><History size={14} /> {t('Go to my trip')}</button>
    </div>
      : elsewhere ? <div className="qrs-callout is-warn" role="alert">
        <b>{t('You are already on {bus}', { bus: t(activeTrip.busName || 'a bus') })}</b>
        <span>{t('You can only be on one bus at a time. End that trip first, then scan this bus again.')}</span>
        <button type="button" className="button button-outline button-small" onClick={() => navigate('My trips')}><History size={14} /> {t('Go to my trip')}</button>
      </div>
      : <>
        <h3 className="qrs-question">{t('Are you boarding this bus?')}</h3>
        <div className="qrs-stops">
          <StopSelect label={t('Getting on at')} value={boarding} stops={stops} onChange={setBoarding} tone="start" />
          {autoStop && boarding === autoStop && <small className="qrs-near"><MapPin size={12} /> {t('Filled in from your location. Change it if it is wrong.')}</small>}
          <StopSelect label={t('Getting off at (optional)')} value={destination} stops={destinationsFrom(stops, boarding, route.runsBothWays)} onChange={setDestination} empty={t('Decide later')} tone="end" />
        </div>
        {full && <div className="qrs-callout is-warn">{t('This bus is full. Pick another bus on the route.')}</div>}
        {error && <div className="inline-error" role="alert">{error}</div>}
      </>}

    <div className="qrs-actions">
      {!onThisBus && !elsewhere && <button type="submit" className={`button button-green${saving ? ' is-loading' : ''}`} disabled={saving || full} aria-busy={saving}>
        {saving ? <><span className="button-spinner" aria-hidden="true" /> {t('Starting your trip…')}</> : <><Play size={15} /> {t('Yes, start my trip')}</>}
      </button>}
      <button type="button" className="button button-outline" onClick={onCancel} disabled={saving}><RotateCcw size={15} /> {t('No, scan again')}</button>
    </div>
  </form>
}
