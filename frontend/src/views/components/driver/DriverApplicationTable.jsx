import React from 'react'
import { ArrowRight, BusFront, Inbox, Loader2 } from 'lucide-react'
import { t } from '../../../models/i18n'
import { dayTime } from '../../../models/driver'
import { VerificationStatusBadge } from './VerificationStatusBadge'

// The admin's list of driver applications. On narrow screens each row becomes a card (see driver.css).
export function DriverApplicationTable({ rows, loading, onOpen, emptyText }) {
  if (loading && !rows.length) return <div className="dv-center"><Loader2 size={20} className="dv-spin" /> {t('Loading applications…')}</div>
  if (!rows.length) return <div className="dv-empty"><span><Inbox size={24} /></span><b>{t('No applications found')}</b><small>{emptyText}</small></div>
  return <div className={`dv-table${loading ? ' is-loading' : ''}`} role="table" aria-label={t('Driver applications')}>
    <div className="dv-tr dv-thead" role="row">
      {['Driver', 'Phone', 'Licence', 'Bus / operator', 'Submitted', 'Status', ''].map((h, i) => <span key={i} role="columnheader">{h ? t(h) : <span className="sr-only">{t('Action')}</span>}</span>)}
    </div>
    {rows.map((row) => <div className="dv-tr" role="row" key={row.driverId}>
      <span role="cell" className="dv-td-driver"><span className="dv-avatar">{(row.fullName || '?')[0].toUpperCase()}</span>
        <span><b>{row.fullName}</b><small>{row.email}</small><small className="dv-code">{row.applicationCode}</small></span></span>
      <span role="cell" data-label={t('Phone')}>{row.phone || '—'}</span>
      <span role="cell" data-label={t('Licence')} className="dv-mono">{row.licenseNumberMasked}</span>
      <span role="cell" data-label={t('Bus / operator')} className="dv-td-bus"><BusFront size={14} aria-hidden="true" /><span><b>{t(row.busName)}</b><small>{row.operatorName}</small></span></span>
      <span role="cell" data-label={t('Submitted')}>{dayTime(row.submittedAt)}</span>
      <span role="cell" data-label={t('Status')}><VerificationStatusBadge status={row.verificationStatus} /></span>
      <span role="cell" className="dv-td-action"><button type="button" className="button button-outline button-small" onClick={() => onOpen(row.driverId)}
        aria-label={t('View details for {name}', { name: row.fullName })}>{t('View details')} <ArrowRight size={14} /></button></span>
    </div>)}
  </div>
}
