import React from 'react'
import { ArrowRight, BadgeCheck, BusFront, FileCheck2, Map, Route, ShieldCheck, Ticket, Users, Wallet } from 'lucide-react'
import { t } from '../../models/i18n'
import commuterPhoto from '../../assets/home/dhaka-nagar-paribahan-bus.jpg'
import driverPhoto from '../../assets/home/women-bus-driver.jpg'
import '../styles/driver.css'

const OPTIONS = [
  {
    id: 'commuter', icon: Users, photo: commuterPhoto, eyebrow: 'For passengers', title: 'Register as Commuter',
    text: 'Join SmartCommute Dhaka to find buses, explore routes, manage trips, and enjoy commuter services.',
    points: [[Route, 'Find buses and routes across Dhaka'], [Ticket, 'Track trips, fares and rewards'], [Wallet, 'Travel wallet and ride sharing']],
    button: 'Register as Commuter', note: 'Takes less than a minute',
  },
  {
    id: 'driver', icon: BusFront, photo: driverPhoto, eyebrow: 'For bus drivers', title: 'Register as Driver',
    text: 'Join SmartCommute Dhaka as a bus driver, manage your driving profile, submit verification documents, and access driver services.',
    points: [[FileCheck2, 'Submit your licence and NID securely'], [ShieldCheck, 'Verified by a SmartCommute administrator'], [Map, 'Driver dashboard once approved']],
    button: 'Register as Driver', note: 'Have your driving licence and NID ready',
  },
]

// Sign up: choose the kind of account first. Commuters get the existing quick sign-up form; drivers go to the
// separate driver registration with document verification.
export function RegistrationTypeSelectionPage({ navigate, openCommuterSignup, openSignIn, signedIn }) {
  const choose = (id) => (id === 'driver' ? navigate('Driver registration') : openCommuterSignup())
  return <div className="page inner-page dv-page dv-choose">
    <header className="dv-choose-head">
      <span className="dv-eyebrow"><BadgeCheck size={14} /> {t('CREATE YOUR ACCOUNT')}</span>
      <h1>{t('How will you use SmartCommute?')}</h1>
      <p>{t('Choose the account that fits you. Commuters and drivers have different forms, permissions and dashboards.')}</p>
    </header>
    {signedIn && <div className="dv-alert info" role="status"><ShieldCheck size={18} /><span>{t('You are already signed in. Sign out first to create another account.')}</span></div>}
    <div className="dv-choose-grid">
      {OPTIONS.map(({ id, icon: Icon, photo, eyebrow, title, text, points, button, note }) => <article key={id} className={`dv-choice is-${id}`}>
        <div className="dv-choice-photo"><img src={photo} alt="" loading="lazy" /><span className="dv-choice-icon"><Icon size={26} /></span></div>
        <div className="dv-choice-body">
          <small className="dv-choice-eyebrow">{t(eyebrow)}</small>
          <h2>{t(title)}</h2>
          <p>{t(text)}</p>
          <ul>{points.map(([PointIcon, label]) => <li key={label}><PointIcon size={16} aria-hidden="true" /> {t(label)}</li>)}</ul>
          <button type="button" className={`button ${id === 'driver' ? 'button-dark' : 'button-green'} dv-choice-button`} onClick={() => choose(id)} disabled={signedIn}>
            {t(button)} <ArrowRight size={16} />
          </button>
          <small className="dv-choice-note">{t(note)}</small>
        </div>
      </article>)}
    </div>
    <p className="dv-choose-foot">{t('Already have an account?')} <button type="button" className="dv-link" onClick={openSignIn}>{t('Sign in')}</button></p>
  </div>
}
