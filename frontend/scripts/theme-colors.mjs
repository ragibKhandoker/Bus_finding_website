// Builds the dark and night themes from the light colours written in the stylesheets.
//
// Every literal colour in a background, text or border declaration is replaced by a variable named after
// its role and light value, e.g. `background:#fff` becomes `background:var(--bg-ffffff)`. src/theme.css then
// gives each variable its light value and the matching dark and night values.
//
// Run it again after adding CSS (it only touches literal colours, so it is safe to re-run):
//   node scripts/theme-colors.mjs
import fs from 'fs'
import path from 'path'

const SRC = path.resolve(path.dirname(new URL(import.meta.url).pathname.replace(/^\/(\w:)/, '$1')), '../src')
const FILES = ['style.css', 'more.css', 'home.css', 'profile.css', 'admin.css']

// Shared custom properties and the role their colour plays where they are used.
const CUSTOM_ROLES = {
  '--ink': 'fg', '--muted': 'fg', '--green-dark': 'fg', '--line': 'bd', '--surface': 'bg', '--paper': 'bg',
  '--hp-ink': 'fg', '--hp-text': 'fg', '--hp-muted': 'fg', '--hp-border': 'bd',
}

function roleOf(prop) {
  if (prop in CUSTOM_ROLES) return CUSTOM_ROLES[prop]
  if (prop.startsWith('--')) return null
  if (/^background(-color|-image)?$/.test(prop)) return 'bg'
  if (/^(color|fill|stroke|caret-color|text-decoration-color)$/.test(prop)) return 'fg'
  if (/^(border|outline)(-(top|right|bottom|left))?(-color)?$/.test(prop)) return 'bd'
  return null // box-shadow, mask-image and the rest keep their colours
}

// ---- colour maths (hex <-> HSL, alpha kept as two hex digits) ----
function normalize(hex) {
  hex = hex.toLowerCase()
  if (hex.length === 3 || hex.length === 4) hex = [...hex].map((c) => c + c).join('')
  return hex
}
function toHsl(hex) {
  const [r, g, b] = [0, 2, 4].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255)
  const max = Math.max(r, g, b), min = Math.min(r, g, b), l = (max + min) / 2
  let h = 0, s = 0
  if (max !== min) {
    const d = max - min
    s = l > 0.5 ? d / (2 - max - min) : d / (max + min)
    h = max === r ? (g - b) / d + (g < b ? 6 : 0) : max === g ? (b - r) / d + 2 : (r - g) / d + 4
    h *= 60
  }
  // chroma: how colourful it is. HSL saturation overstates it for near-whites, so greys are judged by chroma.
  return { h, s, l, a: hex.slice(6), chroma: max - min }
}
// Text greys here carry a little more green than the surface greys, hence the higher limit for text.
const isGrey = (c, limit = 0.06) => c.chroma < limit
// See-through colours (alpha below 50%) are highlights or tints laid over another surface; they stay as they are.
const isSeeThrough = (c) => c.a && parseInt(c.a, 16) < 0x80
function toHex({ h, s, l, a }) {
  s = Math.max(0, Math.min(1, s)); l = Math.max(0, Math.min(1, l))
  const k = (n) => (n + h / 30) % 12
  const f = (n) => l - s * Math.min(l, 1 - l) * Math.max(-1, Math.min(k(n) - 3, 9 - k(n), 1))
  return '#' + [f(0), f(8), f(4)].map((v) => Math.round(v * 255).toString(16).padStart(2, '0')).join('') + (a || '')
}

