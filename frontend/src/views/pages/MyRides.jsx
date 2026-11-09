import React, { useCallback, useEffect, useState } from 'react'
import { ArrowLeft, ArrowRight, CalendarClock, Car, Clock3, Inbox, Loader2, Plus, RefreshCw, Users } from 'lucide-react'
import { formatNumber, t } from '../../models/i18n'
import { money } from '../helpers'
import { errorText, formatDeparture, idOf, rideApi } from '../../models/rides'
import { ConfirmDialog, RequestBadge, RideFormModal, StatusBadge } from '../components/RideShareUI'
import { usePersistentState } from '../../controllers/usePersistentState'
import '../styles/rides.css'

// My Rides: the rides the user hosts and the rides they asked to join, every status, straight from the server.

const HOSTED_FILTERS = [['active', 'Active'], ['full', 'Full'], ['completed', 'Completed'], ['cancelled', 'Cancelled'], ['expired', 'Expired'], ['all', 'All']]
const JOINED_FILTERS = [['pending', 'Pending'], ['approved', 'Approved'], ['rejected', 'Rejected'], ['cancelled', 'Cancelled'], ['all', 'All']]
const hostedMatch = (filter, ride) => filter === 'all' || (filter === 'active' ? ride.state === 'OPEN' : ride.state === filter.toUpperCase())
const joinedMatch = (filter, item) => {
  const state = item.request.state
  return filter === 'all' || (filter === 'approved' ? state === 'APPROVED' : filter === 'cancelled' ? state === 'CANCELLED' || state === 'REMOVED' : state === filter.toUpperCase())
}

function RideRow({ ride, children, onOpen }) {
  return <li className="rs-row">
    <button type="button" className="rs-row-main" onClick={onOpen}>
      <span className="rs-row-route"><i className="rs-dot start" /><b>{t(ride.source)}</b><ArrowRight size={13} /><i className="rs-dot end" /><b>{t(ride.destination)}</b></span>
      <span className="rs-row-meta"><Clock3 size={13} /> {formatDeparture(ride.departureAt)} · {t(ride.vehicleType)} · {ride.costPerPerson == null ? '—' : money(ride.costPerPerson)}</span>
    </button>
    {children}
  </li>
}

function Tabs({ options, value, onChange, counts }) {
  return <div className="rs-tabs" role="tablist">{options.map(([key, label]) => <button key={key} type="button" role="tab" aria-selected={value === key}
    className={value === key ? 'on' : ''} onClick={() => onChange(key)}>{t(label)} <i>{formatNumber(counts[key] ?? 0)}</i></button>)}</div>
}

