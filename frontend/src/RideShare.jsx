import React, { useCallback, useEffect, useRef, useState } from 'react'
import { ArrowRight, BadgeCheck, Check, CheckCircle2, Plus, RefreshCw, Search, Trash2, Users, X } from 'lucide-react'
import { apiRequest } from './api'
import { formatDate, formatNumber, formatTime, t } from './i18n'

// Ride share: browse open rides (filtered by destination on the server), post a ride, and manage your own posts.
// The server takes the host from the sign-in token and decides who may delete or complete a post; the buttons
// shown here are only a convenience.

export const MAX_PARTICIPANTS = 10
export const GENDER_PREFERENCES = ['Any', 'Male', 'Female']
const PREFERENCE_LABELS = { Any: 'Anyone', Male: 'Men only', Female: 'Women only' }
const PAGE_SIZE = 50
// How long the destination box waits after the last keystroke before searching.
const FILTER_DELAY_MS = 400

const errorText = (error) => t(error?.message || 'Something went wrong. Please try again.')
const idOf = (item) => String(item?._id || item?.id || '')
const json = (value) => JSON.stringify(value)
export const rideApi = {
  open: (destination) => apiRequest(`/api/rideshare?pageSize=${PAGE_SIZE}${destination ? `&destination=${encodeURIComponent(destination)}` : ''}`),
  byHost: (userId, token) => apiRequest(`/api/rideshare/user/${encodeURIComponent(userId)}?pageSize=${PAGE_SIZE}`, { token }),
  create: (ride, token) => apiRequest('/api/rideshare', { token, method: 'POST', body: json(ride) }),
  remove: (postId, token) => apiRequest(`/api/rideshare/${encodeURIComponent(postId)}`, { token, method: 'DELETE' }),
  complete: (postId, token) => apiRequest('/api/rideshare/complete', { token, method: 'POST', body: json({ postId }) }),
  join: (postId, token) => apiRequest('/api/riderequests', { token, method: 'POST', body: json({ ridePostId: postId }) }),
  myRequests: (userId, token) => apiRequest(`/api/riderequests/user/${encodeURIComponent(userId)}`, { token }),
  requests: (postId, token) => apiRequest(`/api/riderequests/ride/${encodeURIComponent(postId)}`, { token }),
  answer: (requestId, accepted, token) => apiRequest(`/api/riderequests/${accepted ? 'accept' : 'reject'}/${encodeURIComponent(requestId)}`, { token, method: 'PUT' }),
}

const EMPTY_FORM = { source: '', destination: '', maxParticipants: '4', genderPreference: 'Any' }

// Field errors for the create form; an empty object means it can be sent.
export function validateRide(form) {
  const errors = {}
  if (!form.source.trim()) errors.source = t('Please enter where the ride starts.')
  if (!form.destination.trim()) errors.destination = t('Please enter where the ride goes.')
  const count = Number(form.maxParticipants)
  if (!/^\d+$/.test(String(form.maxParticipants).trim()) || !Number.isInteger(count) || count < 2 || count > MAX_PARTICIPANTS) {
    errors.maxParticipants = t('Choose a whole number from 2 to {max}, counting you.', { max: MAX_PARTICIPANTS })
  }
  if (!GENDER_PREFERENCES.includes(form.genderPreference)) errors.genderPreference = t('Choose who can join.')
  return errors
}

// The destination filter lives in the address (?destination=…), so a refresh keeps it.
const readDestination = () => new URLSearchParams(window.location.search).get('destination') || ''
const writeDestination = (value) => {
  if (window.location.pathname !== '/ride-share') return
  const next = value ? `/ride-share?destination=${encodeURIComponent(value)}` : '/ride-share'
  if (window.location.pathname + window.location.search !== next) window.history.replaceState({ page: 'Ride share' }, '', next)
}

