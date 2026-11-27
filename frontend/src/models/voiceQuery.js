import { toBangla } from './i18n'

// Voice search: turning what was said ("Uttara theke Motijheel", "I want to go from Farmgate to Gulistan",
// "উত্তরা থেকে মতিঝিল যাব", "Raida", "DBR 12") into a search, matched against the stops, buses and route numbers in
// the database.
//
// Speech recognisers write Dhaka place names in many ways ("Moti Jheel", "Maha Kali", "Uttara 2 Motijheel" for
// "Uttara to Motijheel"), and in Bangla with their own spelling. So names are compared by how they sound: English
// and Bangla are both turned into the same rough Latin sound key, and the places are looked for anywhere in the
// sentence rather than in fixed positions.

const BN_DIGITS = '০১২৩৪৫৬৭৮৯'

// Letters and digits only, lower case, Bangla digits as 0-9: "Mohakhali ", "mohakhali" and "MOHAKHALI" are one key.
export const voiceKey = (text) => String(text || '').toLowerCase()
  .replace(/[০-৯]/g, (d) => String(BN_DIGITS.indexOf(d)))
  .replace(/[^\p{L}\p{N}\p{M}]+/gu, '')

// Numbers said as words, in English and Bangla: "Mirpur ten", "মিরপুর দশ", "Dhanmondi twenty seven".
const NUMBER_WORDS = {
  one: 1, two: 2, three: 3, four: 4, five: 5, six: 6, seven: 7, eight: 8, nine: 9, ten: 10, eleven: 11, twelve: 12,
  thirteen: 13, fourteen: 14, fifteen: 15, sixteen: 16, seventeen: 17, eighteen: 18, nineteen: 19, twenty: 20, thirty: 30,
  forty: 40, fifty: 50, sixty: 60, seventy: 70, eighty: 80, ninety: 90, hundred: 100,
  এক: 1, দুই: 2, তিন: 3, চার: 4, পাঁচ: 5, ছয়: 6, সাত: 7, আট: 8, নয়: 9, দশ: 10, এগারো: 11, বারো: 12, তেরো: 13, চৌদ্দ: 14,
  পনেরো: 15, ষোলো: 16, সতেরো: 17, আঠারো: 18, উনিশ: 19, বিশ: 20, সাতাশ: 27, বত্রিশ: 32, তিনশো: 300,
}
// Bangla numbers as an English recogniser writes them when they are said in Bangla: "Mirpur egaro" often comes out
// as "Mirpur agro", "Mirpur baro" as "Mirpur bar". Some are also real words in stop names ("Sat rasta",
// "Ghatar Char"), so these are only tried as one reading of what was said, next to the words as heard.
const SPOKEN_BN_NUMBERS = {
  ek: 1, aek: 1, dui: 2, dooi: 2, duy: 2, tin: 3, teen: 3, char: 4, chaar: 4, panch: 5, paanch: 5, pach: 5, choy: 6, chhoy: 6,
  choi: 6, chhay: 6, sat: 7, saat: 7, aat: 8, aath: 8, noy: 9, noi: 9, nay: 9, dosh: 10, dos: 10, dash: 10, das: 10, dus: 10,
  egaro: 11, egaroh: 11, agaro: 11, agro: 11, aagro: 11, aggro: 11, egro: 11, egar: 11, agar: 11, eagro: 11, igaro: 11, gyarah: 11, gyara: 11,
  baro: 12, baaro: 12, bar: 12, bara: 12, barah: 12, barro: 12, tero: 13, tera: 13, terah: 13, tiro: 13,
  choddo: 14, chouddo: 14, choudda: 14, chodda: 14, chaudah: 14, chowddo: 14, choto: 14, ponero: 15, panero: 15, pondro: 15, pandrah: 15,
  sholo: 16, solo: 16, sotero: 17, satero: 17, atharo: 18, unish: 19, bish: 20, satash: 27, shatash: 27, sataish: 27, batrish: 32, bottrish: 32, botrish: 32,
}
// Bangla script spellings the recogniser uses besides the usual ones.
Object.assign(NUMBER_WORDS, { এগার: 11, এগারোর: 11, বার: 12, বারোর: 12, তের: 13, চোদ্দ: 14, চৌদ্দোর: 14, পনের: 15, দুইয়ের: 2, একের: 1, ছয়ের: 6, দশের: 10 })
const ALL_NUMBER_WORDS = { ...SPOKEN_BN_NUMBERS, ...NUMBER_WORDS }

