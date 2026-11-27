import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

vi.mock('../../controllers/useBusRoutes', () => ({
  useBusRoutes: () => [{ status: 'ready', buses: [
    { _id: 'b1', busName: 'Raida', routeNumber: '36', busType: 'general', isDhakaCity: true, stops: [
      { id: 's1', name: 'Mirpur 10', lat: 23.807, lng: 90.368 }, { id: 's2', name: 'Farmgate', lat: 23.758, lng: 90.389 }, { id: 's3', name: 'Motijheel', lat: 23.733, lng: 90.417 }] },
    { _id: 'b2', busName: 'Intercity Express', routeNumber: 'X1', isDhakaCity: false, stops: [{ id: 's9', name: 'Cumilla', lat: 23.46, lng: 91.18 }] },
  ] }, () => {}],
}))

const { RouteSuggestionPage } = await import('./RouteSuggestions')
const { AdminRouteSuggestions } = await import('./AdminRouteSuggestions')
const { emptySuggestion, suggestionProblems, toSuggestionBody, coordinateProblem } = await import('../../models/routeSuggestions')

const suggestion = (x = {}) => ({
  id: 'r1', code: 'RS-AAAA1111', type: 'NEW_STOP', title: 'Missing stop at Bijoy Sarani', description: 'The bus always stops here but it is not shown.',
  relatedBusId: 'b1', relatedBusName: 'Raida', relatedRouteNumber: '36', busStopName: 'Bijoy Sarani', latitude: 23.7645, longitude: 90.3895, stops: [],
  status: 'PENDING', adminReviewNote: null, canDelete: true, createdAt: '2026-11-20T05:00:00Z', updatedAt: '2026-11-20T05:00:00Z', ...x,
})

let routes
let calls
const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))

