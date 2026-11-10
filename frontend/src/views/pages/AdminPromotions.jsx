import React, { useCallback, useEffect, useState } from 'react'
import { AlertTriangle, BadgePercent, Ban, Check, CircleAlert, Gift, History, Loader2, Megaphone, Pause, Pencil, Play, Plus, RefreshCw, Save, Search, Send, Settings2, ShieldAlert, Square, Trash2, Users, X } from 'lucide-react'
import { formatDate, formatNumber, t } from '../../models/i18n'
import { CAMPAIGN_TYPE_LABELS, COUPON_TYPE_LABELS, REFERRAL_STATUS_LABELS, adminPromoApi, money, shortDay } from '../../models/promotions'
import '../styles/admin-offers.css'

// The admin's Offers & rewards section (F15): statistics and unusual activity, coupons, campaigns, the reward rules
// and tiers, redemptions, referrals and the audit log. The server checks every value again and records every change.

const errorText = (error) => t(error?.message || 'Something went wrong. Please try again.')

// Dhaka is UTC+6 all year: "2026-12-01T10:30" in Dhaka time, for <input type="datetime-local">.
export const toDhakaInput = (iso) => (iso ? new Date(new Date(iso).getTime() + 6 * 3_600_000).toISOString().slice(0, 16) : '')
const dhakaDateTime = (iso) => (iso ? formatDate(iso, { day: 'numeric', month: 'short', year: 'numeric', hour: 'numeric', minute: '2-digit' }) : '—')

const TABS = [
  ['overview', 'Overview', BadgePercent], ['coupons', 'Coupons', Gift], ['campaigns', 'Campaigns', Megaphone], ['rules', 'Rules & tiers', Settings2],
  ['redemptions', 'Redemptions', History], ['referrals', 'Referrals', Users], ['audit', 'Audit log', ShieldAlert],
]

function useLoad(loader) {
  const [state, setState] = useState({ status: 'loading', data: null, error: '' })
  const load = useCallback(async () => {
    setState((current) => ({ ...current, status: current.data ? 'ready' : 'loading' }))
    try { setState({ status: 'ready', data: await loader(), error: '' }) } catch (e) { setState((current) => ({ ...current, status: 'error', error: errorText(e) })) }
  }, [loader])
  useEffect(() => { load() }, [load])
  return [state, load]
}

const Problem = ({ text }) => (text ? <p className="ao-error" role="alert"><CircleAlert size={15} /> {text}</p> : null)
const Busy = () => <div className="ao-busy"><Loader2 size={18} className="ao-spin" /> {t('Loading…')}</div>

function Field({ label, hint, children, wide = false }) {
  return <label className={`ao-field${wide ? ' wide' : ''}`}><span>{label}</span>{children}{hint && <small>{hint}</small>}</label>
}

function Toggle({ label, checked, onChange, hint }) {
  return <label className="ao-toggle"><input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} /><span><b>{label}</b>{hint && <small>{hint}</small>}</span></label>
}

// ---- Overview ----

function Overview({ token, notify }) {
  const [stats, reloadStats] = useLoad(useCallback(() => adminPromoApi.statistics(token), [token]))
  const [flags, reloadFlags] = useLoad(useCallback(() => adminPromoApi.suspicious(token), [token]))
  const [tripId, setTripId] = useState('')
  const [processing, setProcessing] = useState(false)
  const [result, setResult] = useState(null)
  const process = async (event) => {
    event.preventDefault()
    if (!/^[0-9a-f-]{36}$/i.test(tripId.trim())) { setResult({ error: t('Enter the trip id (a 36-character id).') }); return }
    setProcessing(true); setResult(null)
    try { const data = await adminPromoApi.processTrip(tripId.trim(), token); setResult({ ok: t(data.message) }); reloadStats() } catch (e) { setResult({ error: errorText(e) }) } finally { setProcessing(false) }
  }
  const s = stats.data?.data
  const totals = s?.totals
  const peak = Math.max(1, ...(s?.days || []).map((d) => d.discount + d.cashback))
  return <div className="ao-grid">
    <section className="ao-card wide">
      <header className="ao-head"><h3>{t('Rewards at a glance')}</h3><button type="button" className="ao-icon" onClick={() => { reloadStats(); reloadFlags() }} aria-label={t('Refresh')}><RefreshCw size={15} /></button></header>
      <Problem text={stats.error} />
      {!totals ? <Busy /> : <div className="ao-kpis">
        {[
          ['Active coupons', `${formatNumber(totals.activeCoupons)} / ${formatNumber(totals.coupons)}`],
          ['Coupon redemptions', formatNumber(totals.redemptions)],
          ['Discount given', money(totals.discountGiven)],
          ['Cashback credited', money(totals.cashbackCredited)],
          ['Moved to wallets', money(totals.cashbackMovedToWallet)],
          ['Cashback expired', money(totals.cashbackExpired)],
          ['Cashback unspent', money(totals.cashbackOutstanding)],
          ['Eligible trips', formatNumber(totals.eligibleTrips)],
          ['Live campaigns', formatNumber(totals.liveCampaigns)],
          ['Referrals rewarded', `${formatNumber(totals.referralsRewarded)} · ${t('{count} pending', { count: totals.referralsPending })}`],
        ].map(([label, value]) => <div key={label} className="ao-kpi"><small>{t(label)}</small><b>{value}</b></div>)}
      </div>}
      {totals && <p className="ao-split">{t('Cashback by source')}: {t('trips')} {money(totals.cashbackFromTrips)} · {t('first trips')} {money(totals.cashbackFirstTrip)} · {t('referrals')} {money(totals.cashbackReferral)}</p>}
    </section>

    <section className="ao-card wide">
      <header className="ao-head"><h3>{t('Last 14 days')}</h3><span className="ao-legend"><i className="c" /> {t('Cashback')} <i className="d" /> {t('Coupon discounts')}</span></header>
      {!s ? <Busy /> : <div className="ao-days">{s.days.map((d) => <div key={d.date} title={`${d.date}: ${money(d.cashback)} ${t('cashback')}, ${money(d.discount)} ${t('discounts')}, ${d.redemptions} ${t('redemptions')}`}>
        <span style={{ height: `${((d.cashback + d.discount) / peak) * 100}%` }}><i className="d" style={{ flex: d.discount || 0 }} /><i className="c" style={{ flex: d.cashback || 0 }} /></span>
        <small>{d.date.slice(8)}</small></div>)}</div>}
    </section>

    <section className="ao-card">
      <header className="ao-head"><h3>{t('Top coupons')}</h3></header>
      {!s ? <Busy /> : !s.topCoupons.length ? <p className="ao-muted">{t('No redemptions yet.')}</p>
        : <ol className="ao-list">{s.topCoupons.map((c) => <li key={c.code}><b className="ao-mono">{c.code}</b><span>{t('{count} uses', { count: c.redemptions })} · {money(c.discount)}</span></li>)}</ol>}
      <header className="ao-head ao-gap"><h3>{t('Members by tier')}</h3></header>
      {s && <ol className="ao-list">{s.membership.map((m) => <li key={m.tier}><b>{t(m.name)}</b><span>{formatNumber(m.count)}</span></li>)}</ol>}
    </section>

    <section className="ao-card">
      <header className="ao-head"><h3><AlertTriangle size={16} /> {t('Unusual activity')}</h3></header>
      <Problem text={flags.error} />
      {flags.status === 'loading' ? <Busy /> : !(flags.data?.data || []).length ? <p className="ao-ok"><Check size={15} /> {t('Nothing unusual in the last 24 hours.')}</p>
        : <ul className="ao-flags">{flags.data.data.map((f, i) => <li key={`${f.kind}-${f.userId}-${i}`}><b>{f.name}</b><small>{f.email}</small><span>{t(f.detail)}</span></li>)}</ul>}
      <p className="ao-muted">{t('Nothing is blocked automatically. Look into the account, and reject referrals or switch coupons off if needed.')}</p>
    </section>

    <section className="ao-card wide">
      <header className="ao-head"><h3>{t('Process a trip’s rewards')}</h3></header>
      <p className="ao-muted">{t('Runs the reward engine for one completed trip, for example if it was missed. The cashback is worked out from the trip the server recorded; running it again for the same trip does nothing.')}</p>
      <form className="ao-inline" onSubmit={process}>
        <input value={tripId} onChange={(e) => setTripId(e.target.value)} placeholder={t('Trip id')} aria-label={t('Trip id')} />
        <button type="submit" className="button button-green" disabled={processing}>{processing ? <Loader2 size={15} className="ao-spin" /> : <Send size={15} />} {t('Process')}</button>
      </form>
      {result?.ok && <p className="ao-ok"><Check size={15} /> {result.ok}</p>}
      <Problem text={result?.error} />
    </section>
  </div>
}