export function wordsToDigits(text, numbers = NUMBER_WORDS) {
  const words = String(text || '').split(/\s+/).filter(Boolean)
  const out = []
  const value = (word) => numbers[(word || '').toLowerCase().replace(/[.,!?।]+$/, '')]
  for (let i = 0; i < words.length; i++) {
    const n = value(words[i])
    if (n === undefined) { out.push(words[i]); continue }
    // "twenty seven" → 27, "three hundred" → 300
    const next = value(words[i + 1])
    if (n >= 20 && n < 100 && next > 0 && next < 10) { out.push(String(n + next)); i++ }
    else if (n < 10 && next === 100) { out.push(String(n * 100)); i++ }
    else out.push(String(n))
  }
  return out.join(' ')
}

// The readings of one sentence to try: as heard, with number words as digits, and also with Bangla numbers
// written in English letters as digits ("Mirpur agro" → "Mirpur 11").
export const numberReadings = (text) => [...new Set([text, wordsToDigits(text), wordsToDigits(text, ALL_NUMBER_WORDS)])]

// Bangla letters as rough Latin sounds, so "মতিঝিল" and "Motijheel" end up alike.
const BN_CONSONANTS = {
  ক: 'k', খ: 'k', গ: 'g', ঘ: 'g', ঙ: 'ng', চ: 'c', ছ: 'c', জ: 'j', ঝ: 'j', ঞ: 'n', ট: 't', ঠ: 't', ড: 'd', ঢ: 'd', ণ: 'n',
  ত: 't', থ: 't', দ: 'd', ধ: 'd', ন: 'n', প: 'p', ফ: 'f', ব: 'b', ভ: 'b', ম: 'm', য: 'j', র: 'r', ল: 'l', শ: 's', ষ: 's',
  স: 's', হ: 'h', ড়: 'r', ঢ়: 'r', য়: 'y', ৎ: 't',
}
const BN_VOWELS = { অ: 'o', আ: 'a', ই: 'i', ঈ: 'i', উ: 'u', ঊ: 'u', ঋ: 'ri', এ: 'e', ঐ: 'oi', ও: 'o', ঔ: 'ou' }
const BN_SIGNS = { 'া': 'a', 'ি': 'i', 'ী': 'i', 'ু': 'u', 'ূ': 'u', 'ৃ': 'ri', 'ে': 'e', 'ৈ': 'oi', 'ো': 'o', 'ৌ': 'ou' }
export function bnToLatin(text) {
  const chars = [...String(text || '').normalize('NFC').replace(/ড়/g, 'ড়').replace(/ঢ়/g, 'ঢ়').replace(/য়/g, 'য়')]
  let out = ''
  for (let i = 0; i < chars.length; i++) {
    const c = chars[i], next = chars[i + 1]
    if (BN_CONSONANTS[c] !== undefined) {
      out += BN_CONSONANTS[c]
      // The inherent vowel: none before a vowel sign or a hasanta, none at the end of a word, "o" otherwise
      // (often silent in the middle of a word too; the consonant comparison below allows for that).
      if (next && !BN_SIGNS[next] && next !== '্' && next !== 'ং' && next !== 'ঁ' && /[ঀ-৿]/.test(next) && BN_CONSONANTS[next] !== undefined) out += 'o'
    } else if (BN_VOWELS[c]) out += BN_VOWELS[c]
    else if (BN_SIGNS[c]) out += BN_SIGNS[c]
    else if (c === 'ং') out += 'ng'
    else if (c === '্' || c === 'ঁ' || c === 'ঃ') continue
    else if (/[০-৯]/.test(c)) out += String(BN_DIGITS.indexOf(c))
    else out += c
  }
  return out
}

