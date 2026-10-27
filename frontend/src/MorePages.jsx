import React, { useCallback, useEffect, useState } from 'react'
import { ArrowRight, ArrowUpDown, BusFront, Calculator, Check, CreditCard, MapPin, Navigation, RefreshCw, Search, ShieldCheck, Wallet } from 'lucide-react'
import './more.css'
import { apiRequest } from './api'
import { SosAlert } from './Notifications'
import { formatDate, formatDateTime, formatNumber, getLang, t, toEnglish } from './i18n'

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


export function OffersPage({ token, signedIn, openAuth, notify }) {
  const [offers, setOffers] = useState(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const load = useCallback(async () => { if (!signedIn) return; setLoading(true); try { setOffers(await api('/offers/user', token)); setError('') } catch (e) { setError(err(e)) } finally { setLoading(false) } }, [signedIn, token])
  useEffect(() => { load() }, [load])
  const convert = async (type) => {
    const amount = Number(offers?.[type] || 0)
    if (!amount) return
    try { const result = await api(`/offers/use-${type}`, token, { method: 'POST', body: json({ amount }) }); setOffers(result.offers); notify(t(result.message || '{type} converted to wallet balance.', { type: t(type) })) }
    catch (e) { setError(err(e)) }
  }
  if (!signedIn) return <section className="extra-page"><Heading icon={CreditCard} eyebrow={t('LITTLE PERKS, BETTER TRIPS')} title={t('Offers & rewards')} subtitle={t('See your rewards and convert eligible offers into wallet balance.')} /><RequireSignIn openAuth={openAuth} /></section>
  return <section className="extra-page"><Heading icon={CreditCard} eyebrow={t('LITTLE PERKS, BETTER TRIPS')} title={t('Offers & rewards')} subtitle={t('See your rewards and convert eligible offers into wallet balance.')} /><Feedback error={error} />{loading ? <Loading /> : offers ? <div className="offer-grid">{[['cashback','Cashback','Cashback earned from eligible trips.'],['coupon','Coupons','Available coupon value for your account.'],['discount','Discounts','Taken off your fare automatically when you pay for a bus in the SmartCommute app.']].map(([key,label,description]) => <article className="extra-panel offer-card" key={key}><span className="offer-symbol">{key === 'cashback' ? '৳' : key === 'coupon' ? '✳' : '%'}</span><small>{t('YOUR {label}', { label: t(label).toUpperCase() })}</small><strong>৳{formatNumber(offers[key])}</strong><p>{t(description)}</p>{key !== 'discount' && <button className="extra-button" disabled={!Number(offers[key])} onClick={() => convert(key)}>{t('Move to wallet')} <ArrowRight size={14} /></button>}</article>)}</div> : <div className="extra-panel extra-empty-copy">{error || t('No offers are available.')} <button className="extra-button secondary" onClick={load}>{t('Try again')}</button></div>}</section>
}

export function GpayPage({ token, signedIn, openAuth, notify }) {
  const [account, setAccount] = useState(null)
  const [pin, setPin] = useState('')
  const [amount, setAmount] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const load = useCallback(async () => { if (!signedIn) return; setLoading(true); try { const data = await api('/gpay/balance', token); setAccount(data.data); setError('') } catch (e) { setAccount(null); setError(/not found/i.test(err(e)) ? '' : err(e)) } finally { setLoading(false) } }, [signedIn, token])
  useEffect(() => { load() }, [load])
  const call = async (path, body, success) => {
    setError('')
    try { const data = await api(path, token, { method: 'POST', ...(body ? { body: json(body) } : {}) }); if (data.data) setAccount(data.data); notify(success); if (path.endsWith('/recharge') || path.endsWith('/deduct')) load(); return data }
    catch (e) { setError(err(e)); return null }
  }
  const recharge = async (e) => { e.preventDefault(); const data = await call('/gpay/recharge', { amount: Number(amount) }, t('Wallet recharged from GPay.')); if (data) setAmount('') }
  const pay = async (e) => { e.preventDefault(); const data = await call('/gpay/deduct', { amount: Number(amount) }, t('GPay payment completed.')); if (data) setAmount('') }
  if (!signedIn) return <section className="extra-page"><Heading icon={Wallet} eyebrow={t('YOUR DIGITAL WALLET')} title={t('GPay & recharge')} subtitle={t('Manage the in-app GPay balance and move funds to your commute wallet.')} /><RequireSignIn openAuth={openAuth} /></section>
  return <section className="extra-page"><Heading icon={Wallet} eyebrow={t('YOUR DIGITAL WALLET')} title={t('GPay & recharge')} subtitle={t('Manage the in-app GPay balance and move funds to your commute wallet.')} /><Feedback error={error} />{loading ? <Loading /> : <div className="gpay-layout"><article className="extra-panel gpay-card"><small>{t('GPAY BALANCE')}</small><strong>৳{formatNumber(account?.balance)}</strong><span>{account ? t('GPay account active') : t('No GPay account connected')}</span>{!account && <div className="pin-row"><input aria-label={t('GPay PIN')} inputMode="numeric" maxLength={4} value={pin} onChange={(e) => setPin(e.target.value)} placeholder={t('4-digit PIN')} /><button className="extra-button" onClick={async () => { const result = await call('/gpay/register', null, t('GPay account created. Save your new PIN securely.')); if (result?.data) { setAccount(result.data); setPin(result.data.displayCode || '') } }}>{t('Create account')}</button><button className="extra-button secondary" disabled={!pin} onClick={() => call('/gpay/login', { code: pin }, t('GPay unlocked.'))}>{t('Unlock')}</button></div>}{account?.displayCode && <p className="gpay-notice">{t('This API returns a display PIN at registration. Keep it private; the web client does not save it.')}</p>}</article>{account && <article className="extra-panel gpay-actions"><h2>{t('Move funds')}</h2><p>{t('These are in-app stored-value operations. No external payment provider is connected.')}</p><form onSubmit={recharge}><label>{t('AMOUNT (BDT)')}<input required min="1" type="number" value={amount} onChange={(e) => setAmount(e.target.value)} placeholder={t('e.g. 500')} /></label><button className="extra-button">{t('Recharge commute wallet')} <ArrowRight size={14} /></button></form><button className="extra-button secondary" onClick={pay}>{t('Pay from GPay balance')}</button></article>}</div>}</section>
}

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

export function VerificationPage({ token, user, signedIn, openAuth, notify }) {
  const [status, setStatus] = useState(null)
  const [queue, setQueue] = useState([])
  const [form, setForm] = useState({ institutionName: '', institutionId: '', gmail: '' })
  const [image, setImage] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const admin = user?.role?.toLowerCase() === 'admin'
  const load = useCallback(async () => {
    if (!signedIn) return
    setLoading(true)
    try { const data = await api('/verify/my-status', token); setStatus(data.data || null); if (admin) { const all = await api('/verify/all', token); setQueue(all.data || []) } setError('') }
    catch (e) { setError(err(e)) } finally { setLoading(false) }
  }, [signedIn, token, admin])
  useEffect(() => { load() }, [load])
  const submit = async (e) => { e.preventDefault(); setError(''); try { let imageUrl; if (image) { const uploaded = await api('/verify/upload-image', token, { method: 'POST', body: json({ image }) }); imageUrl = uploaded.imageUrl } const result = await api('/verify/submit', token, { method: 'POST', body: json({ ...form, imageUrl }) }); setStatus(result.data); notify(t('Verification submitted for review.')) } catch (ex) { setError(err(ex)) } }
  const review = async (id, approved) => { try { await api(`/verify/${approved ? 'approve' : 'reject'}`, token, { method: 'POST', body: json({ verificationId: id }) }); notify(approved ? t('Verification approved.') : t('Verification rejected.')); load() } catch (ex) { setError(err(ex)) } }
  if (!signedIn) return <section className="extra-page"><Heading icon={ShieldCheck} eyebrow={t('ELIGIBILITY & PASSES')} title={t('Student verification')} subtitle={t('Submit an institution ID for review and check your verification status.')} /><RequireSignIn openAuth={openAuth} /></section>
  return <section className="extra-page"><Heading icon={ShieldCheck} eyebrow={t('ELIGIBILITY & PASSES')} title={admin ? t('Verification review') : t('Student verification')} subtitle={admin ? t('Review student pass applications.') : t('Submit an institution ID for review and check your status.')} /><Feedback error={error} />{loading ? <Loading /> : admin ? <article className="extra-panel extra-list-panel"><div className="extra-panel-title"><div><small>{t('ADMIN REVIEW')}</small><h2>{t('Requests')} <span>{formatNumber(queue.length)}</span></h2></div><button className="extra-icon-button" onClick={load}><RefreshCw size={15} /></button></div>{queue.length ? queue.map((item) => <div className="verification-row" key={item._id}><div><b>{item.userName || item.userId?.name || t('Applicant')} · {item.institutionName}</b><small>{item.userEmail} · {t('ID')} {item.institutionId} · {item.gmail} · {t(item.status)}</small></div>{item.imageUrl && <a href={item.imageUrl} target="_blank" rel="noreferrer">{t('View ID')}</a>}{item.status === 'hold' && <><button className="extra-button" onClick={() => review(item._id, true)}>{t('Approve')}</button><button className="extra-button secondary" onClick={() => review(item._id, false)}>{t('Reject')}</button></>}</div>) : <p className="extra-empty-copy">{t('No verification requests found.')}</p>}</article> : <div className="verify-layout"><article className="extra-panel verify-status"><small>{t('YOUR STATUS')}</small><b className={`status-badge ${status?.status || 'none'}`}>{status?.status ? t(status.status) : t('Not submitted')}</b><p>{status?.status === 'approved' ? t('Your student status has been verified.') : status?.status === 'hold' ? t('Your request is waiting for review.') : status?.status === 'rejected' ? t('Your last request was rejected. You can submit a new one.') : t('Student pass verification helps confirm your eligibility.')}</p>{status?.institutionName && <small>{status.institutionName} · {status.institutionId}</small>}</article>{(!status || status.status === 'rejected') && <form className="extra-panel verify-form" onSubmit={submit}><h2>{t('Submit for review')}</h2><label>{t('Institution name')}<input required value={form.institutionName} onChange={(e) => setForm({ ...form, institutionName: e.target.value })} placeholder={t('University or institution')} /></label><label>{t('Institution ID')}<input required value={form.institutionId} onChange={(e) => setForm({ ...form, institutionId: e.target.value })} placeholder={t('Student ID number')} /></label><label>{t('Institution email')}<input required type="email" value={form.gmail} onChange={(e) => setForm({ ...form, gmail: e.target.value })} placeholder="you@institution.edu" /></label><label>{t('Upload ID image (optional)')}<input type="file" accept="image/png,image/jpeg" onChange={(e) => { const file = e.target.files?.[0]; if (file && file.size <= 3_000_000) { const reader = new FileReader(); reader.onload = () => setImage(String(reader.result)); reader.readAsDataURL(file) } else if (file) setError(t('Choose an image smaller than 3 MB.')) }} /></label>{image && <img className="verify-preview" src={image} alt={t('Verification ID preview')} />}<button className="extra-button">{t('Submit verification')} <ArrowRight size={14} /></button><small className="verify-note">{t('Image uploads require the backend’s Cloudinary configuration.')}</small></form>}</div>}</section>
}

