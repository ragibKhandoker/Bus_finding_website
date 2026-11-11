import React, { useCallback, useEffect, useState } from 'react'
import { ArrowLeft, BadgeCheck, ChevronLeft, ChevronRight, CircleAlert, CircleX, Clock3, FileWarning, Lock, RefreshCw, Search, ShieldCheck, Users } from 'lucide-react'
import { formatNumber, t } from '../../models/i18n'
import { STATUSES, STATUS_LABELS, adminDriverApi } from '../../models/driver'
import { DriverApplicationTable } from '../components/driver/DriverApplicationTable'
import { SignInPrompt } from '../components/common'
import { ADMIN_ALERT_EVENT } from '../../controllers/useAdminAlerts'
import '../styles/driver.css'

const PAGE_SIZE = 10

// /admin/driver-verification: every driver application with counts, search, filters, sorting and pages.
export function AdminDriverVerificationPage({ token, signedIn, isAdmin, openAuth, navigate }) {
  const [stats, setStats] = useState(null)
  const [filters, setFilters] = useState({ search: '', status: '', from: '', to: '', sort: 'newest', page: 1 })
  const [list, setList] = useState({ rows: [], total: 0, totalPages: 1, loading: true, error: '' })
  const [typed, setTyped] = useState('')

  const loadStats = useCallback(() => adminDriverApi.stats(token).then((res) => setStats(res.data)).catch(() => {}), [token])
  const loadList = useCallback(async (f) => {
    setList((l) => ({ ...l, loading: true, error: '' }))
    try {
      const res = await adminDriverApi.list({ ...f, pageSize: PAGE_SIZE }, token)
      setList({ rows: res.data, total: res.total, totalPages: Math.max(1, res.totalPages), loading: false, error: '' })
    } catch (e) { setList({ rows: [], total: 0, totalPages: 1, loading: false, error: t(e.message || 'Could not load applications.') }) }
  }, [token])

  useEffect(() => { if (isAdmin) loadStats() }, [isAdmin, loadStats])
  useEffect(() => { if (isAdmin) loadList(filters) }, [isAdmin, filters, loadList])
  // Search once the admin stops typing.
  useEffect(() => {
    const timer = window.setTimeout(() => setFilters((f) => (f.search === typed.trim() ? f : { ...f, search: typed.trim(), page: 1 })), 300)
    return () => window.clearTimeout(timer)
  }, [typed])
  // New applications arriving (see useAdminAlerts) refresh the page.
  useEffect(() => {
    const refresh = () => { loadStats(); loadList(filters) }
    window.addEventListener(ADMIN_ALERT_EVENT, refresh)
    return () => window.removeEventListener(ADMIN_ALERT_EVENT, refresh)
  }, [filters, loadStats, loadList])

  if (!signedIn) return <div className="page inner-page dv-page"><SignInPrompt openAuth={openAuth} message={t('Sign in with an admin account to review driver applications.')} /></div>
  if (!isAdmin) return <div className="page inner-page dv-page"><div className="dv-panel dv-empty"><span><Lock size={22} /></span><b>{t('Admins only')}</b><small>{t('Your account does not have access to this page.')}</small></div></div>

  const change = (patch) => setFilters((f) => ({ ...f, ...patch, page: patch.page ?? 1 }))
  const n = (v) => (stats ? formatNumber(v || 0) : '–')
  const cards = [
    { key: '', icon: Users, label: 'Total driver applications', value: n(stats?.total), tone: 'neutral' },
    { key: 'PENDING', icon: Clock3, label: 'Pending applications', value: n(stats?.pending), tone: stats?.pending ? 'amber' : 'neutral' },
    { key: 'UNDER_REVIEW', icon: Search, label: 'Under review', value: n(stats?.underReview), tone: 'blue' },
    { key: 'APPROVED', icon: BadgeCheck, label: 'Approved drivers', value: n(stats?.approved), tone: 'green' },
    { key: 'REJECTED', icon: CircleX, label: 'Rejected applications', value: n(stats?.rejected), tone: 'red' },
  ]
  const hasFilters = filters.search || filters.status || filters.from || filters.to

  return <div className="page inner-page dv-page dv-admin">
    <section className="dv-hero">
      <div>
        <button type="button" className="dv-back" onClick={() => navigate('Admin')}><ArrowLeft size={15} /> {t('Admin panel')}</button>
        <span className="dv-eyebrow"><ShieldCheck size={14} /> {t('ADMIN · DRIVERS')}</span>
        <h1>{t('Driver verification.')}</h1>
        <p>{t('Review driver registrations, check their licence and NID documents, and approve, reject or ask for corrections.')}</p>
      </div>
      <div className="dv-hero-side">
        {stats?.resubmissionRequired > 0 && <span className="dv-hero-note"><FileWarning size={14} /> {t('{n} waiting for driver corrections', { n: stats.resubmissionRequired })}</span>}
        <button type="button" className="button adm-ghost" onClick={() => { loadStats(); loadList(filters) }}><RefreshCw size={15} /> {t('Refresh')}</button>
      </div>
    </section>

    <div className="dv-stats">
      {cards.map(({ key, icon: Icon, label, value, tone }) => <button key={label} type="button" className={`dv-stat tone-${tone}${filters.status === key ? ' current' : ''}`}
        onClick={() => change({ status: key })} aria-pressed={filters.status === key}>
        <span className="dv-stat-icon"><Icon size={19} /></span>
        <span><small>{t(label)}</small><b>{value}</b></span>
      </button>)}
    </div>

    <section className="dv-panel">
      <div className="dv-toolbar">
        <label className="dv-search"><Search size={16} aria-hidden="true" /><span className="sr-only">{t('Search')}</span>
          <input value={typed} onChange={(e) => setTyped(e.target.value)} placeholder={t('Search by driver name or email')} /></label>
        <label className="dv-filter"><span>{t('Status')}</span>
          <select className="dv-input" value={filters.status} onChange={(e) => change({ status: e.target.value })}>
            <option value="">{t('All statuses')}</option>
            {STATUSES.map((s) => <option key={s} value={s}>{t(STATUS_LABELS[s])}</option>)}
          </select></label>
        <label className="dv-filter"><span>{t('Submitted from')}</span><input className="dv-input" type="date" value={filters.from} max={filters.to || undefined} onChange={(e) => change({ from: e.target.value })} /></label>
        <label className="dv-filter"><span>{t('to')}</span><input className="dv-input" type="date" value={filters.to} min={filters.from || undefined} onChange={(e) => change({ to: e.target.value })} /></label>
        <label className="dv-filter"><span>{t('Sort')}</span>
          <select className="dv-input" value={filters.sort} onChange={(e) => change({ sort: e.target.value })}>
            <option value="newest">{t('Newest first')}</option><option value="oldest">{t('Oldest first')}</option>
          </select></label>
        {hasFilters && <button type="button" className="dv-link" onClick={() => { setTyped(''); setFilters({ search: '', status: '', from: '', to: '', sort: filters.sort, page: 1 }) }}>{t('Clear filters')}</button>}
      </div>
      <p className="dv-count" aria-live="polite">{list.loading ? t('Loading…') : t('{n} applications', { n: list.total })}</p>
      {list.error && <div className="dv-alert danger" role="alert"><CircleAlert size={16} /><span>{list.error}</span></div>}
      <DriverApplicationTable rows={list.rows} loading={list.loading} onOpen={(id) => navigate('Driver application', { route: id })}
        emptyText={hasFilters ? t('Try a different search or filter.') : t('New driver registrations will appear here.')} />
      {list.totalPages > 1 && <nav className="dv-pager" aria-label={t('Pages')}>
        <button type="button" className="dv-chip" disabled={filters.page <= 1} onClick={() => change({ page: filters.page - 1 })}><ChevronLeft size={14} /> {t('Previous')}</button>
        <span>{t('Page {p} of {n}', { p: filters.page, n: list.totalPages })}</span>
        <button type="button" className="dv-chip" disabled={filters.page >= list.totalPages} onClick={() => change({ page: filters.page + 1 })}>{t('Next')} <ChevronRight size={14} /></button>
      </nav>}
    </section>
  </div>
}
