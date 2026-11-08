import React, { useEffect, useState } from 'react'
import { ArrowRight, BusFront, CheckCircle2, ChevronDown, Clock3, Gem, Lock, MapPin, Play, Receipt, Route, Square, Timer, X } from 'lucide-react'
import { apiRequest as request } from '../../models/api'
import { formatDate, formatNumber, formatTime, t } from '../../models/i18n'
import { errorText } from '../../models/messages'
import { announceTripChange, destinationsFrom, stopsOf } from '../../models/trips'
import { formatClock, useActiveTrip, useElapsed } from '../../controllers/useTrips'
import { SelectMenu } from './AuthModal'

export { errorText } from '../../models/messages'
export { TRIP_CHANGED_EVENT } from '../../models/trips'
export { announceTripChange } from '../../models/trips'
export { stopsOf } from '../../models/trips'
export { destinationsFrom } from '../../models/trips'
export { useTripChanges } from '../../controllers/useTrips'
export { useActiveTrip } from '../../controllers/useTrips'

// Trip boarding (F05): board a bus from a search result, end the trip at a stop, earn gems.
// The server owns all the rules (one active trip per rider, fare, gems once per trip); these components only ask.

export const money = (value) => `৳${formatNumber(value)}`
// The "You are on a bus" panel, which other cards scroll to.
export const ACTIVE_TRIP_ID = 'active-trip'

// A labelled stop picker, styled to match the rest of the app instead of the browser's own drop-down list.
// empty: the label of the "nothing chosen" first option. tone: 'start' or 'end' colours the pin.
let stopSelectCount = 0
export function StopSelect({ label, value, stops, onChange, empty = t('Choose a stop'), tone = 'start' }) {
  const [id] = useState(() => `stop-select-${++stopSelectCount}`)
  const options = [{ value: '', label: empty }, ...stops.map((stop) => ({ value: stop, label: t(stop) }))]
  return <div className={`trip-select stop-select is-${tone}${value ? ' has-value' : ''}`}>
    <small id={id}>{label}</small>
    <SelectMenu labelledBy={id} value={value} onChange={onChange} options={options} icon={MapPin} />
  </div>
}