function CreateRideForm({ token, onCreated, onClose }) {
  const [form, setForm] = useState(EMPTY_FORM)
  const [errors, setErrors] = useState({})
  const [serverError, setServerError] = useState('')
  const [saving, setSaving] = useState(false)
  const set = (field) => (event) => { setForm({ ...form, [field]: event.target.value }); setErrors({ ...errors, [field]: undefined }); setServerError('') }

  const submit = async (event) => {
    event.preventDefault()
    if (saving) return
    const found = validateRide(form)
    setErrors(found)
    if (Object.keys(found).length) return
    setSaving(true)
    try {
      const data = await rideApi.create({ source: form.source.trim(), destination: form.destination.trim(), maxParticipants: Number(form.maxParticipants), genderPreference: form.genderPreference }, token)
      setForm(EMPTY_FORM)
      onCreated(data.data || data.ridePost)
    } catch (e) {
      setServerError(errorText(e))
    } finally { setSaving(false) }
  }

  const field = (name, label, input) => <label className={`field ride-form-field${errors[name] ? ' invalid' : ''}`}>
    <small>{label}</small>{input}{errors[name] && <em role="alert">{errors[name]}</em>}
  </label>
  return <form className="panel create-ride-form" onSubmit={submit} noValidate aria-label={t('Post a ride share')}>
    <div className="form-head"><div><b>{t('Post a ride share')}</b><span>{t('Tell other commuters where you are going.')}</span></div><button className="icon-button" type="button" onClick={onClose} aria-label={t('Close')}><X size={18} /></button></div>
    {serverError && <div className="ride-form-error" role="alert">{serverError}</div>}
    <div className="form-fields ride-form-grid">
      {field('source', t('FROM'), <input value={form.source} onChange={set('source')} placeholder={t('e.g. Mirpur 10')} maxLength={200} aria-invalid={Boolean(errors.source) || undefined} />)}
      {field('destination', t('TO'), <input value={form.destination} onChange={set('destination')} placeholder={t('e.g. Dhanmondi')} maxLength={200} aria-invalid={Boolean(errors.destination) || undefined} />)}
      {field('maxParticipants', t('MAX PEOPLE (INCLUDING YOU)'), <input type="number" inputMode="numeric" min={2} max={MAX_PARTICIPANTS} step={1} value={form.maxParticipants} onChange={set('maxParticipants')} aria-invalid={Boolean(errors.maxParticipants) || undefined} />)}
      {field('genderPreference', t('WHO CAN JOIN'), <select value={form.genderPreference} onChange={set('genderPreference')}>{GENDER_PREFERENCES.map((value) => <option key={value} value={value}>{t(PREFERENCE_LABELS[value])}</option>)}</select>)}
    </div>
    <button className="button button-green" type="submit" disabled={saving}>{saving ? t('Publishing…') : <>{t('Publish ride')} <ArrowRight size={15} /></>}</button>
  </form>
}

const STATUS_LABELS = { pending: 'Pending', accepted: 'Accepted', rejected: 'Declined' }

