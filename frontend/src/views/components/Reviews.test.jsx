import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { BusReviews, validateReview } from './Reviews'

const bus = { _id: 'bus1', busName: 'Victor Classic' }
const me = { id: 'me', name: 'Rafi Ahmed' }
const summary = (averageRating, totalRatings, distribution = {}) => ({ busId: 'bus1', averageRating, totalRatings, totalReviews: totalRatings, distribution: { 1: 0, 2: 0, 3: 0, 4: 0, 5: 0, ...distribution } })
const review = (id, authorId, name, rating, comment, extra = {}) => ({
  _id: id, busId: 'bus1', userId: authorId, author: { id: authorId, name, verified: false }, rating, comment,
  likes: 0, dislikes: 0, myReaction: null, replies: 0, repliesList: [], createdAt: '2026-10-26T09:00:00Z', updatedAt: '2026-10-26T09:00:00Z', ...extra,
})
const mine = review('rv-mine', 'me', 'Rafi Ahmed', 4, 'Comfortable seats.')
const theirs = review('rv-theirs', 'u2', 'Nadia Islam', 2, 'Always late <b>bold</b>', {
  likes: 3, dislikes: 1, myReaction: 'like',
  repliesList: [{ _id: 'rp1', reviewId: 'rv-theirs', author: { id: 'u3', name: 'Tanvir Hasan' }, comment: 'Same for me', createdAt: '2026-10-26T10:00:00Z' }], replies: 1,
})

let routes
let calls
const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))

