import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

vi.mock('../../controllers/useBusRoutes', () => ({
  useBusRoutes: () => [{ status: 'ready', buses: [
    { _id: 'b1', busName: 'Victor Classic', routeNumber: '8', stops: [{ name: 'Sadarghat' }, { name: 'Abdullahpur' }] },
    { _id: 'b2', busName: 'Raida', routeNumber: '36', stops: [{ name: 'Uttara' }, { name: 'Motijheel' }] },
  ] }, () => {}],
}))

const { LostFoundPage } = await import('./LostFound')
const { formProblems, emptyForm, toBody } = await import('../../models/lostFound')

const report = (x = {}) => ({
  id: 'r1', reportType: 'lost', itemName: 'Black Samsung phone', category: 'Mobile Phone', busId: 'b1', busName: 'Victor Classic', route: 'Route 8: Sadarghat → Abdullahpur',
  location: 'Near Farmgate', incidentDate: '2026-11-08', incidentTime: '08:15', description: 'Black phone with a blue cover.', imageUrl: null,
  contactInformation: '01712345678', status: 'open', reporterName: 'Rahim', isMine: false, canManage: false, createdAt: '2026-11-08T03:00:00Z', ...x,
})

let routes
let calls
const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))

beforeEach(() => {
  calls = []
  routes = {
    'GET /api/lostfound/all': () => reply(200, { success: true, total: 2, page: 1, data: [report(), report({ id: 'r2', reportType: 'found', itemName: 'Brown wallet', category: 'Wallet', status: 'resolved' })] }),
    'GET /api/lostfound/my': () => reply(200, { success: true, data: [report({ id: 'm1', isMine: true, canManage: true })] }),
    'GET /api/lostfound/r1': () => reply(200, { success: true, data: report(), matches: [report({ id: 'r9', reportType: 'found', itemName: 'Phone found' })] }),
    'GET /api/lostfound/m1': () => reply(200, { success: true, data: report({ id: 'm1', isMine: true, canManage: true }), matches: [] }),
    'GET /api/lostfound/conversations': () => reply(200, { success: true, unread: 0, data: [] }),
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

const renderPage = (user = { role: 'commuter' }) => {
  const notify = vi.fn()
  render(<LostFoundPage token="tok" signedIn user={user} openAuth={vi.fn()} notify={notify} />)
  return { notify }
}

describe('form rules', () => {
  it('lists every problem and accepts a complete form', () => {
    const p = formProblems({ ...emptyForm(), incidentDate: '2099-01-01' }, '2026-11-10')
    expect(Object.keys(p).sort()).toEqual(['busId', 'category', 'contactInformation', 'description', 'incidentDate', 'itemName', 'location'])
    const good = { ...emptyForm(), itemName: 'Wallet', category: 'Wallet', busId: 'b1', location: 'Farmgate', incidentDate: '2026-11-09', description: 'Brown leather wallet.', contactInformation: '01712345678' }
    expect(formProblems(good, '2026-11-10')).toEqual({})
    expect(toBody(good)).not.toHaveProperty('userId')
  })
})

describe('LostFoundPage', () => {
  it('asks guests to sign in', () => {
    render(<LostFoundPage token={null} signedIn={false} openAuth={vi.fn()} />)
    expect(screen.getByText('Sign in to continue')).toBeTruthy()
    expect(calls).toHaveLength(0)
  })

  it('shows reports as cards with their type and status, and filters them', async () => {
    const user = userEvent.setup()
    renderPage()
    const cards = await screen.findAllByTestId('lf-card')
    expect(cards).toHaveLength(2)
    expect(within(cards[0]).getByText('LOST')).toBeTruthy()
    expect(within(cards[0]).getByText('Open')).toBeTruthy()
    expect(within(cards[1]).getByText('FOUND')).toBeTruthy()
    expect(within(cards[1]).getByText('Resolved')).toBeTruthy()

    await user.click(screen.getByRole('tab', { name: 'Lost items' }))
    await waitFor(() => expect(calls.at(-1).query.type).toBe('lost'))
    await user.click(screen.getByRole('button', { name: /Wallet/ }))
    await waitFor(() => expect(calls.at(-1).query.category).toBe('Wallet'))
    await user.type(screen.getByLabelText('Search reports'), 'samsung')
    await waitFor(() => expect(calls.at(-1).query.search).toBe('samsung'))
  })

  it('has an empty state', async () => {
    routes['GET /api/lostfound/all'] = () => reply(200, { success: true, total: 0, data: [] })
    renderPage()
    expect(await screen.findByText('No reports yet')).toBeTruthy()
  })

  it('shows clear errors and sends nothing for an incomplete form', async () => {
    const user = userEvent.setup()
    renderPage()
    await user.click(screen.getByRole('button', { name: /I lost something/ }))
    await user.click(screen.getByRole('button', { name: /Submit report/ }))
    expect(screen.getByText('Please fix the highlighted fields.')).toBeTruthy()
    expect(screen.getByText('Choose a category.')).toBeTruthy()
    expect(screen.getByText('Choose the Dhaka bus from the list.')).toBeTruthy()
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('submits a found report with a picture preview and shows success', async () => {
    const user = userEvent.setup()
    let release
    routes['POST /api/lostfound/create'] = ({ body }) => new Promise((resolve) => { release = () => resolve(reply(201, { success: true, message: 'Thank you! Your found item report is posted.', data: report({ ...body, id: 'n1' }) })) })
    renderPage()
    await user.click(screen.getByRole('button', { name: /I found something/ }))
    const dialog = screen.getByRole('dialog')
    await user.type(within(dialog).getByLabelText('Item name'), 'Brown wallet')
    await user.selectOptions(within(dialog).getByLabelText('Category'), 'Wallet')
    await user.type(within(dialog).getByLabelText('Bus and route'), 'raida')
    await user.click(within(dialog).getByText('Raida'))
    await user.type(within(dialog).getByLabelText('Where did you find it?'), 'Back seat')
    await user.type(within(dialog).getByLabelText('Description'), 'Brown leather wallet with cards.')
    await user.type(within(dialog).getByLabelText('Contact information'), '01812345678')
    const png = new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])], 'wallet.png', { type: 'image/png' })
    await user.upload(within(dialog).getByLabelText(/Picture/), png)
    expect(await within(dialog).findByAltText('Preview of the picture')).toBeTruthy()

    await user.click(within(dialog).getByRole('button', { name: /Submit report/ }))
    expect(within(dialog).getByRole('button', { name: /Saving/ }).disabled).toBe(true) // no double submit
    const sent = calls.find((c) => c.method === 'POST').body
    expect(sent).toMatchObject({ reportType: 'found', itemName: 'Brown wallet', category: 'Wallet', busId: 'b2', location: 'Back seat', contactInformation: '01812345678' })
    expect(sent.image).toMatch(/^data:image\/png;base64,/)
    release()
    expect(await screen.findByTestId('lf-success')).toBeTruthy()
    expect(screen.getByText('Thank you! Your found item report is posted.')).toBeTruthy()
  })

  it('refuses pictures that are not JPG, PNG or WEBP', async () => {
    const user = userEvent.setup({ applyAccept: false })
    renderPage()
    await user.click(screen.getByRole('button', { name: /I lost something/ }))
    await user.upload(screen.getByLabelText(/Picture/), new File(['GIF89a'], 'x.gif', { type: 'image/gif' }))
    expect(await screen.findByText('Only JPG, PNG or WEBP pictures can be added.')).toBeTruthy()
  })

  it('shows another person’s contact details but no edit or delete', async () => {
    const user = userEvent.setup()
    renderPage()
    const cards = await screen.findAllByTestId('lf-card')
    await user.click(within(cards[0]).getByRole('button', { name: 'View details' }))
    const dialog = await screen.findByRole('dialog')
    expect((await within(dialog).findAllByText('01712345678')).length).toBeGreaterThan(0)
    expect(within(dialog).getByText('Found it? Contact the owner')).toBeTruthy()
    expect(within(dialog).getByText('Found items that may be yours')).toBeTruthy()
    expect(within(dialog).queryByRole('button', { name: /Delete/ })).toBeNull()
  })

  it('lets the owner edit and delete their own report after confirming', async () => {
    const user = userEvent.setup()
    routes['PUT /api/lostfound/m1'] = ({ body }) => reply(200, { success: true, message: 'Report updated.', data: report({ id: 'm1', ...body, isMine: true, canManage: true }) })
    routes['DELETE /api/lostfound/m1'] = () => reply(200, { success: true, message: 'Report deleted.' })
    const { notify } = renderPage()
    await user.click(screen.getByRole('tab', { name: 'My reports' }))
    const mine = await screen.findByTestId('lf-mine')
    await user.click(within(mine).getByRole('button', { name: /Edit Black Samsung phone/ }))
    const dialog = screen.getByRole('dialog')
    await user.selectOptions(within(dialog).getByLabelText('Status'), 'resolved')
    await user.click(within(dialog).getByRole('button', { name: /Save changes/ }))
    await waitFor(() => expect(calls.find((c) => c.method === 'PUT').body.status).toBe('resolved'))

    await user.click(await within(await screen.findByTestId('lf-mine')).findByRole('button', { name: 'View' }))
    const details = await screen.findByRole('dialog')
    await user.click(await within(details).findByRole('button', { name: /Delete/ }))
    expect(calls.some((c) => c.method === 'DELETE')).toBe(false) // asks first
    await user.click(within(details).getByRole('button', { name: /Yes, delete/ }))
    await waitFor(() => expect(notify).toHaveBeenCalledWith('Report deleted.'))
  })

  it('lets a finder message the owner without a phone number', async () => {
    const user = userEvent.setup()
    routes['POST /api/lostfound/r1/conversations'] = ({ body }) => reply(201, { success: true, message: 'Message sent.', data: { id: 'c1', reportId: 'r1' }, sent: body })
    routes['GET /api/lostfound/conversations/c1/messages'] = () => reply(200, { success: true, conversation: { id: 'c1', iAmOwner: false, withName: 'Rahim', itemName: 'Black Samsung phone' },
      data: [{ id: 'x1', text: 'I think I found your phone.', mine: true, createdAt: '2026-11-10T03:00:00Z' }] })
    const { notify } = renderPage()
    const cards = await screen.findAllByTestId('lf-card')
    await user.click(within(cards[0]).getByRole('button', { name: 'View details' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(await within(dialog).findByRole('button', { name: /Message the owner/ }))
    // The chat opens in its own window; the report details are not stretched by it.
    const chatWindow = await screen.findByRole('dialog', { name: /Messages/ })
    expect(screen.getAllByRole('dialog')).toHaveLength(1)
    expect(within(chatWindow).getByTestId('lf-new-chat')).toBeTruthy()
    await user.type(within(chatWindow).getByLabelText('Message'), 'I think I found your phone.')
    await user.click(within(chatWindow).getByRole('button', { name: 'Send' }))
    await waitFor(() => expect(calls.find((c) => c.method === 'POST' && c.path === '/api/lostfound/r1/conversations').body).toEqual({ text: 'I think I found your phone.' }))
    const thread = await within(chatWindow).findByTestId('lf-thread')
    expect(within(thread).getByText('I think I found your phone.')).toBeTruthy()
    expect(within(thread).getByText(/You are talking to Rahim/)).toBeTruthy()
    expect(notify).not.toHaveBeenCalled() // nothing shown outside the window
  })

  it('lists conversations with unread counts and replies in them', async () => {
    const user = userEvent.setup()
    routes['GET /api/lostfound/conversations'] = () => reply(200, { success: true, unread: 2, data: [{ id: 'c1', reportId: 'm1', itemName: 'Black Samsung phone', reportType: 'lost', withName: 'Karim', iAmOwner: true, unread: 2,
      lastMessage: { id: 'x2', text: 'I have your phone.', mine: false, createdAt: '2026-11-10T03:00:00Z' }, lastMessageAt: '2026-11-10T03:00:00Z' }] })
    routes['GET /api/lostfound/conversations/c1/messages'] = () => reply(200, { success: true, conversation: { id: 'c1', iAmOwner: true, withName: 'Karim', itemName: 'Black Samsung phone' },
      data: [{ id: 'x2', text: 'I have your phone.', mine: false, createdAt: '2026-11-10T03:00:00Z' }] })
    routes['POST /api/lostfound/conversations/c1/messages'] = ({ body }) => reply(201, { success: true, data: { id: 'x3', text: body.text, mine: true, createdAt: '2026-11-10T03:05:00Z' } })
    renderPage()
    const tab = screen.getByRole('tab', { name: /Messages/ })
    expect(await within(tab).findByText('2')).toBeTruthy()
    await user.click(tab)
    await user.click(await screen.findByTestId('lf-conversation'))
    const dialog = await screen.findByRole('dialog')
    expect(await within(dialog).findByText('I have your phone.')).toBeTruthy()
    await user.type(within(dialog).getByLabelText('Message'), 'Thank you! Farmgate at 5?{Enter}')
    expect(await within(dialog).findByText('Thank you! Farmgate at 5?')).toBeTruthy()
    expect(within(dialog).getByRole('status').textContent).toContain('Message sent.') // shown inside the chat window
  })

  it('reacts to a message and sends a screenshot', async () => {
    const user = userEvent.setup()
    routes['GET /api/lostfound/conversations'] = () => reply(200, { success: true, unread: 0, data: [{ id: 'c1', reportId: 'm1', itemName: 'Black Samsung phone', reportType: 'lost', withName: 'Karim', iAmOwner: true, unread: 0, lastMessageAt: '2026-11-10T03:00:00Z' }] })
    routes['GET /api/lostfound/conversations/c1/messages'] = () => reply(200, { success: true, conversation: { id: 'c1', iAmOwner: true, withName: 'Karim', itemName: 'Black Samsung phone' },
      data: [{ id: 'x2', text: 'I have your phone.', mine: false, createdAt: '2026-11-10T03:00:00Z', reactions: [] }], reactions: {} })
    routes['PUT /api/lostfound/conversations/c1/messages/x2/reaction'] = ({ body }) => reply(200, { success: true, data: body.emoji ? [{ emoji: body.emoji, count: 1, mine: true }] : [] })
    routes['POST /api/lostfound/conversations/c1/messages'] = ({ body }) => reply(201, { success: true, data: { id: 'x3', text: body.text, imageUrl: body.image ? '/api/lostfound/images/abc.png' : null, mine: true, createdAt: '2026-11-10T03:05:00Z', reactions: [] } })
    renderPage()
    await user.click(screen.getByRole('tab', { name: /Messages/ }))
    await user.click(await screen.findByTestId('lf-conversation'))
    const dialog = await screen.findByRole('dialog')
    const message = await within(dialog).findByTestId('lf-message')

    await user.click(within(message).getByRole('button', { name: 'Add a reaction' }))
    await user.click(within(message).getByRole('menuitem', { name: 'React with ❤️' }))
    expect(calls.find((c) => c.method === 'PUT').body).toEqual({ emoji: '❤️' })
    const mine = await within(message).findByRole('button', { name: 'Remove your ❤️ reaction' })
    expect(mine.getAttribute('aria-pressed')).toBe('true')

    const png = new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])], 'screenshot.png', { type: 'image/png' })
    await user.upload(dialog.querySelector('input[type="file"]'), png)
    expect(await within(dialog).findByAltText('Picture to send')).toBeTruthy()
    await user.click(within(dialog).getByRole('button', { name: 'Send' }))
    await waitFor(() => expect(calls.find((c) => c.method === 'POST')?.body.image).toMatch(/^data:image\/png;base64,/))
    expect(await within(dialog).findByAltText('Picture in the chat')).toBeTruthy()
  })

  it('asks for words or a picture before sending', async () => {
    const user = userEvent.setup()
    routes['GET /api/lostfound/conversations'] = () => reply(200, { success: true, unread: 0, data: [{ id: 'c1', reportId: 'm1', itemName: 'Phone', reportType: 'lost', withName: 'Karim', iAmOwner: true, unread: 0, lastMessageAt: '2026-11-10T03:00:00Z' }] })
    routes['GET /api/lostfound/conversations/c1/messages'] = () => reply(200, { success: true, conversation: { id: 'c1', iAmOwner: true, withName: 'Karim', itemName: 'Phone' }, data: [], reactions: {} })
    renderPage()
    await user.click(screen.getByRole('tab', { name: /Messages/ }))
    await user.click(await screen.findByTestId('lf-conversation'))
    const dialog = await screen.findByRole('dialog')
    await user.click(await within(dialog).findByRole('button', { name: 'Send' }))
    expect(within(dialog).getByText('Write a message or add a picture.')).toBeTruthy()
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })
})
