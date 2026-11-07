import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { GpayPage, amountProblem } from './Gpay'
import { VerificationPage, fileProblem } from './Verification'

let routes
let calls
const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))

beforeEach(() => {
  calls = []
  routes = {}
  vi.stubGlobal('fetch', vi.fn((url, options = {}) => {
    const { pathname, search } = new URL(url)
    const method = options.method || 'GET'
    const body = options.body && JSON.parse(options.body)
    calls.push({ method, path: pathname, search, body, auth: options.headers?.Authorization })
    const handler = routes[`${method} ${pathname}`]
    return handler ? handler({ body, search }) : reply(404, { success: false, message: 'GPay account not found.' })
  }))
  vi.spyOn(window, 'confirm').mockReturnValue(true)
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('GpayPage', () => {
  const account = (balance) => ({ _id: 'g1', balance, displayCode: null })
  const trip = { tripId: 't1', busName: 'Victor Classic', source: 'Mirpur 10', destination: 'Farmgate', fare: 35, paid: false }
  const withAccount = (balance = 500) => {
    routes['GET /gpay/balance'] = () => reply(200, { success: true, data: account(balance) })
    routes['GET /gpay/transactions'] = () => reply(200, { success: true, data: [{ _id: 'x1', type: 'opening', amount: 5000, description: 'Starting balance', createdAt: '2026-10-27T08:00:00Z' }] })
    routes['GET /gpay/trips'] = () => reply(200, { success: true, data: [trip] })
    routes['POST /gpay/login'] = ({ body }) => body.code === '1234' ? reply(200, { success: true }) : reply(400, { success: false, message: 'Incorrect PIN.' })
  }
  const renderPage = () => {
    const notify = vi.fn()
    render(<GpayPage token="tok" signedIn openAuth={vi.fn()} notify={notify} />)
    return { notify }
  }
  const unlock = async (user, pin = '1234') => {
    await user.type(await screen.findByLabelText('GPay PIN'), pin)
    await user.click(screen.getByRole('button', { name: 'Unlock' }))
  }

  it('validates amounts', () => {
    expect(amountProblem('100', 500)).toBe('')
    expect(amountProblem('0', 500)).toBe('Amount must be greater than zero.')
    expect(amountProblem('600', 500)).toContain('You only have')
    expect(amountProblem('-1', 500)).toContain('Enter an amount')
    expect(amountProblem('1.005', 500)).toContain('Enter an amount')
  })

  it('creates an account and shows the PIN once', async () => {
    routes['POST /gpay/register'] = () => {
      withAccount(5000)
      return reply(201, { success: true, data: { ...account(5000), displayCode: '4821' } })
    }
    const user = userEvent.setup()
    const { notify } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'Create GPay account' }))
    expect((await screen.findByTestId('new-pin')).textContent).toBe('4821')
    expect(screen.getByTestId('gpay-balance').textContent).toBe('৳5,000')
    expect(notify).toHaveBeenCalledWith('GPay account created.')
    await user.click(screen.getByRole('button', { name: 'I have saved it' }))
    expect(screen.queryByTestId('new-pin')).toBeNull()
  })

  it('keeps money moves locked until the right PIN is entered', async () => {
    withAccount()
    const user = userEvent.setup()
    renderPage()
    expect((await screen.findByRole('button', { name: 'Move to wallet' })).disabled).toBe(true)
    await unlock(user, '9999')
    expect(await screen.findByText('Incorrect PIN.')).toBeTruthy()
    await unlock(user)
    await waitFor(() => expect(screen.getByRole('button', { name: 'Move to wallet' }).disabled).toBe(false))
    expect(screen.getByText('Unlocked')).toBeTruthy()
  })

  it('moves money to the wallet with the PIN and shows the server balance', async () => {
    withAccount()
    routes['POST /gpay/recharge'] = ({ body }) => reply(200, { success: true, data: account(500 - body.amount), transaction: { amount: -body.amount } })
    const user = userEvent.setup()
    const { notify } = renderPage()
    await unlock(user)
    const input = screen.getByLabelText('Amount (৳)')
    await user.type(input, '600')
    await user.click(screen.getByRole('button', { name: 'Move to wallet' }))
    expect(screen.getByRole('alert').textContent).toContain('You only have')
    await user.clear(input)
    await user.type(input, '120.50')
    await user.click(screen.getByRole('button', { name: 'Move to wallet' }))

    await waitFor(() => expect(screen.getByTestId('gpay-balance').textContent).toBe('৳379.5'))
    const post = calls.find((c) => c.path === '/gpay/recharge')
    expect(post.body).toMatchObject({ amount: 120.5, pin: '1234' })
    expect(post.body.reference).toBeTruthy()
    expect(notify).toHaveBeenCalledWith('৳120.5 added to your wallet.')
  })

  it('pays a trip for its fare after confirming, once', async () => {
    withAccount(100)
    routes['POST /gpay/pay'] = () => {
      routes['GET /gpay/trips'] = () => reply(200, { success: true, data: [{ ...trip, paid: true }] })
      return reply(200, { success: true, data: account(65) })
    }
    const user = userEvent.setup()
    renderPage()
    await unlock(user)
    const row = await screen.findByTestId('gpay-trip')
    await user.click(within(row).getByRole('button', { name: 'Pay ৳35 for Mirpur 10 → Farmgate' }))
    expect(window.confirm).toHaveBeenCalledWith('Pay ৳35 with GPay for Mirpur 10 → Farmgate?')
    expect(calls.find((c) => c.path === '/gpay/pay').body).toMatchObject({ tripId: 't1', pin: '1234' })
    expect(await within(await screen.findByTestId('gpay-trip')).findByText('Paid')).toBeTruthy()
    expect(screen.getByTestId('gpay-balance').textContent).toBe('৳65')
    expect(calls.filter((c) => c.path === '/gpay/pay')).toHaveLength(1)
  })

  it('shows an insufficient-balance refusal and leaves the balance alone', async () => {
    withAccount(10)
    routes['POST /gpay/pay'] = () => reply(400, { success: false, message: 'Insufficient GPay balance: the fare is ৳35.' })
    const user = userEvent.setup()
    renderPage()
    await unlock(user)
    await user.click(within(await screen.findByTestId('gpay-trip')).getByRole('button', { name: /Pay ৳35/ }))
    expect(await screen.findByText('Insufficient GPay balance: the fare is ৳35.')).toBeTruthy()
    expect(screen.getByTestId('gpay-balance').textContent).toBe('৳10')
  })

  it('lists the history and says GPay is a demo balance', async () => {
    withAccount()
    renderPage()
    expect(await screen.findByTestId('gpay-transaction')).toBeTruthy()
    expect(screen.getByText('Starting balance')).toBeTruthy()
    expect(screen.getByText(/not a bank or mobile-money account/)).toBeTruthy()
  })
})

