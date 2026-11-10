import React, { useEffect, useId, useMemo, useRef, useState } from 'react'
import { BusFront, Search, X } from 'lucide-react'
import { useBusRoutes } from '../../controllers/useBusRoutes'
import { formatNumber, t } from '../../models/i18n'
import '../styles/bus-search.css'

// Letters and digits only, lower case, so "Raida", "raida " and "RAIDA" match, and Bangla names work too.
export const searchKey = (text) => String(text || '').toLowerCase().replace(/[^\p{L}\p{N}]+/gu, '')

// Every Dhaka bus (by name) from the route list, with its route numbers and where it runs.
export function busOptions(buses) {
  const byName = new Map()
  for (const bus of buses || []) {
    const name = (bus.busName || '').trim()
    if (!name) continue
    const entry = byName.get(name) || { name, routes: [], operators: new Set(), from: '', to: '' }
    if (bus.routeNumber && !entry.routes.includes(bus.routeNumber)) entry.routes.push(bus.routeNumber)
    if (bus.operator) entry.operators.add(bus.operator)
    const stops = bus.stops || []
    if (!entry.from && stops.length) { entry.from = stops[0].name; entry.to = stops[stops.length - 1].name }
    byName.set(name, entry)
  }
  return [...byName.values()].map((e) => ({ ...e, operators: [...e.operators] })).sort((a, b) => a.name.localeCompare(b.name))
}

// The options matching what was typed: names that start with it first, then names containing it, then matches on a
// route number, operator or the Bangla name.
export function matchBuses(options, query, limit = 50) {
  const q = searchKey(query)
  if (!q) return options.slice(0, limit)
  const scored = []
  for (const o of options) {
    const name = searchKey(o.name)
    const bangla = searchKey(t(o.name))
    let score = -1
    if (name.startsWith(q) || bangla.startsWith(q)) score = 0
    else if (name.includes(q) || bangla.includes(q)) score = 1
    else if (o.routes.some((r) => searchKey(r) === q)) score = 2
    else if (o.routes.some((r) => searchKey(r).startsWith(q)) || o.operators.some((op) => searchKey(op).includes(q))) score = 3
    if (score >= 0) scored.push([score, o])
  }
  return scored.sort((a, b) => a[0] - b[0] || a[1].name.localeCompare(b[1].name)).slice(0, limit).map(([, o]) => o)
}

// The "Bus name" box of Find a bus, with a list of matching Dhaka buses as you type. Picking one searches for it.
export function BusNameSearch({ value, onChange, onPick }) {
  const [routes] = useBusRoutes()
  const options = useMemo(() => (routes.status === 'ready' ? busOptions(routes.buses) : []), [routes])
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(-1)
  const list = useMemo(() => matchBuses(options, value), [options, value])
  const box = useRef(null)
  const listRef = useRef(null)
  const id = useId()

  useEffect(() => { setActive(-1) }, [value])
  useEffect(() => {
    if (!open) return
    const away = (event) => { if (!box.current?.contains(event.target)) setOpen(false) }
    document.addEventListener('pointerdown', away)
    return () => document.removeEventListener('pointerdown', away)
  }, [open])
  useEffect(() => { listRef.current?.querySelector('[aria-selected="true"]')?.scrollIntoView?.({ block: 'nearest' }) }, [active])

  const pick = (option) => {
    onChange(option.name)
    setOpen(false)
    onPick?.(option.name)
  }
  const onKey = (event) => {
    if (event.key === 'ArrowDown') { event.preventDefault(); setOpen(true); setActive((i) => Math.min(list.length - 1, i + 1)) }
    else if (event.key === 'ArrowUp') { event.preventDefault(); setActive((i) => Math.max(0, i - 1)) }
    else if (event.key === 'Escape') setOpen(false)
    else if (event.key === 'Enter' && open && active >= 0 && list[active]) { event.preventDefault(); pick(list[active]) }
  }
  const showList = open && routes.status !== 'error'

  return <div className="field wide bns" ref={box}>
    <label htmlFor={`${id}-input`}><small>{t('BUS NAME')}</small></label>
    <span><Search size={16} />
      <input id={`${id}-input`} value={value} placeholder={t('e.g. BRTC, Raida')} autoComplete="off" spellCheck="false"
        role="combobox" aria-expanded={showList} aria-controls={`${id}-list`} aria-autocomplete="list"
        aria-activedescendant={showList && active >= 0 ? `${id}-opt-${active}` : undefined}
        onChange={(e) => { onChange(e.target.value); setOpen(true) }} onFocus={() => setOpen(true)} onKeyDown={onKey} />
      {value && <button type="button" className="bns-clear" onClick={() => { onChange(''); setOpen(true) }} aria-label={t('Clear')}><X size={14} /></button>}
    </span>
    {showList && <div className="bns-menu">
      {routes.status === 'loading' ? <p className="bns-note">{t('Loading Dhaka buses…')}</p>
        : !list.length ? <p className="bns-note">{t('No Dhaka bus matches “{name}”. Try another name or a route number.', { name: value.trim() })}</p>
          : <>
            <p className="bns-head">{value.trim() ? t('{count} matching buses', { count: formatNumber(list.length) }) : t('All Dhaka buses ({count})', { count: formatNumber(options.length) })}</p>
            <ul id={`${id}-list`} role="listbox" aria-label={t('Bus suggestions')} ref={listRef}>
              {list.map((o, i) => <li key={o.name} id={`${id}-opt-${i}`} role="option" aria-selected={i === active}
                className={i === active ? 'active' : ''} onPointerDown={(e) => e.preventDefault()} onClick={() => pick(o)} onMouseEnter={() => setActive(i)}>
                <span className="bns-icon"><BusFront size={15} /></span>
                <span className="bns-text"><b>{t(o.name)}</b>{o.from && <small>{t(o.from)} → {t(o.to)}</small>}</span>
                {o.routes.length > 0 && <span className="bns-routes">{o.routes.slice(0, 3).map((r) => <em key={r}>{r}</em>)}</span>}
              </li>)}
            </ul>
          </>}
    </div>}
  </div>
}
