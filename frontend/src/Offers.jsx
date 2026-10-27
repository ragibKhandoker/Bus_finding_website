import React, { useCallback, useEffect, useState } from 'react'
import { BadgeDollarSign, Gift, History, Percent, RefreshCw, Ticket, Wallet } from 'lucide-react'
import { apiRequest } from './api'
import { formatDate, formatNumber, t } from './i18n'
import { Feedback, Heading, Loading, RequireSignIn } from './MorePages'

// Offers: cashback (paid into the wallet) and coupon / discount value (taken off a completed trip's fare). The
// server holds every balance and does every calculation; this page only shows what it returns.

const errorText = (error) => t(error?.message || 'Something went wrong. Please try again.')
const money = (value) => `৳${formatNumber(value || 0, { maximumFractionDigits: 2 })}`
// One id per button press, so a retried request is recognised by the server and never pays twice.
const newReference = () => (globalThis.crypto?.randomUUID?.() || `${Date.now()}-${Math.random().toString(16).slice(2)}`)

export const offerApi = {
  get: (token) => apiRequest('/offers/user', { token }),
  useCashback: (amount, token) => apiRequest('/offers/use-cashback', { token, method: 'POST', body: JSON.stringify({ ...(amount ? { amount } : {}), reference: newReference() }) }),
  useOnTrip: (kind, tripId, token) => apiRequest(`/offers/use-${kind}`, { token, method: 'POST', body: JSON.stringify({ tripId, reference: newReference() }) }),
}

// An error message for a cashback amount typed by the user ('' means all of it), or '' when it can be sent.
export function cashbackProblem(text, available) {
  if (!text.trim()) return available > 0 ? '' : t('You have no cashback to redeem.')
  if (!/^\d+(\.\d{1,2})?$/.test(text.trim())) return t('Enter an amount in taka, like 20 or 12.50.')
  const amount = Number(text)
  if (amount <= 0) return t('Amount must be greater than zero.')
  if (amount > available) return t('You only have {amount} cashback.', { amount: money(available) })
  return ''
}

const REASONS = { welcome: 'Welcome offer', opening: 'Earlier balance', granted: 'Added by SmartCommute', adjusted: 'Adjusted by SmartCommute', redeemed: 'Paid into wallet', trip: 'Used on a trip' }
const KIND_LABELS = { cashback: 'Cashback', coupon: 'Coupon', discount: 'Discount' }

function OfferCard({ icon: Icon, title, amount, status, children }) {
  return <article className="extra-panel offer-card" data-testid={`offer-${title.toLowerCase()}`}>
    <span className="offer-symbol" aria-hidden="true"><Icon size={18} /></span>
    <small>{t(title).toUpperCase()}</small>
    <strong>{money(amount)}</strong>
    <span className={`offer-status ${status}`}>{t(status === 'active' ? 'Available' : status === 'paused' ? 'Paused' : 'Used up')}</span>
    {children}
  </article>
}

