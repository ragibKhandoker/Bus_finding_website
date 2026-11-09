import React, { useCallback, useEffect, useRef, useState } from 'react'
import {
  ArrowLeft, BadgeCheck, CheckCircle2, Clock3, Flag, Loader2, Lock, MapPin, MessageSquareText, Pencil, RefreshCw, Send, ShieldBan,
  Star, Trash2, UserMinus, UserRound, Users, XCircle,
} from 'lucide-react'
import { formatDate, formatNumber, formatTime, t } from '../../models/i18n'
import { money } from '../helpers'
import { CONTACT_LABELS, GENDER_LABELS, departureParts, errorText, idOf, relativeDeparture, rideApi } from '../../models/rides'
import {
  Avatar, ConfirmDialog, ContactCard, HostProfileModal, JoinRequestModal, ReportModal, RequestBadge, RideFormModal, RideRoute, SeatMeter,
  StarInput, Stars, StatusBadge, VEHICLE_ICONS, WhatsAppButton, WhatsAppNote,
} from '../components/RideShareUI'
import '../styles/rides.css'

// How often an open chat asks for new messages.
const CHAT_POLL_MS = 5000

const REASONS = {
  full: 'This ride is full. A seat may free up later.',
  closed: 'This ride is no longer taking requests.',
  departed: 'This ride has already left.',
  blocked: 'You cannot join this ride.',
  gender: 'This ride is for a different gender preference.',
}

// ---- Host: requests and passengers ----

