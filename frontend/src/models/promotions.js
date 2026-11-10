// F15: rewards, cashback, coupons, campaigns, membership and Refer & Earn. The server decides every amount; this
// file only calls it and formats what comes back.
import { apiRequest } from './api'
import { formatDate, formatNumber, t } from './i18n'

const get = (path, token) => apiRequest(path, { token })
const send = (method, path, token, body) => apiRequest(path, { token, method, ...(body === undefined ? {} : { body: JSON.stringify(body) }) })

// One id per button press, so a retried request is recognised by the server and never applied twice.
export const newReference = () => (globalThis.crypto?.randomUUID?.() || `${Date.now()}-${Math.random().toString(16).slice(2)}`)

export const promoApi = {
  summary: (token) => get('/offers/summary', token),
  analytics: (token) => get('/offers/analytics', token),
  myCoupons: (token) => get('/offers/my', token),
  trips: (token) => get('/offers/trips', token),
  validate: (code, tripId, token) => send('POST', '/offers/validate-coupon', token, { code, tripId }),
  redeem: (code, tripId, token) => send('POST', '/offers/redeem-coupon', token, { code, tripId, reference: newReference() }),
  best: (tripId, token) => get(`/offers/best-coupon?tripId=${encodeURIComponent(tripId)}`, token),
  cashback: (token) => get('/offers/cashback', token),
  cashbackHistory: (page, token) => get(`/offers/cashback/history?page=${page}&pageSize=10`, token),
  moveCashback: (amount, token) => send('POST', '/offers/use-cashback', token, { ...(amount ? { amount } : {}), reference: newReference() }),
  balances: (token) => get('/offers/user', token),
  useCredit: (kind, tripId, token) => send('POST', `/offers/use-${kind}`, token, { tripId, reference: newReference() }),
  membership: (token) => get('/membership/my', token),
  tiers: () => get('/membership/tiers'),
  campaigns: (token) => get('/campaigns/active', token),
  referralSummary: (token) => get('/referrals/summary', token),
  referralHistory: (token) => get('/referrals/history', token),
  applyReferral: (code, token) => send('POST', '/referrals/apply', token, { code }),
}

export const adminPromoApi = {
  coupons: (token) => get('/admin/offers', token),
  createCoupon: (body, token) => send('POST', '/admin/offers', token, body),
  updateCoupon: (id, body, token) => send('PUT', `/admin/offers/${id}`, token, body),
  deleteCoupon: (id, token) => send('DELETE', `/admin/offers/${id}`, token),
  issueCoupon: (id, email, token) => send('POST', `/admin/offers/${id}/issue`, token, { email }),
  campaigns: (token) => get('/admin/campaigns', token),
  createCampaign: (body, token) => send('POST', '/admin/campaigns', token, body),
  updateCampaign: (id, body, token) => send('PUT', `/admin/campaigns/${id}`, token, body),
  campaignStatus: (id, status, token) => send('POST', `/admin/campaigns/${id}/status`, token, { status }),
  settings: (token) => get('/admin/rewards/settings', token),
  saveSettings: (body, token) => send('PUT', '/admin/rewards/settings', token, body),
  tiers: (token) => get('/admin/membership/tiers', token),
  saveTiers: (tiers, token) => send('PUT', '/admin/membership/tiers', token, { tiers }),
  statistics: (token) => get('/admin/offers/statistics', token),
  redemptions: (code, page, token) => get(`/admin/offers/redemptions?${new URLSearchParams({ ...(code ? { code } : {}), page: String(page) })}`, token),
  suspicious: (token) => get('/admin/offers/suspicious', token),
  audit: (token) => get('/admin/offers/audit', token),
  referrals: (status, token) => get(`/admin/referrals${status ? `?status=${status}` : ''}`, token),
  rejectReferral: (id, note, token) => send('POST', `/admin/referrals/${id}/reject`, token, { note }),
  processTrip: (tripId, token) => send('POST', '/offers/cashback/credit', token, { tripId }),
}

export const money = (value) => `৳${formatNumber(Number(value) || 0, { maximumFractionDigits: 2 })}`

