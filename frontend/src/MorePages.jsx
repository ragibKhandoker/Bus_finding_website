import React, { useCallback, useEffect, useState } from 'react'
import { ArrowRight, ArrowUpDown, BusFront, Calculator, Check, MapPin, Navigation, RefreshCw, Search, ShieldCheck } from 'lucide-react'
import './more.css'
import { apiRequest } from './api'
import { SosAlert } from './Notifications'
import { formatDateTime, formatNumber, getLang, t, toEnglish } from './i18n'

// Leaflet is only downloaded when the map page is opened.
const FareMap = React.lazy(() => import('./FareMap'))

const api = (path, token, options = {}) => apiRequest(path, { ...options, token })
const json = (value) => JSON.stringify(value)
const valueOf = (item) => item?._id || item?.id || ''
// Server messages are in English; the ones with a Bangla entry are shown translated.
const err = (error) => t(error?.message || 'Request failed')
export function Heading({ icon: Icon, eyebrow, title, subtitle }) {
  return <header className="extra-heading"><span><Icon size={20} /></span><div><small>{eyebrow}</small><h1>{title}</h1><p>{subtitle}</p></div></header>
}
export function Feedback({ error, success }) {
  return <>{error && <div className="extra-feedback error">{error}</div>}{success && <div className="extra-feedback success">{success}</div>}</>
}
export function Loading({ label = 'Loading…' }) { return <div className="extra-loading"><i />{t(label)}</div> }
export function RequireSignIn({ openAuth }) { return <div className="extra-panel sign-in-panel"><ShieldCheck size={22} /><div><b>{t('Sign in to continue')}</b><p>{t('This feature uses your SmartCommute account.')}</p></div><button className="extra-button" onClick={openAuth}>{t('Sign in')} <ArrowRight size={14} /></button></div> }




// One place field of the fare calculator. Typing suggests bus stops from our own database as you go; place
// search on OpenStreetMap (Nominatim) only runs when asked (Enter or the button), as its usage policy requires.
function LocationField({ id, label, placeholder, place, onSelect, onLocate }) {
  const [text, setText] = useState(place?.name ? t(place.name) : '')
  const [stops, setStops] = useState([])
  const [places, setPlaces] = useState(null)
  const [open, setOpen] = useState(false)
  const [searching, setSearching] = useState(false)
  const [problem, setProblem] = useState('')

  // A place chosen from outside (history, "use my location", swap) replaces whatever was typed.
  useEffect(() => { if (place) setText(place.name ? t(place.name) : '') }, [place])

  useEffect(() => {
    const term = text.trim()
    if (place || term.length < 2) { setStops([]); return }
    let cancelled = false
    const timer = setTimeout(() => {
      api(`/api/stops/search?${new URLSearchParams({ q: toEnglish(term) })}`)
        .then((data) => { if (!cancelled) setStops(data.data || []) })
        .catch(() => { if (!cancelled) setStops([]) })
    }, 250)
    return () => { cancelled = true; clearTimeout(timer) }
  }, [text, place])

  const searchPlaces = async () => {
    const term = text.trim()
    if (term.length < 2) return setProblem(t('Type at least 2 letters to search.'))
    setSearching(true); setProblem(''); setOpen(true)
    try {
      const data = await api(`/api/geocoding/search?${new URLSearchParams({ q: toEnglish(term), limit: '6', dhaka: 'true', lang: getLang() })}`)
      setPlaces(data.data || [])
      if (!data.data?.length) setProblem(t('No places in Dhaka matched. Try a nearby landmark or area name.'))
    } catch (e) { setPlaces([]); setProblem(err(e)) }
    finally { setSearching(false) }
  }
  const choose = (next) => { onSelect(next); setOpen(false); setPlaces(null); setProblem('') }
  const type = (value) => { setText(value); setPlaces(null); setProblem(''); setOpen(true); if (place) onSelect(null) }
  const shortName = (displayName) => displayName.split(',').slice(0, 2).join(',').trim()
  const showList = open && !place && text.trim().length >= 2

  return <div className="location-field" onBlur={(e) => { if (!e.currentTarget.contains(e.relatedTarget)) setOpen(false) }}>
    <label htmlFor={id}>{label}</label>
    <div className={place ? 'location-input chosen' : 'location-input'}>
      <MapPin size={15} />
      <input id={id} autoComplete="off" value={text} placeholder={placeholder} aria-expanded={showList} aria-controls={`${id}-list`}
        onFocus={() => setOpen(true)} onChange={(e) => type(e.target.value)}
        onKeyDown={(e) => { if (e.key === 'Enter' && !place) { e.preventDefault(); searchPlaces() } if (e.key === 'Escape') setOpen(false) }} />
      {onLocate && <button type="button" className="location-locate" title={t('Use my location')} aria-label={t('Use my location')} onClick={onLocate}><Navigation size={14} /></button>}
    </div>
    {place
      ? <small className="location-coords" title={`${place.lat}, ${place.lon}`}><Check size={13} /> {t('Selected: {name}', { name: t(place.name) })}</small>
      : text.trim().length >= 2 && !showList && <small className="location-hint">{t('Pick a place from the list to select it.')}</small>}
    {showList && <div className="location-list" id={`${id}-list`} role="listbox">
      {stops.length > 0 && <small className="location-group">{t('BUS STOPS')}</small>}
      {stops.map((stop) => <button type="button" role="option" aria-selected="false" key={`stop-${stop.name}`} onClick={() => choose({ name: stop.name, lat: stop.lat, lon: stop.lng, kind: 'stop' })}><BusFront size={14} /><span><b>{t(stop.name)}</b><small>{t('Bus stop')}</small></span></button>)}
      {places?.length > 0 && <small className="location-group">{t('PLACES')}</small>}
      {places?.map((p) => <button type="button" role="option" aria-selected="false" key={`place-${p.place_id}`} onClick={() => choose({ name: shortName(p.display_name), lat: p.lat, lon: p.lon, kind: 'place' })}><MapPin size={14} /><span><b>{shortName(p.display_name)}</b><small>{p.display_name}</small></span></button>)}
      {problem && <small className="location-problem">{problem}</small>}
      {places === null && <button type="button" className="location-search-more" disabled={searching} onClick={searchPlaces}>{searching ? <><i className="location-spinner" /> {t('Searching places…')}</> : <><Search size={14} /> {t('Not in the list? Search all places for “{text}”', { text: text.trim() })}</>}</button>}
      {places !== null && <small className="location-credit">{t('Place search © OpenStreetMap contributors')}</small>}
    </div>}
  </div>
}

