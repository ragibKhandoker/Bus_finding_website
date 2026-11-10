import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AdminPromotions, couponBody, couponFormProblem, toDhakaInput } from './AdminPromotions'

let routes
let calls
const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))

const stats = {
  totals: { coupons: 6, activeCoupons: 5, redemptions: 12, discountGiven: 140, cashbackCredited: 300, cashbackFromTrips: 250, cashbackFirstTrip: 30, cashbackReferral: 20,
    cashbackMovedToWallet: 100, cashbackExpired: 5, cashbackOutstanding: 195, eligibleTrips: 60, ineligibleTrips: 4, referralsPending: 2, referralsRewarded: 3,
    referralsQualified: 0, referralsRejected: 0, liveCampaigns: 1 },
  membership: [{ tier: 'bronze', name: 'Bronze', count: 20 }], topCoupons: [{ code: 'DHAKA10', redemptions: 8, discount: 80 }],
  days: Array.from({ length: 14 }, (_, i) => ({ date: `2026-11-${String(i + 1).padStart(2, '0')}`, redemptions: 1, discount: 5, cashback: 10 })), campaigns: [],
}

beforeEach(() => {
  calls = []
  routes = {
    'GET /admin/offers/statistics': () => reply(200, { success: true, data: stats }),
    'GET /admin/offers/suspicious': () => reply(200, { success: true, data: [{ kind: 'many-redemptions', userId: 'u1', name: 'Rahim', email: 'r@x.com', count: 6, detail: '6 coupons redeemed in 24 hours, saving ৳60.' }] }),
    'GET /admin/offers': () => reply(200, { success: true, data: [{ id: 'c1', code: 'DHAKA10', title: '10% off', type: 'percentage', discountType: 'percent', discountValue: 10, minFare: 50, maxDiscount: 20,
      saving: '10% off (up to ৳20)', startsAt: '2026-11-01T00:00:00Z', expiresAt: null, totalLimit: 100, perUserLimit: 1, redemptionCount: 8, isActive: true, audience: 'all', weekendOnly: false, conditions: [] }] }),
    'GET /admin/campaigns': () => reply(200, { success: true, data: [] }),
    'GET /admin/membership/tiers': () => reply(200, { success: true, data: [{ code: 'bronze', name: 'Bronze', rank: 0, minTrips: 0 }, { code: 'gold', name: 'Gold', rank: 2, minTrips: 25 }] }),
  }
  vi.stubGlobal('fetch', vi.fn((url, options = {}) => {
    const { pathname } = new URL(url)
    const method = options.method || 'GET'
    const body = options.body && JSON.parse(options.body)
    calls.push({ method, path: pathname, body })
    const handler = routes[`${method} ${pathname}`]
    return handler ? handler({ body }) : reply(404, { success: false, message: 'Not found' })
  }))
})

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

describe('coupon form helpers', () => {
  const form = { code: 'dhaka10', title: 'Ten off', description: '', type: 'percentage', discountType: 'percent', discountValue: '10', minFare: '50', maxDiscount: '',
    startsAt: '', expiresAt: '2026-12-31T23:59', totalLimit: '', perUserLimit: '1', campaignId: '', isActive: true, audience: 'all', minTier: '', routeNumber: ' 8 ',
    weekendOnly: false, validDaysAfterIssue: '' }

  it('checks the form before sending it', () => {
    expect(couponFormProblem(form)).toBe('')
    expect(couponFormProblem({ ...form, code: 'a' })).toContain('3 to 30')
    expect(couponFormProblem({ ...form, discountValue: '120' })).toContain('100%')
    expect(couponFormProblem({ ...form, discountValue: '0' })).toContain('more than zero')
    expect(couponFormProblem({ ...form, startsAt: '2027-01-01T00:00' })).toContain('after the start')
  })

  it('sends numbers, upper-case codes and empty limits as null', () => {
    const body = couponBody(form)
    expect(body).toMatchObject({ code: 'DHAKA10', discountValue: 10, minFare: 50, maxDiscount: null, totalLimit: null, routeNumber: '8', expiresAt: '2026-12-31T23:59' })
  })

  it('shows times in Dhaka time for the date inputs', () => {
    expect(toDhakaInput('2026-11-30T18:30:00Z')).toBe('2026-12-01T00:30')
  })
})

describe('AdminPromotions', () => {
  it('shows the statistics and unusual activity', async () => {
    render(<AdminPromotions token="tok" notify={vi.fn()} />)
    expect(await screen.findByText('৳140')).toBeTruthy()
    expect(screen.getByText('5 / 6')).toBeTruthy()
    expect(await screen.findByText('6 coupons redeemed in 24 hours, saving ৳60.')).toBeTruthy()
  })

  it('creates a coupon with the values typed', async () => {
    const user = userEvent.setup()
    routes['POST /admin/offers'] = ({ body }) => reply(201, { success: true, message: `Coupon ${body.code} created.`, data: { id: 'new', ...body } })
    const notify = vi.fn()
    render(<AdminPromotions token="tok" notify={notify} />)
    await user.click(screen.getByRole('tab', { name: /Coupons/ }))
    await screen.findByText('DHAKA10')
    await user.click(screen.getByRole('button', { name: /New coupon/ }))
    await user.type(screen.getByLabelText('Code'), 'eid25')
    await user.type(screen.getByLabelText('Title'), 'Eid special')
    await user.selectOptions(screen.getByLabelText('Discount type'), 'fixed')
    await user.clear(screen.getByLabelText('Discount (৳)'))
    await user.type(screen.getByLabelText('Discount (৳)'), '25')
    await user.click(screen.getByRole('button', { name: /Save coupon/ }))
    await waitFor(() => expect(notify).toHaveBeenCalledWith('Coupon EID25 created.'))
    expect(calls.find((c) => c.method === 'POST' && c.path === '/admin/offers').body).toMatchObject({ code: 'EID25', discountType: 'fixed', discountValue: 25 })
  })

  it('shows the server’s error when saving fails', async () => {
    const user = userEvent.setup()
    routes['POST /admin/offers'] = () => reply(409, { success: false, message: 'The code DHAKA10 is already used by another coupon.' })
    render(<AdminPromotions token="tok" notify={vi.fn()} />)
    await user.click(screen.getByRole('tab', { name: /Coupons/ }))
    await user.click(await screen.findByRole('button', { name: /New coupon/ }))
    await user.type(screen.getByLabelText('Code'), 'DHAKA10')
    await user.type(screen.getByLabelText('Title'), 'Again')
    await user.click(screen.getByRole('button', { name: /Save coupon/ }))
    expect(within(await screen.findByRole('alert')).getByText(/already used by another coupon/)).toBeTruthy()
  })
})