// ---- Coupons ----

const EMPTY_COUPON = {
  code: '', title: '', description: '', type: 'percentage', discountType: 'percent', discountValue: '10', minFare: '0', maxDiscount: '',
  startsAt: '', expiresAt: '', totalLimit: '', perUserLimit: '1', campaignId: '', isActive: true, audience: 'all', minTier: '', routeNumber: '',
  weekendOnly: false, validDaysAfterIssue: '',
}

const numberOrNull = (text) => (String(text).trim() === '' ? null : Number(text))

export function couponBody(form) {
  return {
    code: form.code.trim().toUpperCase(), title: form.title.trim(), description: form.description.trim(), type: form.type, discountType: form.discountType,
    discountValue: Number(form.discountValue), minFare: Number(form.minFare || 0), maxDiscount: numberOrNull(form.maxDiscount),
    startsAt: form.startsAt || null, expiresAt: form.expiresAt || null, totalLimit: numberOrNull(form.totalLimit), perUserLimit: Number(form.perUserLimit || 1),
    campaignId: form.campaignId || null, isActive: form.isActive, audience: form.audience, minTier: form.minTier || null, routeNumber: form.routeNumber.trim() || null,
    weekendOnly: form.weekendOnly, validDaysAfterIssue: numberOrNull(form.validDaysAfterIssue),
  }
}

export function couponFormProblem(form) {
  if (!/^[A-Z0-9_-]{3,30}$/.test(form.code.trim().toUpperCase())) return t('The code must be 3 to 30 letters, numbers, - or _ (for example DHAKA10).')
  if (form.title.trim().length < 3) return t('Enter a title of at least 3 characters.')
  const value = Number(form.discountValue)
  if (!(value > 0)) return t('The discount must be more than zero.')
  if (form.discountType === 'percent' && value > 100) return t('A percentage discount cannot be more than 100%.')
  if (form.expiresAt && form.startsAt && form.expiresAt <= form.startsAt) return t('The expiry must be after the start.')
  return ''
}