// Dark: light surfaces become dark (white cards one step above the page), dark text becomes light,
// light borders become dim. Colours that are already dark surfaces or light text, like the brand green
// buttons and the white text on them, stay as they are.
// Greys take a faint green tint so the dark theme matches the brand colour.
const DARK_GREY = { h: 150, s: 0.08 }
function dark(role, c) {
  if (isSeeThrough(c)) return c
  if (role === 'bg') {
    if (c.l <= 0.55) return { ...c, s: c.s * 0.95 }
    const tint = isGrey(c) ? DARK_GREY : { h: c.h, s: Math.min(c.s, 0.28) }
    if (c.l >= 0.99) return { ...c, ...tint, l: 0.165 }
    return { ...c, ...tint, l: 0.11 + (0.97 - Math.min(c.l, 0.97)) * 0.6 }
  }
  if (role === 'fg') {
    if (c.l >= 0.6) return c
    return { ...c, s: Math.min(c.s, 0.75), l: 0.92 - 0.6 * c.l }
  }
  if (c.l <= 0.5) return c
  const tint = isGrey(c) ? DARK_GREY : { h: c.h, s: Math.min(c.s, 0.25) }
  return { ...c, ...tint, l: 0.2 + (0.97 - Math.min(c.l, 0.97)) * 0.45 }
}

// Night: the dark theme on a deep blue base, with warm, slightly dimmer text that is easier on the eyes.
function night(role, c) {
  const d = dark(role, c)
  if (isSeeThrough(c)) return d
  if (role === 'bg') {
    if (c.l <= 0.55) return { ...d, s: d.s * 0.85, l: d.l * 0.92 }
    return isGrey(c) ? { ...d, h: 226, s: 0.38, l: d.l - 0.025 } : { ...d, s: Math.min(d.s, 0.3), l: d.l - 0.025 }
  }
  if (role === 'fg') {
    if (c.l >= 0.6) return { ...d, l: Math.min(d.l, 0.93) }
    return isGrey(c, 0.1) ? { ...d, h: 40, s: 0.32, l: d.l - 0.04 } : { ...d, s: d.s * 0.85, l: d.l - 0.04 }
  }
  if (c.l <= 0.5) return d
  return isGrey(c) ? { ...d, h: 226, s: 0.28 } : d
}

// ---- rewrite the stylesheets ----
const COLOR = /var\([^)]*\)|#([0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{3,4})\b|\b(white|black)\b/g
const DECLARATION = /([{;]\s*)(--[-a-zA-Z]+|[a-zA-Z][-a-zA-Z]*)(\s*:\s*)([^;{}]+)/g
let replaced = 0
for (const file of FILES) {
  const full = path.join(SRC, file)
  const css = fs.readFileSync(full, 'utf8')
  const out = css.replace(DECLARATION, (all, lead, prop, colon, value) => {
    const role = roleOf(prop)
    if (!role) return all
    const next = value.replace(COLOR, (match, hex, named) => {
      if (match.startsWith('var(')) return match
      replaced++
      const key = normalize(hex || (named === 'white' ? 'fff' : '000'))
      return `var(--${role}-${key})`
    })
    return lead + prop + colon + next
  })
  if (out !== css) fs.writeFileSync(full, out)
}

// ---- write the theme variables ----
const used = new Set()
for (const file of FILES) {
  for (const m of fs.readFileSync(path.join(SRC, file), 'utf8').matchAll(/var\(--(bg|fg|bd)-([0-9a-f]{6}(?:[0-9a-f]{2})?)\)/g)) used.add(`${m[1]}-${m[2]}`)
}
const names = [...used].sort()
const block = (selector, fn) => `${selector}{\n${names.map((n) => {
  const [role, hex] = n.split('-')
  return `  --${n}:${fn(role, hex)};`
}).join('\n')}\n}\n`
fs.writeFileSync(path.join(SRC, 'theme.css'), [
  '/* Generated by scripts/theme-colors.mjs from the colours in the stylesheets. Do not edit by hand; re-run the script. */',
  block(':root', (role, hex) => '#' + hex),
  block('html[data-theme="dark"]', (role, hex) => toHex(dark(role, toHsl(hex)))),
  block('html[data-theme="night"]', (role, hex) => toHex(night(role, toHsl(hex)))),
].join('\n'))
console.log(`${replaced} colours replaced, ${names.length} theme variables written to src/theme.css`)
