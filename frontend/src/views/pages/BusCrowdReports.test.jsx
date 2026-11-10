import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

vi.mock('../../controllers/useBusRoutes', () => ({
  useBusRoutes: () => [{ status: 'ready', buses: [
    { _id: 'b1', busName: 'Victor Classic', routeNumber: '8', runsBothWays: true, stops: [{ id: 's1', name: 'Sadarghat', order: 0 }, { id: 's2', name: 'Farmgate', order: 1 }, { id: 's3', name: 'Abdullahpur', order: 2 }] },
    { _id: 'b2', busName: 'Victor Classic', routeNumber: '8A', runsBothWays: false, stops: [{ id: 's4', name: 'Sadarghat', order: 0 }, { id: 's5', name: 'Gazipur', order: 1 }] },
    { _id: 'b3', busName: 'Raida', routeNumber: '36', runsBothWays: true, stops: [{ id: 's6', name: 'Uttara', order: 0 }, { id: 's7', name: 'Motijheel', order: 1 }] },
  ] }, () => {}],
}))

const { BusCrowdReportPage, MyCrowdReportsPage, CROWD_NOTICE } = await import('./BusCrowdReports')
const { agoText, crowdProblems, emptyCrowdForm, freshnessOf } = await import('../../models/crowdReports')

const minutesAgo = (m) => new Date(Date.now() - m * 60_000).toISOString()
const report = (x = {}) => ({
  id: 'r1', busId: 'b1', busName: 'Victor Classic', routeNumber: '8', routeName: 'Route 8: Sadarghat → Abdullahpur', busStopId: 's2', stopName: 'Farmgate',
  travelDirection: 'FORWARD', towardsStop: 'Abdullahpur', crowdLevel: 'HIGH', seatAvailability: 'NO_SEATS', availableSeatCount: null, standingPassengerCount: 12,
  additionalComments: null, observedAt: minutesAgo(5), createdAt: minutesAgo(4), updatedAt: minutesAgo(4), isMine: false, canEdit: false, editableUntil: null, ...x,
})

let routes
let calls
const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))