function CouponForm({ initial, campaigns, tiers, onCancel, onSave }) {
  const [form, setForm] = useState(initial)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const set = (key) => (e) => setForm({ ...form, [key]: e.target.value })
  const issued = ['welcome', 'first-trip', 'referral'].includes(form.type)
  const submit = async (event) => {
    event.preventDefault()
    const problem = couponFormProblem(form)
    if (problem) { setError(problem); return }
    setBusy(true); setError('')
    try { await onSave(couponBody(form)) } catch (e) { setError(errorText(e)) } finally { setBusy(false) }
  }
  return <form className="ao-form" onSubmit={submit} noValidate>
    <div className="ao-fields">
      <Field label={t('Code')}><input value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value.toUpperCase() })} placeholder="DHAKA10" maxLength={30} className="ao-mono" /></Field>
      <Field label={t('Title')}><input value={form.title} onChange={set('title')} maxLength={100} placeholder={t('10% off your bus fare')} /></Field>
      <Field label={t('Description')} wide><input value={form.description} onChange={set('description')} maxLength={500} /></Field>
      <Field label={t('Coupon type')}><select value={form.type} onChange={set('type')}>{Object.entries(COUPON_TYPE_LABELS).map(([value, label]) => <option key={value} value={value}>{t(label)}</option>)}</select></Field>
      <Field label={t('Discount type')}><select value={form.discountType} onChange={set('discountType')}><option value="percent">{t('Percentage')}</option><option value="fixed">{t('Fixed amount (৳)')}</option></select></Field>
      <Field label={form.discountType === 'percent' ? t('Discount (%)') : t('Discount (৳)')}><input inputMode="decimal" value={form.discountValue} onChange={set('discountValue')} /></Field>
      <Field label={t('Minimum fare (৳)')}><input inputMode="decimal" value={form.minFare} onChange={set('minFare')} /></Field>
      <Field label={t('Maximum discount (৳)')} hint={t('Empty: no maximum')}><input inputMode="decimal" value={form.maxDiscount} onChange={set('maxDiscount')} /></Field>
      <Field label={t('Starts (Dhaka time)')} hint={t('Empty: now')}><input type="datetime-local" value={form.startsAt} onChange={set('startsAt')} /></Field>
      <Field label={t('Expires (Dhaka time)')} hint={t('Empty: never')}><input type="datetime-local" value={form.expiresAt} onChange={set('expiresAt')} /></Field>
      <Field label={t('Total redemptions')} hint={t('Empty: no limit')}><input inputMode="numeric" value={form.totalLimit} onChange={set('totalLimit')} /></Field>
      <Field label={t('Per user')}><input inputMode="numeric" value={form.perUserLimit} onChange={set('perUserLimit')} /></Field>
      <Field label={t('Who can use it')} hint={issued ? t('Welcome, first-trip and referral coupons are handed out automatically.') : ''}>
        <select value={issued ? 'assigned' : form.audience} onChange={set('audience')} disabled={issued}>
          <option value="all">{t('Everyone')}</option><option value="new-users">{t('New users')}</option><option value="assigned">{t('Only users it is issued to')}</option></select></Field>
      <Field label={t('Minimum tier')}><select value={form.minTier} onChange={set('minTier')}><option value="">{t('Any tier')}</option>{tiers.map((tier) => <option key={tier.code} value={tier.code}>{t(tier.name)}</option>)}</select></Field>
      <Field label={t('Campaign')}><select value={form.campaignId} onChange={set('campaignId')}><option value="">{t('None')}</option>{campaigns.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}</select></Field>
      <Field label={t('Route number')} hint={t('Empty: every Dhaka route')}><input value={form.routeNumber} onChange={set('routeNumber')} maxLength={40} placeholder="8" /></Field>
      <Field label={t('Days valid after issue')} hint={t('For coupons handed to users')}><input inputMode="numeric" value={form.validDaysAfterIssue} onChange={set('validDaysAfterIssue')} /></Field>
    </div>
    <div className="ao-toggles">
      <Toggle label={t('Active')} checked={form.isActive} onChange={(isActive) => setForm({ ...form, isActive })} />
      <Toggle label={t('Friday and Saturday trips only')} checked={form.weekendOnly || form.type === 'weekend'} onChange={(weekendOnly) => setForm({ ...form, weekendOnly })} />
    </div>
    <Problem text={error} />
    <div className="ao-actions"><button type="button" className="button button-outline" onClick={onCancel}>{t('Cancel')}</button>
      <button type="submit" className="button button-green" disabled={busy}>{busy ? <Loader2 size={15} className="ao-spin" /> : <Save size={15} />} {t('Save coupon')}</button></div>
  </form>
}

