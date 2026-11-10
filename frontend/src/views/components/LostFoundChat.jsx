import React, { useCallback, useEffect, useRef, useState } from 'react'
import { Check, CircleAlert, ImagePlus, Loader2, MessageCircle, Send, SmilePlus, X } from 'lucide-react'
import { formatDate, formatNumber, t } from '../../models/i18n'
import { imageSrc, lostFoundApi, readImage } from '../../models/lostFound'

// F17 messaging: a private conversation about one Lost & Found report between its author and one other person.

const errorText = (error) => t(error?.message || 'Something went wrong. Please try again.')
const POLL_MS = 5000

const time = (iso) => formatDate(iso, { day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit' })

export const REACTIONS = ['👍', '❤️', '😂', '😮', '😢', '🙏']

// The message box. Enter sends; Shift+Enter starts a new line. A picture can be added with the paperclip, or pasted
// straight in (Ctrl+V after taking a screenshot); it is shown before sending.
function Composer({ onSend, placeholder, busy, autoFocus = false }) {
  const [text, setText] = useState('')
  const [image, setImage] = useState('')
  const [error, setError] = useState('')
  const [sent, setSent] = useState(false)
  const fileInput = useRef(null)
  useEffect(() => {
    if (!sent) return
    const id = window.setTimeout(() => setSent(false), 2500)
    return () => window.clearTimeout(id)
  }, [sent])
  const addFile = async (file) => {
    try { setImage(await readImage(file)); setError('') } catch (e) { setError(e.message) }
  }
  const onPaste = (event) => {
    const file = [...(event.clipboardData?.items || [])].find((item) => item.kind === 'file' && item.type.startsWith('image/'))?.getAsFile()
    if (file) { event.preventDefault(); addFile(file) }
  }
  const submit = async (event) => {
    event?.preventDefault()
    if (busy) return
    const value = text.trim()
    if (!value && !image) { setError(t('Write a message or add a picture.')); return }
    if (value.length > 1000) { setError(t('A message can be at most 1000 characters.')); return }
    setError('')
    try { await onSend(value, image); setText(''); setImage(''); setSent(true) } catch (e) { setError(errorText(e)) }
  }
  return <form className="lfc-compose" onSubmit={submit} noValidate>
    {image && <div className="lfc-attach-preview">
      <img src={image} alt={t('Picture to send')} />
      <span>{t('Picture ready to send')}</span>
      <button type="button" className="lf-icon" onClick={() => setImage('')} aria-label={t('Remove picture')}><X size={15} /></button>
    </div>}
    <div className="lfc-compose-row">
      <input ref={fileInput} type="file" accept="image/jpeg,image/png,image/webp" hidden aria-label={t('Add a picture or screenshot')}
        onChange={(e) => { const file = e.target.files?.[0]; e.target.value = ''; if (file) addFile(file) }} />
      <button type="button" className="lfc-attach" onClick={() => fileInput.current?.click()} disabled={busy} aria-label={t('Add a picture or screenshot')} title={t('Add a picture or screenshot (or paste one with Ctrl+V)')}>
        <ImagePlus size={18} /></button>
      <textarea value={text} onChange={(e) => { setText(e.target.value); setError('') }} rows={2} maxLength={1000} placeholder={placeholder}
        aria-label={t('Message')} autoFocus={autoFocus} onPaste={onPaste}
        onKeyDown={(e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); submit() } }} />
      <button type="submit" className="button button-green" disabled={busy} aria-label={t('Send')}>{busy ? <Loader2 size={16} className="lf-spin" /> : <Send size={16} />} <span>{t('Send')}</span></button>
    </div>
    {error && <small className="lf-error" role="alert">{error}</small>}
    {sent && !error && <small className="lfc-sent" role="status"><Check size={13} /> {t('Message sent.')}</small>}
  </form>
}

// The reactions under a message, and a small picker to add one. Tapping your own reaction again takes it away.
function Reactions({ message, onReact }) {
  const [open, setOpen] = useState(false)
  const box = useRef(null)
  useEffect(() => {
    if (!open) return
    const away = (event) => { if (!box.current?.contains(event.target)) setOpen(false) }
    document.addEventListener('pointerdown', away)
    return () => document.removeEventListener('pointerdown', away)
  }, [open])
  const react = (emoji) => { setOpen(false); onReact(message, emoji) }
  return <div className="lfc-reactions" ref={box}>
    {(message.reactions || []).map((r) => <button key={r.emoji} type="button" className={`lfc-reaction${r.mine ? ' mine' : ''}`} onClick={() => react(r.emoji)}
      aria-label={r.mine ? t('Remove your {emoji} reaction', { emoji: r.emoji }) : t('React with {emoji}', { emoji: r.emoji })} aria-pressed={r.mine}>
      {r.emoji}{r.count > 1 && <b>{formatNumber(r.count)}</b>}</button>)}
    <button type="button" className="lfc-react-add" onClick={() => setOpen(!open)} aria-label={t('Add a reaction')} aria-expanded={open}><SmilePlus size={15} /></button>
    {open && <div className="lfc-picker" role="menu" aria-label={t('Reactions')}>
      {REACTIONS.map((emoji) => <button key={emoji} type="button" role="menuitem" onClick={() => react(emoji)} aria-label={t('React with {emoji}', { emoji })}>{emoji}</button>)}
    </div>}
  </div>
}