// How a name sounds, in plain Latin letters: "Motijheel", "Moti Jheel", "motijhil" and "মতিঝিল" give "matijil".
export function soundKey(text) {
  let s = voiceKey(/[ঀ-৿]/.test(String(text)) ? bnToLatin(text) : text)
  s = s.replace(/ph/g, 'f').replace(/ch/g, 'C').replace(/sh/g, 's').replace(/([kgjtdbpC])h/g, '$1')
    .replace(/c/g, 'k').replace(/C/g, 'c').replace(/q/g, 'k').replace(/z/g, 'j').replace(/v/g, 'b').replace(/w/g, 'o').replace(/x/g, 'ks')
    .replace(/ee|ea|ie/g, 'i').replace(/oo|ou/g, 'u').replace(/y/g, 'i').replace(/o/g, 'a').replace(/h$/, '').replace(/([^aeiu])(er|ar)$/, '$1a')
    .replace(/(\p{L})\1+/gu, '$1') // doubled letters only: "Uttara" is "Utara", but 11 stays 11
  return s
}
// Only the consonants of a sound key, so a vowel heard differently ("Panthopoth"/"Panthapath") does not matter.
const skeleton = (key) => key.replace(/[aeiu]/g, '')

// How many single-letter edits turn one key into the other.
function distance(a, b) {
  if (a === b) return 0
  let row = Array.from({ length: b.length + 1 }, (_, i) => i)
  for (let i = 1; i <= a.length; i++) {
    const next = [i]
    for (let j = 1; j <= b.length; j++) next[j] = Math.min(row[j] + 1, next[j - 1] + 1, row[j - 1] + (a[i - 1] === b[j - 1] ? 0 : 1))
    row = next
  }
  return row[b.length]
}
const likeness = (a, b) => (!a || !b ? 0 : 1 - distance(a, b) / Math.max(a.length, b.length))

// How well a spoken piece fits a name (both as sound keys): 1 is the same, 0 is nothing alike. A spoken piece that
// is only the start of a longer name ("Mirpur" for "Mirpur 10") fits less well than the whole name would.
function fit(spoken, name) {
  if (!spoken || !name) return 0
  if (spoken === name) return 1
  const short = Math.min(spoken.length, name.length)
  let score = likeness(spoken, name)
  // Only the start of a longer name ("Dhaka" for "Dhaka College") gets no bonus: it is not that place.
  const a = skeleton(spoken), b = skeleton(name)
  if (a.length >= 3 && a === b) score = Math.max(score, 0.9)
  else if (a.length >= 3 && b.length >= 3) score = Math.max(score, likeness(a, b) * 0.9)
  // Short names (MES, IDB, CMH, Badda) must be said nearly exactly.
  if (name.length <= 4 && score < 0.9) score *= 0.85
  return score
}

// The sound keys of every name in a list, worked out once per list.
const keyCache = new WeakMap()
function namesWithKeys(names) {
  let keyed = keyCache.get(names)
  if (!keyed) {
    keyed = names.map((name) => {
      const keys = new Set([soundKey(name), soundKey(toBangla(name))].filter(Boolean))
      return { name, keys: [...keys] }
    })
    keyCache.set(names, keyed)
  }
  return keyed
}

// The best fitting name for a spoken piece, with its score.
function closest(spoken, names) {
  const key = soundKey(spoken)
  let best = null, score = 0
  if (!key) return { name: null, score: 0 }
  const exact = voiceKey(spoken)
  for (const entry of namesWithKeys(names)) {
    for (const k of entry.keys) {
      const s = fit(key, k) + (voiceKey(entry.name) === exact ? 0.0005 : 0)
      if (s > score || (s === score && best && entry.name.length < best.length)) { best = entry.name; score = s }
    }
  }
  return { name: best, score }
}