// The host's list of join requests for one ride, with Accept and Decline for the pending ones. The server decides
// whether a place is left; the list only reflects what it says.
function RideRequests({ ride, token, notify, onRideChanged }) {
  const [requests, setRequests] = useState([])
  const [status, setStatus] = useState('loading') // loading | ready | error
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(() => new Set())
  const [summary, setSummary] = useState(null)
  // Only the ride's id matters: the card's other details change after every answer and must not reload the list.
  const rideId = idOf(ride)

  const load = useCallback(async () => {
    try {
      const data = await rideApi.requests(rideId, token)
      setRequests(data.data || []); setSummary(data.ride || null); setStatus('ready'); setError('')
    } catch (e) { setStatus('error'); setError(errorText(e)) }
  }, [rideId, token])
  useEffect(() => { load() }, [load])

  const answer = async (item, accepted) => {
    const id = idOf(item)
    if (busy.has(id)) return
    setBusy((current) => new Set(current).add(id))
    setError('')
    try {
      const data = await rideApi.answer(id, accepted, token)
      setRequests((current) => current.map((r) => idOf(r) === id ? { ...r, ...data.data } : r))
      if (data.ride) { setSummary(data.ride); onRideChanged(data.ride) }
      notify(accepted ? t('Ride request accepted.') : t('Ride request declined.'))
    } catch (e) {
      setError(errorText(e))
      load()
    } finally { setBusy((current) => { const next = new Set(current); next.delete(id); return next }) }
  }

  const seatsLeft = summary?.seatsLeft ?? ride.seatsLeft
  const full = seatsLeft === 0
  return <div className="ride-request-list" aria-live="polite">
    {summary && <p className={`ride-request-summary${full ? ' full' : ''}`}>{full
      ? t('This ride is full. Pending requests cannot be accepted.')
      : t('{count} of {max} going · {left} places left', { count: summary.participantCount, max: summary.maxParticipants, left: seatsLeft })}</p>}
    {error && <small className="ride-request-error" role="alert">{error}</small>}
    {status === 'loading' ? <small className="ride-request-error">{t('Loading requests…')}</small>
      : status === 'error' ? null
        : requests.length ? requests.map((item) => {
          const id = idOf(item)
          const person = item.requester || { name: item.requesterName, gender: item.requesterGender }
          return <div className="ride-request-row" key={id} data-testid="ride-request">
            <span className="host-avatar" aria-hidden="true">{person.name?.trim()?.[0]?.toUpperCase() || '?'}</span>
            <span>
              <b>{person.name || t('Commuter')}{person.verified && <BadgeCheck size={13} aria-label={t('Verified student')} />}</b>
              <small>{person.gender ? `${t(person.gender)} · ` : ''}{t('Asked {date}', { date: formatDate(item.createdAt) })}</small>
            </span>
            {item.status === 'pending'
              ? <>
                <button type="button" className="button button-green button-small" disabled={busy.has(id) || full} onClick={() => answer(item, true)} aria-label={t('Accept {name}', { name: person.name })}>{busy.has(id) ? t('Saving…') : t('Accept')}</button>
                <button type="button" className="button button-outline button-small" disabled={busy.has(id)} onClick={() => answer(item, false)} aria-label={t('Decline {name}', { name: person.name })}>{t('Decline')}</button>
              </>
              : <span className={`ride-request-status ${item.status}`}>{t(STATUS_LABELS[item.status] || item.status)}</span>}
          </div>
        }) : <small className="ride-request-error">{t('No requests yet.')}</small>}
  </div>
}

// The rider's side of a ride: Join, or what they have already asked for.
function JoinButton({ ride, myRequest, onJoin }) {
  const [sending, setSending] = useState(false)
  if (myRequest === 'accepted') return <span className="ride-join-state"><CheckCircle2 size={14} /> {t('Joined')}</span>
  if (myRequest === 'pending') return <button type="button" className="button button-dark button-small" disabled><Check size={14} /> {t('Requested')}</button>
  if (ride.seatsLeft === 0) return <button type="button" className="button button-outline button-small" disabled>{t('Full')}</button>
  const join = async () => {
    if (sending) return
    setSending(true)
    try { await onJoin(ride) } finally { setSending(false) }
  }
  return <button type="button" className="button button-dark button-small" disabled={sending} onClick={join}>
    {sending ? t('Sending…') : <>{myRequest === 'rejected' ? t('Ask again') : t('Join ride')} <ArrowRight size={14} /></>}
  </button>
}

