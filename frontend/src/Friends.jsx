import React, { useCallback, useEffect, useRef, useState } from 'react'
import { BadgeCheck, Check, Clock, Copy, MessageCircle, RefreshCw, Search, Send, UserPlus, Users, X } from 'lucide-react'
import { apiRequest } from './api'
import { formatDate, formatNumber, t } from './i18n'
import { Feedback, Heading, Loading, RequireSignIn } from './MorePages'

// Friends page: your friend code, search by code, incoming and sent requests, and your friends.
// Every call carries the sign-in token; the server takes the user from it, so nothing here sends a user id.
const errorText = (error) => t(error?.message || 'Something went wrong. Please try again.')
const idOf = (item) => String(item?._id || item?.id || '')
// The friend code alphabet is A-Z and 0-9; anything else typed is dropped and letters are upper-cased.
export const cleanCode = (value) => value.toUpperCase().replace(/[^A-Z0-9]/g, '').slice(0, 5)
export const isValidCode = (value) => /^[A-Z0-9]{5}$/.test(value)

// The lists are short, so one page of the largest size the server allows holds them all.
const LIST_SIZE = 100
const post = (path, token, body) => apiRequest(path, { token, method: 'POST', ...(body ? { body: JSON.stringify(body) } : {}) })
export const friendsApi = {
  myCode: (token) => apiRequest('/auth/friend-code', { token }),
  search: (code, token) => apiRequest(`/auth/search-friend?code=${encodeURIComponent(code)}`, { token }),
  sendRequest: (friendCode, token) => post('/friends/request', token, { friendCode }),
  incoming: (token) => apiRequest(`/friends/requests/incoming?pageSize=${LIST_SIZE}`, { token }),
  sent: (token) => apiRequest(`/friends/requests/sent?pageSize=${LIST_SIZE}`, { token }),
  friends: (token) => apiRequest(`/friends?pageSize=${LIST_SIZE}`, { token }),
  accept: (requestId, token) => post(`/friends/requests/${encodeURIComponent(requestId)}/accept`, token),
  reject: (requestId, token) => post(`/friends/requests/${encodeURIComponent(requestId)}/reject`, token),
  // Unread chat messages keyed by the friend's user id, for the badge on each Message button.
  chatUnread: (token) => apiRequest('/api/chat/unread-counts', { token }),
}

// Keeps the first entry for each id, so a person never appears twice in a list.
const uniqueById = (items) => [...new Map(items.map((item) => [idOf(item), item])).values()]

function Avatar({ person }) {
  return <span className="extra-avatar" aria-hidden="true">{person?.name?.trim()?.[0]?.toUpperCase() || '?'}</span>
}

function PersonName({ person }) {
  return <b className="friend-name">{person?.name || t('SmartCommute member')}{person?.verified && <BadgeCheck size={13} aria-label={t('Verified student')} />}</b>
}

function Section({ eyebrow, title, count, onRefresh, refreshing, children, className = '' }) {
  return <article className={`extra-panel ${className}`} aria-busy={refreshing || undefined}>
    <div className="extra-panel-title">
      <div><small>{eyebrow}</small><h2>{title} {count !== undefined && <span>{formatNumber(count)}</span>}</h2></div>
      {onRefresh && <button type="button" className="extra-icon-button" onClick={onRefresh} disabled={refreshing} aria-label={t('Refresh')} title={t('Refresh')}><RefreshCw size={15} /></button>}
    </div>
    {children}
  </article>
}