function HostRequestManagement({ ride, token, notify, onChanged, onProfile }) {
  const [state, setState] = useState({ status: 'loading', items: [] })
  const [tab, setTab] = useState('pending')
  const [busy, setBusy] = useState('')
  const [removing, setRemoving] = useState(null)
  // The request whose rider profile is open; a pending one can be answered from the profile too.
  const [viewing, setViewing] = useState(null)
  const id = idOf(ride)
  const load = useCallback(() => rideApi.requests(id, token)
    .then((d) => setState({ status: 'ready', items: d.data || [] }))
    .catch((e) => setState({ status: 'error', items: [], message: errorText(e) })), [id, token])
  useEffect(() => { load() }, [load])

  const act = async (request, action) => {
    setBusy(idOf(request))
    try {
      const d = action === 'approve' ? await rideApi.approve(idOf(request), token) : await rideApi.reject(idOf(request), token)
      // One message per answer: approving the last seat says so in the same message.
      notify(action !== 'approve' ? t('Request rejected.')
        : d.ride?.seatsLeft === 0 ? t('{name} approved. All seats are taken, so the ride is now full.', { name: request.requester.name })
          : d.ride?.seatsLeft === 1 ? t('{name} approved. 1 seat left.', { name: request.requester.name })
            : t('{name} approved. {left} seats left.', { name: request.requester.name, left: d.ride?.seatsLeft ?? 0 }))
      await Promise.all([load(), onChanged()])
    } catch (e) { notify(errorText(e)); load() } finally { setBusy('') }
  }
  const groups = {
    pending: state.items.filter((r) => r.status === 'pending'),
    approved: state.items.filter((r) => r.status === 'accepted'),
    rejected: state.items.filter((r) => r.status === 'rejected'),
    cancelled: state.items.filter((r) => r.status === 'cancelled'),
  }
  const tabs = [['pending', 'Pending'], ['approved', 'Approved'], ['rejected', 'Rejected'], ['cancelled', 'Cancelled']]
  const live = ride.viewer?.canManage

  return <section className="panel rs-panel" aria-labelledby="rs-manage-title">
    <div className="rs-panel-head">
      <div><h2 id="rs-manage-title">{t('Passenger management')}</h2><p>{t('{approved} approved', { approved: groups.approved.length })} · {ride.seatsLeft === 1 ? t('1 seat left') : t('{left} seats left', { left: ride.seatsLeft })} · {t('{pending} waiting', { pending: groups.pending.length })}</p></div>
      <button type="button" className="rs-icon-button" onClick={load} aria-label={t('Refresh requests')}><RefreshCw size={15} /></button>
    </div>
    <div className="rs-tabs" role="tablist">
      {tabs.map(([key, label]) => <button key={key} type="button" role="tab" aria-selected={tab === key} className={tab === key ? 'on' : ''} onClick={() => setTab(key)}>
        {t(label)} <i>{formatNumber(groups[key].length)}</i></button>)}
    </div>
    {state.status === 'loading' ? <div className="rs-center"><Loader2 className="rs-spin" /> {t('Loading requests…')}</div>
      : state.status === 'error' ? <p className="rs-form-error">{state.message}</p>
        : groups[tab].length === 0 ? <p className="rs-muted rs-pad">{t(tab === 'pending' ? 'No one is waiting for an answer.' : tab === 'approved' ? 'No riders approved yet.' : 'Nothing here.')}</p>
          : <ul className="rs-people">{groups[tab].map((r) => <li key={idOf(r)}>
            <button type="button" className="rs-person" onClick={() => setViewing(r)}>
              <Avatar name={r.requester.name} verified={r.requester.verified} />
              <span><b>{r.requester.name}</b><small><Stars value={r.requester.rating} count={r.requester.reviewCount} compact /> · {t('{n} rides', { n: r.requester.completedRides })} · {t('Asked {date}', { date: formatDate(r.createdAt, { day: 'numeric', month: 'short' }) })}<WhatsAppNote visible={r.contactVisible} number={r.whatsApp} /></small>
                {r.note && <em>“{r.note}”</em>}</span>
            </button>
            <span className="rs-person-actions">
              <button type="button" className="button button-outline button-small" onClick={() => setViewing(r)} aria-label={t('View profile of {name}', { name: r.requester.name })}><UserRound size={14} /> {t('View profile')}</button>
              {r.status === 'pending' && live ? <>
                <button type="button" className="button button-green button-small" disabled={busy === idOf(r) || ride.seatsLeft === 0} onClick={() => act(r, 'approve')}>{busy === idOf(r) ? <Loader2 size={14} className="rs-spin" /> : <CheckCircle2 size={14} />} {t('Accept')}</button>
                <button type="button" className="button button-outline button-small" disabled={busy === idOf(r)} onClick={() => act(r, 'reject')}><XCircle size={14} /> {t('Reject')}</button>
              </> : r.status === 'accepted' && live
                ? <>{r.contactVisible && <WhatsAppButton compact number={r.whatsApp} name={r.requester.name} ride={ride} />}
                  <button type="button" className="button button-outline button-small rs-danger-text" onClick={() => setRemoving(r)}><UserMinus size={14} /> {t('Remove')}</button></>
                : <RequestBadge request={r} />}
            </span>
          </li>)}</ul>}
    {tab === 'pending' && ride.seatsLeft === 0 && groups.pending.length > 0 && <p className="rs-note">{t('All seats are taken. Remove a rider to accept someone else.')}</p>}
    {viewing && <HostProfileModal userId={viewing.requester.id} token={token} signedIn notify={notify} onClose={() => setViewing(null)}
      actions={viewing.status === 'pending' && live ? <>
        <span>{viewing.note ? t('Note: “{note}”', { note: viewing.note }) : t('Asked to join your ride.')}</span>
        <button type="button" className="button button-outline button-small" disabled={busy === idOf(viewing)} onClick={async () => { await act(viewing, 'reject'); setViewing(null) }}><XCircle size={14} /> {t('Reject')}</button>
        <button type="button" className="button button-green button-small" disabled={busy === idOf(viewing) || ride.seatsLeft === 0} onClick={async () => { await act(viewing, 'approve'); setViewing(null) }}><CheckCircle2 size={14} /> {t('Accept')}</button>
      </> : null} />}
    {removing && <ConfirmDialog danger title={t('Remove {name} from the ride?', { name: removing.requester.name })} message={t('Their seat opens up again and they are notified.')}
      confirmLabel={t('Remove rider')} onClose={() => setRemoving(null)}
      doneTitle={t('Rider removed')} doneMessage={t('{name} has been notified and their seat is free again.', { name: removing.requester.name })}
      onConfirm={async () => { await rideApi.removePassenger(idOf(ride), removing.requester.id, token); await Promise.all([load(), onChanged()]) }} />}
  </section>
}