function Coupons({ token, notify }) {
  const [state, reload] = useLoad(useCallback(() => adminPromoApi.coupons(token), [token]))
  const [campaigns] = useLoad(useCallback(() => adminPromoApi.campaigns(token), [token]))
  const [tiers] = useLoad(useCallback(() => adminPromoApi.tiers(token), [token]))
  const [editing, setEditing] = useState(null) // null | 'new' | coupon
  const [issuing, setIssuing] = useState(null)
  const [email, setEmail] = useState('')
  const [message, setMessage] = useState(null)
  const [confirmDelete, setConfirmDelete] = useState(null)
  const list = state.data?.data || []

  const save = async (body) => {
    const result = editing === 'new' ? await adminPromoApi.createCoupon(body, token) : await adminPromoApi.updateCoupon(editing.id, body, token)
    notify(t(result.message)); setEditing(null); reload()
  }
  const toggle = async (c) => {
    try {
      const result = await adminPromoApi.updateCoupon(c.id, couponBody({ ...formFrom(c), isActive: !c.isActive }), token)
      notify(t(result.message)); reload()
    } catch (e) { notify(errorText(e), 'error') }
  }
  const remove = async (c) => {
    try { const result = await adminPromoApi.deleteCoupon(c.id, token); notify(t(result.message)); setConfirmDelete(null); reload() } catch (e) { notify(errorText(e), 'error') }
  }
  const issue = async (event) => {
    event.preventDefault()
    try { const result = await adminPromoApi.issueCoupon(issuing.id, email.trim(), token); setMessage({ ok: t(result.message) }); setEmail('') } catch (e) { setMessage({ error: errorText(e) }) }
  }

  if (editing) {
    return <section className="ao-card">
      <header className="ao-head"><h3>{editing === 'new' ? t('New coupon') : t('Edit {code}', { code: editing.code })}</h3></header>
      <CouponForm initial={editing === 'new' ? EMPTY_COUPON : formFrom(editing)} campaigns={campaigns.data?.data || []} tiers={tiers.data?.data || []} onCancel={() => setEditing(null)} onSave={save} />
    </section>
  }
  return <section className="ao-card">
    <header className="ao-head"><h3>{t('Coupons')} <span className="ao-count">{formatNumber(list.length)}</span></h3>
      <button type="button" className="button button-green button-small" onClick={() => setEditing('new')}><Plus size={14} /> {t('New coupon')}</button></header>
    <Problem text={state.error} />
    {state.status === 'loading' ? <Busy /> : !list.length ? <p className="ao-muted">{t('No coupons yet. Create the first one.')}</p>
      : <div className="ao-table-wrap"><table className="ao-table">
        <thead><tr><th>{t('Code')}</th><th>{t('Saving')}</th><th>{t('Rules')}</th><th>{t('Valid')}</th><th>{t('Used')}</th><th>{t('Status')}</th><th><span className="sr-only">{t('Actions')}</span></th></tr></thead>
        <tbody>{list.map((c) => <tr key={c.id}>
          <td><b className="ao-mono">{c.code}</b><small>{c.title} · {t(COUPON_TYPE_LABELS[c.type] || c.type)}</small></td>
          <td>{t(c.saving)}</td>
          <td><small>{c.minFare > 0 ? t('Min. {amount}', { amount: money(c.minFare) }) : t('No minimum')}{c.minTier ? ` · ${c.minTier}` : ''}{c.weekendOnly ? ` · ${t('weekend')}` : ''}{c.routeNumber ? ` · ${t('route {number}', { number: c.routeNumber })}` : ''} · {c.audience}</small></td>
          <td><small>{shortDay(c.startsAt)} → {c.expiresAt ? shortDay(c.expiresAt) : t('no expiry')}</small></td>
          <td>{formatNumber(c.redemptionCount)}{c.totalLimit ? ` / ${formatNumber(c.totalLimit)}` : ''}<small>{t('{count} per user', { count: c.perUserLimit })}</small></td>
          <td><span className={`ao-status ${c.isActive ? (c.expiresAt && new Date(c.expiresAt) <= new Date() ? 'expired' : 'active') : 'paused'}`}>{c.isActive ? (c.expiresAt && new Date(c.expiresAt) <= new Date() ? t('Expired') : t('Active')) : t('Inactive')}</span></td>
          <td className="ao-row-actions">
            <button type="button" className="ao-icon" onClick={() => setEditing(c)} aria-label={t('Edit {code}', { code: c.code })}><Pencil size={14} /></button>
            <button type="button" className="ao-icon" onClick={() => toggle(c)} aria-label={c.isActive ? t('Deactivate {code}', { code: c.code }) : t('Activate {code}', { code: c.code })}>{c.isActive ? <Pause size={14} /> : <Play size={14} />}</button>
            <button type="button" className="ao-icon" onClick={() => { setIssuing(c); setMessage(null) }} aria-label={t('Give {code} to a user', { code: c.code })}><Send size={14} /></button>
            <button type="button" className="ao-icon danger" onClick={() => setConfirmDelete(c)} aria-label={t('Delete {code}', { code: c.code })}><Trash2 size={14} /></button>
          </td>
        </tr>)}</tbody>
      </table></div>}
    {confirmDelete && <div className="ao-confirm" role="alertdialog" aria-label={t('Delete coupon')}>
      <p>{t('Delete {code}? It can no longer be used; its redemptions stay in the history.', { code: confirmDelete.code })}</p>
      <div><button type="button" className="button button-outline button-small" onClick={() => setConfirmDelete(null)}>{t('Cancel')}</button>
        <button type="button" className="button button-small ao-danger" onClick={() => remove(confirmDelete)}><Trash2 size={14} /> {t('Delete')}</button></div>
    </div>}
    {issuing && <form className="ao-confirm" onSubmit={issue}>
      <p>{t('Give {code} to one user. They get a notification.', { code: issuing.code })}</p>
      <div className="ao-inline"><input type="email" value={email} onChange={(e) => setEmail(e.target.value)} placeholder="user@example.com" aria-label={t('User email')} />
        <button type="submit" className="button button-green button-small"><Send size={14} /> {t('Give coupon')}</button>
        <button type="button" className="ao-icon" onClick={() => setIssuing(null)} aria-label={t('Close')}><X size={14} /></button></div>
      {message?.ok && <p className="ao-ok"><Check size={15} /> {message.ok}</p>}
      <Problem text={message?.error} />
    </form>}
  </section>
}

function formFrom(c) {
  return {
    code: c.code, title: c.title, description: c.description || '', type: c.type, discountType: c.discountType, discountValue: String(c.discountValue),
    minFare: String(c.minFare), maxDiscount: c.maxDiscount ? String(c.maxDiscount) : '', startsAt: toDhakaInput(c.startsAt), expiresAt: toDhakaInput(c.expiresAt),
    totalLimit: c.totalLimit ? String(c.totalLimit) : '', perUserLimit: String(c.perUserLimit), campaignId: c.campaignId || '', isActive: c.isActive,
    audience: c.audience, minTier: c.minTier || '', routeNumber: c.routeNumber || '', weekendOnly: c.weekendOnly,
    validDaysAfterIssue: c.validDaysAfterIssue ? String(c.validDaysAfterIssue) : '',
  }
}

// ---- Campaigns ----

const THEMES = ['green', 'sunset', 'eid', 'night', 'sky', 'gold']

function emptyCampaign() {
  const start = toDhakaInput(new Date().toISOString())
  const end = toDhakaInput(new Date(Date.now() + 14 * 86_400_000).toISOString())
  return { name: '', description: '', type: 'cashback', theme: 'green', bannerUrl: '', startsAt: start, endsAt: end, cashbackPercent: '5', cashbackFixed: '0',
    maxCashbackPerTrip: '', minFare: '0', minTier: '', newUsersOnly: false, weekendOnly: false, firstTripOnly: false, budget: '', maxRedemptions: '' }
}

