import React, { useCallback, useEffect, useRef, useState } from 'react'
import { BadgeCheck, Check, Clock, Eye, FileImage, RefreshCw, ShieldCheck, X } from 'lucide-react'
import { apiBlob, apiRequest } from './api'
import { formatDate, t } from './i18n'
import { Feedback, Heading, Loading, RequireSignIn } from './MorePages'

// Account verification: upload an NID or student pass photo and follow its review; admins review the queue.
// Document photos are private: they are fetched with the sign-in token and shown from memory, never by public URL.

export const MAX_IMAGE_BYTES = 5 * 1024 * 1024
export const IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/webp']
const errorText = (error) => t(error?.message || 'Something went wrong. Please try again.')
const post = (path, token, body) => apiRequest(path, { token, method: 'POST', body: JSON.stringify(body) })

export const verifyApi = {
  status: (token) => apiRequest('/verify/my-status', { token }),
  upload: (dataUrl, token) => post('/verify/upload-image', token, { image: dataUrl }),
  submit: (body, token) => post('/verify/submit', token, body),
  all: (status, token) => apiRequest(`/verify/all?status=${encodeURIComponent(status)}&pageSize=50`, { token }),
  approve: (id, token) => post('/verify/approve', token, { verificationId: id }),
  reject: (id, reason, token) => post('/verify/reject', token, { verificationId: id, reason }),
  image: (path, token) => apiBlob(path, { token }),
}

export function fileProblem(file) {
  if (!file) return t('Choose a photo of your document.')
  if (!IMAGE_TYPES.includes(file.type)) return t('Only JPEG, PNG or WebP images are allowed.')
  if (file.size > MAX_IMAGE_BYTES) return t('The image must be at most {mb} MB.', { mb: MAX_IMAGE_BYTES / (1024 * 1024) })
  if (file.size === 0) return t('The image is empty.')
  return ''
}

const readAsDataUrl = (file) => new Promise((resolve, reject) => {
  const reader = new FileReader()
  reader.onload = () => resolve(reader.result)
  reader.onerror = () => reject(new Error(t('That image could not be read.')))
  reader.readAsDataURL(file)
})

const DOC_LABELS = { student_pass: 'Student pass', nid: 'NID' }
const STATE_LABELS = { pending: 'Pending', approved: 'Approved', rejected: 'Rejected' }

function StateBadge({ state }) {
  const Icon = state === 'approved' ? BadgeCheck : state === 'rejected' ? X : Clock
  return <span className={`status-badge ${state === 'pending' ? 'hold' : state}`}><Icon size={13} aria-hidden="true" /> {t(STATE_LABELS[state] || state)}</span>
}

// Loads a private document photo with the token and shows it; the object URL is released when it goes away.
export function PrivateImage({ path, token, alt }) {
  const [url, setUrl] = useState('')
  const [error, setError] = useState('')
  useEffect(() => {
    let active = true
    let objectUrl = ''
    verifyApi.image(path, token)
      .then((blob) => { if (!active) return; objectUrl = URL.createObjectURL(blob); setUrl(objectUrl) })
      .catch((e) => { if (active) setError(errorText(e)) })
    return () => { active = false; if (objectUrl) URL.revokeObjectURL(objectUrl) }
  }, [path, token])
  if (error) return <p className="review-error" role="alert">{error}</p>
  return url ? <img className="verify-preview large" src={url} alt={alt} /> : <Loading label="Loading document…" />
}