function Passengers({ ride, onProfile }) {
  const riders = ride.participants || []
  return <section className="panel rs-panel">
    <div className="rs-panel-head"><div><h2>{t('Who is going')}</h2><p>{t('{count} of {max} people, counting the host', { count: ride.participantCount, max: ride.maxParticipants })}</p></div></div>
    <ul className="rs-people">
      <li><button type="button" className="rs-person" onClick={() => onProfile(ride.host.id)}><Avatar name={ride.host.name} verified={ride.host.verified} /><span><b>{ride.host.name}</b><small>{t('Host')}</small></span></button></li>
      {riders.map((p) => <li key={p.userId}><button type="button" className="rs-person" onClick={() => onProfile(p.userId)}><Avatar name={p.userName} verified={p.verified} /><span><b>{p.userName}</b><small>{t('Approved rider')}<WhatsAppNote visible={p.contactVisible} number={p.whatsApp} /></small></span></button>
        {p.contactVisible && <WhatsAppButton compact number={p.whatsApp} name={p.userName} ride={ride} />}</li>)}
      {Array.from({ length: Math.max(0, ride.seatsLeft) }, (_, i) => <li key={`free-${i}`} className="rs-free-seat"><span className="rs-avatar rs-avatar-md" aria-hidden="true"><UserRound size={16} /></span><span>{t('Seat available')}</span></li>)}
    </ul>
  </section>
}

// ---- Ride chat ----

export function RideChat({ ride, token, me }) {
  const [messages, setMessages] = useState([])
  const [status, setStatus] = useState('loading')
  const [text, setText] = useState('')
  const [sending, setSending] = useState(false)
  const [error, setError] = useState('')
  const list = useRef(null)
  const last = useRef(null)
  const id = idOf(ride)
  const poll = useCallback(async (initial) => {
    try {
      const d = await rideApi.messages(id, initial ? null : last.current, token)
      const fresh = d.data || []
      if (fresh.length) {
        last.current = fresh[fresh.length - 1].createdAt
        setMessages((m) => { const seen = new Set(m.map(idOf)); return [...m, ...fresh.filter((x) => !seen.has(idOf(x)))] })
      }
      setStatus('ready')
    } catch (e) { if (initial) { setStatus('error'); setError(errorText(e)) } }
  }, [id, token])
  useEffect(() => {
    poll(true)
    const timer = window.setInterval(() => { if (document.visibilityState === 'visible') poll(false) }, CHAT_POLL_MS)
    return () => window.clearInterval(timer)
  }, [poll])
  useEffect(() => { if (list.current) list.current.scrollTop = list.current.scrollHeight }, [messages.length])
  const send = async (e) => {
    e.preventDefault()
    const body = text.trim()
    if (!body || sending) return
    setSending(true); setError('')
    try {
      const d = await rideApi.send(id, body, token)
      setText('')
      last.current = d.data.createdAt
      setMessages((m) => [...m, d.data])
    } catch (err) { setError(errorText(err)) } finally { setSending(false) }
  }
  const closed = ride.state === 'CANCELLED'
  return <section className="panel rs-panel rs-chat" aria-labelledby="rs-chat-title">
    <div className="rs-panel-head"><div><h2 id="rs-chat-title"><MessageSquareText size={17} /> {t('Ride chat')}</h2><p><Lock size={12} /> {t('Private to the host and approved riders.')}</p></div></div>
    <div className="rs-chat-list" ref={list} aria-live="polite">
      {status === 'loading' ? <div className="rs-center"><Loader2 className="rs-spin" /></div>
        : status === 'error' ? <p className="rs-form-error">{error}</p>
          : messages.length === 0 ? <p className="rs-muted rs-center">{t('No messages yet. Say hello and agree on the pickup.')}</p>
            : messages.map((m, i) => {
              const mine = m.senderId === me
              const showName = !mine && (i === 0 || messages[i - 1].senderId !== m.senderId)
              return <div key={idOf(m)} className={`rs-msg${mine ? ' mine' : ''}`}>
                {showName && <small className="rs-msg-name">{m.senderName}{m.fromHost && <i>{t('Host')}</i>}</small>}
                <p>{m.text}</p>
                <time dateTime={m.createdAt}>{formatTime(m.createdAt)} · {formatDate(m.createdAt, { day: 'numeric', month: 'short' })}</time>
              </div>
            })}
    </div>
    {closed ? <p className="rs-note">{t('This ride was cancelled, so its chat is closed.')}</p>
      : <form className="rs-chat-form" onSubmit={send}>
        <input value={text} onChange={(e) => setText(e.target.value)} maxLength={1000} placeholder={t('Write a message…')} aria-label={t('Message')} />
        <button type="submit" className="button button-green button-small" disabled={sending || !text.trim()} aria-label={t('Send')}>{sending ? <Loader2 size={15} className="rs-spin" /> : <Send size={15} />}</button>
      </form>}
    {error && status === 'ready' && <p className="rs-form-error" role="alert">{error}</p>}
  </section>
}