beforeEach(() => {
  calls = []
  routes = {
    'GET /api/reviews/bus/bus1': () => reply(200, { success: true, total: 2, totalPages: 1, summary: summary(3, 2, { 4: 1, 2: 1 }), reviews: [mine, theirs] }),
  }
  vi.stubGlobal('fetch', vi.fn((url, options = {}) => {
    const { pathname, searchParams } = new URL(url)
    const method = options.method || 'GET'
    const body = options.body && JSON.parse(options.body)
    calls.push({ method, path: pathname, params: Object.fromEntries(searchParams), body, auth: options.headers?.Authorization })
    const handler = routes[`${method} ${pathname}`]
    return handler ? handler({ params: Object.fromEntries(searchParams), body }) : reply(404, { success: false, message: 'Review not found.' })
  }))
  vi.spyOn(window, 'confirm').mockReturnValue(true)
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const renderReviews = (props = {}) => {
  const notify = vi.fn()
  const openAuth = vi.fn()
  render(<BusReviews bus={bus} user={me} token="tok" signedIn notify={notify} openAuth={openAuth} defaultOpen {...props} />)
  return { notify, openAuth }
}
const item = async (text) => (await screen.findByText(text)).closest('[data-testid="review"]')

describe('validateReview', () => {
  it('needs 1-5 whole stars and some text', () => {
    expect(validateReview(1, 'ok')).toBe('')
    expect(validateReview(5, 'ok')).toBe('')
    expect(validateReview(0, 'ok')).toBe('Choose a rating from 1 to 5 stars.')
    expect(validateReview(6, 'ok')).toBe('Choose a rating from 1 to 5 stars.')
    expect(validateReview(4.5, 'ok')).toBe('Choose a rating from 1 to 5 stars.')
    expect(validateReview(3, '   ')).toBe('Please write a few words.')
    expect(validateReview(0, 'a reply', { needsRating: false })).toBe('')
  })
})

describe('BusReviews', () => {
  it('shows the average, count and distribution from the server', async () => {
    renderReviews()
    const box = await screen.findByTestId('rating-summary')
    expect(box.textContent).toContain('3.0')
    expect(box.textContent).toContain('2 ratings')
    expect(within(box).getByRole('img', { name: '3 out of 5 stars' })).toBeTruthy()
    expect(screen.getByRole('button', { name: /Hide community reviews/ }).textContent).toContain('3.0 · 2 ratings')
  })

  it('shows a friendly empty state for a bus with no ratings', async () => {
    routes['GET /api/reviews/bus/bus1'] = () => reply(200, { success: true, total: 0, totalPages: 0, summary: summary(null, 0), reviews: [] })
    renderReviews()
    expect(await screen.findByText('No ratings yet. Be the first to review this bus.')).toBeTruthy()
    expect(screen.getByText('No reviews yet.')).toBeTruthy()
  })

  it('shows replies under their review and renders text safely', async () => {
    renderReviews()
    const card = await item('Always late <b>bold</b>')
    expect(card.querySelector('b b')).toBeNull()
    const replies = within(card).getAllByTestId('reply')
    expect(replies).toHaveLength(1)
    expect(within(replies[0]).getByText('Same for me')).toBeTruthy()
    expect(within(await item('Comfortable seats.')).queryAllByTestId('reply')).toHaveLength(0)
  })

  it('shows Edit and Delete only on your own review', async () => {
    renderReviews()
    const own = await item('Comfortable seats.')
    const other = await item('Always late <b>bold</b>')
    expect(within(own).getByRole('button', { name: /Edit/ })).toBeTruthy()
    expect(within(own).getByRole('button', { name: /Delete/ })).toBeTruthy()
    expect(within(other).queryByRole('button', { name: /Edit/ })).toBeNull()
    expect(within(other).queryByRole('button', { name: /^Delete/ })).toBeNull()
  })

  it('posts a review with the chosen stars and updates the list and average', async () => {
    const created = review('rv-new', 'me', 'Rafi Ahmed', 5, 'Great driver')
    routes['POST /api/reviews'] = () => reply(201, { success: true, review: created, summary: summary(3.7, 3, { 5: 1, 4: 1, 2: 1 }) })
    const user = userEvent.setup()
    const { notify } = renderReviews()
    const form = await screen.findByRole('form', { name: 'Write a review' })

    await user.click(within(form).getByRole('button', { name: /Post review/ }))
    expect(within(form).getByRole('alert').textContent).toBe('Choose a rating from 1 to 5 stars.')
    expect(calls.some((c) => c.method === 'POST')).toBe(false)

    await user.click(within(form).getByRole('radio', { name: '5 stars' }))
    expect(within(form).getByRole('radio', { name: '5 stars' }).getAttribute('aria-checked')).toBe('true')
    expect(within(form).getByText('5 of 5')).toBeTruthy()
    await user.type(within(form).getByRole('textbox'), '  Great driver  ')
    await user.click(within(form).getByRole('button', { name: /Post review/ }))

    expect(calls.find((c) => c.method === 'POST').body).toEqual({ busId: 'bus1', rating: 5, comment: 'Great driver' })
    expect(calls.find((c) => c.method === 'POST').auth).toBe('Bearer tok')
    expect(await screen.findByText('Great driver')).toBeTruthy()
    expect(screen.getByTestId('rating-summary').textContent).toContain('3.7')
    expect(notify).toHaveBeenCalledWith('Review posted.')
    expect(within(form).getByRole('textbox').value).toBe('')
  })

  it('keeps the form and shows the error when posting fails', async () => {
    routes['POST /api/reviews'] = () => reply(400, { success: false, message: 'A rating must be from 1 to 5 stars.' })
    const user = userEvent.setup()
    renderReviews()
    const form = await screen.findByRole('form', { name: 'Write a review' })
    await user.click(within(form).getByRole('radio', { name: '3 stars' }))
    await user.type(within(form).getByRole('textbox'), 'Fine')
    await user.click(within(form).getByRole('button', { name: /Post review/ }))
    expect((await within(form).findByRole('alert')).textContent).toBe('A rating must be from 1 to 5 stars.')
    expect(within(form).getByRole('textbox').value).toBe('Fine')
    expect(screen.queryAllByTestId('review')).toHaveLength(2)
  })

  it('edits your own review, or cancels without saving', async () => {
    routes['PUT /api/reviews/rv-mine'] = ({ body }) => reply(200, { success: true, review: { ...mine, ...body, edited: true }, summary: summary(2, 2, { 2: 2 }) })
    const user = userEvent.setup()
    renderReviews()
    let card = await item('Comfortable seats.')
    await user.click(within(card).getByRole('button', { name: /Edit/ }))
    let form = within(card).getByRole('form', { name: 'Edit your review' })
    expect(within(form).getByRole('radio', { name: '4 stars' }).getAttribute('aria-checked')).toBe('true')
    await user.clear(within(form).getByRole('textbox'))
    await user.type(within(form).getByRole('textbox'), 'Changed my mind')
    await user.click(within(form).getByRole('button', { name: 'Cancel' }))
    expect(within(card).getByText('Comfortable seats.')).toBeTruthy()
    expect(calls.some((c) => c.method === 'PUT')).toBe(false)

    await user.click(within(card).getByRole('button', { name: /Edit/ }))
    form = within(card).getByRole('form', { name: 'Edit your review' })
    await user.click(within(form).getByRole('radio', { name: '2 stars' }))
    await user.clear(within(form).getByRole('textbox'))
    await user.type(within(form).getByRole('textbox'), 'Seats got worse')
    await user.click(within(form).getByRole('button', { name: 'Save changes' }))

    expect(calls.find((c) => c.method === 'PUT').body).toEqual({ rating: 2, comment: 'Seats got worse' })
    card = await item('Seats got worse')
    expect(within(card).getByText(/edited/)).toBeTruthy()
    expect(screen.getByTestId('rating-summary').textContent).toContain('2.0')
  })

  it('deletes your review after confirming and updates the average', async () => {
    routes['DELETE /api/reviews/rv-mine'] = () => reply(200, { success: true, summary: summary(2, 1, { 2: 1 }) })
    const user = userEvent.setup()
    renderReviews()
    window.confirm.mockReturnValueOnce(false)
    await user.click(within(await item('Comfortable seats.')).getByRole('button', { name: /Delete/ }))
    expect(calls.some((c) => c.method === 'DELETE')).toBe(false)

    await user.click(within(await item('Comfortable seats.')).getByRole('button', { name: /Delete/ }))
    await waitFor(() => expect(screen.queryByText('Comfortable seats.')).toBeNull())
    expect(screen.getByTestId('rating-summary').textContent).toContain('1 ratings')
  })

  it('keeps the review when the server refuses to delete it', async () => {
    routes['DELETE /api/reviews/rv-mine'] = () => reply(403, { success: false, message: 'You can only delete your own review.' })
    const user = userEvent.setup()
    renderReviews()
    const card = await item('Comfortable seats.')
    await user.click(within(card).getByRole('button', { name: /Delete/ }))
    expect((await within(card).findByRole('alert')).textContent).toBe('You can only delete your own review.')
    expect(screen.getByText('Comfortable seats.')).toBeTruthy()
  })

  it('toggles and switches reactions on one review only', async () => {
    let state = { likes: 0, dislikes: 0, myReaction: null }
    const toggle = (kind) => () => {
      if (state.myReaction === kind) state = { ...state, [`${kind}s`]: state[`${kind}s`] - 1, myReaction: null }
      else {
        const other = kind === 'like' ? 'dislike' : 'like'
        state = { ...state, [`${kind}s`]: state[`${kind}s`] + 1, ...(state.myReaction === other ? { [`${other}s`]: state[`${other}s`] - 1 } : {}), myReaction: kind }
      }
      return reply(200, { success: true, ...state })
    }
    routes['POST /api/reviews/rv-mine/like'] = toggle('like')
    routes['POST /api/reviews/rv-mine/dislike'] = toggle('dislike')
    const user = userEvent.setup()
    renderReviews()
    const card = await item('Comfortable seats.')
    const like = () => within(card).getByRole('button', { name: /^Like/ })
    const dislike = () => within(card).getByRole('button', { name: /^Dislike/ })

    await user.click(like())
    await waitFor(() => expect(like().getAttribute('aria-pressed')).toBe('true'))
    expect(like().textContent).toContain('1')
    await user.click(like())
    await waitFor(() => expect(like().getAttribute('aria-pressed')).toBe('false'))
    expect(like().textContent).toContain('0')
    await user.click(like())
    await waitFor(() => expect(like().textContent).toContain('1'))
    await user.click(dislike())
    await waitFor(() => expect(dislike().getAttribute('aria-pressed')).toBe('true'))
    expect(like().textContent).toContain('0')
    expect(dislike().textContent).toContain('1')

    const other = await item('Always late <b>bold</b>')
    expect(within(other).getByRole('button', { name: /^Like/ }).getAttribute('aria-pressed')).toBe('true')
    expect(within(other).getByRole('button', { name: /^Like/ }).textContent).toContain('3')
  })

  it('adds a reply under the right review', async () => {
    routes['POST /api/reviews/rv-mine/reply'] = ({ body }) => reply(201, { success: true, reply: { _id: 'rp-new', reviewId: 'rv-mine', author: { id: 'me', name: 'Rafi Ahmed' }, comment: body.comment, createdAt: '2026-10-27T10:00:00Z' } })
    const user = userEvent.setup()
    renderReviews()
    const card = await item('Comfortable seats.')
    await user.click(within(card).getByRole('button', { name: /Reply/ }))
    await user.click(within(card).getByRole('button', { name: 'Post reply' }))
    expect(within(card).getByRole('alert').textContent).toBe('Please write a few words.')
    await user.type(within(card).getByRole('textbox', { name: 'Your reply' }), '  Thanks!  ')
    await user.click(within(card).getByRole('button', { name: 'Post reply' }))

    expect(calls.find((c) => c.path === '/api/reviews/rv-mine/reply').body).toEqual({ busId: 'bus1', comment: 'Thanks!' })
    expect(await within(card).findByText('Thanks!')).toBeTruthy()
    expect(within(card).getAllByTestId('reply')).toHaveLength(1)
    expect(within(await item('Always late <b>bold</b>')).getAllByTestId('reply')).toHaveLength(1)
  })

  it('lets visitors read but sends them to sign in to react or reply', async () => {
    const user = userEvent.setup()
    const { openAuth } = renderReviews({ signedIn: false, token: null, user: null })
    const card = await item('Comfortable seats.')
    expect(screen.queryByRole('form', { name: 'Write a review' })).toBeNull()
    expect(within(card).queryByRole('button', { name: /Edit/ })).toBeNull()
    await user.click(within(card).getByRole('button', { name: /^Like/ }))
    await user.click(within(card).getByRole('button', { name: /Reply/ }))
    expect(openAuth).toHaveBeenCalledTimes(2)
    expect(calls.some((c) => c.method !== 'GET')).toBe(false)
  })

  it('shows an error with a retry when reviews cannot be loaded', async () => {
    routes['GET /api/reviews/bus/bus1'] = () => reply(500, { success: false, message: 'Something went wrong on the server.' })
    const user = userEvent.setup()
    renderReviews()
    expect(await screen.findByText('Something went wrong on the server.')).toBeTruthy()
    routes['GET /api/reviews/bus/bus1'] = () => reply(200, { success: true, total: 1, totalPages: 1, summary: summary(4, 1, { 4: 1 }), reviews: [mine] })
    await user.click(screen.getByRole('button', { name: 'Try again' }))
    expect(await screen.findByText('Comfortable seats.')).toBeTruthy()
  })

  it('loads only when opened on a bus card', async () => {
    const user = userEvent.setup()
    renderReviews({ defaultOpen: false })
    expect(calls).toHaveLength(0)
    await user.click(screen.getByRole('button', { name: /Community reviews/ }))
    expect(await screen.findByText('Comfortable seats.')).toBeTruthy()
  })
})