// The trip in progress, with where to get off and an End trip button; after ending, the fare and gems earned.
export function ActiveTripPanel({ token, signedIn, notify, onCompleted }) {
  const { status, trip, route, clockOffset } = useActiveTrip(token, signedIn)
  const elapsed = useElapsed(trip?.startedAt, clockOffset)
  const [destination, setDestination] = useState('')
  const [ending, setEnding] = useState(false)
  const [error, setError] = useState('')
  const [finished, setFinished] = useState(null) // { trip, wallet } of the trip just completed

  const stops = stopsOf(route)
  const choices = trip ? destinationsFrom(stops, trip.source, route?.runsBothWays) : []
  useEffect(() => { setDestination(trip?.destination || ''); setError('') }, [trip?._id, trip?.destination])

  const end = async () => {
    if (!destination) { setError(t('Choose the stop where you got off.')); return }
    setEnding(true); setError('')
    try {
      const data = await request(`/individual-bus/${encodeURIComponent(trip.individualBusId)}/end-trip`, { token, method: 'POST', body: JSON.stringify({ destination }) })
      setFinished({ trip: data.data, wallet: data.wallet })
      notify?.(t('Trip completed! You earned {gems} gems.', { gems: data.gemsEarned ?? data.data.gemsEarned }))
      announceTripChange()
      onCompleted?.(data)
    } catch (e) { setError(errorText(e)) } finally { setEnding(false) }
  }

  if (finished) {
    const minutes = finished.trip.durationMinutes ?? (finished.trip.endedAt && finished.trip.startedAt ? (Date.parse(finished.trip.endedAt) - Date.parse(finished.trip.startedAt)) / 60000 : null)
    return <section className="panel trip-panel trip-panel-done" role="status">
      <span className="trip-panel-icon done"><CheckCircle2 size={22} /></span>
      <div className="trip-panel-main">
        <b>{t('Trip completed!')}</b>
        <span>{t(finished.trip.busName)} · {t(finished.trip.source)} <ArrowRight size={12} /> {t(finished.trip.destination)}</span>
        {finished.trip.startedAt && finished.trip.endedAt && <span><Clock3 size={13} /> {t('{from} – {to}', { from: formatTime(finished.trip.startedAt), to: formatTime(finished.trip.endedAt) })}</span>}
        <div className="trip-panel-figures">
          {minutes !== null && <span className="time"><small>{t('Time on the bus')}</small><b><Timer size={15} /> {tripDuration(minutes)}</b></span>}
          <span><small>{t('Fare')}</small><b>{money(finished.trip.fare)}</b></span>
          <span><small>{t('Distance')}</small><b>{t('{km} km', { km: formatNumber(finished.trip.distance, { maximumFractionDigits: 1 }) })}</b></span>
          <span className="gems"><small>{t('Gems earned')}</small><b><Gem size={15} /> +{formatNumber(finished.trip.gemsEarned)}</b></span>
          {finished.wallet && <span><small>{t('Gems in wallet')}</small><b>{formatNumber(finished.wallet.gems)}</b></span>}
        </div>
      </div>
      <button type="button" className="trip-panel-close" onClick={() => setFinished(null)} aria-label={t('Close')}><X size={16} /></button>
    </section>
  }
  if (status !== 'ready' || !trip) return null

  return <section className="panel trip-panel" id={ACTIVE_TRIP_ID} aria-live="polite">
    <span className="trip-panel-icon"><BusFront size={22} /><i className="trip-live-dot" /></span>
    <div className="trip-panel-main">
      <span className="trip-panel-kicker">{t('YOU ARE ON A BUS')}</span>
      <b>{t(trip.busName)}{trip.busCode && <em> · {trip.busCode}</em>}</b>
      <span><MapPin size={13} /> {t('Got on at {stop}', { stop: t(trip.source) })} · <Clock3 size={13} /> {t('since {time}', { time: formatTime(trip.startedAt) })}</span>
      {elapsed !== null && <div className="trip-timer" data-testid="trip-timer">
        <span className="trip-timer-icon" aria-hidden="true"><Timer size={18} /></span>
        <div>
          <small>{t('Time on the bus')}</small>
          {/* The ticking clock is hidden from screen readers; the label says the same in minutes, and only changes once a minute. */}
          <b aria-hidden="true">{formatClock(elapsed)}</b>
          <span className="sr-only">{tripDuration(elapsed / 60000)}</span>
        </div>
        <i className="trip-live-dot" aria-hidden="true" />
      </div>}
      <div className="trip-panel-end">
        <StopSelect label={t('Getting off at')} value={destination} stops={choices} onChange={setDestination} tone="end" />
        <button type="button" className={`button button-dark${ending ? ' is-loading' : ''}`} onClick={end} disabled={ending} aria-busy={ending}>
          {ending ? <><span className="button-spinner" aria-hidden="true" /> {t('Ending trip…')}</> : <><Square size={14} /> {t('End trip')}</>}
        </button>
      </div>
      {error && <div className="inline-error" role="alert">{error}</div>}
    </div>
  </section>
}

