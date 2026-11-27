import React, { useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import { Mic, MicOff, X } from 'lucide-react'
import { useVoiceSearch } from '../../controllers/useVoiceSearch'
import { t } from '../../models/i18n'
import '../styles/voice-search.css'

const ERRORS = {
  'mic-blocked': 'Microphone access is blocked. Allow the microphone for this site in your browser settings, then try again.',
  'no-speech': 'Nothing was heard. Tap the mic and say where you are going.',
  network: 'Voice search needs an internet connection.',
  failed: 'Voice search could not start. Please try again.',
}

// A microphone button: tap it and say a search. While listening, a small card shows what is
// being heard and lets the speaker switch between English and Bangla. When they stop, onResult gets the
// recogniser's readings and may return a line saying what it searched for, shown for a moment.
// Browsers without speech recognition (Firefox) show no button, so the typed search is all there is.
// `slot` is the element the card is shown in (a spot in the page under the search box), so it takes its own room
// and never covers other parts of the page. Without one, it floats at the bottom of the screen.
export function VoiceSearchButton({ onResult, examples = [], className = '', label = 'Search by voice', slot = null }) {
  const [done, setDone] = useState('')
  const voice = useVoiceSearch((readings) => {
    const message = onResult(readings)
    setDone(message ? { heard: readings[0], message } : '')
  })
  useEffect(() => {
    if (!done) return undefined
    const timer = setTimeout(() => setDone(''), 2600)
    return () => clearTimeout(timer)
  }, [done])
  useEffect(() => {
    if (voice.status === 'idle') return undefined
    const onKey = (event) => { if (event.key === 'Escape') voice.cancel() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [voice.status, voice.cancel])
  if (!voice.supported) return null

  const listening = voice.status === 'listening'
  const open = listening || voice.status === 'error' || done
  const toggle = () => (listening ? voice.stop() : (setDone(''), voice.start()))

  return <>
    <button type="button" className={`voice-button${listening ? ' is-listening' : ''} ${className}`} onClick={toggle}
      aria-label={t(listening ? 'Stop listening' : label)} title={t(listening ? 'Stop listening' : label)} aria-pressed={listening}>
      <Mic size={17} />
    </button>
    {open && createPortal(<div className={`voice-card${slot ? '' : ' is-floating'}`} role="status" aria-live="polite" aria-label={t('Voice search')}>
      {voice.status === 'error' ? <>
        <span className="voice-orb is-error"><MicOff size={16} /></span>
        <div className="voice-text"><p className="voice-error">{t(ERRORS[voice.error] || ERRORS.failed)}</p></div>
        <button type="button" className="button button-green button-small voice-retry" onClick={() => voice.start()}><Mic size={13} /> {t('Try again')}</button>
      </> : done ? <>
        <span className="voice-orb is-done"><Mic size={16} /></span>
        <div className="voice-text"><p className="voice-heard">“{done.heard}”</p><p className="voice-hint">{done.message}</p></div>
      </> : <>
        <button type="button" className="voice-orb" onClick={voice.stop} aria-label={t('Stop listening')}><span className="voice-ring" /><Mic size={16} /></button>
        <div className="voice-text">
          <p className="voice-heard">{voice.heard ? `“${voice.heard}”` : t('Listening…')}</p>
          {!voice.heard && examples.length > 0 && <p className="voice-hint">{t('Try saying')} {examples.map((e, i) => <em key={i}>“{t(e)}”</em>)}</p>}
        </div>
        <div className="voice-langs" role="group" aria-label={t('Language you speak')}>
          <button type="button" className={voice.lang === 'en' ? 'selected' : ''} onClick={() => voice.setLang('en')}>EN</button>
          <button type="button" className={voice.lang === 'bn' ? 'selected' : ''} onClick={() => voice.setLang('bn')}>বাংলা</button>
        </div>
      </>}
      <button type="button" className="voice-close" onClick={() => { voice.cancel(); voice.dismiss(); setDone('') }} aria-label={t('Close')}><X size={14} /></button>
    </div>, slot || document.body)}
  </>
}
