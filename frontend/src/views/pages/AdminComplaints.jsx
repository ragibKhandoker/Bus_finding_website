import React, { useCallback, useEffect, useState } from 'react'
import {
  BarChart3, CheckCircle2, ChevronDown, CircleAlert, ClipboardList, Eye, FileSearch, Inbox, Loader2, Mail, MessageSquareText, Phone, RefreshCw, Search,
  SlidersHorizontal, User,
} from 'lucide-react'
import { formatNumber, t } from '../../models/i18n'
import { CATEGORIES, CATEGORY_ICON, NOTE_TEMPLATES, STATUSES, STATUS_LABELS, complaintApi, day, dayTime } from '../../models/complaints'
import { CpModal, EvidenceGallery, StatusBadge, Timeline } from '../components/ComplaintUI'

// F18 for admins, made easy to follow: what to do (three steps), what needs attention now, one status at a time,
// and a review window that says what each choice means for the user.

const errorText = (error) => t(error?.message || 'Something went wrong. Please try again.')

// What each status means, in plain words, for the cards, the tabs and the review choices.
const STATUS_HELP = {
  PENDING: 'New, waiting for you to look at it',
  REVIEWING: 'You are checking it',
  RESOLVED: 'Done: action was taken',
  REJECTED: 'Closed: could not be accepted',
}
const CHOICE_HELP = {
  REVIEWING: 'Start working on it. The user sees that their complaint is being checked.',
  RESOLVED: 'The problem was handled. Tell the user what was done.',
  REJECTED: 'It cannot be accepted (for example, not enough evidence). Tell the user why.',
  PENDING: 'Put it back in the queue to look at again later.',
}

const daysWaiting = (iso) => Math.max(0, Math.floor((Date.now() - new Date(iso).getTime()) / 86_400_000))

function useStats(token, refreshKey) {
  const [state, setState] = useState({ status: 'loading' })
  useEffect(() => {
    complaintApi.stats(token).then((res) => setState({ status: 'ready', s: res.data })).catch((e) => setState({ status: 'error', error: errorText(e) }))
  }, [token, refreshKey])
  return state
}

function Insights({ s }) {
  const top = Math.max(1, ...s.byCategory.map((c) => c.count))
  const peak = Math.max(1, ...s.last30Days.map((d) => d.count))
  return <div className="cpa-charts">
    <div className="cpa-chart"><h4>{t('Complaints by category')}</h4>
      <ul>{s.byCategory.map((c) => <li key={c.category}><span>{CATEGORY_ICON[c.category]} {t(c.category)}</span><i><b style={{ width: `${(c.count / top) * 100}%` }} /></i><em>{formatNumber(c.count)}</em></li>)}</ul></div>
    <div className="cpa-chart"><h4>{t('New complaints, last 30 days')}</h4>
      <div className="cpa-days">{s.last30Days.map((d) => <span key={d.date} title={`${d.date}: ${d.count}`} style={{ height: `${Math.max(3, (d.count / peak) * 100)}%` }} className={d.count ? '' : 'zero'} />)}</div>
      {s.topBuses.length > 0 && <><h4>{t('Buses with the most complaints')}</h4><ol className="cpa-top">{s.topBuses.map((b) => <li key={b.bus}><span>{t(b.bus)}</span><em>{formatNumber(b.count)}</em></li>)}</ol></>}
    </div>
  </div>
}

// ---- Review window ----

