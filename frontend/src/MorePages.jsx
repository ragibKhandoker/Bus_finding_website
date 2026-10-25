import React, { useCallback, useEffect, useState } from 'react'
import { ArrowRight, Bell, Check, ChevronRight, CreditCard, MapPin, MessageCircle, Navigation, Plus, RefreshCw, Send, ShieldCheck, Trash2, Users, Wallet } from 'lucide-react'
import './more.css'
import { apiRequest } from './api'
import { formatDate, formatDateTime, formatNumber, formatTime, t, toEnglish } from './i18n'

const api = (path, token, options = {}) => apiRequest(path, { ...options, token })
const json = (value) => JSON.stringify(value)
const valueOf = (item) => item?._id || item?.id || ''
// Server messages are in English; the ones with a Bangla entry are shown translated.
const err = (error) => t(error?.message || 'Request failed')
function Heading({ icon: Icon, eyebrow, title, subtitle }) {
  return <header className="extra-heading"><span><Icon size={20} /></span><div><small>{eyebrow}</small><h1>{title}</h1><p>{subtitle}</p></div></header>
}
function Feedback({ error, success }) {
  return <>{error && <div className="extra-feedback error">{error}</div>}{success && <div className="extra-feedback success">{success}</div>}</>
}
function Loading({ label = 'Loading…' }) { return <div className="extra-loading"><i />{t(label)}</div> }
function RequireSignIn({ openAuth }) { return <div className="extra-panel sign-in-panel"><ShieldCheck size={22} /><div><b>{t('Sign in to continue')}</b><p>{t('This feature uses your SmartCommute account.')}</p></div><button className="extra-button" onClick={openAuth}>{t('Sign in')} <ArrowRight size={14} /></button></div> }

