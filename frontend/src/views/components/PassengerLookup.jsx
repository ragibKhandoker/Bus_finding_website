import React, { useEffect, useRef, useState } from 'react'
import { ArrowRight, BusFront, Search, Users } from 'lucide-react'
import { apiRequest } from '../../models/api'
import { formatNumber, t } from '../../models/i18n'
import { errorText } from '../../models/messages'
import { usePersistentState } from '../../controllers/usePersistentState'

// "Check passenger count": look a bus up by the code written on it (e.g. AL-2-01) and see how full it is.
// Codes are suggested while typing. It does not show where the bus is.

export function PassengerLookup({ token, signedIn, openAuth, navigate }) {
  // The code typed and the bus found are kept when leaving the page and coming back.
  const [code, setCode] = usePersistentState('map:lookup-code', '')
  const [suggestions, setSuggestions] = useState([])
  const [state, setState] = usePersistentState('map:lookup', { status: 'idle' }) // idle | loading | ready | error
  // A look-up still running when the page was left did not finish; start from idle.
  useEffect(() => { setState((s) => s.status === 'loading' ? { status: 'idle' } : s) }, []) // eslint-disable-line react-hooks/exhaustive-deps
  const listId = useRef(`bus-codes-${Math.random().toString(36).slice(2, 8)}`).current

  // Suggest codes once two characters are typed.
  useEffect(() => {
    const term = code.trim()
    if (!signedIn || term.length < 2) { setSuggestions([]); return }
    let active = true
    const timer = window.setTimeout(() => {
      apiRequest(`/individual-bus/search?${new URLSearchParams({ q: term })}`, { token })
        .then((data) => { if (active) setSuggestions(data.data || []) })
        .catch(() => { if (active) setSuggestions([]) })
    }, 250)
    return () => { active = false; window.clearTimeout(timer) }
  }, [code, token, signedIn])

  const lookUp = async (event) => {
    event.preventDefault()
    if (!signedIn) return openAuth()
    const term = code.trim()
    if (!term) { setState({ status: 'error', message: t('Type the code written on the bus, e.g. AL-2-01.') }); return }
    setState({ status: 'loading' })
    try {
      const data = await apiRequest(`/individual-bus/${encodeURIComponent(term)}`, { token })
      setState({ status: 'ready', bus: data.data })
    } catch (e) { setState({ status: 'error', message: errorText(e) }) }
  }

  const bus = state.bus
  const taken = bus?.currentPassengerCount ?? 0
  const total = bus?.totalPassengerCapacity ?? 0
  const share = total ? Math.min(100, Math.round((taken / total) * 100)) : 0
  const level = share >= 90 ? 'full' : share >= 60 ? 'busy' : 'free'

  return <article className="extra-panel passenger-lookup">
    <div className="extra-panel-title"><div className="tool-heading"><span className="tool-icon" aria-hidden="true"><BusFront size={19} /></span><div><small>{t('ACTIVE BUS LOOKUP')}</small><h2>{t('Check passenger count')}</h2></div></div></div>
    <p className="extra-help">{t('Type the code written on the bus (for example AL-2-01) to see how many seats are taken. This does not show where the bus is.')}</p>
    <form className="passenger-lookup-form" onSubmit={lookUp}>
      <label className="passenger-lookup-input">
        <BusFront size={16} aria-hidden="true" />
        <input value={code} onChange={(e) => { setCode(e.target.value); if (state.status === 'error') setState({ status: 'idle' }) }}
          placeholder={t('Bus code, e.g. AL-2-01')} aria-label={t('Bus code')} list={listId} autoComplete="off" spellCheck={false} />
      </label>
      <datalist id={listId}>
        {suggestions.map((s) => <option key={s._id} value={s.busCode}>{[s.busName, s.routeNumber].filter(Boolean).map((x) => t(x)).join(' · ')}</option>)}
      </datalist>
      <button className="extra-button" disabled={state.status === 'loading'}><Search size={14} /> {state.status === 'loading' ? t('Looking up…') : t('Look up bus')}</button>
    </form>

    {state.status === 'error' && <p className="passenger-lookup-error" role="alert">{state.message}</p>}
    {state.status === 'ready' && bus && <div className={`passenger-result is-${level}`} data-testid="passenger-result" aria-live="polite">
      <div className="passenger-result-head">
        <b>{bus.busCode}</b>
        {(bus.busName || bus.routeNumber) && <span>{[bus.busName && t(bus.busName), bus.routeNumber].filter(Boolean).join(' · ')}</span>}
      </div>
      <div className="passenger-result-count">
        <Users size={16} aria-hidden="true" />
        <span>{t('{taken} of {total} seats taken', { taken: formatNumber(taken), total: formatNumber(total) })}</span>
        <em>{t('{free} free', { free: formatNumber(Math.max(0, total - taken)) })}</em>
      </div>
      <div className="passenger-bar" role="meter" aria-valuemin={0} aria-valuemax={total} aria-valuenow={taken} aria-label={t('Seats taken')}><i style={{ width: `${share}%` }} /></div>
      <small>{level === 'full' ? t('This bus is almost full.') : level === 'busy' ? t('This bus is filling up.') : t('Plenty of seats free.')}</small>
    </div>}

    {navigate && <button type="button" className="passenger-lookup-board" onClick={() => navigate('Find a bus')}>
      {t('To board a bus, search for it on Find a bus')} <ArrowRight size={13} aria-hidden="true" /></button>}
  </article>
}