// Boarding from a bus search result: a Board button and the small form it opens (which vehicle, where you get on,
// where you get off). Returned separately so the card can put the button beside the fare and the form full width.
// activeTrip: the rider's current trip, if any, so the button can say "On this bus" or ask to end the other trip.
export function useBoardControls({ bus, token, signedIn, openAuth, notify, activeTrip }) {
  const [open, setOpen] = useState(false)
  const [vehicles, setVehicles] = useState({ status: 'idle', list: [] })
  const [vehicleId, setVehicleId] = useState('')
  const stops = stopsOf(bus)
  const [boarding, setBoarding] = useState(bus.match?.boarding.name || '')
  const [destination, setDestination] = useState(bus.match?.alighting.name || '')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  const onThisBus = activeTrip && activeTrip.busId === bus._id
  const busyElsewhere = activeTrip && !onThisBus
  const onBoardFor = useElapsed(onThisBus ? activeTrip.startedAt : null)

  const toggle = () => {
    if (!signedIn) return openAuth()
    if (open) { setOpen(false); return }
    setOpen(true); setError('')
    if (vehicles.status === 'ready') return
    setVehicles({ status: 'loading', list: [] })
    request(`/bus/individual/${encodeURIComponent(bus._id)}`, { token })
      .then((data) => {
        const list = data.data || []
        setVehicles({ status: 'ready', list })
        setVehicleId((list.find((v) => v.currentPassengerCount < v.totalPassengerCapacity) || list[0])?._id || '')
      })
      .catch((e) => setVehicles({ status: 'error', list: [], message: errorText(e) }))
  }

  const board = async (event) => {
    event.preventDefault()
    if (!vehicleId) { setError(t('Choose a bus.')); return }
    if (!boarding) { setError(t('Choose the stop where you are getting on.')); return }
    setSaving(true); setError('')
    try {
      const data = await request(`/individual-bus/${encodeURIComponent(vehicleId)}/board`, { token, method: 'POST', body: JSON.stringify({ boardingStop: boarding, destinationStop: destination || null }) })
      notify(t('You boarded {bus} at {stop}. Have a good trip!', { bus: t(data.data.busName), stop: t(data.data.source) }))
      setOpen(false)
      announceTripChange()
    } catch (e) { setError(errorText(e)) } finally { setSaving(false) }
  }

  if (onThisBus) return { button: <span className="board-state"><i className="trip-live-dot" /> {t('On this bus')}{onBoardFor !== null && <em className="board-state-time"> · {formatClock(onBoardFor)}</em>}</span>, form: null }
  // One bus at a time: while on another bus this one cannot be boarded (the server refuses it too), and the card
  // says why and how to end the current trip.
  if (busyElsewhere) {
    const showTrip = () => document.getElementById(ACTIVE_TRIP_ID)?.scrollIntoView({ behavior: 'smooth', block: 'center' })
    return {
      button: <button type="button" className="button button-small board-button is-locked" disabled aria-describedby={`board-blocked-${bus._id}`}>
        <Lock size={13} /> {t('Board')}
      </button>,
      form: <div className="board-blocked" id={`board-blocked-${bus._id}`} role="note">
        <span className="board-blocked-icon" aria-hidden="true"><Lock size={15} /></span>
        <div><b>{t('You are already on {bus}', { bus: t(activeTrip.busName || 'a bus') })}</b>
          <span>{t('You can only be on one bus at a time. End that trip first, then board this bus.')}</span></div>
        <button type="button" className="button button-outline button-small" onClick={showTrip}><Square size={12} /> {t('Go to my trip')}</button>
      </div>,
    }
  }
  const button = <button type="button" className="button button-green button-small board-button" onClick={toggle} aria-expanded={open}>
      <Play size={13} /> {t('Board')}
    </button>
  const form = open && <form className="board-form" onSubmit={board}>
      <b className="board-form-title">{t('Board {bus}', { bus: t(bus.busName) })}</b>
      {vehicles.status === 'loading' && <span className="board-form-note"><span className="spinner" /> {t('Loading buses on this route…')}</span>}
      {vehicles.status === 'error' && <div className="inline-error">{vehicles.message}</div>}
      {vehicles.status === 'ready' && <>
        <div className="board-vehicles" role="radiogroup" aria-label={t('Bus')}>
          {vehicles.list.map((v) => {
            const full = v.currentPassengerCount >= v.totalPassengerCapacity
            return <label key={v._id} className={`board-vehicle${vehicleId === v._id ? ' selected' : ''}${full ? ' full' : ''}`}>
              <input type="radio" name={`vehicle-${bus._id}`} value={v._id} checked={vehicleId === v._id} disabled={full} onChange={() => setVehicleId(v._id)} />
              <b>{v.busCode}</b>
              <small>{full ? t('Full') : t('{free} seats free', { free: v.totalPassengerCapacity - v.currentPassengerCount })}</small>
            </label>
          })}
        </div>
        <div className="board-stops">
          <StopSelect label={t('Getting on at')} value={boarding} stops={stops} tone="start"
            onChange={(next) => { setBoarding(next); setError(''); if (!destinationsFrom(stops, next, bus.runsBothWays).includes(destination)) setDestination('') }} />
          <StopSelect label={t('Getting off at (optional)')} value={destination} stops={destinationsFrom(stops, boarding, bus.runsBothWays)} onChange={setDestination} empty={t('Decide later')} tone="end" />
        </div>
        <div className="board-form-actions">
          <button type="submit" className={`button button-green button-small${saving ? ' is-loading' : ''}`} disabled={saving} aria-busy={saving}>{saving ? <><span className="button-spinner" aria-hidden="true" /> {t('Boarding…')}</> : <><Play size={13} /> {t('Start trip')}</>}</button>
          <button type="button" className="button button-outline button-small" onClick={() => setOpen(false)}>{t('Cancel')}</button>
        </div>
      </>}
      {error && <div className="inline-error" role="alert">{error}</div>}
    </form>
  return { button, form }
}

