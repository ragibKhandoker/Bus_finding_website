import React, { useState } from 'react'
import { Ban, CheckCircle2, Clock3, Navigation, OctagonAlert } from 'lucide-react'
import { formatNumber, t } from '../../../../models/i18n'
import { TRIP_STATUS_LABELS } from '../../../../models/driverTrips'

// A single-series bar chart (completed trips per period). One series, so no legend box: the title names it.
// Thin bars with rounded tops on a recessive grid, one direct label on the highest bar, a hover/focus tooltip on
// every bar, and the same numbers in a table for screen readers.
export function DriverStatisticsChart({ title, rows, valueKey = 'completed', labelOf, detailOf }) {
  const [hover, setHover] = useState(null)
  const max = Math.max(1, ...rows.map((r) => r[valueKey]))
  const top = Math.ceil(max / 2) * 2
  const peak = rows.reduce((best, r, i) => (r[valueKey] > (rows[best]?.[valueKey] ?? -1) ? i : best), 0)
  return <figure className="do-chart">
    <figcaption>{t(title)}</figcaption>
    <div className="do-chart-plot" role="img" aria-label={t('{title}: {list}', { title: t(title), list: rows.map((r) => `${labelOf(r)} ${r[valueKey]}`).join(', ') })}>
      <div className="do-chart-grid" aria-hidden="true">{[top, top / 2, 0].map((v) => <span key={v}><em>{formatNumber(v)}</em></span>)}</div>
      <div className="do-chart-bars">
        {rows.map((r, i) => <div key={i} className="do-chart-col" onMouseEnter={() => setHover(i)} onMouseLeave={() => setHover(null)}
          onFocus={() => setHover(i)} onBlur={() => setHover(null)} tabIndex={0}>
          <span className="do-chart-bar" style={{ height: `${(r[valueKey] / top) * 100}%` }}>
            {i === peak && r[valueKey] > 0 && <b className="do-chart-label">{formatNumber(r[valueKey])}</b>}
          </span>
          {hover === i && <span className="do-chart-tip" role="tooltip"><b>{labelOf(r)}</b>{detailOf ? detailOf(r) : t('{n} completed', { n: r[valueKey] })}</span>}
          <small>{labelOf(r, true)}</small>
        </div>)}
      </div>
    </div>
    <table className="sr-only"><caption>{t(title)}</caption><tbody>{rows.map((r, i) => <tr key={i}><th>{labelOf(r)}</th><td>{r[valueKey]}</td></tr>)}</tbody></table>
  </figure>
}

const STATUS_ICONS = { COMPLETED: CheckCircle2, MISSED: OctagonAlert, CANCELLED: Ban, SCHEDULED: Clock3, IN_PROGRESS: Navigation }

// Trip status distribution as labelled horizontal bars: each row carries its icon, name and count, so the
// status colours are never the only signal.
export function StatusDistribution({ counts }) {
  const entries = ['COMPLETED', 'MISSED', 'CANCELLED', 'SCHEDULED', 'IN_PROGRESS'].map((s) => [s, counts[s] || 0])
  const total = entries.reduce((sum, [, n]) => sum + n, 0)
  return <figure className="do-chart">
    <figcaption>{t('Trip status distribution')}</figcaption>
    <ul className="do-dist">{entries.map(([status, n]) => {
      const Icon = STATUS_ICONS[status]
      const share = total ? Math.round((n / total) * 100) : 0
      return <li key={status} className={`is-${status.toLowerCase().replace(/_/g, '-')}`}>
        <span className="do-dist-name"><Icon size={14} aria-hidden="true" /> {t(TRIP_STATUS_LABELS[status])}</span>
        <span className="do-dist-track"><i style={{ width: `${share}%` }} /></span>
        <span className="do-dist-value"><b>{formatNumber(n)}</b> <small>{formatNumber(share)}%</small></span>
      </li>
    })}</ul>
  </figure>
}