// One conversation, refreshed every few seconds while it is open (new messages and everyone's reactions).
export function ChatThread({ conversationId, token, onRead }) {
  const [state, setState] = useState({ status: 'loading', messages: [], conversation: null })
  const [busy, setBusy] = useState(false)
  const [viewing, setViewing] = useState('')
  const list = useRef(null)
  const last = useRef(null)
  const load = useCallback(async () => {
    try {
      const res = await lostFoundApi.messages(conversationId, last.current, token)
      const fresh = res.data || []
      if (fresh.length) last.current = fresh[fresh.length - 1].createdAt
      const reactions = res.reactions || {}
      setState((s) => {
        const known = new Set(s.messages.map((m) => m.id))
        const messages = [...s.messages, ...fresh.filter((m) => !known.has(m.id))].map((m) => ({ ...m, reactions: reactions[m.id] || [] }))
        return { status: 'ready', conversation: res.conversation, messages }
      })
      onRead?.()
    } catch (e) { setState((s) => (s.messages.length ? s : { ...s, status: 'error', error: errorText(e) })) }
  }, [conversationId, token]) // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => {
    last.current = null
    setState({ status: 'loading', messages: [], conversation: null })
    load()
    const id = window.setInterval(load, POLL_MS)
    return () => window.clearInterval(id)
  }, [load])
  useEffect(() => { if (list.current) list.current.scrollTop = list.current.scrollHeight }, [state.messages.length])
  const send = async (text, image) => {
    setBusy(true)
    try {
      const res = await lostFoundApi.sendMessage(conversationId, text, image, token)
      last.current = res.data.createdAt
      setState((s) => ({ ...s, messages: [...s.messages, res.data] }))
    } finally { setBusy(false) }
  }
  const react = async (message, emoji) => {
    try {
      const res = await lostFoundApi.react(conversationId, message.id, emoji, token)
      setState((s) => ({ ...s, messages: s.messages.map((m) => (m.id === message.id ? { ...m, reactions: res.data || [] } : m)) }))
    } catch { /* the next refresh shows the real state */ }
  }
  const c = state.conversation
  if (state.status === 'loading') return <div className="lf-center"><Loader2 size={18} className="lf-spin" /> {t('Loading messages…')}</div>
  if (state.status === 'error') return <p className="lf-form-error" role="alert"><CircleAlert size={15} /> {state.error}</p>
  return <div className="lfc-thread" data-testid="lf-thread">
    {c && <p className="lfc-with">{c.iAmOwner ? t('{name} replied to your report “{item}”', { name: c.withName, item: c.itemName }) : t('You are talking to {name} about “{item}”', { name: c.withName, item: c.itemName })}</p>}
    <ol className="lfc-messages" ref={list} aria-live="polite">
      {state.messages.map((m) => <li key={m.id} className={m.mine ? 'mine' : 'theirs'} data-testid="lf-message">
        <div className="lfc-bubble">
          {m.imageUrl && <button type="button" className="lfc-photo" onClick={() => setViewing(imageSrc(m.imageUrl))} aria-label={t('Open picture')}>
            <img src={imageSrc(m.imageUrl)} alt={t('Picture in the chat')} loading="lazy" /></button>}
          {m.text && <p>{m.text}</p>}
          <small>{time(m.createdAt)}</small>
        </div>
        <Reactions message={m} onReact={react} />
      </li>)}
    </ol>
    <p className="lfc-safety">{t('Keep the chat here until you trust the other person. Meet in a busy public place to hand the item over.')}</p>
    <Composer onSend={send} busy={busy} placeholder={t('Write a message…')} />
    {viewing && <div className="lfc-viewer" role="dialog" aria-label={t('Picture')} onClick={() => setViewing('')}>
      <img src={viewing} alt={t('Picture in the chat')} />
      <button type="button" className="lf-icon" onClick={() => setViewing('')} aria-label={t('Close')}><X size={18} /></button>
    </div>}
  </div>
}