export function RideCard({ ride, isMine, signedIn, busy, myRequest, onDelete, onComplete, onJoin, onRideChanged, token, notify }) {
  const open = ride.status === 'active'
  const host = ride.host || { name: ride.userName, gender: ride.gender }
  const [showRequests, setShowRequests] = useState(false)
  const going = ride.participantCount ?? (ride.participants?.length ?? 0) + 1

  return <article className={`ride-card panel${open ? '' : ' completed'}`} data-testid="ride-card">
    <div className="ride-card-top">
      {open ? <><span className="ride-pulse"><span /></span><span>{t('Open')}</span></> : <span className="ride-status-done"><CheckCircle2 size={14} /> {t('Completed')}</span>}
      <span className="ride-preference">{t(PREFERENCE_LABELS[ride.genderPreference] || 'Anyone')}</span>
      <span className="ride-seats" data-testid="ride-seats"><Users size={13} /> {t('{count} of {max} going', { count: going, max: ride.maxParticipants })}</span>
    </div>
    <div className="ride-route">
      <div><span className="route-origin" /><span className="sr-only">{t('From')}</span><b>{ride.source}</b></div>
      <span className="route-connector" />
      <div><span className="route-destination" /><span className="sr-only">{t('To')}</span><b>{ride.destination}</b></div>
    </div>
    <div className="ride-card-footer">
      <span className="host-avatar" aria-hidden="true">{host.name?.trim()?.[0]?.toUpperCase() || 'R'}</span>
      <span className="host-info">
        <b>{host.name || t('Dhaka commuter')}{host.verified && <BadgeCheck size={13} aria-label={t('Verified student')} />}{isMine && <span className="ride-you"> · {t('You')}</span>}</b>
        <small>{t('Posted {date}', { date: `${formatDate(ride.createdAt)} ${formatTime(ride.createdAt)}` })}{!open && ride.completedAt ? ` · ${t('Completed {date}', { date: formatDate(ride.completedAt) })}` : ''}</small>
      </span>
      {!isMine && open && <JoinButton ride={ride} myRequest={myRequest} onJoin={onJoin} />}
    </div>
    {!isMine && open && myRequest === 'rejected' && <p className="ride-join-note">{t('The host declined your last request. You can ask again.')}</p>}
    {isMine && signedIn && <div className="ride-owner-actions">
      {open && <button type="button" className="button button-green button-small" disabled={busy} onClick={() => onComplete(ride)}><CheckCircle2 size={14} /> {t('Mark as completed')}</button>}
      <button type="button" className="button button-outline button-small ride-delete" disabled={busy} onClick={() => onDelete(ride)}><Trash2 size={14} /> {t('Delete')}</button>
      {open && <button type="button" className="button button-outline button-small" onClick={() => setShowRequests(!showRequests)} aria-expanded={showRequests}>{showRequests ? t('Hide requests') : t('View ride requests')}</button>}
      {showRequests && <RideRequests ride={ride} token={token} notify={notify} onRideChanged={onRideChanged} />}
    </div>}
  </article>
}

