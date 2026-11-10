import React, { useState } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

const BUSES = [
  { busName: 'Raida', routeNumber: '36', operator: 'Raida Enterprise', stops: [{ name: 'Uttara' }, { name: 'Motijheel' }] },
  { busName: 'BRTC Double Decker', routeNumber: 'BRTC-1', operator: 'BRTC', stops: [{ name: 'Mirpur 12' }, { name: 'Motijheel' }] },
  { busName: 'Victor Classic', routeNumber: '8', operator: 'Victor', stops: [{ name: 'Sadarghat' }, { name: 'Abdullahpur' }] },
  { busName: 'Victor Classic', routeNumber: '8A', operator: 'Victor', stops: [{ name: 'Sadarghat' }, { name: 'Gazipur' }] },
  { busName: 'Bihanga', routeNumber: '9', operator: 'Bihanga', stops: [{ name: 'Mirpur 1' }, { name: 'Gulistan' }] },
]
vi.mock('../../controllers/useBusRoutes', () => ({ useBusRoutes: () => [{ status: 'ready', buses: BUSES }, () => {}] }))

const { BusNameSearch, busOptions, matchBuses, searchKey } = await import('./BusNameSearch')

afterEach(() => cleanup())

describe('bus name matching', () => {
  const options = busOptions(BUSES)

  it('lists every bus once, with all its route numbers', () => {
    expect(options.map((o) => o.name)).toEqual(['Bihanga', 'BRTC Double Decker', 'Raida', 'Victor Classic'])
    expect(options.find((o) => o.name === 'Victor Classic').routes).toEqual(['8', '8A'])
  })

  it('ignores case, spaces and punctuation', () => {
    expect(searchKey(' Victor-Classic ')).toBe('victorclassic')
    expect(matchBuses(options, 'victor cl').map((o) => o.name)).toEqual(['Victor Classic'])
    expect(matchBuses(options, 'RAI').map((o) => o.name)).toEqual(['Raida'])
  })

  it('puts names that start with the text first, then route numbers and operators', () => {
    expect(matchBuses(options, 'b').map((o) => o.name)).toEqual(['Bihanga', 'BRTC Double Decker'])
    expect(matchBuses(options, '36').map((o) => o.name)).toEqual(['Raida'])
    expect(matchBuses(options, 'enterprise').map((o) => o.name)).toEqual(['Raida'])
  })

  it('shows every bus when nothing is typed', () => {
    expect(matchBuses(options, '')).toHaveLength(4)
  })
})

function Harness({ onPick }) {
  const [value, setValue] = useState('')
  return <BusNameSearch value={value} onChange={setValue} onPick={onPick} />
}

describe('BusNameSearch', () => {
  it('suggests Dhaka buses while typing and searches the one clicked', async () => {
    const user = userEvent.setup()
    const onPick = vi.fn()
    render(<Harness onPick={onPick} />)
    const input = screen.getByRole('combobox')
    await user.click(input)
    expect(screen.getByText('All Dhaka buses (4)')).toBeTruthy()
    await user.type(input, 'vic')
    const list = screen.getByRole('listbox')
    expect(within(list).getAllByRole('option')).toHaveLength(1)
    expect(within(list).getByText('Sadarghat → Abdullahpur')).toBeTruthy()
    await user.click(within(list).getByText('Victor Classic'))
    expect(onPick).toHaveBeenCalledWith('Victor Classic')
    expect(input.value).toBe('Victor Classic')
    expect(screen.queryByRole('listbox')).toBeNull()
  })

  it('works with the keyboard', async () => {
    const user = userEvent.setup()
    const onPick = vi.fn()
    render(<Harness onPick={onPick} />)
    await user.type(screen.getByRole('combobox'), 'b')
    await user.keyboard('{ArrowDown}{ArrowDown}{Enter}')
    expect(onPick).toHaveBeenCalledWith('BRTC Double Decker')
  })

  it('says so when nothing matches', async () => {
    const user = userEvent.setup()
    render(<Harness onPick={vi.fn()} />)
    await user.type(screen.getByRole('combobox'), 'zzz')
    expect(screen.getByText(/No Dhaka bus matches “zzz”/)).toBeTruthy()
  })
})