function campaignForm(c) {
  return { name: c.name, description: c.description || '', type: c.type, theme: c.theme || 'green', bannerUrl: c.bannerUrl || '', startsAt: toDhakaInput(c.startsAt),
    endsAt: toDhakaInput(c.endsAt), cashbackPercent: String(c.cashbackPercent), cashbackFixed: String(c.cashbackFixed), maxCashbackPerTrip: c.maxCashbackPerTrip ? String(c.maxCashbackPerTrip) : '',
    minFare: String(c.minFare), minTier: c.minTier || '', newUsersOnly: c.newUsersOnly, weekendOnly: c.weekendOnly, firstTripOnly: c.firstTripOnly,
    budget: c.budget ? String(c.budget) : '', maxRedemptions: c.maxRedemptions ? String(c.maxRedemptions) : '' }
}

function CampaignEditor({ initial, tiers, onCancel, onSave }) {
  const [form, setForm] = useState(initial)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const set = (key) => (e) => setForm({ ...form, [key]: e.target.value })
  const submit = async (event) => {
    event.preventDefault()
    if (form.name.trim().length < 3) { setError(t('Enter a name of at least 3 characters.')); return }
    if (!form.startsAt || !form.endsAt || form.endsAt <= form.startsAt) { setError(t('The campaign must end after it starts.')); return }
    setBusy(true); setError('')
    try {
      await onSave({ ...form, name: form.name.trim(), cashbackPercent: Number(form.cashbackPercent || 0), cashbackFixed: Number(form.cashbackFixed || 0),
        maxCashbackPerTrip: numberOrNull(form.maxCashbackPerTrip), minFare: Number(form.minFare || 0), minTier: form.minTier || null, bannerUrl: form.bannerUrl.trim() || null,
        budget: numberOrNull(form.budget), maxRedemptions: numberOrNull(form.maxRedemptions) })
    } catch (e) { setError(errorText(e)) } finally { setBusy(false) }
  }
  return <form className="ao-form" onSubmit={submit} noValidate>
    <div className="ao-fields">
      <Field label={t('Name')}><input value={form.name} onChange={set('name')} maxLength={100} placeholder={t('Eid Special Offers')} /></Field>
      <Field label={t('Campaign type')}><select value={form.type} onChange={set('type')}>{Object.entries(CAMPAIGN_TYPE_LABELS).map(([value, label]) => <option key={value} value={value}>{t(label)}</option>)}</select></Field>
      <Field label={t('Description')} wide><input value={form.description} onChange={set('description')} maxLength={500} /></Field>
      <Field label={t('Starts (Dhaka time)')}><input type="datetime-local" value={form.startsAt} onChange={set('startsAt')} /></Field>
      <Field label={t('Ends (Dhaka time)')}><input type="datetime-local" value={form.endsAt} onChange={set('endsAt')} /></Field>
      <Field label={t('Extra cashback (%)')}><input inputMode="decimal" value={form.cashbackPercent} onChange={set('cashbackPercent')} /></Field>
      <Field label={t('Extra cashback (৳ per trip)')}><input inputMode="decimal" value={form.cashbackFixed} onChange={set('cashbackFixed')} /></Field>
      <Field label={t('Max cashback per trip (৳)')} hint={t('Empty: no maximum')}><input inputMode="decimal" value={form.maxCashbackPerTrip} onChange={set('maxCashbackPerTrip')} /></Field>
      <Field label={t('Minimum fare (৳)')}><input inputMode="decimal" value={form.minFare} onChange={set('minFare')} /></Field>
      <Field label={t('Total budget (৳)')} hint={t('Empty: no budget limit')}><input inputMode="decimal" value={form.budget} onChange={set('budget')} /></Field>
      <Field label={t('Most rewards')} hint={t('Empty: no limit')}><input inputMode="numeric" value={form.maxRedemptions} onChange={set('maxRedemptions')} /></Field>
      <Field label={t('Minimum tier')}><select value={form.minTier} onChange={set('minTier')}><option value="">{t('Any tier')}</option>{tiers.map((tier) => <option key={tier.code} value={tier.code}>{t(tier.name)}</option>)}</select></Field>
      <Field label={t('Artwork colours')}><select value={form.theme} onChange={set('theme')}>{THEMES.map((theme) => <option key={theme} value={theme}>{t(theme)}</option>)}</select></Field>
      <Field label={t('Banner image link')} hint={t('Optional https:// link; the drawn artwork is used without one')} wide><input value={form.bannerUrl} onChange={set('bannerUrl')} placeholder="https://…" /></Field>
    </div>
    <div className="ao-toggles">
      <Toggle label={t('New users only')} checked={form.newUsersOnly || form.type === 'welcome'} onChange={(newUsersOnly) => setForm({ ...form, newUsersOnly })} />
      <Toggle label={t('Friday and Saturday trips only')} checked={form.weekendOnly || form.type === 'weekend'} onChange={(weekendOnly) => setForm({ ...form, weekendOnly })} />
      <Toggle label={t('First eligible trip only')} checked={form.firstTripOnly || form.type === 'first-trip'} onChange={(firstTripOnly) => setForm({ ...form, firstTripOnly })} />
    </div>
    <Problem text={error} />
    <div className="ao-actions"><button type="button" className="button button-outline" onClick={onCancel}>{t('Cancel')}</button>
      <button type="submit" className="button button-green" disabled={busy}>{busy ? <Loader2 size={15} className="ao-spin" /> : <Save size={15} />} {t('Save campaign')}</button></div>
  </form>
}

