import React, { useEffect, useState } from 'react'
import { ArrowRight, BusFront, Check, ChevronDown, Footprints, History } from 'lucide-react'
import { apiRequest } from '../../models/api'
import { formatNumber, t } from '../../models/i18n'
import { errorText } from '../../models/messages'
import { announceTripChange } from '../../models/trips'
import { usePersistentState } from '../../controllers/usePersistentState'

// "Which bus did you take?": the buses that run between the two places picked on the fare calculator, to choose
// one and log the ride into trip history. The server works out the ride's distance, fare and stops from the bus's
// route, so the trip is saved with the bus's real details.

// Buses shown before "Show more".
export const BUS_PREVIEW = 6
const km = (value) => t('{km} km', { km: formatNumber(value, { maximumFractionDigits: 1 }) })
const money = (value) => `৳${formatNumber(value, { maximumFractionDigits: 2 })}`
const walkText = (value) => value < 0.05 ? t('at the stop') : value < 1 ? t('{m} m walk', { m: formatNumber(Math.round(value * 1000 / 10) * 10) }) : t('{km} walk', { km: km(value) })

export function RouteBusPicker({ token, start, end, notify }) {
  const [state, setState] = useState({ status: 'idle', list: [] })
  // The bus chosen is kept with the two places it was chosen for (see the effect below).
  const [kept, setKept] = usePersistentState('map:bus', null) // { key, id }
  const [showAll, setShowAll] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [logged, setLogged] = useState('')

  const key = start && end ? `${start.lat},${start.lon}|${end.lat},${end.lon}` : ''
  // Only a choice made for these same two places counts.
  const chosen = kept && kept.key === key ? kept.id : ''
  const setChosen = (id) => setKept(id ? { key, id } : null)
  useEffect(() => {
    if (!key) { setState({ status: 'idle', list: [] }); return }
    let active = true
    setState({ status: 'loading', list: [] }); setShowAll(false); setError(''); setLogged('')
    const params = new URLSearchParams({ fromLat: start.lat, fromLng: start.lon, toLat: end.lat, toLng: end.lon })
    apiRequest(`/bus/near-route?${params}`, { token })
      .then((data) => {
        if (!active) return
        const list = data.data || []
        setState({ status: 'ready', list })
        // Keep the earlier choice if that bus is still listed; pick the only bus when there is just one.
        setKept((k) => k && k.key === key && list.some((o) => o.bus._id === k.id) ? k
          : list.length === 1 ? { key, id: list[0].bus._id } : null)
      })
      .catch((e) => { if (active) setState({ status: 'error', list: [], message: errorText(e) }) })
    return () => { active = false }
  }, [key, token]) // eslint-disable-line react-hooks/exhaustive-deps

  const option = state.list.find((o) => o.bus._id === chosen)
  const log = async () => {
    if (!option) { setError(t('Choose the bus you took.')); return }
    setSaving(true); setError('')
    const { bus } = option
    try {
      await apiRequest('/trip/add', {
        token, method: 'POST',
        body: JSON.stringify({ busId: bus._id, busName: bus.busName, distance: option.rideKm, fare: Number(option.fare), source: bus.match.boarding.name, destination: bus.match.alighting.name }),
      })
      setLogged(bus._id)
      notify?.(t('Trip on {bus} logged. It now appears in your trip history.', { bus: t(bus.busName) }))
      announceTripChange()
    } catch (e) { setError(errorText(e)) } finally { setSaving(false) }
  }

  if (state.status === 'idle') return null
  const shown = showAll ? state.list : state.list.slice(0, BUS_PREVIEW)
  const logButton = <button type="button" className="extra-button route-buses-log" onClick={log} disabled={saving || !option || logged === chosen}>
    {saving ? t('Logging…') : logged === chosen && option ? <><Check size={14} /> {t('Trip logged')}</> : <><History size={14} /> {t('Log this trip')}</>}
  </button>
  return <section className="extra-panel route-buses" aria-label={t('Which bus did you take?')}>
    <div className="route-buses-head">
      <span className="route-buses-icon" aria-hidden="true"><BusFront size={18} /></span>
      <div className="route-buses-title">
        <b>{t('Which bus did you take?')}{state.list.length > 0 && <span className="route-buses-count">{formatNumber(state.list.length)}</span>}</b>
        <small id="route-bus-toggle-hint">{t('Pick a bus that runs between these places to log this trip. Click it again to unselect.')}</small>
      </div>
      {state.list.length > 0 && <div className="route-buses-actions">
        {option && <small>{t('Saved with {bus}: {from} to {to}, {fare}.', { bus: t(option.bus.busName), from: t(option.bus.match.boarding.name), to: t(option.bus.match.alighting.name), fare: money(option.fare) })}</small>}
        {logButton}
      </div>}
    </div>

    {state.status === 'loading' && <p className="route-buses-note"><i className="location-spinner" /> {t('Finding buses on this route…')}</p>}
    {state.status === 'error' && <p className="route-buses-note is-error" role="alert">{state.message}</p>}
    {state.status === 'ready' && !state.list.length && <p className="route-buses-note">{t('No bus in our database runs between these two places. Try a place closer to a main road or bus stop.')}</p>}

    {state.list.length > 0 && <>
      <div className="route-bus-list" role="radiogroup" aria-label={t('Buses on this route')}>
        {shown.map((o) => {
          const { bus } = o
          const selected = chosen === bus._id
          return <label key={bus._id} className={`route-bus${selected ? ' is-selected' : ''}`} data-testid="route-bus">
            {/* A radio cannot normally be unchosen; clicking the chosen bus again clears the choice. A click on a bus
                not yet chosen is left to onChange. */}
            <input type="radio" name="route-bus" value={bus._id} checked={selected} aria-describedby="route-bus-toggle-hint"
              onChange={() => { setChosen(bus._id); setError(''); setLogged('') }}
              onClick={() => { if (selected) { setChosen(''); setError('') } }} />
            <span className="route-bus-check" aria-hidden="true">{selected && <Check size={13} strokeWidth={3} />}</span>
            <span className="route-bus-main">
              <span className="route-bus-title"><b>{t(bus.busName)}</b>{bus.routeNumber && <em>{bus.routeNumber}</em>}{bus.match?.direction === 'return' && <em className="is-return">{t('Return trip')}</em>}</span>
              <span className="route-bus-stops">{t(bus.match.boarding.name)} <ArrowRight size={12} aria-hidden="true" /> {t(bus.match.alighting.name)}</span>
              <span className="route-bus-meta">
                <span><Footprints size={12} aria-hidden="true" /> {walkText(o.walkToBoardingKm)}</span>
                <span>{t('{count} stops', { count: bus.match.segment.length - 1 })} · {km(o.rideKm)}</span>
              </span>
            </span>
            <span className="route-bus-fare"><small>{t('Bus fare')}</small><b>{money(o.fare)}</b></span>
          </label>
        })}
      </div>
      {state.list.length > BUS_PREVIEW && <button type="button" className="route-buses-more" onClick={() => setShowAll(!showAll)} aria-expanded={showAll}>
        {showAll ? t('Show fewer buses') : t('Show {count} more buses', { count: state.list.length - BUS_PREVIEW })} <ChevronDown size={14} aria-hidden="true" /></button>}

      {error && <p className="route-buses-note is-error" role="alert">{error}</p>}
    </>}
  </section>
}
