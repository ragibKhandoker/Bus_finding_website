import React, { useCallback, useEffect, useState } from 'react'
import { ArrowRight, BadgeCheck, BusFront, CheckCircle2, CircleAlert, CircleX, Clock3, FileWarning, Headset, Loader2, RefreshCw, Search, Send } from 'lucide-react'
import { t } from '../../models/i18n'
import { FIELD_LABELS, dayTime, dhakaToday, driverApi, emptyApplication, stepProblems } from '../../models/driver'
import { VerificationStatusBadge } from '../components/driver/VerificationStatusBadge'
import { DocumentUploadField } from '../components/driver/DocumentUploadField'
import {
  AddressInformationSection, BusEmploymentSection, DrivingInformationSection, IdentityVerificationSection, PersonalInformationSection,
} from '../components/driver/DriverFormSections'
import { SignInPrompt } from '../components/common'
import '../styles/driver.css'

const VIEW = {
  PENDING: { icon: Clock3, tone: 'amber', title: 'Application submitted', message: 'Your driver registration has been submitted successfully. Our administration team is reviewing your documents.' },
  UNDER_REVIEW: { icon: Search, tone: 'blue', title: 'Under review', message: 'Your documents are currently being reviewed.' },
  REJECTED: { icon: CircleX, tone: 'red', title: 'Not approved', message: 'Unfortunately, your application was not approved.' },
  RESUBMISSION_REQUIRED: { icon: FileWarning, tone: 'orange', title: 'Action needed', message: 'Some information or documents need correction.' },
  APPROVED: { icon: BadgeCheck, tone: 'green', title: 'Verified', message: 'Congratulations! Your driver account has been verified.' },
}

const TRACK = ['PENDING', 'UNDER_REVIEW', 'APPROVED']

// Where a driver's application stands, shown instead of the driver dashboard until it is approved.
export function DriverVerificationStatusPage({ token, user, signedIn, openAuth, navigate, notify, embedded = false }) {
  const [state, setState] = useState({ status: 'loading' })
  const load = useCallback(() => driverApi.status(token).then((res) => setState({ status: 'ready', s: res.data }))
    .catch((e) => setState({ status: 'error', error: t(e.message || 'Could not load your application.') })), [token])
  useEffect(() => { if (signedIn && user?.role === 'driver') load() }, [signedIn, user?.role, load])

  const wrap = (content) => embedded ? content : <div className="page inner-page dv-page">{content}</div>
  if (!signedIn) return wrap(<SignInPrompt openAuth={openAuth} message={t('Sign in with your driver account to see your verification status.')} />)
  if (user?.role !== 'driver') return wrap(<div className="dv-panel dv-empty"><span><BusFront size={24} /></span><b>{t('This page is for drivers')}</b>
    <small>{t('Register as a driver to apply for a driver account.')}</small>
    <button type="button" className="button button-green" onClick={() => navigate('Register')}>{t('Choose account type')} <ArrowRight size={15} /></button></div>)
  if (state.status === 'loading') return wrap(<div className="dv-panel dv-center"><Loader2 size={20} className="dv-spin" /> {t('Loading your application…')}</div>)
  if (state.status === 'error') return wrap(<div className="dv-alert danger" role="alert"><CircleAlert size={18} /><span>{state.error}</span>
    <button type="button" className="dv-chip" onClick={load}><RefreshCw size={13} /> {t('Try again')}</button></div>)

  const s = state.s
  if (!s.registered) return wrap(<div className="dv-panel dv-empty"><span><FileWarning size={24} /></span><b>{t('No driver registration on file')}</b><small>{t(s.nextStep)}</small>
    <button type="button" className="button button-outline" onClick={() => navigate('Support')}><Headset size={15} /> {t('Contact support')}</button></div>)

  const view = VIEW[s.verificationStatus] || VIEW.PENDING
  const Icon = view.icon
  const position = s.verificationStatus === 'REJECTED' || s.verificationStatus === 'RESUBMISSION_REQUIRED' ? 1 : TRACK.indexOf(s.verificationStatus)

  return wrap(<div className="dv-status">
    <section className={`dv-status-hero tone-${view.tone}`}>
      <span className="dv-status-icon"><Icon size={30} /></span>
      <div>
        <small>{t('Driver verification')}</small>
        <h1>{t(view.title)}</h1>
        <p>{t(view.message)}</p>
      </div>
      <VerificationStatusBadge status={s.verificationStatus} large />
    </section>

    <ol className="dv-track" aria-label={t('Verification progress')}>
      {['Submitted', 'Reviewed by admin', 'Verified'].map((label, i) => {
        const failed = i === 1 && (s.verificationStatus === 'REJECTED' || s.verificationStatus === 'RESUBMISSION_REQUIRED')
        const state = failed ? 'failed' : i < position || (i === position && s.verificationStatus === 'APPROVED') ? 'done' : i === position ? 'current' : 'todo'
        return <li key={label} className={`is-${state}`}><span>{state === 'done' ? <CheckCircle2 size={16} /> : state === 'failed' ? <CircleAlert size={16} /> : i + 1}</span>{t(label)}</li>
      })}
    </ol>

    <div className="dv-status-grid">
      <div className="dv-panel">
        <h3>{t('Application details')}</h3>
        <dl className="dv-facts">
          <div><dt>{t('Application ID')}</dt><dd className="dv-mono">{s.applicationCode}</dd></div>
          <div><dt>{t('Submission date')}</dt><dd>{dayTime(s.submittedAt)}</dd></div>
          <div><dt>{t('Current status')}</dt><dd><VerificationStatusBadge status={s.verificationStatus} /></dd></div>
          {s.reviewedAt && <div><dt>{t('Last reviewed')}</dt><dd>{dayTime(s.reviewedAt)}</dd></div>}
        </dl>
      </div>
      <div className="dv-panel">
        <h3>{t('Expected next step')}</h3>
        <p className="dv-next">{t(s.nextStep)}</p>
        <div className="dv-row">
          <button type="button" className="button button-outline button-small" onClick={load}><RefreshCw size={14} /> {t('Refresh')}</button>
          <button type="button" className="button button-outline button-small" onClick={() => navigate('Support')}><Headset size={14} /> {t('Contact support')}</button>
        </div>
      </div>
    </div>

    {s.verificationStatus === 'REJECTED' && s.rejectionReason && <div className="dv-reason danger"><b>{t('Reason from the administrator')}</b><p>{s.rejectionReason}</p></div>}

    {s.verificationStatus === 'RESUBMISSION_REQUIRED' && <ResubmissionForm token={token} status={s} notify={notify} onDone={load} />}

    {s.verificationStatus === 'APPROVED' && <div className="dv-panel dv-approved">
      <span className="dv-big-icon green"><BadgeCheck size={26} /></span>
      <div><h3>{t('Your driver dashboard is ready.')}</h3>
        <p>{s.hasBusAssignment ? t('You are assigned to a bus.') : t('An administrator still needs to assign you to a bus before you can use bus operations.')}</p></div>
      {!embedded && <button type="button" className="button button-green" onClick={() => navigate('Driver dashboard')}>{t('Open driver dashboard')} <ArrowRight size={15} /></button>}
    </div>}
  </div>)
}

