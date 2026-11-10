import React, { useEffect, useState } from 'react'
import { CheckCircle2, CircleDot, Clock3, ImageOff, Loader2, Search, X, XCircle } from 'lucide-react'
import { t } from '../../models/i18n'
import { STATUS_LABELS, complaintApi, dayTime } from '../../models/complaints'
import '../styles/complaints.css'

// F18: pieces shared by the user's complaint page and the admin's complaint management.

const STATUS_ICONS = { PENDING: Clock3, REVIEWING: Search, RESOLVED: CheckCircle2, REJECTED: XCircle }

export function StatusBadge({ status, large = false }) {
  const Icon = STATUS_ICONS[status] || CircleDot
  return <span className={`cp-status ${status?.toLowerCase()}${large ? ' large' : ''}`}><Icon size={large ? 15 : 13} /> {t(STATUS_LABELS[status] || status)}</span>
}

export function CpModal({ title, subtitle, onClose, children, wide = false }) {
  useEffect(() => {
    const onKey = (event) => { if (event.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    const overflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => { document.removeEventListener('keydown', onKey); document.body.style.overflow = overflow }
  }, [onClose])
  return <div className="cp-backdrop" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose() }}>
    <div className={`cp-modal${wide ? ' wide' : ''}`} role="dialog" aria-modal="true" aria-label={title}>
      <header className="cp-modal-head"><div><h2>{title}</h2>{subtitle && <small>{subtitle}</small>}</div>
        <button type="button" className="cp-icon" onClick={onClose} aria-label={t('Close')}><X size={18} /></button></header>
      <div className="cp-modal-body">{children}</div>
    </div>
  </div>
}

// The complaint's real history: submitted, then each status change with its note and time.
export function Timeline({ history, status, forAdmin = false }) {
  const steps = history || []
  const finished = status === 'RESOLVED' || status === 'REJECTED'
  return <ol className="cp-timeline" aria-label={t('Complaint timeline')}>
    {steps.map((h, i) => <li key={`${h.toStatus}-${h.createdAt}-${i}`} className={`done ${h.toStatus.toLowerCase()}`}>
      <span className="cp-dot" aria-hidden="true" />
      <div><b>{i === 0 ? t('Submitted') : t(STATUS_LABELS[h.toStatus] || h.toStatus)}</b>
        <small>{dayTime(h.createdAt)} · {h.actorRole === 'admin' || forAdmin ? h.actorName : t('You')}</small>
        {h.note && i > 0 && <p>{h.note}</p>}</div>
    </li>)}
    {!finished && <li className="todo">
      <span className="cp-dot" aria-hidden="true" />
      <div><b>{status === 'PENDING' ? t('Waiting for review') : t('Resolved or rejected')}</b><small>{t('Next step')}</small></div>
    </li>}
  </ol>
}

// A private evidence picture: fetched with the sign-in token, so it is never reachable by a plain link.
export function EvidenceImage({ url, token, onOpen }) {
  const [src, setSrc] = useState('')
  const [failed, setFailed] = useState(false)
  useEffect(() => {
    let live = true
    let objectUrl = ''
    complaintApi.evidence(url, token).then((blob) => { if (!live) return; objectUrl = URL.createObjectURL(blob); setSrc(objectUrl) }).catch(() => { if (live) setFailed(true) })
    return () => { live = false; if (objectUrl) URL.revokeObjectURL(objectUrl) }
  }, [url, token])
  if (failed) return <span className="cp-evidence missing"><ImageOff size={18} /> {t('Picture unavailable')}</span>
  if (!src) return <span className="cp-evidence loading"><Loader2 size={18} className="cp-spin" /></span>
  return <button type="button" className="cp-evidence" onClick={() => onOpen?.(src)} aria-label={t('Open evidence picture')}><img src={src} alt={t('Evidence picture')} /></button>
}

export function EvidenceGallery({ evidence, token }) {
  const [viewing, setViewing] = useState('')
  if (!evidence?.length) return <p className="cp-muted">{t('No pictures were added.')}</p>
  return <>
    <div className="cp-gallery">{evidence.map((e) => <EvidenceImage key={e.id} url={e.url} token={token} onOpen={setViewing} />)}</div>
    {viewing && <div className="cp-viewer" role="dialog" aria-label={t('Evidence picture')} onClick={() => setViewing('')}>
      <img src={viewing} alt={t('Evidence picture')} /><button type="button" className="cp-icon" onClick={() => setViewing('')} aria-label={t('Close')}><X size={18} /></button>
    </div>}
  </>
}
