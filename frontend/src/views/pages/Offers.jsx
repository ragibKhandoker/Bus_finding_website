import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  ArrowRight, BadgeCheck, BusFront, Check, CircleAlert, Clock3, Copy, Crown, Gift, History, Info, Loader2, Lock, PartyPopper, PiggyBank,
  RefreshCw, Share2, Sparkles, Tag, Ticket, Trophy, UserPlus, Users, Wallet, X,
} from 'lucide-react'
import { formatDate, formatNumber, t } from '../../models/i18n'
import {
  CASHBACK_SOURCE_LABELS, REFERRAL_STATUS_LABELS, cleanCode, copyText, countdown, couponCodeProblem, money, pendingReferral, promoApi,
  referralLink, rememberReferral, shortDay, timeLeft,
} from '../../models/promotions'
import { RequireSignIn } from './MorePages'
import '../styles/offers.css'

// Offers & rewards (F15), made simple: three cards with what the user has, a three-step "how it works", and one
// tab at a time (use a coupon, cashback, coupons, invite friends, level, savings). Each part loads on its own and
// the server works out every amount.

const errorText = (error) => t(error?.message || 'Something went wrong. Please try again.')

// Loads one part of the page. reload({ quiet: true }) refreshes without the loading placeholder.
function useSection(loader, enabled) {
  const [state, setState] = useState({ status: 'loading', data: null, error: '' })
  const load = useCallback(async ({ quiet = false } = {}) => {
    if (!enabled) return
    if (!quiet) setState((current) => ({ ...current, status: current.data ? 'ready' : 'loading' }))
    try {
      const data = await loader()
      setState({ status: 'ready', data, error: '' })
    } catch (e) {
      setState((current) => ({ ...current, status: current.data ? 'ready' : 'error', error: errorText(e) }))
    }
  }, [loader, enabled]) // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { load() }, [load])
  return [state, load]
}

function useNow(ms = 30_000) {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => { const id = window.setInterval(() => setNow(Date.now()), ms); return () => window.clearInterval(id) }, [ms])
  return now
}

// The page's tabs. Old links (/offers#refer, #coupons, …) open the matching tab.
const TABS = [
  { id: 'use', label: 'Use a coupon', icon: Ticket },
  { id: 'cashback', label: 'My cashback', icon: Wallet },
  { id: 'coupons', label: 'My coupons', icon: Tag },
  { id: 'invite', label: 'Invite friends', icon: UserPlus },
  { id: 'level', label: 'My level', icon: Trophy },
  { id: 'savings', label: 'Savings', icon: PiggyBank },
]
const HASH_TABS = { apply: 'use', offers: 'use', campaigns: 'use', cashback: 'cashback', coupons: 'coupons', refer: 'invite', invite: 'invite', membership: 'level', level: 'level', savings: 'savings' }

function SectionError({ state, onRetry }) {
  if (state.status !== 'error') return null
  return <div className="of-error" role="alert"><CircleAlert size={16} /><span>{state.error}</span>
    <button type="button" className="of-link" onClick={() => onRetry()}><RefreshCw size={13} /> {t('Try again')}</button></div>
}

const Skeleton = ({ rows = 3, tall = false }) => <div className="of-skeleton" aria-hidden="true">{Array.from({ length: rows }, (_, i) => <i key={i} className={tall ? 'tall' : ''} />)}</div>

function PanelHead({ title, text, action }) {
  return <header className="of-panel-head"><div><h2>{title}</h2>{text && <p>{text}</p>}</div>{action}</header>
}

function CopyButton({ text, label, notify, compact = false }) {
  const [done, setDone] = useState(false)
  const copy = async () => {
    const ok = await copyText(text)
    setDone(ok)
    notify?.(ok ? t('Copied {text}', { text }) : t('Could not copy. Select the text and copy it yourself.'), ok ? 'success' : 'error')
    if (ok) window.setTimeout(() => setDone(false), 1800)
  }
  return <button type="button" className={`of-copy${compact ? ' compact' : ''}`} onClick={copy} aria-label={label || t('Copy {text}', { text })}>
    {done ? <Check size={14} /> : <Copy size={14} />}{!compact && <span>{done ? t('Copied') : t('Copy')}</span>}
  </button>
}

// ---- What the user has, at a glance ----

function OverviewCards({ cashback, coupons, membership, onOpen }) {
  const available = coupons.data?.available?.filter((item) => item.status === 'available').length
  const wallet = cashback.data?.data
  const tier = membership.data?.data
  const cards = [
    { tab: 'cashback', icon: Wallet, tone: 'green', label: 'Your cashback', value: wallet ? money(wallet.balance) : null, note: t('Tap to move it to your wallet') },
    { tab: 'coupons', icon: Ticket, tone: 'amber', label: 'Coupons ready to use', value: coupons.data ? formatNumber(available || 0) : null, note: t('Save money on a trip') },
    { tab: 'level', icon: Crown, tone: tier ? `tier-${tier.tier.code}` : 'gold', label: 'Your level', value: tier ? t(tier.tier.name) : null,
      note: tier ? (tier.next ? t('{count} trips to {tier}', { count: tier.tripsToNext, tier: t(tier.next.name) }) : t('Highest level')) : '' },
  ]
  return <div className="of-overview" data-testid="offers-overview">
    {cards.map((card) => {
      const Icon = card.icon
      return <button type="button" key={card.label} className={`of-stat ${card.tone}`} onClick={() => onOpen(card.tab)}>
        <span className="of-stat-icon" aria-hidden="true"><Icon size={20} /></span>
        <span className="of-stat-text"><small>{t(card.label)}</small>
          {card.value === null ? <span className="of-stat-skeleton" aria-label={t('Loading…')} /> : <b>{card.value}</b>}
          <em>{card.note}</em></span>
      </button>
    })}
  </div>
}