export function MapPage({ token, user, signedIn, openAuth, notify }) {
  const [start, setStart] = useState(null)
  const [end, setEnd] = useState(null)
  const [rate, setRate] = useState(null)
  const [result, setResult] = useState(null)
  const [calculating, setCalculating] = useState(false)
  const [error, setError] = useState('')
  const [history, setHistory] = useState([])
  const [historyState, setHistoryState] = useState('idle')
  const [busyId, setBusyId] = useState('')
  const [tripBusId, setTripBusId] = useState('')

  useEffect(() => { api('/api/fares/rate').then((data) => setRate(data.data)).catch(() => setRate(null)) }, [])
  const loadHistory = useCallback(async () => {
    if (!signedIn) { setHistory([]); return }
    setHistoryState('loading')
    try { const data = await api('/api/fares/history?limit=20', token); setHistory(data.data || []); setHistoryState('idle') }
    catch (e) { setHistoryState(err(e)) }
  }, [signedIn, token])
  useEffect(() => { loadHistory() }, [loadHistory])

  // Changing either place clears the old result, so a fare is never shown for places that are no longer selected.
  const pickStart = (place) => { setStart(place); setResult(null) }
  const pickEnd = (place) => { setEnd(place); setResult(null) }
  const swap = () => { setStart(end); setEnd(start); setResult(null) }
  const locate = () => {
    if (!navigator.geolocation) return setError(t('Location is not available in this browser.'))
    navigator.geolocation.getCurrentPosition(({ coords }) => pickStart({ name: 'Your current location', lat: coords.latitude, lon: coords.longitude, kind: 'location' }), () => setError(t('Location permission was denied. Search for a place instead.')))
  }
  const calculate = async (e) => {
    e.preventDefault(); setError('')
    if (!start) return setError(t('Choose a starting point from the suggestions.'))
    if (!end) return setError(t('Choose a destination from the suggestions.'))
    setCalculating(true)
    try {
      const data = await api('/api/fares/calculate', token, { method: 'POST', body: json({ source: start.name, destination: end.name, sourceCoordinates: { lat: start.lat, lon: start.lon }, destinationCoordinates: { lat: end.lat, lon: end.lon } }) })
      setResult(data.data)
      if (signedIn) loadHistory()
    } catch (ex) { setError(err(ex)) }
    finally { setCalculating(false) }
  }
  const showSaved = (item) => {
    const point = (name, c) => c ? { name, lat: c.lat, lon: c.lon, kind: 'history' } : null
    setStart(point(item.source, item.sourceCoordinates)); setEnd(point(item.destination, item.destinationCoordinates))
    setResult({ fare: item, path: [], saved: false, fromHistory: true }); setError('')
    window.scrollTo({ top: 0, behavior: 'smooth' })
  }
  const busInfo = async (id) => {
    if (!signedIn) return openAuth()
    try { const data = await api(`/individual-bus/${encodeURIComponent(id.trim())}`, token); notify(t('Bus {code}: {taken} of {total} seats taken.', { code: data.data.busCode, taken: data.data.currentPassengerCount, total: data.data.totalPassengerCapacity })) }
    catch (ex) { setError(err(ex)) }
  }
  const capacityAction = async (id, action) => {
    if (!signedIn) return openAuth()
    try { const data = await api(`/individual-bus/${action}`, token, { method: 'POST', body: json({ busId: id }) }); notify(t(data.message || 'Passenger count updated.')) }
    catch (ex) { setError(err(ex)) }
  }
  const logRouteTrip = async () => {
    if (!signedIn) return openAuth()
    if (!result || !tripBusId.trim()) return setError(t('Enter the bus ID before logging this route as a trip.'))
    const fare = result.fare
    try {
      await api('/trip/add', token, { method: 'POST', body: json({ busId: tripBusId.trim(), busName: 'Dhaka commute', distance: fare.distance, fare: fare.fare, source: fare.source, destination: fare.destination }) })
      notify(t('Trip logged. Your journey now appears in trip history.'))
    } catch (ex) { setError(err(ex)) }
  }


  const fare = result?.fare
  const money = (value) => `৳${formatNumber(value, { maximumFractionDigits: 2 })}`
  return <section className="extra-page fare-page"><Heading icon={Calculator} eyebrow={t('PLACES, DISTANCE & FARE')} title={t('Map & fare calculator')} subtitle={t('Find out how far it is between two places in Dhaka and about how much the bus fare will be.')} /><Feedback error={error} /><div className="map-grid">
    <div className="map-main">
      <article className="extra-panel fare-calc">
        <div className="extra-panel-title"><div><small>{t('FARE CALCULATOR')}</small><h2>{t('Where are you going?')}</h2></div>{rate?.configured && <span className="fare-rate-chip">{t('Rate: {rate} per km', { rate: money(rate.farePerKm) })}</span>}</div>
        <ol className="fare-steps">
          <li className={start ? 'done' : ''}>{t('Type your starting point and pick it from the list.')}</li>
          <li className={end ? 'done' : ''}>{t('Do the same for your destination.')}</li>
          <li className={fare ? 'done' : ''}>{t('Press “Calculate distance & fare”.')}</li>
        </ol>
        <form className="fare-form" onSubmit={calculate} noValidate>
          <LocationField id="fare-from" label={t('From (starting point)')} placeholder={t('Type a place, e.g. Mirpur 10')} place={start} onSelect={pickStart} onLocate={locate} />
          <button type="button" className="fare-swap" onClick={swap} disabled={!start || !end} title={t('Swap places')} aria-label={t('Swap places')}><ArrowUpDown size={14} /></button>
          <LocationField id="fare-to" label={t('To (destination)')} placeholder={t('Type a place, e.g. Motijheel')} place={end} onSelect={pickEnd} />
          <button className="extra-button fare-submit" disabled={calculating || rate?.configured === false}>{calculating ? <><i className="location-spinner light" /> {t('Calculating…')}</> : <>{t('Calculate distance & fare')} <ArrowRight size={14} /></>}</button>
          {rate?.configured === false && <small className="location-problem">{t('Fare calculation is unavailable: the fare rate is not configured on the server.')}</small>}
        </form>
        {fare && <div className="fare-result" aria-live="polite">
          <div className="fare-route"><b>{t(fare.source)}</b><ArrowRight size={13} /><b>{t(fare.destination)}</b></div>
          <div className="fare-stats">
            <div><small>{fare.distanceEstimated ? t('DISTANCE (APPROX.)') : t('DISTANCE BY ROAD')}</small><strong>{t('{distance} km', { distance: formatNumber(fare.distance, { maximumFractionDigits: 2 }) })}</strong></div>
            <div><small>{t('RATE PER KM')}</small><strong>{fare.farePerKm != null ? money(fare.farePerKm) : '—'}</strong></div>
            <div className="fare-total"><small>{t('ESTIMATED FARE')}</small><strong>{money(fare.fare)}</strong></div>
          </div>
          {fare.farePerKm != null && <p className="fare-explain">{t('How it is worked out: {distance} km × {rate} per km = {exact}, rounded up to {fare}.', { distance: formatNumber(fare.distance, { maximumFractionDigits: 2 }), rate: money(fare.farePerKm), exact: money(fare.distance * fare.farePerKm), fare: money(fare.fare) })}</p>}
          {result.duration > 0 && <small>{t('Travel time: about {minutes} minutes by road, without traffic.', { minutes: Math.round(result.duration) })}</small>}
          {fare.distanceEstimated && <small className="fare-warning">{t('We could not get the road route right now, so this distance is only an approximate guess. Try again later for the exact road distance.')}</small>}
          <small>{t('This is an estimate. The fare you pay on the bus may be different.')}</small>
          {!result.fromHistory && <small className="fare-saved">{signedIn ? (result.saved ? t('Saved to your fare history.') : t('Already saved to your fare history.')) : t('Sign in to keep a history of your fare calculations.')}</small>}
          {signedIn && <div className="fare-log"><input className="route-bus-id" value={tripBusId} onChange={(e) => setTripBusId(e.target.value)} placeholder={t('Bus ID (required to log trip)')} /><button type="button" className="extra-button secondary" onClick={logRouteTrip}>{t('Log this trip')}</button></div>}
        </div>}
      </article>
      <article className="extra-panel fare-map-panel"><React.Suspense fallback={<div className="fare-map"><Loading label="Loading map…" /></div>}><FareMap start={start} end={end} path={result?.path || []} /></React.Suspense><small className="map-attribution">{t('Map data © OpenStreetMap contributors · Routes by OSRM')}</small></article>
    </div>
    <div className="map-side">
      <article className="extra-panel">
        <div className="extra-panel-title"><div><small>{t('SAVED CALCULATIONS')}</small><h2>{t('Your past calculations')}</h2></div>{signedIn && <button className="extra-icon-button" onClick={loadHistory} title={t('Refresh')} aria-label={t('Refresh')}><RefreshCw size={14} /></button>}</div>
        {!signedIn ? <p className="extra-empty-copy">{t('Sign in to see the fares you have calculated.')} <button className="link-button" onClick={openAuth}>{t('Sign in')}</button></p>
          : historyState === 'loading' && !history.length ? <Loading />
          : historyState !== 'idle' && historyState !== 'loading' ? <div className="extra-feedback error">{historyState}</div>
          : !history.length ? <p className="extra-empty-copy">{t('No saved calculations yet. Calculate a fare and it will appear here.')}</p>
          : <div className="fare-history"><p className="extra-help">{t('Tap a row to see it on the map again.')}</p>{history.map((item) => <button key={item._id} className="fare-history-row" onClick={() => showSaved(item)} title={t('Show on the map')}>
              <span><b>{t(item.source)} → {t(item.destination)}</b><small>{formatDateTime(item.createdAt)} · {t('{distance} km', { distance: formatNumber(item.distance, { maximumFractionDigits: 2 }) })}{item.farePerKm != null ? ` · ${t('{rate} per km', { rate: money(item.farePerKm) })}` : ''}{item.distanceEstimated ? ` · ${t('approximate')}` : ''}</small></span>
              <strong>{money(item.fare)}</strong>
            </button>)}</div>}
      </article>
      <article className="extra-panel"><div className="extra-panel-title"><div><small>{t('ACTIVE BUS LOOKUP')}</small><h2>{t('Check passenger count')}</h2></div></div><p className="extra-help">{t('Enter the bus ID (for example DBR-128-01) to see how many seats are taken. This does not show where the bus is.')}</p><form className="map-form map-form-stacked" onSubmit={(e) => { e.preventDefault(); busInfo(busyId) }}><input required value={busyId} onChange={(e) => setBusyId(e.target.value)} placeholder={t('Individual bus ID')} /><button className="extra-button secondary">{t('Look up bus')}</button></form>{signedIn && <div className="map-capacity-actions"><button className="extra-button secondary" onClick={() => capacityAction(busyId, 'board')}>{t('Board bus')}</button><button className="extra-button secondary" onClick={() => capacityAction(busyId, 'end-trip')}>{t('End trip')}</button></div>}</article>
      <SosAlert token={token} signedIn={signedIn} openAuth={openAuth} notify={notify} />
    </div>
  </div></section>
}


