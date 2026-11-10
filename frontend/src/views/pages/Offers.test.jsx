import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { OffersPage, cashbackProblem } from './Offers'
import { couponCodeProblem, countdown, timeLeft } from '../../models/promotions'

// F15 Offers page: every section loads from its own endpoint; the server's numbers are shown as they are.

const coupon = (extra = {}) => ({
  id: 'c1', code: 'DHAKA10', title: '10% off your bus fare', description: 'Save 10% on any completed trip.', type: 'percentage', discountType: 'percent',
  discountValue: 10, minFare: 50, maxDiscount: 20, saving: '10% off (up to ৳20)', startsAt: '2026-11-01T00:00:00Z', expiresAt: '2026-12-31T00:00:00Z',
  totalLimit: 1000, perUserLimit: 1, redemptionCount: 3, isActive: true, audience: 'all', minTier: null, weekendOnly: false,
  conditions: ['Fare of at least ৳50', 'Saves at most ৳20', 'Once per user'], ...extra,
})

const fixtures = () => ({
  cashback: { success: true, data: { balance: 25.5, totalEarned: 40, totalUsed: 14.5, totalExpired: 0, earnedThisMonth: 12, monthlyLimit: 350, expiringSoon: 0, nextExpiry: '2027-02-01T00:00:00Z', expiryDays: 90 } },
  history: { success: true, total: 1, page: 1, pageSize: 10, data: [{ id: 'h1', amount: 5, source: 'trip', description: 'Trip on Victor Classic, Mirpur 10 to Farmgate', remaining: 5, expiresAt: '2027-02-01T00:00:00Z', baseAmount: 4, tierBonus: 1, campaignAmount: 0, createdAt: '2026-11-08T08:00:00Z' }] },
  my: {
    success: true,
    available: [{ coupon: coupon(), status: 'available', usesLeft: 1, totalLeft: 997, expiresAt: '2026-12-31T00:00:00Z' },
      { coupon: coupon({ id: 'c2', code: 'GOLD25', title: 'Gold member exclusive', discountType: 'fixed', discountValue: 25, minTier: 'gold', type: 'campaign' }), status: 'locked', note: 'This coupon is for Gold members and above.', usesLeft: 2, totalLeft: null, expiresAt: null }],
    used: [{ coupon: coupon({ id: 'c3', code: 'WELCOME15', title: 'Welcome', discountType: 'fixed', discountValue: 15 }), status: 'used', usesLeft: 0, redemption: { id: 'r1', code: 'WELCOME15', discount: 15, trip: 'Victor Classic: Mirpur 10 to Farmgate', createdAt: '2026-11-05T08:00:00Z' } }],
    expired: [{ coupon: coupon({ id: 'c4', code: 'EID20' }), status: 'expired', note: 'Expired', usesLeft: 1, expiresAt: '2026-10-20T00:00:00Z' }],
  },
  membership: { success: true, data: { tier: { code: 'silver', name: 'Silver', rank: 1, minTrips: 10, extraCashbackPercent: 1, benefits: '+1% cashback.' }, next: { code: 'gold', name: 'Gold', rank: 2, minTrips: 25, benefits: '+2% cashback and Gold-only coupons.' },
    eligibleTrips: 12, eligibleSpend: 600, tripsToNext: 13, spendToNext: 0, percent: 13, cashbackPercent: 6,
    tiers: [{ code: 'bronze', name: 'Bronze', rank: 0, minTrips: 0, extraCashbackPercent: 0 }, { code: 'silver', name: 'Silver', rank: 1, minTrips: 10, extraCashbackPercent: 1 }, { code: 'gold', name: 'Gold', rank: 2, minTrips: 25, extraCashbackPercent: 2 }] } },
  analytics: { success: true, data: { totalCashback: 40, totalDiscounts: 15, totalSaved: 55, couponsUsed: 1, topCoupon: 'WELCOME15', topCouponUses: 1,
    months: [{ month: '2026-10', label: 'Oct', cashback: 10, discounts: 0 }, { month: '2026-11', label: 'Nov', cashback: 30, discounts: 15 }],
    history: [{ kind: 'cashback', title: 'Cashback earned', detail: 'Trip on Victor Classic', amount: 5, at: '2026-11-08T08:00:00Z' }] } },
  campaigns: { success: true, data: [{ id: 'k1', name: 'Weekend Cashback Festival', description: 'Extra 5% on Friday and Saturday.', type: 'weekend', theme: 'sunset', bannerUrl: null,
    endsAt: new Date(Date.now() + 3 * 86_400_000).toISOString(), reward: 'Extra 5% cashback per trip', cashbackPercent: 5, cashbackFixed: 0, budgetLeftPercent: 80,
    conditions: ['Friday and Saturday trips'], eligible: true }] },
  trips: { success: true, data: [{ tripId: 't1', busName: 'Victor Classic', routeNumber: '8', source: 'Mirpur 10', destination: 'Farmgate', endedAt: '2026-11-08T08:00:00Z',
    fare: 60, couponSavings: 0, creditSavings: 0, leftToPay: 60, cashback: 3, cashbackProcessed: true, paid: false, canUseCoupon: true, coupons: [] }] },
  referral: { success: true, data: { code: 'DHK7K2M9Q', invited: 3, pending: 1, qualified: 0, rewarded: 2, rejected: 0, earned: 50, referrerReward: 25, referredReward: 15, enabled: true, canApply: true, referredBy: null, applyDays: 7 } },
  referrals: { success: true, data: [{ id: 'f1', friendName: 'Karim', status: 'rewarded', createdAt: '2026-11-01T00:00:00Z', reward: 25 }] },
  balances: { _id: 'o1', cashback: 25.5, coupon: 0, discount: 0, isActive: true, walletId: { balance: 5 } },
})