// Words around the places that say nothing about them: "I want to go", "bus", "please", "jabo", "যাব"…
const FILLER_WORDS = new Set(`i im i'm me my we want wanna need would like to go going goes get take took reach travel
  a an the is are am at in on of and or for with by please bus buses route routes find search show tell which what
  where how there here now today give any can could will shall let lets let's from too two till until up upto
  theke thake theka thekey teke thekee hote hoite se tak porjonto porjanto parjanto jabo jaabo jaba jete jaite jai
  chai chaai jaw kon ki ache achhe ase bolo dekhao amake ami amar
  থেকে থিকা হতে হইতে যাব যাবো যাবে যেতে যাই চাই পর্যন্ত কোন কি কী আছে বাস রুট আমি আমার আমাকে দেখাও খুঁজুন খুঁজে দাও বলো এবং আর
  হবে যায় যাওয়া number no nombor nambar numbar নম্বর নাম্বার নং`.split(/\s+/).filter(Boolean))
// Words that mark the start ("from Uttara", "Uttara theke") or the end ("to Motijheel") of the trip.
const FROM_BEFORE = new Set(['from', 'frm'])
const FROM_AFTER = new Set(['theke', 'thake', 'theka', 'thekey', 'teke', 'thekee', 'hote', 'hoite', 'se', 'থেকে', 'থিকা', 'হতে', 'হইতে'])
const TO_BEFORE = new Set(['to', 'too', 'till', 'until', 'upto'])
const TO_AFTER = new Set(['porjonto', 'porjanto', 'parjanto', 'tak', 'jabo', 'jaabo', 'jete', 'পর্যন্ত', 'যাব', 'যাবো', 'যেতে'])

