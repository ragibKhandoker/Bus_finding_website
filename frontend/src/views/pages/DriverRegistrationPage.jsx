import React from 'react'
import { ArrowLeft, BusFront, Clock3, CreditCard, IdCard, Lock, ShieldCheck, UserCheck } from 'lucide-react'
import { t } from '../../models/i18n'
import { driverApi } from '../../models/driver'
import { DriverRegistrationForm } from '../components/driver/DriverRegistrationForm'
import '../styles/driver.css'

// The driver registration page: what is needed and how verification works, beside the six-step form.
// A successful registration signs the new driver in (onRegistered) and opens their verification status.
export function DriverRegistrationPage({ signedIn, user, navigate, onRegistered, notify }) {
  const submit = async (body) => {
    const data = await driverApi.register(body)
    onRegistered(data.user, data.token)
    notify(t('Your driver registration has been submitted.'))
    navigate('Driver status')
  }

  return <div className="page inner-page dv-page">
    <section className="dv-hero">
      <div>
        <button type="button" className="dv-back" onClick={() => navigate('Register')}><ArrowLeft size={15} /> {t('Choose account type')}</button>
        <span className="dv-eyebrow"><BusFront size={14} /> {t('DRIVER REGISTRATION')}</span>
        <h1>{t('Drive with SmartCommute Dhaka.')}</h1>
        <p>{t('Register as a bus driver. An administrator verifies your licence and NID before your driver account is activated.')}</p>
      </div>
      <ol className="dv-hero-steps">
        <li><b>1</b><span>{t('Submit details & documents')}</span></li>
        <li><b>2</b><span>{t('Admin verifies them')}</span></li>
        <li><b>3</b><span>{t('Get your driver dashboard')}</span></li>
      </ol>
    </section>

    {signedIn ? <div className="dv-panel dv-signed-in">
      <span className="dv-big-icon"><UserCheck size={26} /></span>
      <div><h2>{t('You are signed in as {name}.', { name: user?.name || user?.email || '' })}</h2>
        <p>{user?.role === 'driver' ? t('Your driver application is already on file.') : t('Sign out first to register a separate driver account.')}</p></div>
      {user?.role === 'driver' && <button type="button" className="button button-green" onClick={() => navigate('Driver status')}>{t('See my application')}</button>}
    </div> : <div className="dv-layout">
      <DriverRegistrationForm onSubmit={submit} />
      <aside className="dv-aside" aria-label={t('What you need')}>
        <div className="dv-panel">
          <h3>{t('What you need')}</h3>
          <ul className="dv-needs">
            <li><CreditCard size={18} /><span><b>{t('Driving licence')}</b><small>{t('Number, type, expiry date and a clear photo of the front')}</small></span></li>
            <li><IdCard size={18} /><span><b>{t('National ID (NID)')}</b><small>{t('Number and a clear photo of the front')}</small></span></li>
            <li><BusFront size={18} /><span><b>{t('Your current bus')}</b><small>{t('Name, operator and route')}</small></span></li>
          </ul>
        </div>
        <div className="dv-panel dv-panel-soft">
          <h3><Lock size={16} /> {t('Your privacy')}</h3>
          <p>{t('Licence and NID numbers are encrypted. Document photos are stored privately and are never public. Only administrators can view them to verify you.')}</p>
        </div>
        <div className="dv-panel dv-panel-soft">
          <h3><Clock3 size={16} /> {t('After you submit')}</h3>
          <p>{t('Your account starts as pending verification. You can sign in to follow the review, and you will be notified of the decision.')}</p>
          <p className="dv-small"><ShieldCheck size={14} /> {t('Bus assignment is confirmed separately by an administrator.')}</p>
        </div>
      </aside>
    </div>}
  </div>
}
