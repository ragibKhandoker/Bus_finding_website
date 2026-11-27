import { describe, expect, it } from 'vitest'
import { bestMatch, readRouteSearch, readVoice, splitFromTo, stripFiller } from './voiceQuery'

const buses = [
  { busName: 'Raida', routeNumber: 'DBR-12', stops: [{ name: 'Uttara' }, { name: 'Mohakhali' }, { name: 'Motijheel' }] },
  { busName: 'Victor Classic', routeNumber: 'DBR-40', stops: [{ name: 'Farmgate' }, { name: 'Gulistan' }] },
]

describe('voice search', () => {
  it('splits English, Banglish and Bangla from/to phrases', () => {
    expect(splitFromTo('from Uttara to Motijheel')).toEqual({ from: 'Uttara', to: 'Motijheel' })
    expect(splitFromTo('I want to go from Farmgate to Gulistan')).toEqual({ from: 'Farmgate', to: 'Gulistan' })
    expect(splitFromTo('Uttara theke Motijheel jabo')).toEqual({ from: 'Uttara', to: 'Motijheel' })
    expect(splitFromTo('উত্তরা থেকে মতিঝিল যাব')).toEqual({ from: 'উত্তরা', to: 'মতিঝিল' })
    expect(splitFromTo('Uttara → Motijheel')).toEqual({ from: 'Uttara', to: 'Motijheel' })
    expect(splitFromTo('Raida')).toBeNull()
  })

  it('drops filler words', () => {
    expect(stripFiller('Raida bus please')).toBe('Raida')
  })

  it('matches near spellings and Bangla to the stored names', () => {
    const stops = ['Uttara', 'Mohakhali', 'Motijheel']
    expect(bestMatch('mohakhal', stops)).toBe('Mohakhali')
    expect(bestMatch('motijhil', stops)).toBe('Motijheel')
    expect(bestMatch('উত্তরা', stops)).toBe('Uttara')
    expect(bestMatch('banani', stops)).toBeNull()
  })

  it('finds the two places anywhere in what was said, however the recogniser wrote them', () => {
    const more = [...buses, { busName: 'Akash', routeNumber: 'DBR-50', stops: [{ name: 'Mirpur 10' }, { name: 'Jatrabari' }, { name: 'Farmgate' }] }]
    const trip = (said) => { const r = readVoice([said], more); return r.kind === 'route' ? `${r.from} > ${r.to}` : r.kind }
    expect(trip('I want to go from Farmgate to Gulistan')).toBe('Farmgate > Gulistan')
    expect(trip('Uttara 2 Motijheel')).toBe('Uttara > Motijheel')
    expect(trip('Uttara thekey Moti jheel jabo')).toBe('Uttara > Motijheel')
    expect(trip('Maha Kali to Jatra bari')).toBe('Mohakhali > Jatrabari')
    expect(trip('Mirpur ten to Motijheel')).toBe('Mirpur 10 > Motijheel')
    expect(trip('to Motijheel from Uttara')).toBe('Uttara > Motijheel')
    expect(trip('Uttara Motijheel')).toBe('Uttara > Motijheel')
    expect(trip('মিরপুর দশ থেকে মতিঝিলে যাব')).toBe('Mirpur 10 > Motijheel')
    expect(trip('Thor versus Captain America')).toBe('text')
  })

  it('tells numbered stops apart, however the number was said', () => {
    const mirpur = [{ busName: 'Akash', routeNumber: 'DBR-50', stops: ['Mirpur 1', 'Mirpur 10', 'Mirpur 11', 'Mirpur 12', 'Sat rasta', 'Motijheel'].map((name) => ({ name })) }]
    const stop = (said) => readVoice([said], mirpur).stop
    for (const said of ['Mirpur 11', 'Mirpur eleven', 'Mirpur agro', 'Mirpur egaro', 'Mirpur-11', 'মিরপুর এগারো', 'মিরপুর ১১', 'Mirpur 11 number']) expect(stop(said)).toBe('Mirpur 11')
    expect(stop('Mirpur 1')).toBe('Mirpur 1')
    expect(stop('Mirpur dosh')).toBe('Mirpur 10')
    expect(stop('Mirpur baro')).toBe('Mirpur 12')
    // "Sat" is a number word, but here it is part of the stop's name.
    expect(readVoice(['Sat rasta to Mirpur agro'], mirpur)).toMatchObject({ kind: 'route', from: 'Sat rasta', to: 'Mirpur 11' })
  })

  it('never searches for a place that is not a stop, and keeps the one that is in the right field', () => {
    const city = [...buses, { busName: 'Akash', routeNumber: 'DBR-50', stops: [{ name: 'Dhaka College' }, { name: 'Uttara' }] }]
    // "Dhaka" is only the start of "Dhaka College": not that stop.
    expect(readVoice(['Delhi to Dhaka'], city)).toMatchObject({ kind: 'text' })
    expect(readVoice(['Delhi to Dhaka College'], city)).toMatchObject({ kind: 'stop', stop: 'Dhaka College', role: 'to', missing: true })
    expect(readVoice(['to Motijheel'], city)).toMatchObject({ kind: 'stop', stop: 'Motijheel', role: 'to', missing: false })
    expect(readVoice(['Uttara theke'], city)).toMatchObject({ kind: 'stop', stop: 'Uttara', role: 'from', missing: false })
  })

  it('reads a whole phrase into a search', () => {
    expect(readVoice(['uttara theke motijhil'], buses)).toMatchObject({ kind: 'route', from: 'Uttara', to: 'Motijheel' })
    expect(readVoice(['উত্তরা থেকে মতিঝিল'], buses)).toMatchObject({ kind: 'route', from: 'Uttara', to: 'Motijheel' })
    expect(readVoice(['raida bus'], buses)).toMatchObject({ kind: 'bus', name: 'Raida' })
    expect(readVoice(['DBR 12'], buses)).toMatchObject({ kind: 'number', route: 'DBR-12' })
    expect(readVoice(['farmgate'], buses)).toMatchObject({ kind: 'stop', stop: 'Farmgate' })
    expect(readVoice(['something else'], buses)).toMatchObject({ kind: 'text' })
    // A later alternative from the recogniser is used when the first does not match.
    expect(readVoice(['something else', 'raida'], buses)).toMatchObject({ kind: 'bus', name: 'Raida' })
  })

  it('reads bus names, route numbers and places for the Bus routes search', () => {
    const list = [...buses,
      { busName: 'Victor Paribahan', routeNumber: 'DBR-41', stops: [] }, { busName: 'Bikash Paribahan', routeNumber: 'DBR-60', stops: [] },
      { busName: 'Savar Paribahan', routeNumber: 'DBR-70', stops: [{ name: 'Savar' }] }, { busName: 'Dipon', routeNumber: 'DBR-80', stops: [] },
      { busName: 'Deepan', routeNumber: 'DBR-81', stops: [] }, { busName: 'BRTC', routeNumber: 'BRTC-5', stops: [] }]
    const search = (said) => readRouteSearch([said], list)
    expect(search('Victor')).toMatchObject({ kind: 'bus', text: 'Victor' }) // both Victor buses
    expect(search('Victor Classic')).toMatchObject({ kind: 'bus', text: 'Victor Classic' })
    expect(search('Rider')).toMatchObject({ kind: 'bus', text: 'Raida' })
    expect(search('রাইদা')).toMatchObject({ kind: 'bus', text: 'Raida' })
    expect(search('B R T C')).toMatchObject({ kind: 'bus', text: 'BRTC' })
    expect(search('Savar Paribahan')).toMatchObject({ kind: 'bus', text: 'Savar Paribahan' })
    expect(search('Dipon')).toMatchObject({ kind: 'bus', text: 'Dipon' })
    for (const said of ['DBR 12', 'DBR twelve', 'D B R 12', 'DVR 12', 'route 12', '12 number', 'DBR baro']) expect(search(said)).toMatchObject({ kind: 'number', text: 'DBR-12' })
    expect(search('Uttara to Motijheel')).toMatchObject({ kind: 'route', from: 'Uttara', to: 'Motijheel' })
    expect(search('Motijheel')).toMatchObject({ kind: 'stop', text: 'Motijheel' })
    expect(search('Thor')).toMatchObject({ kind: 'text' })
  })
})
