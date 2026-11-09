import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { t } from '../models/i18n'
import { errorText } from '../models/messages'
import { addFavorite, busIdOf, checkFavorite, myFavorites, removeFavorite } from '../models/favorites'

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
      const data = await myFavorites(token)
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

// Favourite state of a single bus, read with GET /api/favourites/check/{busId}, for pages that show one bus.
export function useFavoriteStatus({ busId, token, signedIn, notify, openAuth }) {
  const [favorite, setFavorite] = useState(false)
  const [pending, setPending] = useState(false)
  const busyRef = useRef(false)
  useEffect(() => {
    if (!signedIn || !busId) { setFavorite(false); return }
    let cancelled = false
    checkFavorite(busId, token)
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
