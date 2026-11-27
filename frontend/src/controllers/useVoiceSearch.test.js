import { describe, expect, it } from 'vitest'
import { voiceReadings } from './useVoiceSearch'

const part = (...texts) => texts.map((transcript) => ({ transcript }))

describe('voiceReadings', () => {
  it('joins the parts said with pauses, and tries each part’s other guesses', () => {
    expect(voiceReadings([part('Uttara'), part('theke Motijheel', 'take Moti jheel')]))
      .toEqual(['Uttara theke Motijheel', 'Uttara take Moti jheel'])
  })
})
