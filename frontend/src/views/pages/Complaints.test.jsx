import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

vi.mock('../../controllers/useBusRoutes', () => ({
  useBusRoutes: () => [{ status: 'ready', buses: [
    { _id: 'b1', busName: 'Victor Classic', routeNumber: '8', stops: [{ name: 'Sadarghat' }, { name: 'Farmgate' }, { name: 'Abdullahpur' }] },
    { _id: 'b2', busName: 'Victor Classic', routeNumber: '8A', stops: [{ name: 'Sadarghat' }, { name: 'Gazipur' }] },
    { _id: 'b3', busName: 'Raida', routeNumber: '36', stops: [{ name: 'Uttara' }, { name: 'Motijheel' }] },
  ] }, () => {}],
}))

const { ComplaintPage } = await import('./Complaints')
const { AdminComplaints } = await import('./AdminComplaints')
const { complaintProblems, emptyComplaint } = await import('../../models/complaints')

const complaint = (x = {}) => ({
  id: 'c1', code: 'CMP-AAAA1111', busId: 'b1', busName: 'Victor Classic', routeNumber: '8', routeName: 'Route 8: Sadarghat → Abdullahpur', stopName: 'Farmgate',
  category: 'Overcharging / Extra Fare', subject: 'Conductor charged 40 taka', description: 'The conductor asked for 40 taka for a 20 taka trip.',
  incidentDate: '2026-11-09', incidentLocation: 'Farmgate', status: 'PENDING', adminNotes: null, createdAt: '2026-11-09T05:00:00Z', updatedAt: '2026-11-09T05:00:00Z',
  canDelete: true, evidenceCount: 1, evidence: [{ id: 'e1', url: '/api/complaints/c1/evidence/e1' }],
  history: [{ toStatus: 'PENDING', actorRole: 'user', actorName: 'You', note: 'Complaint submitted.', createdAt: '2026-11-09T05:00:00Z' }], nextStatuses: [], ...x,
})

let routes
let calls
const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))

beforeEach(() => {
  calls = []
  routes = {
    'GET /api/complaints/my': () => reply(200, { success: true, data: [complaint(), complaint({ id: 'c2', code: 'CMP-BBBB2222', status: 'RESOLVED', canDelete: false, subject: 'Bus skipped my stop', adminNotes: 'Necessary action has been taken.' })] }),
    'GET /api/complaints/c1': () => reply(200, { success: true, data: complaint() }),
    'GET /api/complaints/c1/evidence/e1': () => Promise.resolve(new Response(new Blob(['x'], { type: 'image/png' }), { status: 200 })),
  }
  vi.stubGlobal('fetch', vi.fn((url, options = {}) => {
    const u = new URL(url)
    const method = options.method || 'GET'
    const body = options.body && JSON.parse(options.body)
    calls.push({ method, path: u.pathname, query: Object.fromEntries(u.searchParams), body, auth: options.headers?.Authorization })
    const handler = routes[`${method} ${u.pathname}`]
    return handler ? handler({ body }) : reply(404, { message: 'Not found' })
  }))
  URL.createObjectURL = vi.fn(() => 'blob:evidence')
  URL.revokeObjectURL = vi.fn()
})
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

const renderPage = () => {
  const notify = vi.fn()
  render(<ComplaintPage token="tok" signedIn openAuth={vi.fn()} notify={notify} />)
  return { notify }
}

describe('complaint form rules', () => {
  it('lists every missing field', () => {
    expect(Object.keys(complaintProblems({ ...emptyComplaint(), incidentDate: '2099-01-01' }, '2026-11-10')).sort())
      .toEqual(['busId', 'busName', 'category', 'description', 'incidentDate', 'incidentLocation', 'subject'])
  })
})