// ---- Reviews ----

function RideReviewSection({ ride, token, signedIn, notify }) {
  const [state, setState] = useState({ status: 'loading', reviews: [], canReview: [] })
  const [target, setTarget] = useState('')
  const [rating, setRating] = useState(0)
  const [comment, setComment] = useState('')
  const [busy, setBusy] = useState(false)
  const id = idOf(ride)
  const load = useCallback(() => rideApi.reviews(id, signedIn ? token : undefined)
    .then((d) => { setState({ status: 'ready', reviews: d.data || [], canReview: d.canReview || [] }); setTarget((cur) => cur || d.canReview?.[0]?.userId || '') })
    .catch((e) => setState({ status: 'error', reviews: [], canReview: [], message: errorText(e) })), [id, token, signedIn])
  useEffect(() => { load() }, [load])
  const submit = async (e) => {
    e.preventDefault()
    if (!target || rating < 1) { notify(t('Choose a person and give 1 to 5 stars.')); return }
    setBusy(true)
    try { await rideApi.review(id, { reviewedUserId: target, rating, comment: comment.trim() || null }, token); notify(t('Thanks for your review.')); setRating(0); setComment(''); setTarget(''); await load() }
    catch (err) { notify(errorText(err)) } finally { setBusy(false) }
  }
  return <section className="panel rs-panel" aria-labelledby="rs-reviews-title">
    <div className="rs-panel-head"><div><h2 id="rs-reviews-title"><Star size={17} /> {t('Ride reviews')}</h2><p>{t('After a completed ride, the host and riders can review each other once.')}</p></div></div>
    {state.canReview.length > 0 && <form className="rs-review-form" onSubmit={submit}>
      <label className="rs-field"><span>{t('Review')}</span><select value={target} onChange={(e) => setTarget(e.target.value)}>
        {state.canReview.map((p) => <option key={p.userId} value={p.userId}>{p.name}</option>)}</select></label>
      <div className="rs-field"><span>{t('Rating')}</span><StarInput value={rating} onChange={setRating} label={t('Rating')} /></div>
      <label className="rs-field rs-span"><span>{t('Your review (optional)')}</span><textarea rows={2} maxLength={1000} value={comment} onChange={(e) => setComment(e.target.value)} placeholder={t('Was the ride safe and on time?')} /></label>
      <button type="submit" className="button button-green button-small" disabled={busy}>{busy && <Loader2 size={14} className="rs-spin" />} {t('Submit review')}</button>
    </form>}
    {state.status === 'loading' ? <div className="rs-center"><Loader2 className="rs-spin" /></div>
      : state.reviews.length === 0 ? <p className="rs-muted rs-pad">{ride.state === 'COMPLETED' ? t('No reviews for this ride yet.') : t('Reviews open once the host marks the ride as completed.')}</p>
        : <ul className="rs-review-list">{state.reviews.map((r) => <li key={idOf(r)}>
          <div><Avatar name={r.reviewerName} size="sm" /><b>{r.reviewerName}</b><span className="rs-muted">→ {r.reviewedUserName}</span><span className="rs-review-stars">{'★'.repeat(r.rating)}<i>{'★'.repeat(5 - r.rating)}</i></span></div>
          {r.comment && <p>{r.comment}</p>}<small>{formatDate(r.createdAt)}</small></li>)}</ul>}
  </section>
}

