import React, { cloneElement, isValidElement, useId, useMemo, useState } from 'react'
import { BadgeCheck, BusFront, CreditCard, Eye, EyeOff, IdCard, Lock, Mail, MapPin, Phone, Search, ShieldCheck, UserRound, X } from 'lucide-react'
import { t } from '../../../models/i18n'
import { DISTRICTS, EMPLOYMENT_TYPES, GENDERS, LICENSE_TYPES, passwordRules } from '../../../models/driver'
import { useBusRoutes } from '../../../controllers/useBusRoutes'
import { DocumentUploadField } from './DocumentUploadField'

// The sections of the driver registration form. Each gets the whole form, a setter and the problems by field,
// so the same sections are reused when a driver resubmits only the parts an admin asked for.

// A labelled input. The control gets its id, aria-invalid and aria-describedby from here.
export function DvField({ label, error, hint, optional = false, wide = false, icon: Icon, children }) {
  const id = useId()
  const control = isValidElement(children)
    ? cloneElement(children, { id, 'aria-invalid': Boolean(error), 'aria-describedby': `${id}-note` })
    : children
  return <div className={`dv-field${wide ? ' wide' : ''}${error ? ' has-error' : ''}`}>
    <label htmlFor={id}>{t(label)} {optional && <em>{t('(optional)')}</em>}</label>
    <div className="dv-control">{Icon && <Icon size={16} aria-hidden="true" />}{control}</div>
    <small id={`${id}-note`} className={error ? 'dv-error' : 'dv-hint'} role={error ? 'alert' : undefined}>{error || (hint ? t(hint) : '')}</small>
  </div>
}

export function SectionCard({ number, icon: Icon, title, text, children }) {
  return <section className="dv-section" aria-labelledby={`dv-sec-${number}`}>
    <header className="dv-section-head">
      <span className="dv-section-icon">{Icon && <Icon size={19} />}</span>
      <div><small>{t('Section {n}', { n: number })}</small><h3 id={`dv-sec-${number}`}>{t(title)}</h3>{text && <p>{t(text)}</p>}</div>
    </header>
    <div className="dv-grid">{children}</div>
  </section>
}

const bind = (form, set, key) => ({ value: form[key] ?? '', onChange: (e) => set(key, e.target.value) })

// includeAccount: email and password (only when first registering).
export function PersonalInformationSection({ form, set, errors, today, includeAccount = true }) {
  const [show, setShow] = useState(false)
  const rules = passwordRules(form.password || '')
  return <SectionCard number="A" icon={UserRound} title="Personal information" text="As it appears on your NID and driving licence.">
    <DvField label="Full name" icon={UserRound} error={errors.fullName} wide>
      <input className="dv-input" autoComplete="name" placeholder={t('e.g. Md. Karim Uddin')} {...bind(form, set, 'fullName')} />
    </DvField>
    {includeAccount && <DvField label="Email address" icon={Mail} error={errors.email} hint="You will sign in with this email.">
      <input className="dv-input" type="email" autoComplete="email" placeholder="you@example.com" {...bind(form, set, 'email')} />
    </DvField>}
    <DvField label="Phone number" icon={Phone} error={errors.phone} hint="Bangladesh mobile, e.g. 01712-345678">
      <input className="dv-input" type="tel" autoComplete="tel" inputMode="tel" placeholder="01712-345678" {...bind(form, set, 'phone')} />
    </DvField>
    {includeAccount && <>
      <DvField label="Password" icon={Lock} error={errors.password}>
        <input className="dv-input" type={show ? 'text' : 'password'} autoComplete="new-password" placeholder={t('Create a password')} {...bind(form, set, 'password')} />
      </DvField>
      <DvField label="Confirm password" icon={Lock} error={errors.confirmPassword}>
        <input className="dv-input" type={show ? 'text' : 'password'} autoComplete="new-password" placeholder={t('Type it again')} {...bind(form, set, 'confirmPassword')} />
      </DvField>
      <div className="dv-pass-row wide">
        <ul className="dv-rules" aria-label={t('Password requirements')}>{rules.map((r) => <li key={r.text} className={r.ok ? 'ok' : ''}><BadgeCheck size={14} aria-hidden="true" /> {t(r.text)}</li>)}</ul>
        <button type="button" className="dv-chip" onClick={() => setShow(!show)} aria-pressed={show}>{show ? <EyeOff size={14} /> : <Eye size={14} />} {show ? t('Hide passwords') : t('Show passwords')}</button>
      </div>
    </>}
    <DvField label="Date of birth" error={errors.dateOfBirth} hint="You must be at least 18.">
      <input className="dv-input" type="date" max={today} {...bind(form, set, 'dateOfBirth')} />
    </DvField>
    <DvField label="Gender" error={errors.gender}>
      <select className="dv-input" {...bind(form, set, 'gender')}>
        <option value="">{t('Choose…')}</option>
        {GENDERS.map((g) => <option key={g} value={g}>{t(g)}</option>)}
      </select>
    </DvField>
    {includeAccount && <div className="dv-field wide">
      <DocumentUploadField label="Profile picture" hint="A clear photo of your face helps the admin match your documents." compact
        value={form.documents.profile_photo} onChange={(v) => set('documents', { ...form.documents, profile_photo: v })} />
    </div>}
  </SectionCard>
}

