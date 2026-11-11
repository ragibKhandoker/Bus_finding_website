import { useCallback, useEffect, useRef, useState } from 'react'
import { t } from '../../../models/i18n'
import { cached, fetchInto } from '../../../models/driverRequests'

// Loads one driver request ({ key, load } from driverRequests) and keeps { status: 'loading' | 'ready' | 'error',
// data, error }. Data already in the console cache is shown at once and refreshed in the background, so a page
// opens instantly. reload() keeps the old data on screen while it refreshes, so actions do not flash a loader.
export function useDriverData(request, token) {
  const initial = cached(token, request.key)
  const [state, setState] = useState(() => (initial !== undefined ? { status: 'ready', data: initial } : { status: 'loading' }))
  const latest = useRef(0)
  const requestRef = useRef(request)
  requestRef.current = request
  const load = useCallback((fresh) => {
    const id = ++latest.current
    return fetchInto(token, requestRef.current, { fresh })
      .then((data) => { if (id === latest.current) setState({ status: 'ready', data }) })
      .catch((e) => { if (id === latest.current) setState((s) => (s.data !== undefined ? { ...s, refreshError: t(e.message) } : { status: 'error', error: t(e.message || 'Could not load this page.') })) })
  }, [request.key, token]) // eslint-disable-line react-hooks/exhaustive-deps
  // After an action: always a new request, never one that started before the change.
  const reload = useCallback(() => load(true), [load])
  // A new key (another date, filter or page): show what is cached for it straight away, then refresh.
  useEffect(() => {
    const hit = cached(token, request.key)
    // Never show another key's data (another day's trips) as this one's.
    setState(hit !== undefined ? { status: 'ready', data: hit } : { status: 'loading' })
    load(false)
  }, [load]) // eslint-disable-line react-hooks/exhaustive-deps
  return [state, reload]
}