// Which sections to show for the requested items, and which document pickers inside each.
const DOCS_IN = { driving: ['license_front', 'license_back'], identity: ['nid_front', 'nid_back'] }

function fromProfile(p) {
  return {
    ...emptyApplication(),
    fullName: p.fullName, phone: p.phone, dateOfBirth: p.dateOfBirth, gender: p.gender,
    licenseType: p.licenseType, licenseExpiryDate: p.licenseExpiryDate, drivingExperienceYears: String(p.drivingExperienceYears),
    busId: p.claimedBusInDatabase ? p.claimedBusId : '', busName: p.claimedBusName, operatorName: p.claimedOperatorName, route: p.claimedRoute,
    busRegistrationNumber: p.busRegistrationNumber, employmentType: p.employmentType,
    presentAddress: p.presentAddress, district: p.district, area: p.area, emergencyContact: p.emergencyContact,
  }
}

// Only the parts the admin asked for; the server ignores anything else.
function ResubmissionForm({ token, status, notify, onDone }) {
  const requested = status.requestedFields || []
  const has = (f) => requested.includes(f)
  const [form, setForm] = useState(null)
  const [errors, setErrors] = useState({})
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const today = dhakaToday()
  useEffect(() => {
    driverApi.myProfile(token).then((res) => setForm(fromProfile(res.data))).catch((e) => setError(t(e.message || 'Could not load your application.')))
  }, [token])
  const set = (key, value) => { setForm((f) => ({ ...f, [key]: value })); setErrors({}) }
  const setMany = (values) => { setForm((f) => ({ ...f, ...values })); setErrors({}) }

  const problems = () => {
    if (!form) return {}
    const p = {}
    const pick = (step, keys) => { const found = stepProblems(step, { ...form, documents: { license_front: 1, nid_front: 1, ...form.documents } }, today); keys.forEach((k) => { if (found[k]) p[k] = found[k] }) }
    if (has('personal')) pick('personal', ['fullName', 'phone', 'dateOfBirth', 'gender'])
    if (has('driving_details')) pick('driving', ['licenseNumber', 'licenseType', 'licenseExpiryDate', 'drivingExperienceYears'])
    if (has('nid_number')) pick('identity', ['nidNumber'])
    if (has('employment')) pick('bus', ['busName', 'operatorName', 'route', 'employmentType', 'busRegistrationNumber'])
    if (has('address')) pick('address', ['presentAddress', 'district', 'area', 'emergencyContact'])
    requested.filter((f) => FIELD_LABELS[f] && /_(front|back|photo)$/.test(f)).forEach((type) => { if (!form.documents[type]) p[type] = t('Upload a new {doc}.', { doc: t(FIELD_LABELS[type]).toLowerCase() }) })
    return p
  }
  const submit = async (event) => {
    event.preventDefault()
    const found = problems()
    setErrors(found)
    if (Object.keys(found).length) { setError(t('Please correct the highlighted items.')); return }
    setBusy(true); setError('')
    try {
      const body = {
        fullName: form.fullName, phone: form.phone, dateOfBirth: form.dateOfBirth, gender: form.gender,
        licenseNumber: form.licenseNumber, licenseType: form.licenseType, licenseExpiryDate: form.licenseExpiryDate, drivingExperienceYears: Number(form.drivingExperienceYears),
        nidNumber: form.nidNumber, ...(form.busId ? { busId: form.busId } : { busName: form.busName, operatorName: form.operatorName, route: form.route }),
        busRegistrationNumber: form.busRegistrationNumber || null, employmentType: form.employmentType,
        presentAddress: form.presentAddress, district: form.district, area: form.area, emergencyContact: form.emergencyContact || null,
        documents: Object.fromEntries(Object.entries(form.documents).filter(([type, d]) => has(type) && d?.data).map(([type, d]) => [type, d.data])),
      }
      const res = await driverApi.resubmit(body, token)
      notify(t(res.message || 'Your corrections were sent for review.'))
      onDone()
    } catch (e) { setError(t(e.message || 'Something went wrong. Please try again.')) } finally { setBusy(false) }
  }

  const drivingDocs = DOCS_IN.driving.filter(has)
  const identityDocs = DOCS_IN.identity.filter(has)
  return <form className="dv-panel dv-resubmit" onSubmit={submit} noValidate>
    <div className="dv-reason warn">
      <b>{t('What the administrator asked for')}</b>
      <p>{status.resubmissionReason}</p>
      <div className="dv-tags">{requested.map((f) => <span key={f}>{t(FIELD_LABELS[f] || f)}</span>)}</div>
    </div>
    {!form ? (error ? <p className="dv-alert danger" role="alert"><CircleAlert size={16} /> {error}</p> : <div className="dv-center"><Loader2 size={18} className="dv-spin" /> {t('Loading…')}</div>) : <>
      {has('personal') && <PersonalInformationSection form={form} set={set} errors={errors} today={today} includeAccount={false} />}
      {(has('driving_details') || drivingDocs.length > 0) && <DrivingInformationSection form={form} set={set} errors={errors} today={today} showDetails={has('driving_details')} docs={drivingDocs} />}
      {(has('nid_number') || identityDocs.length > 0) && <IdentityVerificationSection form={form} set={set} errors={errors} showDetails={has('nid_number')} docs={identityDocs} />}
      {has('employment') && <BusEmploymentSection form={form} set={set} errors={errors} setMany={setMany} />}
      {has('address') && <AddressInformationSection form={form} set={set} errors={errors} />}
      {has('profile_photo') && <div className="dv-section"><DocumentUploadField label="Profile picture" required value={form.documents.profile_photo}
        onChange={(v) => set('documents', { ...form.documents, profile_photo: v })} error={errors.profile_photo} /></div>}
      {(has('driving_details') || has('nid_number')) && <p className="dv-small">{t('For your security, licence and NID numbers are never shown again. Type them in full.')}</p>}
      {error && <p className="dv-alert danger" role="alert"><CircleAlert size={16} /> {error}</p>}
      <div className="dv-actions"><span />
        <button type="submit" className="button button-green" disabled={busy}>{busy ? <Loader2 size={16} className="dv-spin" /> : <Send size={16} />} {t('Resubmit application')}</button>
      </div>
    </>}
  </form>
}