// docs: which document pickers to show (all by default; a resubmission shows only those asked for).
export function DrivingInformationSection({ form, set, errors, today, showDetails = true, docs = ['license_front', 'license_back'] }) {
  const setDoc = (type) => (v) => set('documents', { ...form.documents, [type]: v })
  return <SectionCard number="B" icon={CreditCard} title="Driving information" text="Your licence is checked by an administrator before you can drive.">
    {showDetails && <>
      <DvField label="Driving licence number" icon={CreditCard} error={errors.licenseNumber} hint="Exactly as printed, e.g. DK0123456L00001">
        <input className="dv-input" autoComplete="off" spellCheck={false} placeholder="DK0123456L00001" {...bind(form, set, 'licenseNumber')} />
      </DvField>
      <DvField label="Licence type" error={errors.licenseType} hint={form.licenseType === 'Other' ? 'Other licence types are checked by an admin.' : ''}>
        <select className="dv-input" {...bind(form, set, 'licenseType')}>
          <option value="">{t('Choose…')}</option>
          {LICENSE_TYPES.map((type) => <option key={type} value={type}>{t(type)}{type === 'Other' ? ` (${t('subject to admin review')})` : ''}</option>)}
        </select>
      </DvField>
      <DvField label="Licence expiry date" error={errors.licenseExpiryDate}>
        <input className="dv-input" type="date" min={today} {...bind(form, set, 'licenseExpiryDate')} />
      </DvField>
      <DvField label="Driving experience (years)" error={errors.drivingExperienceYears}>
        <input className="dv-input" type="number" min="0" max="60" step="1" inputMode="numeric" placeholder="e.g. 5" {...bind(form, set, 'drivingExperienceYears')} />
      </DvField>
    </>}
    {docs.length > 0 && <div className="dv-docs wide">
      {docs.includes('license_front') && <DocumentUploadField label="Driving licence (front)" required value={form.documents.license_front} onChange={setDoc('license_front')} error={errors.license_front} />}
      {docs.includes('license_back') && <DocumentUploadField label="Driving licence (back)" hint="If your licence has a back side." value={form.documents.license_back} onChange={setDoc('license_back')} error={errors.license_back} />}
    </div>}
  </SectionCard>
}