// "1 hr 5 min" / "25 min" / "under a minute".
export const tripDuration = (minutes) => {
  if (minutes === null || minutes === undefined || minutes < 0) return null
  if (minutes < 1) return t('under a minute')
  const total = Math.round(minutes)
  const hours = Math.floor(total / 60), mins = total % 60
  return hours ? (mins ? t('{h} hr {m} min', { h: hours, m: mins }) : t('{h} hr', { h: hours })) : t('{m} min', { m: mins })
}
// Stops shown before "Show all stops" in a long ride.
const STOPS_PREVIEW = 6

// The stops of one ride as a vertical timeline: where they got on, each stop passed, where they got off.
function StopsTimeline({ stops }) {
  const [all, setAll] = useState(false)
  const long = stops.length > STOPS_PREVIEW
  // A long ride shows its first two and last two stops until expanded.
  const shown = all || !long ? stops.map((name, i) => ({ name, i })) : [...stops.slice(0, 2).map((name, i) => ({ name, i })), null, ...stops.slice(-2).map((name, j) => ({ name, i: stops.length - 2 + j }))]
  return <div className="trip-stops">
    <ol className="trip-timeline">
      {shown.map((stop) => stop === null
        ? <li key="gap" className="is-gap"><button type="button" className="trip-link" onClick={() => setAll(true)}>{t('+{count} more stops', { count: stops.length - 4 })}</button></li>
        : <li key={stop.i} className={stop.i === 0 ? 'is-start' : stop.i === stops.length - 1 ? 'is-end' : ''}>
          <span>{t(stop.name)}</span>
          {stop.i === 0 && <small>{t('Got on')}</small>}
          {stop.i === stops.length - 1 && <small>{t('Got off')}</small>}
        </li>)}
    </ol>
    {long && all && <button type="button" className="trip-link" onClick={() => setAll(false)}>{t('Show fewer stops')}</button>}
  </div>
}