function SubmitForm({ token, onSubmitted, notify }) {
  const [type, setType] = useState('student_pass')
  const [form, setForm] = useState({ institutionName: '', institutionId: '', gmail: '' })
  const [file, setFile] = useState(null)
  const [preview, setPreview] = useState('')
  const [error, setError] = useState('')
  const [step, setStep] = useState('') // '' | 'uploading' | 'submitting'
  const inputRef = useRef(null)

  const choose = async (event) => {
    const chosen = event.target.files?.[0] || null
    setError('')
    const problem = fileProblem(chosen)
    if (problem) { setFile(null); setPreview(''); setError(problem); event.target.value = ''; return }
    try { setPreview(await readAsDataUrl(chosen)); setFile(chosen) } catch (e) { setError(errorText(e)) }
  }
  const clearFile = () => { setFile(null); setPreview(''); if (inputRef.current) inputRef.current.value = '' }

  const submit = async (event) => {
    event.preventDefault()
    if (step) return
    if (type === 'student_pass' && (!form.institutionName.trim() || !form.institutionId.trim())) { setError(t("Enter your institution's name and your student ID.")); return }
    const problem = fileProblem(file)
    if (problem) { setError(problem); return }
    setError('')
    try {
      setStep('uploading')
      const uploaded = await verifyApi.upload(preview, token)
      setStep('submitting')
      const body = type === 'student_pass'
        ? { documentType: type, imageId: uploaded.imageId, institutionName: form.institutionName.trim(), institutionId: form.institutionId.trim(), gmail: form.gmail.trim() || undefined }
        : { documentType: type, imageId: uploaded.imageId }
      await verifyApi.submit(body, token)
      notify(t('Verification submitted for review.'))
      clearFile()
      onSubmitted()
    } catch (e) { setError(errorText(e)) } finally { setStep('') }
  }

  return <form className="extra-panel verify-form" onSubmit={submit} noValidate aria-label={t('Submit a document')}>
    <h2>{t('Submit a document')}</h2>
    <fieldset className="verify-types">
      <legend>{t('Document type')}</legend>
      {['student_pass', 'nid'].map((value) => <label key={value} className={type === value ? 'active' : ''}>
        <input type="radio" name="doc-type" value={value} checked={type === value} onChange={() => { setType(value); setError('') }} /> {t(DOC_LABELS[value])}
      </label>)}
    </fieldset>
    {type === 'student_pass' && <>
      <label>{t('Institution name')}<input value={form.institutionName} maxLength={200} onChange={(e) => setForm({ ...form, institutionName: e.target.value })} /></label>
      <label>{t('Student ID')}<input value={form.institutionId} maxLength={100} onChange={(e) => setForm({ ...form, institutionId: e.target.value })} /></label>
      <label>{t('Institution email (optional)')}<input type="email" value={form.gmail} maxLength={255} onChange={(e) => setForm({ ...form, gmail: e.target.value })} /></label>
    </>}
    <label>{t(type === 'nid' ? 'Photo of your NID' : 'Photo of your student ID card')}
      <input ref={inputRef} type="file" accept={IMAGE_TYPES.join(',')} onChange={choose} />
    </label>
    <small className="verify-note">{t('JPEG, PNG or WebP, up to {mb} MB. Only you and SmartCommute admins can see it.', { mb: MAX_IMAGE_BYTES / (1024 * 1024) })}</small>
    {preview && <div className="verify-chosen"><img className="verify-preview" src={preview} alt={t('Your chosen document photo')} /><button type="button" className="extra-button secondary" onClick={clearFile}>{t('Remove photo')}</button></div>}
    {error && <p className="review-error" role="alert">{error}</p>}
    <button type="submit" className="extra-button" disabled={Boolean(step)}>{step === 'uploading' ? t('Uploading photo…') : step === 'submitting' ? t('Submitting…') : t('Submit for review')}</button>
  </form>
}

function MyVerification({ token, notify }) {
  const [data, setData] = useState(null)
  const [status, setStatus] = useState('loading')
  const [error, setError] = useState('')
  const load = useCallback(async () => {
    setStatus('loading')
    try { setData(await verifyApi.status(token)); setStatus('ready'); setError('') } catch (e) { setStatus('error'); setError(errorText(e)) }
  }, [token])
  useEffect(() => { load() }, [load])

  if (status === 'loading') return <Loading label="Loading your verification…" />
  if (status === 'error') return <div className="extra-panel"><Feedback error={error} /><button type="button" className="extra-button" onClick={load}><RefreshCw size={14} /> {t('Try again')}</button></div>
  const latest = data?.data
  const pending = latest?.state === 'pending'
  const canSubmit = !pending && !(data?.studentPass?.verified && data?.nid?.verified)
  return <div className="verify-layout">
    <article className="extra-panel verify-status" data-testid="my-verification">
      <small>{t('YOUR STATUS')}</small>
      <div className="verify-flags">
        <span className={data?.studentPass?.verified ? 'on' : ''}><BadgeCheck size={14} aria-hidden="true" /> {t('Student')}: {data?.studentPass?.verified ? t('Verified') : t('Not verified')}</span>
        <span className={data?.nid?.verified ? 'on' : ''}><ShieldCheck size={14} aria-hidden="true" /> {t('NID')}: {data?.nid?.verified ? t('Verified') : t('Not verified')}</span>
      </div>
      {latest ? <>
        <p>{t('Latest request')}: <b>{t(DOC_LABELS[latest.documentType] || latest.documentType)}</b> · {formatDate(latest.createdAt)}</p>
        <StateBadge state={latest.state} />
        {latest.state === 'pending' && <p>{t('An admin will review your document soon.')}</p>}
        {latest.state === 'approved' && <p>{t('Your document was approved.')}</p>}
        {latest.state === 'rejected' && <p className="verify-reason">{t('Reason')}: {latest.rejectionReason || t('No reason was given.')} {t('You can submit a new photo.')}</p>}
        {latest.hasImage && <details className="verify-own-image"><summary><Eye size={14} aria-hidden="true" /> {t('See the photo you sent')}</summary><PrivateImage path={latest.imageUrl} token={token} alt={t('The document photo you sent')} /></details>}
      </> : <p>{t('You have not submitted a document yet.')}</p>}
    </article>
    {canSubmit ? <SubmitForm token={token} notify={notify} onSubmitted={load} /> : <article className="extra-panel"><p>{pending ? t('You can send another document after this one is reviewed.') : t('You are fully verified.')}</p></article>}
  </div>
}

