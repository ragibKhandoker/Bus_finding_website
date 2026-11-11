import React, { useEffect, useRef, useState } from 'react'
import { CircleAlert, FileWarning, Loader2, Minus, Plus, RotateCw, X, XCircle } from 'lucide-react'
import { t } from '../../../models/i18n'
import { RESUBMISSION_FIELDS, adminDriverApi } from '../../../models/driver'

// Dialogs used by the driver verification pages. Escape and the backdrop close them; focus moves into them.
export function DvModal({ title, subtitle, onClose, children, wide = false, tone = '' }) {
  const box = useRef(null)
  useEffect(() => {
    const onKey = (event) => { if (event.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    const overflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    box.current?.querySelector('textarea, input, button:not(.dv-modal-close)')?.focus?.()
    return () => { document.removeEventListener('keydown', onKey); document.body.style.overflow = overflow }
  }, [onClose])
  return <div className="dv-backdrop" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose() }}>
    <div ref={box} className={`dv-modal${wide ? ' wide' : ''}${tone ? ` is-${tone}` : ''}`} role="dialog" aria-modal="true" aria-label={title}>
      <header className="dv-modal-head"><div><h2>{title}</h2>{subtitle && <small>{subtitle}</small>}</div>
        <button type="button" className="dv-icon-button dv-modal-close" onClick={onClose} aria-label={t('Close')}><X size={18} /></button></header>
      <div className="dv-modal-body">{children}</div>
    </div>
  </div>
}

// Loads a private document with the admin's token; the object URL is released when it is no longer shown.
export function usePrivateImage(url, token) {
  const [state, setState] = useState({ status: 'loading' })
  useEffect(() => {
    if (!url) { setState({ status: 'error', error: t('Not uploaded') }); return undefined }
    let active = true
    let objectUrl = ''
    setState({ status: 'loading' })
    adminDriverApi.document(url, token)
      .then((blob) => { if (!active) return; objectUrl = URL.createObjectURL(blob); setState({ status: 'ready', src: objectUrl }) })
      .catch((e) => { if (active) setState({ status: 'error', error: t(e.message || 'Could not load the document.') }) })
    return () => { active = false; if (objectUrl) URL.revokeObjectURL(objectUrl) }
  }, [url, token])
  return state
}

export function DocumentPreviewModal({ document: doc, label, token, onClose }) {
  const image = usePrivateImage(doc?.url, token)
  const [zoom, setZoom] = useState(1)
  const [turn, setTurn] = useState(0)
  return <DvModal title={t(label)} subtitle={t('Private document · visible to administrators only')} onClose={onClose} wide>
    <div className="dv-viewer">
      {image.status === 'loading' && <div className="dv-center"><Loader2 size={22} className="dv-spin" /> {t('Loading document…')}</div>}
      {image.status === 'error' && <p className="dv-alert danger" role="alert"><CircleAlert size={16} /> {image.error}</p>}
      {image.status === 'ready' && <div className="dv-viewer-stage"><img src={image.src} alt={t(label)} style={{ transform: `scale(${zoom}) rotate(${turn}deg)` }} /></div>}
    </div>
    <div className="dv-viewer-tools">
      <button type="button" className="dv-chip" onClick={() => setZoom((z) => Math.max(0.5, z - 0.25))} aria-label={t('Zoom out')}><Minus size={14} /></button>
      <span>{Math.round(zoom * 100)}%</span>
      <button type="button" className="dv-chip" onClick={() => setZoom((z) => Math.min(3, z + 0.25))} aria-label={t('Zoom in')}><Plus size={14} /></button>
      <button type="button" className="dv-chip" onClick={() => setTurn((d) => (d + 90) % 360)}><RotateCw size={14} /> {t('Rotate')}</button>
    </div>
  </DvModal>
}

// A yes/no question before an action that cannot be undone.
export function ConfirmDialog({ title, message, confirmLabel, tone = 'green', busy, onConfirm, onClose, children }) {
  return <DvModal title={title} onClose={onClose} tone={tone}>
    <p className="dv-modal-text">{message}</p>
    {children}
    <div className="dv-modal-actions">
      <button type="button" className="button button-outline" onClick={onClose} disabled={busy}>{t('Cancel')}</button>
      <button type="button" className={`button ${tone === 'danger' ? 'dv-button-danger' : 'button-green'}`} onClick={onConfirm} disabled={busy}>
        {busy && <Loader2 size={15} className="dv-spin" />} {confirmLabel}
      </button>
    </div>
  </DvModal>
}