const tokensOf = (text) => String(text || '').replace(/[.,!?।"“”]+/g, ' ').replace(/(→|->|–|—)/g, ' to ')
  // "Mirpur-11" and "mirpur11" are "Mirpur 11"
  .replace(/([\p{L}])-?(\d)/gu, '$1 $2').split(/\s+/).filter(Boolean)
const isFiller = (word) => FILLER_WORDS.has(word.toLowerCase())

// Bangla endings on a place ("মতিঝিলে", "উত্তরার", "Motijheel-e") are left off before matching.
const dropEnding = (word) => (/[ঀ-৿]/.test(word) ? word.replace(/(তে|এর|ের|র|ে|য়)$/u, '') || word : word)

export const stripFiller = (text) => tokensOf(text).filter((w) => !isFiller(w)).join(' ')

// The stops said in a sentence, in the order said: each { stop, score, start, end }.
export function findPlaces(text, stops, min = 0.78) {
  const words = tokensOf(text)
  const found = []
  for (let i = 0; i < words.length; i++) {
    if (isFiller(words[i])) continue
    for (let n = 1; n <= 4 && i + n <= words.length; n++) {
      const piece = words.slice(i, i + n)
      if (isFiller(piece[n - 1]) && !/^\d+$/.test(piece[n - 1])) break
      const spoken = [...piece.slice(0, -1), dropEnding(piece[n - 1])].join(' ')
      // Each piece as heard and with its number words as digits, whichever fits a stop better.
      let name = null, score = 0
      for (const reading of numberReadings(spoken)) {
        const hit = closest(reading, stops)
        if (hit.score > score) ({ name, score } = hit)
      }
      if (name && score >= min) found.push({ stop: name, score: score + n * 0.001, start: i, end: i + n })
    }
  }
  // The best fits first, keeping only pieces of the sentence not used yet.
  const used = new Set(), chosen = []
  for (const f of found.sort((a, b) => b.score - a.score)) {
    let free = true
    for (let k = f.start; k < f.end; k++) if (used.has(k)) free = false
    if (!free) continue
    for (let k = f.start; k < f.end; k++) used.add(k)
    chosen.push(f)
  }
  return { words, places: chosen.sort((a, b) => a.start - b.start) }
}

// Whether a sentence has words besides this place and words like "to", "theke", "I want to go": another place
// was said that is not a stop ("Delhi to Dhaka College"), unlike "to Motijheel" alone.
const otherWords = (words, p) => words.some((w, i) => (i < p.start || i >= p.end) && !isFiller(w) && !/^\d+$/.test(w))

// Whether a place found in a sentence is where the trip starts ("from X", "X theke") or ends ("to X", "X porjonto").
function roleOf(words, p) {
  const before = (words[p.start - 1] || '').toLowerCase(), after = (words[p.end] || '').toLowerCase()
  if (FROM_BEFORE.has(before) || FROM_AFTER.has(after)) return 'from'
  if (TO_BEFORE.has(before) || TO_AFTER.has(after)) return 'to'
  return null
}

// The two places of a trip in a sentence, the start first: "from X to Y", "X theke Y", "to Y from X", "X Y".
export function tripFromSentence(text, stops) {
  const { words, places } = findPlaces(text, stops)
  const distinct = places.filter((p, i) => places.findIndex((q) => q.stop === p.stop) === i)
  if (distinct.length < 2) return null
  const role = (p) => roleOf(words, p)
  const from = distinct.find((p) => role(p) === 'from')
  const to = distinct.find((p) => role(p) === 'to' && p !== from)
  if (from && to) return { from: from.stop, to: to.stop, score: Math.min(from.score, to.score) }
  if (from) { const other = distinct.find((p) => p !== from); return { from: from.stop, to: other.stop, score: Math.min(from.score, other.score) } }
  if (to) { const other = distinct.find((p) => p !== to); return { from: other.stop, to: to.stop, score: Math.min(other.score, to.score) } }
  const [a, b] = [distinct[0], distinct[distinct.length - 1]]
  return { from: a.stop, to: b.stop, score: Math.min(a.score, b.score), plain: true }
}

// "from X to Y", "X to Y", "X theke Y", "X থেকে Y", "X → Y" as typed: the two pieces, or null for only one.
// (The Bus routes box uses this for "Uttara → Motijheel".)
export function splitFromTo(text) {
  const said = String(text || '').replace(/\s+/g, ' ').trim()
  const patterns = [
    /^(?:.*?\bfrom\s+)?(.+?)\s+(?:to|too|till|upto|up to)\s+(.+)$/iu,
    /^(.+?)\s*(?:থেকে|থিকা|হতে|theke|thake|theka|thekey|hote|hoite)\s+(.+?)(?:\s+(?:er|এর))?$/iu,
    /^(.+?)\s*(?:→|->|–|—)\s*(.+)$/u,
  ]
  for (const pattern of patterns) {
    const match = said.match(pattern)
    if (!match) continue
    const from = stripFiller(match[1]), to = stripFiller(match[2])
    if (from && to) return { from, to }
  }
  return null
}

// The name in the list closest to what was said, in English or Bangla, or null when nothing is close enough.
export function bestMatch(spoken, names, min = 0.78) {
  const cleaned = stripFiller(spoken)
  if (!cleaned) return null
  const { name, score } = closest(cleaned, names)
  return score >= min ? name : null
}

// Every stop name in the route list, once.
export const stopNames = (buses) => [...new Set((buses || []).flatMap((bus) => (bus.stops || []).map((stop) => stop.name)).filter(Boolean))]
// Every bus name in the route list, once.
export const busNames = (buses) => [...new Set((buses || []).map((bus) => (bus.busName || '').trim()).filter(Boolean))]
// Every route number, once.
export const routeNumbers = (buses) => [...new Set((buses || []).map((bus) => bus.routeNumber).filter(Boolean))]

const listCache = new WeakMap()
const listsOf = (buses) => {
  let lists = listCache.get(buses)
  if (!lists) { lists = { stops: stopNames(buses), names: busNames(buses), numbers: routeNumbers(buses) }; listCache.set(buses, lists) }
  return lists
}
const hasTripWords = (text) => tokensOf(text).some((w) => { const l = w.toLowerCase(); return FROM_BEFORE.has(l) || FROM_AFTER.has(l) || TO_BEFORE.has(l) })

// What a spoken phrase asks for, given the routes in the database:
//  { kind: 'route', from, to }  two places (matched to stops where possible, otherwise as said);
//  { kind: 'bus', name }        a bus name;
//  { kind: 'number', route }    a route number, e.g. DBR-12;
//  { kind: 'stop', stop }       one place;
//  { kind: 'text', text }       nothing matched: the words as said.
// Several transcripts (the recogniser's alternatives) can be given; the best reading of them all wins.
export function readVoice(transcripts, buses) {
  const heard = (Array.isArray(transcripts) ? transcripts : [transcripts]).map((x) => String(x || '').trim()).filter(Boolean)
  if (!heard.length) return null
  const list = [...new Set(heard.flatMap(numberReadings))]
  const { stops, names, numbers } = listsOf(buses || [])

  // A route number: "DBR 12", "dbr twelve".
  for (const said of list) {
    const key = voiceKey(stripFiller(said))
    const number = key && numbers.find((r) => voiceKey(r) === key)
    if (number) return { kind: 'number', route: number, said }
  }
  // A bus said by its name alone ("Raida", "Victor Classic", "Mirpur Link"), unless the sentence is about a trip.
  let bus = null
  for (const said of list) {
    if (hasTripWords(said)) continue
    const cleaned = stripFiller(said)
    const { name, score } = closest(cleaned, names)
    if (name && score >= 0.86 && (!bus || score > bus.score)) bus = { name, score, said }
  }
  // Two places: the best reading among the alternatives. Words like "to" or "theke" make it surely a trip.
  let trip = null
  for (const said of list) {
    const found = tripFromSentence(said, stops)
    if (found && (!trip || found.score > trip.score)) trip = { ...found, said }
  }
  if (trip && (!bus || !trip.plain || trip.score > bus.score)) return { kind: 'route', from: trip.from, to: trip.to, said: trip.said }
  if (bus) return { kind: 'bus', name: bus.name, said: bus.said }
  // One place.
  // The place said most clearly: "Mirpur 11" said exactly beats "Mirpur" alone matching "Mirpur 1".
  let place = null
  for (const said of list) {
    const { words, places } = findPlaces(said, stops, 0.82)
    if (places.length === 1 && (!place || places[0].score > place.score)) place = { ...places[0], role: roleOf(words, places[0]), trip: hasTripWords(said) && otherWords(words, places[0]), said }
  }
  // role: 'from' or 'to' when the sentence says which end it is; missing: true when the sentence was about a trip
  // but its other place is not a stop in the database ("Delhi to Dhaka College").
  if (place) return { kind: 'stop', stop: place.stop, role: place.role, missing: place.trip, said: place.said }
  // A bus name said less clearly.
  for (const said of list) {
    if (hasTripWords(said)) continue
    const name = bestMatch(said, names, 0.8)
    if (name) return { kind: 'bus', name, said }
  }
  // Nothing matched a stop: never search for places as heard ("Delhi"), since no bus goes there.
  return { kind: 'text', text: stripFiller(heard[0]) || heard[0], said: heard[0] }
}

// ---- Bus routes page: what to put in its search box ----

// Bus names said in part, as people do: "Victor" for Victor Classic and Victor Paribahan, "Bikash" for Bikash
// Paribahan. The best fitting name or leading part of one: { text, score }, text being the stored spelling.
function busByName(spoken, names) {
  const key = soundKey(spoken)
  if (!key) return null
  let best = null
  for (const name of names) {
    const en = name.split(/\s+/), bn = toBangla(name).split(/\s+/)
    for (let k = 1; k <= en.length; k++) {
      const part = en.slice(0, k).join(' ')
      const keys = [soundKey(part)]
      if (bn.length === en.length || k === en.length) keys.push(soundKey((k === en.length ? bn : bn.slice(0, k)).join(' ')))
      let score = Math.max(...keys.map((x) => fit(key, x)))
      if (k < en.length) score -= 0.01 // the whole name wins when it was said
      if (voiceKey(spoken) === voiceKey(part)) score += 0.005 // and the spelling heard wins among names that sound alike
      if (score > (best?.score ?? 0)) best = { text: k === en.length ? name : part, score }
    }
  }
  return best
}

// A route number however it was said: "DBR 12", "D B R twelve", "DVR 12", "route 12", "12 number", "DBR baro".
function routeByNumber(said, numbers) {
  for (const reading of numberReadings(said)) {
    const words = tokensOf(reading)
    const digits = words.filter((w) => /^\d+$/.test(w))
    if (digits.length !== 1) continue
    const rest = words.filter((w) => !/^\d+$/.test(w) && !isFiller(w) && !['rut', 'root', 'rout', 'রুট'].includes(w.toLowerCase())).join('')
    const asked = /\b(route|root|rout|rut|number|no|nambar|nombor)\b|রুট|নম্বর|নং/i.test(reading)
    const candidates = numbers.filter((r) => (r.match(/\d+/) || [])[0] === digits[0])
    if (!candidates.length) continue
    if (!rest) { if (asked) return candidates.find((r) => /^DBR/i.test(r)) || candidates[0]; continue }
    const letters = soundKey(rest)
    let best = null, score = 0
    for (const r of candidates) {
      const prefix = soundKey(r.replace(/[-\s]*\d+.*$/, ''))
      const s = letters === prefix ? 1 : likeness(letters, prefix)
      if (s > score) { best = r; score = s }
    }
    if (best && score >= 0.5) return best
  }
  return null
}

// What to search the route list for, from what was said: a route number, two places ("Uttara → Motijheel"), a bus
// name or the start of one, or a stop. { text, kind } with kind 'number' | 'route' | 'bus' | 'stop' | 'text'.
export function readRouteSearch(transcripts, buses) {
  const heard = (Array.isArray(transcripts) ? transcripts : [transcripts]).map((x) => String(x || '').trim()).filter(Boolean)
  if (!heard.length) return null
  const { stops, names, numbers } = listsOf(buses || [])
  for (const said of heard) {
    const number = routeByNumber(said, numbers)
    if (number) return { kind: 'number', text: number, said }
  }
  let bus = null
  for (const said of heard) {
    if (hasTripWords(said)) continue
    for (const reading of numberReadings(stripFiller(said))) {
      const hit = busByName(reading, names)
      if (hit && hit.score >= 0.8 && (!bus || hit.score > bus.score)) bus = { ...hit, said }
    }
  }
  let trip = null
  for (const said of heard.flatMap(numberReadings)) {
    const found = tripFromSentence(said, stops)
    if (found && (!trip || found.score > trip.score)) trip = { ...found, said }
  }
  if (trip && (!bus || !trip.plain || trip.score > bus.score)) return { kind: 'route', from: trip.from, to: trip.to, said: trip.said }
  let place = null
  for (const said of heard.flatMap(numberReadings)) {
    const { places } = findPlaces(said, stops, 0.82)
    if (places.length === 1 && (!place || places[0].score > place.score)) place = { ...places[0], said }
  }
  // A bus name and a stop can both fit ("Mirpur", "Savar"): the closer one wins.
  if (bus && (!place || bus.score + 0.01 >= place.score)) return { kind: 'bus', text: bus.text, said: bus.said }
  if (place) return { kind: 'stop', text: place.stop, said: place.said }
  return { kind: 'text', text: stripFiller(heard[0]) || heard[0], said: heard[0] }
}
