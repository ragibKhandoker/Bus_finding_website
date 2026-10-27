import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { ArrowRight, BusFront, Heart, HeartOff, Search } from 'lucide-react'
import { apiRequest } from './api'
import { formatDate, formatNumber, t } from './i18n'
import { Feedback, Heading, Loading, RequireSignIn } from './MorePages'

// Favourite buses: the signed-in user's list (one request for every card on a page), the heart button, and the
// Favourite buses page. The server takes the user from the token, so nothing here sends a user id.
const errorText = (error) => t(error?.message || 'Something went wrong. Please try again.')
const money = (value) => `৳${formatNumber(value)}`
const busIdOf = (bus) => String(bus?._id || bus?.busId || '')

const addFavorite = (busId, token) => apiRequest('/api/favbus/add', { token, method: 'POST', body: JSON.stringify({ busId }) })
const removeFavorite = (busId, token) => apiRequest(`/api/favbus/${encodeURIComponent(busId)}`, { token, method: 'DELETE' })

// The user's favourites, loaded once. toggle(bus) adds after the server confirms, and removes at once (put
// back if the server refuses). A bus with a request in flight ignores further clicks.
export function useFavoriteBuses({ token, user, signedIn, notify, openAuth }) {
  const [items, setItems] = useState([])
  const [status, setStatus] = useState('idle') // idle | loading | ready | error message
  const [pending, setPending] = useState(() => new Set())
  const pendingRef = useRef(new Set())
  const itemsRef = useRef(items)
  itemsRef.current = items

  const load = useCallback(async () => {
    if (!signedIn || !user?.id) { setItems([]); setStatus('idle'); return }
    setStatus('loading')
    try {
      const data = await apiRequest(`/api/favbus/user/${encodeURIComponent(user.id)}`, { token })
      setItems(data.data || []); setStatus('ready')
    } catch (e) { setStatus(errorText(e)) }
  }, [signedIn, token, user?.id])
  useEffect(() => { load() }, [load])

  const ids = useMemo(() => new Set(items.map((item) => String(item.busId))), [items])
  const setBusy = (id, busy) => {
    if (busy) pendingRef.current.add(id); else pendingRef.current.delete(id)
    setPending(new Set(pendingRef.current))
  }

  const toggle = useCallback(async (bus) => {
    if (!signedIn) return openAuth?.()
    const id = busIdOf(bus)
    if (!id || pendingRef.current.has(id)) return
    setBusy(id, true)
    const saved = itemsRef.current.find((item) => String(item.busId) === id)
    try {
      if (saved) {
        const index = itemsRef.current.indexOf(saved)
        setItems((current) => current.filter((item) => item !== saved))
        try {
          await removeFavorite(id, token)
          notify?.(t('Removed from your favourite buses.'))
        } catch (e) {
          setItems((current) => current.some((item) => String(item.busId) === id) ? current : [...current.slice(0, index), saved, ...current.slice(index)])
          notify?.(t('Could not remove this bus: {reason}', { reason: errorText(e) }))
        }
      } else {
        try {
          const data = await addFavorite(id, token)
          setItems((current) => current.some((item) => String(item.busId) === id) ? current : [data.data, ...current])
          notify?.(t('Added to your favourite buses.'))
        } catch (e) { notify?.(t('Could not save this bus: {reason}', { reason: errorText(e) })) }
      }
    } finally { setBusy(id, false) }
  }, [signedIn, token, notify, openAuth])

  return { items, status, reload: load, toggle, isFavorite: (bus) => ids.has(busIdOf(bus)), isPending: (bus) => pending.has(busIdOf(bus)) }
}

// Favourite state of a single bus, read with GET /api/favbus/check, for pages that show one bus.
export function useFavoriteStatus({ busId, token, signedIn, notify, openAuth }) {
  const [favorite, setFavorite] = useState(false)
  const [pending, setPending] = useState(false)
  const busyRef = useRef(false)
  useEffect(() => {
    if (!signedIn || !busId) { setFavorite(false); return }
    let cancelled = false
    apiRequest(`/api/favbus/check?${new URLSearchParams({ busId })}`, { token })
      .then((data) => { if (!cancelled) setFavorite(Boolean(data.isFavourite)) })
      .catch(() => {})
    return () => { cancelled = true }
  }, [busId, token, signedIn])

  const toggle = async () => {
    if (!signedIn) return openAuth?.()
    if (!busId || busyRef.current) return
    busyRef.current = true; setPending(true)
    const wasFavorite = favorite
    if (wasFavorite) setFavorite(false)
    try {
      if (wasFavorite) { await removeFavorite(busId, token); notify?.(t('Removed from your favourite buses.')) }
      else { await addFavorite(busId, token); setFavorite(true); notify?.(t('Added to your favourite buses.')) }
    } catch (e) {
      setFavorite(wasFavorite)
      notify?.(wasFavorite ? t('Could not remove this bus: {reason}', { reason: errorText(e) }) : t('Could not save this bus: {reason}', { reason: errorText(e) }))
    } finally { busyRef.current = false; setPending(false) }
  }
  return { favorite, pending, toggle }
}

