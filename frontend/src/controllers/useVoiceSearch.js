import { useCallback, useEffect, useRef, useState } from 'react'
import { getLang } from '../models/i18n'

// The browser's speech recogniser (Chrome, Edge, Safari, Samsung Internet), or null where there is none (Firefox).
export const speechRecognition = () => (typeof window === 'undefined' ? null : window.SpeechRecognition || window.webkitSpeechRecognition || null)

export const VOICE_LANG_KEY = 'sc_voice_lang'
export const VOICE_LANGS = { en: 'en-IN', bn: 'bn-BD' } // en-IN hears Dhaka place names far better than en-US

const savedLang = () => {
  try { const saved = localStorage.getItem(VOICE_LANG_KEY); if (saved in VOICE_LANGS) return saved } catch { /* not remembered */ }
  return getLang() === 'bn' ? 'bn' : 'en'
}

// Every reading of what was said: the recogniser's best guess for each part, then the same with one part swapped
// for each of its other guesses, so a name it was unsure of ("Rider"/"Raida") can still be matched.
export function voiceReadings(results) {
  const best = results.map((res) => res[0]?.transcript || '')
  const join = (parts) => parts.join(' ').replace(/\s+/g, ' ').trim()
  const out = [join(best)]
  results.forEach((res, i) => {
    for (let k = 1; k < res.length; k++) out.push(join(best.map((text, j) => (j === i ? res[k].transcript : text))))
  })
  return [...new Set(out.filter(Boolean))].slice(0, 12)
}

// Listening for one spoken search. status: 'idle' | 'listening' | 'error'. While listening, `heard` is what has
// been recognised so far; when the speaker stops, onResult gets every alternative the recogniser offers.
export function useVoiceSearch(onResult) {
  const Recognition = speechRecognition()
  const [status, setStatus] = useState('idle')
  const [heard, setHeard] = useState('')
  const [error, setError] = useState('')
  const [lang, setLangState] = useState(savedLang)
  const recognizer = useRef(null)
  const timers = useRef({})
  const handler = useRef(onResult)
  handler.current = onResult

  const stop = useCallback(() => { recognizer.current?.stop() }, [])
  const cancel = useCallback(() => {
    const r = recognizer.current
    recognizer.current = null
    clearTimeout(timers.current.quiet); clearTimeout(timers.current.limit)
    if (r) { r.onresult = r.onerror = r.onend = null; try { r.abort() } catch { /* already stopped */ } }
    setStatus('idle'); setHeard('')
  }, [])
  useEffect(() => cancel, [cancel])

  const start = useCallback((language = lang) => {
    if (!Recognition) return
    cancel()
    const r = new Recognition()
    r.lang = VOICE_LANGS[language] || VOICE_LANGS.en
    r.interimResults = true
    r.maxAlternatives = 5
    // Keeps listening through short pauses ("Uttara… theke… Motijheel"); it stops itself once the speaker has been
    // quiet for a moment after saying something, or after 12 seconds in all.
    r.continuous = true
    let readings = [], interim = ''
    const clear = () => { clearTimeout(timers.current.quiet); clearTimeout(timers.current.limit) }
    timers.current.limit = setTimeout(() => r.stop(), 12000)
    r.onresult = (event) => {
      const results = Array.from(event.results)
      const text = results.map((res) => res[0].transcript).join(' ').replace(/\s+/g, ' ').trim()
      setHeard(text)
      const finals = results.filter((res) => res.isFinal)
      interim = text
      if (finals.length) readings = voiceReadings(finals)
      clearTimeout(timers.current.quiet)
      timers.current.quiet = setTimeout(() => r.stop(), finals.length === results.length ? 1300 : 2200)
    }
    r.onerror = (event) => {
      if (event.error === 'aborted') return
      if (event.error === 'no-speech' && (readings.length || interim)) return
      readings = []; interim = ''
      setError(event.error === 'not-allowed' || event.error === 'service-not-allowed' ? 'mic-blocked'
        : event.error === 'no-speech' ? 'no-speech' : event.error === 'network' ? 'network' : 'failed')
      setStatus('error')
    }
    r.onend = () => {
      clear()
      if (recognizer.current !== r) return
      recognizer.current = null
      // Stopped before the last words were final: what was heard so far is still worth trying.
      const said = interim && interim !== readings[0] ? [interim, ...readings] : readings
      if (said.length) { setStatus('idle'); setHeard(''); handler.current?.(said) }
      else { setError((e) => e || 'no-speech'); setStatus('error') }
    }
    recognizer.current = r
    setError(''); setHeard(''); setStatus('listening')
    try { r.start() } catch { recognizer.current = null; setError('failed'); setStatus('error') }
  }, [Recognition, cancel, lang])

  const setLang = useCallback((next) => {
    setLangState(next)
    try { localStorage.setItem(VOICE_LANG_KEY, next) } catch { /* not remembered */ }
    if (recognizer.current) start(next)
  }, [start])

  return { supported: Boolean(Recognition), status, heard, error, lang, setLang, start, stop, cancel, dismiss: () => { setStatus('idle'); setError('') } }
}
