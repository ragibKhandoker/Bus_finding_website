import React, { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { Check, ChevronLeft, ChevronRight } from 'lucide-react'
import { getLang, t } from '../../models/i18n'
import '../styles/controls.css'

// The browser draws <select> lists and date pickers itself, in the operating system's style, and CSS cannot reach
// them. This component, mounted once for the whole app, opens a designed list or calendar in their place. The
// native element stays the source of truth: a choice is written to it and announced with the same input/change
// events a person would cause, so every form keeps working exactly as before (React state, validation, tests).
// Skipped for <select multiple>, list boxes, disabled or read-only fields, and anything marked data-native.

const usable = (el) => el && !el.disabled && !el.readOnly && !el.closest('[data-native]')
const isSelect = (el) => el instanceof HTMLSelectElement && usable(el) && !el.multiple && !(el.size > 1)
const isDate = (el) => el instanceof HTMLInputElement && el.type === 'date' && usable(el)

// Chrome, Edge and Safari let us replace the date picker; elsewhere the browser's own picker is kept.
const canReplaceDatePicker = typeof CSS !== 'undefined' && CSS.supports?.('selector(::-webkit-calendar-picker-indicator)')

function writeValue(el, value) {
  const proto = el instanceof HTMLSelectElement ? HTMLSelectElement.prototype : HTMLInputElement.prototype
  Object.getOwnPropertyDescriptor(proto, 'value').set.call(el, value)
  el.dispatchEvent(new Event('input', { bubbles: true }))
  el.dispatchEvent(new Event('change', { bubbles: true }))
}

const pad = (n) => String(n).padStart(2, '0')
const toIso = (d) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
const fromIso = (s) => (/^\d{4}-\d{2}-\d{2}$/.test(s || '') ? new Date(Number(s.slice(0, 4)), Number(s.slice(5, 7)) - 1, Number(s.slice(8, 10))) : null)
const locale = () => (getLang() === 'bn' ? 'bn-BD' : 'en-GB')

// Where the panel goes: under the field, or above it when there is more room there.
function place(el, height, minWidth) {
  const r = el.getBoundingClientRect()
  const gap = 6
  const below = window.innerHeight - r.bottom - gap - 8
  const above = r.top - gap - 8
  const up = below < Math.min(height, 260) && above > below
  const width = Math.max(r.width, minWidth)
  const left = Math.min(Math.max(8, r.left), window.innerWidth - width - 8)
  return { left, width, maxHeight: Math.max(160, up ? above : below), ...(up ? { bottom: window.innerHeight - r.top + gap } : { top: r.bottom + gap }), up }
}

export function NativeControlsEnhancer() {
  const [open, setOpen] = useState(null) // { kind: 'select' | 'date', el }
  const openRef = useRef(null)
  openRef.current = open

  const close = useCallback((refocus = true) => {
    const current = openRef.current
    if (!current) return
    current.el.removeAttribute('aria-expanded')
    setOpen(null)
    if (refocus && document.contains(current.el)) current.el.focus({ preventScroll: true })
  }, [])

  const show = useCallback((kind, el) => {
    el.setAttribute('aria-expanded', 'true')
    setOpen({ kind, el })
  }, [])

  useEffect(() => {
    if (canReplaceDatePicker) document.documentElement.classList.add('sc-custom-date')
    const onPointerDown = (event) => {
      const target = event.target
      const current = openRef.current
      if (current && (current.el === target || target.closest?.('.ncx-panel'))) {
        if (current.el === target && current.kind === 'select') { event.preventDefault(); close() }
        return
      }
      if (current) close(false)
      // Touch screens keep the phone's own picker sheet, which is the better experience there.
      if (event.button !== 0 || event.sourceCapabilities?.firesTouchEvents) return
      if (isSelect(target)) {
        event.preventDefault() // stops the browser's own list from opening
        target.focus({ preventScroll: true })
        show('select', target)
      }
    }
    const onClick = (event) => {
      const target = event.target
      if (canReplaceDatePicker && isDate(target) && event.pointerType !== 'touch' && !event.sourceCapabilities?.firesTouchEvents) {
        event.preventDefault() // stops the browser's own calendar from opening
        if (openRef.current?.el !== target) show('date', target)
      }
    }
    const onKeyDown = (event) => {
      const target = event.target
      if (openRef.current) return // the open panel handles its own keys
      const opener = event.key === ' ' || event.key === 'Enter' || event.key === 'F4' || (event.altKey && event.key === 'ArrowDown')
      if (opener && isSelect(target)) { event.preventDefault(); show('select', target) }
      else if ((event.key === 'F4' || (event.altKey && event.key === 'ArrowDown')) && canReplaceDatePicker && isDate(target)) { event.preventDefault(); show('date', target) }
    }
    document.addEventListener('mousedown', onPointerDown, true)
    document.addEventListener('click', onClick, true)
    document.addEventListener('keydown', onKeyDown, true)
    return () => {
      document.removeEventListener('mousedown', onPointerDown, true)
      document.removeEventListener('click', onClick, true)
      document.removeEventListener('keydown', onKeyDown, true)
    }
  }, [close, show])

  if (!open) return null
  return createPortal(open.kind === 'select'
    ? <SelectPanel key="s" el={open.el} onClose={close} />
    : <DatePanel key="d" el={open.el} onClose={close} />, document.body)
}

// Keeps the panel attached to its field while the page scrolls or resizes, and closes it if the field goes away.
function useFollow(el, height, minWidth, onClose) {
  const [pos, setPos] = useState(() => place(el, height, minWidth))
  useLayoutEffect(() => {
    let frame = 0
    const update = () => {
      frame = 0
      if (!document.contains(el) || el.offsetParent === null) { onClose(false); return }
      setPos(place(el, height, minWidth))
    }
    const schedule = () => { if (!frame) frame = requestAnimationFrame(update) }
    window.addEventListener('scroll', schedule, true)
    window.addEventListener('resize', schedule)
    return () => { cancelAnimationFrame(frame); window.removeEventListener('scroll', schedule, true); window.removeEventListener('resize', schedule) }
  }, [el, height, minWidth, onClose])
  return pos
}

function SelectPanel({ el, onClose }) {
  const options = [...el.options].filter((o) => !o.hidden)
  const [active, setActive] = useState(() => Math.max(0, options.findIndex((o) => o.selected)))
  const listRef = useRef(null)
  const pos = useFollow(el, Math.min(320, options.length * 40 + 12), 180, onClose)
  const typed = useRef({ text: '', at: 0 })

  const choose = (index) => {
    const option = options[index]
    if (!option || option.disabled) return
    if (el.value !== option.value) writeValue(el, option.value)
    onClose()
  }
  const step = (from, dir) => {
    for (let i = from + dir; i >= 0 && i < options.length; i += dir) if (!options[i].disabled) return i
    return from
  }

  useEffect(() => { listRef.current?.querySelector(`[data-i="${active}"]`)?.scrollIntoView({ block: 'nearest' }) }, [active])

  useEffect(() => {
    const onKey = (event) => {
      if (event.target !== el) return
      const keys = { ArrowDown: () => setActive((a) => step(a, 1)), ArrowUp: () => setActive((a) => step(a, -1)),
        Home: () => setActive(step(-1, 1)), End: () => setActive(step(options.length, -1)),
        Enter: () => choose(active), ' ': () => choose(active), Escape: () => onClose(), Tab: () => onClose(false) }
      if (keys[event.key]) { if (event.key !== 'Tab') event.preventDefault(); keys[event.key](); return }
      if (event.key.length === 1 && !event.ctrlKey && !event.metaKey) {
        // Typing jumps to the first option that starts with the typed letters.
        const now = Date.now()
        typed.current = { text: (now - typed.current.at < 700 ? typed.current.text : '') + event.key.toLowerCase(), at: now }
        const hit = options.findIndex((o) => !o.disabled && o.text.trim().toLowerCase().startsWith(typed.current.text))
        if (hit >= 0) setActive(hit)
        event.preventDefault()
      }
    }
    document.addEventListener('keydown', onKey, true)
    return () => document.removeEventListener('keydown', onKey, true)
  })

  return <div className={`ncx-panel ncx-list${pos.up ? ' is-up' : ''}`} role="listbox" ref={listRef}
    style={{ left: pos.left, width: pos.width, top: pos.top, bottom: pos.bottom, maxHeight: Math.min(320, pos.maxHeight) }}
    aria-label={el.getAttribute('aria-label') || el.labels?.[0]?.textContent || undefined}>
    {options.map((o, i) => <div key={i} data-i={i} role="option" aria-selected={o.selected} aria-disabled={o.disabled || undefined}
      className={`ncx-option${i === active ? ' is-active' : ''}${o.selected ? ' is-selected' : ''}${o.disabled ? ' is-disabled' : ''}`}
      onPointerMove={() => !o.disabled && setActive(i)} onMouseDown={(e) => e.preventDefault()} onClick={() => choose(i)}>
      <span>{o.text}</span>{o.selected && <Check size={16} aria-hidden="true" />}
    </div>)}
  </div>
}

function DatePanel({ el, onClose }) {
  const today = new Date(); today.setHours(0, 0, 0, 0)
  const selected = fromIso(el.value)
  const min = el.min || '', max = el.max || ''
  const allowed = (d) => (!min || toIso(d) >= min) && (!max || toIso(d) <= max)
  const [focus, setFocus] = useState(() => selected || (min && fromIso(min) > today ? fromIso(min) : max && fromIso(max) < today ? fromIso(max) : today))
  const [view, setView] = useState(() => new Date(focus.getFullYear(), focus.getMonth(), 1))
  const pos = useFollow(el, 360, 300, onClose)

  const move = (d) => { setFocus(d); if (d.getMonth() !== view.getMonth() || d.getFullYear() !== view.getFullYear()) setView(new Date(d.getFullYear(), d.getMonth(), 1)) }
  const addDays = (d, n) => new Date(d.getFullYear(), d.getMonth(), d.getDate() + n)
  const addMonths = (d, n) => { const r = new Date(d.getFullYear(), d.getMonth() + n, 1); r.setDate(Math.min(d.getDate(), new Date(r.getFullYear(), r.getMonth() + 1, 0).getDate())); return r }
  const pick = (d) => { if (!allowed(d)) return; writeValue(el, toIso(d)); onClose() }

  useEffect(() => {
    const onKey = (event) => {
      if (event.target !== el) return
      const keys = { ArrowLeft: () => move(addDays(focus, -1)), ArrowRight: () => move(addDays(focus, 1)), ArrowUp: () => move(addDays(focus, -7)),
        ArrowDown: () => move(addDays(focus, 7)), PageUp: () => move(addMonths(focus, -1)), PageDown: () => move(addMonths(focus, 1)),
        Enter: () => pick(focus), Escape: () => onClose(), Tab: () => onClose(false) }
      if (keys[event.key]) { if (event.key !== 'Tab') event.preventDefault(); keys[event.key]() }
    }
    document.addEventListener('keydown', onKey, true)
    return () => document.removeEventListener('keydown', onKey, true)
  })

  // Weeks start on Saturday, as in Bangladesh.
  const first = new Date(view.getFullYear(), view.getMonth(), 1)
  const lead = (first.getDay() + 1) % 7
  const start = addDays(first, -lead)
  const days = Array.from({ length: 42 }, (_, i) => addDays(start, i))
  const weekdays = Array.from({ length: 7 }, (_, i) => new Intl.DateTimeFormat(locale(), { weekday: 'short' }).format(new Date(2024, 0, 6 + i)))
  const monthLabel = new Intl.DateTimeFormat(locale(), { month: 'long', year: 'numeric' }).format(view)
  const dayLabel = (d) => new Intl.DateTimeFormat(locale(), { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }).format(d)
  const num = (n) => new Intl.NumberFormat(locale()).format(n)
  const prevOk = !min || toIso(addDays(first, -1)) >= min
  const nextOk = !max || toIso(new Date(view.getFullYear(), view.getMonth() + 1, 1)) <= max

  return <div className={`ncx-panel ncx-cal${pos.up ? ' is-up' : ''}`} role="dialog" aria-label={t('Choose a date')}
    style={{ left: pos.left, width: 300, top: pos.top, bottom: pos.bottom }} onMouseDown={(e) => e.preventDefault()}>
    <div className="ncx-cal-head">
      <button type="button" className="ncx-cal-nav" onClick={() => setView(addMonths(view, -1))} disabled={!prevOk} aria-label={t('Previous month')}><ChevronLeft size={17} /></button>
      <b aria-live="polite">{monthLabel}</b>
      <button type="button" className="ncx-cal-nav" onClick={() => setView(addMonths(view, 1))} disabled={!nextOk} aria-label={t('Next month')}><ChevronRight size={17} /></button>
    </div>
    <div className="ncx-cal-grid" role="grid">
      {weekdays.map((w, i) => <span key={`w${i}`} className={`ncx-cal-wd${i === 6 ? ' is-weekend' : ''}`} role="columnheader">{w}</span>)}
      {days.map((d) => {
        const iso = toIso(d)
        const outside = d.getMonth() !== view.getMonth()
        const ok = allowed(d)
        const cls = ['ncx-day', outside && 'is-outside', !ok && 'is-disabled', iso === toIso(today) && 'is-today',
          selected && iso === toIso(selected) && 'is-selected', iso === toIso(focus) && 'is-focus', d.getDay() === 5 && 'is-weekend'].filter(Boolean).join(' ')
        return <button type="button" key={iso} className={cls} disabled={!ok} aria-label={dayLabel(d)} aria-pressed={selected ? iso === toIso(selected) : false}
          onClick={() => pick(d)}>{num(d.getDate())}</button>
      })}
    </div>
    <div className="ncx-cal-foot">
      <button type="button" className="ncx-cal-link" disabled={!allowed(today)} onClick={() => pick(today)}>{t('Today')}</button>
      {!el.required && el.value && <button type="button" className="ncx-cal-link muted" onClick={() => { writeValue(el, ''); onClose() }}>{t('Clear')}</button>}
    </div>
  </div>
}