describe('VerificationPage', () => {
  const me = { id: 'me', role: 'commuter' }
  const admin = { id: 'ad', role: 'admin' }
  const status = (latest, extra = {}) => () => reply(200, { success: true, data: latest, studentPass: { verified: false }, nid: { verified: false }, ...extra })
  const request = (id, state, extra = {}) => ({ _id: id, userName: 'Nadia Islam', userEmail: 'nadia@test.local', documentType: 'student_pass', institutionName: 'BRAC University', institutionId: '22101234', state, hasImage: true, imageUrl: `/verify/${id}/image`, createdAt: '2026-10-27T08:00:00Z', ...extra })
  const renderPage = (user = me) => {
    const notify = vi.fn()
    render(<VerificationPage token="tok" user={user} signedIn openAuth={vi.fn()} notify={notify} />)
    return { notify }
  }
  const file = (name, type, size = 1000) => new File([new Uint8Array(size)], name, { type })

  it('checks the file type and size before uploading', () => {
    expect(fileProblem(file('id.jpg', 'image/jpeg'))).toBe('')
    expect(fileProblem(file('id.pdf', 'application/pdf'))).toBe('Only JPEG, PNG or WebP images are allowed.')
    expect(fileProblem(file('big.png', 'image/png', 6 * 1024 * 1024))).toBe('The image must be at most 5 MB.')
    expect(fileProblem(null)).toBe('Choose a photo of your document.')
  })

  it('uploads the photo, then submits it, and shows Pending from the server', async () => {
    routes['GET /verify/my-status'] = status(null)
    routes['POST /verify/upload-image'] = () => reply(201, { success: true, imageId: 'img1' })
    routes['POST /verify/submit'] = () => { routes['GET /verify/my-status'] = status(request('v1', 'pending')); return reply(201, { success: true }) }
    const user = userEvent.setup()
    const { notify } = renderPage()
    const form = await screen.findByRole('form', { name: 'Submit a document' })
    await user.click(within(form).getByRole('radio', { name: 'NID' }))
    await user.upload(within(form).getByLabelText('Photo of your NID'), file('nid.jpg', 'image/jpeg'))
    await within(form).findByAltText('Your chosen document photo')
    await user.click(within(form).getByRole('button', { name: 'Submit for review' }))

    await waitFor(() => expect(calls.some((c) => c.path === '/verify/submit')).toBe(true))
    expect(calls.find((c) => c.path === '/verify/upload-image').body.image).toMatch(/^data:image\/jpeg;base64,/)
    expect(calls.find((c) => c.path === '/verify/submit').body).toEqual({ documentType: 'nid', imageId: 'img1' })
    expect(notify).toHaveBeenCalledWith('Verification submitted for review.')
    expect(await within(await screen.findByTestId('my-verification')).findByText('Pending')).toBeTruthy()
    expect(screen.queryByRole('form', { name: 'Submit a document' })).toBeNull()
  })

  it('refuses a non-image file and a student pass without its details', async () => {
    routes['GET /verify/my-status'] = status(null)
    const user = userEvent.setup({ applyAccept: false })
    renderPage()
    const form = await screen.findByRole('form', { name: 'Submit a document' })
    await user.upload(within(form).getByLabelText('Photo of your student ID card'), file('doc.pdf', 'application/pdf'))
    expect(within(form).getByRole('alert').textContent).toBe('Only JPEG, PNG or WebP images are allowed.')
    await user.click(within(form).getByRole('button', { name: 'Submit for review' }))
    expect(within(form).getByRole('alert').textContent).toBe("Enter your institution's name and your student ID.")
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('shows a rejection with its reason and lets the user try again', async () => {
    routes['GET /verify/my-status'] = status(request('v1', 'rejected', { rejectionReason: 'The photo is blurry.' }))
    renderPage()
    const box = await screen.findByTestId('my-verification')
    expect(within(box).getByText('Rejected')).toBeTruthy()
    expect(within(box).getByText(/The photo is blurry\./)).toBeTruthy()
    expect(screen.getByRole('form', { name: 'Submit a document' })).toBeTruthy()
  })

  it('shows Approved and the verified flags', async () => {
    routes['GET /verify/my-status'] = status(request('v1', 'approved'), { studentPass: { verified: true }, nid: { verified: false } })
    renderPage()
    const box = await screen.findByTestId('my-verification')
    expect(within(box).getByText('Approved')).toBeTruthy()
    expect(within(box).getByText(/Student: Verified/)).toBeTruthy()
  })

  it('lets an admin view the private photo, approve, and reject with a reason', async () => {
    routes['GET /verify/my-status'] = status(null)
    routes['GET /verify/all'] = () => reply(200, { success: true, pending: 2, data: [request('v1', 'pending'), request('v2', 'pending', { userName: 'Tanvir Hasan' })] })
    routes['GET /verify/v1/image'] = () => Promise.resolve(new Response(new Blob([new Uint8Array([0xff, 0xd8, 0xff])], { type: 'image/jpeg' }), { status: 200 }))
    routes['POST /verify/approve'] = () => reply(200, { success: true })
    routes['POST /verify/reject'] = () => reply(200, { success: true })
    globalThis.URL.createObjectURL = vi.fn(() => 'blob:doc')
    globalThis.URL.revokeObjectURL = vi.fn()
    const user = userEvent.setup()
    const { notify } = renderPage(admin)

    const queue = await screen.findByRole('article', { name: 'Verification review' })
    const rows = await within(queue).findAllByTestId('verification-request')
    expect(within(queue).getByText('2 pending')).toBeTruthy()
    await user.click(within(rows[0]).getByRole('button', { name: /View photo/ }))
    expect((await within(rows[0]).findByAltText('Document photo from Nadia Islam')).getAttribute('src')).toBe('blob:doc')
    expect(calls.find((c) => c.path === '/verify/v1/image').auth).toBe('Bearer tok')

    await user.click(within(rows[0]).getByRole('button', { name: /Approve/ }))
    expect(calls.find((c) => c.path === '/verify/approve').body).toEqual({ verificationId: 'v1' })
    expect(notify).toHaveBeenCalledWith('Verification approved.')

    const second = (await within(queue).findAllByTestId('verification-request'))[1]
    await user.click(within(second).getByRole('button', { name: /^Reject$/ }))
    await user.click(within(second).getByRole('button', { name: 'Reject request' }))
    expect(within(queue).getByText('Give a reason so the user knows what to fix.')).toBeTruthy()
    await user.type(within(second).getByLabelText('Reason for rejecting (the user will see it)'), 'Blurry photo')
    await user.click(within(second).getByRole('button', { name: 'Reject request' }))
    await waitFor(() => expect(calls.find((c) => c.path === '/verify/reject')?.body).toEqual({ verificationId: 'v2', reason: 'Blurry photo' }))
  })

  it('does not show the review queue to ordinary users', async () => {
    routes['GET /verify/my-status'] = status(null)
    renderPage()
    await screen.findByTestId('my-verification')
    expect(screen.queryByRole('article', { name: 'Verification review' })).toBeNull()
    expect(calls.some((c) => c.path === '/verify/all')).toBe(false)
  })
})