const TEMPLATES = {
  reject: ['The licence details do not match the uploaded documents.', 'The NID and licence belong to different people.', 'The driving licence has expired or is not valid for buses.'],
  resubmit: ['The document photo is blurry. Please upload a clear photo.', 'The licence number does not match the photo. Please check it.', 'Please upload the back side of the document.'],
}

function ReasonBox({ value, onChange, kind, error }) {
  return <div className="dv-field wide">
    <label htmlFor={`dv-reason-${kind}`}>{t('Reason')} <em>{t('(the driver will see this)')}</em></label>
    <textarea id={`dv-reason-${kind}`} className="dv-input dv-textarea" rows={4} maxLength={1000} value={value} onChange={(e) => onChange(e.target.value)}
      aria-invalid={Boolean(error)} placeholder={t('Explain clearly so the driver knows what to do.')} />
    <div className="dv-templates">{TEMPLATES[kind].map((text) => <button key={text} type="button" className="dv-chip" onClick={() => onChange(t(text))}>{t(text)}</button>)}</div>
    <small className={error ? 'dv-error' : 'dv-hint'} role={error ? 'alert' : undefined}>{error || t('{n}/1000 characters', { n: value.length })}</small>
  </div>
}

export function RejectApplicationModal({ name, busy, onSubmit, onClose }) {
  const [reason, setReason] = useState('')
  const [error, setError] = useState('')
  const [confirm, setConfirm] = useState(false)
  const go = () => { if (reason.trim().length < 5) { setError(t('Write a reason of at least 5 characters.')); return } setError(''); setConfirm(true) }
  if (confirm) {
    return <ConfirmDialog title={t('Reject this application?')} tone="danger" busy={busy} confirmLabel={t('Yes, reject')}
      message={t('{name} will be told the application was not approved, with your reason. This cannot be undone.', { name })}
      onConfirm={() => onSubmit(reason.trim())} onClose={() => setConfirm(false)}><blockquote className="dv-quote">{reason.trim()}</blockquote></ConfirmDialog>
  }
  return <DvModal title={t('Reject application')} subtitle={name} onClose={onClose} tone="danger">
    <p className="dv-modal-text"><XCircle size={16} aria-hidden="true" /> {t('Rejecting closes the application. Use “Request resubmission” instead if the driver can fix the problem.')}</p>
    <ReasonBox value={reason} onChange={(v) => { setReason(v); setError('') }} kind="reject" error={error} />
    <div className="dv-modal-actions">
      <button type="button" className="button button-outline" onClick={onClose}>{t('Cancel')}</button>
      <button type="button" className="button dv-button-danger" onClick={go}>{t('Continue')}</button>
    </div>
  </DvModal>
}

export function ResubmissionRequestModal({ name, busy, onSubmit, onClose }) {
  const [reason, setReason] = useState('')
  const [fields, setFields] = useState([])
  const [error, setError] = useState('')
  const toggle = (value) => setFields((list) => list.includes(value) ? list.filter((f) => f !== value) : [...list, value])
  const send = () => {
    if (!fields.length) { setError(t('Choose at least one item the driver must correct.')); return }
    if (reason.trim().length < 5) { setError(t('Write a reason of at least 5 characters.')); return }
    setError(''); onSubmit(reason.trim(), fields)
  }
  return <DvModal title={t('Request resubmission')} subtitle={name} onClose={onClose} tone="warn">
    <p className="dv-modal-text"><FileWarning size={16} aria-hidden="true" /> {t('The driver can change only the items you tick, then send the application back for review.')}</p>
    <fieldset className="dv-fieldset">
      <legend>{t('What needs correcting?')}</legend>
      <div className="dv-checks">{RESUBMISSION_FIELDS.map((f) => <label key={f.value} className={fields.includes(f.value) ? 'checked' : ''}>
        <input type="checkbox" checked={fields.includes(f.value)} onChange={() => toggle(f.value)} /> {t(f.label)}
      </label>)}</div>
    </fieldset>
    <ReasonBox value={reason} onChange={setReason} kind="resubmit" />
    {error && <p className="dv-alert danger" role="alert"><CircleAlert size={16} /> {error}</p>}
    <div className="dv-modal-actions">
      <button type="button" className="button button-outline" onClick={onClose} disabled={busy}>{t('Cancel')}</button>
      <button type="button" className="button dv-button-warn" onClick={send} disabled={busy}>{busy && <Loader2 size={15} className="dv-spin" />} {t('Send request')}</button>
    </div>
  </DvModal>
}