export function RidePage({ token, user, signedIn, notify, openAuth }) {
  const me = String(user?.id || user?._id || '')
  const [tab, setTab] = useState('open') // open | mine
  const [query, setQuery] = useState(readDestination)
  const [destination, setDestination] = useState(() => readDestination().trim())
  const [openRides, setOpenRides] = useState([])
  const [openTotal, setOpenTotal] = useState(0)
  const [openStatus, setOpenStatus] = useState('loading') // loading | ready | error
  const [myRides, setMyRides] = useState([])
  const [myStatus, setMyStatus] = useState('idle')
  const [error, setError] = useState('')
  const [showForm, setShowForm] = useState(false)
  const [busy, setBusy] = useState(() => new Set())
  // The rider's current request per ride ({ rideId: 'pending' | 'accepted' | 'rejected' }), loaded from the server so
  // the Join buttons are still right after a refresh.
  const [myRequests, setMyRequests] = useState({})
  const openRun = useRef(0)

  const loadOpen = useCallback(async () => {
    const run = ++openRun.current
    setOpenStatus('loading')
    try {
      const data = await rideApi.open(destination)
      if (run !== openRun.current) return
      setOpenRides(data.data || []); setOpenTotal(data.total ?? (data.data || []).length); setOpenStatus('ready'); setError('')
    } catch (e) {
      if (run !== openRun.current) return
      setOpenStatus('error'); setError(errorText(e))
    }
  }, [destination])
  useEffect(() => { loadOpen() }, [loadOpen])

  const loadMine = useCallback(async () => {
    if (!signedIn || !me) return
    setMyStatus((current) => current === 'ready' ? current : 'loading')
    try { setMyRides((await rideApi.byHost(me, token)).data || []); setMyStatus('ready') } catch (e) { setMyStatus('error'); setError(errorText(e)) }
  }, [signedIn, me, token])
  useEffect(() => { if (tab === 'mine') loadMine() }, [tab, loadMine])

  const loadMyRequests = useCallback(async () => {
    if (!signedIn || !me) { setMyRequests({}); return }
    try {
      const data = await rideApi.myRequests(me, token)
      // The list is newest first, so the first request seen for a ride is the current one.
      const latest = {}
      for (const item of data.data || []) if (!(item.ridePostId in latest)) latest[item.ridePostId] = item.status
      setMyRequests(latest)
    } catch { /* the buttons fall back to Join; the server still refuses a duplicate */ }
  }, [signedIn, me, token])
  useEffect(() => { loadMyRequests() }, [loadMyRequests])

  // Search a moment after typing stops, and keep the filter in the address.
  useEffect(() => {
    const timer = window.setTimeout(() => { const value = query.trim(); setDestination(value); writeDestination(value) }, FILTER_DELAY_MS)
    return () => window.clearTimeout(timer)
  }, [query])
  const searchNow = (event) => { event.preventDefault(); const value = query.trim(); setDestination(value); writeDestination(value) }
  const clearFilter = () => { setQuery(''); setDestination(''); writeDestination('') }

  const openForm = () => { if (!signedIn) { openAuth(); return } setShowForm(true) }
  const created = (ride) => {
    setShowForm(false)
    notify(t('Your ride share is live.'))
    if (ride) setMyRides((current) => [ride, ...current.filter((r) => idOf(r) !== idOf(ride))])
    loadOpen()
    if (tab === 'mine') loadMine()
  }

  const withBusy = async (ride, action) => {
    const id = idOf(ride)
    if (busy.has(id)) return
    setBusy((current) => new Set(current).add(id))
    try { await action(id) } finally { setBusy((current) => { const next = new Set(current); next.delete(id); return next }) }
  }
  const remove = (ride) => {
    if (!window.confirm(t('Delete this ride from {source} to {destination}? This cannot be undone.', { source: ride.source, destination: ride.destination }))) return
    return withBusy(ride, async (id) => {
      try {
        await rideApi.remove(id, token)
        setOpenRides((current) => current.filter((r) => idOf(r) !== id))
        setOpenTotal((current) => Math.max(0, current - (openRides.some((r) => idOf(r) === id) ? 1 : 0)))
        setMyRides((current) => current.filter((r) => idOf(r) !== id))
        notify(t('Ride post removed.'))
      } catch (e) { notify(errorText(e)) }
    })
  }
  const complete = (ride) => {
    if (!window.confirm(t('Mark the ride to {destination} as completed? It will leave the open rides list.', { destination: ride.destination }))) return
    return withBusy(ride, async (id) => {
      try {
        const data = await rideApi.complete(id, token)
        const updated = data.data || { ...ride, status: 'completed' }
        setOpenRides((current) => current.filter((r) => idOf(r) !== id))
        setOpenTotal((current) => Math.max(0, current - (openRides.some((r) => idOf(r) === id) ? 1 : 0)))
        setMyRides((current) => current.map((r) => idOf(r) === id ? updated : r))
        notify(t('Ride marked as completed.'))
      } catch (e) { notify(errorText(e)) }
    })
  }
  const join = async (ride) => {
    if (!signedIn) { openAuth(); return }
    try {
      const data = await rideApi.join(idOf(ride), token)
      setMyRequests((current) => ({ ...current, [idOf(ride)]: data.data?.status || 'pending' }))
      notify(t('Your request has been sent.'))
    } catch (e) {
      notify(errorText(e))
      loadMyRequests()
    }
  }
  // After the host accepts or declines, show the ride's new head count on its card.
  const rideChanged = (summary) => {
    const update = (r) => idOf(r) === idOf(summary) ? { ...r, participantCount: summary.participantCount, seatsLeft: summary.seatsLeft } : r
    setOpenRides((current) => current.map(update))
    setMyRides((current) => current.map(update))
  }

  const cards = (list) => <div className="ride-grid">{list.map((ride) => {
    const mine = Boolean(me) && String(ride.host?.id || ride.userId) === me
    return <RideCard key={idOf(ride)} ride={ride} isMine={mine} signedIn={signedIn} busy={busy.has(idOf(ride))} myRequest={myRequests[idOf(ride)]} onDelete={remove} onComplete={complete} onJoin={join} onRideChanged={rideChanged} token={token} notify={notify} />
  })}</div>
  const empty = (title, text, action) => <div className="empty-state panel"><span className="empty-icon"><Users size={22} /></span><b>{title}</b><span>{text}</span>{action}</div>

  let body
  if (tab === 'open') {
    body = openStatus === 'loading' ? <div className="empty-state"><span className="spinner" /> {t('Finding available rides…')}</div>
      : openStatus === 'error' ? empty(t('Could not load rides'), error, <button type="button" className="button button-outline button-small" onClick={loadOpen}><RefreshCw size={14} /> {t('Try again')}</button>)
        : openRides.length ? cards(openRides)
          : destination ? empty(t('No open rides to {destination}', { destination }), t('Try another destination, or clear the search to see every ride.'), <button type="button" className="button button-outline button-small" onClick={clearFilter}>{t('Clear search')}</button>)
            : empty(t('No rides on this route yet'), t('Be the first to post a ride and find your travel crew.'), !showForm && <button type="button" className="button button-green button-small" onClick={openForm}><Plus size={14} /> {t('Post the first ride')}</button>)
  } else {
    body = !signedIn ? empty(t('Sign in to see your rides'), t('Your posted rides, open and completed, appear here.'), <button type="button" className="button button-green button-small" onClick={openAuth}>{t('Sign in')}</button>)
      : myStatus === 'loading' || myStatus === 'idle' ? <div className="empty-state"><span className="spinner" /> {t('Loading your rides…')}</div>
        : myStatus === 'error' ? empty(t('Could not load your rides'), error, <button type="button" className="button button-outline button-small" onClick={loadMine}><RefreshCw size={14} /> {t('Try again')}</button>)
          : myRides.length ? cards(myRides)
            : empty(t('You have not posted a ride yet'), t('Post a ride and it will appear here.'), !showForm && <button type="button" className="button button-green button-small" onClick={openForm}><Plus size={14} /> {t('Post a ride')}</button>)
  }

  return <div className="page inner-page ride-page">
    <div className="page-heading"><span className="heading-icon"><Users size={21} /></span><div><div className="eyebrow">{t('GOOD COMPANY, BETTER RIDES')}</div><h1>{t('Share the journey.')}</h1><p>{t('Find people heading your way. Split the ride, share the road.')}</p></div></div>
    <section className="rides-hero"><span className="rides-hero-icon"><Users size={22} /></span><div><b>{t('Going the same way?')}</b><p>{t('Post where you are going and how many people can come, or browse rides other commuters have posted.')}</p></div><button type="button" className="button button-dark" onClick={() => showForm ? setShowForm(false) : openForm()}><Plus size={16} /> {t('Post a ride')}</button><div className="rides-hero-shape" /></section>
    {showForm && <CreateRideForm token={token} onCreated={created} onClose={() => setShowForm(false)} />}

    <div className="ride-toolbar">
      <div role="tablist" aria-label={t('Ride lists')} className="ride-tabs">
        <button type="button" role="tab" aria-selected={tab === 'open'} className={tab === 'open' ? 'active' : ''} onClick={() => setTab('open')}>{t('Open rides')} {openStatus === 'ready' && <span className="count-bubble">{formatNumber(openTotal)}</span>}</button>
        <button type="button" role="tab" aria-selected={tab === 'mine'} className={tab === 'mine' ? 'active' : ''} onClick={() => setTab('mine')}>{t('My rides')}</button>
      </div>
      {tab === 'open' && <form className="ride-search" role="search" onSubmit={searchNow}>
        <Search size={15} aria-hidden="true" />
        <label htmlFor="ride-destination" className="sr-only">{t('Search a destination')}</label>
        <input id="ride-destination" value={query} onChange={(e) => setQuery(e.target.value)} placeholder={t('Search a destination')} maxLength={200} />
        {query && <button type="button" className="ride-search-clear" onClick={clearFilter} aria-label={t('Clear search')}><X size={14} /></button>}
      </form>}
    </div>
    {body}
  </div>
}