beforeEach(() => {
  calls = []
  routes = {
    'GET /api/route-suggestions/my': () => reply(200, { success: true, data: [
      suggestion(),
      suggestion({ id: 'r2', code: 'RS-BBBB2222', status: 'APPROVED', canDelete: false, title: 'Wrong stop name', adminReviewNote: 'Checked, thank you.', appliedChange: 'Renamed stop "Farmgat" on Raida (36) to "Farmgate".' }),
      suggestion({ id: 'r3', code: 'RS-CCCC3333', status: 'REJECTED', canDelete: false, title: 'New route idea', adminReviewNote: 'This route already exists.' }),
    ] }),
  }
  vi.stubGlobal('fetch', vi.fn((url, options = {}) => {
    const u = new URL(url)
    const method = options.method || 'GET'
    const body = options.body && JSON.parse(options.body)
    calls.push({ method, path: u.pathname, query: Object.fromEntries(u.searchParams), body })
    const handler = routes[`${method} ${u.pathname}`]
    return handler ? handler({ body }) : reply(404, { message: 'Not found' })
  }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

describe('suggestion form rules', () => {
  it('needs only stop names for a new route, never coordinates', () => {
    const form = { ...emptySuggestion('NEW_ROUTE'), title: 'New bus to Motijheel', description: 'A new company runs this route.', routeName: 'Test Paribahan',
      startLocation: 'Mirpur 10', endLocation: '', middleStops: [{ name: 'Farmgate' }] }
    expect(Object.keys(suggestionProblems(form))).toEqual(['endLocation'])
    const fixed = { ...form, endLocation: 'Brand New Corner' }
    expect(suggestionProblems(fixed)).toEqual({})
    const body = toSuggestionBody(fixed)
    expect(body.stops).toEqual([{ name: 'Mirpur 10' }, { name: 'Farmgate' }, { name: 'Brand New Corner' }])
    expect(body.latitude).toBeUndefined()
  })
  it('needs a bus, a name and a described place for a new stop', () => {
    expect(Object.keys(suggestionProblems({ ...emptySuggestion('NEW_STOP'), title: 'x' })).sort()).toEqual(['busName', 'busStopName', 'description', 'locationName', 'title'])
  })
  it('needs a description of the real place for a stop location correction', () => {
    const form = { ...emptySuggestion('INCORRECT_INFORMATION'), title: 'Stop is in the wrong place', description: 'The map shows it on the other side.',
      busName: 'Raida', busId: 'b1', correctionField: 'STOP_LOCATION', relatedBusStopId: 's2' }
    expect(Object.keys(suggestionProblems(form))).toEqual(['locationName'])
    expect(suggestionProblems({ ...form, locationName: 'About 100 m north, in front of the mosque' })).toEqual({})
  })
  it('needs the stop and the correct value for a stop correction', () => {
    const form = { ...emptySuggestion('INCORRECT_INFORMATION'), title: 'Wrong stop name', description: 'The sign says otherwise.', busName: 'Raida', busId: 'b1', correctionField: 'STOP_NAME' }
    expect(Object.keys(suggestionProblems(form)).sort()).toEqual(['correctedValue', 'relatedBusStopId'])
    const body = toSuggestionBody({ ...form, relatedBusStopId: 's2', correctedValue: ' Farmgate  Junction ' })
    expect(body).toMatchObject({ relatedBusId: 'b1', relatedBusStopId: 's2', correctionField: 'STOP_NAME', correctedValue: 'Farmgate Junction' })
    expect(body.routeName).toBeUndefined()
  })
  it('checks coordinates', () => {
    expect(coordinateProblem('23.75', '')).toBeTruthy()
    expect(coordinateProblem('123', '90')).toBeTruthy()
    expect(coordinateProblem('abc', '90')).toBeTruthy()
    expect(coordinateProblem('23.75', '90.39')).toBeNull()
    expect(coordinateProblem('', '', true)).toBeTruthy()
  })
})

describe('suggestion page', () => {
  const renderPage = () => { const notify = vi.fn(); render(<RouteSuggestionPage token="tok" signedIn openAuth={vi.fn()} notify={notify} />); return { notify } }

  it('asks guests to sign in', () => {
    render(<RouteSuggestionPage token="" signedIn={false} openAuth={vi.fn()} />)
    expect(screen.getByRole('heading', { name: 'Suggest a Bus Route or Stop' })).toBeTruthy()
    expect(screen.getByText('Sign in to continue')).toBeTruthy()
    expect(calls).toHaveLength(0)
  })

  it('shows only the fields of the chosen type', async () => {
    renderPage()
    expect(screen.getByLabelText('Start location')).toBeTruthy()
    expect(screen.queryByLabelText('Bus stop name')).toBeNull()
    await userEvent.type(screen.getByLabelText('End location'), 'Brand New Corner')
    expect(screen.getByText('New place. Mention where it is in the description.')).toBeTruthy()
    await userEvent.click(screen.getByText('New Bus Stop'))
    expect(screen.getByLabelText('Bus stop name')).toBeTruthy()
    expect(screen.getByLabelText('Where is the stop?')).toBeTruthy()
    expect(screen.queryByLabelText('Start location')).toBeNull()
    await userEvent.click(screen.getByText('Incorrect Information'))
    expect(screen.getByLabelText('What is wrong?')).toBeTruthy()
    expect(screen.queryByLabelText('Bus stop name')).toBeNull()
    // Users are never asked for coordinates.
    expect(screen.queryByLabelText('Latitude')).toBeNull()
    expect(screen.queryByLabelText('Longitude')).toBeNull()
    expect(screen.queryByText('Use my location')).toBeNull()
  })

  it('sends a stop correction and shows the success message', async () => {
    routes['POST /api/route-suggestions'] = ({ body }) => reply(201, { success: true, message: 'Thank you. Your suggestion RS-DDDD4444 was sent.', data: suggestion({ ...body, id: 'r4' }) })
    renderPage()
    await userEvent.click(screen.getByText('Incorrect Information'))
    await userEvent.click(screen.getByLabelText('Bus name'))
    expect(screen.queryByText('Intercity Express')).toBeNull() // only Dhaka City buses
    await userEvent.click(screen.getByText('Raida'))
    await userEvent.selectOptions(screen.getByLabelText('What is wrong?'), 'STOP_NAME')
    await userEvent.selectOptions(screen.getByLabelText('Which stop?'), 's2')
    expect(screen.getByText('Farmgate', { selector: '.sug-current' , exact: false })).toBeTruthy()
    await userEvent.type(screen.getByLabelText('Correct name'), 'Farmgate Junction')
    await userEvent.type(screen.getByLabelText('Title'), 'Wrong stop name at Farmgate')
    await userEvent.type(screen.getByLabelText('Description'), 'The sign at the stop says Farmgate Junction.')
    await userEvent.click(screen.getByRole('button', { name: /Submit suggestion/ }))

    expect(await screen.findByTestId('sug-success')).toBeTruthy()
    const post = calls.find((c) => c.method === 'POST')
    expect(post.body).toMatchObject({ type: 'INCORRECT_INFORMATION', relatedBusId: 'b1', relatedBusStopId: 's2', correctionField: 'STOP_NAME', correctedValue: 'Farmgate Junction' })
    expect(post.body.userId).toBeUndefined()
    expect(screen.getByLabelText('Title').value).toBe('') // the form was reset
  })

  it('opens the full details of a suggestion from My Suggestions', async () => {
    routes['GET /api/route-suggestions/r2'] = () => reply(200, { success: true, data: suggestion({
      id: 'r2', code: 'RS-BBBB2222', type: 'NEW_ROUTE', status: 'APPROVED', canDelete: false, title: 'New bus from Badda to Gulistan',
      description: 'A new company started this route.\nIt runs every 15 minutes.', routeName: 'Test Paribahan', routeNumber: null, busType: 'seating',
      startLocation: 'Badda Link Road', endLocation: 'Gulistan', locationName: null, adminReviewNote: 'Checked, thank you.',
      appliedChange: 'Create route S-ABCDE: Test Paribahan (seating) with 3 stops.', reviewedAt: '2026-11-22T05:00:00Z',
      stops: [{ order: 0, name: 'Badda Link Road', isExisting: true }, { order: 1, name: 'Brand New Corner', isExisting: false }, { order: 2, name: 'Gulistan', isExisting: true }],
    }) })
    renderPage()
    await userEvent.click(screen.getByRole('tab', { name: /My Suggestions/ }))
    await screen.findAllByTestId('sug-card')
    await userEvent.click(screen.getByRole('button', { name: 'View details of RS-BBBB2222' }))
    const dialog = await screen.findByRole('dialog', { name: 'New bus from Badda to Gulistan' })
    expect(await within(dialog).findByText('Test Paribahan')).toBeTruthy() // fresh from the server
    expect(within(dialog).getByText('Seating service')).toBeTruthy()
    expect(within(dialog).getByText('Brand New Corner')).toBeTruthy()
    expect(within(dialog).getByText('New place')).toBeTruthy()
    expect(within(dialog).getByText('Approved and published')).toBeTruthy()
    expect(within(dialog).getByText(/Create route S-ABCDE/)).toBeTruthy()
    expect(within(dialog).queryByRole('button', { name: /Delete suggestion/ })).toBeNull() // reviewed: cannot be deleted
    expect(within(dialog).queryByText(/Latitude|Longitude/)).toBeNull()
  })

  it('edits a pending suggestion with the form filled in, and updates its card', async () => {
    routes['GET /api/route-suggestions/my'] = () => reply(200, { success: true, data: [
      suggestion({ locationName: 'In front of the signal', imageUrl: '/api/route-suggestions/r1/image' }),
      suggestion({ id: 'r2', code: 'RS-BBBB2222', status: 'APPROVED', canDelete: false, title: 'Wrong stop name' }),
    ] })
    routes['PUT /api/route-suggestions/r1'] = ({ body }) => reply(200, { success: true, message: 'Suggestion updated. It is still waiting for review.',
      data: suggestion({ ...body, id: 'r1', imageUrl: null }) })
    const { notify } = renderPage()
    await userEvent.click(screen.getByRole('tab', { name: /My Suggestions/ }))
    const cards = await screen.findAllByTestId('sug-card')
    expect(within(cards[1]).queryByRole('button', { name: /Edit/ })).toBeNull() // reviewed: no editing
    await userEvent.click(within(cards[0]).getByRole('button', { name: 'Edit suggestion RS-AAAA1111' }))

    const dialog = screen.getByRole('dialog', { name: 'Edit suggestion' })
    expect(within(dialog).getByLabelText('Title').value).toBe('Missing stop at Bijoy Sarani')
    expect(within(dialog).getByLabelText('Bus stop name').value).toBe('Bijoy Sarani')
    expect(within(dialog).getByLabelText('Where is the stop?').value).toBe('In front of the signal')
    expect(within(dialog).getByLabelText('Bus route').value).toBe('b1')
    expect(within(dialog).getByText('Current photo')).toBeTruthy()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Remove photo' }))
    await userEvent.clear(within(dialog).getByLabelText('Title'))
    await userEvent.type(within(dialog).getByLabelText('Title'), 'Missing stop at Bijoy Sarani signal')
    await userEvent.click(within(dialog).getByRole('button', { name: /Save changes/ }))

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    const put = calls.find((c) => c.method === 'PUT')
    expect(put.path).toBe('/api/route-suggestions/r1')
    expect(put.body).toMatchObject({ type: 'NEW_STOP', title: 'Missing stop at Bijoy Sarani signal', relatedBusId: 'b1', busStopName: 'Bijoy Sarani',
      locationName: 'In front of the signal', removeImage: true })
    expect(screen.getByText('Missing stop at Bijoy Sarani signal')).toBeTruthy()
    expect(notify).toHaveBeenCalledWith('Suggestion updated. It is still waiting for review.')
  })

  it('does not send a form with missing fields', async () => {
    renderPage()
    await userEvent.click(screen.getByRole('button', { name: /Submit suggestion/ }))
    expect(screen.getByText('Please fix the highlighted fields.')).toBeTruthy()
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('lists my suggestions with their status and review notes, and deletes a pending one', async () => {
    routes['DELETE /api/route-suggestions/r1'] = () => reply(200, { success: true, message: 'Suggestion deleted.' })
    renderPage()
    await userEvent.click(screen.getByRole('tab', { name: /My Suggestions/ }))
    const cards = await screen.findAllByTestId('sug-card')
    expect(cards).toHaveLength(3)
    expect(within(cards[1]).getByText('Approved')).toBeTruthy()
    expect(within(cards[1]).getByText('Checked, thank you.')).toBeTruthy()
    expect(within(cards[2]).getByText('Rejected')).toBeTruthy()
    // Only the pending one can be deleted.
    expect(within(cards[1]).queryByRole('button', { name: /Delete/ })).toBeNull()
    await userEvent.click(within(cards[0]).getByRole('button', { name: /Delete suggestion RS-AAAA1111/ }))
    await userEvent.click(screen.getByRole('button', { name: /Yes, delete/ }))
    await waitFor(() => expect(screen.getAllByTestId('sug-card')).toHaveLength(2))
  })
})

describe('admin suggestions', () => {
  beforeEach(() => {
    routes['GET /api/route-suggestions/all'] = () => reply(200, { success: true, total: 1, totalPages: 1, counts: { pending: 1, approved: 4, rejected: 2 },
      data: [suggestion({ user: { id: 'u1', name: 'Karim', email: 'karim@test.local' } })] })
    routes['GET /api/route-suggestions/r1'] = () => reply(200, { success: true, data: suggestion({ user: { id: 'u1', name: 'Karim', email: 'karim@test.local', phone: '01711111111' } }) })
    routes['GET /api/route-suggestions/r1/preview'] = () => reply(200, { success: true, data: { canApprove: true, changes: ['Add stop "Bijoy Sarani" to Raida (36), between Mirpur 10 and Farmgate.'] } })
  })

  it('filters, shows the submitter and what approving changes, and asks before approving', async () => {
    routes['PUT /api/route-suggestions/r1/review'] = ({ body }) => reply(200, { success: true, message: 'Suggestion approved. The official bus data was updated.', data: suggestion({ status: body.status }) })
    const notify = vi.fn()
    render(<AdminRouteSuggestions token="tok" notify={notify} />)
    await screen.findByTestId('sug-admin-row')
    expect(calls[0].query).toMatchObject({ status: 'PENDING' })
    await userEvent.selectOptions(screen.getByLabelText('Suggestion type'), 'NEW_STOP')
    await waitFor(() => expect(calls.some((c) => c.query?.type === 'NEW_STOP')).toBe(true))

    await userEvent.click(screen.getByRole('button', { name: 'Review RS-AAAA1111' }))
    const dialog = await screen.findByRole('dialog')
    expect(await within(dialog).findByText(/between Mirpur 10 and Farmgate/)).toBeTruthy()
    expect(within(dialog).getByText('Karim')).toBeTruthy()
    expect(within(dialog).getByText('23.76450, 90.38950')).toBeTruthy()
    await userEvent.click(within(dialog).getByRole('button', { name: /^Approve$/ }))
    expect(calls.some((c) => c.method === 'PUT')).toBe(false) // nothing saved before confirming
    await userEvent.click(within(dialog).getByRole('button', { name: /Yes, approve/ }))
    await waitFor(() => expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({ status: 'APPROVED', adminReviewNote: null }))
    expect(notify).toHaveBeenCalledWith('Suggestion approved. The official bus data was updated.')
  })

  it('needs a reason to reject', async () => {
    routes['PUT /api/route-suggestions/r1/review'] = ({ body }) => reply(200, { success: true, message: 'Suggestion rejected. The bus data was not changed.', data: suggestion({ status: body.status }) })
    render(<AdminRouteSuggestions token="tok" />)
    await userEvent.click(await screen.findByRole('button', { name: 'Review RS-AAAA1111' }))
    const dialog = await screen.findByRole('dialog')
    await within(dialog).findByText(/between Mirpur 10 and Farmgate/)
    await userEvent.click(within(dialog).getByRole('button', { name: /^Reject$/ }))
    await userEvent.click(within(dialog).getByRole('button', { name: /Yes, reject/ }))
    expect(within(dialog).getByText('Tell the user why (at least 5 characters).')).toBeTruthy()
    await userEvent.type(within(dialog).getByLabelText('Reason for rejecting'), 'Already listed as Farmgate.')
    await userEvent.click(within(dialog).getByRole('button', { name: /Yes, reject/ }))
    await waitFor(() => expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({ status: 'REJECTED', adminReviewNote: 'Already listed as Farmgate.' }))
  })

  it('asks the admin for the coordinates of a place the user only described', async () => {
    routes['GET /api/route-suggestions/r1'] = () => reply(200, { success: true, data: suggestion({ latitude: null, longitude: null, locationName: 'In front of the signal' }) })
    routes['GET /api/route-suggestions/r1/preview'] = () => reply(200, { success: true, data: { canApprove: true,
      changes: ['Add stop "Bijoy Sarani" to Raida (36). Its place on the route is chosen from the location you enter.'],
      needsLocations: [{ order: null, name: 'Bijoy Sarani', description: 'In front of the signal' }] } })
    routes['PUT /api/route-suggestions/r1/review'] = ({ body }) => reply(200, { success: true, message: 'Suggestion approved. The official bus data was updated.', data: suggestion({ status: body.status }) })
    render(<AdminRouteSuggestions token="tok" />)
    await userEvent.click(await screen.findByRole('button', { name: 'Review RS-AAAA1111' }))
    const dialog = await screen.findByRole('dialog')
    await within(dialog).findByText(/chosen from the location you enter/)
    await userEvent.click(within(dialog).getByRole('button', { name: /^Approve$/ }))
    expect(within(dialog).getByRole('link', { name: /Search on map/ }).getAttribute('href')).toContain('In%20front%20of%20the%20signal')
    await userEvent.click(within(dialog).getByRole('button', { name: /Yes, approve/ }))
    expect(within(dialog).getByText(/Bijoy Sarani: Add the location/)).toBeTruthy() // not sent without coordinates
    expect(calls.some((c) => c.method === 'PUT')).toBe(false)
    await userEvent.type(within(dialog).getByLabelText('Latitude of Bijoy Sarani'), '23.7645')
    await userEvent.type(within(dialog).getByLabelText('Longitude of Bijoy Sarani'), '90.3895')
    await userEvent.click(within(dialog).getByRole('button', { name: /Yes, approve/ }))
    await waitFor(() => expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({ status: 'APPROVED', adminReviewNote: null, latitude: 23.7645, longitude: 90.3895 }))
  })

  it('blocks approval when the server says it cannot be applied', async () => {
    routes['GET /api/route-suggestions/r1/preview'] = () => reply(200, { success: true, data: { canApprove: false, problem: 'Bijoy Sarani is already a stop on Raida (36).', changes: [] } })
    render(<AdminRouteSuggestions token="tok" />)
    await userEvent.click(await screen.findByRole('button', { name: 'Review RS-AAAA1111' }))
    const dialog = await screen.findByRole('dialog')
    expect(await within(dialog).findByText(/already a stop on Raida/)).toBeTruthy()
    expect(within(dialog).getByRole('button', { name: /^Approve$/ }).disabled).toBe(true)
    expect(within(dialog).getByRole('button', { name: /^Reject$/ }).disabled).toBe(false)
  })
})