// What the search result offers, depending on how the two people are already connected.
function SearchResult({ result, busy, onAdd, onAnswer, onChat }) {
  const { user, relationship, requestId } = result
  let action
  if (relationship === 'self') action = <p className="friend-note">{t('This is your own friend code.')}</p>
  else if (relationship === 'friends') action = <><span className="friend-status accepted"><Check size={12} /> {t('Already friends')}</span><button type="button" className="extra-button secondary" onClick={() => onChat(user)}><MessageCircle size={14} /> {t('Message')}</button></>
  else if (relationship === 'request_sent') action = <span className="friend-status pending"><Clock size={12} /> {t('Request sent')}</span>
  else if (relationship === 'request_received') action = <>
    <p className="friend-note">{t('{name} already sent you a request.', { name: user.name })}</p>
    <button type="button" className="extra-button" disabled={busy} onClick={() => onAnswer(requestId, true)}><Check size={13} /> {t('Accept')}</button>
    <button type="button" className="extra-button secondary" disabled={busy} onClick={() => onAnswer(requestId, false)}>{t('Decline')}</button>
  </>
  else action = <button type="button" className="extra-button" disabled={busy} onClick={onAdd}>{busy ? t('Sending…') : <><UserPlus size={14} /> {t('Add friend')}</>}</button>
  return <div className="friend-result" data-testid="search-result">
    <div className="friend-result-person">
      <Avatar person={user} />
      <div>
        <PersonName person={user} />
        <small>{t('Friend code')}: {user.friendCode}{user.memberSince && <> · {t('Member since {date}', { date: formatDate(user.memberSince) })}</>}</small>
        {user.bio && <p>{user.bio}</p>}
      </div>
    </div>
    <div className="friend-result-actions">{action}</div>
  </div>
}