beforeEach(() => {
  localStorage.clear()
  calls = []
  routes = {
    'GET /api/bus-crowd-reports/live': () => reply(200, { success: true, data: [
      { busId: 'b1', busName: 'Victor Classic', routeNumber: '8', routeName: 'Route 8', reportCount: 2, latest: report() },
      { busId: 'b3', busName: 'Raida', routeNumber: '36', routeName: 'Route 36', reportCount: 1, latest: report({ id: 'r2', busId: 'b3', busName: 'Raida', crowdLevel: 'LOW', seatAvailability: 'AVAILABLE', availableSeatCount: 9 }) },
    ] }),
    'GET /api/bus-crowd-reports': () => reply(200, { success: true, page: 1, total: 2, totalPages: 1, data: [
      report(), report({ id: 'r3', busName: 'Raida', crowdLevel: 'LOW', seatAvailability: 'AVAILABLE', observedAt: minutesAgo(95) }),
    ] }),
  }
  vi.stubGlobal('fetch', vi.fn((url, options = {}) => {
    const u = new URL(url)
    const method = options.method || 'GET'
    const body = options.body && JSON.parse(options.body)
    calls.push({ method, path: u.pathname, query: Object.fromEntries(u.searchParams), body, auth: options.headers?.Authorization })
    const handler = routes[`${method} ${u.pathname}`]
    return handler ? handler({ body }) : reply(404, { message: 'Not found' })
  }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

const renderPage = (props = {}) => {
  const notify = vi.fn()
  const openAuth = vi.fn()
  render(<BusCrowdReportPage token="tok" signedIn openAuth={openAuth} notify={notify} navigate={vi.fn()} {...props} />)
  return { notify, openAuth }
}

// Fills the whole form for Victor Classic route 8 at Farmgate.
async function fillForm(user, dialog) {
  await user.type(within(dialog).getByLabelText('Bus name'), 'victor')
  await user.click(within(dialog).getByText('Victor Classic'))
  await user.selectOptions(within(dialog).getByLabelText('Bus route'), 'b1')
  await user.selectOptions(within(dialog).getByLabelText(/Current bus stop/), 's2')
  await user.click(within(dialog).getByRole('radio', { name: /Towards Abdullahpur/ }))
  await user.click(within(dialog).getByRole('radio', { name: /^Medium/ }))
  await user.click(within(dialog).getByRole('radio', { name: /^Limited/ }))
  await user.type(within(dialog).getByLabelText(/^Available seats/), '3')
}

describe('crowd report rules', () => {
  it('lists every missing field', () => {
    expect(Object.keys(crowdProblems({ ...emptyCrowdForm(), observationDate: '', observationTime: '' })).sort())
      .toEqual(['busId', 'busName', 'busStopId', 'crowdLevel', 'observationDate', 'observationTime', 'seatAvailability', 'travelDirection'])
  })

  it('refuses negative or inconsistent seat counts, future times and old sightings', () => {
    const now = Date.parse('2026-11-10T06:00:00Z') // 12:00 in Dhaka
    const base = { ...emptyCrowdForm(now), busName: 'Raida', busId: 'b3', busStopId: 's6', travelDirection: 'FORWARD', crowdLevel: 'LOW', seatAvailability: 'AVAILABLE' }
    expect(crowdProblems(base, now)).toEqual({})
    expect(crowdProblems({ ...base, availableSeatCount: '-2' }, now)).toHaveProperty('availableSeatCount')
    expect(crowdProblems({ ...base, availableSeatCount: '2.5' }, now)).toHaveProperty('availableSeatCount')
    expect(crowdProblems({ ...base, seatAvailability: 'NO_SEATS', availableSeatCount: '4' }, now)).toHaveProperty('availableSeatCount')
    expect(crowdProblems({ ...base, standingPassengerCount: '-1' }, now)).toHaveProperty('standingPassengerCount')
    expect(crowdProblems({ ...base, observationTime: '13:30' }, now)).toHaveProperty('observationTime') // in the future
    expect(crowdProblems({ ...base, observationDate: '2026-11-08' }, now)).toHaveProperty('observationDate') // over 24 hours ago
  })

  it('works out freshness from the observation time', () => {
    const now = Date.parse('2026-11-10T06:00:00Z')
    const at = (m) => new Date(now - m * 60_000).toISOString()
    expect([0, 15, 16, 30, 31, 60, 61].map((m) => freshnessOf(at(m), now))).toEqual(['VERY_RECENT', 'VERY_RECENT', 'RECENT', 'RECENT', 'AGING', 'AGING', 'EXPIRED'])
    expect(agoText(at(45), now)).toBe('Last reported 45 minutes ago')
    expect(agoText(at(130), now)).toBe('Last reported 2 hours ago')
  })
})

describe('BusCrowdReportPage', () => {
  it('shows the live dashboard, the notice and current reports, with expired ones clearly marked', async () => {
    renderPage()
    expect(screen.getByRole('heading', { name: 'Bus Crowd & Seat Availability' })).toBeTruthy()
    expect(screen.getByText(CROWD_NOTICE)).toBeTruthy()
    expect(await screen.findAllByTestId('live-tile')).toHaveLength(2)
    await userEvent.setup().click(screen.getByRole('button', { name: /Low crowd/, pressed: false }))
    expect(screen.getAllByTestId('live-tile')).toHaveLength(1)

    const cards = await screen.findAllByTestId('crowd-card')
    expect(within(cards[0]).getByTestId('freshness').textContent).toContain('Very Recent')
    expect(within(cards[1]).getByTestId('freshness').textContent).toContain('Expired')
    expect(within(cards[1]).getByText(/may not be the bus’s current condition/)).toBeTruthy()
    // By default only current reports are asked for, newest first.
    expect(calls.find((c) => c.path === '/api/bus-crowd-reports').query).toMatchObject({ freshness: 'current', sort: 'recent' })
  })

  it('sends the chosen filters and sort to the server', async () => {
    const user = userEvent.setup()
    renderPage()
    await screen.findAllByTestId('crowd-card')
    await user.selectOptions(screen.getByLabelText('Crowd level'), 'LOW')
    await user.selectOptions(screen.getByLabelText('Sort by'), 'seats')
    await user.selectOptions(screen.getByLabelText('Freshness'), 'all')
    const last = calls.filter((c) => c.path === '/api/bus-crowd-reports').at(-1)
    expect(last.query).toMatchObject({ crowdLevel: 'LOW', sort: 'seats', freshness: 'all' })
    expect(screen.getByText(/Expired reports are history only/)).toBeTruthy()
  })

  it('asks guests to sign in before reporting', async () => {
    const { openAuth } = renderPage({ token: null, signedIn: false })
    await userEvent.setup().click(screen.getByRole('button', { name: /Submit Crowd Report/ }))
    expect(openAuth).toHaveBeenCalled()
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('shows errors next to the fields and sends nothing for an empty form', async () => {
    const user = userEvent.setup()
    renderPage()
    await user.click(screen.getByRole('button', { name: /Submit Crowd Report/ }))
    const dialog = screen.getByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: /Submit Report/ }))
    expect(within(dialog).getByText('Please fix the highlighted fields.')).toBeTruthy()
    expect(within(dialog).getByText('Choose how crowded the bus was.')).toBeTruthy()
    expect(within(dialog).getByText('Choose the stop where you saw the bus.')).toBeTruthy()
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('submits a report once, highlights the choices and shows a success message', async () => {
    const user = userEvent.setup()
    let release
    routes['POST /api/bus-crowd-reports'] = ({ body }) => new Promise((resolve) => {
      release = () => resolve(reply(201, { success: true, message: 'Thank you! Your crowd report is now live for other commuters.', data: report({ ...body, id: 'n1', isMine: true }) }))
    })
    const { notify } = renderPage()
    await user.click(screen.getByRole('button', { name: /Submit Crowd Report/ }))
    const dialog = screen.getByRole('dialog')
    await fillForm(user, dialog)
    expect(within(dialog).getByRole('radio', { name: /^Medium/ }).closest('label').className).toContain('selected')
    // A one-way route offers only its own direction.
    await user.selectOptions(within(dialog).getByLabelText('Bus route'), 'b2')
    expect(within(dialog).queryByRole('radio', { name: /Towards Sadarghat/ })).toBeNull()
    await user.selectOptions(within(dialog).getByLabelText('Bus route'), 'b1')
    await user.selectOptions(within(dialog).getByLabelText(/Current bus stop/), 's2')
    await user.click(within(dialog).getByRole('radio', { name: /Towards Sadarghat/ }))

    const submit = within(dialog).getByRole('button', { name: /Submit Report/ })
    await user.dblClick(submit)
    expect(within(dialog).getByRole('button', { name: /Submitting/ }).disabled).toBe(true)
    const posts = calls.filter((c) => c.method === 'POST')
    expect(posts).toHaveLength(1)
    expect(posts[0].body).toMatchObject({ busId: 'b1', busStopId: 's2', travelDirection: 'RETURN', crowdLevel: 'MEDIUM', seatAvailability: 'LIMITED', availableSeatCount: 3, standingPassengerCount: null })
    expect(posts[0].body).not.toHaveProperty('userId')
    expect(posts[0].auth).toBe('Bearer tok')
    release()
    expect(await screen.findByTestId('cr-success')).toBeTruthy()
    expect(notify).toHaveBeenCalledWith('Thank you! Your crowd report is now live for other commuters.')
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('guides the user: progress, quick time, steppers and a live preview', async () => {
    const user = userEvent.setup()
    renderPage()
    await user.click(screen.getByRole('button', { name: /Submit Crowd Report/ }))
    const dialog = screen.getByRole('dialog')
    expect(within(dialog).getByText('1 of 4 steps done')).toBeTruthy() // the time is already filled in
    await fillForm(user, dialog)
    expect(within(dialog).getByText('All set! Review and submit.')).toBeTruthy()
    expect(within(dialog).getAllByText('Done')).toHaveLength(4)
    // − and + change the count and never go below 0.
    await user.click(within(dialog).getByRole('button', { name: 'Increase Available seats' }))
    expect(within(dialog).getByLabelText(/^Available seats/).value).toBe('4')
    await user.click(within(dialog).getByRole('button', { name: 'Increase Standing passengers' }))
    await user.click(within(dialog).getByRole('button', { name: 'Decrease Standing passengers' }))
    expect(within(dialog).getByLabelText(/^Standing passengers/).value).toBe('0')
    expect(within(dialog).getByRole('button', { name: 'Decrease Standing passengers' }).disabled).toBe(true)
    // The preview shows what others will see.
    const preview = within(dialog).getByLabelText('Preview')
    expect(within(preview).getByText('At Farmgate · towards Abdullahpur')).toBeTruthy()
    expect(within(preview).getByTestId('crowd-badge').textContent).toContain('Medium crowd')
    // "15 min ago" sets the time 15 minutes back.
    await user.click(within(dialog).getByRole('button', { name: /15 min ago/ }))
    expect(within(dialog).getByRole('button', { name: /15 min ago/ }).className).toContain('selected')
  })

  it('shows the server’s duplicate message in the form', async () => {
    const user = userEvent.setup()
    routes['POST /api/bus-crowd-reports'] = () => reply(409, { message: 'You already reported Victor Classic at Farmgate around 11:58. Edit that report from My Reports instead.' })
    renderPage()
    await user.click(screen.getByRole('button', { name: /Submit Crowd Report/ }))
    const dialog = screen.getByRole('dialog')
    await fillForm(user, dialog)
    await user.click(within(dialog).getByRole('button', { name: /Submit Report/ }))
    expect(await within(dialog).findByText(/You already reported Victor Classic/)).toBeTruthy()
    expect(within(dialog).getByRole('button', { name: /Submit Report/ }).disabled).toBe(false)
  })
})

describe('MyCrowdReportsPage', () => {
  beforeEach(() => {
    routes['GET /api/bus-crowd-reports/my'] = () => reply(200, { success: true, page: 1, total: 2, totalPages: 1, data: [
      report({ isMine: true, canEdit: true, editableUntil: new Date(Date.now() + 11 * 60_000).toISOString() }),
      report({ id: 'r9', isMine: true, observedAt: minutesAgo(200), createdAt: minutesAgo(199), editableUntil: minutesAgo(184) }),
    ] })
  })

  it('asks guests to sign in', () => {
    render(<MyCrowdReportsPage token={null} signedIn={false} openAuth={vi.fn()} />)
    expect(screen.getByText('Sign in to continue')).toBeTruthy()
  })

  it('lists my reports with their status and only lets fresh ones be edited', async () => {
    render(<MyCrowdReportsPage token="tok" signedIn openAuth={vi.fn()} notify={vi.fn()} />)
    const cards = await screen.findAllByTestId('crowd-card')
    expect(cards).toHaveLength(2)
    expect(within(cards[0]).getByTestId('report-status').textContent).toMatch(/Editable · 1[012] min left/)
    expect(within(cards[0]).getByRole('button', { name: 'Edit report' })).toBeTruthy()
    expect(within(cards[1]).getByTestId('report-status').textContent).toBe('History · locked')
    expect(within(cards[1]).queryByRole('button', { name: 'Edit report' })).toBeNull()
  })

  it('edits a report with PUT and deletes one after confirming', async () => {
    const user = userEvent.setup()
    routes['PUT /api/bus-crowd-reports/r1'] = ({ body }) => reply(200, { success: true, message: 'Your report was updated.', data: report({ ...body, isMine: true }) })
    routes['DELETE /api/bus-crowd-reports/r9'] = () => reply(200, { success: true, message: 'Report deleted.' })
    const notify = vi.fn()
    render(<MyCrowdReportsPage token="tok" signedIn openAuth={vi.fn()} notify={notify} />)
    const cards = await screen.findAllByTestId('crowd-card')

    await user.click(within(cards[0]).getByRole('button', { name: 'Edit report' }))
    const dialog = screen.getByRole('dialog')
    expect(within(dialog).getByLabelText('Bus route').value).toBe('b1')
    await user.click(within(dialog).getByRole('radio', { name: /^Low/ }))
    await user.click(within(dialog).getByRole('radio', { name: /^Available/ }))
    await user.click(within(dialog).getByRole('button', { name: /Save changes/ }))
    const put = calls.find((c) => c.method === 'PUT')
    expect(put.path).toBe('/api/bus-crowd-reports/r1')
    expect(put.body).toMatchObject({ crowdLevel: 'LOW', seatAvailability: 'AVAILABLE', busStopId: 's2' })
    await vi.waitFor(() => expect(notify).toHaveBeenCalledWith('Your report was updated.'))

    await user.click(within((await screen.findAllByTestId('crowd-card'))[1]).getByRole('button', { name: 'Delete report' }))
    const details = screen.getByRole('dialog')
    await user.click(within(details).getByRole('button', { name: /^Delete$/ }))
    expect(calls.some((c) => c.method === 'DELETE')).toBe(false) // asks first
    await user.click(within(details).getByRole('button', { name: /Yes, delete/ }))
    expect(calls.find((c) => c.method === 'DELETE').path).toBe('/api/bus-crowd-reports/r9')
    await vi.waitFor(() => expect(notify).toHaveBeenCalledWith('Report deleted.'))
  })
})
