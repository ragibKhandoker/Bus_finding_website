import React, { useCallback, useEffect, useState } from 'react'
import { ArrowRight, BusFront, CheckCircle2, Clock3, Gem, MapPin, Play, Square, X } from 'lucide-react'
import { apiRequest as request } from './api'
import { formatDate, formatNumber, formatTime, t } from './i18n'

// Trip boarding (F05): board a bus from a search result, end the trip at a stop, earn gems.
// The server owns all the rules (one active trip per rider, fare, gems once per trip); these components only ask.

const money = (value) => `৳${formatNumber(value)}`
const errorText = (error) => t(error?.message || 'Something went wrong. Please try again.')

// Fired after boarding or ending a trip, so every open panel (bus page, trips page, wallet) reloads.
export const TRIP_CHANGED_EVENT = 'smartcommute:trip-changed'
const announceTripChange = () => window.dispatchEvent(new Event(TRIP_CHANGED_EVENT))

// Runs load now and again whenever a trip is boarded or ended anywhere in the app.
export function useTripChanges(load) {
  useEffect(() => {
    load()
    window.addEventListener(TRIP_CHANGED_EVENT, load)
    return () => window.removeEventListener(TRIP_CHANGED_EVENT, load)
  }, [load])
}

// The rider's active trip. status: 'idle' (signed out), 'loading', 'ready' (trip may be null) or 'error'.
export function useActiveTrip(token, signedIn) {
  const [state, setState] = useState({ status: signedIn ? 'loading' : 'idle', trip: null, route: null })
  const load = useCallback(() => {
    if (!signedIn) { setState({ status: 'idle', trip: null, route: null }); return }
    request('/trip/active', { token })
      .then((data) => setState({ status: 'ready', trip: data.data || null, route: data.route || null }))
      .catch((e) => setState({ status: 'error', trip: null, route: null, message: errorText(e) }))
  }, [token, signedIn])
  useTripChanges(load)
  return state
}

const stopsOf = (route) => [...(route?.stops || [])].sort((a, b) => (a.order ?? 0) - (b.order ?? 0)).map((stop) => stop.name)

// Stops the rider can get off at, given where they got on: every other stop, or only later ones on a one-way route.
const destinationsFrom = (stops, source, runsBothWays) => {
  const from = stops.indexOf(source)
  return stops.filter((stop, index) => index !== from && (runsBothWays !== false || index > from))
}

// The trip in progress, with where to get off and an End trip button; after ending, the fare and gems earned.
export function ActiveTripPanel({ token, signedIn, notify, onCompleted }) {
  const { status, trip, route } = useActiveTrip(token, signedIn)
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
    return <section className="panel trip-panel trip-panel-done" role="status">
      <span className="trip-panel-icon done"><CheckCircle2 size={22} /></span>
      <div className="trip-panel-main">
        <b>{t('Trip completed!')}</b>
        <span>{t(finished.trip.busName)} · {t(finished.trip.source)} <ArrowRight size={12} /> {t(finished.trip.destination)}</span>
        <div className="trip-panel-figures">
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

  return <section className="panel trip-panel" aria-live="polite">
    <span className="trip-panel-icon"><BusFront size={22} /><i className="trip-live-dot" /></span>
    <div className="trip-panel-main">
      <span className="trip-panel-kicker">{t('YOU ARE ON A BUS')}</span>
      <b>{t(trip.busName)}{trip.busCode && <em> · {trip.busCode}</em>}</b>
      <span><MapPin size={13} /> {t('Got on at {stop}', { stop: t(trip.source) })} · <Clock3 size={13} /> {t('since {time}', { time: formatTime(trip.startedAt) })}</span>
      <div className="trip-panel-end">
        <label className="trip-select"><small>{t('Getting off at')}</small>
          <select value={destination} onChange={(e) => setDestination(e.target.value)}>
            <option value="">{t('Choose a stop')}</option>
            {choices.map((stop) => <option key={stop} value={stop}>{t(stop)}</option>)}
          </select>
        </label>
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

  if (onThisBus) return { button: <span className="board-state"><i className="trip-live-dot" /> {t('On this bus')}</span>, form: null }
  const button = <button type="button" className="button button-green button-small board-button" onClick={toggle} disabled={busyElsewhere} title={busyElsewhere ? t('End your current trip first.') : undefined} aria-expanded={open}>
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
          <label className="trip-select"><small>{t('Getting on at')}</small>
            <select value={boarding} onChange={(e) => setBoarding(e.target.value)}>
              <option value="">{t('Choose a stop')}</option>
              {stops.map((stop) => <option key={stop} value={stop}>{t(stop)}</option>)}
            </select>
          </label>
          <label className="trip-select"><small>{t('Getting off at (optional)')}</small>
            <select value={destination} onChange={(e) => setDestination(e.target.value)}>
              <option value="">{t('Decide later')}</option>
              {destinationsFrom(stops, boarding, bus.runsBothWays).map((stop) => <option key={stop} value={stop}>{t(stop)}</option>)}
            </select>
          </label>
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

// One row of trip history.
export function TripRow({ trip }) {
  const active = trip.status === 'active'
  return <article className={`trip-row${active ? ' is-active' : ''}`}>
    <span className="trip-icon"><BusFront size={18} /></span>
    <div className="trip-route-info">
      <b>{t(trip.source || 'Trip')} <ArrowRight size={13} /> {trip.destination ? t(trip.destination) : t('Destination not chosen yet')}</b>
      <small>{t(trip.busName || 'Public transit')}{trip.busCode ? ` · ${trip.busCode}` : ''} · {formatDate(trip.startedAt || trip.createdAt)} {formatTime(trip.startedAt || trip.createdAt)}</small>
    </div>
    <div className="trip-row-side">
      <span className={`trip-status ${active ? 'active' : 'completed'}`}>{active ? t('Active') : t('Completed')}</span>
      <span className="trip-fare">{active ? '—' : money(trip.fare)}</span>
      {trip.gemsEarned > 0 && <span className="trip-gems"><Gem size={12} /> +{formatNumber(trip.gemsEarned)}</span>}
    </div>
  </article>
}
