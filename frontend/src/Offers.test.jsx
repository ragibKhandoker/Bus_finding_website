import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { OffersPage, cashbackProblem } from './Offers'

const offers = (extra = {}) => ({
  _id: 'o1', userId: 'me', walletId: { balance: 5 }, cashback: 20, coupon: 10, discount: 15, isActive: true,
  welcome: { cashback: 20, coupon: 10, discount: 15 },
  history: [{ _id: 'h1', kind: 'cashback', amount: 20, balanceAfter: 20, reason: 'welcome', description: 'Welcome cashback', createdAt: '2026-10-27T08:00:00Z' }],
  trips: [{ tripId: 't1', busName: 'Victor Classic', source: 'Mirpur 10', destination: 'Farmgate', endedAt: '2026-10-27T09:00:00Z', fare: 30, couponSaved: 0, discountSaved: 0, remainingFare: 30, couponUsed: false, discountUsed: false }],
  ...extra,
})

let routes
let calls
const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))

beforeEach(() => {
  calls = []
  routes = { 'GET /offers/user': () => reply(200, offers()) }
  vi.stubGlobal('fetch', vi.fn((url, options = {}) => {
    const { pathname } = new URL(url)
    const method = options.method || 'GET'
    const body = options.body && JSON.parse(options.body)
    calls.push({ method, path: pathname, body, auth: options.headers?.Authorization })
    const handler = routes[`${method} ${pathname}`]
    return handler ? handler({ body }) : reply(404, { success: false, message: 'Not found' })
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
  render(<OffersPage token="tok" signedIn openAuth={vi.fn()} notify={notify} {...props} />)
  return { notify }
}

describe('cashbackProblem', () => {
  it('accepts empty (all) or a valid amount within the balance', () => {
    expect(cashbackProblem('', 20)).toBe('')
    expect(cashbackProblem('12.50', 20)).toBe('')
    expect(cashbackProblem('', 0)).toBe('You have no cashback to redeem.')
    expect(cashbackProblem('21', 20)).toContain('You only have')
    expect(cashbackProblem('0', 20)).toBe('Amount must be greater than zero.')
    expect(cashbackProblem('-5', 20)).toBe('Enter an amount in taka, like 20 or 12.50.')
    expect(cashbackProblem('1.005', 20)).toBe('Enter an amount in taka, like 20 or 12.50.')
  })
})

describe('OffersPage', () => {
  it('shows the remaining cashback, coupon and discount, the welcome offer and history', async () => {
    renderPage()
    expect(within(await screen.findByTestId('offer-cashback')).getByText('৳20')).toBeTruthy()
    expect(within(screen.getByTestId('offer-coupons')).getByText('৳10')).toBeTruthy()
    expect(within(screen.getByTestId('offer-discounts')).getByText('৳15')).toBeTruthy()
    expect(within(screen.getByTestId('offer-coupons')).getByText('Available')).toBeTruthy()
    expect(screen.getByText(/Welcome offer for new accounts: ৳20 cashback, ৳10 coupon and ৳15 discount/)).toBeTruthy()
    expect(screen.getByTestId('wallet-balance').textContent).toBe('৳5')
    expect(screen.getByText(/^Welcome cashback ·/)).toBeTruthy()
    expect(screen.getByText('+৳20')).toBeTruthy()
    expect(calls[0].auth).toBe('Bearer tok')
  })

  it('shows used-up and paused offers as not available', async () => {
    routes['GET /offers/user'] = () => reply(200, offers({ coupon: 0, isActive: false }))
    renderPage()
    expect(within(await screen.findByTestId('offer-coupons')).getByText('Paused')).toBeTruthy()
    expect(screen.getByText(/Your offers are paused/)).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Move to wallet' }).disabled).toBe(true)
    cleanup()
    routes['GET /offers/user'] = () => reply(200, offers({ coupon: 0 }))
    renderPage()
    expect(within(await screen.findByTestId('offer-coupons')).getByText('Used up')).toBeTruthy()
  })

  it('moves part of the cashback into the wallet after confirming, then shows the server balances', async () => {
    routes['POST /offers/use-cashback'] = () => {
      routes['GET /offers/user'] = () => reply(200, offers({ cashback: 8, walletId: { balance: 17 } }))
      return reply(200, { success: true, entry: { amount: -12 }, offers: { cashback: 8 } })
    }
    const user = userEvent.setup()
    const { notify } = renderPage()
    await user.type(await screen.findByLabelText('Amount (leave empty for all)'), '12')
    await user.click(screen.getByRole('button', { name: 'Move to wallet' }))

    expect(window.confirm).toHaveBeenCalledWith('Move ৳12 cashback into your wallet?')
    const post = calls.find((c) => c.method === 'POST')
    expect(post.body.amount).toBe(12)
    expect(post.body.reference).toBeTruthy()
    await waitFor(() => expect(within(screen.getByTestId('offer-cashback')).getByText('৳8')).toBeTruthy())
    expect(screen.getByTestId('wallet-balance').textContent).toBe('৳17')
    expect(notify).toHaveBeenCalledWith('৳12 cashback added to your wallet.')
  })

  it('refuses a bad amount without asking the server, and sends all when empty', async () => {
    routes['POST /offers/use-cashback'] = () => reply(200, { success: true, entry: { amount: -20 } })
    const user = userEvent.setup()
    renderPage()
    const input = await screen.findByLabelText('Amount (leave empty for all)')
    await user.type(input, '25')
    await user.click(screen.getByRole('button', { name: 'Move to wallet' }))
    expect(screen.getByRole('alert').textContent).toBe('You only have ৳20 cashback.')
    expect(calls.some((c) => c.method === 'POST')).toBe(false)

    await user.clear(input)
    await user.click(screen.getByRole('button', { name: 'Move to wallet' }))
    await waitFor(() => expect(calls.some((c) => c.method === 'POST')).toBe(true))
    expect(calls.find((c) => c.method === 'POST').body.amount).toBeUndefined()
  })

  it('does nothing when the confirmation is cancelled, and shows server refusals', async () => {
    routes['POST /offers/use-cashback'] = () => reply(400, { success: false, message: 'Not enough cashback available.' })
    const user = userEvent.setup()
    renderPage()
    window.confirm.mockReturnValueOnce(false)
    await user.click(await screen.findByRole('button', { name: 'Move to wallet' }))
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
    await user.click(screen.getByRole('button', { name: 'Move to wallet' }))
    expect(await screen.findByText('Not enough cashback available.')).toBeTruthy()
    expect(within(screen.getByTestId('offer-cashback')).getByText('৳20')).toBeTruthy()
  })

  it('uses a coupon on a completed trip and shows the new fare from the server', async () => {
    let finish
    routes['POST /offers/use-coupon'] = () => new Promise((resolve) => {
      finish = () => {
        routes['GET /offers/user'] = () => reply(200, offers({ coupon: 0, trips: [{ ...offers().trips[0], couponSaved: 10, couponUsed: true, remainingFare: 20 }] }))
        resolve(new Response(JSON.stringify({ success: true, trip: { saving: 10, finalFare: 20 } }), { status: 200 }))
      }
    })
    const user = userEvent.setup()
    const { notify } = renderPage()
    const trip = await screen.findByTestId('offer-trip')
    await user.click(within(trip).getByRole('button', { name: 'Use coupon on Mirpur 10 → Farmgate' }))
    expect(calls.find((c) => c.path === '/offers/use-coupon').body.tripId).toBe('t1')
    expect(within(trip).getByRole('button', { name: 'Use discount on Mirpur 10 → Farmgate' }).disabled).toBe(true)
    finish()

    await waitFor(() => expect(within(screen.getByTestId('offer-trip')).getByText(/coupon −৳10/)).toBeTruthy())
    expect(within(screen.getByTestId('offer-trip')).getByText('you pay ৳20')).toBeTruthy()
    expect(within(screen.getByTestId('offer-trip')).getByRole('button', { name: 'Use coupon on Mirpur 10 → Farmgate' }).disabled).toBe(true)
    expect(notify).toHaveBeenCalledWith('৳10 coupon applied. You now pay ৳20 for this trip.')
    expect(calls.filter((c) => c.path === '/offers/use-coupon')).toHaveLength(1)
  })

  it('shows an empty state when there are no completed trips', async () => {
    routes['GET /offers/user'] = () => reply(200, offers({ trips: [], history: [] }))
    renderPage()
    expect(await screen.findByText(/No completed trips yet/)).toBeTruthy()
    expect(screen.getByText('Nothing yet.')).toBeTruthy()
  })

  it('shows loading, then an error with a retry', async () => {
    routes['GET /offers/user'] = () => reply(500, { success: false, message: 'Something went wrong on the server.' })
    const user = userEvent.setup()
    renderPage()
    expect(screen.getByText('Loading your offers…')).toBeTruthy()
    expect(await screen.findByText('Could not load your offers.')).toBeTruthy()
    routes['GET /offers/user'] = () => reply(200, offers())
    await user.click(screen.getByRole('button', { name: /Try again/ }))
    expect(await screen.findByTestId('offer-cashback')).toBeTruthy()
  })

  it('asks visitors to sign in', () => {
    renderPage({ signedIn: false, token: null })
    expect(screen.getByText('Sign in to continue')).toBeTruthy()
    expect(calls).toHaveLength(0)
  })
})
