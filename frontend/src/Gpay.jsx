import React, { useCallback, useEffect, useState } from 'react'
import { ArrowDownLeft, ArrowUpRight, Bus, KeyRound, Lock, RefreshCw, Wallet } from 'lucide-react'
import { apiRequest } from './api'
import { formatDate, formatDateTime, formatNumber, t } from './i18n'
import { Feedback, Heading, Loading, RequireSignIn } from './MorePages'

// GPay, the in-app stored-value account. The server keeps the balance and the history; moving money needs the
// 4-digit PIN, which this page keeps in memory only while it is unlocked and never stores.

const errorText = (error) => t(error?.message || 'Something went wrong. Please try again.')
const money = (value) => `৳${formatNumber(value || 0, { maximumFractionDigits: 2 })}`
// One id per button press, so a retried request is recognised by the server and never moves money twice.
const newReference = () => (globalThis.crypto?.randomUUID?.() || `${Date.now()}-${Math.random().toString(16).slice(2)}`)
const post = (path, token, body) => apiRequest(path, { token, method: 'POST', ...(body ? { body: JSON.stringify(body) } : {}) })

export const gpayApi = {
  balance: (token) => apiRequest('/gpay/balance', { token }),
  register: (token) => post('/gpay/register', token),
  unlock: (pin, token) => post('/gpay/login', token, { code: pin }),
  resetPin: (password, token) => post('/gpay/reset-pin', token, { password }),
  transactions: (token) => apiRequest('/gpay/transactions?pageSize=30', { token }),
  trips: (token) => apiRequest('/gpay/trips', { token }),
  recharge: (amount, pin, token) => post('/gpay/recharge', token, { amount, pin, reference: newReference() }),
  payTrip: (tripId, pin, token) => post('/gpay/pay', token, { tripId, pin, reference: newReference() }),
}

export function amountProblem(text, balance) {
  if (!/^\d+(\.\d{1,2})?$/.test(text.trim())) return t('Enter an amount in taka, like 20 or 12.50.')
  const amount = Number(text)
  if (amount <= 0) return t('Amount must be greater than zero.')
  if (amount > balance) return t('You only have {amount} in GPay.', { amount: money(balance) })
  return ''
}

const TYPE_LABELS = { opening: 'Starting balance', 'wallet-recharge': 'Moved to wallet', 'trip-payment': 'Trip fare', payment: 'Payment', 'top-up': 'Top-up' }

// Shows a newly created PIN once, with a clear warning.
function NewPin({ pin, onDone }) {
  return <div className="gpay-pin-reveal" role="alert">
    <KeyRound size={18} aria-hidden="true" />
    <div><b>{t('Your GPay PIN')}: <code data-testid="new-pin">{pin}</code></b><span>{t('Write it down now. It will not be shown again, and you need it to move money.')}</span></div>
    <button type="button" className="extra-button" onClick={onDone}>{t('I have saved it')}</button>
  </div>
}