function Review({ id, token, onClose, onChanged, notify }) {
  const [state, setState] = useState({ status: 'loading' })
  const [next, setNext] = useState('')
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  // Shown inside the review window (not as a page message behind it).
  const [saved, setSaved] = useState('')
  const load = useCallback(() => complaintApi.get(id, token).then((res) => setState({ status: 'ready', c: res.data })).catch((e) => setState({ status: 'error', error: errorText(e) })), [id, token])
  useEffect(() => { load() }, [load])
  const c = state.c
  const choose = (status) => { setNext(status); setNote(NOTE_TEMPLATES[status] || ''); setError(''); setSaved('') }
  const save = async (event) => {
    event.preventDefault()
    if (!next) { setError(t('Choose the new status.')); return }
    if (note.trim().length < 5) { setError(t('Write a note of at least 5 characters for the user.')); return }
    setBusy(true); setError('')
    try {
      const res = await complaintApi.setStatus(c.id, next, note.trim(), token)
      setSaved(t('Saved. The complaint is now {status} and the passenger was notified.', { status: t(STATUS_LABELS[res.data.status] || res.data.status) }))
      setState({ status: 'ready', c: res.data }); setNext(''); setNote('')
      onChanged()
    } catch (e) { setError(errorText(e)) } finally { setBusy(false) }
  }
  return <CpModal title={c ? c.subject : t('Complaint')} subtitle={c ? `${c.code} · ${t('submitted {date}', { date: dayTime(c.createdAt) })}` : ''} onClose={onClose} wide>
    {state.status === 'loading' ? <div className="cp-center"><Loader2 size={20} className="cp-spin" /> {t('Loading…')}</div>
      : state.status === 'error' ? <p className="cp-form-error" role="alert"><CircleAlert size={15} /> {state.error}</p>
        : <div className="cp-details">
          <div className="cp-details-main">
            <h4 className="cpa-step"><span>1</span> {t('Read the complaint')}</h4>
            <div className="cp-details-top"><StatusBadge status={c.status} large /><span className="cp-chip">{CATEGORY_ICON[c.category]} {t(c.category)}</span></div>
            {c.user && <div className="cpa-user"><span className="cpa-avatar"><User size={16} /></span><div><b>{c.user.name}</b>
              <small><Mail size={12} /> {c.user.email}{c.user.phone ? <> · <Phone size={12} /> {c.user.phone}</> : null}</small></div></div>}
            <dl className="cp-dl">
              <div><dt>{t('Bus')}</dt><dd>{t(c.busName)}</dd></div>
              <div><dt>{t('Route')}</dt><dd>{c.routeName || '—'}</dd></div>
              {c.stopName && <div><dt>{t('Stop')}</dt><dd>{t(c.stopName)}</dd></div>}
              <div><dt>{t('Location')}</dt><dd>{c.incidentLocation}</dd></div>
              <div><dt>{t('Incident date')}</dt><dd>{day(c.incidentDate)}</dd></div>
            </dl>
            <h4>{t('What the user wrote')}</h4>
            <p className="cp-description">{c.description}</p>
            <h4>{t('Evidence')}</h4>
            <EvidenceGallery evidence={c.evidence} token={token} />
          </div>
          <aside className="cp-details-side">
            {c.nextStatuses.length ? <form className="cpa-review" onSubmit={save} noValidate>
              <h4 className="cpa-step"><span>2</span> {t('Choose what happens next')}</h4>
              <div className="cpa-choices" role="radiogroup" aria-label={t('New status')}>
                {c.nextStatuses.map((s) => <label key={s} className={`cpa-choice ${s.toLowerCase()}${next === s ? ' selected' : ''}`}>
                  <input type="radio" name="cpa-next" value={s} checked={next === s} onChange={() => choose(s)} aria-label={t(STATUS_LABELS[s])} />
                  <b>{t(STATUS_LABELS[s])}</b><small>{t(CHOICE_HELP[s])}</small>
                </label>)}
              </div>
              <h4 className="cpa-step"><span>3</span> {t('Write a note the user will read')}</h4>
              <label htmlFor="cpa-note" className="sr-only">{t('Note for the user (required)')}</label>
              <textarea id="cpa-note" rows={4} maxLength={1000} value={note} onChange={(e) => { setNote(e.target.value); setError('') }}
                placeholder={next ? t('A ready-made note is filled in. Change it to say what you did.') : t('Choose a status first. A ready-made note will be filled in.')} aria-label={t('Note for the user (required)')} />
              {error && <small className="cp-error" role="alert">{error}</small>}
              {saved && !error && <p className="cpa-saved" role="status"><CheckCircle2 size={15} /> {saved}</p>}
              <button type="submit" className="button cp-primary" disabled={busy}>{busy ? <Loader2 size={15} className="cp-spin" /> : <MessageSquareText size={15} />} {t('Save review')}</button>
              <small className="cp-muted">{t('The user gets a notification with your note.')}</small>
            </form> : <p className="cp-muted">{t('No further status change is possible.')}</p>}
            <h4>{t('Review history')}</h4>
            <Timeline history={c.history} status={c.status} forAdmin />
          </aside>
        </div>}
  </CpModal>
}

// ---- Section ----

const STATUS_TABS = [['', 'All'], ...STATUSES.map((s) => [s, STATUS_LABELS[s]])]