function Campaigns({ token, notify }) {
  const [state, reload] = useLoad(useCallback(() => adminPromoApi.campaigns(token), [token]))
  const [tiers] = useLoad(useCallback(() => adminPromoApi.tiers(token), [token]))
  const [editing, setEditing] = useState(null)
  const list = state.data?.data || []
  const save = async (body) => {
    const result = editing === 'new' ? await adminPromoApi.createCampaign(body, token) : await adminPromoApi.updateCampaign(editing.id, body, token)
    notify(t(result.message)); setEditing(null); reload()
  }
  const status = async (c, next) => {
    try { const result = await adminPromoApi.campaignStatus(c.id, next, token); notify(t(result.message)); reload() } catch (e) { notify(errorText(e), 'error') }
  }
  if (editing) {
    return <section className="ao-card"><header className="ao-head"><h3>{editing === 'new' ? t('New campaign') : t('Edit {name}', { name: editing.name })}</h3></header>
      <CampaignEditor initial={editing === 'new' ? emptyCampaign() : campaignForm(editing)} tiers={tiers.data?.data || []} onCancel={() => setEditing(null)} onSave={save} /></section>
  }
  return <section className="ao-card">
    <header className="ao-head"><h3>{t('Campaigns')} <span className="ao-count">{formatNumber(list.length)}</span></h3>
      <button type="button" className="button button-green button-small" onClick={() => setEditing('new')}><Plus size={14} /> {t('New campaign')}</button></header>
    <Problem text={state.error} />
    {state.status === 'loading' ? <Busy /> : !list.length ? <p className="ao-muted">{t('No campaigns yet.')}</p>
      : <div className="ao-campaigns">{list.map((c) => <article key={c.id} className="ao-campaign">
        <span className={`ao-swatch theme-${c.theme}`} aria-hidden="true" />
        <div><b>{c.name}</b><small>{t(CAMPAIGN_TYPE_LABELS[c.type] || c.type)} · {t(c.reward)} · {dhakaDateTime(c.startsAt)} → {dhakaDateTime(c.endsAt)}</small>
          <small>{t('Given out')}: {money(c.spent)}{c.budget ? ` / ${money(c.budget)}` : ''} · {formatNumber(c.redemptions)}{c.maxRedemptions ? ` / ${formatNumber(c.maxRedemptions)}` : ''} {t('rewards')}</small></div>
        <span className={`ao-status ${c.status}`}>{t(c.status === 'scheduled' ? 'Scheduled' : c.status === 'active' ? 'Active' : c.status === 'paused' ? 'Paused' : 'Expired')}</span>
        <div className="ao-row-actions">
          <button type="button" className="ao-icon" onClick={() => setEditing(c)} aria-label={t('Edit {name}', { name: c.name })}><Pencil size={14} /></button>
          {(c.status === 'active' || c.status === 'scheduled') && <button type="button" className="ao-icon" onClick={() => status(c, 'paused')} aria-label={t('Pause {name}', { name: c.name })}><Pause size={14} /></button>}
          {c.status === 'paused' && <button type="button" className="ao-icon" onClick={() => status(c, 'active')} aria-label={t('Resume {name}', { name: c.name })}><Play size={14} /></button>}
          {c.status !== 'expired' && <button type="button" className="ao-icon danger" onClick={() => status(c, 'expired')} aria-label={t('End {name}', { name: c.name })}><Square size={14} /></button>}
        </div>
      </article>)}</div>}
  </section>
}

// ---- Rules & tiers ----

const SETTING_FIELDS = [
  ['Cashback', [
    ['cashbackPercent', 'Cashback (%)', 'Of the fare of each eligible trip.'], ['cashbackFixed', 'Fixed cashback (৳)', 'Added to the percentage.'],
    ['cashbackMinFare', 'Minimum fare (৳)', 'Trips below this earn no cashback.'], ['cashbackMaxPerTrip', 'Most per trip (৳)', '0 means no limit.'],
    ['cashbackMaxPerUserMonthly', 'Most per user per month (৳)', '0 means no limit.'], ['cashbackExpiryDays', 'Expires after (days)', '0 means it never expires.'],
  ]],
  ['New users and referrals', [
    ['newUserDays', 'An account is new for (days)', 'For new-user coupons and campaigns.'], ['firstTripCashback', 'First trip bonus (৳)', 'Once, after the first eligible trip.'],
    ['referrerReward', 'Reward for the inviter (৳)', 'Paid after the friend’s first eligible trip.'], ['referredReward', 'Reward for the friend (৳)', 'Paid with it.'],
    ['referralApplyDays', 'Enter a code within (days)', 'After signing up.'], ['maxReferralRewardsPerMonth', 'Referral rewards per person per month', '0 means no limit.'],
  ]],
]