export function IdentityVerificationSection({ form, set, errors, showDetails = true, docs = ['nid_front', 'nid_back'] }) {
  const setDoc = (type) => (v) => set('documents', { ...form.documents, [type]: v })
  return <SectionCard number="C" icon={IdCard} title="Identity verification" text="Your National ID confirms who you are.">
    {showDetails && <DvField label="NID number" icon={IdCard} error={errors.nidNumber} hint="10, 13 or 17 digits. Stored encrypted and never shown in full." wide>
      <input className="dv-input" inputMode="numeric" autoComplete="off" spellCheck={false} placeholder="e.g. 1990123456789" {...bind(form, set, 'nidNumber')} />
    </DvField>}
    {docs.length > 0 && <div className="dv-docs wide">
      {docs.includes('nid_front') && <DocumentUploadField label="NID (front)" required value={form.documents.nid_front} onChange={setDoc('nid_front')} error={errors.nid_front} />}
      {docs.includes('nid_back') && <DocumentUploadField label="NID (back)" hint="If your NID card has a back side." value={form.documents.nid_back} onChange={setDoc('nid_back')} error={errors.nid_back} />}
    </div>}
    <p className="dv-privacy wide"><ShieldCheck size={16} aria-hidden="true" /> {t('Your documents are stored privately. Only SmartCommute administrators can view them, and only to verify your account.')}</p>
  </SectionCard>
}

// Pick the bus from the database when it is there; otherwise type its details. Either way this is only a claim:
// an administrator confirms the real bus assignment.
export function BusEmploymentSection({ form, set, errors, setMany }) {
  const [routes] = useBusRoutes()
  const [search, setSearch] = useState('')
  const [manual, setManual] = useState(Boolean(!form.busId && form.busName))
  const matches = useMemo(() => {
    if (routes.status !== 'ready') return []
    const q = search.trim().toLowerCase()
    if (q.length < 2) return []
    return routes.buses.filter((bus) => `${bus.busName} ${bus.routeNumber || ''} ${bus.operator || ''}`.toLowerCase().includes(q)).slice(0, 6)
  }, [routes, search])
  const routeOf = (bus) => {
    const stops = [...(bus.stops || [])].sort((a, b) => (a.order ?? 0) - (b.order ?? 0))
    return stops.length >= 2 ? `${stops[0].name} – ${stops[stops.length - 1].name}` : bus.routeNumber || ''
  }
  const pick = (bus) => {
    setMany({ busId: bus._id || bus.id, busName: bus.busName, operatorName: bus.operator || bus.busName, route: [bus.routeNumber, routeOf(bus)].filter(Boolean).join(' · ') })
    setSearch(''); setManual(false)
  }
  const clearBus = () => setMany({ busId: '', busName: '', operatorName: '', route: '' })

  return <SectionCard number="D" icon={BusFront} title="Bus information" text="The bus you currently drive. This is only a claim: an administrator confirms your actual bus assignment.">
    {form.busId ? <div className="dv-bus-picked wide">
      <span className="dv-bus-icon"><BusFront size={20} /></span>
      <div><b>{t(form.busName)}</b><small>{form.operatorName} · {form.route}</small></div>
      <button type="button" className="dv-chip" onClick={clearBus}><X size={13} /> {t('Change')}</button>
    </div> : !manual ? <div className="dv-field wide">
      <label htmlFor="dv-bus-search">{t('Find your bus')}</label>
      <div className="dv-control"><Search size={16} aria-hidden="true" />
        <input id="dv-bus-search" className="dv-input" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('Type a bus name or route number, e.g. Turag')} autoComplete="off" />
      </div>
      {routes.status === 'loading' && <small className="dv-hint">{t('Loading buses…')}</small>}
      {matches.length > 0 && <ul className="dv-bus-results" role="listbox" aria-label={t('Matching buses')}>
        {matches.map((bus) => <li key={bus._id || bus.id} role="option" aria-selected="false">
          <button type="button" onClick={() => pick(bus)}><BusFront size={16} aria-hidden="true" /><span><b>{t(bus.busName)}</b><small>{[bus.routeNumber, bus.operator, routeOf(bus)].filter(Boolean).join(' · ')}</small></span></button>
        </li>)}
      </ul>}
      {search.trim().length >= 2 && routes.status === 'ready' && matches.length === 0 && <small className="dv-hint">{t('No bus matches “{q}”.', { q: search.trim() })}</small>}
      <button type="button" className="dv-link" onClick={() => setManual(true)}>{t('My bus is not listed — enter its details')}</button>
      {(errors.busName || errors.operatorName || errors.route) && <small className="dv-error" role="alert">{t('Choose your bus, or enter its details.')}</small>}
    </div> : <>
      <DvField label="Bus name" icon={BusFront} error={errors.busName}><input className="dv-input" placeholder={t('e.g. Turag Paribahan')} {...bind(form, set, 'busName')} /></DvField>
      <DvField label="Bus operator / company" error={errors.operatorName}><input className="dv-input" placeholder={t('e.g. Turag Transport Ltd.')} {...bind(form, set, 'operatorName')} /></DvField>
      <DvField label="Bus route" icon={MapPin} error={errors.route} wide><input className="dv-input" placeholder={t('e.g. Gazipur – Jatrabari')} {...bind(form, set, 'route')} /></DvField>
      <button type="button" className="dv-link wide" onClick={() => setManual(false)}>{t('Search the bus list instead')}</button>
    </>}
    <DvField label="Bus registration number" optional error={errors.busRegistrationNumber} hint="e.g. Dhaka Metro-Ba 11-2345">
      <input className="dv-input" placeholder="Dhaka Metro-Ba 11-2345" {...bind(form, set, 'busRegistrationNumber')} />
    </DvField>
    <div className={`dv-field${errors.employmentType ? ' has-error' : ''}`} role="radiogroup" aria-labelledby="dv-emp-label">
      <label id="dv-emp-label">{t('Employment type')}</label>
      <div className="dv-pills">{EMPLOYMENT_TYPES.map((type) => <label key={type} className={form.employmentType === type ? 'selected' : ''}>
        <input type="radio" name="employmentType" value={type} checked={form.employmentType === type} onChange={() => set('employmentType', type)} /> {t(type)}
      </label>)}</div>
      <small className={errors.employmentType ? 'dv-error' : 'dv-hint'} role={errors.employmentType ? 'alert' : undefined}>{errors.employmentType || ''}</small>
    </div>
  </SectionCard>
}