export function AdminComplaints({ token, notify = () => {} }) {
  const [filters, setFilters] = useState({ search: '', category: '', status: '', from: '', to: '', sort: 'newest' })
  const [applied, setApplied] = useState(filters)
  const [page, setPage] = useState(1)
  const [state, setState] = useState({ status: 'loading', items: [], total: 0, totalPages: 1 })
  const [openId, setOpenId] = useState(null)
  const [refreshKey, setRefreshKey] = useState(0)
  const [moreFilters, setMoreFilters] = useState(false)
  const [insights, setInsights] = useState(false)
  const stats = useStats(token, refreshKey)
  const s = stats.s
  useEffect(() => { const id = window.setTimeout(() => { setApplied(filters); setPage(1) }, 300); return () => window.clearTimeout(id) }, [filters])
  const load = useCallback(async () => {
    setState((x) => ({ ...x, status: x.items.length ? 'ready' : 'loading' }))
    try {
      const res = await complaintApi.all({ ...applied, page, pageSize: 15 }, token)
      setState({ status: 'ready', items: res.data || [], total: res.total || 0, totalPages: Math.max(1, res.totalPages || 1) })
    } catch (e) { setState({ status: 'error', items: [], total: 0, totalPages: 1, error: errorText(e) }) }
  }, [applied, page, token])
  useEffect(() => { load() }, [load, refreshKey])
  const set = (key) => (e) => setFilters((f) => ({ ...f, [key]: e.target.value }))
  const showStatus = (status) => setFilters((f) => ({ ...f, status }))
  const waiting = s ? s.pending + s.reviewing : 0
  const extraFilters = [filters.category, filters.from, filters.to].filter(Boolean).length + (filters.sort === 'oldest' ? 1 : 0)
  const cards = [['', 'total', 'All complaints', s?.total, 'Every complaint ever sent'], ...STATUSES.map((st) => [st, st.toLowerCase(), STATUS_LABELS[st], s?.[st.toLowerCase()], STATUS_HELP[st]])]

  return <section className="pf-panel cpa-panel" id="admin-complaints" aria-labelledby="cpa-title">
    <header className="ad-toolbar"><div><h2 id="cpa-title"><ClipboardList size={18} /> {t('Complaint Management')}</h2>
      <p>{t('Passengers report problems with Dhaka buses here. Check each one, decide what to do, and tell the passenger.')}</p></div>
      <button type="button" className="cp-icon" onClick={() => setRefreshKey((k) => k + 1)} aria-label={t('Refresh')}><RefreshCw size={15} /></button></header>

    <ol className="cpa-guide" aria-label={t('How to handle a complaint')}>
      <li><span>1</span><div><b>{t('Open a new complaint')}</b><small>{t('Click Review on a Pending one.')}</small></div></li>
      <li><span>2</span><div><b>{t('Mark it Reviewing')}</b><small>{t('While you check with the bus company.')}</small></div></li>
      <li><span>3</span><div><b>{t('Resolve or reject it')}</b><small>{t('Always with a note: the passenger reads it.')}</small></div></li>
    </ol>

    {stats.status === 'error' ? <p className="cp-form-error" role="alert"><CircleAlert size={15} /> {stats.error}</p>
      : s && (waiting > 0
        ? <div className="cpa-attention" role="status"><Inbox size={20} /><div><b>{t('{count} complaints need your attention', { count: formatNumber(waiting) })}</b>
          <small>{t('{pending} new and {reviewing} being checked.', { pending: formatNumber(s.pending), reviewing: formatNumber(s.reviewing) })}</small></div>
          <button type="button" className="button cp-primary button-small" onClick={() => showStatus('PENDING')}>{t('Show new complaints')}</button></div>
        : <div className="cpa-attention clear" role="status"><Inbox size={20} /><div><b>{t('All caught up')}</b><small>{t('No complaint is waiting for you.')}</small></div></div>)}

    <div className="cpa-cards">{cards.map(([status, key, label, value, help]) => <button type="button" key={key} data-testid="cpa-stat"
      className={`cpa-card ${key}${filters.status === status ? ' current' : ''}`} onClick={() => showStatus(status)} aria-pressed={filters.status === status}>
      <small>{t(label)}</small><b>{s ? formatNumber(value) : '–'}</b><em>{t(help)}</em></button>)}</div>

    <div className="cpa-list-head">
      <nav className="cpa-status-tabs" role="tablist" aria-label={t('Status')}>
        {STATUS_TABS.map(([value, label]) => <button key={label} type="button" role="tab" aria-selected={filters.status === value} className={`${filters.status === value ? 'selected' : ''} ${value.toLowerCase()}`} onClick={() => showStatus(value)}>{t(label)}</button>)}
      </nav>
      <label className="cpa-search"><Search size={15} /><input value={filters.search} onChange={set('search')} placeholder={t('Find by ID (CMP-…), bus, subject or passenger')} aria-label={t('Search complaints')} /></label>
      <button type="button" className={`button button-outline button-small cpa-more${moreFilters ? ' open' : ''}`} onClick={() => setMoreFilters(!moreFilters)} aria-expanded={moreFilters}>
        <SlidersHorizontal size={14} /> {t('More filters')}{extraFilters > 0 && <em>{formatNumber(extraFilters)}</em>}</button>
    </div>
    {moreFilters && <div className="cpa-filters">
      <label><span>{t('Category')}</span><select value={filters.category} onChange={set('category')} aria-label={t('Category')}><option value="">{t('All categories')}</option>{CATEGORIES.map(([c]) => <option key={c} value={c}>{t(c)}</option>)}</select></label>
      <label><span>{t('Happened from')}</span><input type="date" value={filters.from} onChange={set('from')} aria-label={t('Incident from')} /></label>
      <label><span>{t('Happened until')}</span><input type="date" value={filters.to} onChange={set('to')} aria-label={t('Incident to')} /></label>
      <label><span>{t('Order')}</span><select value={filters.sort} onChange={set('sort')} aria-label={t('Sort')}><option value="newest">{t('Newest first')}</option><option value="oldest">{t('Oldest first')}</option></select></label>
      <button type="button" className="cp-link" onClick={() => setFilters((f) => ({ ...f, category: '', from: '', to: '', sort: 'newest' }))}>{t('Clear filters')}</button>
    </div>}

    {state.status === 'loading' ? <div className="cp-skeletons">{[0, 1, 2, 3].map((i) => <i key={i} />)}</div>
      : state.status === 'error' ? <p className="cp-form-error" role="alert"><CircleAlert size={15} /> {state.error}</p>
        : !state.items.length ? <div className="cp-empty"><ClipboardList size={26} /><b>{filters.status === 'PENDING' && !filters.search ? t('No new complaints') : t('No complaints found')}</b>
          <span>{filters.search || extraFilters ? t('Try other words or clear the filters.') : t('Nothing to show here.')}</span></div>
          : <>
            <div className="cpa-table-wrap"><table className="cpa-table">
              <thead><tr><th>{t('Complaint')}</th><th>{t('Passenger')}</th><th>{t('Bus and route')}</th><th>{t('Problem')}</th><th>{t('Dates')}</th><th>{t('Status')}</th><th><span className="sr-only">{t('Actions')}</span></th></tr></thead>
              <tbody>{state.items.map((c) => {
                const open = c.status === 'PENDING' || c.status === 'REVIEWING'
                const age = daysWaiting(c.createdAt)
                return <tr key={c.id} data-testid="cpa-row" className={open ? 'open' : ''}>
                  <td className="cpa-subject"><b>{c.subject}</b><small className="cp-code">{c.code}{c.evidenceCount > 0 ? ` · 📎 ${formatNumber(c.evidenceCount)}` : ''}</small></td>
                  <td>{c.user?.name}<small>{c.user?.email}</small></td>
                  <td>{t(c.busName)}<small>{c.routeName}</small></td>
                  <td>{CATEGORY_ICON[c.category]} {t(c.category)}</td>
                  <td><small>{t('Happened {date}', { date: day(c.incidentDate) })}</small><small>{t('Sent {date}', { date: day(c.createdAt) })}</small>
                    {open && <small className={`cpa-age${age >= 3 ? ' late' : ''}`}>{age === 0 ? t('Waiting since today') : age === 1 ? t('Waiting 1 day') : t('Waiting {count} days', { count: age })}</small>}</td>
                  <td><StatusBadge status={c.status} /></td>
                  <td>{open
                    ? <button type="button" className="button cp-primary button-small" onClick={() => setOpenId(c.id)} aria-label={t('View {code}', { code: c.code })}><FileSearch size={14} /> {t('Review')}</button>
                    : <button type="button" className="button button-outline button-small" onClick={() => setOpenId(c.id)} aria-label={t('View {code}', { code: c.code })}><Eye size={14} /> {t('View')}</button>}</td>
                </tr>
              })}</tbody>
            </table></div>
            <div className="cpa-pager"><span>{t('{count} complaints', { count: formatNumber(state.total) })}</span>
              {state.totalPages > 1 && <div><button type="button" className="button button-outline button-small" disabled={page <= 1} onClick={() => setPage(page - 1)}>{t('Previous')}</button>
                <span>{t('Page {page} of {pages}', { page, pages: state.totalPages })}</span>
                <button type="button" className="button button-outline button-small" disabled={page >= state.totalPages} onClick={() => setPage(page + 1)}>{t('Next')}</button></div>}</div>
          </>}

    {s && <div className="cpa-insights">
      <button type="button" className="cpa-insights-toggle" onClick={() => setInsights(!insights)} aria-expanded={insights}>
        <BarChart3 size={16} /> {t('Insights: which problems and buses get the most complaints')} <ChevronDown size={16} className={insights ? 'up' : ''} /></button>
      {insights && <Insights s={s} />}
    </div>}

    {openId && <Review id={openId} token={token} notify={notify} onClose={() => setOpenId(null)} onChanged={() => setRefreshKey((k) => k + 1)} />}
  </section>
}
