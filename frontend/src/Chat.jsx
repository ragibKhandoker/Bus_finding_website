import React, { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import { ArrowLeft, BadgeCheck, Check, CheckCheck, MessageCircle, RefreshCw, Send, Users } from 'lucide-react'
import { apiRequest } from './api'
import { formatDate, formatNumber, formatTime, t } from './i18n'
import { Feedback, Heading, Loading, RequireSignIn } from './MorePages'

// One-to-one chat with friends. The server decides who the sender is and who may see a conversation; this page
// only shows what it is given. Message text is rendered as plain text, never as HTML.

export const MAX_MESSAGE_LENGTH = 2000
// How often an open Messages page checks for new messages, and how many checks between conversation-list refreshes.
const POLL_MS = 5000
const LIST_EVERY = 3

const errorText = (error) => t(error?.message || 'Something went wrong. Please try again.')
const idOf = (item) => String(item?._id || item?.id || '')
const post = (path, token, body) => apiRequest(path, { token, method: 'POST', ...(body ? { body: JSON.stringify(body) } : {}) })
const query = (params) => {
  const search = new URLSearchParams(Object.entries(params).filter(([, value]) => value))
  return search.toString() ? `?${search}` : ''
}
export const chatApi = {
  list: (token) => apiRequest('/api/chat', { token }),
  unreadCounts: (token) => apiRequest('/api/chat/unread-counts', { token }),
  open: (friendId, token) => apiRequest(`/api/chat/${encodeURIComponent(friendId)}`, { token }),
  messages: (chatId, token, { before, after, limit } = {}) => apiRequest(`/api/chat/messages/${encodeURIComponent(chatId)}${query({ before, after, limit })}`, { token }),
  send: (chatId, content, token) => post('/api/chat/send', token, { chatId, content }),
  markRead: (chatId, upTo, token) => post(`/api/chat/${encodeURIComponent(chatId)}/read${query({ upTo })}`, token),
}

// Oldest first, by time then id (the server's order), with each message once however often it was fetched.
export function mergeMessages(current, incoming) {
  const byId = new Map(current.map((m) => [idOf(m), m]))
  incoming.forEach((m) => byId.set(idOf(m), m))
  return [...byId.values()].sort((a, b) => new Date(a.createdAt) - new Date(b.createdAt) || (idOf(a) < idOf(b) ? -1 : idOf(a) > idOf(b) ? 1 : 0))
}

// The selected conversation lives in the address (?chat=…), so a refresh reopens it; ?with=<friend id> is how
// the Friends page asks for a conversation with someone.
const readUrl = () => { const params = new URLSearchParams(window.location.search); return { chat: params.get('chat'), with: params.get('with') } }
const writeUrl = (chatId) => {
  if (window.location.pathname !== '/messages') return
  const next = chatId ? `/messages?chat=${encodeURIComponent(chatId)}` : '/messages'
  if (window.location.pathname + window.location.search !== next) window.history.replaceState({ page: 'Messages' }, '', next)
}

const isToday = (value) => new Date(value).toDateString() === new Date().toDateString()
const shortWhen = (value) => value ? (isToday(value) ? formatTime(value) : formatDate(value, { day: 'numeric', month: 'short' })) : ''

function Avatar({ person }) {
  return <span className="extra-avatar" aria-hidden="true">{person?.name?.trim()?.[0]?.toUpperCase() || '?'}</span>
}

function ConversationRow({ chat, me, current, onOpen }) {
  const other = chat.otherParticipant || {}
  const last = chat.lastMessage
  const mine = last && String(last.senderId) === String(me)
  const unread = chat.unreadCount || 0
  return <button type="button" className={`chat-contact${current ? ' current' : ''}${unread ? ' has-unread' : ''}`} onClick={() => onOpen(chat)} aria-current={current || undefined}
    aria-label={unread ? t('{name}, {count} unread messages', { name: other.name, count: unread }) : other.name}>
    <Avatar person={other} />
    <span>
      <span className="chat-contact-top"><b>{other.name || t('SmartCommute member')}</b><time>{shortWhen(last?.createdAt || chat.lastMessageAt || chat.createdAt)}</time></span>
      <small>{last ? `${mine ? `${t('You')}: ` : ''}${last.content ?? last.text}` : t('No messages yet')}</small>
    </span>
    {unread > 0 && <i data-testid="unread-badge">{formatNumber(unread)}</i>}
  </button>
}

export function ChatPage({ token, user, signedIn, openAuth, onFindFriends }) {
  const me = String(user?.id || user?._id || '')
  const [chats, setChats] = useState([])
  const [listStatus, setListStatus] = useState('loading') // loading | ready | error
  const [selected, setSelected] = useState(null) // { chatId, other }
  const [messages, setMessages] = useState([])
  const [hasMore, setHasMore] = useState(false)
  const [messageStatus, setMessageStatus] = useState('idle') // idle | loading | ready | error
  const [loadingOlder, setLoadingOlder] = useState(false)
  const [text, setText] = useState('')
  const [sending, setSending] = useState(false)
  const [error, setError] = useState('')
  const [composeError, setComposeError] = useState('')

  const scroller = useRef(null)
  const scrollPlan = useRef(null) // { bottom: true } or { keepFrom: previous scrollHeight }
  const selectedRef = useRef(null)
  selectedRef.current = selected
  const messagesRef = useRef(messages)
  messagesRef.current = messages
  const listRun = useRef(0)

  const loadChats = useCallback(async ({ quiet = false } = {}) => {
    if (!signedIn) return []
    const run = ++listRun.current
    if (!quiet) setListStatus('loading')
    try {
      const data = await chatApi.list(token)
      if (run !== listRun.current) return null
      setChats(data.chats || [])
      setListStatus('ready')
      return data.chats || []
    } catch (e) {
      if (run !== listRun.current) return null
      if (!quiet) { setListStatus('error'); setError(errorText(e)) }
      return null
    }
  }, [signedIn, token])

  // Marks what is on screen as read, then takes the server's word for the conversation's unread count.
  const markRead = useCallback(async (chatId, list) => {
    const latestIncoming = [...list].reverse().find((m) => String(m.senderId) !== me && !m.read)
    if (!latestIncoming) return
    try {
      await chatApi.markRead(chatId, idOf(list[list.length - 1]), token)
      setChats((current) => current.map((c) => idOf(c) === chatId ? { ...c, unreadCount: 0 } : c))
      setMessages((current) => current.map((m) => String(m.senderId) !== me ? { ...m, read: true } : m))
      loadChats({ quiet: true })
    } catch { /* stays unread; the next check tries again */ }
  }, [me, token, loadChats])

  const showChat = useCallback(async (chatId, other, firstPage) => {
    setSelected({ chatId, other })
    writeUrl(chatId)
    setComposeError('')
    setError('')
    scrollPlan.current = { bottom: true }
    if (firstPage) {
      setMessages(firstPage.messages); setHasMore(firstPage.hasMore); setMessageStatus('ready')
      markRead(chatId, firstPage.messages)
      return
    }
    setMessages([]); setMessageStatus('loading')
    try {
      const data = await chatApi.messages(chatId, token)
      if (selectedRef.current?.chatId !== chatId) return
      const page = mergeMessages([], data.messages || [])
      scrollPlan.current = { bottom: true }
      setMessages(page); setHasMore(Boolean(data.hasMore)); setMessageStatus('ready')
      markRead(chatId, page)
    } catch (e) {
      if (selectedRef.current?.chatId !== chatId) return
      setMessageStatus('error'); setError(errorText(e))
    }
  }, [token, markRead])

  // Opens (or creates) the conversation with a friend; the server checks the friendship.
  const openWithFriend = useCallback(async (friendId) => {
    setMessageStatus('loading')
    try {
      const data = await chatApi.open(friendId, token)
      const chat = data.chat
      await showChat(idOf(chat), chat.otherParticipant, { messages: mergeMessages([], chat.messages || []), hasMore: Boolean(chat.hasMoreMessages) })
      loadChats({ quiet: true })
    } catch (e) {
      setMessageStatus('idle'); setError(errorText(e)); writeUrl(null)
    }
  }, [token, showChat, loadChats])

  // First load: the conversation list, then whatever the address asks for.
  useEffect(() => {
    if (!signedIn) return
    let active = true
    const wanted = readUrl()
    loadChats().then((list) => {
      if (!active) return
      if (wanted.with) openWithFriend(wanted.with)
      else if (wanted.chat && list) {
        const chat = list.find((c) => idOf(c) === wanted.chat)
        if (chat) showChat(idOf(chat), chat.otherParticipant)
        else writeUrl(null)
      }
    })
    return () => { active = false }
  }, [signedIn, loadChats, openWithFriend, showChat])

  // While the page is open and visible, pick up new messages in the open conversation and refresh the list.
  useEffect(() => {
    if (!signedIn) return undefined
    let ticks = 0
    const timer = window.setInterval(async () => {
      if (document.hidden) return
      ticks += 1
      const open = selectedRef.current
      if (open) {
        const list = messagesRef.current
        const last = list[list.length - 1]
        try {
          const data = await chatApi.messages(open.chatId, token, last ? { after: idOf(last) } : {})
          if (selectedRef.current?.chatId !== open.chatId || !(data.messages || []).length) return
          const box = scroller.current
          const nearBottom = !box || box.scrollHeight - box.scrollTop - box.clientHeight < 80
          if (nearBottom) scrollPlan.current = { bottom: true }
          const merged = mergeMessages(messagesRef.current, data.messages)
          setMessages(merged)
          markRead(open.chatId, merged)
        } catch { /* try again on the next tick */ }
      }
      if (ticks % LIST_EVERY === 0) loadChats({ quiet: true })
    }, POLL_MS)
    return () => window.clearInterval(timer)
  }, [signedIn, token, loadChats, markRead])

  // Scroll after messages render: to the bottom when opening, sending or receiving at the bottom; when older
  // messages are added on top, keep the reader where they were.
  useLayoutEffect(() => {
    const box = scroller.current
    const plan = scrollPlan.current
    if (!box || !plan) return
    if (plan.bottom) box.scrollTop = box.scrollHeight
    else if (plan.keepFrom !== undefined) box.scrollTop += box.scrollHeight - plan.keepFrom
    scrollPlan.current = null
  }, [messages])

  const loadOlder = async () => {
    if (!selected || loadingOlder || !messages.length) return
    setLoadingOlder(true)
    try {
      const data = await chatApi.messages(selected.chatId, token, { before: idOf(messages[0]) })
      scrollPlan.current = { keepFrom: scroller.current?.scrollHeight ?? 0 }
      setMessages((current) => mergeMessages(current, data.messages || []))
      setHasMore(Boolean(data.hasMore))
    } catch (e) { setError(errorText(e)) } finally { setLoadingOlder(false) }
  }

  const send = async (event) => {
    event?.preventDefault()
    if (!selected || sending) return
    const content = text.trim()
    if (!content) { setComposeError(t('Message cannot be empty.')); return }
    if (content.length > MAX_MESSAGE_LENGTH) { setComposeError(t('A message can be at most 2000 characters.')); return }
    setSending(true); setComposeError('')
    try {
      const data = await chatApi.send(selected.chatId, content, token)
      scrollPlan.current = { bottom: true }
      setMessages((current) => mergeMessages(current, [data.message]))
      setText('')
      loadChats({ quiet: true })
    } catch (e) {
      setComposeError(errorText(e))
    } finally { setSending(false) }
  }

  // Enter sends; Shift+Enter starts a new line.
  const onComposerKey = (event) => {
    if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) { event.preventDefault(); send() }
  }

  const back = () => { setSelected(null); setMessages([]); setMessageStatus('idle'); writeUrl(null) }

  const heading = <Heading icon={MessageCircle} eyebrow={t('KEEP IN TOUCH')} title={t('Messages')} subtitle={t('Chat with your SmartCommute friends.')} />
  if (!signedIn) return <section className="extra-page">{heading}<RequireSignIn openAuth={openAuth} /></section>

  const other = selected?.other
  const totalUnread = chats.reduce((sum, c) => sum + (c.unreadCount || 0), 0)
  let lastDay = ''

  return <section className="extra-page chat-page">
    {heading}
    <Feedback error={error} />
    <div className={`chat-layout extra-panel${selected ? ' has-chat' : ''}`}>
      <aside className="chat-sidebar" aria-label={t('Conversations')}>
        <div className="extra-panel-title">
          <div><small>{t('INBOX')}</small><h2>{t('Conversations')} {totalUnread > 0 && <span data-testid="total-unread">{t('{count} unread', { count: totalUnread })}</span>}</h2></div>
          <button type="button" className="extra-icon-button" onClick={() => loadChats()} aria-label={t('Refresh')} title={t('Refresh')}><RefreshCw size={15} /></button>
        </div>
        {onFindFriends && <button type="button" className="extra-button secondary chat-new" onClick={onFindFriends}><Users size={14} /> {t('Message a friend')}</button>}
        {listStatus === 'loading' ? <Loading />
          : listStatus === 'error' ? <div className="chat-empty"><p>{t('Could not load your conversations.')}</p><button type="button" className="extra-button" onClick={() => loadChats()}><RefreshCw size={14} /> {t('Try again')}</button></div>
            : chats.length ? <div className="chat-contact-list">{chats.map((chat) => <ConversationRow key={idOf(chat)} chat={chat} me={me} current={idOf(chat) === selected?.chatId} onOpen={(c) => showChat(idOf(c), c.otherParticipant)} />)}</div>
              : <div className="chat-empty"><p>{t('No conversations yet. Open Friends and press Message next to a friend to start one.')}</p></div>}
      </aside>

      <div className="chat-main">
        {selected ? <>
          <header className="chat-title">
            <button type="button" className="extra-icon-button chat-back" onClick={back} aria-label={t('Back to conversations')}><ArrowLeft size={17} /></button>
            <Avatar person={other} />
            <div>
              <b className="friend-name">{other?.name || t('Conversation')}{other?.verified && <BadgeCheck size={14} aria-label={t('Verified student')} />}</b>
              <small>{other?.friendCode ? `${t('Friend')} · ${t('Friend code')}: ${other.friendCode}` : t('SmartCommute chat')}</small>
            </div>
          </header>
          <div className="chat-messages" ref={scroller} role="log" aria-live="polite" aria-label={t('Messages with {name}', { name: other?.name || '' })}>
            {hasMore && <button type="button" className="extra-button secondary chat-older" onClick={loadOlder} disabled={loadingOlder}>{loadingOlder ? t('Loading…') : t('Load older messages')}</button>}
            {messageStatus === 'loading' ? <Loading />
              : messageStatus === 'error' ? <div className="chat-empty"><p>{t('Could not load messages.')}</p><button type="button" className="extra-button" onClick={() => showChat(selected.chatId, other)}><RefreshCw size={14} /> {t('Try again')}</button></div>
                : !messages.length ? <p className="extra-empty-copy chat-say-hello">{t('Say hello to start the conversation.')}</p>
                  : messages.map((message) => {
                    const own = String(message.senderId) === me
                    const day = new Date(message.createdAt).toDateString()
                    const divider = day !== lastDay ? <div className="chat-day" key={`day-${day}`}><span>{isToday(message.createdAt) ? t('Today') : formatDate(message.createdAt)}</span></div> : null
                    lastDay = day
                    return <React.Fragment key={idOf(message)}>
                      {divider}
                      <div className={`message-bubble${own ? ' mine' : ''}`} data-testid="message">
                        <span className="message-text">{message.content ?? message.text}</span>
                        <small>
                          <b>{own ? t('You') : (message.senderName || other?.name)}</b> · <time dateTime={message.createdAt}>{formatTime(message.createdAt)}</time>
                          {own && (message.read ? <CheckCheck size={13} aria-label={t('Seen')} /> : <Check size={13} aria-label={t('Sent')} />)}
                        </small>
                      </div>
                    </React.Fragment>
                  })}
          </div>
          <form className="chat-compose" onSubmit={send}>
            <label htmlFor="chat-message" className="sr-only">{t('Write a message…')}</label>
            <textarea id="chat-message" rows={1} value={text} maxLength={MAX_MESSAGE_LENGTH} placeholder={t('Write a message…')} disabled={messageStatus !== 'ready'}
              onChange={(e) => { setText(e.target.value); if (composeError) setComposeError('') }} onKeyDown={onComposerKey}
              aria-invalid={Boolean(composeError) || undefined} aria-describedby="chat-compose-help" />
            <button type="submit" className="extra-button" disabled={sending || !text.trim() || messageStatus !== 'ready'}>{sending ? t('Sending…') : <><Send size={14} /> {t('Send')}</>}</button>
          </form>
          <p id="chat-compose-help" className={composeError ? 'chat-compose-help error' : 'chat-compose-help'} role={composeError ? 'alert' : undefined}>
            {composeError || (text.length > MAX_MESSAGE_LENGTH - 200 ? t('{count} characters left', { count: MAX_MESSAGE_LENGTH - text.length }) : t('Enter to send · Shift+Enter for a new line'))}
          </p>
        </> : <div className="chat-placeholder"><MessageCircle size={30} /><b>{t('Your conversations, all in one place.')}</b><span>{t('Choose a conversation, or open Friends and press Message next to a friend.')}</span></div>}
      </div>
    </div>
  </section>
}
