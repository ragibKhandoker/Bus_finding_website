import React, { useCallback, useState } from 'react'
import { ArrowDownLeft, ArrowUpRight, BusFront, Check, CircleAlert, CreditCard, History, Sparkles, Wallet } from 'lucide-react'
import { useTripChanges } from '../../controllers/useTrips'
import { apiRequest as request } from '../../models/api'
import { formatDateTime, formatNumber, t } from '../../models/i18n'
import { money, signedGems, signedMoney } from '../helpers'
import { errorText } from '../../models/messages'
import { PageHeading, SignInPrompt } from '../components/common'

// How each ledger type is shown. Anything else (e.g. a type added later) falls back to the raw type.
export const TRANSACTION_TYPES = {
  'fare': 'Fare payment',
  'recharge': 'Recharge from GPay',
  'gems-earned': 'Gems earned',
  'gems-converted': 'Gems converted to taka',
  'cashback': 'Cashback',
  'coupon': 'Coupon',
}

export const TRANSACTIONS_PER_PAGE = 10

// The signed-in user's wallet: balances, gems-to-taka conversion and the ledger. Every number comes from the
// server; after a change the page loads the balances again instead of working them out itself.
export function WalletPage({ token, signedIn, openAuth, notify }) {
  const [wallet, setWallet] = useState(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(signedIn)
  const [history, setHistory] = useState({ items: [], page: 0, totalPages: 0, total: 0, status: 'idle' })
  const [gemsInput, setGemsInput] = useState('')
  const [converting, setConverting] = useState(false)
  const [convertMessage, setConvertMessage] = useState({ error: '', success: '' })

  const loadBalance = useCallback(async () => {
    if (!signedIn) return
    setLoading(true); setError('')
    try { setWallet(await request('/wallet/balance', { token })) }
    catch (e) { setError(errorText(e)) }
    finally { setLoading(false) }
  }, [signedIn, token])
  // page 1 replaces the list; later pages are added below it.
  const loadHistory = useCallback(async (page = 1) => {
    if (!signedIn) return
    setHistory((current) => ({ ...current, status: 'loading' }))
    try {
      const data = await request(`/wallet/transactions?${new URLSearchParams({ page, pageSize: TRANSACTIONS_PER_PAGE })}`, { token })
      setHistory((current) => ({ items: page === 1 ? data.data : [...current.items, ...data.data], page: data.page, totalPages: data.totalPages, total: data.total, status: 'ready' }))
    } catch (e) { setHistory((current) => ({ ...current, status: errorText(e) })) }
  }, [signedIn, token])
  const load = useCallback(() => { loadBalance(); loadHistory(1) }, [loadBalance, loadHistory])
  useTripChanges(load)

  const gems = wallet?.gemsBalance ?? 0
  const rate = wallet?.conversion?.gemsPerTaka ?? 0
  // The gems field starts empty, meaning "convert as many as possible".
  const wanted = gemsInput.trim() === '' ? gems : Number(gemsInput)
  const inputProblem = gemsInput.trim() === '' ? ''
    : !Number.isInteger(wanted) || wanted <= 0 ? t('Enter a whole number of gems greater than zero.')
    : wanted > gems ? t('You only have {gems} gems.', { gems })
    : ''
  const blocks = rate > 0 && !inputProblem ? Math.floor(wanted / rate) : 0
  const preview = { taka: blocks, used: blocks * rate, left: gems - blocks * rate }
  const canConvert = !converting && rate > 0 && !inputProblem && blocks > 0

  const convert = async (event) => {
    event.preventDefault()
    if (!canConvert) return
    setConverting(true); setConvertMessage({ error: '', success: '' })
    try {
      const body = gemsInput.trim() === '' ? {} : { gems: wanted }
      const data = await request('/wallet/convert-gems', { token, method: 'POST', body: JSON.stringify(body) })
      const success = t('Converted {gems} gems to ৳{taka}. {left} gems left.', { gems: data.gemsUsed, taka: data.convertedAmount, left: data.gemsRemaining })
      setConvertMessage({ error: '', success }); setGemsInput(''); notify?.(success)
      load()
    } catch (e) { setConvertMessage({ error: errorText(e), success: '' }) }
    finally { setConverting(false) }
  }

  const heading = <PageHeading eyebrow={t('YOUR COMMUTE COMPANION')} title={t('My wallet.')} description={t('Your taka balance, your gems, and every change to them.')} icon={Wallet} />
  if (!signedIn) return <div className="page inner-page">{heading}<SignInPrompt openAuth={openAuth} message={t('Sign in to check your SmartCommute wallet.')} /></div>
  if (loading && !wallet) return <div className="page inner-page">{heading}<div className="empty-state"><span className="spinner" /> {t('Checking your balance…')}</div></div>
  if (error && !wallet) return <div className="page inner-page">{heading}<div className="empty-state panel"><span className="empty-icon"><Wallet size={22} /></span><b>{t('Wallet unavailable')}</b><span>{error}</span><button className="button button-outline button-small" onClick={load}>{t('Try again')}</button></div></div>

  return <div className="page inner-page">{heading}
    {error && <div className="wallet-inline-error"><CircleAlert size={15} /> {error}</div>}
    <section className="wallet-overview">
      <div className="wallet-card-large"><div className="wallet-card-head"><span className="wallet-mark"><BusFront size={18} /></span><span>{t('SMARTCOMMUTE WALLET')}</span><CreditCard size={20} /></div><div className="balance-caption">{t('AVAILABLE BALANCE')}</div><div className="balance-amount">{money(wallet.takaBalance)}</div><div className="wallet-card-foot"><span>{t('BDT · Bangladeshi Taka')}</span><span>{t('Updated {time}', { time: formatDateTime(wallet.wallet?.updatedAt) })}</span></div><div className="wallet-card-orb" /></div>
      <div className="wallet-note panel wallet-gems"><span className="service-icon mint"><Sparkles size={19} /></span><small>{t('GEMS')}</small><b className="wallet-gems-count">{formatNumber(gems)}</b><p>{rate > 0 ? t('{rate} gems = ৳1. You earn gems for every trip you complete.', { rate }) : t('You earn gems for every trip you complete. End a trip at your stop to collect them.')}</p></div>
    </section>

    <section className="panel wallet-convert">
      <div className="wallet-section-head"><div><div className="eyebrow">{t('GEMS TO TAKA')}</div><h2>{t('Convert your gems')}</h2></div>{rate > 0 && <span className="wallet-rate">{t('{rate} gems = ৳1', { rate })}</span>}</div>
      {rate <= 0 ? <p className="wallet-muted">{t('Gem conversion is not available right now.')}</p> : <form className="wallet-convert-form" onSubmit={convert} noValidate>
        <label htmlFor="convert-gems">{t('Gems to convert')}</label>
        <div className="wallet-convert-row">
          <input id="convert-gems" type="number" inputMode="numeric" min={rate} max={gems} step={rate} value={gemsInput} placeholder={t('All {gems} gems', { gems })} onChange={(e) => { setGemsInput(e.target.value); setConvertMessage({ error: '', success: '' }) }} disabled={converting} />
          <button className="button button-green" disabled={!canConvert}>{converting ? <><span className="button-spinner" /> {t('Converting…')}</> : t('Convert')}</button>
        </div>
        {inputProblem ? <p className="wallet-field-error">{inputProblem}</p>
          : blocks > 0 ? <p className="wallet-preview">{t('You get ৳{taka} for {used} gems. {left} gems stay in your wallet.', { taka: preview.taka, used: preview.used, left: preview.left })}</p>
          : <p className="wallet-muted">{t('You need at least {rate} gems to convert. Only whole blocks of {rate} gems are converted; the rest stay as gems.', { rate })}</p>}
        {convertMessage.error && <p className="wallet-field-error">{convertMessage.error}</p>}
        {convertMessage.success && <p className="wallet-success"><Check size={14} /> {convertMessage.success}</p>}
      </form>}
    </section>

    <section className="panel wallet-history">
      <div className="wallet-section-head"><div><div className="eyebrow">{t('TRANSACTION HISTORY')}</div><h2>{t('Every change to your wallet')}</h2></div>{history.total > 0 && <span className="result-count">{history.total === 1 ? t('1 transaction') : t('{count} transactions', { count: history.total })}</span>}</div>
      {history.status === 'loading' && !history.items.length ? <div className="empty-state"><span className="spinner" /> {t('Loading transactions…')}</div>
        : history.status !== 'ready' && history.status !== 'loading' && !history.items.length ? <div className="empty-state"><span className="empty-icon"><CircleAlert size={22} /></span><b>{t('Transactions could not be loaded')}</b><span>{history.status}</span><button className="button button-outline button-small" onClick={() => loadHistory(1)}>{t('Try again')}</button></div>
        : !history.items.length ? <div className="empty-state"><span className="empty-icon"><History size={22} /></span><b>{t('No transactions yet')}</b><span>{t('Complete a trip to earn gems, or recharge from GPay. Each change will be listed here.')}</span></div>
        : <><ul className="wallet-ledger">{history.items.map((item) => {
            const credit = item.amount > 0 || (item.amount === 0 && item.gemsChange > 0)
            return <li key={item._id} className={credit ? 'credit' : 'debit'}>
              <span className="wallet-ledger-icon">{item.amount === 0 ? <Sparkles size={16} /> : credit ? <ArrowDownLeft size={16} /> : <ArrowUpRight size={16} />}</span>
              <div className="wallet-ledger-main"><b>{t(TRANSACTION_TYPES[item.type] || item.type)}</b><span>{t(item.description)}</span><small>{formatDateTime(item.createdAt)}</small></div>
              <div className="wallet-ledger-amounts">
                {item.amount !== 0 && <b className={item.amount > 0 ? 'plus' : 'minus'}>{signedMoney(item.amount)}</b>}
                {item.gemsChange !== 0 && <span className={item.gemsChange > 0 ? 'plus' : 'minus'}>{t('{gems} gems', { gems: signedGems(item.gemsChange) })}</span>}
                {item.balanceAfter != null && <small>{t('Balance after: {balance} · {gems} gems', { balance: money(item.balanceAfter), gems: formatNumber(item.gemsAfter) })}</small>}
              </div>
            </li>
          })}</ul>
          {history.status !== 'ready' && history.status !== 'loading' && <p className="wallet-field-error">{history.status}</p>}
          {history.page < history.totalPages && <button className="button button-outline button-small wallet-more" disabled={history.status === 'loading'} onClick={() => loadHistory(history.page + 1)}>{history.status === 'loading' ? t('Loading…') : t('Show older transactions')}</button>}</>}
    </section>
  </div>
}