export function OffersPage({ token, signedIn, openAuth, notify }) {
  const [data, setData] = useState(null)
  const [status, setStatus] = useState('loading') // loading | ready | error
  const [error, setError] = useState('')
  const [amount, setAmount] = useState('')
  const [amountError, setAmountError] = useState('')
  const [busy, setBusy] = useState('') // '' | 'cashback' | '<kind>:<tripId>'

  const load = useCallback(async ({ quiet = false } = {}) => {
    if (!signedIn) return
    if (!quiet) setStatus('loading')
    try { setData(await offerApi.get(token)); setStatus('ready'); setError('') } catch (e) { if (!quiet) setStatus('error'); setError(errorText(e)) }
  }, [signedIn, token])
  useEffect(() => { load() }, [load])

  const statusOf = (value) => !data?.isActive ? 'paused' : value > 0 ? 'active' : 'empty'

  const redeem = async (event) => {
    event.preventDefault()
    if (busy) return
    const problem = cashbackProblem(amount, data.cashback)
    setAmountError(problem)
    if (problem) return
    const value = amount.trim() ? Number(amount) : data.cashback
    if (!window.confirm(t('Move {amount} cashback into your wallet?', { amount: money(value) }))) return
    setBusy('cashback'); setError('')
    try {
      const result = await offerApi.useCashback(amount.trim() ? value : undefined, token)
      notify(t('{amount} cashback added to your wallet.', { amount: money(-(result.entry?.amount ?? -value)) }))
      setAmount('')
      await load({ quiet: true })
    } catch (e) { setError(errorText(e)) } finally { setBusy('') }
  }

  const useOnTrip = async (kind, trip) => {
    if (busy) return
    if (!window.confirm(t('Use your {kind} on the trip {route}? The saving goes into your wallet.', { kind: t(KIND_LABELS[kind]).toLowerCase(), route: `${trip.source} → ${trip.destination}` }))) return
    setBusy(`${kind}:${trip.tripId}`); setError('')
    try {
      const result = await offerApi.useOnTrip(kind, trip.tripId, token)
      notify(t('{amount} {kind} applied. You now pay {fare} for this trip.', { amount: money(result.trip?.saving), kind: t(KIND_LABELS[kind]).toLowerCase(), fare: money(result.trip?.finalFare) }))
      await load({ quiet: true })
    } catch (e) { setError(errorText(e)) } finally { setBusy('') }
  }

  const heading = <Heading icon={Gift} eyebrow={t('LITTLE PERKS, BETTER TRIPS')} title={t('Offers & rewards')} subtitle={t('Your cashback, coupons and discounts, and where to use them.')} />
  if (!signedIn) return <section className="extra-page">{heading}<RequireSignIn openAuth={openAuth} /></section>
  if (status === 'loading') return <section className="extra-page offers-page">{heading}<Loading label="Loading your offers…" /></section>
  if (status === 'error') return <section className="extra-page offers-page">{heading}<div className="extra-panel offers-error"><p>{t('Could not load your offers.')}</p><Feedback error={error} /><button type="button" className="extra-button" onClick={() => load()}><RefreshCw size={14} /> {t('Try again')}</button></div></section>

  const welcome = data.welcome || {}
  const hasWelcome = (welcome.cashback || 0) + (welcome.coupon || 0) + (welcome.discount || 0) > 0
  const trips = data.trips || []
  const history = data.history || []
  return <section className="extra-page offers-page">
    {heading}
    <Feedback error={error} />
    {!data.isActive && <p className="offers-paused" role="status">{t('Your offers are paused, so they cannot be used right now. Contact support if this looks wrong.')}</p>}
    {hasWelcome && <p className="offers-welcome"><Gift size={15} aria-hidden="true" /> {t('Welcome offer for new accounts: {cashback} cashback, {coupon} coupon and {discount} discount.', { cashback: money(welcome.cashback), coupon: money(welcome.coupon), discount: money(welcome.discount) })}</p>}
    <p className="offers-wallet"><Wallet size={15} aria-hidden="true" /> {t('Wallet balance')}: <b data-testid="wallet-balance">{money(data.walletId?.balance)}</b></p>

    <div className="offer-grid">
      <OfferCard icon={BadgeDollarSign} title="Cashback" amount={data.cashback} status={statusOf(data.cashback)}>
        <p>{t('Move cashback into your wallet whenever you like, all at once or a part of it.')}</p>
        <form className="offer-redeem" onSubmit={redeem} noValidate>
          <label htmlFor="cashback-amount">{t('Amount (leave empty for all)')}</label>
          <div>
            <input id="cashback-amount" inputMode="decimal" value={amount} placeholder={formatNumber(data.cashback, { maximumFractionDigits: 2 })} disabled={!data.isActive || busy === 'cashback'}
              onChange={(e) => { setAmount(e.target.value); setAmountError('') }} aria-invalid={Boolean(amountError) || undefined} aria-describedby={amountError ? 'cashback-error' : undefined} />
            <button type="submit" className="extra-button" disabled={!data.isActive || data.cashback <= 0 || busy === 'cashback'}>{busy === 'cashback' ? t('Moving…') : t('Move to wallet')}</button>
          </div>
          {amountError && <small id="cashback-error" className="offer-error" role="alert">{amountError}</small>}
        </form>
      </OfferCard>
      <OfferCard icon={Ticket} title="Coupons" amount={data.coupon} status={statusOf(data.coupon)}>
        <p>{t('Coupon value comes off the fare of a completed trip, once per trip, up to the fare. The saving is paid into your wallet.')}</p>
      </OfferCard>
      <OfferCard icon={Percent} title="Discounts" amount={data.discount} status={statusOf(data.discount)}>
        <p>{t('Discount value also comes off a completed trip, once per trip, and can be combined with a coupon until the fare is covered.')}</p>
      </OfferCard>
    </div>

    <article className="extra-panel extra-list-panel">
      <div className="extra-panel-title"><div><small>{t('USE ON A TRIP')}</small><h2>{t('Completed trips')}</h2></div>
        <button type="button" className="extra-icon-button" onClick={() => load({ quiet: true })} aria-label={t('Refresh')}><RefreshCw size={15} /></button></div>
      {trips.length ? <div className="offer-trips">{trips.map((trip) => {
        const covered = trip.remainingFare <= 0
        const action = (kind, used, balance) => {
          const key = `${kind}:${trip.tripId}`
          const reason = used ? t('Used') : covered ? t('Fare covered') : !data.isActive ? t('Paused') : balance <= 0 ? t('None left') : ''
          return <button type="button" className="extra-button secondary" disabled={Boolean(reason) || Boolean(busy)} onClick={() => useOnTrip(kind, trip)}
            aria-label={t('Use {kind} on {route}', { kind: t(KIND_LABELS[kind]).toLowerCase(), route: `${trip.source} → ${trip.destination}` })}>
            {busy === key ? t('Applying…') : reason ? `${t(KIND_LABELS[kind])}: ${reason}` : t('Use {kind}', { kind: t(KIND_LABELS[kind]).toLowerCase() })}
          </button>
        }
        return <div className="offer-trip" key={trip.tripId} data-testid="offer-trip">
          <div>
            <b>{trip.source} → {trip.destination}</b>
            <small>{trip.busName}{trip.endedAt ? ` · ${formatDate(trip.endedAt)}` : ''}</small>
            <small>{t('Fare {fare}', { fare: money(trip.fare) })}{trip.couponSaved > 0 ? ` · ${t('coupon −{amount}', { amount: money(trip.couponSaved) })}` : ''}{trip.discountSaved > 0 ? ` · ${t('discount −{amount}', { amount: money(trip.discountSaved) })}` : ''} · <b>{t('you pay {amount}', { amount: money(trip.remainingFare) })}</b></small>
          </div>
          <div className="offer-trip-actions">{action('coupon', trip.couponUsed, data.coupon)}{action('discount', trip.discountUsed, data.discount)}</div>
        </div>
      })}</div> : <p className="extra-empty-copy">{t('No completed trips yet. Finish a bus trip from Find a bus, then use your coupon or discount on it here.')}</p>}
    </article>

    <article className="extra-panel extra-list-panel">
      <div className="extra-panel-title"><div><small>{t('HISTORY')}</small><h2><History size={16} aria-hidden="true" /> {t('Offer history')}</h2></div></div>
      {history.length ? <ul className="offer-history">{history.map((entry) => <li key={entry._id}>
        <span><b>{t(KIND_LABELS[entry.kind] || entry.kind)} · {t(REASONS[entry.reason] || entry.reason)}</b><small>{entry.description} · {formatDate(entry.createdAt)}</small></span>
        <strong className={entry.amount < 0 ? 'out' : 'in'}>{entry.amount < 0 ? '−' : '+'}{money(Math.abs(entry.amount))}</strong>
      </li>)}</ul> : <p className="extra-empty-copy">{t('Nothing yet.')}</p>}
    </article>
  </section>
}