// One trip in the history: a summary line that opens to every detail saved about the ride.
export function TripRow({ trip, onOpenRoute }) {
  const [open, setOpen] = useState(false)
  const active = trip.status === 'active'
  const started = trip.startedAt || trip.createdAt
  const duration = tripDuration(trip.durationMinutes)
  const stops = trip.stopsRidden || []
  const detailsId = `trip-details-${trip._id}`
  const fact = (label, value, extra) => value || value === 0 ? <div className={extra}><dt>{t(label)}</dt><dd>{value}</dd></div> : null
  const km = Number(trip.distance || 0)

  return <article className={`trip-row${active ? ' is-active' : ''}${open ? ' is-open' : ''}`} data-testid="trip">
    <button type="button" className="trip-row-head" onClick={() => setOpen(!open)} aria-expanded={open} aria-controls={detailsId}>
      <span className="trip-icon"><BusFront size={18} /></span>
      <div className="trip-route-info">
        <b>{t(trip.source || 'Trip')} <ArrowRight size={13} /> {trip.destination ? t(trip.destination) : t('Destination not chosen yet')}</b>
        <small>
          {t(trip.busName || 'Public transit')}
          {trip.routeNumber && <span className="trip-chip">{trip.routeNumber}</span>}
          {trip.busCode && <span className="trip-chip is-code">{trip.busCode}</span>}
          <span className="trip-when">{formatDate(started)} · {formatTime(started)}</span>
          {duration && !active && <span className="trip-chip is-time"><Timer size={11} aria-hidden="true" /> {duration}</span>}
        </small>
      </div>
      <div className="trip-row-side">
        <span className={`trip-status ${active ? 'active' : 'completed'}`}>{active ? t('Active') : t('Completed')}</span>
        <span className="trip-fare">{active ? '—' : money(trip.fare)}</span>
        {trip.gemsEarned > 0 && <span className="trip-gems"><Gem size={12} /> +{formatNumber(trip.gemsEarned)}</span>}
      </div>
      <ChevronDown size={17} className="trip-chevron" aria-hidden="true" />
    </button>

    {open && <div className="trip-details" id={detailsId}>
      <div className="trip-details-grid">
        <section>
          <h4><BusFront size={14} aria-hidden="true" /> {t('Bus')}</h4>
          <dl>
            {fact('Bus name', t(trip.busName || 'Public transit'))}
            {fact('Route number', trip.routeNumber)}
            {fact('Vehicle', trip.busCode)}
            {fact('Bus type', trip.busType && t(trip.busType.charAt(0).toUpperCase() + trip.busType.slice(1)))}
            {fact('Operator', trip.operator && t(trip.operator))}
          </dl>
        </section>
        <section>
          <h4><Clock3 size={14} aria-hidden="true" /> {t('Time')}</h4>
          <dl>
            {fact('Date', formatDate(started, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }))}
            {fact('Got on', formatTime(started))}
            {fact('Got off', trip.endedAt ? formatTime(trip.endedAt) : active ? t('Still on the bus') : null)}
            {fact('Time on the bus', duration)}
          </dl>
        </section>
        <section>
          <h4><Receipt size={14} aria-hidden="true" /> {t('Fare & rewards')}</h4>
          <dl>
            {fact('Distance', km > 0 ? t('{km} km', { km: formatNumber(km, { maximumFractionDigits: 1 }) }) : null)}
            {fact('Fare paid', active ? null : money(trip.fare), 'is-strong')}
            {fact('Rate', trip.perKmFare ? t('{rate} per km · minimum {min}', { rate: money(trip.perKmFare), min: money(trip.baseFare || 0) }) : null)}
            {fact('Gems earned', trip.gemsEarned > 0 ? `+${formatNumber(trip.gemsEarned)}` : null, 'is-gems')}
          </dl>
        </section>
      </div>
      {stops.length > 1 && <section className="trip-details-stops">
        <h4><MapPin size={14} aria-hidden="true" /> {t('Stops on this ride')} <span>{t('{count} stops', { count: stops.length })}</span></h4>
        <StopsTimeline stops={stops} />
      </section>}
      {onOpenRoute && trip.routeNumber && <div className="trip-details-actions">
        <button type="button" className="button button-outline button-small" onClick={() => onOpenRoute(trip.routeNumber)}><Route size={14} /> {t('View this bus route')}</button>
      </div>}
    </div>}
  </article>
}

