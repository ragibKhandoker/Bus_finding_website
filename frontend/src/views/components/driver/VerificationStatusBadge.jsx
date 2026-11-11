import React from 'react'
import { BadgeCheck, CircleX, Clock3, FileWarning, Search } from 'lucide-react'
import { t } from '../../../models/i18n'
import { STATUS_LABELS } from '../../../models/driver'

const ICONS = { PENDING: Clock3, UNDER_REVIEW: Search, APPROVED: BadgeCheck, REJECTED: CircleX, RESUBMISSION_REQUIRED: FileWarning }

// The colour-coded status of a driver application (pending amber, under review blue, approved green,
// rejected red, resubmission orange). The text is always there too, so colour is never the only signal.
export function VerificationStatusBadge({ status, large = false }) {
  const Icon = ICONS[status] || Clock3
  return <span className={`dv-badge is-${String(status || 'PENDING').toLowerCase().replace(/_/g, '-')}${large ? ' large' : ''}`}>
    <Icon size={large ? 16 : 13} aria-hidden="true" /> {t(STATUS_LABELS[status] || status || 'Pending')}
  </span>
}