export function AddressInformationSection({ form, set, errors, showEmergency = true }) {
  return <SectionCard number="E" icon={MapPin} title="Address & additional information" text="Where you live now.">
    <DvField label="Present address" icon={MapPin} error={errors.presentAddress} wide>
      <input className="dv-input" autoComplete="street-address" placeholder={t('House, road, block')} {...bind(form, set, 'presentAddress')} />
    </DvField>
    <DvField label="District" error={errors.district}>
      <select className="dv-input" {...bind(form, set, 'district')}>
        <option value="">{t('Choose…')}</option>
        {DISTRICTS.map((d) => <option key={d} value={d}>{t(d)}</option>)}
      </select>
    </DvField>
    <DvField label="Area / Thana" error={errors.area}>
      <input className="dv-input" placeholder={t('e.g. Uttara')} {...bind(form, set, 'area')} />
    </DvField>
    {showEmergency && <DvField label="Emergency contact number" icon={Phone} optional error={errors.emergencyContact} wide>
      <input className="dv-input" type="tel" inputMode="tel" placeholder="01812-345678" {...bind(form, set, 'emergencyContact')} />
    </DvField>}
  </SectionCard>
}

export function ConsentSection({ form, set, errors }) {
  const items = [
    ['confirmAccuracy', 'I confirm that the information provided is accurate.'],
    ['authorizeVerification', 'I authorize SmartCommute Dhaka to verify my submitted identity and driving documents.'],
    ['acceptTerms', 'I agree to the Terms and Conditions and Privacy Policy.'],
  ]
  return <SectionCard number="F" icon={ShieldCheck} title="Declaration & consent" text="Please read and confirm each statement.">
    <div className="dv-consents wide">
      {items.map(([key, text]) => <label key={key} className={`dv-consent${form[key] ? ' checked' : ''}`}>
        <input type="checkbox" checked={form[key]} onChange={(e) => set(key, e.target.checked)} />
        <span>{t(text)}</span>
      </label>)}
      {errors.consent && <small className="dv-error" role="alert">{errors.consent}</small>}
    </div>
  </SectionCard>
}