// Outline heart when not saved, filled when saved, a spinner while the request runs.
export function FavoriteButton({ active, pending, onClick, withLabel = false, className = '' }) {
  const label = active ? t('Remove from favourite buses') : t('Add to favourite buses')
  return <button type="button" className={`favorite${active ? ' saved' : ''}${withLabel ? ' with-label' : ''} ${className}`.trim()} onClick={onClick} disabled={pending} aria-pressed={active} aria-busy={pending} aria-label={label} title={label}>
    {pending ? <span className="favorite-spinner" aria-hidden="true" /> : <Heart size={17} fill={active ? 'currentColor' : 'none'} />}
    {withLabel && <span>{active ? t('Saved to favourites') : t('Save to favourites')}</span>}
  </button>
}

export function FavoritesPage({ token, user, signedIn, openAuth, notify, navigate }) {
  const favorites = useFavoriteBuses({ token, user, signedIn, notify, openAuth })
  const heading = <Heading icon={Heart} eyebrow={t('YOUR SAVED BUSES')} title={t('Favourite buses')} subtitle={t('Buses you saved with the heart button. Open one to see its stops, or remove the ones you no longer need.')} />
  if (!signedIn) return <section className="extra-page">{heading}<RequireSignIn openAuth={openAuth} /></section>

  const { items, status } = favorites
  return <section className="extra-page">{heading}
    {status !== 'idle' && status !== 'loading' && status !== 'ready' && <Feedback error={<>{status} <button className="link-button" onClick={favorites.reload}>{t('Try again')}</button></>} />}
    {status === 'loading' && !items.length ? <Loading label="Loading your favourite buses…" />
      : status === 'ready' && !items.length ? <div className="extra-panel fav-empty"><span><HeartOff size={22} /></span><b>{t('No favourite buses yet')}</b><p>{t('Search for a bus and tap the heart on its card to save it here.')}</p><button className="extra-button" onClick={() => navigate('Find a bus')}><Search size={14} /> {t('Find a bus')}</button></div>
      : <>{items.length > 0 && <p className="fav-count">{items.length === 1 ? t('1 saved bus') : t('{count} saved buses', { count: items.length })}</p>}
        <div className="fav-grid">{items.map((bus) => <article key={bus.busId} className="extra-panel fav-card">
          <div className="fav-card-head">
            <span className="fav-card-icon"><BusFront size={19} /></span>
            <div><h2>{t(bus.busName) || t('City bus')}</h2><p>{[bus.operator, bus.busType, bus.frequency].filter(Boolean).map((text) => t(text)).join(' · ') || t('Dhaka transit')}</p></div>
            {bus.routeNumber && <span className="route-badge">{bus.routeNumber}</span>}
          </div>
          {bus.firstStop && <div className="fav-route"><span><i className="start" />{t(bus.firstStop)}</span><ArrowRight size={13} /><span><i className="end" />{t(bus.lastStop)}</span></div>}
          <div className="fav-facts">
            {bus.stopCount != null && <span><small>{t('Stops')}</small><b>{formatNumber(bus.stopCount)}</b></span>}
            {bus.base_fare != null && <span><small>{t('Fare from')}</small><b>{money(bus.base_fare)}</b></span>}
            <span><small>{t('Saved on')}</small><b>{formatDate(bus.createdAt)}</b></span>
          </div>
          <div className="fav-actions">
            {bus.routeNumber && <button className="extra-button" onClick={() => navigate('Bus details', { route: bus.routeNumber })}>{t('View stops')} <ArrowRight size={13} /></button>}
            <button className="extra-button secondary fav-remove" disabled={favorites.isPending(bus)} onClick={() => favorites.toggle(bus)}>{favorites.isPending(bus) ? <span className="favorite-spinner" /> : <HeartOff size={14} />} {t('Remove')}</button>
          </div>
        </article>)}</div></>}
  </section>
}