export function FriendsPage({ token, user, signedIn, openAuth, onChat, notify }) {
  const [friends, setFriends] = useState([])
  const [requests, setRequests] = useState([])
  const [friendCode, setFriendCode] = useState('')
  const [searchCode, setSearchCode] = useState('')
  const [searchedUser, setSearchedUser] = useState(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const load = useCallback(async () => {
    if (!signedIn) return
    setLoading(true)
    try {
      const [f, r, c] = await Promise.all([
        api('/auth/friends', token), api('/auth/pending-friend-requests', token), api('/auth/friend-code', token),
      ])
      setFriends(f.friends || f.data || (Array.isArray(f) ? f : []))
      setRequests(r.requests || [])
      setFriendCode(c.friendCode || '')
      setError('')
    } catch (e) { setError(err(e)) } finally { setLoading(false) }
  }, [signedIn, token])
  useEffect(() => { load() }, [load])
  const find = async (event) => {
    event.preventDefault()
    setError(''); setSearchedUser(null)
    try { setSearchedUser(await api(`/auth/search-friend/${encodeURIComponent(searchCode.trim())}`, token)) } catch (e) { setError(err(e)) }
  }
  const sendRequest = async () => {
    try { await api('/auth/send-friend-request', token, { method: 'POST', body: json({ toUserId: valueOf(searchedUser) }) }); notify(t('Friend request sent.')); setSearchedUser(null); setSearchCode('') }
    catch (e) { setError(err(e)) }
  }
  const resolveRequest = async (requestId, accepted) => {
    try { await api(`/auth/${accepted ? 'accept' : 'reject'}-friend-request/${requestId}`, token, { method: 'PUT' }); notify(accepted ? t('Friend request accepted.') : t('Friend request declined.')); load() }
    catch (e) { setError(err(e)) }
  }
  if (!signedIn) return <section className="extra-page"><Heading icon={Users} eyebrow={t('YOUR PEOPLE')} title={t('Friends')} subtitle={t('Keep your commute crew close.')} /><RequireSignIn openAuth={openAuth} /></section>
  return <section className="extra-page">
    <Heading icon={Users} eyebrow={t('YOUR PEOPLE')} title={t('Friends')} subtitle={t('Find your friends and plan the next journey together.')} />
    <Feedback error={error} />
    <div className="extra-grid friends-layout">
      <article className="extra-panel friend-code-card"><small>{t('YOUR FRIEND CODE')}</small><strong>{friendCode || '—'}</strong><p>{t('Share this code so friends can find and add you.')}</p><form onSubmit={find}><input required value={searchCode} onChange={(e) => setSearchCode(e.target.value)} placeholder={t('Enter a friend\'s code')} /><button className="extra-button" type="submit">{t('Find')} <ArrowRight size={14} /></button></form>{searchedUser && <div className="person-result"><span className="extra-avatar">{searchedUser.name?.[0]?.toUpperCase()}</span><div><b>{searchedUser.name}</b><small>{searchedUser.email}</small></div><button className="extra-button" onClick={sendRequest}>{t('Add friend')} <Plus size={13} /></button></div>}</article>
      <article className="extra-panel"><div className="extra-panel-title"><div><small>{t('INCOMING')}</small><h2>{t('Friend requests')} <span>{formatNumber(requests.length)}</span></h2></div><button className="extra-icon-button" onClick={load} aria-label={t('Refresh')}><RefreshCw size={15} /></button></div>{loading ? <Loading /> : requests.length ? requests.map((request) => { const sender = request.fromUserId || request.from || request.sender || request.user || request; const id = request._id || request.id; return <div className="person-row" key={id}><span className="extra-avatar">{sender.name?.[0]?.toUpperCase() || 'F'}</span><div><b>{sender.name || t('SmartCommute member')}</b><small>{sender.email || t('Wants to connect')}</small></div><button className="extra-button" onClick={() => resolveRequest(id, true)}>{t('Accept')} <Check size={13} /></button><button className="extra-icon-button danger" onClick={() => resolveRequest(id, false)} title={t('Decline')}><Trash2 size={14} /></button></div> }) : <p className="extra-empty-copy">{t('No pending requests.')}</p>}</article>
    </div>
    <article className="extra-panel extra-list-panel"><div className="extra-panel-title"><div><small>{t('YOUR COMMUNITY')}</small><h2>{t('Friends')} <span>{formatNumber(friends.length)}</span></h2></div><button className="extra-icon-button" onClick={load} aria-label={t('Refresh')}><RefreshCw size={15} /></button></div>{loading ? <Loading /> : friends.length ? friends.map((friend) => <div className="person-row" key={valueOf(friend)}><span className="extra-avatar">{friend.name?.[0]?.toUpperCase() || 'F'}</span><div><b>{friend.name || t('SmartCommute member')}</b><small>{friend.email || t('Friend')}</small></div><button className="extra-button secondary" onClick={() => onChat(friend)}><MessageCircle size={14} /> {t('Message')}</button></div>) : <p className="extra-empty-copy">{t('Your friend list is waiting to grow. Add a friend with their code above.')}</p>}</article>
  </section>
}

export function ChatPage({ token, user, signedIn, openAuth, notify }) {
  const [chats, setChats] = useState([])
  const [chatId, setChatId] = useState('')
  const [contact, setContact] = useState(null)
  const [messages, setMessages] = useState([])
  const [otherId, setOtherId] = useState('')
  const [text, setText] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const loadChats = useCallback(async () => {
    if (!signedIn) return
    setLoading(true)
    try { const data = await api('/api/chat', token); setChats(data.chats || []); setError('') }
    catch (e) { setError(err(e)) } finally { setLoading(false) }
  }, [signedIn, token])
  useEffect(() => { loadChats() }, [loadChats])
  useEffect(() => {
    const contactId = sessionStorage.getItem('chat_contact_id')
    if (!contactId || !signedIn) return
    sessionStorage.removeItem('chat_contact_id')
    setOtherId(contactId)
    api(`/api/chat/${encodeURIComponent(contactId)}`, token)
      .then((data) => { setChatId(data.chat._id); setContact(data.chat.participants?.find((p) => valueOf(p) !== String(user.id))); setMessages(data.chat.messages || []) })
      .catch((e) => setError(err(e)))
  }, [signedIn, token, user?.id])
  const openChat = async (id, person) => {
    setError('')
    try {
      const data = await api(`/api/chat/${encodeURIComponent(id)}`, token)
      setChatId(data.chat._id); setContact(person || data.chat.participants?.find((p) => valueOf(p) !== String(user.id)))
      setMessages(data.chat.messages || [])
      const refreshed = await api(`/api/chat/messages/${data.chat._id}`, token)
      setMessages(refreshed.messages || [])
      loadChats()
    } catch (e) { setError(err(e)) }
  }
  useEffect(() => {
    if (!chatId) return undefined
    const timer = window.setInterval(async () => {
      try { const data = await api(`/api/chat/messages/${chatId}`, token); setMessages(data.messages || []) } catch {}
    }, 6000)
    return () => window.clearInterval(timer)
  }, [chatId, token])
  const start = async (event) => {
    event.preventDefault()
    if (!otherId.trim()) return
    try { const data = await api(`/api/chat/${encodeURIComponent(otherId.trim())}`, token); setChatId(data.chat._id); setContact(data.chat.participants?.find((p) => valueOf(p) !== String(user.id))); setMessages(data.chat.messages || []); setOtherId(''); setError('') }
    catch (e) { setError(err(e)) }
  }
  const send = async (event) => {
    event.preventDefault()
    if (!text.trim() || !chatId) return
    const sending = text.trim(); setText('')
    try { await api('/api/chat/send', token, { method: 'POST', body: json({ chatId, text: sending }) }); const data = await api(`/api/chat/messages/${chatId}`, token); setMessages(data.messages || []); loadChats() }
    catch (e) { setText(sending); setError(err(e)) }
  }
  if (!signedIn) return <section className="extra-page"><Heading icon={MessageCircle} eyebrow={t('KEEP IN TOUCH')} title={t('Messages')} subtitle={t('Pick up a conversation with a friend.')} /><RequireSignIn openAuth={openAuth} /></section>
  return <section className="extra-page">
    <Heading icon={MessageCircle} eyebrow={t('KEEP IN TOUCH')} title={t('Messages')} subtitle={t('Chat with your SmartCommute friends.')} />
    <Feedback error={error} />
    <div className="chat-layout extra-panel">
      <aside className="chat-sidebar"><div className="extra-panel-title"><div><small>{t('INBOX')}</small><h2>{t('Conversations')}</h2></div><button className="extra-icon-button" onClick={loadChats} aria-label={t('Refresh')}><RefreshCw size={14} /></button></div><form className="start-chat-form" onSubmit={start}><input value={otherId} onChange={(e) => setOtherId(e.target.value)} placeholder={t('Friend user ID')} required /><button className="extra-icon-button" title={t('Start chat')}><Plus size={16} /></button></form>{loading ? <Loading /> : chats.length ? chats.map((chat) => <button key={chat._id} className={`chat-contact ${chat._id === chatId ? 'current' : ''}`} onClick={() => openChat(valueOf(chat.otherParticipant), chat.otherParticipant)}><span className="extra-avatar">{chat.otherParticipant?.name?.[0]?.toUpperCase() || 'C'}</span><span><b>{chat.otherParticipant?.name || t('Contact')}</b><small>{chat.lastMessage?.text || t('Start a conversation')}</small></span>{chat.unreadCount > 0 && <i>{formatNumber(chat.unreadCount)}</i>}</button>) : <p className="extra-empty-copy">{t('No conversations yet. Start one with a friend\'s user ID.')}</p>}</aside>
      <div className="chat-main">{chatId ? <><div className="chat-title"><span className="extra-avatar">{contact?.name?.[0]?.toUpperCase() || 'C'}</span><div><b>{contact?.name || t('Conversation')}</b><small>{contact?.email || t('SmartCommute chat')}</small></div></div><div className="chat-messages">{messages.map((message, index) => { const sender = typeof message.senderId === 'object' ? valueOf(message.senderId) : String(message.senderId); const own = sender === String(user.id); return <div className={`message-bubble ${own ? 'mine' : ''}`} key={message._id || index}><span>{message.text}</span><small>{formatTime(message.createdAt)}</small></div> })}{!messages.length && <p className="extra-empty-copy">{t('Say hello to start the conversation.')}</p>}</div><form className="chat-compose" onSubmit={send}><input value={text} onChange={(e) => setText(e.target.value)} placeholder={t('Write a message…')} /><button className="extra-button" disabled={!text.trim()}><Send size={14} /> {t('Send')}</button></form></> : <div className="chat-placeholder"><MessageCircle size={27} /><b>{t('Your conversations, all in one place.')}</b><span>{t('Choose a conversation or start one with a friend\'s user ID.')}</span></div>}</div>
    </div>
  </section>
}

export function NotificationsPage({ user, signedIn, openAuth, notify }) {
  const [items, setItems] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const userId = user?.id || user?._id
  const load = useCallback(async () => {
    if (!signedIn || !userId) return
    setLoading(true)
    try { const data = await api(`/api/notifications/user/${encodeURIComponent(userId)}?limit=50`); setItems(data.data || []); setError('') }
    catch (e) { setError(err(e)) } finally { setLoading(false) }
  }, [signedIn, userId])
  useEffect(() => { load() }, [load])
  const act = async (path, options, message) => {
    try { await api(path, null, options); notify(message); load() } catch (e) { setError(err(e)) }
  }
  if (!signedIn) return <section className="extra-page"><Heading icon={Bell} eyebrow={t('UPDATES FOR YOU')} title={t('Notifications')} subtitle={t('Stay on top of the things that matter to your commute.')} /><RequireSignIn openAuth={openAuth} /></section>
  return <section className="extra-page"><Heading icon={Bell} eyebrow={t('UPDATES FOR YOU')} title={t('Notifications')} subtitle={t('Stay on top of the things that matter to your commute.')} /><Feedback error={error} /><article className="extra-panel notification-panel"><div className="extra-panel-title"><div><small>{t('YOUR INBOX')}</small><h2>{t('Notifications')} <span>{t('{count} unread', { count: items.filter((item) => !item.isRead).length })}</span></h2></div><div className="notification-actions"><button className="extra-button secondary" onClick={() => act('/api/notifications/read-all', { method: 'PATCH', body: json({ userId }) }, t('Notifications marked as read.'))}>{t('Mark all read')}</button><button className="extra-icon-button danger" title={t('Clear all')} onClick={() => window.confirm(t('Delete all notifications?')) && act(`/api/notifications/user/${userId}`, { method: 'DELETE' }, t('Notifications cleared.'))}><Trash2 size={15} /></button></div></div>{loading ? <Loading /> : items.length ? items.map((item) => <div className={`notification-row ${item.isRead ? '' : 'unread'}`} key={item._id}><span className="notification-dot">{item.type === 'error' ? '!' : <Bell size={15} />}</span><div className="notification-copy"><b>{t(item.title)}</b><p>{t(item.message)}</p><small>{formatDateTime(item.createdAt)}</small></div><div className="notification-row-actions">{!item.isRead && <button className="extra-icon-button" title={t('Mark as read')} onClick={() => act(`/api/notifications/read/${item._id}`, { method: 'PATCH' }, t('Marked as read.'))}><Check size={15} /></button>}<button className="extra-icon-button danger" title={t('Delete')} onClick={() => act(`/api/notifications/${item._id}`, { method: 'DELETE' }, t('Notification removed.'))}><Trash2 size={14} /></button></div></div>) : <p className="extra-empty-copy">{t('You are all caught up. No notifications.')}</p>}</article></section>
}

export function OffersPage({ token, signedIn, openAuth, notify }) {
  const [offers, setOffers] = useState(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const load = useCallback(async () => { if (!signedIn) return; setLoading(true); try { setOffers(await api('/offers/user', token)); setError('') } catch (e) { setError(err(e)) } finally { setLoading(false) } }, [signedIn, token])
  useEffect(() => { load() }, [load])
  const convert = async (type) => {
    const amount = Number(offers?.[type] || 0)
    if (!amount) return
    try { const result = await api(`/offers/use-${type}`, token, { method: 'POST', body: json({ amount }) }); setOffers(result.offers); notify(t(result.message || '{type} converted to wallet balance.', { type: t(type) })) }
    catch (e) { setError(err(e)) }
  }
  if (!signedIn) return <section className="extra-page"><Heading icon={CreditCard} eyebrow={t('LITTLE PERKS, BETTER TRIPS')} title={t('Offers & rewards')} subtitle={t('See your rewards and convert eligible offers into wallet balance.')} /><RequireSignIn openAuth={openAuth} /></section>
  return <section className="extra-page"><Heading icon={CreditCard} eyebrow={t('LITTLE PERKS, BETTER TRIPS')} title={t('Offers & rewards')} subtitle={t('See your rewards and convert eligible offers into wallet balance.')} /><Feedback error={error} />{loading ? <Loading /> : offers ? <div className="offer-grid">{[['cashback','Cashback','Cashback earned from eligible trips.'],['coupon','Coupons','Available coupon value for your account.'],['discount','Discounts','Taken off your fare automatically when you pay for a bus in the SmartCommute app.']].map(([key,label,description]) => <article className="extra-panel offer-card" key={key}><span className="offer-symbol">{key === 'cashback' ? '৳' : key === 'coupon' ? '✳' : '%'}</span><small>{t('YOUR {label}', { label: t(label).toUpperCase() })}</small><strong>৳{formatNumber(offers[key])}</strong><p>{t(description)}</p>{key !== 'discount' && <button className="extra-button" disabled={!Number(offers[key])} onClick={() => convert(key)}>{t('Move to wallet')} <ArrowRight size={14} /></button>}</article>)}</div> : <div className="extra-panel extra-empty-copy">{error || t('No offers are available.')} <button className="extra-button secondary" onClick={load}>{t('Try again')}</button></div>}</section>
}

export function GpayPage({ token, signedIn, openAuth, notify }) {
  const [account, setAccount] = useState(null)
  const [pin, setPin] = useState('')
  const [amount, setAmount] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const load = useCallback(async () => { if (!signedIn) return; setLoading(true); try { const data = await api('/gpay/balance', token); setAccount(data.data); setError('') } catch (e) { setAccount(null); setError(/not found/i.test(err(e)) ? '' : err(e)) } finally { setLoading(false) } }, [signedIn, token])
  useEffect(() => { load() }, [load])
  const call = async (path, body, success) => {
    setError('')
    try { const data = await api(path, token, { method: 'POST', ...(body ? { body: json(body) } : {}) }); if (data.data) setAccount(data.data); notify(success); if (path.endsWith('/recharge') || path.endsWith('/deduct')) load(); return data }
    catch (e) { setError(err(e)); return null }
  }
  const recharge = async (e) => { e.preventDefault(); const data = await call('/gpay/recharge', { amount: Number(amount) }, t('Wallet recharged from GPay.')); if (data) setAmount('') }
  const pay = async (e) => { e.preventDefault(); const data = await call('/gpay/deduct', { amount: Number(amount) }, t('GPay payment completed.')); if (data) setAmount('') }
  if (!signedIn) return <section className="extra-page"><Heading icon={Wallet} eyebrow={t('YOUR DIGITAL WALLET')} title={t('GPay & recharge')} subtitle={t('Manage the in-app GPay balance and move funds to your commute wallet.')} /><RequireSignIn openAuth={openAuth} /></section>
  return <section className="extra-page"><Heading icon={Wallet} eyebrow={t('YOUR DIGITAL WALLET')} title={t('GPay & recharge')} subtitle={t('Manage the in-app GPay balance and move funds to your commute wallet.')} /><Feedback error={error} />{loading ? <Loading /> : <div className="gpay-layout"><article className="extra-panel gpay-card"><small>{t('GPAY BALANCE')}</small><strong>৳{formatNumber(account?.balance)}</strong><span>{account ? t('GPay account active') : t('No GPay account connected')}</span>{!account && <div className="pin-row"><input aria-label={t('GPay PIN')} inputMode="numeric" maxLength={4} value={pin} onChange={(e) => setPin(e.target.value)} placeholder={t('4-digit PIN')} /><button className="extra-button" onClick={async () => { const result = await call('/gpay/register', null, t('GPay account created. Save your new PIN securely.')); if (result?.data) { setAccount(result.data); setPin(result.data.displayCode || '') } }}>{t('Create account')}</button><button className="extra-button secondary" disabled={!pin} onClick={() => call('/gpay/login', { code: pin }, t('GPay unlocked.'))}>{t('Unlock')}</button></div>}{account?.displayCode && <p className="gpay-notice">{t('This API returns a display PIN at registration. Keep it private; the web client does not save it.')}</p>}</article>{account && <article className="extra-panel gpay-actions"><h2>{t('Move funds')}</h2><p>{t('These are in-app stored-value operations. No external payment provider is connected.')}</p><form onSubmit={recharge}><label>{t('AMOUNT (BDT)')}<input required min="1" type="number" value={amount} onChange={(e) => setAmount(e.target.value)} placeholder={t('e.g. 500')} /></label><button className="extra-button">{t('Recharge commute wallet')} <ArrowRight size={14} /></button></form><button className="extra-button secondary" onClick={pay}>{t('Pay from GPay balance')}</button></article>}</div>}</section>
}

export function MapPage({ token, user, signedIn, openAuth, notify }) {
  const [query, setQuery] = useState('')
  const [results, setResults] = useState([])
  const [origin, setOrigin] = useState('')
  const [destination, setDestination] = useState('')
  const [route, setRoute] = useState(null)
  const [error, setError] = useState('')
  const [busyId, setBusyId] = useState('')
  const [tripBusId, setTripBusId] = useState('')
  const search = async (e) => {
    e.preventDefault(); setError('')
    const [stopResult, placeResult] = await Promise.allSettled([
      api(`/api/stops/search?${new URLSearchParams({ q: toEnglish(query) })}`),
      api(`/api/geocoding/search?${new URLSearchParams({ q: query, limit: '5' })}`),
    ])
    const stopPlaces = stopResult.status === 'fulfilled' ? (stopResult.value.data || []).map((stop) => ({ display_name: stop.name, lat: stop.lat, lon: stop.lng, place_id: `stop-${stop.name}` })) : []
    const places = placeResult.status === 'fulfilled' ? (placeResult.value.data || []) : []
    const combined = [...stopPlaces, ...places]
    setResults(combined)
    if (!combined.length) setError(placeResult.status === 'rejected' ? err(placeResult.reason) : t('No places were found. Try a nearby landmark.'))
  }
  const routeSearch = async (e) => {
    e.preventDefault(); setError(''); setRoute(null)
    try {
      const locatePlace = async (name) => {
        const stop = await api(`/api/stops/coordinates?${new URLSearchParams({ name: toEnglish(name) })}`)
        if (stop.data) return { data: { ...stop.data, display_name: stop.data.name, lon: stop.data.lng } }
        return api(`/api/geocoding/geocode?${new URLSearchParams({ q: name })}`)
      }
      const [a,b] = await Promise.all([locatePlace(origin), locatePlace(destination)])
      const x = a.data, y = b.data
      const data = await api(`/api/geocoding/distance?${new URLSearchParams({ sourceLat: x.lat, sourceLon: x.lon, destLat: y.lat, destLon: y.lon })}`)
      setRoute({ source: x, destination: y, ...data.data })
      if (signedIn) api('/api/fares', token, { method: 'POST', body: json({ userId: user.id, source: origin, destination, distance: data.data.distance, sourceCoordinates: { lat: x.lat, lon: x.lon }, destinationCoordinates: { lat: y.lat, lon: y.lon } }) }).catch(() => {})
    } catch (ex) { setError(err(ex)) }
  }
  const locate = () => {
    if (!navigator.geolocation) return setError(t('Location is not available in this browser.'))
    navigator.geolocation.getCurrentPosition(({ coords }) => { const place = { display_name: t('Your current location'), lat: coords.latitude, lon: coords.longitude }; setResults([place]) }, () => setError(t('Location permission was denied. Search for a place instead.')))
  }
  const busInfo = async (id) => {
    if (!signedIn) return openAuth()
    try { const data = await api(`/individual-bus/${encodeURIComponent(id.trim())}`, token); notify(t('Bus {code}: {taken} of {total} seats taken.', { code: data.data.busCode, taken: data.data.currentPassengerCount, total: data.data.totalPassengerCapacity })) }
    catch (ex) { setError(err(ex)) }
  }
  const capacityAction = async (id, action) => {
    if (!signedIn) return openAuth()
    try { const data = await api(`/individual-bus/${action}`, token, { method: 'POST', body: json({ busId: id }) }); notify(t(data.message || 'Passenger count updated.')) }
    catch (ex) { setError(err(ex)) }
  }
  const logRouteTrip = async () => {
    if (!signedIn) return openAuth()
    if (!route || !tripBusId.trim()) return setError(t('Enter the bus ID before logging this route as a trip.'))
    try {
      await api('/trip/add', token, { method: 'POST', body: json({ busId: tripBusId.trim(), busName: 'Dhaka commute', distance: route.distance, fare: route.fare, source: origin, destination }) })
      notify(t('Trip logged. Your journey now appears in trip history.'))
    } catch (ex) { setError(err(ex)) }
  }
  const sendSOS = async (serviceType, serviceName) => {
    if (!signedIn) return openAuth()
    // serviceName goes to the server in English; only what the user reads is translated.
    if (!window.confirm(t('Send a {service} alert with your location to all other SmartCommute users? This cannot be undone.', { service: t(serviceName) }))) return
    const send = (coords) => api('/api/notifications/sos', null, { method: 'POST', body: json({ userId: user.id, userName: user.name, source: coords ? 'Current location' : 'Location not shared', latitude: coords?.latitude, longitude: coords?.longitude, serviceType, serviceName }) })
    if (!navigator.geolocation) return setError(t('Location is not available in this browser; no alert was sent.'))
    navigator.geolocation.getCurrentPosition(async ({ coords }) => {
      try { const result = await send(coords); notify(t(result.message || '{service} alert sent.', { service: t(serviceName) })) }
      catch (ex) { setError(err(ex)) }
    }, () => setError(t('Location permission was denied; no alert was sent.')))
  }
  return <section className="extra-page"><Heading icon={MapPin} eyebrow={t('PLACES, STOPS & DISTANCE')} title={t('Explore the map')} subtitle={t('Search Dhaka places, estimate route distance and fare, or check a known active bus ID.')} /><Feedback error={error} /><div className="map-grid"><article className="extra-panel map-search-panel"><div className="extra-panel-title"><div><small>{t('FIND A PLACE')}</small><h2>{t('Search the map')}</h2></div><button className="extra-icon-button" onClick={locate} title={t('Use my location')}><Navigation size={15} /></button></div><form className="map-form" onSubmit={search}><input required value={query} onChange={(e) => setQuery(e.target.value)} placeholder={t('Search a place in Dhaka')} /><button className="extra-button">{t('Search')} <ArrowRight size={14} /></button></form>{results.map((place, i) => <button className="place-result" key={place.place_id || i} onClick={() => setQuery(place.display_name)}><MapPin size={16} /><span><b>{t(place.display_name?.split(',')[0])}</b><small>{t(place.display_name)}</small></span><a href={`https://www.openstreetmap.org/?mlat=${place.lat}&mlon=${place.lon}#map=16/${place.lat}/${place.lon}`} target="_blank" rel="noreferrer" onClick={(e) => e.stopPropagation()}>{t('Map')} <ChevronRight size={13} /></a></button>)}<div className="map-embed"><iframe title={t('OpenStreetMap view of Dhaka')} src="https://www.openstreetmap.org/export/embed.html?bbox=90.30%2C23.65%2C90.48%2C23.87&layer=mapnik&marker=23.78%2C90.38" loading="lazy" /></div><small className="map-attribution">{t('Map data © OpenStreetMap contributors')}</small></article><div className="map-side"><article className="extra-panel"><div className="extra-panel-title"><div><small>{t('ROUTE PLANNER')}</small><h2>{t('Distance & estimated fare')}</h2></div></div><form className="map-form map-form-stacked" onSubmit={routeSearch}><input required value={origin} onChange={(e) => setOrigin(e.target.value)} placeholder={t('From (e.g. Uttara)')} /><input required value={destination} onChange={(e) => setDestination(e.target.value)} placeholder={t('To (e.g. Motijheel)')} /><button className="extra-button">{t('Estimate route')} <ArrowRight size={14} /></button></form>{route && <div className="route-estimate"><b>{t('{distance} km', { distance: formatNumber(route.distance, { maximumFractionDigits: 2 }) })}</b><span>{t('Estimated fare')} ৳{formatNumber(Math.round(route.fare || 0))}</span><small>{t('Based on the backend’s ৳30/km estimate. Actual fares may differ.')}</small>{signedIn && <><input className="route-bus-id" value={tripBusId} onChange={(e) => setTripBusId(e.target.value)} placeholder={t('Bus ID (required to log trip)')} /><button className="extra-button" onClick={logRouteTrip}>{t('Log this trip')}</button></>}</div>}</article><article className="extra-panel"><div className="extra-panel-title"><div><small>{t('ACTIVE BUS LOOKUP')}</small><h2>{t('Check passenger count')}</h2></div></div><p className="extra-help">{t('Enter a known individual bus ID. This API reports occupancy only; it does not provide GPS tracking.')}</p><form className="map-form map-form-stacked" onSubmit={(e) => { e.preventDefault(); busInfo(busyId) }}><input required value={busyId} onChange={(e) => setBusyId(e.target.value)} placeholder={t('Individual bus ID')} /><button className="extra-button secondary">{t('Look up bus')}</button></form>{signedIn && <div className="map-capacity-actions"><button className="extra-button secondary" onClick={() => capacityAction(busyId, 'board')}>{t('Board bus')}</button><button className="extra-button secondary" onClick={() => capacityAction(busyId, 'end-trip')}>{t('End trip')}</button></div>}</article><article className="extra-panel sos-panel"><div><small>{t('NEED URGENT HELP?')}</small><h2>{t('Emergency alert')}</h2><p>{t('This API notifies all other SmartCommute users and shares your current coordinates.')}</p></div><div className="map-capacity-actions"><button className="extra-button sos-button" onClick={() => sendSOS('trigger','Emergency')}>{t('SOS')}</button><button className="extra-button secondary" onClick={() => sendSOS('ambulance','Ambulance')}>{t('Ambulance')}</button><button className="extra-button secondary" onClick={() => sendSOS('police','Police')}>{t('Police')}</button><button className="extra-button secondary" onClick={() => sendSOS('fire','Fire')}>{t('Fire')}</button></div></article></div></div></section>
}

export function VerificationPage({ token, user, signedIn, openAuth, notify }) {
  const [status, setStatus] = useState(null)
  const [queue, setQueue] = useState([])
  const [form, setForm] = useState({ institutionName: '', institutionId: '', gmail: '' })
  const [image, setImage] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const admin = user?.role?.toLowerCase() === 'admin'
  const load = useCallback(async () => {
    if (!signedIn) return
    setLoading(true)
    try { const data = await api('/verify/my-status', token); setStatus(data.data || null); if (admin) { const all = await api('/verify/all', token); setQueue(all.data || []) } setError('') }
    catch (e) { setError(err(e)) } finally { setLoading(false) }
  }, [signedIn, token, admin])
  useEffect(() => { load() }, [load])
  const submit = async (e) => { e.preventDefault(); setError(''); try { let imageUrl; if (image) { const uploaded = await api('/verify/upload-image', token, { method: 'POST', body: json({ image }) }); imageUrl = uploaded.imageUrl } const result = await api('/verify/submit', token, { method: 'POST', body: json({ ...form, imageUrl }) }); setStatus(result.data); notify(t('Verification submitted for review.')) } catch (ex) { setError(err(ex)) } }
  const review = async (id, approved) => { try { await api(`/verify/${approved ? 'approve' : 'reject'}`, token, { method: 'POST', body: json({ verificationId: id }) }); notify(approved ? t('Verification approved.') : t('Verification rejected.')); load() } catch (ex) { setError(err(ex)) } }
  if (!signedIn) return <section className="extra-page"><Heading icon={ShieldCheck} eyebrow={t('ELIGIBILITY & PASSES')} title={t('Student verification')} subtitle={t('Submit an institution ID for review and check your verification status.')} /><RequireSignIn openAuth={openAuth} /></section>
  return <section className="extra-page"><Heading icon={ShieldCheck} eyebrow={t('ELIGIBILITY & PASSES')} title={admin ? t('Verification review') : t('Student verification')} subtitle={admin ? t('Review student pass applications.') : t('Submit an institution ID for review and check your status.')} /><Feedback error={error} />{loading ? <Loading /> : admin ? <article className="extra-panel extra-list-panel"><div className="extra-panel-title"><div><small>{t('ADMIN REVIEW')}</small><h2>{t('Requests')} <span>{formatNumber(queue.length)}</span></h2></div><button className="extra-icon-button" onClick={load}><RefreshCw size={15} /></button></div>{queue.length ? queue.map((item) => <div className="verification-row" key={item._id}><div><b>{item.userName || item.userId?.name || t('Applicant')} · {item.institutionName}</b><small>{item.userEmail} · {t('ID')} {item.institutionId} · {item.gmail} · {t(item.status)}</small></div>{item.imageUrl && <a href={item.imageUrl} target="_blank" rel="noreferrer">{t('View ID')}</a>}{item.status === 'hold' && <><button className="extra-button" onClick={() => review(item._id, true)}>{t('Approve')}</button><button className="extra-button secondary" onClick={() => review(item._id, false)}>{t('Reject')}</button></>}</div>) : <p className="extra-empty-copy">{t('No verification requests found.')}</p>}</article> : <div className="verify-layout"><article className="extra-panel verify-status"><small>{t('YOUR STATUS')}</small><b className={`status-badge ${status?.status || 'none'}`}>{status?.status ? t(status.status) : t('Not submitted')}</b><p>{status?.status === 'approved' ? t('Your student status has been verified.') : status?.status === 'hold' ? t('Your request is waiting for review.') : status?.status === 'rejected' ? t('Your last request was rejected. You can submit a new one.') : t('Student pass verification helps confirm your eligibility.')}</p>{status?.institutionName && <small>{status.institutionName} · {status.institutionId}</small>}</article>{(!status || status.status === 'rejected') && <form className="extra-panel verify-form" onSubmit={submit}><h2>{t('Submit for review')}</h2><label>{t('Institution name')}<input required value={form.institutionName} onChange={(e) => setForm({ ...form, institutionName: e.target.value })} placeholder={t('University or institution')} /></label><label>{t('Institution ID')}<input required value={form.institutionId} onChange={(e) => setForm({ ...form, institutionId: e.target.value })} placeholder={t('Student ID number')} /></label><label>{t('Institution email')}<input required type="email" value={form.gmail} onChange={(e) => setForm({ ...form, gmail: e.target.value })} placeholder="you@institution.edu" /></label><label>{t('Upload ID image (optional)')}<input type="file" accept="image/png,image/jpeg" onChange={(e) => { const file = e.target.files?.[0]; if (file && file.size <= 3_000_000) { const reader = new FileReader(); reader.onload = () => setImage(String(reader.result)); reader.readAsDataURL(file) } else if (file) setError(t('Choose an image smaller than 3 MB.')) }} /></label>{image && <img className="verify-preview" src={image} alt={t('Verification ID preview')} />}<button className="extra-button">{t('Submit verification')} <ArrowRight size={14} /></button><small className="verify-note">{t('Image uploads require the backend’s Cloudinary configuration.')}</small></form>}</div>}</section>
}

export function BusReviews({ bus, user, signedIn, notify }) {
  const [expanded, setExpanded] = useState(false)
  const [reviews, setReviews] = useState([])
  const [comment, setComment] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const load = async () => {
    setLoading(true)
    try { const data = await api(`/api/reviews/bus/${encodeURIComponent(bus._id)}`); setReviews(data.reviews || []); setError('') }
    catch (e) { setError(err(e)) } finally { setLoading(false) }
  }
  const toggle = () => {
    if (!expanded) load()
    setExpanded(!expanded)
  }
  const submit = async (e) => {
    e.preventDefault()
    if (!signedIn) return setError(t('Sign in to leave a review.'))
    try { await api('/api/reviews', null, { method: 'POST', body: json({ busId: bus._id, userId: user.id, userName: user.name, comment }) }); setComment(''); notify(t('Review posted.')); load() }
    catch (e2) { setError(err(e2)) }
  }
  const reactToReview = async (reviewId, reaction) => {
    if (!signedIn) return setError(t('Sign in to react to a review.'))
    try { await api(`/api/reviews/${reviewId}/${reaction}`, null, { method: 'POST', body: json({ userId: user.id }) }); load() }
    catch (e) { setError(err(e)) }
  }
  return <div className="bus-reviews"><button className="review-toggle" onClick={toggle}>{expanded ? t('Hide community reviews') : t('Community reviews')} <ChevronRight size={13} /></button>{expanded && <div className="review-content">{error && <div className="extra-feedback error">{error}</div>}{signedIn && <form className="review-compose" onSubmit={submit}><input required maxLength={1000} value={comment} onChange={(e) => setComment(e.target.value)} placeholder={t('Review {name}…', { name: bus.busName ? t(bus.busName) : t('this bus') })} /><button className="extra-button">{t('Post review')}</button></form>}{loading ? <div className="extra-loading"><i />{t('Loading reviews…')}</div> : reviews.length ? reviews.map((review) => <div className="review-row" key={review._id}><span className="extra-avatar">{review.userName?.[0]?.toUpperCase() || 'R'}</span><div><b>{review.userName || t('Commuter')}</b><p>{review.comment}</p><small>{formatDate(review.createdAt)}</small></div><div className="review-reactions"><button onClick={() => reactToReview(review._id, 'like')}>♥ {formatNumber(review.likes)}</button><button onClick={() => reactToReview(review._id, 'dislike')}>− {formatNumber(review.dislikes)}</button></div></div>) : !error && <p className="extra-empty-copy">{t('No reviews yet. Share the first one.')}</p>}</div>}</div>
}