export function FriendsPage({ token, signedIn, openAuth, onChat, notify }) {
  const [code, setCode] = useState('')
  const [incoming, setIncoming] = useState([])
  const [sent, setSent] = useState([])
  const [friends, setFriends] = useState([])
  const [unreadByFriend, setUnreadByFriend] = useState({})
  const [status, setStatus] = useState('loading') // loading | ready | error
  const [refreshing, setRefreshing] = useState(false)
  const [error, setError] = useState('')
  const [copied, setCopied] = useState(false)

  const [query, setQuery] = useState('')
  const [queryError, setQueryError] = useState('')
  const [searching, setSearching] = useState(false)
  const [result, setResult] = useState(null) // { user, relationship, requestId } or { notFound: true }
  const [sending, setSending] = useState(false)
  // Requests with an accept or decline in flight; their buttons stay disabled until it finishes.
  const [answering, setAnswering] = useState(() => new Set())
  const answeringRef = useRef(new Set())
  const loadRun = useRef(0)
  const copiedTimer = useRef(0)

  // Loads every section in one go. Only the latest call's answer is used, so quick repeat refreshes cannot
  // put an older list on screen.
  const load = useCallback(async ({ quiet = false } = {}) => {
    if (!signedIn) return
    const run = ++loadRun.current
    if (quiet) setRefreshing(true)
    else setStatus('loading')
    try {
      const [mine, inbox, outbox, list, unread] = await Promise.all([
        friendsApi.myCode(token), friendsApi.incoming(token), friendsApi.sent(token), friendsApi.friends(token),
        // The badges are extra; the page still works if they cannot be loaded.
        friendsApi.chatUnread(token).catch(() => null),
      ])
      if (run !== loadRun.current) return
      setCode(mine.friendCode || '')
      setUnreadByFriend(unread?.unreadCounts || {})
      setIncoming(uniqueById(inbox.data || []))
      setSent(uniqueById(outbox.data || []))
      setFriends(uniqueById(list.data || []))
      setError('')
      setStatus('ready')
    } catch (e) {
      if (run !== loadRun.current) return
      setError(errorText(e))
      setStatus((current) => current === 'ready' ? 'ready' : 'error')
    } finally {
      if (run === loadRun.current) setRefreshing(false)
    }
  }, [signedIn, token])
  useEffect(() => { load() }, [load])
  useEffect(() => () => window.clearTimeout(copiedTimer.current), [])

  const copyCode = async () => {
    if (!code) return
    try {
      await navigator.clipboard.writeText(code)
      setCopied(true)
      window.clearTimeout(copiedTimer.current)
      copiedTimer.current = window.setTimeout(() => setCopied(false), 2500)
      notify(t('Friend code copied.'))
    } catch {
      notify(t('Your friend code is {code}', { code }))
    }
  }

  const search = async (event) => {
    event.preventDefault()
    setResult(null)
    if (!isValidCode(query)) { setQueryError(t('A friend code is exactly 5 letters or digits.')); return }
    setQueryError('')
    setSearching(true)
    try {
      const data = await friendsApi.search(query, token)
      setResult({ user: data.user, relationship: data.relationship, requestId: data.requestId })
    } catch (e) {
      if (/not found/i.test(e?.message || '')) setResult({ notFound: true })
      else setQueryError(errorText(e))
    } finally { setSearching(false) }
  }

  const sendRequest = async () => {
    if (sending || !result?.user) return
    setSending(true)
    try {
      const data = await friendsApi.sendRequest(result.user.friendCode, token)
      notify(t('Friend request sent.'))
      setResult((current) => current && { ...current, relationship: 'request_sent' })
      if (data.data) setSent((current) => uniqueById([data.data, ...current]))
    } catch (e) {
      setQueryError(errorText(e))
    } finally {
      setSending(false)
      load({ quiet: true })
    }
  }

  const answer = async (requestId, accepted) => {
    if (!requestId || answeringRef.current.has(requestId)) return
    answeringRef.current.add(requestId)
    setAnswering(new Set(answeringRef.current))
    try {
      await (accepted ? friendsApi.accept(requestId, token) : friendsApi.reject(requestId, token))
      notify(accepted ? t('Friend request accepted.') : t('Friend request declined.'))
      const request = incoming.find((item) => idOf(item) === requestId)
      setIncoming((current) => current.filter((item) => idOf(item) !== requestId))
      if (accepted && request?.sender) setFriends((current) => uniqueById([...current, request.sender]))
      setResult((current) => current?.requestId === requestId ? { ...current, relationship: accepted ? 'friends' : 'none', requestId: null } : current)
      setError('')
    } catch (e) {
      setError(errorText(e))
    } finally {
      answeringRef.current.delete(requestId)
      setAnswering(new Set(answeringRef.current))
      load({ quiet: true })
    }
  }

  const heading = <Heading icon={Users} eyebrow={t('YOUR PEOPLE')} title={t('Friends')} subtitle={t('Find your friends and plan the next journey together.')} />
  if (!signedIn) return <section className="extra-page">{heading}<RequireSignIn openAuth={openAuth} /></section>

  const loading = status === 'loading'
  const failed = status === 'error'
  const refresh = () => load({ quiet: true })
  const listBody = (items, empty, render) => loading ? <Loading /> : failed ? <p className="extra-empty-copy">{t('Could not load this list.')}</p> : items.length ? items.map(render) : <p className="extra-empty-copy">{empty}</p>

  return <section className="extra-page friends-page">
    {heading}
    <Feedback error={error} />
    {failed && <button type="button" className="extra-button friends-retry" onClick={() => load()}><RefreshCw size={14} /> {t('Try again')}</button>}

    <div className="extra-grid friends-layout">
      <article className="extra-panel friend-code-card">
        <small>{t('YOUR FRIEND CODE')}</small>
        <div className="friend-code-row">
          <strong aria-label={t('Your friend code is {code}', { code: code || '—' })} data-testid="my-friend-code">{loading ? '·····' : code || '—'}</strong>
          <button type="button" className="extra-button secondary" onClick={copyCode} disabled={!code}>
            {copied ? <><Check size={14} /> {t('Copied')}</> : <><Copy size={14} /> {t('Copy code')}</>}
          </button>
        </div>
        <p>{t('Share this code so friends can find and add you.')}</p>
        <span className="sr-only" role="status">{copied ? t('Friend code copied.') : ''}</span>
      </article>

      <article className="extra-panel friend-search-card">
        <small className="friend-eyebrow">{t('SEARCH FRIENDS')}</small>
        <form className="friend-search-form" onSubmit={search} noValidate>
          <label htmlFor="friend-code-search">{t("Enter a friend's code")}</label>
          <div>
            <input id="friend-code-search" value={query} maxLength={5} autoComplete="off" autoCapitalize="characters" spellCheck="false"
              placeholder="R7K2M" aria-invalid={Boolean(queryError) || undefined} aria-describedby="friend-code-hint"
              onChange={(e) => { setQuery(cleanCode(e.target.value)); setQueryError(''); setResult(null) }} />
            <button className="extra-button" type="submit" disabled={searching}>{searching ? <><i className="location-spinner light" aria-hidden="true" /> {t('Searching…')}</> : <><Search size={14} /> {t('Search')}</>}</button>
          </div>
          <small id="friend-code-hint" className={queryError ? 'friend-hint error' : 'friend-hint'}>{queryError || t('5 letters or digits, e.g. R7K2M')}</small>
        </form>
        <div aria-live="polite">
          {result?.notFound && <p className="friend-not-found" data-testid="not-found">{t('User not found')}</p>}
          {result?.user && <SearchResult result={result} busy={sending || answering.has(result.requestId)} onAdd={sendRequest} onAnswer={answer} onChat={onChat} />}
        </div>
      </article>
    </div>

    <div className="extra-grid friends-layout friends-requests">
      <Section eyebrow={t('INCOMING')} title={t('Friend requests')} count={loading ? undefined : incoming.length} onRefresh={refresh} refreshing={refreshing}>
        {listBody(incoming, t('No pending requests.'), (request) => {
          const id = idOf(request)
          const busy = answering.has(id)
          return <div className="person-row" key={id}>
            <Avatar person={request.sender} />
            <div><PersonName person={request.sender} /><small>{t('Sent {date}', { date: formatDate(request.createdAt) })}</small></div>
            <button type="button" className="extra-button" disabled={busy} onClick={() => answer(id, true)} aria-label={t('Accept request from {name}', { name: request.sender?.name })}><Check size={13} /> {t('Accept')}</button>
            <button type="button" className="extra-button secondary" disabled={busy} onClick={() => answer(id, false)} aria-label={t('Decline request from {name}', { name: request.sender?.name })}><X size={13} /> {t('Decline')}</button>
          </div>
        })}
      </Section>

      <Section eyebrow={t('SENT')} title={t('Sent requests')} count={loading ? undefined : sent.length} onRefresh={refresh} refreshing={refreshing}>
        {listBody(sent, t('No requests waiting for an answer.'), (request) => <div className="person-row" key={idOf(request)}>
          <Avatar person={request.receiver} />
          <div><PersonName person={request.receiver} /><small>{t('Sent {date}', { date: formatDate(request.createdAt) })}</small></div>
          <span className="friend-status pending"><Send size={11} /> {t('Pending')}</span>
        </div>)}
      </Section>
    </div>

    <Section className="extra-list-panel" eyebrow={t('YOUR COMMUNITY')} title={t('My friends')} count={loading ? undefined : friends.length} onRefresh={refresh} refreshing={refreshing}>
      {listBody(friends, t('Your friend list is waiting to grow. Add a friend with their code above.'), (friend) => <div className="person-row" key={idOf(friend)}>
        <Avatar person={friend} />
        <div><PersonName person={friend} /><small>{friend.friendsSince ? t('Friends since {date}', { date: formatDate(friend.friendsSince) }) : `${t('Friend code')}: ${friend.friendCode}`}</small></div>
        <button type="button" className="extra-button secondary" onClick={() => onChat(friend)} aria-label={unreadByFriend[idOf(friend)] ? t('Message {name}, {count} unread', { name: friend.name, count: unreadByFriend[idOf(friend)] }) : t('Message {name}', { name: friend.name })}>
          <MessageCircle size={14} /> {t('Message')}{unreadByFriend[idOf(friend)] > 0 && <i className="friend-unread" data-testid="friend-unread">{formatNumber(unreadByFriend[idOf(friend)])}</i>}
        </button>
      </div>)}
    </Section>
  </section>
}