describe('ComplaintPage', () => {
  it('asks guests to sign in', () => {
    render(<ComplaintPage token={null} signedIn={false} openAuth={vi.fn()} />)
    expect(screen.getByText('Sign in to continue')).toBeTruthy()
  })

  it('shows clear errors and sends nothing for an empty form', async () => {
    const user = userEvent.setup()
    renderPage()
    await user.click(screen.getByRole('button', { name: /Submit complaint/ }))
    expect(screen.getByText('Please fix the highlighted fields.')).toBeTruthy()
    expect(screen.getByText('Choose what the complaint is about.')).toBeTruthy()
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('submits a complaint with a bus, route, stop and picture, then shows it as pending', async () => {
    const user = userEvent.setup()
    let release
    routes['POST /api/complaints/create'] = ({ body }) => new Promise((resolve) => { release = () => resolve(reply(201, { success: true, message: 'Thank you. Your complaint CMP-NEW00001 was submitted and is pending review.', data: complaint({ ...body, id: 'n1' }) })) })
    const { notify } = renderPage()
    await user.type(screen.getByLabelText('Bus name'), 'victor')
    await user.click(screen.getByText('Victor Classic'))
    await user.selectOptions(screen.getByLabelText('Bus route'), 'b1')
    await user.selectOptions(screen.getByLabelText(/Bus stop/), 'Farmgate')
    expect(screen.getByLabelText('Incident location').value).toBe('Farmgate') // filled from the stop
    await user.click(screen.getByRole('radio', { name: /Overcharging/ }))
    await user.type(screen.getByLabelText('Subject'), 'Conductor charged 40 taka')
    await user.type(screen.getByLabelText('What happened?'), 'The conductor asked for 40 taka for a 20 taka trip and shouted.')
    const png = new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'proof.png', { type: 'image/png' })
    await user.upload(screen.getByLabelText('Add evidence pictures'), png)
    expect(await screen.findByAltText('Evidence preview')).toBeTruthy()

    await user.click(screen.getByRole('button', { name: /Submit complaint/ }))
    expect(screen.getByRole('button', { name: /Uploading and submitting/ }).disabled).toBe(true)
    const sent = calls.find((c) => c.method === 'POST').body
    expect(sent).toMatchObject({ busId: 'b1', category: 'Overcharging / Extra Fare', stopName: 'Farmgate', incidentLocation: 'Farmgate' })
    expect(sent.evidence[0]).toMatch(/^data:image\/png;base64,/)
    expect(sent).not.toHaveProperty('status')
    expect(sent).not.toHaveProperty('userId')
    release()
    expect(await screen.findByTestId('cp-success')).toBeTruthy()
    expect(notify).toHaveBeenCalledWith('Thank you. Your complaint CMP-NEW00001 was submitted and is pending review.')
    expect(screen.getByLabelText('Subject').value).toBe('') // the form is cleared
  })

  it('refuses pictures that are not JPG, PNG or WEBP', async () => {
    const user = userEvent.setup({ applyAccept: false })
    renderPage()
    await user.upload(screen.getByLabelText('Add evidence pictures'), new File(['GIF89a'], 'x.gif', { type: 'image/gif' }))
    expect(await screen.findByText('Only JPG, PNG or WEBP pictures can be added.')).toBeTruthy()
  })

  it('lists my complaints with their status and admin feedback, and filters them', async () => {
    const user = userEvent.setup()
    renderPage()
    await user.click(screen.getByRole('tab', { name: /My complaints/ }))
    const cards = await screen.findAllByTestId('cp-card')
    expect(cards).toHaveLength(2)
    expect(within(cards[0]).getByText('Pending')).toBeTruthy()
    expect(within(cards[1]).getByText('Resolved')).toBeTruthy()
    expect(within(cards[1]).getByText(/Necessary action has been taken/)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: /Resolved 1/ }))
    expect(screen.getAllByTestId('cp-card')).toHaveLength(1)
  })

  it('shows the details, evidence and timeline, and deletes a pending complaint after confirming', async () => {
    const user = userEvent.setup()
    routes['DELETE /api/complaints/c1'] = () => reply(200, { success: true, message: 'Complaint deleted.' })
    const { notify } = renderPage()
    await user.click(screen.getByRole('tab', { name: /My complaints/ }))
    await user.click(within((await screen.findAllByTestId('cp-card'))[0]).getByRole('button', { name: /View details/ }))
    const dialog = await screen.findByRole('dialog')
    expect(await within(dialog).findByAltText('Evidence picture')).toBeTruthy()
    expect(calls.find((c) => c.path === '/api/complaints/c1/evidence/e1').auth).toBe('Bearer tok') // private: sent with the token
    expect(within(within(dialog).getByRole('list', { name: 'Complaint timeline' })).getByText('Submitted')).toBeTruthy()
    expect(within(dialog).getByText('Waiting for review')).toBeTruthy()
    await user.click(within(dialog).getByRole('button', { name: /Delete complaint/ }))
    expect(calls.some((c) => c.method === 'DELETE')).toBe(false)
    await user.click(within(dialog).getByRole('button', { name: /Yes, delete/ }))
    await waitFor(() => expect(notify).toHaveBeenCalledWith('Complaint deleted.'))
  })
})

