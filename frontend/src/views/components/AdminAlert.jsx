import React, { useEffect } from 'react'
import { ArrowRight, BadgeCheck, Inbox, KeyRound, Siren, X } from 'lucide-react'
import { t } from '../../models/i18n'

const ICONS = { password: KeyRound, message: Inbox, emergency: Siren, verification: BadgeCheck }
// Ordinary alerts close by themselves; an emergency stays until the admin closes or opens it.
const AUTO_CLOSE_MS = 15000

// The pop-up an admin sees when something new arrives (see useAdminAlerts).
export function AdminAlert({ alert, onView, onClose }) {
  useEffect(() => {
    if (!alert || alert.kind === 'emergency') return undefined
    const timer = window.setTimeout(onClose, AUTO_CLOSE_MS)
    return () => window.clearTimeout(timer)
  }, [alert]) // eslint-disable-line react-hooks/exhaustive-deps
  if (!alert) return null
  const Icon = ICONS[alert.kind] || Inbox
  return <div className={`admin-alert ${alert.kind}`} role="alert" key={alert.id} data-testid="admin-alert">
    <span className="admin-alert-icon"><Icon size={20} /></span>
    <div className="admin-alert-body">
      <b>{alert.title}</b>
      {alert.text && <p>{alert.text}</p>}
      <button type="button" className="button button-small admin-alert-view" onClick={onView}>{t('View')} <ArrowRight size={14} /></button>
    </div>
    <button type="button" className="admin-alert-close" onClick={onClose} aria-label={t('Close')}><X size={16} /></button>
  </div>
}