// What the code box will send: upper case, no spaces. The server checks everything else.
export const cleanCode = (text) => (text || '').toUpperCase().replace(/\s+/g, '')

// A message for a code that cannot be a coupon code at all, before asking the server.
export function couponCodeProblem(text) {
  const code = cleanCode(text)
  if (!code) return t('Enter a coupon code.')
  if (!/^[A-Z0-9_-]{3,30}$/.test(code)) return t('Coupon codes use 3 to 30 letters and numbers, like DHAKA10.')
  return ''
}

// "3 days left", "5 hours left", "Ends today"... for an expiry or end time.
export function timeLeft(date, now = Date.now()) {
  if (!date) return t('No expiry')
  const ms = new Date(date).getTime() - now
  if (ms <= 0) return t('Ended')
  const hours = ms / 3_600_000
  if (hours < 1) return t('{count} min left', { count: Math.max(1, Math.round(ms / 60_000)) })
  if (hours < 24) return t('{count} h left', { count: Math.floor(hours) })
  const days = Math.floor(hours / 24)
  return days === 1 ? t('1 day left') : t('{count} days left', { count: days })
}

// "2d 4h 10m" for a campaign countdown.
export function countdown(date, now = Date.now()) {
  const ms = Math.max(0, new Date(date).getTime() - now)
  const days = Math.floor(ms / 86_400_000)
  const hours = Math.floor((ms % 86_400_000) / 3_600_000)
  const minutes = Math.floor((ms % 3_600_000) / 60_000)
  return days > 0 ? `${formatNumber(days)}${t('d')} ${formatNumber(hours)}${t('h')} ${formatNumber(minutes)}${t('m')}` : `${formatNumber(hours)}${t('h')} ${formatNumber(minutes)}${t('m')}`
}

export const shortDay = (date) => (date ? formatDate(date, { day: 'numeric', month: 'short', year: 'numeric' }) : '')

// The link a user shares: opening it starts sign-up with the code filled in.
export const referralLink = (code) => `${window.location.origin}/offers?ref=${encodeURIComponent(code)}`

// A referral code that came in on a shared link, kept for the sign-up form.
export const REFERRAL_KEY = 'smartcommute:referral'
export function pendingReferral() {
  try { return sessionStorage.getItem(REFERRAL_KEY) || '' } catch { return '' }
}
export function rememberReferral(code) {
  try { if (code) sessionStorage.setItem(REFERRAL_KEY, code); else sessionStorage.removeItem(REFERRAL_KEY) } catch { /* private mode */ }
}
// Reads ?ref=CODE from the address once; returns the code (or '').
export function captureReferralFromUrl() {
  try {
    const code = new URLSearchParams(window.location.search).get('ref')
    const clean = (code || '').toUpperCase().replace(/[^A-Z0-9]/g, '')
    if (/^DHK[A-Z0-9]{6}$/.test(clean)) { rememberReferral(clean); return clean }
  } catch { /* no URL */ }
  return ''
}

export async function copyText(text) {
  try {
    await navigator.clipboard.writeText(text)
    return true
  } catch {
    return false
  }
}

export const COUPON_TYPE_LABELS = {
  welcome: 'Welcome', 'first-trip': 'First trip', weekend: 'Weekend', seasonal: 'Seasonal', referral: 'Referral',
  fixed: 'Fixed amount', percentage: 'Percentage', campaign: 'Special campaign',
}
export const CAMPAIGN_TYPE_LABELS = {
  cashback: 'Cashback', weekend: 'Weekend', 'first-trip': 'First trip', festival: 'Festival / Eid', welcome: 'New user welcome', 'tier-exclusive': 'Member exclusive',
}
export const CASHBACK_SOURCE_LABELS = { trip: 'Trip cashback', 'first-trip': 'First trip bonus', referral: 'Referral reward', redeemed: 'Moved to wallet', expired: 'Expired' }
export const REFERRAL_STATUS_LABELS = { pending: 'Pending', qualified: 'Qualified', rewarded: 'Rewarded', rejected: 'Rejected' }
export const TIER_COLORS = { bronze: 'bronze', silver: 'silver', gold: 'gold', platinum: 'platinum' }