describe('AdminComplaints', () => {
  beforeEach(() => {
    routes['GET /api/complaints/stats'] = () => reply(200, { success: true, data: { total: 7, pending: 3, reviewing: 2, resolved: 1, rejected: 1,
      byCategory: [{ category: 'Harassment', count: 2 }], last30Days: [{ date: '2026-11-09', count: 2 }], topBuses: [{ bus: 'Raida', count: 4 }] } })
    routes['GET /api/complaints/all'] = () => reply(200, { success: true, total: 1, totalPages: 1, data: [complaint({ user: { name: 'Rahim', email: 'rahim@example.com' } })] })
    routes['GET /api/complaints/c1'] = () => reply(200, { success: true, data: complaint({ user: { name: 'Rahim', email: 'rahim@example.com' }, nextStatuses: ['REVIEWING', 'REJECTED'] }) })
  })

  it('shows real statistics and filters the list on the server', async () => {
    const user = userEvent.setup()
    render(<AdminComplaints token="tok" notify={vi.fn()} />)
    await screen.findByText('5 complaints need your attention')
    const stats = screen.getAllByTestId('cpa-stat')
    expect(stats.map((x) => x.querySelector('b').textContent)).toEqual(['7', '3', '2', '1', '1'])
    expect(within(stats[1]).getByText('New, waiting for you to look at it')).toBeTruthy()
    const row = await screen.findByTestId('cpa-row')
    expect(within(row).getByRole('button', { name: 'View CMP-AAAA1111' }).textContent).toContain('Review') // still to be handled
    await user.type(screen.getByLabelText('Search complaints'), 'CMP-AAAA')
    await user.click(screen.getByRole('button', { name: 'Show new complaints' }))
    expect(screen.getByRole('tab', { name: 'Pending' }).getAttribute('aria-selected')).toBe('true')
    await user.click(screen.getByRole('button', { name: /More filters/ }))
    await user.selectOptions(screen.getByLabelText('Sort'), 'oldest')
    await waitFor(() => expect(calls.filter((c) => c.path === '/api/complaints/all').at(-1).query).toMatchObject({ search: 'CMP-AAAA', status: 'PENDING', sort: 'oldest' }))
  })

  it('reviews a complaint with a required note', async () => {
    const user = userEvent.setup()
    routes['PUT /api/complaints/c1/status'] = ({ body }) => reply(200, { success: true, message: 'Complaint marked REVIEWING.', data: complaint({ status: body.status, adminNotes: body.adminNotes, user: { name: 'Rahim', email: 'r@x' }, nextStatuses: ['RESOLVED', 'REJECTED', 'PENDING'],
      history: [...complaint().history, { fromStatus: 'PENDING', toStatus: body.status, actorRole: 'admin', actorName: 'Admin', note: body.adminNotes, createdAt: '2026-11-10T05:00:00Z' }] }) })
    const notify = vi.fn()
    render(<AdminComplaints token="tok" notify={notify} />)
    await user.click(await screen.findByRole('button', { name: 'View CMP-AAAA1111' }))
    const dialog = await screen.findByRole('dialog')
    expect(await within(dialog).findByText('Rahim')).toBeTruthy()
    await user.click(within(dialog).getByRole('button', { name: /Save review/ }))
    expect(within(dialog).getByText('Choose the new status.')).toBeTruthy()
    await user.click(within(dialog).getByRole('radio', { name: 'Reviewing' }))
    expect(within(dialog).getByText('Start working on it. The user sees that their complaint is being checked.')).toBeTruthy()
    expect(within(dialog).getByLabelText(/Note for the user/).value).toContain('reviewing') // a ready-made note
    await user.clear(within(dialog).getByLabelText(/Note for the user/))
    await user.type(within(dialog).getByLabelText(/Note for the user/), 'Checking with the operator.')
    await user.click(within(dialog).getByRole('button', { name: /Save review/ }))
    await waitFor(() => expect(calls.find((c) => c.method === 'PUT').body).toEqual({ status: 'REVIEWING', adminNotes: 'Checking with the operator.' }))
    expect(await within(dialog).findByText('Checking with the operator.')).toBeTruthy() // in the review history
    // The confirmation is inside the review window, not a page message behind it.
    expect(within(dialog).getByRole('status').textContent).toContain('Saved. The complaint is now Reviewing and the passenger was notified.')
    expect(notify).not.toHaveBeenCalled()
  })
})