let routes
let calls
let data
const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))

beforeEach(() => {
  calls = []
  data = fixtures()
  routes = {
    'GET /offers/cashback': () => reply(200, data.cashback),
    'GET /offers/cashback/history': () => reply(200, data.history),
    'GET /offers/my': () => reply(200, data.my),
    'GET /membership/my': () => reply(200, data.membership),
    'GET /offers/analytics': () => reply(200, data.analytics),
    'GET /campaigns/active': () => reply(200, data.campaigns),
    'GET /offers/trips': () => reply(200, data.trips),
    'GET /referrals/summary': () => reply(200, data.referral),
    'GET /referrals/history': () => reply(200, data.referrals),
    'GET /offers/user': () => reply(200, data.balances),
  }
  vi.stubGlobal('fetch', vi.fn((url, options = {}) => {
    const { pathname } = new URL(url)
    const method = options.method || 'GET'
    const body = options.body && JSON.parse(options.body)
    calls.push({ method, path: pathname, body, auth: options.headers?.Authorization })
    const handler = routes[`${method} ${pathname}`]
    return handler ? handler({ body }) : reply(404, { success: false, message: 'Not found' })
  }))
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const renderPage = (props = {}) => {
  const notify = vi.fn()
  render(<OffersPage token="tok" signedIn openAuth={vi.fn()} notify={notify} navigate={vi.fn()} {...props} />)
  return { notify }
}
const count = (method, path) => calls.filter((c) => c.method === method && c.path === path).length
const openTab = (user, name) => user.click(screen.getByRole('tab', { name: new RegExp(name) }))

describe('helpers', () => {
  it('checks cashback amounts before sending them', () => {
    expect(cashbackProblem('', 20)).toBe('')
    expect(cashbackProblem('12.50', 20)).toBe('')
    expect(cashbackProblem('', 0)).toBe('You have no cashback to move.')
    expect(cashbackProblem('21', 20)).toContain('You only have')
    expect(cashbackProblem('0', 20)).toBe('Amount must be greater than zero.')
    expect(cashbackProblem('1.005', 20)).toBe('Enter an amount in taka, like 20 or 12.50.')
  })

  it('explains a code that cannot be a coupon code', () => {
    expect(couponCodeProblem('')).toBe('Enter a coupon code.')
    expect(couponCodeProblem('a!')).toContain('3 to 30 letters')
    expect(couponCodeProblem(' dhaka 10 ')).toBe('')
  })

  it('formats time left and countdowns', () => {
    const now = Date.parse('2026-11-09T00:00:00Z')
    expect(timeLeft('2026-11-12T00:00:00Z', now)).toBe('3 days left')
    expect(timeLeft('2026-11-09T05:00:00Z', now)).toBe('5 h left')
    expect(timeLeft('2026-11-08T00:00:00Z', now)).toBe('Ended')
    expect(countdown('2026-11-10T02:30:00Z', now)).toBe('1d 2h 30m')
  })
})

describe('OffersPage', () => {
  it('asks guests to sign in and loads nothing', () => {
    render(<OffersPage token={null} signedIn={false} openAuth={vi.fn()} notify={vi.fn()} />)
    expect(screen.getByText('Sign in to continue')).toBeTruthy()
    expect(calls).toHaveLength(0)
  })

  it('shows loading placeholders, then the overview from the server', async () => {
    let release
    routes['GET /offers/cashback'] = () => new Promise((resolve) => { release = () => resolve(reply(200, data.cashback)) })
    renderPage()
    const overview = screen.getByTestId('offers-overview')
    expect(within(overview).getAllByLabelText('Loading…').length).toBeGreaterThan(0)
    await waitFor(() => expect(release).toBeTypeOf('function'))
    release()
    expect(await within(overview).findByText('৳25.5')).toBeTruthy()
    expect(within(overview).getByText('Silver')).toBeTruthy()
    expect(within(overview).getByText('13 trips to Gold')).toBeTruthy()
    expect(within(overview).getByText('1')).toBeTruthy() // only the coupon the user can use now
    expect(calls.every((c) => c.auth === 'Bearer tok')).toBe(true)
  })

  it('explains the three steps and opens a tab from a summary card', async () => {
    const user = userEvent.setup()
    renderPage()
    const how = screen.getByRole('region', { name: 'How it works' })
    expect(within(how).getByText('Take a bus trip')).toBeTruthy()
    expect(within(how).getByText('Use a coupon')).toBeTruthy()
    expect(await within(how).findByText(/You get ৳25 and your friend gets ৳15/)).toBeTruthy()
    await user.click(await within(screen.getByTestId('offers-overview')).findByText('Silver'))
    expect(screen.getByRole('tab', { name: /My level/ }).getAttribute('aria-selected')).toBe('true')
  })

  it('lists offers with their conditions, and locked ones say why', async () => {
    renderPage()
    const cards = await screen.findAllByTestId('offer-card')
    expect(cards).toHaveLength(2)
    expect(within(cards[0]).getByText('10% off your bus fare')).toBeTruthy()
    expect(within(cards[0]).getByText('৳50')).toBeTruthy()
    expect(within(cards[0]).getByRole('button', { name: 'Use this coupon' })).toBeTruthy()
    expect(within(cards[1]).getByText('This coupon is for Gold members and above.')).toBeTruthy()
    expect(within(cards[1]).queryByRole('button', { name: 'Use this coupon' })).toBeNull()
  })

  it('switches between available, used and expired coupons', async () => {
    const user = userEvent.setup()
    renderPage()
    await openTab(user, 'My coupons')
    const tabs = await screen.findByRole('tablist', { name: 'My coupons' })
    await waitFor(() => expect(screen.getAllByTestId('coupon-available')).toHaveLength(2))
    await user.click(within(tabs).getByRole('tab', { name: /Used/ }))
    const used = screen.getAllByTestId('coupon-used')
    expect(used).toHaveLength(1)
    expect(within(used[0]).getByText(/Saved ৳15/)).toBeTruthy()
    await user.click(within(tabs).getByRole('tab', { name: /Expired/ }))
    expect(within(screen.getByTestId('coupon-expired')).getByText('EID20')).toBeTruthy()
  })

  it('shows a clear error for a badly typed code without asking the server', async () => {
    const user = userEvent.setup()
    renderPage()
    const input = await screen.findByLabelText('Coupon code')
    await user.type(input, '!!')
    await user.click(screen.getByRole('button', { name: 'Check' }))
    expect(screen.getByRole('alert').textContent).toContain('3 to 30 letters')
    expect(count('POST', '/offers/validate-coupon')).toBe(0)
  })

  it('shows the server’s reason when a coupon does not apply', async () => {
    const user = userEvent.setup()
    routes['POST /offers/validate-coupon'] = () => reply(200, { success: true, valid: false, message: 'This coupon has expired.', data: { valid: false, originalFare: 60, finalFare: 60 } })
    renderPage()
    await user.type(await screen.findByLabelText('Coupon code'), 'eid20')
    await user.click(screen.getByRole('button', { name: 'Check' }))
    expect(await screen.findByText(/This coupon has expired\./)).toBeTruthy()
    expect(calls.find((c) => c.path === '/offers/validate-coupon').body).toEqual({ code: 'EID20', tripId: 't1' })
  })

  it('breaks down the saving, redeems after confirming and refreshes the cashback', async () => {
    const user = userEvent.setup()
    routes['POST /offers/validate-coupon'] = () => reply(200, { success: true, valid: true, message: 'DHAKA10 saves you ৳6 on this trip.',
      data: { valid: true, coupon: coupon(), trip: 'Victor Classic: Mirpur 10 to Farmgate', originalFare: 60, alreadySaved: 0, discount: 6, finalFare: 54, cashbackEarned: 3 } })
    routes['POST /offers/redeem-coupon'] = ({ body }) => reply(200, { success: true, message: 'DHAKA10 applied: you saved ৳6. You pay ৳54 for this trip.', data: { code: body.code, discount: 6 }, quote: { discount: 6 } })
    renderPage()
    const offer = (await screen.findAllByTestId('offer-card'))[0]
    await user.click(within(offer).getByRole('button', { name: 'Use this coupon' }))
    expect(screen.getByLabelText('Coupon code').value).toBe('DHAKA10')
    await user.click(screen.getByRole('button', { name: 'Check' }))
    const breakdown = await screen.findByTestId('coupon-breakdown')
    expect(within(breakdown).getByText('Original fare').nextSibling.textContent).toBe('৳60')
    expect(within(breakdown).getByText('Coupon discount').nextSibling.textContent).toBe('−৳6')
    expect(within(breakdown).getByText('You pay').nextSibling.textContent).toBe('৳54')
    expect(within(breakdown).getByText('Cashback earned on this trip').nextSibling.textContent).toBe('৳3')

    const before = count('GET', '/offers/cashback')
    await user.click(within(breakdown).getByRole('button', { name: /Save ৳6 now/ }))
    expect(count('POST', '/offers/redeem-coupon')).toBe(0) // asks first
    await user.click(screen.getByRole('button', { name: /Yes, use it/ }))
    const success = await screen.findByTestId('redeem-success')
    expect(success.textContent).toContain('You pay ৳54')
    const sent = calls.find((c) => c.path === '/offers/redeem-coupon').body
    expect(sent).toMatchObject({ code: 'DHAKA10', tripId: 't1' })
    expect(Object.keys(sent).sort()).toEqual(['code', 'reference', 'tripId']) // never an amount
    await waitFor(() => expect(count('GET', '/offers/cashback')).toBeGreaterThan(before))
    await user.click(within(success).getByRole('button', { name: 'OK' }))
    expect(screen.queryByTestId('redeem-success')).toBeNull()
  })

  it('moves cashback to the wallet after confirming and shows the new balance', async () => {
    const user = userEvent.setup()
    routes['POST /offers/use-cashback'] = () => {
      data.cashback = { success: true, data: { ...data.cashback.data, balance: 15.5, totalUsed: 24.5 } }
      return reply(200, { success: true, message: '৳10 cashback added to your wallet.' })
    }
    const { notify } = renderPage()
    await openTab(user, 'My cashback')
    const balance = await screen.findByTestId('cashback-balance')
    expect(balance.textContent).toContain('৳25.5')
    await user.type(screen.getByLabelText('Amount (leave empty for all)'), '10')
    await user.click(screen.getByRole('button', { name: 'Move to wallet' }))
    await user.click(screen.getByRole('button', { name: /Yes, move it/ }))
    await waitFor(() => expect(screen.getByTestId('cashback-balance').textContent).toContain('৳15.5'))
    expect(calls.find((c) => c.path === '/offers/use-cashback').body.amount).toBe(10)
    expect(notify).toHaveBeenCalledWith('৳10 cashback added to your wallet.')
  })

  it('shows membership progress and the next tier’s benefits', async () => {
    const user = userEvent.setup()
    renderPage()
    await openTab(user, 'My level')
    const tier = await screen.findByTestId('membership-tier')
    expect(tier.textContent).toContain('Silver')
    expect(screen.getByRole('progressbar').getAttribute('aria-valuenow')).toBe('13')
    expect(screen.getByText('13 more eligible trips to reach Gold.')).toBeTruthy()
    expect(screen.getByText('+2% cashback and Gold-only coupons.')).toBeTruthy()
  })

  it('shows the referral code, stats and accepts a friend’s code', async () => {
    const user = userEvent.setup()
    routes['POST /referrals/apply'] = () => reply(201, { success: true, message: 'Code accepted. You and Rahim will both be rewarded after your first eligible bus trip.' })
    renderPage()
    await openTab(user, 'Invite friends')
    expect((await screen.findByTestId('referral-code')).textContent).toContain('DHK7K2M9Q')
    expect(screen.getByText('Karim')).toBeTruthy()
    await user.type(screen.getByLabelText('Have a friend’s code?'), 'dhk-abc234')
    await user.click(within(screen.getByLabelText('Have a friend’s code?').closest('form')).getByRole('button', { name: 'Apply' }))
    expect(await screen.findByText(/Code accepted/)).toBeTruthy()
  })

  it('fills in the friend’s code from an invite link', async () => {
    sessionStorage.setItem('smartcommute:referral', 'DHKABC234')
    renderPage()
    expect((await screen.findByLabelText('Have a friend’s code?')).value).toBe('DHKABC234')
    sessionStorage.clear()
  })

  it('shows campaigns with a countdown and the savings chart', async () => {
    const user = userEvent.setup()
    renderPage()
    const campaign = await screen.findByTestId('campaign')
    expect(within(campaign).getByText('Weekend Cashback Festival')).toBeTruthy()
    expect(within(campaign).getByText(/Ends in 2d|Ends in 3d/)).toBeTruthy()
    expect(within(campaign).getByText('You qualify')).toBeTruthy()
    await openTab(user, 'Savings')
    expect(await screen.findByText('WELCOME15 ×1')).toBeTruthy()
  })

  it('has empty states when there is nothing yet', async () => {
    const user = userEvent.setup()
    data.trips = { success: true, data: [] }
    data.campaigns = { success: true, data: [] }
    data.my = { success: true, available: [], used: [], expired: [] }
    renderPage()
    expect(await screen.findByText('No eligible trips yet')).toBeTruthy()
    expect(await screen.findByText('No offers available right now')).toBeTruthy()
    expect(screen.queryByTestId('campaign')).toBeNull()
    await openTab(user, 'My coupons')
    expect(await screen.findByText('You have no coupons to use yet.')).toBeTruthy()
  })

  it('keeps other sections working when one fails, and retries it', async () => {
    const user = userEvent.setup()
    let fail = true
    routes['GET /membership/my'] = () => (fail ? reply(500, { message: 'Something went wrong on the server.' }) : reply(200, data.membership))
    renderPage()
    expect(await within(screen.getByTestId('offers-overview')).findByText('৳25.5')).toBeTruthy()
    await openTab(user, 'My level')
    expect(await screen.findByText('Something went wrong on the server.')).toBeTruthy()
    fail = false
    await user.click(screen.getByRole('button', { name: /Try again/ }))
    expect(await screen.findByTestId('membership-tier')).toBeTruthy()
  })
})