// ---- The page ----

export function RideDetailsPage({ rideId, token, user, signedIn, notify, navigate, openAuth }) {
  const me = String(user?.id || user?._id || '')
  const [state, setState] = useState({ status: 'loading' })
  const [joining, setJoining] = useState(false)
  const [editing, setEditing] = useState(false)
  const [confirm, setConfirm] = useState(null)
  const [profile, setProfile] = useState(null)
  const [reporting, setReporting] = useState(false)
  const load = useCallback(() => rideApi.get(rideId, signedIn ? token : undefined)
    .then((d) => setState({ status: 'ready', ride: d.data }))
    .catch((e) => setState({ status: 'error', message: errorText(e) })), [rideId, token, signedIn])
  useEffect(() => { load() }, [load])

  const back = <button type="button" className="route-back" onClick={() => navigate('Ride share')}><ArrowLeft size={15} /> {t('All rides')}</button>
  if (state.status !== 'ready') {
    return <div className="page inner-page rs-page">{back}
      {state.status === 'loading' ? <div className="rs-detail-skeleton panel"><i className="w40" /><i className="w75 tall" /><i className="w60" /><i className="w90" /></div>
        : <div className="rs-empty"><span><Users size={22} /></span><b>{t('Ride could not be loaded')}</b><p>{state.message}</p><button type="button" className="button button-outline button-small" onClick={load}>{t('Try again')}</button></div>}
    </div>
  }

  const { ride } = state
  const viewer = ride.viewer || {}
  const isHost = viewer.role === 'host'
  const VehicleIcon = VEHICLE_ICONS[ride.vehicleType] || VEHICLE_ICONS.Car
  const departed = ride.departureAt && new Date(ride.departureAt).getTime() <= Date.now()
  const live = ride.state === 'OPEN' || ride.state === 'FULL'
  const ask = (dialog) => setConfirm(dialog)

  const cancelRequest = () => ask({
    title: viewer.role === 'approved' ? t('Leave this ride?') : t('Cancel your request?'),
    message: viewer.role === 'approved' ? t('Your seat goes back to the host, who is notified.') : t('The host will no longer see your request.'),
    confirmLabel: viewer.role === 'approved' ? t('Leave ride') : t('Cancel request'), danger: true,
    doneTitle: viewer.role === 'approved' ? t('You left the ride') : t('Request cancelled'),
    doneMessage: viewer.role === 'approved' ? t('Your seat went back to the host, who has been notified.') : t('Your request was withdrawn. You can ask again while the ride is open.'),
    run: async () => { await rideApi.cancelRequest(viewer.requestId, token); await load() },
  })
  const cancelRide = () => ask({
    title: t('Cancel this ride?'), message: t('Everyone who asked or was approved is notified. The ride stays in your history.'),
    confirmLabel: t('Cancel ride'), danger: true, withReason: true,
    doneTitle: t('Ride cancelled'), doneMessage: t('Everyone who asked or was approved has been notified.'),
    run: async (reason) => { await rideApi.cancel(idOf(ride), reason, token); await load() },
  })
  const completeRide = () => ask({
    title: t('Mark the ride as completed?'), message: t('Do this after the trip. Everyone on the ride earns gems and can review each other.'),
    confirmLabel: t('Mark as completed'),
    doneTitle: t('Ride completed'), doneMessage: t('Thanks for sharing! Everyone on the ride earned gems and can now review each other.'),
    run: async () => { await rideApi.complete(idOf(ride), token); await load() },
  })

  let action
  if (isHost) action = null
  else if (viewer.role === 'approved') {
    action = <div className="rs-strip rs-action rs-action-ok"><CheckCircle2 size={18} /><div><b>{t('You are on this ride')}</b><p>{ride.pickupDetails ? t('Pickup: {details}', { details: ride.pickupDetails }) : t('Agree on the pickup point in the ride chat.')}</p><ContactCard ride={ride} /></div>
      {live && !departed && <button type="button" className="button button-outline button-small rs-danger-text" onClick={cancelRequest}>{t('Leave ride')}</button>}</div>
  } else if (viewer.role === 'pending') {
    action = <div className="rs-strip rs-action rs-action-wait"><Clock3 size={18} /><div><b>{t('Request sent')}</b><p>{t('Waiting for the host to answer. You will get a notification.')}</p></div>
      <button type="button" className="button button-outline button-small" onClick={cancelRequest}>{t('Cancel request')}</button></div>
  } else if (viewer.canRequest || viewer.cannotRequestReason === 'signin') {
    action = <div className="rs-strip rs-action"><div><b>{t('{n} seats available', { n: ride.seatsLeft })}</b><p>{viewer.role === 'rejected' ? t('The host declined your last request. You can ask again.') : viewer.role === 'removed' ? t('The host removed you from this ride earlier.') : t('Send a request; the host approves riders one by one.')}</p></div>
      <button type="button" className="button button-green" onClick={() => signedIn ? setJoining(true) : openAuth()}>{t('Request to join')}</button></div>
  } else {
    action = <div className="rs-strip rs-action rs-action-off"><XCircle size={18} /><div><b>{t(STATE_TEXT[ride.state] || 'Not taking requests')}</b><p>{t(REASONS[viewer.cannotRequestReason] || 'This ride is not taking requests.')}</p></div></div>
  }

  const when = departureParts(ride.departureAt)
  const facts = [
    [VehicleIcon, 'Vehicle', t(ride.vehicleType || 'Other')],
    [UserRound, 'Who can join', t(GENDER_LABELS[ride.genderPreference] || 'Any gender')],
    [Users, 'Group size', t('{n} people max', { n: ride.maxParticipants })],
    [MapPin, 'Distance', ride.distance ? t('~{km} km', { km: formatNumber(ride.distance) }) : t('Not given')],
    [MessageSquareText, 'Contact', t(CONTACT_LABELS[ride.contactPreference] || 'In-app chat only')],
    [Clock3, 'Posted', formatDate(ride.createdAt, { day: 'numeric', month: 'short', year: 'numeric' })],
  ]

  return <div className="page inner-page rs-page rs-detail">
    {back}
    <section className="rs-band">
      <div className="rs-band-route">
        <div className="rs-detail-top"><StatusBadge state={ride.state} />{ride.departureAt && live && <span className="rs-countdown"><Clock3 size={13} /> {relativeDeparture(ride.departureAt)}</span>}</div>
        <RideRoute ride={ride} large />
      </div>
      <dl className="rs-band-keys">
        <div><dt>{t('Departure')}</dt><dd>{when ? <><b>{when.time}</b><span>{when.day}</span></> : <><b>{t('Flexible')}</b><span>{t('Ask the host')}</span></>}</dd></div>
        <div><dt>{t('Cost per person')}</dt><dd>{ride.costPerPerson == null ? <><b>{t('Ask')}</b><span>{t('the host')}</span></> : <><b>{money(ride.costPerPerson)}</b><span>{t('per person')}</span></>}</dd></div>
        <div><dt>{t('Seats')}</dt><dd><SeatMeter ride={ride} /><span>{t('of {n} for riders', { n: Math.max(0, ride.maxParticipants - 1) })}</span></dd></div>
      </dl>
    </section>

    {isHost ? <section className="rs-strip rs-strip-host">
      <div><b>{t('Your ride')}</b><p>{live ? t('Manage riders below. Mark the ride completed after the trip.') : t('This ride is closed and kept in your history.')}</p></div>
      {live && <div className="rs-strip-actions">
        {!departed && <button type="button" className="button button-outline button-small" onClick={() => setEditing(true)}><Pencil size={14} /> {t('Edit')}</button>}
        <button type="button" className="button button-outline button-small rs-danger-text" onClick={cancelRide}><Trash2 size={14} /> {t('Cancel ride')}</button>
        <button type="button" className="button button-green button-small" onClick={completeRide}><CheckCircle2 size={14} /> {t('Mark as completed')}</button>
      </div>}
    </section> : action}

    <div className="rs-detail-layout">
      <div className="rs-detail-col">
        <section className="panel rs-panel">
          <div className="rs-panel-head"><div><h2>{t('Ride details')}</h2><p>{t('Everything the host shared about this trip.')}</p></div></div>
          <ul className="rs-fact-list">{facts.map(([Icon, label, value]) => <li key={label}><span><Icon size={16} /></span><div><small>{t(label)}</small><b>{value}</b></div></li>)}</ul>
          {ride.instructions && <div className="rs-instructions"><small>{t('Host’s instructions')}</small><p>{ride.instructions}</p></div>}
          {(isHost || viewer.role === 'approved') && ride.pickupDetails && <div className="rs-instructions rs-private"><small><Lock size={12} /> {t('Pickup details · only you and approved riders see this')}</small><p><MapPin size={14} /> {ride.pickupDetails}</p></div>}
          {ride.state === 'CANCELLED' && ride.cancelReason && <p className="rs-note">{t('Cancelled: {reason}', { reason: ride.cancelReason })}</p>}
        </section>
        {isHost && <HostRequestManagement ride={ride} token={token} notify={notify} onChanged={load} onProfile={setProfile} />}
        {viewer.canChat && <RideChat ride={ride} token={token} me={me} />}
        {!viewer.canChat && live && !isHost && <section className="panel rs-panel rs-locked"><span><Lock size={18} /></span><div><b>{t('Ride chat')}</b><p>{t('The private chat opens once the host approves you.')}</p></div></section>}
        {(ride.state === 'COMPLETED' || viewer.canReview) && <RideReviewSection ride={ride} token={token} signedIn={signedIn} notify={notify} />}
      </div>
      <aside className="rs-detail-side">
        <section className="panel rs-host-card">
          <div className="rs-host-card-top">
            <Avatar name={ride.host.name} size="lg" verified={ride.host.verified} />
            <div><small>{t('Hosted by')}</small><b>{ride.host.name}{ride.host.verified && <BadgeCheck size={15} className="rs-verified" aria-label={t('Verified')} />}</b><Stars value={ride.host.rating} count={ride.host.reviewCount} /></div>
          </div>
          <div className="rs-host-card-stats">
            <span><b>{formatNumber(ride.host.completedRides || 0)}</b><small>{t('Completed rides')}</small></span>
            <span><b>{ride.host.rating == null ? t('New') : formatNumber(ride.host.rating)}</b><small>{t('Rating')}</small></span>
          </div>
          <button type="button" className="button button-outline button-small" onClick={() => setProfile(ride.host.id)}><UserRound size={14} /> {t('View host profile')}</button>
          {signedIn && !isHost && <div className="rs-safety">
            <button type="button" className="rs-link" onClick={() => setReporting(true)}><Flag size={13} /> {t('Report ride')}</button>
            <button type="button" className="rs-link" onClick={() => setProfile(ride.host.id)}><ShieldBan size={13} /> {t('Block host')}</button>
          </div>}
        </section>
        <Passengers ride={ride} onProfile={setProfile} />
      </aside>
    </div>

    {joining && <JoinRequestModal ride={ride} token={token} onClose={() => setJoining(false)} onSent={() => { setJoining(false); notify(t('Request sent. The host will be notified.')); load() }} />}
    {editing && <RideFormModal ride={ride} token={token} onClose={() => setEditing(false)} onSaved={() => { setEditing(false); notify(t('Ride updated.')); load() }} />}
    {confirm && <ConfirmDialog {...confirm} onConfirm={confirm.run} onClose={() => setConfirm(null)} />}
    {profile && <HostProfileModal userId={profile} token={token} signedIn={signedIn} notify={notify} onClose={() => setProfile(null)} />}
    {reporting && <ReportModal ride={ride} token={token} notify={notify} onClose={() => setReporting(false)} />}
  </div>
}

const STATE_TEXT = { FULL: 'This ride is full', COMPLETED: 'This ride is completed', CANCELLED: 'This ride was cancelled', EXPIRED: 'This ride has expired', OPEN: 'Not taking requests' }