export function GpayPage({ token, signedIn, openAuth, notify }) {
  const [account, setAccount] = useState(null)
  const [status, setStatus] = useState('loading') // loading | none | ready | error
  const [error, setError] = useState('')
  const [pin, setPin] = useState('') // the unlocked PIN, in memory only
  const [pinInput, setPinInput] = useState('')
  const [shownPin, setShownPin] = useState('')
  const [history, setHistory] = useState([])
  const [trips, setTrips] = useState([])
  const [amount, setAmount] = useState('')
  const [amountError, setAmountError] = useState('')
  const [busy, setBusy] = useState('')
  const [resetting, setResetting] = useState(false)
  const [password, setPassword] = useState('')

  const loadDetails = useCallback(async () => {
    const [h, tr] = await Promise.all([gpayApi.transactions(token), gpayApi.trips(token)])
    setHistory(h.data || []); setTrips(tr.data || [])
  }, [token])
  const load = useCallback(async () => {
    if (!signedIn) return
    setStatus('loading')
    try {
      const data = await gpayApi.balance(token)
      setAccount(data.data); setStatus('ready'); setError('')
      await loadDetails()
    } catch (e) {
      if (/not found/i.test(e?.message || '')) { setAccount(null); setStatus('none'); setError('') }
      else { setStatus('error'); setError(errorText(e)) }
    }
  }, [signedIn, token, loadDetails])
  useEffect(() => { load() }, [load])

  const run = async (key, action) => {
    if (busy) return
    setBusy(key); setError('')
    try { await action() } catch (e) { setError(errorText(e)) } finally { setBusy('') }
  }
  const create = () => run('create', async () => {
    const data = await gpayApi.register(token)
    setShownPin(data.data?.displayCode || '')
    setAccount(data.data); setStatus('ready')
    notify(t('GPay account created.'))
    await loadDetails()
  })
  const unlock = (event) => {
    event.preventDefault()
    if (!/^\d{4}$/.test(pinInput)) { setError(t('PIN must be 4 digits.')); return }
    // The typed PIN is cleared either way, so a wrong one can simply be typed again.
    run('unlock', async () => { const typed = pinInput; setPinInput(''); await gpayApi.unlock(typed, token); setPin(typed) })
  }
  const resetPin = (event) => {
    event.preventDefault()
    run('reset', async () => {
      const data = await gpayApi.resetPin(password, token)
      setShownPin(data.data?.displayCode || ''); setPassword(''); setResetting(false); setPin('')
    })
  }
  const recharge = (event) => {
    event.preventDefault()
    const problem = amountProblem(amount, account.balance)
    setAmountError(problem)
    if (problem) return
    if (!window.confirm(t('Move {amount} from GPay to your commute wallet?', { amount: money(Number(amount)) }))) return
    run('recharge', async () => {
      const data = await gpayApi.recharge(Number(amount), pin, token)
      setAccount(data.data); setAmount('')
      notify(t('{amount} added to your wallet.', { amount: money(-data.transaction.amount) }))
      await loadDetails()
    })
  }
  const payTrip = (trip) => {
    if (!window.confirm(t('Pay {fare} with GPay for {route}?', { fare: money(trip.fare), route: `${trip.source} → ${trip.destination}` }))) return
    run(`trip:${trip.tripId}`, async () => {
      const data = await gpayApi.payTrip(trip.tripId, pin, token)
      setAccount(data.data)
      notify(t('Paid {fare} for your trip.', { fare: money(trip.fare) }))
      await loadDetails()
    })
  }

  const heading = <Heading icon={Wallet} eyebrow={t('YOUR DIGITAL WALLET')} title={t('GPay')} subtitle={t('An in-app balance for paying trip fares and topping up your commute wallet.')} />
  if (!signedIn) return <section className="extra-page">{heading}<RequireSignIn openAuth={openAuth} /></section>
  if (status === 'loading') return <section className="extra-page gpay-page">{heading}<Loading label="Loading GPay…" /></section>
  if (status === 'error') return <section className="extra-page gpay-page">{heading}<Feedback error={error} /><button type="button" className="extra-button" onClick={load}><RefreshCw size={14} /> {t('Try again')}</button></section>

  const unlocked = Boolean(pin)
  return <section className="extra-page gpay-page">
    {heading}
    <p className="gpay-notice-banner">{t('GPay is a SmartCommute demo balance, not a bank or mobile-money account. No real money is charged or added.')}</p>
    <Feedback error={error} />
    {shownPin && <NewPin pin={shownPin} onDone={() => setShownPin('')} />}

    {status === 'none' ? <article className="extra-panel gpay-start">
      <h2>{t('Open your GPay account')}</h2>
      <p>{t('Your account starts with a demo balance and a 4-digit PIN that protects every payment.')}</p>
      <button type="button" className="extra-button" onClick={create} disabled={busy === 'create'}>{busy === 'create' ? t('Creating…') : t('Create GPay account')}</button>
    </article> : <>
      <div className="gpay-layout">
        <article className="extra-panel gpay-card">
          <small>{t('GPAY BALANCE')}</small>
          <strong data-testid="gpay-balance">{money(account?.balance)}</strong>
          <span>{unlocked ? t('Unlocked') : t('Locked: enter your PIN to move money')}</span>
          {unlocked
            ? <div className="pin-row"><button type="button" className="extra-button secondary" onClick={() => setPin('')}><Lock size={14} /> {t('Lock')}</button></div>
            : <form className="pin-row" onSubmit={unlock}>
              <input aria-label={t('GPay PIN')} type="password" inputMode="numeric" autoComplete="off" maxLength={4} value={pinInput} onChange={(e) => setPinInput(e.target.value.replace(/\D/g, ''))} placeholder={t('4-digit PIN')} />
              <button type="submit" className="extra-button" disabled={busy === 'unlock'}>{busy === 'unlock' ? t('Checking…') : t('Unlock')}</button>
              <button type="button" className="extra-button secondary" onClick={() => setResetting(!resetting)}>{t('Forgot PIN?')}</button>
            </form>}
          {resetting && !unlocked && <form className="pin-row" onSubmit={resetPin}>
            <input aria-label={t('Account password')} type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} placeholder={t('Your account password')} />
            <button type="submit" className="extra-button" disabled={busy === 'reset' || !password}>{busy === 'reset' ? t('Checking…') : t('Get a new PIN')}</button>
          </form>}
        </article>
        <article className="extra-panel gpay-actions">
          <h2>{t('Top up your commute wallet')}</h2>
          <p>{t('Move money from GPay into the wallet you use on SmartCommute.')}</p>
          <form onSubmit={recharge} noValidate>
            <label htmlFor="gpay-amount">{t('Amount (৳)')}</label>
            <input id="gpay-amount" inputMode="decimal" value={amount} disabled={!unlocked} onChange={(e) => { setAmount(e.target.value); setAmountError('') }} aria-invalid={Boolean(amountError) || undefined} />
            {amountError && <small className="offer-error" role="alert">{amountError}</small>}
            <button type="submit" className="extra-button" disabled={!unlocked || busy === 'recharge'}>{busy === 'recharge' ? t('Moving…') : t('Move to wallet')}</button>
          </form>
          {!unlocked && <p className="extra-help">{t('Unlock GPay with your PIN first.')}</p>}
        </article>
      </div>

      <article className="extra-panel extra-list-panel">
        <div className="extra-panel-title"><div><small>{t('PAY A TRIP')}</small><h2>{t('Completed trips')}</h2></div></div>
        {trips.length ? <div className="offer-trips">{trips.map((trip) => <div className="offer-trip" key={trip.tripId} data-testid="gpay-trip">
          <div><b>{trip.source} → {trip.destination}</b><small>{trip.busName}{trip.endedAt ? ` · ${formatDate(trip.endedAt)}` : ''} · {t('Fare {fare}', { fare: money(trip.fare) })}</small></div>
          {trip.paid
            ? <span className="offer-status">{t('Paid')}</span>
            : <button type="button" className="extra-button" disabled={!unlocked || Boolean(busy)} onClick={() => payTrip(trip)} aria-label={t('Pay {fare} for {route}', { fare: money(trip.fare), route: `${trip.source} → ${trip.destination}` })}>
              {busy === `trip:${trip.tripId}` ? t('Paying…') : <><Bus size={14} /> {t('Pay {fare}', { fare: money(trip.fare) })}</>}
            </button>}
        </div>)}</div> : <p className="extra-empty-copy">{t('No completed trips to pay yet. Finish a bus trip from Find a bus, then pay its fare here.')}</p>}
      </article>

      <article className="extra-panel extra-list-panel">
        <div className="extra-panel-title"><div><small>{t('HISTORY')}</small><h2>{t('GPay transactions')}</h2></div><button type="button" className="extra-icon-button" onClick={load} aria-label={t('Refresh')}><RefreshCw size={15} /></button></div>
        {history.length ? <ul className="offer-history">{history.map((x) => <li key={x._id} data-testid="gpay-transaction">
          <span>{x.amount < 0 ? <ArrowUpRight size={15} aria-hidden="true" /> : <ArrowDownLeft size={15} aria-hidden="true" />}<span><b>{t(TYPE_LABELS[x.type] || x.type)}</b><small>{x.description} · {formatDateTime(x.createdAt)} · {t('Completed')}</small></span></span>
          <strong className={x.amount < 0 ? 'out' : 'in'}>{x.amount < 0 ? '−' : '+'}{money(Math.abs(x.amount))}</strong>
        </li>)}</ul> : <p className="extra-empty-copy">{t('Nothing yet.')}</p>}
      </article>
    </>}
  </section>
}