function Rules({ token, notify }) {
  const [settings, reloadSettings] = useLoad(useCallback(() => adminPromoApi.settings(token), [token]))
  const [tiers, reloadTiers] = useLoad(useCallback(() => adminPromoApi.tiers(token), [token]))
  const [form, setForm] = useState(null)
  const [tierForm, setTierForm] = useState(null)
  const [error, setError] = useState('')
  const [tierError, setTierError] = useState('')
  const [busy, setBusy] = useState('')
  useEffect(() => { if (settings.data?.data) setForm(Object.fromEntries(Object.entries(settings.data.data).map(([k, v]) => [k, typeof v === 'number' ? String(v) : v]))) }, [settings.data])
  useEffect(() => { if (tiers.data?.data) setTierForm(tiers.data.data.map((tier) => ({ ...tier }))) }, [tiers.data])
  const save = async (event) => {
    event.preventDefault()
    const body = { ...form }
    for (const group of SETTING_FIELDS) for (const [key] of group[1]) {
      const value = Number(form[key])
      if (!Number.isFinite(value) || value < 0) { setError(t('Enter a number of 0 or more for every field.')); return }
      body[key] = value
    }
    body.cashbackPercent = Number(form.cashbackPercent)
    if (body.cashbackPercent > 100) { setError(t('Cashback cannot be more than 100%.')); return }
    setBusy('settings'); setError('')
    try { const result = await adminPromoApi.saveSettings(body, token); notify(t(result.message)); reloadSettings() } catch (e) { setError(errorText(e)) } finally { setBusy('') }
  }
  const saveTiers = async (event) => {
    event.preventDefault()
    setBusy('tiers'); setTierError('')
    try {
      const result = await adminPromoApi.saveTiers(tierForm.map((tier) => ({ code: tier.code, name: tier.name, minTrips: Number(tier.minTrips), minSpend: Number(tier.minSpend || 0),
        extraCashbackPercent: Number(tier.extraCashbackPercent || 0), extraCapPerTrip: Number(tier.extraCapPerTrip || 0), extraMonthlyCap: Number(tier.extraMonthlyCap || 0), benefits: tier.benefits })), token)
      notify(t(result.message)); reloadTiers()
    } catch (e) { setTierError(errorText(e)) } finally { setBusy('') }
  }
  const setTier = (index, key) => (e) => setTierForm(tierForm.map((tier, i) => (i === index ? { ...tier, [key]: e.target.value } : tier)))
  return <div className="ao-grid">
    <section className="ao-card wide">
      <header className="ao-head"><h3>{t('Reward rules')}</h3></header>
      <Problem text={settings.error} />
      {!form ? <Busy /> : <form className="ao-form" onSubmit={save} noValidate>
        <div className="ao-toggles">
          <Toggle label={t('Cashback on eligible trips')} checked={form.cashbackEnabled} onChange={(cashbackEnabled) => setForm({ ...form, cashbackEnabled })} />
          <Toggle label={t('Refer & Earn')} checked={form.referralEnabled} onChange={(referralEnabled) => setForm({ ...form, referralEnabled })} />
          <Toggle label={t('Allow more than one coupon per trip')} checked={form.allowCouponStacking} onChange={(allowCouponStacking) => setForm({ ...form, allowCouponStacking })} hint={t('Stacking')} />
          <Toggle label={t('Allow coupons on trips that earned cashback')} checked={form.allowCouponWithCashback} onChange={(allowCouponWithCashback) => setForm({ ...form, allowCouponWithCashback })} />
        </div>
        {SETTING_FIELDS.map(([group, fields]) => <fieldset key={group} className="ao-fieldset"><legend>{t(group)}</legend><div className="ao-fields">
          {fields.map(([key, label, hint]) => <Field key={key} label={t(label)} hint={t(hint)}><input inputMode="decimal" value={form[key]} onChange={(e) => setForm({ ...form, [key]: e.target.value })} /></Field>)}
        </div></fieldset>)}
        <p className="ao-muted">{t('Changes apply to trips completed from now on. Rewards already given stay as they are.')}</p>
        <Problem text={error} />
        <div className="ao-actions"><button type="submit" className="button button-green" disabled={busy === 'settings'}>{busy === 'settings' ? <Loader2 size={15} className="ao-spin" /> : <Save size={15} />} {t('Save rules')}</button></div>
      </form>}
    </section>
    <section className="ao-card wide">
      <header className="ao-head"><h3>{t('Membership tiers')}</h3></header>
      <Problem text={tiers.error} />
      {!tierForm ? <Busy /> : <form className="ao-form" onSubmit={saveTiers} noValidate>
        <div className="ao-table-wrap"><table className="ao-table ao-tiers">
          <thead><tr><th>{t('Tier')}</th><th>{t('Trips needed')}</th><th>{t('Or spending (৳)')}</th><th>{t('Extra cashback (%)')}</th><th>{t('Extra per trip (৳)')}</th><th>{t('Extra per month (৳)')}</th><th>{t('Benefits shown to users')}</th></tr></thead>
          <tbody>{tierForm.map((tier, i) => <tr key={tier.code}>
            <td><input value={tier.name} onChange={setTier(i, 'name')} aria-label={t('Name of {tier}', { tier: tier.code })} /></td>
            <td><input inputMode="numeric" value={tier.minTrips} onChange={setTier(i, 'minTrips')} disabled={i === 0} aria-label={t('Trips needed for {tier}', { tier: tier.name })} /></td>
            <td><input inputMode="decimal" value={tier.minSpend} onChange={setTier(i, 'minSpend')} disabled={i === 0} aria-label={t('Spending for {tier}', { tier: tier.name })} /></td>
            <td><input inputMode="decimal" value={tier.extraCashbackPercent} onChange={setTier(i, 'extraCashbackPercent')} aria-label={t('Extra cashback for {tier}', { tier: tier.name })} /></td>
            <td><input inputMode="decimal" value={tier.extraCapPerTrip} onChange={setTier(i, 'extraCapPerTrip')} aria-label={t('Extra per-trip limit for {tier}', { tier: tier.name })} /></td>
            <td><input inputMode="decimal" value={tier.extraMonthlyCap} onChange={setTier(i, 'extraMonthlyCap')} aria-label={t('Extra monthly limit for {tier}', { tier: tier.name })} /></td>
            <td><input value={tier.benefits} onChange={setTier(i, 'benefits')} maxLength={300} aria-label={t('Benefits of {tier}', { tier: tier.name })} /></td>
          </tr>)}</tbody>
        </table></div>
        <Problem text={tierError} />
        <div className="ao-actions"><button type="submit" className="button button-green" disabled={busy === 'tiers'}>{busy === 'tiers' ? <Loader2 size={15} className="ao-spin" /> : <Save size={15} />} {t('Save tiers')}</button></div>
      </form>}
    </section>
  </div>
}

// ---- Redemptions, referrals, audit ----

