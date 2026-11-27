import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

vi.mock('../../controllers/useBusRoutes', () => ({
  useBusRoutes: () => [{ status: 'ready', buses: [
    { _id: 'b1', busName: 'Victor Classic', routeNumber: '8', isDhakaCity: true, stops: [{ name: 'Sadarghat' }, { name: 'Farmgate' }, { name: 'Abdullahpur' }] },
    { _id: 'b2', busName: 'Victor Classic', routeNumber: '8A', isDhakaCity: true, stops: [{ name: 'Sadarghat' }, { name: 'Gazipur' }] },
    { _id: 'b3', busName: 'Raida', routeNumber: '36', isDhakaCity: true, stops: [{ name: 'Mirpur 10' }, { name: 'Shahbag' }] },
    { _id: 'b4', busName: 'Intercity Express', routeNumber: 'X1', isDhakaCity: false, stops: [{ name: 'Dhaka' }, { name: 'Cumilla' }] },
  ] }, () => {}],
}))

const { TravelPreferencePage } = await import('./TravelPreferences')
const { emptyPreference, preferenceProblems, toPreferenceBody, daysLabel, formatTime } = await import('../../models/travelPreferences')

const pref = (x = {}) => ({
  id: 'p1', preferenceName: 'University Journey', startLocation: 'Mirpur 10', destination: 'Shahbag', busId: 'b3', frequentlyUsedBus: 'Raida',
  busRouteNumber: '36', preferredTravelTime: '08:30', weeklyFrequency: 5, travelDays: ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday'],
  busType: 'Non-AC', travelPurpose: 'University', createdAt: '2026-11-20T05:00:00Z', updatedAt: '2026-11-20T05:00:00Z', ...x,
})

let routes
let calls
const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))

