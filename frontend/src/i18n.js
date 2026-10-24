import bn from './i18n.bn.js'

// The interface language: 'en' (English, the text written in the code) or 'bn' (Bangla).
// t() reads the current language when it is called, so changing it needs a re-render (see App).
const LANGS = ['en', 'bn']
const STORAGE_KEY = 'sc_lang'

let current = (() => {
  try { const saved = localStorage.getItem(STORAGE_KEY); return LANGS.includes(saved) ? saved : 'en' } catch { return 'en' }
})()
document.documentElement.lang = current

export const getLang = () => current

export function setLang(lang) {
  if (!LANGS.includes(lang)) return
  current = lang
  document.documentElement.lang = lang
  try { localStorage.setItem(STORAGE_KEY, lang) } catch { /* the choice just is not remembered */ }
}

const locale = () => current === 'bn' ? 'bn-BD' : 'en-BD'

// Numbers in the current language's digits, e.g. 1,250 or ১,২৫০.
export const formatNumber = (value, options) => Number(value || 0).toLocaleString(locale(), options)

export const formatDate = (value, options = { day: 'numeric', month: 'short', year: 'numeric' }) =>
  value ? new Date(value).toLocaleDateString(locale(), options) : ''

export const formatTime = (value) => value ? new Date(value).toLocaleTimeString(locale(), { hour: '2-digit', minute: '2-digit' }) : ''

export const formatDateTime = (value) => value ? new Date(value).toLocaleString(locale()) : ''

// Translates English UI text. Text with no Bangla entry is shown in English.
// {name} placeholders are filled from vars; numbers are written in the current language's digits.
export function t(text, vars) {
  let out = current === 'bn' ? (bn[text] ?? text) : text
  if (vars) {
    out = out.replace(/\{(\w+)\}/g, (match, key) => {
      if (!(key in vars)) return match
      const value = vars[key]
      return typeof value === 'number' ? formatNumber(value) : String(value ?? '')
    })
  }
  return out
}
