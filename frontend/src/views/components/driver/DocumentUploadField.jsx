import React, { useId, useRef, useState } from 'react'
import { CheckCircle2, FileImage, ImageUp, Loader2, RefreshCw, Trash2 } from 'lucide-react'
import { t } from '../../../models/i18n'
import { IMAGE_TYPES, MAX_IMAGE_BYTES, fileProblem, fileSize, readAsDataUrl } from '../../../models/driver'

// One document picker: drop or choose an image, see a preview, replace or remove it. The image is read in the
// browser as a data URL and only sent when the form is submitted. value: { data, name, size } or undefined.
export function DocumentUploadField({ label, hint, required = false, value, onChange, error, compact = false }) {
  const id = useId()
  const input = useRef(null)
  const [reading, setReading] = useState(false)
  const [problem, setProblem] = useState('')
  const [over, setOver] = useState(false)
  const shown = problem || error

  const take = async (file) => {
    const issue = fileProblem(file)
    if (issue) { setProblem(issue); return }
    setProblem(''); setReading(true)
    try {
      const data = await readAsDataUrl(file)
      onChange({ data, name: file.name, size: file.size, type: file.type })
    } catch (e) { setProblem(e.message) } finally { setReading(false); if (input.current) input.current.value = '' }
  }
  const onDrop = (event) => { event.preventDefault(); setOver(false); const file = event.dataTransfer?.files?.[0]; if (file) take(file) }

  return <div className={`dv-upload${value ? ' has-file' : ''}${shown ? ' has-error' : ''}${compact ? ' compact' : ''}`}>
    <div className="dv-upload-label" id={`${id}-label`}>
      <span>{t(label)}</span>{required ? <em className="dv-required">{t('Required')}</em> : <em>{t('Optional')}</em>}
    </div>
    <input ref={input} id={id} type="file" accept={IMAGE_TYPES.join(',')} className="sr-only" aria-labelledby={`${id}-label`}
      aria-describedby={`${id}-hint`} aria-invalid={Boolean(shown)} onChange={(event) => take(event.target.files?.[0])} />
    {value ? <div className="dv-upload-preview">
      <img src={value.data} alt={t('Preview of {label}', { label: t(label) })} />
      <div className="dv-upload-meta">
        <b><CheckCircle2 size={15} aria-hidden="true" /> {t('Ready to upload')}</b>
        <small title={value.name}>{value.name}</small>
        <small>{fileSize(value.size)}</small>
        <span className="dv-upload-actions">
          <button type="button" className="dv-chip" onClick={() => input.current?.click()}><RefreshCw size={13} /> {t('Replace')}</button>
          <button type="button" className="dv-chip danger" onClick={() => { onChange(undefined); setProblem('') }}><Trash2 size={13} /> {t('Remove')}</button>
        </span>
      </div>
    </div> : <label htmlFor={id} className={`dv-drop${over ? ' is-over' : ''}`}
      onDragOver={(event) => { event.preventDefault(); setOver(true) }} onDragLeave={() => setOver(false)} onDrop={onDrop}>
      <span className="dv-drop-icon">{reading ? <Loader2 size={22} className="dv-spin" /> : <ImageUp size={22} />}</span>
      <b>{reading ? t('Reading image…') : t('Click to upload or drag an image here')}</b>
      <small><FileImage size={13} aria-hidden="true" /> {t('JPG, PNG or WebP · up to {mb} MB', { mb: MAX_IMAGE_BYTES / (1024 * 1024) })}</small>
    </label>}
    <small id={`${id}-hint`} className={shown ? 'dv-error' : 'dv-hint'} role={shown ? 'alert' : undefined}>{shown || (hint ? t(hint) : '')}</small>
  </div>
}