export function MyRidesPage({ token, signedIn, notify, navigate, openAuth }) {
  const [section, setSection] = usePersistentState('myrides:section', 'hosted')
  const [hostedFilter, setHostedFilter] = usePersistentState('myrides:hosted', 'active')
  const [joinedFilter, setJoinedFilter] = usePersistentState('myrides:joined', 'pending')
  const [hosted, setHosted] = useState({ status: 'loading', items: [] })
  const [joined, setJoined] = useState({ status: 'loading', items: [] })
  const [creating, setCreating] = useState(false)
  const [confirm, setConfirm] = useState(null)

  const load = useCallback(async () => {
    if (!signedIn) return
    const [h, j] = await Promise.allSettled([rideApi.hosted(token), rideApi.joined(token)])
    setHosted(h.status === 'fulfilled' ? { status: 'ready', items: h.value.data || [] } : { status: 'error', items: [], message: errorText(h.reason) })
    setJoined(j.status === 'fulfilled' ? { status: 'ready', items: j.value.data || [] } : { status: 'error', items: [], message: errorText(j.reason) })
  }, [signedIn, token])
  useEffect(() => { load() }, [load])

  const back = <button type="button" className="route-back" onClick={() => navigate('Ride share')}><ArrowLeft size={15} /> {t('All rides')}</button>
  if (!signedIn) {
    return <div className="page inner-page rs-page">{back}
      <div className="rs-empty"><span><Users size={22} /></span><b>{t('Sign in to see your rides')}</b><p>{t('The rides you host and the rides you joined appear here.')}</p>
        <button type="button" className="button button-green button-small" onClick={openAuth}>{t('Sign in')}</button></div></div>
  }

  const open = (ride) => navigate('Ride details', { route: idOf(ride) })
  const hostedCounts = Object.fromEntries(HOSTED_FILTERS.map(([key]) => [key, hosted.items.filter((r) => hostedMatch(key, r)).length]))
  const joinedCounts = Object.fromEntries(JOINED_FILTERS.map(([key]) => [key, joined.items.filter((i) => joinedMatch(key, i)).length]))
  const pendingTotal = hosted.items.filter((r) => r.state === 'OPEN' || r.state === 'FULL').reduce((sum, r) => sum + (r.pendingCount || 0), 0)
  const approvedTotal = hosted.items.filter((r) => r.state === 'OPEN' || r.state === 'FULL').reduce((sum, r) => sum + (r.participantCount - 1), 0)
  const upcomingJoined = joined.items.filter((i) => i.request.state === 'APPROVED' && (i.ride.state === 'OPEN' || i.ride.state === 'FULL')).length
  const loading = hosted.status === 'loading' || joined.status === 'loading'

  const cancelRequest = (item) => setConfirm({
    title: item.request.state === 'APPROVED' ? t('Leave this ride?') : t('Cancel your request?'),
    message: item.request.state === 'APPROVED' ? t('Your seat goes back to the host, who is notified.') : t('The host will no longer see your request.'),
    confirmLabel: item.request.state === 'APPROVED' ? t('Leave ride') : t('Cancel request'), danger: true,
    doneTitle: item.request.state === 'APPROVED' ? t('You left the ride') : t('Request cancelled'),
    doneMessage: item.request.state === 'APPROVED' ? t('Your seat went back to the host, who has been notified.') : t('Your request was withdrawn. You can ask again while the ride is open.'),
    onConfirm: async () => { await rideApi.cancelRequest(idOf(item.request), token); await load() },
  })

  return <div className="page inner-page rs-page">
    {back}
    <div className="page-heading"><span className="heading-icon"><Car size={21} /></span><div><div className="eyebrow">{t('RIDE SHARE')}</div><h1>{t('My rides')}</h1><p>{t('Rides you host and rides you asked to join, with every request and passenger.')}</p></div></div>

    <div className="rs-stats">
      <button type="button" className={section === 'hosted' ? 'on' : ''} onClick={() => setSection('hosted')}><Car size={18} /><span><small>{t('Active hosted rides')}</small><b>{formatNumber(hostedCounts.active + hostedCounts.full)}</b></span></button>
      <button type="button" className={section === 'hosted' ? 'on' : ''} onClick={() => { setSection('hosted'); setHostedFilter('active') }}><Inbox size={18} /><span><small>{t('Requests waiting')}</small><b>{formatNumber(pendingTotal)}</b></span></button>
      <button type="button" className={section === 'hosted' ? 'on' : ''} onClick={() => setSection('hosted')}><Users size={18} /><span><small>{t('Approved passengers')}</small><b>{formatNumber(approvedTotal)}</b></span></button>
      <button type="button" className={section === 'joined' ? 'on' : ''} onClick={() => { setSection('joined'); setJoinedFilter('approved') }}><CalendarClock size={18} /><span><small>{t('Upcoming rides joined')}</small><b>{formatNumber(upcomingJoined)}</b></span></button>
    </div>

    <div className="rs-switch" role="tablist" aria-label={t('My rides')}>
      <button type="button" role="tab" aria-selected={section === 'hosted'} className={section === 'hosted' ? 'on' : ''} onClick={() => setSection('hosted')}>{t('My hosted rides')}</button>
      <button type="button" role="tab" aria-selected={section === 'joined'} className={section === 'joined' ? 'on' : ''} onClick={() => setSection('joined')}>{t('My joined rides')}</button>
      <button type="button" className="rs-icon-button" onClick={load} aria-label={t('Refresh')}>{loading ? <Loader2 size={15} className="rs-spin" /> : <RefreshCw size={15} />}</button>
    </div>

    {section === 'hosted' ? <section className="panel rs-panel">
      <div className="rs-panel-head"><div><h2>{t('My hosted rides')}</h2><p>{t('Open a ride to accept riders, chat, edit, cancel or complete it.')}</p></div>
        <button type="button" className="button button-green button-small" onClick={() => setCreating(true)}><Plus size={14} /> {t('Offer a ride')}</button></div>
      <Tabs options={HOSTED_FILTERS} value={hostedFilter} onChange={setHostedFilter} counts={hostedCounts} />
      {hosted.status === 'loading' ? <div className="rs-center"><Loader2 className="rs-spin" /> {t('Loading your rides…')}</div>
        : hosted.status === 'error' ? <p className="rs-form-error">{hosted.message}</p>
          : <List items={hosted.items.filter((r) => hostedMatch(hostedFilter, r))} empty={hostedFilter === 'active' ? t('You have no open rides. Offer one to find co-riders.') : t('Nothing here yet.')}
            render={(ride) => <RideRow key={idOf(ride)} ride={ride} onOpen={() => open(ride)}>
              <span className="rs-row-side">
                <StatusBadge state={ride.state} />
                <small>{t('{approved} approved · {left} seats left', { approved: ride.participantCount - 1, left: ride.seatsLeft })}</small>
                {ride.pendingCount > 0 && (ride.state === 'OPEN' || ride.state === 'FULL') && <span className="rs-pending-pill">{t('{n} waiting', { n: ride.pendingCount })}</span>}
                <button type="button" className="button button-outline button-small" onClick={() => open(ride)}>{ride.viewer?.canManage ? t('Manage') : t('View')}</button>
              </span>
            </RideRow>} />}
    </section>
      : <section className="panel rs-panel">
        <div className="rs-panel-head"><div><h2>{t('My joined rides')}</h2><p>{t('Every ride you asked to join, and what the host answered.')}</p></div>
          <button type="button" className="button button-outline button-small" onClick={() => navigate('Ride share')}>{t('Find a ride')}</button></div>
        <Tabs options={JOINED_FILTERS} value={joinedFilter} onChange={setJoinedFilter} counts={joinedCounts} />
        {joined.status === 'loading' ? <div className="rs-center"><Loader2 className="rs-spin" /> {t('Loading your requests…')}</div>
          : joined.status === 'error' ? <p className="rs-form-error">{joined.message}</p>
            : <List items={joined.items.filter((i) => joinedMatch(joinedFilter, i))} empty={t('Nothing here yet.')}
              render={(item) => {
                const live = item.ride.state === 'OPEN' || item.ride.state === 'FULL'
                const departed = item.ride.departureAt && new Date(item.ride.departureAt).getTime() <= Date.now()
                const canCancel = live && !departed && (item.request.state === 'PENDING' || item.request.state === 'APPROVED')
                return <RideRow key={idOf(item.request)} ride={item.ride} onOpen={() => open(item.ride)}>
                  <span className="rs-row-side">
                    <RequestBadge request={item.request} />
                    <small>{t('Host: {name}', { name: item.ride.host?.name })} · <StatusBadge state={item.ride.state} /></small>
                    {canCancel && <button type="button" className="button button-outline button-small rs-danger-text" onClick={() => cancelRequest(item)}>{item.request.state === 'APPROVED' ? t('Leave') : t('Cancel')}</button>}
                    <button type="button" className="button button-outline button-small" onClick={() => open(item.ride)}>{t('View')}</button>
                  </span>
                </RideRow>
              }} />}
      </section>}

    {creating && <RideFormModal token={token} onClose={() => setCreating(false)} onSaved={(ride) => { setCreating(false); notify(t('Your ride is live. Riders can now ask to join.')); if (ride) open(ride); else load() }} />}
    {confirm && <ConfirmDialog {...confirm} onClose={() => setConfirm(null)} />}
  </div>
}

function List({ items, render, empty }) {
  return items.length ? <ul className="rs-rows">{items.map(render)}</ul> : <p className="rs-muted rs-pad">{empty}</p>
}
