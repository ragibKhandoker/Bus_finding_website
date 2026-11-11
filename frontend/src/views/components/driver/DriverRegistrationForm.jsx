import React, { useEffect, useRef, useState } from 'react'
import { ArrowLeft, ArrowRight, Check, CircleAlert, Loader2, Send } from 'lucide-react'
import { t } from '../../../models/i18n'
import { DOCUMENTS, STEPS, dhakaToday, emptyApplication, stepProblems, toRegistrationBody } from '../../../models/driver'
import {
  AddressInformationSection, BusEmploymentSection, ConsentSection, DrivingInformationSection, IdentityVerificationSection, PersonalInformationSection,
} from './DriverFormSections'

// The six-step driver registration form with a progress indicator. Each step is checked before moving on; the
// last step shows a summary and the declarations. onSubmit(body) sends it; errors from the server are shown here.
export function DriverRegistrationForm({ onSubmit }) {
  const [form, setForm] = useState(emptyApplication)
  const [step, setStep] = useState(0)
  const [errors, setErrors] = useState({})
  const [visited, setVisited] = useState(0)
  const [busy, setBusy] = useState(false)
  const [serverError, setServerError] = useState('')
  const top = useRef(null)
  const today = dhakaToday()
  const current = STEPS[step]

  const set = (key, value) => {
    setForm((f) => ({ ...f, [key]: value }))
    // Clear a field's message as soon as it is changed.
    setErrors((e) => {
      if (key === 'documents') return Object.fromEntries(Object.entries(e).filter(([k]) => !DOCUMENTS.some((d) => d.type === k)))
      if (!(key in e) && !['confirmAccuracy', 'authorizeVerification', 'acceptTerms'].includes(key)) return e
      const { [key]: _, consent, ...rest } = e
      return rest
    })
  }
  const setMany = (values) => { setForm((f) => ({ ...f, ...values })); setErrors({}) }

  // Bring the new step into view when it changes (not on the first render, so the page opens at the top).
  const firstRender = useRef(true)
  useEffect(() => {
    if (firstRender.current) { firstRender.current = false; return }
    top.current?.scrollIntoView?.({ behavior: 'smooth', block: 'start' })
  }, [step])

  const check = (index) => {
    const problems = stepProblems(STEPS[index].id, form, today)
    setErrors(problems)
    return Object.keys(problems).length === 0
  }
  const next = () => {
    if (!check(step)) return
    const to = Math.min(step + 1, STEPS.length - 1)
    setStep(to); setVisited((v) => Math.max(v, to))
  }
  const back = () => { setErrors({}); setStep((s) => Math.max(0, s - 1)) }
  const jump = (index) => {
    if (index === step) return
    if (index < step || index <= visited) { if (index > step && !check(step)) return; setErrors({}); setStep(index) }
  }

  const submit = async (event) => {
    event.preventDefault()
    if (step < STEPS.length - 1) { next(); return }
    // Every step again, in case something was changed after it was checked.
    for (let i = 0; i < STEPS.length; i += 1) {
      if (!check(i)) { setStep(i); return }
    }
    setBusy(true); setServerError('')
    try { await onSubmit(toRegistrationBody(form)) } catch (e) { setServerError(t(e.message || 'Something went wrong. Please try again.')) } finally { setBusy(false) }
  }

  const props = { form, set, errors, today }
  const uploaded = DOCUMENTS.filter((d) => form.documents[d.type])

  return <form className="dv-form" noValidate onSubmit={submit} ref={top}>
    <ol className="dv-steps" aria-label={t('Registration progress')}>
      {STEPS.map((s, index) => {
        const state = index < step ? 'done' : index === step ? 'current' : index <= visited ? 'seen' : 'todo'
        return <li key={s.id} className={`is-${state}`}>
          <button type="button" onClick={() => jump(index)} disabled={index > visited} aria-current={index === step ? 'step' : undefined}>
            <span className="dv-step-dot">{index < step ? <Check size={14} /> : index + 1}</span>
            <span className="dv-step-text">{t(s.title)}</span>
          </button>
        </li>
      })}
    </ol>
    <div className="dv-progress" aria-hidden="true"><i style={{ width: `${((step + 1) / STEPS.length) * 100}%` }} /></div>
    <p className="dv-step-caption">{t('Step {n} of {total}', { n: step + 1, total: STEPS.length })} · <b>{t(current.long)}</b></p>

    <div className="dv-step-body" key={current.id}>
      {current.id === 'personal' && <PersonalInformationSection {...props} />}
      {current.id === 'driving' && <DrivingInformationSection {...props} />}
      {current.id === 'identity' && <IdentityVerificationSection {...props} />}
      {current.id === 'bus' && <BusEmploymentSection {...props} setMany={setMany} />}
      {current.id === 'address' && <AddressInformationSection {...props} />}
      {current.id === 'consent' && <>
        <section className="dv-summary" aria-label={t('Application summary')}>
          <h3>{t('Check your application')}</h3>
          <dl>
            <div><dt>{t('Name')}</dt><dd>{form.fullName}</dd></div>
            <div><dt>{t('Email')}</dt><dd>{form.email}</dd></div>
            <div><dt>{t('Phone')}</dt><dd>{form.phone}</dd></div>
            <div><dt>{t('Licence')}</dt><dd>{t(form.licenseType)} · {t('expires {date}', { date: form.licenseExpiryDate })}</dd></div>
            <div><dt>{t('Experience')}</dt><dd>{t('{n} years', { n: Number(form.drivingExperienceYears) || 0 })}</dd></div>
            <div><dt>{t('Bus')}</dt><dd>{t(form.busName)} · {form.operatorName}</dd></div>
            <div><dt>{t('Employment')}</dt><dd>{t(form.employmentType)}</dd></div>
            <div><dt>{t('Address')}</dt><dd>{form.area}, {t(form.district)}</dd></div>
            <div className="wide"><dt>{t('Documents')}</dt><dd className="dv-summary-docs">{uploaded.map((d) => <span key={d.type}><Check size={13} /> {t(d.label)}</span>)}</dd></div>
          </dl>
          <button type="button" className="dv-link" onClick={() => jump(0)}>{t('Edit from the beginning')}</button>
        </section>
        <ConsentSection {...props} />
      </>}
    </div>

    {serverError && <div className="dv-alert danger" role="alert"><CircleAlert size={18} /><span>{serverError}</span></div>}

    <div className="dv-actions">
      <button type="button" className="button button-outline" onClick={back} disabled={step === 0 || busy}><ArrowLeft size={16} /> {t('Back')}</button>
      {step < STEPS.length - 1
        ? <button type="submit" className="button button-green">{t('Continue')} <ArrowRight size={16} /></button>
        : <button type="submit" className="button button-green" disabled={busy}>
          {busy ? <><Loader2 size={16} className="dv-spin" /> {t('Submitting…')}</> : <><Send size={16} /> {t('Submit application')}</>}
        </button>}
    </div>
  </form>
}