function ReviewQueue({ token, notify }) {
  const [filter, setFilter] = useState('pending')
  const [items, setItems] = useState([])
  const [pendingCount, setPendingCount] = useState(0)
  const [status, setStatus] = useState('loading')
  const [error, setError] = useState('')
  const [open, setOpen] = useState('') // id whose photo is shown
  const [rejecting, setRejecting] = useState('') // id whose reason box is shown
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState('')

  const load = useCallback(async () => {
    setStatus('loading')
    try { const data = await verifyApi.all(filter, token); setItems(data.data || []); setPendingCount(data.pending ?? 0); setStatus('ready'); setError('') }
    catch (e) { setStatus('error'); setError(errorText(e)) }
  }, [filter, token])
  useEffect(() => { load() }, [load])

  const decide = async (item, approve) => {
    if (busy) return
    if (!approve && reason.trim().length < 3) { setError(t('Give a reason so the user knows what to fix.')); return }
    setBusy(item._id); setError('')
    try {
      await (approve ? verifyApi.approve(item._id, token) : verifyApi.reject(item._id, reason.trim(), token))
      notify(approve ? t('Verification approved.') : t('Verification rejected.'))
      setRejecting(''); setReason('')
      await load()
    } catch (e) { setError(errorText(e)); await load() } finally { setBusy('') }
  }

  return <article className="extra-panel extra-list-panel verify-queue" aria-label={t('Verification review')}>
    <div className="extra-panel-title"><div><small>{t('ADMIN REVIEW')}</small><h2>{t('Verification requests')} <span>{t('{count} pending', { count: pendingCount })}</span></h2></div>
      <button type="button" className="extra-icon-button" onClick={load} aria-label={t('Refresh')}><RefreshCw size={15} /></button></div>
    <div className="verify-filters" role="tablist">{['pending', 'approved', 'rejected', 'all'].map((value) => <button key={value} type="button" role="tab" aria-selected={filter === value} className={filter === value ? 'active' : ''} onClick={() => setFilter(value)}>{t(STATE_LABELS[value] || 'All')}</button>)}</div>
    {error && <p className="review-error" role="alert">{error}</p>}
    {status === 'loading' ? <Loading />
      : status === 'error' ? <button type="button" className="extra-button" onClick={load}><RefreshCw size={14} /> {t('Try again')}</button>
        : items.length ? items.map((item) => <div className="verification-row" key={item._id} data-testid="verification-request">
          <div>
            <b>{item.userName || t('Applicant')} · {t(DOC_LABELS[item.documentType] || item.documentType)}</b>
            <small>{item.userEmail}{item.documentType === 'student_pass' ? ` · ${item.institutionName} · ${t('ID')} ${item.institutionId}` : ''} · {formatDate(item.createdAt)}</small>
            {item.state === 'rejected' && item.rejectionReason && <small>{t('Reason')}: {item.rejectionReason}</small>}
          </div>
          <StateBadge state={item.state} />
          {item.hasImage && <button type="button" className="extra-button secondary" onClick={() => setOpen(open === item._id ? '' : item._id)} aria-expanded={open === item._id}><FileImage size={14} aria-hidden="true" /> {open === item._id ? t('Hide photo') : t('View photo')}</button>}
          {item.state === 'pending' && <>
            <button type="button" className="extra-button" disabled={busy === item._id} onClick={() => decide(item, true)}><Check size={14} aria-hidden="true" /> {busy === item._id ? t('Saving…') : t('Approve')}</button>
            <button type="button" className="extra-button secondary danger" disabled={busy === item._id} onClick={() => { setRejecting(rejecting === item._id ? '' : item._id); setReason('') }}><X size={14} aria-hidden="true" /> {t('Reject')}</button>
          </>}
          {open === item._id && <div className="verify-photo"><PrivateImage path={item.imageUrl} token={token} alt={t('Document photo from {name}', { name: item.userName })} /></div>}
          {rejecting === item._id && <form className="verify-reject" onSubmit={(e) => { e.preventDefault(); decide(item, false) }}>
            <label htmlFor={`reason-${item._id}`}>{t('Reason for rejecting (the user will see it)')}</label>
            <textarea id={`reason-${item._id}`} rows={2} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
            <button type="submit" className="extra-button secondary danger" disabled={busy === item._id}>{t('Reject request')}</button>
          </form>}
        </div>) : <p className="extra-empty-copy">{t('No requests here.')}</p>}
  </article>
}

export function VerificationPage({ token, user, signedIn, openAuth, notify }) {
  const admin = user?.role?.toLowerCase() === 'admin'
  const heading = <Heading icon={ShieldCheck} eyebrow={t('ELIGIBILITY & PASSES')} title={t('Account verification')} subtitle={t('Verify your NID or student pass. An admin reviews each document.')} />
  if (!signedIn) return <section className="extra-page">{heading}<RequireSignIn openAuth={openAuth} /></section>
  return <section className="extra-page verify-page">
    {heading}
    {admin && <ReviewQueue token={token} notify={notify} />}
    <MyVerification token={token} notify={notify} />
  </section>
}