// The three things to do, in order.
function HowItWorks({ referral, onOpen, navigate }) {
  const s = referral.data?.data
  const steps = [
    { icon: BusFront, title: 'Take a bus trip', text: 'Board a bus in Find a bus and end the trip at your stop. Cashback is added by itself.', action: navigate ? ['Find a bus', () => navigate('Find a bus')] : null },
    { icon: Ticket, title: 'Use a coupon', text: 'Pick your trip, type a coupon code and see how much you save.', action: ['Try a coupon', () => onOpen('use')] },
    { icon: UserPlus, title: 'Invite friends', text: s ? t('Share your code. You get {mine} and your friend gets {theirs}.', { mine: money(s.referrerReward), theirs: money(s.referredReward) }) : 'Share your code and you both get cashback.', action: ['Get my code', () => onOpen('invite')] },
  ]
  return <section className="of-how" aria-labelledby="of-how-title">
    <h2 id="of-how-title">{t('How it works')}</h2>
    <ol className="of-steps">{steps.map((step, i) => {
      const Icon = step.icon
      return <li key={step.title} className="of-step">
        <span className="of-step-number" aria-hidden="true">{formatNumber(i + 1)}</span>
        <span className="of-step-icon" aria-hidden="true"><Icon size={20} /></span>
        <b>{t(step.title)}</b>
        <p>{t(step.text)}</p>
        {step.action && <button type="button" className="of-link" onClick={step.action[1]}>{t(step.action[0])} <ArrowRight size={13} /></button>}
      </li>
    })}</ol>
  </section>
}

// Live campaigns as one short line each.
function CampaignStrip({ state }) {
  const now = useNow()
  const list = state.data?.data || []
  if (!list.length) return null
  return <div className="of-promos">{list.map((c) => <article key={c.id} className={`of-promo theme-${c.theme || 'green'}`} data-testid="campaign">
    <span className="of-promo-icon" aria-hidden="true"><Sparkles size={18} /></span>
    <div className="of-promo-text">
      <b>{c.name}</b>
      <span>{t(c.reward)}{c.description ? ` · ${c.description}` : ''}</span>
    </div>
    <span className="of-promo-side">
      <span className="of-promo-time"><Clock3 size={13} /> {t('Ends in {time}', { time: countdown(c.endsAt, now) })}</span>
      {c.eligible === true && <span className="of-promo-tag">{t('You qualify')}</span>}
      {c.eligible === false && <span className="of-promo-tag muted"><Lock size={12} /> {t(c.note || 'Not eligible')}</span>}
    </span>
  </article>)}</div>
}

// ---- Use a coupon ----

function couponValue(coupon) {
  return coupon.discountType === 'fixed' ? money(coupon.discountValue) : `${formatNumber(coupon.discountValue)}%`
}

function OfferCard({ item, onUse, notify }) {
  const c = item.coupon
  const locked = item.status === 'locked'
  return <article className={`of-offer${locked ? ' locked' : ''}`} data-testid="offer-card">
    <div className="of-offer-value"><strong>{couponValue(c)}</strong><small>{t('OFF')}</small></div>
    <div className="of-offer-body">
      <h3>{c.title}</h3>
      <p className="of-offer-line">
        {c.minFare > 0 ? <span>{t('Fare from')} <b>{money(c.minFare)}</b></span> : <span>{t('Any fare')}</span>}
        {c.maxDiscount && c.discountType === 'percent' ? <span>{t('Save up to')} <b>{money(c.maxDiscount)}</b></span> : null}
        <span>{item.expiresAt ? timeLeft(item.expiresAt) : t('No expiry')}</span>
      </p>
      {locked && <p className="of-offer-lock"><Lock size={13} /> {t(item.note || 'Not available yet')}</p>}
      <div className="of-offer-foot">
        <span className="of-code"><Tag size={13} /> {c.code}<CopyButton text={c.code} notify={notify} compact /></span>
        {!locked && <button type="button" className="button button-green button-small" onClick={() => onUse(c.code)}>{t('Use this coupon')}</button>}
      </div>
    </div>
  </article>
}

