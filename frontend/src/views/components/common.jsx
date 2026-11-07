import React, { useEffect, useMemo, useRef, useState } from 'react'
import { ArrowRight, Check, Moon, MoonStar, ShieldCheck, Sun } from 'lucide-react'
import { t } from '../../models/i18n'
import { useBusRoutes } from '../../controllers/useBusRoutes'

// swatch: a preview of each theme's own colours, so it is styled inline and does not follow the current theme.
export const THEME_OPTIONS = [
  { value: 'light', icon: Sun, label: 'Light', text: 'Bright and clear, for daytime', swatch: { background: '#ffffff', color: '#d98a00', borderColor: '#e2e9e4' } },
  { value: 'dark', icon: Moon, label: 'Dark', text: 'Dim colours, easy on the eyes', swatch: { background: '#1a1e1c', color: '#cbe6db', borderColor: '#36403b' } },
  { value: 'night', icon: MoonStar, label: 'Night', text: 'Deep blue with warm text for late hours', swatch: { background: '#0d111e', color: '#ded4bf', borderColor: '#2b324c' } },
]

export function ThemeMenu({ theme, onChange }) {
  const [open, setOpen] = useState(false)
  const ref = useRef(null)
  // Clicking anywhere else or pressing Escape closes the menu.
  useEffect(() => {
    if (!open) return
    const close = (event) => { if (event.type === 'keydown' ? event.key === 'Escape' : !ref.current?.contains(event.target)) setOpen(false) }
    document.addEventListener('mousedown', close)
    document.addEventListener('keydown', close)
    return () => { document.removeEventListener('mousedown', close); document.removeEventListener('keydown', close) }
  }, [open])
  const Current = THEME_OPTIONS.find((option) => option.value === theme)?.icon || Sun
  return <div className="theme-switch" ref={ref}>
    <button className="theme-toggle" aria-haspopup="menu" aria-expanded={open} title={t('Change theme')} aria-label={t('Change theme')} onClick={() => setOpen(!open)}><Current size={17} /></button>
    {open && <div className="theme-menu" role="menu" aria-label={t('Theme')}>
      <small className="theme-menu-title">{t('Theme')}</small>
      {THEME_OPTIONS.map(({ value, icon: Icon, label, text, swatch }) => <button key={value} role="menuitemradio" aria-checked={theme === value} className={theme === value ? 'selected' : ''} onClick={() => { onChange(value); setOpen(false) }}>
        <span className="theme-swatch" style={swatch}><Icon size={16} /></span>
        <span><b>{t(label)}</b><small>{t(text)}</small></span>
        {theme === value && <Check size={16} className="theme-check" />}
      </button>)}
    </div>}
  </div>
}

// Shortcuts shown under the home search; each one runs the same search as typing the stops in.

// Every stop name in the database, offered as suggestions in the place inputs (input list="stop-suggestions").
// Stop names must match the database for route search, so picking one avoids spelling misses.
export function StopSuggestions() {
  const [routes] = useBusRoutes()
  const names = useMemo(() => routes.status === 'ready'
    ? [...new Set(routes.buses.flatMap((bus) => (bus.stops || []).map((stop) => stop.name)))].sort((a, b) => a.localeCompare(b))
    : [], [routes])
  return <datalist id="stop-suggestions">{names.map((name) => <option key={name} value={t(name)} />)}</datalist>
}

export function PageHeading({ eyebrow, title, description, icon: Icon }) {
  return <div className="page-heading"><span className="heading-icon">{Icon && <Icon size={21} />}</span><div><div className="eyebrow">{eyebrow}</div><h1>{title}</h1><p>{description}</p></div></div>
}

export function SignInPrompt({ openAuth, message }) {
  return <div className="signin-prompt panel"><span className="service-icon mint"><ShieldCheck size={20} /></span><div><b>{t('Good to have you here.')}</b><p>{message}</p></div><button className="button button-dark button-small" onClick={openAuth}>{t('Sign in')} <ArrowRight size={14} /></button></div>
}