beforeEach(() => {
  calls = []
  routes = {
    'GET /api/commuter-profile/my': () => reply(200, { success: true, count: 2, data: [pref(), pref({ id: 'p2', preferenceName: 'Office Journey', destination: 'Motijheel', travelPurpose: 'Office', busType: 'AC' })] }),
  }
  vi.stubGlobal('fetch', vi.fn((url, options = {}) => {
    const u = new URL(url)
    const method = options.method || 'GET'
    const body = options.body && JSON.parse(options.body)
    calls.push({ method, path: u.pathname, body, auth: options.headers?.Authorization })
    const handler = routes[`${method} ${u.pathname}`]
    return handler ? handler({ body }) : reply(404, { message: 'Not found' })
  }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

const renderPage = (props = {}) => {
  const notify = vi.fn()
  render(<TravelPreferencePage token="tok" signedIn openAuth={vi.fn()} notify={notify} {...props} />)
  return { notify }
}

describe('travel preference form rules', () => {
  it('lists every missing field', () => {
    const empty = { ...emptyPreference(), travelDays: [], weeklyFrequency: '9', preferredTravelTime: '', busType: 'Double' }
    expect(Object.keys(preferenceProblems(empty)).sort())
      .toEqual(['busName', 'busType', 'destination', 'preferenceName', 'preferredTravelTime', 'startLocation', 'travelDays', 'travelPurpose', 'weeklyFrequency'])
  })
  it('accepts a complete preference and sends days in week order', () => {
    const form = { ...emptyPreference(), preferenceName: ' Gym  trip ', startLocation: 'Motijheel', destination: 'Gulshan 1', busName: 'Raida', busId: 'b3',
      travelPurpose: 'Personal', travelDays: ['Friday', 'Saturday'], weeklyFrequency: '2' }
    expect(preferenceProblems(form)).toEqual({})
    expect(toPreferenceBody(form)).toMatchObject({ preferenceName: 'Gym trip', busId: 'b3', weeklyFrequency: 2, travelDays: ['Saturday', 'Friday'] })
  })
  it('refuses the same start and destination', () => {
    expect(preferenceProblems({ ...emptyPreference(), startLocation: 'Farmgate', destination: 'farmgate' }).destination).toBeTruthy()
  })
  it('formats times and days for the cards', () => {
    expect(formatTime('08:30')).toBe('8:30 AM')
    expect(formatTime('18:05')).toBe('6:05 PM')
    expect(daysLabel(['Thursday', 'Sunday', 'Monday', 'Tuesday', 'Wednesday'])).toBe('Sun – Thu')
    expect(daysLabel(['Friday', 'Saturday'])).toBe('Sat, Fri')
  })
})

describe('travel preferences page', () => {
  it('asks guests to sign in and calls nothing', () => {
    render(<TravelPreferencePage token="" signedIn={false} openAuth={vi.fn()} />)
    expect(screen.getByText('Sign in to continue')).toBeTruthy()
    expect(calls).toHaveLength(0)
  })

  it('shows every saved preference as its own card', async () => {
    renderPage()
    const cards = await screen.findAllByTestId('tp-card')
    expect(cards).toHaveLength(2)
    expect(within(cards[0]).getByText('University Journey')).toBeTruthy()
    expect(within(cards[0]).getByText('Mirpur 10')).toBeTruthy()
    expect(within(cards[0]).getByText('8:30 AM')).toBeTruthy()
    expect(within(cards[1]).getByText('Office Journey')).toBeTruthy()
    expect(calls[0]).toMatchObject({ method: 'GET', path: '/api/commuter-profile/my', auth: 'Bearer tok' })
  })

  it('shows the empty state with a button to add the first preference', async () => {
    routes['GET /api/commuter-profile/my'] = () => reply(200, { success: true, count: 0, data: [] })
    renderPage()
    expect(await screen.findByText('No travel preferences yet')).toBeTruthy()
    await userEvent.click(screen.getByRole('button', { name: /Add your first preference/ }))
    expect(screen.getByRole('dialog', { name: 'Add travel preference' })).toBeTruthy()
  })

  it('adds a new preference next to the existing ones', async () => {
    routes['POST /api/commuter-profile'] = ({ body }) => reply(201, { success: true, message: 'Travel preference saved.', data: pref({ ...body, id: 'p3', frequentlyUsedBus: 'Raida' }) })
    const { notify } = renderPage()
    await screen.findAllByTestId('tp-card')
    await userEvent.click(screen.getByRole('button', { name: /Add travel preference/ }))
    const dialog = screen.getByRole('dialog', { name: 'Add travel preference' })
    // Buses outside Dhaka City are not offered.
    await userEvent.click(within(dialog).getByLabelText('Frequently used Dhaka bus'))
    expect(within(dialog).queryByText('Intercity Express')).toBeNull()
    await userEvent.click(within(dialog).getByText('Raida'))

    await userEvent.type(within(dialog).getByLabelText('Preference name'), 'Coaching Journey')
    await userEvent.type(within(dialog).getByLabelText('Start location'), 'Mirpur 10')
    await userEvent.type(within(dialog).getByLabelText('Destination'), 'Shahbag')
    await userEvent.click(within(dialog).getByText('Coaching'))
    await userEvent.click(within(dialog).getByLabelText('Friday'))
    await userEvent.click(within(dialog).getByRole('button', { name: /Save preference/ }))

    await waitFor(() => expect(screen.getAllByTestId('tp-card')).toHaveLength(3))
    const post = calls.find((c) => c.method === 'POST')
    expect(post.body).toMatchObject({ preferenceName: 'Coaching Journey', busId: 'b3', travelPurpose: 'Coaching', weeklyFrequency: 6,
      travelDays: ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'] })
    expect(post.body.userId).toBeUndefined()
    expect(notify).toHaveBeenCalledWith('Travel preference saved.')
  })

  it('does not send a form with missing fields', async () => {
    renderPage()
    await screen.findAllByTestId('tp-card')
    await userEvent.click(screen.getByRole('button', { name: /Add travel preference/ }))
    await userEvent.click(screen.getByRole('button', { name: /Save preference/ }))
    expect(screen.getByText('Please fix the highlighted fields.')).toBeTruthy()
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('edits only the chosen preference', async () => {
    routes['PUT /api/commuter-profile/p2'] = ({ body }) => reply(200, { success: true, message: 'Travel preference updated.', data: pref({ ...body, id: 'p2', frequentlyUsedBus: 'Raida' }) })
    renderPage()
    await screen.findAllByTestId('tp-card')
    await userEvent.click(screen.getByRole('button', { name: 'Edit Office Journey' }))
    const dialog = screen.getByRole('dialog', { name: 'Edit travel preference' })
    const name = within(dialog).getByLabelText('Preference name')
    expect(name.value).toBe('Office Journey')
    await userEvent.clear(name)
    await userEvent.type(name, 'Office (late shift)')
    await userEvent.click(within(dialog).getByRole('button', { name: /Save changes/ }))

    await waitFor(() => expect(screen.getByText('Office (late shift)')).toBeTruthy())
    expect(screen.getByText('University Journey')).toBeTruthy()
    expect(calls.filter((c) => c.method === 'PUT').map((c) => c.path)).toEqual(['/api/commuter-profile/p2'])
  })

  it('deletes one preference after confirming and keeps the others', async () => {
    routes['DELETE /api/commuter-profile/p1'] = () => reply(200, { success: true, message: 'Travel preference deleted.' })
    renderPage()
    await screen.findAllByTestId('tp-card')
    await userEvent.click(screen.getByRole('button', { name: 'Delete University Journey' }))
    expect(calls.some((c) => c.method === 'DELETE')).toBe(false) // nothing is deleted before confirming
    await userEvent.click(screen.getByRole('button', { name: /Yes, delete/ }))
    await waitFor(() => expect(screen.getAllByTestId('tp-card')).toHaveLength(1))
    expect(screen.getByText('Office Journey')).toBeTruthy()
    expect(screen.queryByText('University Journey')).toBeNull()
  })

  it('opens the full details of one preference, fresh from the server, with its route stops', async () => {
    routes['GET /api/commuter-profile/p1'] = () => reply(200, { success: true, data: pref({ preferredTravelTime: '07:45', updatedAt: '2026-11-25T05:00:00Z' }) })
    renderPage()
    await screen.findAllByTestId('tp-card')
    await userEvent.click(screen.getByRole('button', { name: 'View details of University Journey' }))
    const dialog = screen.getByRole('dialog', { name: 'University Journey' })
    expect(await within(dialog).findByText('7:45 AM')).toBeTruthy() // the server's latest copy
    expect(calls.some((c) => c.method === 'GET' && c.path === '/api/commuter-profile/p1')).toBe(true)
    expect(within(dialog).getByText('Raida · Route 36')).toBeTruthy()
    expect(within(dialog).getByText('5 days a week')).toBeTruthy()
    expect(within(dialog).getByText('Your start')).toBeTruthy() // Mirpur 10 is a stop on route 36
    expect(within(dialog).getByText('Your destination')).toBeTruthy()
    expect(within(dialog).getByText('Friday: No')).toBeTruthy()
    expect(within(dialog).getByText('Monday: Yes')).toBeTruthy()

    // Edit from the details window opens the edit form for the same preference.
    await userEvent.click(within(dialog).getByRole('button', { name: /Edit/ }))
    expect(screen.queryByTestId('tp-details')).toBeNull()
    expect(screen.getByRole('dialog', { name: 'Edit travel preference' })).toBeTruthy()
  })

  it('drops a preference from the list when its details are gone from the server', async () => {
    routes['GET /api/commuter-profile/p2'] = () => reply(404, { message: 'Travel preference not found.' })
    const { notify } = renderPage()
    await screen.findAllByTestId('tp-card')
    await userEvent.click(screen.getByRole('button', { name: 'View details of Office Journey' }))
    await waitFor(() => expect(screen.getAllByTestId('tp-card')).toHaveLength(1))
    expect(screen.queryByTestId('tp-details')).toBeNull()
    expect(notify).toHaveBeenCalledWith('This travel preference no longer exists.', 'error')
  })

  it('shows the server error when loading fails', async () => {
    routes['GET /api/commuter-profile/my'] = () => reply(500, { message: 'Server is down.' })
    renderPage()
    expect(await screen.findByText(/Server is down\./)).toBeTruthy()
  })
})
