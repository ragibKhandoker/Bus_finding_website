import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RidePage, validateRide } from './RideShare'

const me = { id: 'me', name: 'Rafi Ahmed', gender: 'Male' }
const ride = (id, hostId, hostName, destination, extra = {}) => ({
  _id: id, userId: hostId, userName: hostName, host: { id: hostId, name: hostName, verified: false },
  source: 'Mirpur 10', destination, maxParticipants: 4, genderPreference: 'Any', status: 'active',
  createdAt: '2026-10-27T08:30:00Z', ...extra,
})
const mine = ride('r-mine', 'me', 'Rafi Ahmed', 'Dhanmondi 27')
const theirs = ride('r-theirs', 'u2', 'Nadia Islam', 'Banani', { genderPreference: 'Female' })

let routes
let calls
const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))
const list = (items) => reply(200, { success: true, total: items.length, data: items })

beforeEach(() => {
  calls = []
  window.history.replaceState({}, '', '/ride-share')
  routes = {
    'GET /api/rideshare': () => list([mine, theirs]),
    'GET /api/rideshare/user/me': () => list([mine]),
  }
  vi.stubGlobal('fetch', vi.fn((url, options = {}) => {
    const { pathname, searchParams } = new URL(url)
    const method = options.method || 'GET'
    const body = options.body && JSON.parse(options.body)
    calls.push({ method, path: pathname, params: Object.fromEntries(searchParams), body, auth: options.headers?.Authorization })
    const handler = routes[`${method} ${pathname}`]
    return handler ? handler({ params: Object.fromEntries(searchParams), body }) : reply(404, { success: false, message: 'Ride post not found.' })
  }))
  vi.spyOn(window, 'confirm').mockReturnValue(true)
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const renderPage = (props = {}) => {
  const notify = vi.fn()
  const openAuth = vi.fn()
  render(<RidePage token="tok" user={me} signedIn notify={notify} openAuth={openAuth} {...props} />)
  return { notify, openAuth }
}
const cardFor = async (destination) => (await screen.findByText(destination)).closest('[data-testid="ride-card"]')

describe('validateRide', () => {
  const ok = { source: 'Mirpur', destination: 'Banani', maxParticipants: '3', genderPreference: 'Any' }
  it('accepts a complete ride', () => expect(validateRide(ok)).toEqual({}))
  it.each([
    [{ source: '   ' }, 'source'], [{ destination: '' }, 'destination'],
    [{ maxParticipants: '0' }, 'maxParticipants'], [{ maxParticipants: '-3' }, 'maxParticipants'], [{ maxParticipants: '2.5' }, 'maxParticipants'],
    [{ maxParticipants: '11' }, 'maxParticipants'], [{ maxParticipants: 'abc' }, 'maxParticipants'], [{ genderPreference: 'Other' }, 'genderPreference'],
  ])('rejects %o', (change, field) => expect(Object.keys(validateRide({ ...ok, ...change }))).toEqual([field]))
})

describe('RidePage', () => {
  it('shows ride cards with route, host, size, preference and status', async () => {
    renderPage()
    const card = await cardFor('Banani')
    expect(within(card).getByText('Mirpur 10')).toBeTruthy()
    expect(within(card).getByText('Nadia Islam')).toBeTruthy()
    expect(within(card).getByTestId('ride-seats').textContent).toContain('1 of 4 going')
    expect(within(card).getByText('Women only')).toBeTruthy()
    expect(within(card).getByText('Open')).toBeTruthy()
    expect(within(card).getByText(/^Posted /)).toBeTruthy()
    expect(screen.getByRole('tab', { name: /Open rides/ }).textContent).toContain('2')
  })

  it('shows Delete and Mark as completed only on your own posts', async () => {
    renderPage()
    const own = await cardFor('Dhanmondi 27')
    const other = await cardFor('Banani')
    expect(within(own).getByRole('button', { name: /Delete/ })).toBeTruthy()
    expect(within(own).getByRole('button', { name: /Mark as completed/ })).toBeTruthy()
    expect(within(other).queryByRole('button', { name: /Delete/ })).toBeNull()
    expect(within(other).queryByRole('button', { name: /Mark as completed/ })).toBeNull()
    expect(within(other).getByRole('button', { name: /Join ride/ })).toBeTruthy()
  })

  it('shows a loading state, then an empty state', async () => {
    routes['GET /api/rideshare'] = () => list([])
    renderPage()
    expect(screen.getByText('Finding available rides…')).toBeTruthy()
    expect(await screen.findByText('No rides on this route yet')).toBeTruthy()
  })

  it('shows an error with a retry when rides cannot be loaded', async () => {
    routes['GET /api/rideshare'] = () => reply(500, { success: false, message: 'Something went wrong on the server.' })
    const user = userEvent.setup()
    renderPage()
    expect(await screen.findByText('Could not load rides')).toBeTruthy()
    routes['GET /api/rideshare'] = () => list([theirs])
    await user.click(screen.getByRole('button', { name: /Try again/ }))
    expect(await screen.findByText('Banani')).toBeTruthy()
  })

  it('filters by destination on the server, keeps it in the address and can clear it', async () => {
    routes['GET /api/rideshare'] = ({ params }) => list(params.destination ? [mine, theirs].filter((r) => r.destination.toLowerCase().includes(params.destination.toLowerCase())) : [mine, theirs])
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('Banani')

    await user.type(screen.getByLabelText('Search a destination'), '  dhanmondi  {Enter}')
    await waitFor(() => expect(screen.queryByText('Banani')).toBeNull())
    expect(screen.getByText('Dhanmondi 27')).toBeTruthy()
    expect(calls.filter((c) => c.path === '/api/rideshare').at(-1).params.destination).toBe('dhanmondi')
    expect(window.location.search).toBe('?destination=dhanmondi')

    await user.clear(screen.getByLabelText('Search a destination'))
    await user.type(screen.getByLabelText('Search a destination'), 'Sylhet{Enter}')
    expect(await screen.findByText('No open rides to Sylhet')).toBeTruthy()
    await user.click(screen.getAllByRole('button', { name: 'Clear search' })[0])
    expect(await screen.findByText('Banani')).toBeTruthy()
    expect(window.location.search).toBe('')
  })

  it('starts with the filter saved in the address after a refresh', async () => {
    window.history.replaceState({}, '', '/ride-share?destination=Banani')
    renderPage()
    await screen.findByText('Banani')
    expect(calls.find((c) => c.path === '/api/rideshare').params.destination).toBe('Banani')
    expect(screen.getByLabelText('Search a destination').value).toBe('Banani')
  })

  it('sends guests to sign in instead of showing the form', async () => {
    const user = userEvent.setup()
    const { openAuth } = renderPage({ signedIn: false, token: null, user: null })
    await screen.findByText('Banani')
    await user.click(screen.getByRole('button', { name: /Post a ride/ }))
    expect(openAuth).toHaveBeenCalled()
    expect(screen.queryByRole('form', { name: 'Post a ride share' })).toBeNull()
    expect(screen.queryByRole('button', { name: /Delete/ })).toBeNull()
  })

  it('validates the form before sending anything', async () => {
    const user = userEvent.setup()
    renderPage()
    await user.click(screen.getByRole('button', { name: /Post a ride/ }))
    const form = screen.getByRole('form', { name: 'Post a ride share' })
    await user.clear(within(form).getByRole('spinbutton'))
    await user.type(within(form).getByRole('spinbutton'), '0')
    await user.type(within(form).getByPlaceholderText('e.g. Mirpur 10'), '   ')
    await user.click(within(form).getByRole('button', { name: /Publish ride/ }))
    expect(within(form).getByText('Please enter where the ride starts.')).toBeTruthy()
    expect(within(form).getByText('Please enter where the ride goes.')).toBeTruthy()
    expect(within(form).getByText('Choose a whole number from 2 to 10, counting you.')).toBeTruthy()
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('creates a ride once, closes the form and refreshes the list', async () => {
    let finish
    const created = ride('r-new', 'me', 'Rafi Ahmed', 'Motijheel', { maxParticipants: 3, genderPreference: 'Male' })
    routes['POST /api/rideshare'] = () => new Promise((resolve) => { finish = () => { routes['GET /api/rideshare'] = () => list([created, mine, theirs]); resolve(new Response(JSON.stringify({ success: true, data: created }), { status: 201 })) } })
    const user = userEvent.setup()
    const { notify } = renderPage()
    await screen.findByText('Banani')
    await user.click(screen.getByRole('button', { name: /Post a ride/ }))
    const form = screen.getByRole('form', { name: 'Post a ride share' })
    await user.type(within(form).getByPlaceholderText('e.g. Mirpur 10'), ' Farmgate ')
    await user.type(within(form).getByPlaceholderText('e.g. Dhanmondi'), 'Motijheel')
    await user.clear(within(form).getByRole('spinbutton'))
    await user.type(within(form).getByRole('spinbutton'), '3')
    await user.selectOptions(within(form).getByRole('combobox'), 'Male')
    const publish = within(form).getByRole('button', { name: /Publish ride/ })
    await user.click(publish)
    await user.click(within(form).getByRole('button', { name: /Publishing/ }))

    const posts = calls.filter((c) => c.method === 'POST' && c.path === '/api/rideshare')
    expect(posts).toHaveLength(1)
    expect(posts[0].body).toEqual({ source: 'Farmgate', destination: 'Motijheel', maxParticipants: 3, genderPreference: 'Male' })
    expect(posts[0].auth).toBe('Bearer tok')
    finish()
    expect(await screen.findByText('Motijheel')).toBeTruthy()
    expect(screen.queryByRole('form', { name: 'Post a ride share' })).toBeNull()
    expect(notify).toHaveBeenCalledWith('Your ride share is live.')
  })

  it('shows the server error when creating fails', async () => {
    routes['POST /api/rideshare'] = () => reply(400, { success: false, message: 'Gender preference must be Any, Male or Female.' })
    const user = userEvent.setup()
    renderPage()
    await user.click(screen.getByRole('button', { name: /Post a ride/ }))
    const form = screen.getByRole('form', { name: 'Post a ride share' })
    await user.type(within(form).getByPlaceholderText('e.g. Mirpur 10'), 'A')
    await user.type(within(form).getByPlaceholderText('e.g. Dhanmondi'), 'B')
    await user.click(within(form).getByRole('button', { name: /Publish ride/ }))
    expect((await within(form).findByRole('alert')).textContent).toBe('Gender preference must be Any, Male or Female.')
  })

  it('completes your ride: it leaves Open rides and shows as Completed in My rides', async () => {
    routes['POST /api/rideshare/complete'] = ({ body }) => {
      const done = { ...mine, status: 'completed', completedAt: '2026-10-27T09:00:00Z' }
      routes['GET /api/rideshare/user/me'] = () => list([done])
      return reply(200, { success: true, data: done, postId: body.postId })
    }
    const user = userEvent.setup()
    const { notify } = renderPage()
    await user.click(within(await cardFor('Dhanmondi 27')).getByRole('button', { name: /Mark as completed/ }))

    expect(window.confirm).toHaveBeenCalled()
    expect(calls.find((c) => c.path === '/api/rideshare/complete').body).toEqual({ postId: 'r-mine' })
    await waitFor(() => expect(screen.queryByText('Dhanmondi 27')).toBeNull())
    expect(notify).toHaveBeenCalledWith('Ride marked as completed.')

    await user.click(screen.getByRole('tab', { name: 'My rides' }))
    const card = await cardFor('Dhanmondi 27')
    expect(within(card).getByText('Completed')).toBeTruthy()
    expect(within(card).queryByRole('button', { name: /Mark as completed/ })).toBeNull()
  })

  it('deletes only after confirmation and only once the server agrees', async () => {
    routes['DELETE /api/rideshare/r-mine'] = () => reply(403, { success: false, message: 'Only the host can delete this ride.' })
    const user = userEvent.setup()
    const { notify } = renderPage()
    window.confirm.mockReturnValueOnce(false)
    await user.click(within(await cardFor('Dhanmondi 27')).getByRole('button', { name: /Delete/ }))
    expect(calls.some((c) => c.method === 'DELETE')).toBe(false)

    await user.click(within(await cardFor('Dhanmondi 27')).getByRole('button', { name: /Delete/ }))
    await waitFor(() => expect(notify).toHaveBeenCalledWith('Only the host can delete this ride.'))
    expect(screen.getByText('Dhanmondi 27')).toBeTruthy()

    routes['DELETE /api/rideshare/r-mine'] = () => reply(200, { success: true, message: 'Ride post deleted.' })
    await user.click(within(await cardFor('Dhanmondi 27')).getByRole('button', { name: /Delete/ }))
    await waitFor(() => expect(screen.queryByText('Dhanmondi 27')).toBeNull())
    expect(notify).toHaveBeenCalledWith('Ride post removed.')
  })

  it('asks guests to sign in on My rides', async () => {
    const user = userEvent.setup()
    renderPage({ signedIn: false, token: null, user: null })
    await user.click(screen.getByRole('tab', { name: 'My rides' }))
    expect(screen.getByText('Sign in to see your rides')).toBeTruthy()
    expect(calls.some((c) => c.path.startsWith('/api/rideshare/user/'))).toBe(false)
  })
})

describe('Ride join requests', () => {
  const another = ride('r-other', 'u3', 'Tanvir Hasan', 'Gulshan 1')
  const request = (id, name, status = 'pending', extra = {}) => ({ _id: id, ridePostId: 'r-mine', requesterId: `u-${id}`, requester: { id: `u-${id}`, name, gender: 'Female', verified: false }, status, createdAt: '2026-10-27T08:45:00Z', ...extra })
  const summary = (participantCount, seatsLeft) => ({ _id: 'r-mine', maxParticipants: 4, participantCount, seatsLeft, status: 'active' })

  beforeEach(() => {
    routes['GET /api/rideshare'] = () => list([mine, theirs, another])
    routes['GET /api/riderequests/user/me'] = () => reply(200, { success: true, data: [] })
  })

  it('sends a join request for that ride only and then shows Requested', async () => {
    let finish
    routes['POST /api/riderequests'] = () => new Promise((resolve) => { finish = () => resolve(new Response(JSON.stringify({ success: true, data: { _id: 'q1', ridePostId: 'r-theirs', status: 'pending' } }), { status: 201 })) })
    const user = userEvent.setup()
    const { notify } = renderPage()
    const card = await cardFor('Banani')
    await user.click(within(card).getByRole('button', { name: /Join ride/ }))
    expect(within(card).getByRole('button', { name: 'Sending…' }).disabled).toBe(true)
    finish()

    const requested = await within(card).findByRole('button', { name: /Requested/ })
    expect(requested.disabled).toBe(true)
    expect(calls.filter((c) => c.method === 'POST' && c.path === '/api/riderequests')).toEqual([expect.objectContaining({ body: { ridePostId: 'r-theirs' }, auth: 'Bearer tok' })])
    expect(notify).toHaveBeenCalledWith('Your request has been sent.')
    expect(within(await cardFor('Gulshan 1')).getByRole('button', { name: /Join ride/ })).toBeTruthy()
    expect(within(await cardFor('Dhanmondi 27')).queryByRole('button', { name: /Join ride|Requested/ })).toBeNull()
  })

  it('keeps Join ride when the request fails and shows why', async () => {
    routes['POST /api/riderequests'] = () => reply(403, { success: false, message: 'This ride is for female riders only.' })
    const user = userEvent.setup()
    const { notify } = renderPage()
    const card = await cardFor('Banani')
    await user.click(within(card).getByRole('button', { name: /Join ride/ }))
    await waitFor(() => expect(notify).toHaveBeenCalledWith('This ride is for female riders only.'))
    expect(within(card).getByRole('button', { name: /Join ride/ }).disabled).toBe(false)
    expect(within(card).queryByRole('button', { name: /Requested/ })).toBeNull()
  })

  it('shows Requested, Joined and Ask again from the server after a refresh', async () => {
    routes['GET /api/riderequests/user/me'] = () => reply(200, { success: true, data: [
      { _id: 'a', ridePostId: 'r-theirs', status: 'pending' },
      { _id: 'b', ridePostId: 'r-other', status: 'rejected' },
    ] })
    renderPage()
    expect(await within(await cardFor('Banani')).findByRole('button', { name: /Requested/ })).toBeTruthy()
    const other = await cardFor('Gulshan 1')
    expect(await within(other).findByRole('button', { name: /Ask again/ })).toBeTruthy()
    expect(within(other).getByText('The host declined your last request. You can ask again.')).toBeTruthy()

    cleanup()
    routes['GET /api/riderequests/user/me'] = () => reply(200, { success: true, data: [{ _id: 'c', ridePostId: 'r-theirs', status: 'accepted' }] })
    renderPage()
    expect(await within(await cardFor('Banani')).findByText('Joined')).toBeTruthy()
  })

  it('sends guests to sign in instead of joining', async () => {
    const user = userEvent.setup()
    const { openAuth } = renderPage({ signedIn: false, token: null, user: null })
    await user.click(within(await cardFor('Banani')).getByRole('button', { name: /Join ride/ }))
    expect(openAuth).toHaveBeenCalled()
    expect(calls.some((c) => c.path === '/api/riderequests')).toBe(false)
  })

  it('lets the host accept and decline, with the new head count', async () => {
    routes['GET /api/riderequests/ride/r-mine'] = () => reply(200, { success: true, ride: summary(1, 3), data: [request('q1', 'Nadia Islam'), request('q2', 'Sumaiya Akter'), request('q3', 'Old Rider', 'rejected')] })
    routes['PUT /api/riderequests/accept/q1'] = () => reply(200, { success: true, data: request('q1', 'Nadia Islam', 'accepted'), ride: summary(2, 2) })
    routes['PUT /api/riderequests/reject/q2'] = () => reply(200, { success: true, data: request('q2', 'Sumaiya Akter', 'rejected'), ride: summary(2, 2) })
    const user = userEvent.setup()
    const { notify } = renderPage()
    const card = await cardFor('Dhanmondi 27')
    await user.click(within(card).getByRole('button', { name: 'View ride requests' }))

    expect(await within(card).findByText('1 of 4 going · 3 places left')).toBeTruthy()
    const rows = within(card).getAllByTestId('ride-request')
    expect(rows).toHaveLength(3)
    expect(within(rows[2]).getByText('Declined')).toBeTruthy()

    await user.click(within(card).getByRole('button', { name: 'Accept Nadia Islam' }))
    expect(await within(card).findByText('Accepted')).toBeTruthy()
    expect(notify).toHaveBeenCalledWith('Ride request accepted.')
    expect(within(card).getByTestId('ride-seats').textContent).toContain('2 of 4 going')
    expect(within(card).getByText('2 of 4 going · 2 places left')).toBeTruthy()

    await user.click(within(card).getByRole('button', { name: 'Decline Sumaiya Akter' }))
    await waitFor(() => expect(within(card).getAllByText('Declined')).toHaveLength(2))
    expect(notify).toHaveBeenCalledWith('Ride request declined.')
    expect(within(card).getByTestId('ride-seats').textContent).toContain('2 of 4 going')
    expect(calls.filter((c) => c.method === 'PUT')).toHaveLength(2)
  })

  it('tells the host when the ride is full and disables Accept', async () => {
    routes['GET /api/riderequests/ride/r-mine'] = () => reply(200, { success: true, ride: summary(4, 0), data: [request('q1', 'Nadia Islam')] })
    const user = userEvent.setup()
    renderPage()
    const card = await cardFor('Dhanmondi 27')
    await user.click(within(card).getByRole('button', { name: 'View ride requests' }))
    expect(await within(card).findByText('This ride is full. Pending requests cannot be accepted.')).toBeTruthy()
    expect(within(card).getByRole('button', { name: 'Accept Nadia Islam' }).disabled).toBe(true)
    expect(within(card).getByRole('button', { name: 'Decline Nadia Islam' }).disabled).toBe(false)
  })

  it('shows the server refusal and blocks a double click while saving', async () => {
    let finish
    let pendingList = [request('q1', 'Nadia Islam')]
    routes['GET /api/riderequests/ride/r-mine'] = () => reply(200, { success: true, ride: summary(1, 3), data: pendingList })
    routes['PUT /api/riderequests/accept/q1'] = () => new Promise((resolve) => { finish = () => resolve(new Response(JSON.stringify({ success: false, message: 'No places are left on this ride, so the request cannot be accepted.' }), { status: 409 })) })
    const user = userEvent.setup()
    renderPage()
    const card = await cardFor('Dhanmondi 27')
    await user.click(within(card).getByRole('button', { name: 'View ride requests' }))
    const accept = await within(card).findByRole('button', { name: 'Accept Nadia Islam' })
    await user.click(accept)
    expect(accept.disabled).toBe(true)
    await user.click(accept)
    expect(calls.filter((c) => c.method === 'PUT')).toHaveLength(1)
    pendingList = [request('q1', 'Nadia Islam')]
    finish()
    expect((await within(card).findByRole('alert')).textContent).toBe('No places are left on this ride, so the request cannot be accepted.')
    expect(within(card).queryByText('Accepted')).toBeNull()
  })

  it('shows an empty list when nobody has asked yet', async () => {
    routes['GET /api/riderequests/ride/r-mine'] = () => reply(200, { success: true, ride: summary(1, 3), data: [] })
    const user = userEvent.setup()
    renderPage()
    const card = await cardFor('Dhanmondi 27')
    await user.click(within(card).getByRole('button', { name: 'View ride requests' }))
    expect(await within(card).findByText('No requests yet.')).toBeTruthy()
  })
})