// Three steps: choose the trip, type the code, see the saving and confirm.
function ApplyCoupon({ token, trips, reloadTrips, balances, code, setCode, onChanged, notify, inputRef }) {
  const list = trips.data?.data || []
  const usable = list.filter((trip) => trip.canUseCoupon)
  const [tripId, setTripId] = useState('')
  const [quote, setQuote] = useState(null)
  const [step, setStep] = useState('edit') // edit | confirm | done
  const [busy, setBusy] = useState('')
  const [error, setError] = useState('')
  const [done, setDone] = useState(null)
  useEffect(() => { if (!tripId && usable.length) setTripId(usable[0].tripId) }, [usable.length]) // eslint-disable-line react-hooks/exhaustive-deps
  const trip = list.find((item) => item.tripId === tripId)
  const reset = () => { setQuote(null); setStep('edit'); setError('') }

  const check = async (event) => {
    event?.preventDefault()
    const problem = couponCodeProblem(code)
    if (problem) { setError(problem); setQuote(null); return }
    if (!tripId) { setError(t('Choose the completed trip to use the coupon on.')); return }
    setBusy('check'); setError(''); setStep('edit')
    try { setQuote(await promoApi.validate(cleanCode(code), tripId, token)) } catch (e) { setQuote(null); setError(errorText(e)) } finally { setBusy('') }
  }
  const best = async () => {
    if (!tripId) return
    setBusy('best'); setError('')
    try {
      const result = await promoApi.best(tripId, token)
      if (result.data) { setCode(result.data.coupon.code); setQuote({ valid: true, message: result.data.message, data: result.data }) } else { setQuote(null); setError(t('None of your coupons work on this trip.')) }
    } catch (e) { setError(errorText(e)) } finally { setBusy('') }
  }
  const redeem = async () => {
    setBusy('redeem'); setError('')
    try {
      const result = await promoApi.redeem(cleanCode(code), tripId, token)
      setDone(result); setStep('done'); setQuote(null)
      onChanged()
    } catch (e) { setError(errorText(e)); setStep('edit') } finally { setBusy('') }
  }
  const useCredit = async (kind) => {
    if (!trip) return
    setBusy(kind); setError('')
    try {
      const result = await promoApi.useCredit(kind, trip.tripId, token)
      notify(t(result.message || 'Applied.'))
      onChanged()
    } catch (e) { setError(errorText(e)) } finally { setBusy('') }
  }
  const finish = () => { setDone(null); setStep('edit'); setCode('') }
  const data = quote?.data

  return <section className="of-panel of-apply" aria-labelledby="of-apply-title">
    <PanelHead title={<span id="of-apply-title">{t('Use a coupon')}</span>} text={t('Save money on a bus trip you already took.')}
      action={<button type="button" className="of-icon-button" onClick={() => reloadTrips({ quiet: true })} aria-label={t('Refresh trips')}><RefreshCw size={15} /></button>} />
    {trips.status === 'loading' ? <Skeleton rows={2} /> : !list.length ? <div className="of-empty"><BusFront size={22} /><b>{t('No eligible trips yet')}</b>
      <span>{t('First take a bus trip: board a bus in Find a bus and end the trip at your stop. Then come back here.')}</span></div>
      : <>
        <h3 className="of-step-label"><span>1</span> {t('Choose your trip')}</h3>
        <div className="of-trips" role="radiogroup" aria-label={t('Completed trips')}>
          {list.map((item) => <label key={item.tripId} className={`of-trip${tripId === item.tripId ? ' selected' : ''}${item.canUseCoupon ? '' : ' disabled'}`}>
            <input type="radio" name="of-trip" value={item.tripId} checked={tripId === item.tripId} disabled={!item.canUseCoupon} onChange={() => { setTripId(item.tripId); reset() }} />
            <span className="of-trip-main"><b>{item.source} → {item.destination}</b>
              <small>{item.busName} · {formatDate(item.endedAt, { day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit' })}</small></span>
            <span className="of-trip-side"><b>{money(item.fare)}</b>
              <small>{!item.canUseCoupon ? (item.leftToPay <= 0 ? t('Fully covered') : t('Older than 7 days')) : item.couponSavings + item.creditSavings > 0 ? t('{amount} saved', { amount: money(item.couponSavings + item.creditSavings) }) : t('Fare')}</small></span>
          </label>)}
        </div>

        {step === 'done' && done ? <div className="of-success" role="status" data-testid="redeem-success">
          <span className="of-success-icon"><PartyPopper size={22} /></span>
          <div><b>{t('Coupon used!')}</b><p>{t(done.message)}</p>
            <p className="of-small">{t('The money you saved is now in your wallet.')}</p></div>
          <button type="button" className="button button-green" onClick={finish}>{t('OK')}</button>
        </div> : <>
          <h3 className="of-step-label"><span>2</span> {t('Type your coupon code')}</h3>
          <form className="of-code-form" onSubmit={check} noValidate>
            <label className="of-code-input">
              <span className="sr-only">{t('Coupon code')}</span>
              <span className="of-input"><Tag size={16} aria-hidden="true" />
                <input ref={inputRef} value={code} onChange={(e) => { setCode(e.target.value.toUpperCase()); reset() }} placeholder={t('e.g. DHAKA10')} autoComplete="off" spellCheck="false"
                  maxLength={30} aria-label={t('Coupon code')} aria-invalid={Boolean(error) || (quote && !quote.valid) || undefined} />
                {code && <button type="button" className="of-clear" onClick={() => { setCode(''); reset() }} aria-label={t('Clear code')}><X size={14} /></button>}</span>
            </label>
            <button type="submit" className="button button-green" disabled={Boolean(busy) || !usable.length}>{busy === 'check' ? <Loader2 size={15} className="of-spin" /> : <Check size={15} />} {t('Check')}</button>
          </form>
          <button type="button" className="of-link of-best" onClick={best} disabled={Boolean(busy) || !tripId}>
            {busy === 'best' ? <Loader2 size={14} className="of-spin" /> : <Sparkles size={14} />} {t('Don’t have a code? Find my best coupon')}</button>
        </>}

        {error && <p className="of-form-error" role="alert"><CircleAlert size={15} /> {error}</p>}
        {quote && !quote.valid && <p className="of-form-error" role="alert"><CircleAlert size={15} /> {t(quote.message)}</p>}
        {data && quote.valid && step !== 'done' && <>
          <h3 className="of-step-label"><span>3</span> {t('See your saving')}</h3>
          <div className="of-breakdown" data-testid="coupon-breakdown">
            <div className="of-breakdown-head"><BadgeCheck size={18} /><div><b>{t('{code} works on this trip', { code: data.coupon.code })}</b><small>{data.trip}</small></div></div>
            <dl>
              <div><dt>{t('Original fare')}</dt><dd>{money(data.originalFare)}</dd></div>
              {data.alreadySaved > 0 && <div><dt>{t('Already saved on this trip')}</dt><dd>−{money(data.alreadySaved)}</dd></div>}
              <div className="saving"><dt>{t('Coupon discount')}</dt><dd>−{money(data.discount)}</dd></div>
              <div className="total"><dt>{t('You pay')}</dt><dd>{money(data.finalFare)}</dd></div>
              <div className="cashback"><dt>{t('Cashback earned on this trip')}</dt><dd>{money(data.cashbackEarned)}</dd></div>
            </dl>
            {step === 'confirm'
              ? <div className="of-confirm" role="group" aria-label={t('Confirm')}>
                <p>{t('Use {code} on this trip now? You cannot undo this.', { code: data.coupon.code })}</p>
                <div><button type="button" className="button button-outline" onClick={() => setStep('edit')} disabled={busy === 'redeem'}>{t('Cancel')}</button>
                  <button type="button" className="button button-green" onClick={redeem} disabled={busy === 'redeem'}>{busy === 'redeem' ? <Loader2 size={15} className="of-spin" /> : <Check size={15} />} {t('Yes, use it')}</button></div>
              </div>
              : <button type="button" className="button button-green of-redeem" onClick={() => setStep('confirm')}>{t('Save {amount} now', { amount: money(data.discount) })} <ArrowRight size={15} /></button>}
          </div>
        </>}

        {trip && balances.data && (balances.data.coupon > 0 || balances.data.discount > 0) && <div className="of-credits">
          <span><Gift size={15} /> {t('You also have credit to use on this trip:')}</span>
          {['coupon', 'discount'].map((kind) => {
            const left = balances.data[kind]
            return left > 0 && <button key={kind} type="button" className="button button-outline button-small" disabled={Boolean(busy) || !trip.canUseCoupon || !balances.data.isActive} onClick={() => useCredit(kind)}>
              {busy === kind ? <Loader2 size={14} className="of-spin" /> : null}{t(kind === 'coupon' ? 'Use coupon credit ({amount})' : 'Use discount credit ({amount})', { amount: money(left) })}</button>
          })}
        </div>}
      </>}
  </section>
}

function OffersList({ state, reload, onUse, notify }) {
  const list = state.data?.available || []
  return <section className="of-panel" aria-labelledby="of-offers-title">
    <PanelHead title={<span id="of-offers-title">{t('Coupons you can use')}</span>} text={t('Tap “Use this coupon” and it is filled in above.')} />
    <SectionError state={state} onRetry={reload} />
    {state.status === 'loading' ? <Skeleton rows={2} tall /> : !list.length ? state.status === 'ready' && <div className="of-empty"><Ticket size={22} /><b>{t('No offers available right now')}</b><span>{t('Check back soon: new coupons and campaigns appear here.')}</span></div>
      : <div className="of-offers">{list.map((item) => <OfferCard key={item.coupon.id} item={item} onUse={onUse} notify={notify} />)}</div>}
  </section>
}

// ---- My coupons ----

const COUPON_TABS = [['available', 'Available'], ['used', 'Used'], ['expired', 'Expired']]

function MyCoupons({ state, reload, notify, onUse }) {
  const [tab, setTab] = useState('available')
  const lists = state.data || {}
  const items = lists[tab] || []
  return <section className="of-panel" aria-labelledby="of-coupons-title">
    <PanelHead title={<span id="of-coupons-title">{t('My coupons')}</span>} text={t('All your coupon codes in one place.')} />
    <div className="of-tabs" role="tablist" aria-label={t('My coupons')}>
      {COUPON_TABS.map(([id, label]) => <button key={id} type="button" role="tab" aria-selected={tab === id} className={tab === id ? 'selected' : ''} onClick={() => setTab(id)}>
        {t(label)} <span>{formatNumber((lists[id] || []).length)}</span></button>)}
    </div>
    <SectionError state={state} onRetry={reload} />
    <div role="tabpanel" aria-label={t(COUPON_TABS.find(([id]) => id === tab)[1])}>
      {state.status === 'loading' ? <Skeleton rows={3} /> : !items.length ? <div className="of-empty small"><Ticket size={18} />
        <span>{tab === 'available' ? t('You have no coupons to use yet.') : tab === 'used' ? t('Coupons you use will be listed here.') : t('No expired coupons.')}</span></div>
        : <ul className="of-coupon-list">{items.map((item, index) => {
          const c = item.coupon
          return <li key={`${c.id}-${item.redemption?.id || index}`} className={`of-coupon-row ${tab}`} data-testid={`coupon-${tab}`}>
            <span className="of-coupon-badge">{couponValue(c)}</span>
            <span className="of-coupon-text"><b>{c.code}</b><small>{c.title}</small>
              <small className="of-coupon-meta">{tab === 'used' && item.redemption ? <>{t('Saved {amount}', { amount: money(item.redemption.discount) })} · {item.redemption.trip} · {shortDay(item.redemption.createdAt)}</>
                : tab === 'expired' ? <>{t(item.note || 'Expired')}{item.expiresAt ? ` · ${shortDay(item.expiresAt)}` : ''}</>
                  : <>{item.expiresAt ? timeLeft(item.expiresAt) : t('No expiry')}{item.status === 'locked' ? ` · ${t(item.note)}` : ''}</>}</small></span>
            <span className="of-coupon-actions"><CopyButton text={c.code} notify={notify} compact />
              {tab === 'available' && item.status === 'available' && <button type="button" className="button button-green button-small" onClick={() => onUse(c.code)}>{t('Use')}</button>}</span>
          </li>
        })}</ul>}
    </div>
  </section>
}

// ---- Cashback ----

export function cashbackProblem(text, available) {
  if (!text.trim()) return available > 0 ? '' : t('You have no cashback to move.')
  if (!/^\d+(\.\d{1,2})?$/.test(text.trim())) return t('Enter an amount in taka, like 20 or 12.50.')
  const amount = Number(text)
  if (amount <= 0) return t('Amount must be greater than zero.')
  if (amount > available) return t('You only have {amount} cashback.', { amount: money(available) })
  return ''
}

function CashbackWallet({ state, reload, history, token, notify, onChanged }) {
  const data = state.data?.data
  const [amount, setAmount] = useState('')
  const [problem, setProblem] = useState('')
  const [step, setStep] = useState('edit')
  const [busy, setBusy] = useState(false)
  const [entries, setEntries] = useState([])
  const [page, setPage] = useState(1)
  const [total, setTotal] = useState(0)
  useEffect(() => {
    if (history.data) { setEntries(history.data.data || []); setTotal(history.data.total || 0); setPage(1) }
  }, [history.data])
  const more = async () => {
    try { const next = await promoApi.cashbackHistory(page + 1, token); setEntries((current) => [...current, ...(next.data || [])]); setPage(page + 1); setTotal(next.total || 0) } catch (e) { notify(errorText(e), 'error') }
  }
  const ask = (event) => {
    event.preventDefault()
    const issue = cashbackProblem(amount, data?.balance || 0)
    setProblem(issue)
    if (!issue) setStep('confirm')
  }
  const move = async () => {
    setBusy(true)
    try {
      const result = await promoApi.moveCashback(amount.trim() ? Number(amount) : undefined, token)
      notify(t(result.message || 'Cashback moved to your wallet.'))
      setAmount(''); setStep('edit')
      onChanged()
    } catch (e) { setProblem(errorText(e)); setStep('edit') } finally { setBusy(false) }
  }
  return <section className="of-panel" aria-labelledby="of-cashback-title">
    <PanelHead title={<span id="of-cashback-title">{t('My cashback')}</span>} text={t('You get cashback by itself after every bus trip. Move it to your wallet to spend it on fares.')} />
    <SectionError state={state} onRetry={reload} />
    {!data ? <Skeleton rows={3} /> : <div className="of-cash-grid">
      <div>
        <div className="of-balance" data-testid="cashback-balance"><small>{t('Cashback you can use')}</small><b>{money(data.balance)}</b>
          <span>{t('Earned so far: {amount}', { amount: money(data.totalEarned) })}</span></div>
        {data.expiringSoon > 0 && <p className="of-note warn"><Info size={14} /> {t('{amount} expires within 7 days. Move it to your wallet to keep it.', { amount: money(data.expiringSoon) })}</p>}
        {step === 'confirm' ? <div className="of-confirm">
          <p>{t('Move {amount} cashback into your wallet?', { amount: money(amount.trim() ? Number(amount) : data.balance) })}</p>
          <div><button type="button" className="button button-outline" onClick={() => setStep('edit')} disabled={busy}>{t('Cancel')}</button>
            <button type="button" className="button button-green" onClick={move} disabled={busy}>{busy ? <Loader2 size={15} className="of-spin" /> : <Check size={15} />} {t('Yes, move it')}</button></div>
        </div> : <form className="of-move" onSubmit={ask} noValidate>
          <label htmlFor="of-cashback-amount">{t('Amount (leave empty for all)')}</label>
          <div><input id="of-cashback-amount" inputMode="decimal" value={amount} placeholder={formatNumber(data.balance, { maximumFractionDigits: 2 })}
            onChange={(e) => { setAmount(e.target.value); setProblem('') }} aria-invalid={Boolean(problem) || undefined} aria-describedby={problem ? 'of-cashback-error' : undefined} />
            <button type="submit" className="button button-green" disabled={data.balance <= 0}>{t('Move to wallet')}</button></div>
          {problem && <small id="of-cashback-error" className="of-field-error" role="alert">{problem}</small>}
        </form>}
        <p className="of-muted">{data.expiryDays > 0 ? t('Cashback stays for {days} days after you earn it.', { days: data.expiryDays }) : t('Your cashback does not expire.')}
          {data.monthlyLimit ? ` ${t('This month: {earned} of {limit}.', { earned: money(data.earnedThisMonth), limit: money(data.monthlyLimit) })}` : ''}</p>
      </div>
      <div>
        <h3 className="of-subhead"><History size={14} /> {t('Cashback history')}</h3>
        {history.status === 'loading' ? <Skeleton rows={2} /> : !entries.length ? <p className="of-muted">{t('Your cashback will appear here after your first eligible trip.')}</p>
          : <ul className="of-history">{entries.map((entry) => <li key={entry.id}>
            <span><b>{t(CASHBACK_SOURCE_LABELS[entry.source] || entry.source)}</b><small>{entry.description} · {shortDay(entry.createdAt)}</small></span>
            <strong className={entry.amount < 0 ? 'out' : 'in'}>{entry.amount < 0 ? '−' : '+'}{money(Math.abs(entry.amount))}</strong>
          </li>)}</ul>}
        {entries.length < total && <button type="button" className="of-link of-more" onClick={more}>{t('Show more')}</button>}
      </div>
    </div>}
  </section>
}

// ---- Level ----

function MembershipCard({ state, reload }) {
  const m = state.data?.data
  return <section className="of-panel" aria-labelledby="of-membership-title">
    <PanelHead title={<span id="of-membership-title">{t('My level')}</span>} text={t('The more bus trips you take, the higher your level and the more cashback you get.')} />
    <SectionError state={state} onRetry={reload} />
    {!m ? <Skeleton rows={3} /> : <>
      <div className={`of-tier-card tier-${m.tier.code}`} data-testid="membership-tier">
        <Crown size={24} />
        <div><small>{t('Your level')}</small><b>{t(m.tier.name)}</b><span>{t('{percent}% cashback on every eligible trip', { percent: formatNumber(m.cashbackPercent) })}</span></div>
      </div>
      <div className="of-progress" aria-label={t('Progress to the next level')}>
        <div className="of-progress-bar" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={m.percent}><i style={{ width: `${m.percent}%` }} /></div>
        <p>{m.next ? t('{count} more eligible trips to reach {tier}.', { count: m.tripsToNext, tier: t(m.next.name) }) : t('You are on the highest level. Enjoy the best rewards!')}</p>
      </div>
      {m.next && <div className="of-next-benefits"><small>{t('At {tier} you get', { tier: t(m.next.name) })}</small><p>{t(m.next.benefits)}</p></div>}
      <ol className="of-ladder">{m.tiers.map((tier) => <li key={tier.code} className={`${tier.code === m.tier.code ? 'current' : ''} ${tier.rank < m.tier.rank ? 'passed' : ''}`}>
        <span className={`of-tier-dot tier-${tier.code}`} /><b>{t(tier.name)}</b><small>{tier.minTrips ? t('{count}+ trips', { count: tier.minTrips }) : t('Start')}</small>
        <em>{tier.extraCashbackPercent > 0 ? t('+{percent}% cashback', { percent: formatNumber(tier.extraCashbackPercent) }) : t('Base rate')}</em>
      </li>)}</ol>
    </>}
  </section>
}

// ---- Invite friends ----

function ReferCard({ state, reload, history, token, notify, onApplied }) {
  const s = state.data?.data
  // A code from a friend's invite link is waiting here, ready to apply.
  const [code, setCode] = useState(pendingReferral)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState(null)
  const share = async () => {
    const link = referralLink(s.code)
    const text = t('Join me on SmartCommute for Dhaka buses. Use my code {code} and we both get cashback after your first trip.', { code: s.code })
    if (navigator.share) {
      try { await navigator.share({ title: 'SmartCommute', text, url: link }); return } catch { /* cancelled */ }
    }
    const ok = await copyText(`${text} ${link}`)
    notify(ok ? t('Invite link copied.') : t('Could not copy. Select the text and copy it yourself.'), ok ? 'success' : 'error')
  }
  const apply = async (event) => {
    event.preventDefault()
    if (!/^DHK[A-Z0-9]{6}$/.test(code.trim().toUpperCase().replace(/[\s-]/g, ''))) { setMessage({ error: t('Enter a referral code like DHK7K2M9Q.') }); return }
    setBusy(true); setMessage(null)
    try { const result = await promoApi.applyReferral(code.trim(), token); setMessage({ ok: t(result.message) }); setCode(''); rememberReferral(''); onApplied() } catch (e) { setMessage({ error: errorText(e) }) } finally { setBusy(false) }
  }
  const items = history.data?.data || []
  return <section className="of-panel of-refer" aria-labelledby="of-refer-title">
    <PanelHead title={<span id="of-refer-title">{t('Invite friends')}</span>} />
    <SectionError state={state} onRetry={reload} />
    {!s ? <Skeleton rows={3} /> : <div className="of-refer-grid">
      <div>
        <ol className="of-mini-steps">
          <li>{t('Send your code or link to a friend.')}</li>
          <li>{t('Your friend creates an account and enters your code here on this page.')}</li>
          <li>{s.enabled ? t('After their first bus trip, you get {mine} and they get {theirs}.', { mine: money(s.referrerReward), theirs: money(s.referredReward) }) : t('Refer & Earn is paused right now.')}</li>
        </ol>
        <div className="of-refer-code" data-testid="referral-code"><small>{t('Your referral code')}</small><b>{s.code}</b>
          <div><CopyButton text={s.code} notify={notify} /><button type="button" className="of-copy" onClick={share}><Share2 size={14} /><span>{t('Share link')}</span></button></div></div>
        <p className="of-muted">{t('Invited {count} · Successful {done} · You earned {amount}', { count: formatNumber(s.invited), done: formatNumber(s.rewarded), amount: money(s.earned) })}</p>
      </div>
      <div>
        {s.referredBy && <p className="of-note"><Users size={14} /> {t('You joined with {name}’s code ({status}).', { name: s.referredBy, status: t(REFERRAL_STATUS_LABELS[s.referredStatus] || s.referredStatus).toLowerCase() })}</p>}
        {s.canApply && <form className="of-apply-ref" onSubmit={apply} noValidate>
          <label htmlFor="of-ref-code">{t('Have a friend’s code?')}</label>
          <div><input id="of-ref-code" value={code} onChange={(e) => { setCode(e.target.value.toUpperCase()); setMessage(null) }} placeholder="DHK7K2M9Q" maxLength={12} autoComplete="off" />
            <button type="submit" className="button button-green" disabled={busy}>{busy ? <Loader2 size={15} className="of-spin" /> : null}{t('Apply')}</button></div>
          <small className="of-muted">{t('Enter it within {days} days of signing up, before your first bus trip.', { days: s.applyDays })}</small>
        </form>}
        {message?.error && <p className="of-form-error" role="alert"><CircleAlert size={15} /> {message.error}</p>}
        {message?.ok && <p className="of-form-ok" role="status"><Check size={15} /> {message.ok}</p>}
        {items.length > 0 && <><h3 className="of-subhead"><Users size={14} /> {t('People you invited')}</h3>
          <ul className="of-history">{items.map((item) => <li key={item.id}>
            <span><b>{item.friendName}</b><small>{t('Joined {date}', { date: shortDay(item.createdAt) })}</small></span>
            <span className={`of-ref-status ${item.status}`}>{t(REFERRAL_STATUS_LABELS[item.status] || item.status)}{item.status === 'rewarded' ? ` · +${money(item.reward)}` : ''}</span>
          </li>)}</ul></>}
      </div>
    </div>}
  </section>
}

// ---- Savings ----

function SavingsAnalytics({ state, reload }) {
  const a = state.data?.data
  const peak = useMemo(() => Math.max(1, ...(a?.months || []).map((m) => m.cashback + m.discounts)), [a])
  return <section className="of-panel" aria-labelledby="of-savings-title">
    <PanelHead title={<span id="of-savings-title">{t('My savings')}</span>} text={t('How much SmartCommute has saved you.')} />
    <SectionError state={state} onRetry={reload} />
    {!a ? <Skeleton rows={3} /> : <div className="of-savings">
      <div className="of-savings-totals">
        <div className="main"><small>{t('Total saved')}</small><b>{money(a.totalSaved)}</b></div>
        <div><small>{t('From cashback')}</small><b>{money(a.totalCashback)}</b></div>
        <div><small>{t('From coupons')}</small><b>{money(a.totalDiscounts)}</b></div>
        <div><small>{t('Most used coupon')}</small><b>{a.topCoupon ? `${a.topCoupon} ×${formatNumber(a.topCouponUses)}` : '—'}</b></div>
      </div>
      <figure className="of-chart" aria-label={t('Savings per month')}>
        <div className="of-bars">{a.months.map((m) => {
          const total = m.cashback + m.discounts
          return <div key={m.month} className="of-bar" title={`${t(m.label)}: ${money(m.cashback)} ${t('cashback')}, ${money(m.discounts)} ${t('discounts')}`}>
            <span className="of-bar-value">{total > 0 ? money(total) : ''}</span>
            <span className="of-bar-stack" style={{ height: `${(total / peak) * 100}%` }}>
              <i className="of-c-discount" style={{ height: total ? `${(m.discounts / total) * 100}%` : 0 }} />
              <i className="of-c-cashback" style={{ height: total ? `${(m.cashback / total) * 100}%` : 0 }} />
            </span>
            <small>{t(m.label)}</small>
          </div>
        })}</div>
        <figcaption><span><i className="of-c-cashback" /> {t('Cashback')}</span><span><i className="of-c-discount" /> {t('Coupons')}</span></figcaption>
      </figure>
      <h3 className="of-subhead"><History size={14} /> {t('Rewards history')}</h3>
      {!a.history.length ? <p className="of-muted">{t('Nothing yet. Your cashback and coupon savings will be listed here.')}</p>
        : <ul className="of-history">{a.history.map((e, i) => <li key={`${e.at}-${i}`}>
          <span><b>{t(e.title)}</b><small>{e.detail} · {shortDay(e.at)}</small></span>
          <strong className="in">+{money(e.amount)}</strong>
        </li>)}</ul>}
    </div>}
  </section>
}

// ---- Page ----

export function OffersPage({ token, signedIn, openAuth, notify = () => {}, navigate }) {
  const enabled = Boolean(signedIn && token)
  const [cashback, reloadCashback] = useSection(useCallback(() => promoApi.cashback(token), [token]), enabled)
  const [history, reloadHistory] = useSection(useCallback(() => promoApi.cashbackHistory(1, token), [token]), enabled)
  const [coupons, reloadCoupons] = useSection(useCallback(() => promoApi.myCoupons(token), [token]), enabled)
  const [membership, reloadMembership] = useSection(useCallback(() => promoApi.membership(token), [token]), enabled)
  const [analytics, reloadAnalytics] = useSection(useCallback(() => promoApi.analytics(token), [token]), enabled)
  const [campaigns] = useSection(useCallback(() => promoApi.campaigns(token), [token]), enabled)
  const [trips, reloadTrips] = useSection(useCallback(() => promoApi.trips(token), [token]), enabled)
  const [referral, reloadReferral] = useSection(useCallback(() => promoApi.referralSummary(token), [token]), enabled)
  const [referrals, reloadReferrals] = useSection(useCallback(() => promoApi.referralHistory(token), [token]), enabled)
  const [balances, reloadBalances] = useSection(useCallback(() => promoApi.balances(token), [token]), enabled)
  const [code, setCode] = useState('')
  const [tab, setTab] = useState(() => {
    const hash = (window.location.hash || '').replace('#', '')
    // A friend's invite link opens the Invite tab, where the code is waiting.
    return HASH_TABS[hash] || (pendingReferral() ? 'invite' : 'use')
  })
  const codeInput = useRef(null)
  const tabsRef = useRef(null)

  const open = (id) => {
    setTab(id)
    window.setTimeout(() => tabsRef.current?.scrollIntoView?.({ behavior: 'smooth', block: 'start' }), 30)
  }
  const use = (value) => {
    setCode(value)
    open('use')
    window.setTimeout(() => codeInput.current?.focus({ preventScroll: true }), 350)
  }
  const changed = () => {
    for (const reload of [reloadCashback, reloadHistory, reloadCoupons, reloadAnalytics, reloadTrips, reloadBalances]) reload({ quiet: true })
  }

  const hero = <section className="of-hero">
    <div className="of-hero-text">
      <span className="of-eyebrow">{t('REWARDS · DHAKA CITY BUSES')}</span>
      <h1>{t('Offers & rewards.')}</h1>
      <p>{t('Save money on your Dhaka bus trips: get cashback, use coupons and invite friends.')}</p>
    </div>
  </section>

  if (!enabled) return <div className="page inner-page of-page">{hero}<RequireSignIn openAuth={openAuth} /></div>

  return <div className="page inner-page of-page">
    {hero}
    <OverviewCards cashback={cashback} coupons={coupons} membership={membership} onOpen={open} />
    <HowItWorks referral={referral} onOpen={open} navigate={navigate} />
    <CampaignStrip state={campaigns} />

    <nav className="of-pagetabs" role="tablist" aria-label={t('Offers sections')} ref={tabsRef}>
      {TABS.map(({ id, label, icon: Icon }) => <button key={id} type="button" role="tab" aria-selected={tab === id} className={tab === id ? 'selected' : ''} onClick={() => setTab(id)}>
        <Icon size={16} /> <span>{t(label)}</span></button>)}
    </nav>

    <div className="of-tabpanel" role="tabpanel" aria-label={t(TABS.find((x) => x.id === tab).label)}>
      {tab === 'use' && <>
        <ApplyCoupon token={token} trips={trips} reloadTrips={reloadTrips} balances={balances} code={code} setCode={setCode} onChanged={changed} notify={notify} inputRef={codeInput} />
        <OffersList state={coupons} reload={reloadCoupons} onUse={use} notify={notify} />
      </>}
      {tab === 'cashback' && <CashbackWallet state={cashback} reload={reloadCashback} history={history} token={token} notify={notify} onChanged={changed} />}
      {tab === 'coupons' && <MyCoupons state={coupons} reload={reloadCoupons} notify={notify} onUse={use} />}
      {tab === 'invite' && <ReferCard state={referral} reload={reloadReferral} history={referrals} token={token} notify={notify} onApplied={() => { reloadReferral({ quiet: true }); reloadReferrals({ quiet: true }) }} />}
      {tab === 'level' && <MembershipCard state={membership} reload={reloadMembership} />}
      {tab === 'savings' && <SavingsAnalytics state={analytics} reload={reloadAnalytics} />}
    </div>
  </div>
}