// On a report someone else made: one button that opens the chat window (a new conversation, or the one already
// started). The chat itself never sits inside the report details, so long messages cannot stretch them.
export function ContactByMessage({ report, token, onOpenChat }) {
  const [existing, setExisting] = useState(null)
  const [checking, setChecking] = useState(true)
  useEffect(() => {
    let live = true
    lostFoundApi.conversations(token)
      .then((res) => { if (live) setExisting(res.data?.find((x) => x.reportId === report.id) || null) })
      .catch(() => {}).finally(() => { if (live) setChecking(false) })
    return () => { live = false }
  }, [report.id, token])
  const label = existing ? t('Open chat with {name}', { name: report.reporterName })
    : report.reportType === 'lost' ? t('Message the owner') : t('Message the finder')
  return <div className="lfc-start-row">
    <button type="button" className="button button-green lfc-start" disabled={checking}
      onClick={() => onOpenChat?.(existing ? { id: existing.id, itemName: report.itemName } : { report, itemName: report.itemName })}>
      <MessageCircle size={16} /> {label}{existing?.unread > 0 && <em className="lfc-badge">{formatNumber(existing.unread)}</em>}
    </button>
    <small className="lfc-hint">{t('Chat inside SmartCommute. No phone number is needed.')}</small>
  </div>
}

// The chat window: an existing conversation ({ id }), or a new one about a report ({ report }) that starts with the
// first message sent.
export function ChatWindow({ chat, token }) {
  const [id, setId] = useState(chat.id || null)
  const [busy, setBusy] = useState(false)
  if (id) return <ChatThread conversationId={id} token={token} />
  const report = chat.report
  const prompt = report.reportType === 'lost'
    ? t('Hi! I think I found your {item}. ', { item: report.itemName })
    : t('Hi! I think the {item} you found is mine. ', { item: report.itemName })
  const start = async (text, image) => {
    setBusy(true)
    try { const res = await lostFoundApi.startChat(report.id, text, image, token); setId(res.data.id) } finally { setBusy(false) }
  }
  return <div className="lfc-thread" data-testid="lf-new-chat">
    <p className="lfc-with">{t('Your message goes only to {name}. No phone number is needed.', { name: report.reporterName })}</p>
    <div className="lfc-messages lfc-empty-chat">
      <MessageCircle size={28} />
      <b>{t('Start the conversation')}</b>
      <span>{t('Tip: start with something like “{text}”', { text: prompt.trim() })}</span>
    </div>
    <p className="lfc-safety">{t('Keep the chat here until you trust the other person. Meet in a busy public place to hand the item over.')}</p>
    <Composer onSend={start} busy={busy} placeholder={prompt} autoFocus />
  </div>
}

// Every conversation this user is in.
export function ConversationList({ token, onOpen, reportId = null, refreshKey = 0, onCount }) {
  const [state, setState] = useState({ status: 'loading', items: [] })
  const load = useCallback(async () => {
    try {
      const res = await lostFoundApi.conversations(token)
      setState({ status: 'ready', items: res.data || [] })
      onCount?.(res.unread || 0)
    } catch (e) { setState({ status: 'error', items: [], error: errorText(e) }) }
  }, [token]) // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { load() }, [load, refreshKey])
  const items = reportId ? state.items.filter((c) => c.reportId === reportId) : state.items
  if (state.status === 'loading') return <div className="lf-skeletons">{[0, 1].map((i) => <i key={i} />)}</div>
  if (state.status === 'error') return <p className="lf-form-error" role="alert"><CircleAlert size={15} /> {state.error}</p>
  if (!items.length) return reportId ? <p className="lfc-hint">{t('No messages about this report yet.')}</p>
    : <div className="lf-empty"><MessageCircle size={26} /><b>{t('No messages yet')}</b><span>{t('When you message someone about a report, or someone messages you about yours, it shows up here.')}</span></div>
  return <ul className="lfc-list">{items.map((c) => <li key={c.id}>
    <button type="button" onClick={() => onOpen(c)} className={c.unread ? 'unread' : ''} data-testid="lf-conversation">
      <span className={`lfc-avatar ${c.reportType}`} aria-hidden="true">{(c.withName || '?')[0].toUpperCase()}</span>
      <span className="lfc-list-text"><b>{c.withName} · <span className={`lf-type ${c.reportType}`}>{c.reportType === 'lost' ? t('LOST') : t('FOUND')}</span> {c.itemName}</b>
        <small>{c.lastMessage ? `${c.lastMessage.mine ? `${t('You')}: ` : ''}${c.lastMessage.text || `📷 ${t('Picture')}`}` : ''}</small></span>
      <span className="lfc-list-side"><small>{time(c.lastMessageAt)}</small>{c.unread > 0 && <em aria-label={t('{count} unread', { count: c.unread })}>{formatNumber(c.unread)}</em>}</span>
    </button>
  </li>)}</ul>
}