function Redemptions({ token }) {
  const [code, setCode] = useState('')
  const [query, setQuery] = useState('')
  const [page, setPage] = useState(1)
  const [state] = useLoad(useCallback(() => adminPromoApi.redemptions(query, page, token), [query, page, token]))
  const rows = state.data?.data || []
  const pages = Math.max(1, Math.ceil((state.data?.total || 0) / (state.data?.pageSize || 25)))
  return <section className="ao-card">
    <header className="ao-head"><h3>{t('Redemptions')} <span className="ao-count">{formatNumber(state.data?.total || 0)}</span></h3>
      <form className="ao-inline" onSubmit={(e) => { e.preventDefault(); setPage(1); setQuery(code.trim().toUpperCase()) }}>
        <input value={code} onChange={(e) => setCode(e.target.value.toUpperCase())} placeholder={t('Filter by code')} aria-label={t('Filter by code')} className="ao-mono" />
        <button type="submit" className="ao-icon" aria-label={t('Search')}><Search size={14} /></button></form></header>
    <Problem text={state.error} />
    {state.status === 'loading' ? <Busy /> : !rows.length ? <p className="ao-muted">{t('No redemptions found.')}</p>
      : <div className="ao-table-wrap"><table className="ao-table">
        <thead><tr><th>{t('When')}</th><th>{t('User')}</th><th>{t('Code')}</th><th>{t('Trip')}</th><th>{t('Fare')}</th><th>{t('Saving')}</th><th>{t('Paid')}</th><th>{t('How')}</th></tr></thead>
        <tbody>{rows.map((r) => <tr key={r.id}><td><small>{dhakaDateTime(r.createdAt)}</small></td><td>{r.userName}<small>{r.userEmail}</small></td><td className="ao-mono">{r.code}</td>
          <td><small>{r.trip}</small></td><td>{money(r.fare)}</td><td className="ao-good">−{money(r.discount)}</td><td>{money(r.finalFare)}</td><td><small>{r.mode === 'gpay' ? 'GPay' : t('Wallet')}</small></td></tr>)}</tbody>
      </table></div>}
    {pages > 1 && <div className="ao-pager"><button type="button" className="button button-outline button-small" disabled={page <= 1} onClick={() => setPage(page - 1)}>{t('Previous')}</button>
      <span>{t('Page {page} of {pages}', { page, pages })}</span><button type="button" className="button button-outline button-small" disabled={page >= pages} onClick={() => setPage(page + 1)}>{t('Next')}</button></div>}
  </section>
}

function Referrals({ token, notify }) {
  const [status, setStatus] = useState('')
  const [state, reload] = useLoad(useCallback(() => adminPromoApi.referrals(status, token), [status, token]))
  const reject = async (r) => {
    try { const result = await adminPromoApi.rejectReferral(r.id, 'Rejected by an admin after review.', token); notify(t(result.message)); reload() } catch (e) { notify(errorText(e), 'error') }
  }
  const rows = state.data?.data || []
  return <section className="ao-card">
    <header className="ao-head"><h3>{t('Referrals')}</h3>
      <select value={status} onChange={(e) => setStatus(e.target.value)} aria-label={t('Filter by status')}><option value="">{t('All')}</option>{Object.entries(REFERRAL_STATUS_LABELS).map(([value, label]) => <option key={value} value={value}>{t(label)}</option>)}</select></header>
    <Problem text={state.error} />
    {state.status === 'loading' ? <Busy /> : !rows.length ? <p className="ao-muted">{t('No referrals yet.')}</p>
      : <div className="ao-table-wrap"><table className="ao-table">
        <thead><tr><th>{t('Joined')}</th><th>{t('Inviter')}</th><th>{t('New user')}</th><th>{t('Status')}</th><th>{t('Rewards')}</th><th><span className="sr-only">{t('Actions')}</span></th></tr></thead>
        <tbody>{rows.map((r) => <tr key={r.id}><td><small>{shortDay(r.createdAt)}</small></td><td>{r.referrer.name}<small>{r.referrer.email}</small></td><td>{r.referred.name}<small>{r.referred.email}</small></td>
          <td><span className={`ao-status ${r.status}`}>{t(REFERRAL_STATUS_LABELS[r.status] || r.status)}</span>{r.note && <small>{r.note}</small>}</td>
          <td><small>{r.status === 'rewarded' ? `${money(r.referrerReward)} + ${money(r.referredReward)}` : '—'}</small></td>
          <td>{(r.status === 'pending' || r.status === 'qualified') && <button type="button" className="button button-outline button-small" onClick={() => reject(r)}><Ban size={13} /> {t('Reject')}</button>}</td></tr>)}</tbody>
      </table></div>}
  </section>
}

function AuditLog({ token }) {
  const [state, reload] = useLoad(useCallback(() => adminPromoApi.audit(token), [token]))
  const rows = state.data?.data || []
  return <section className="ao-card">
    <header className="ao-head"><h3>{t('Audit log')}</h3><button type="button" className="ao-icon" onClick={reload} aria-label={t('Refresh')}><RefreshCw size={15} /></button></header>
    <Problem text={state.error} />
    {state.status === 'loading' ? <Busy /> : !rows.length ? <p className="ao-muted">{t('No changes recorded yet.')}</p>
      : <ul className="ao-audit">{rows.map((a) => <li key={a.id}><span className={`ao-action ${a.action.split(':')[0]}`}>{a.action}</span>
        <small>{dhakaDateTime(a.createdAt)}{a.entityId ? ` · ${a.entityType} ${a.entityId.slice(0, 8)}` : ''}</small><code>{a.details}</code></li>)}</ul>}
  </section>
}

export function AdminPromotions({ token, notify }) {
  const [tab, setTab] = useState('overview')
  return <section className="pf-panel ao-panel" id="admin-offers" aria-labelledby="ao-title">
    <header className="ad-toolbar"><div><h2 id="ao-title"><Gift size={18} /> {t('Offers & rewards')}</h2>
      <p>{t('Coupons, cashback campaigns, reward rules and membership tiers for Dhaka bus trips. Every change is recorded in the audit log.')}</p></div></header>
    <nav className="ao-tabs" role="tablist" aria-label={t('Offers & rewards sections')}>
      {TABS.map(([id, label, Icon]) => <button key={id} type="button" role="tab" aria-selected={tab === id} className={tab === id ? 'selected' : ''} onClick={() => setTab(id)}><Icon size={15} /> {t(label)}</button>)}
    </nav>
    {tab === 'overview' && <Overview token={token} notify={notify} />}
    {tab === 'coupons' && <Coupons token={token} notify={notify} />}
    {tab === 'campaigns' && <Campaigns token={token} notify={notify} />}
    {tab === 'rules' && <Rules token={token} notify={notify} />}
    {tab === 'redemptions' && <Redemptions token={token} />}
    {tab === 'referrals' && <Referrals token={token} notify={notify} />}
    {tab === 'audit' && <AuditLog token={token} />}
  </section>
}
