import React, { useCallback, useEffect, useMemo, useState } from 'react'
import { createRoot } from 'react-dom/client'
import {
  ArrowDownUp, ArrowLeft, ArrowRight, ArrowUpRight, BusFront, Check, ChevronDown,
  CircleHelp, Clock3, Compass, CreditCard, Heart, History, Home, LogOut, MapPin,
  Menu, MessageCircle, Plus, Search, ShieldCheck, Sparkles, Users, Wallet, X,
} from 'lucide-react'
import './style.css'
import { BusReviews, ChatPage, FriendsPage, GpayPage, MapPage, NotificationsPage, OffersPage, VerificationPage } from './MorePages'
import { API_BASE, UNAUTHORIZED_EVENT, apiRequest as request } from './api'

const money = (value) => `৳${Number(value || 0).toLocaleString('en-BD')}`
const errorText = (error) => error?.message || 'Something went wrong. Please try again.'
const getStops = (bus) => Array.isArray(bus.stops) ? bus.stops.map((stop) => typeof stop === 'string' ? stop : stop.name).filter(Boolean) : []

function App() {
  const [user, setUser] = useState(() => { try { return JSON.parse(localStorage.getItem('sc_user') || 'null') } catch { return null } })
  const [token, setToken] = useState(() => localStorage.getItem('sc_token'))
  const [page, setPage] = useState('Home')
  const [mobileMenu, setMobileMenu] = useState(false)
  const [moreMenu, setMoreMenu] = useState(false)
  const [toast, setToast] = useState('')
  const [authMode, setAuthMode] = useState('')

  const notify = useCallback((message) => {
    setToast(message)
    window.setTimeout(() => setToast(''), 3600)
  }, [])
  const signOut = useCallback((message = 'You have been signed out.') => {
    localStorage.removeItem('sc_token')
    localStorage.removeItem('sc_user')
    setToken(null)
    setUser(null)
    setPage('Home')
    notify(message)
  }, [notify])
  // Any request that comes back 401 with our token means the sign-in expired or the account is gone.
  useEffect(() => {
    const expired = () => signOut('Your session has expired. Please sign in again.')
    window.addEventListener(UNAUTHORIZED_EVENT, expired)
    return () => window.removeEventListener(UNAUTHORIZED_EVENT, expired)
  }, [signOut])
  // Check a saved sign-in once on load and refresh the stored profile from the server.
  useEffect(() => {
    if (!token) return
    request('/auth/validate', { token }).then((data) => {
      if (data.user) { localStorage.setItem('sc_user', JSON.stringify(data.user)); setUser(data.user) }
    }).catch(() => {})
  }, []) // eslint-disable-line react-hooks/exhaustive-deps
  const signedIn = Boolean(token && user)
  const navigate = (target) => {
    setPage(target)
    setMobileMenu(false)
    setMoreMenu(false)
  }

  return (
    <div className="app-shell">
      <header className="topbar">
        <button className="brand" onClick={() => navigate('Home')} aria-label="SmartCommute home">
          <span className="brand-icon"><BusFront size={19} strokeWidth={2.5} /></span>
          <span>smart<span className="brand-accent">commute</span><small>DHAKA, MADE EASIER</small></span>
        </button>
        <button className="mobile-menu" onClick={() => setMobileMenu(!mobileMenu)} aria-label="Toggle navigation">
          {mobileMenu ? <X /> : <Menu />}
        </button>
        <nav className={mobileMenu ? 'nav-links open' : 'nav-links'}>
          {['Home', 'Find a bus', 'Ride share', 'My trips'].map((item) => (
            <button key={item} className={page === item ? 'nav-link active' : 'nav-link'} onClick={() => navigate(item)}>{item}</button>
          ))}
          <div className="more-nav-wrap">
            <button className={['Friends', 'Messages', 'Notifications', 'Offers', 'Map', 'GPay', 'Verification'].includes(page) ? 'nav-link active' : 'nav-link'} aria-expanded={moreMenu} onClick={() => setMoreMenu(!moreMenu)}>More <ChevronDown size={13} /></button>
            {moreMenu && <div className="more-nav-menu">{[['Friends','Friends'],['Messages','Messages'],['Notifications','Notifications'],['Offers','Offers & rewards'],['Map','Explore map'],['GPay','GPay & recharge'],['Verification','Student verification']].map(([target,label]) => <button key={target} className={page === target ? 'current' : ''} onClick={() => navigate(target)}>{label}<ArrowRight size={13} /></button>)}</div>}
          </div>
        </nav>
        <div className="top-actions">
          {signedIn ? (
            <>
              <button className="icon-button" title="My wallet" onClick={() => navigate('Wallet')}><Wallet size={18} /></button>
              <button className="profile-chip" onClick={() => navigate('Profile')}>
                <span className="avatar">{user.name?.[0]?.toUpperCase() || 'U'}</span>
                <span>{user.name?.split(' ')[0] || 'Account'}</span><ChevronDown size={14} />
              </button>
            </>
          ) : <button className="button button-dark button-small" onClick={() => setAuthMode('login')}>Sign in <ArrowRight size={15} /></button>}
        </div>
      </header>

      <main>
        {page === 'Home' && <HomePage user={user} token={token} signedIn={signedIn} navigate={navigate} openAuth={() => setAuthMode('login')} />}
        {page === 'Find a bus' && <BusPage token={token} signedIn={signedIn} user={user} notify={notify} openAuth={() => setAuthMode('login')} />}
        {page === 'Ride share' && <RidePage token={token} user={user} signedIn={signedIn} notify={notify} openAuth={() => setAuthMode('login')} />}
        {page === 'My trips' && <TripsPage token={token} signedIn={signedIn} openAuth={() => setAuthMode('login')} />}
        {page === 'Wallet' && <WalletPage token={token} signedIn={signedIn} openAuth={() => setAuthMode('login')} />}
        {page === 'Profile' && <ProfilePage user={user} token={token} signedIn={signedIn} updateUser={setUser} signOut={signOut} openAuth={() => setAuthMode('login')} notify={notify} />}
        {page === 'Friends' && <FriendsPage token={token} user={user} signedIn={signedIn} openAuth={() => setAuthMode('login')} onChat={(friend) => { sessionStorage.setItem('chat_contact_id', friend.id || friend._id); navigate('Messages') }} notify={notify} />}
        {page === 'Messages' && <ChatPage token={token} user={user} signedIn={signedIn} openAuth={() => setAuthMode('login')} notify={notify} />}
        {page === 'Notifications' && <NotificationsPage user={user} signedIn={signedIn} openAuth={() => setAuthMode('login')} notify={notify} />}
        {page === 'Offers' && <OffersPage token={token} signedIn={signedIn} openAuth={() => setAuthMode('login')} notify={notify} />}
        {page === 'Map' && <MapPage token={token} user={user} signedIn={signedIn} openAuth={() => setAuthMode('login')} notify={notify} />}
        {page === 'GPay' && <GpayPage token={token} signedIn={signedIn} openAuth={() => setAuthMode('login')} notify={notify} />}
        {page === 'Verification' && <VerificationPage token={token} user={user} signedIn={signedIn} openAuth={() => setAuthMode('login')} notify={notify} />}
      </main>

      <footer className="footer">
        <span className="footer-brand"><BusFront size={15} /> SmartCommute Dhaka</span>
        <span>Making every trip a little simpler.</span>
        <button onClick={() => navigate('Profile')}><CircleHelp size={14} /> Help & account</button>
      </footer>
      {toast && <div className="toast"><Check size={16} /> {toast}</div>}
      {authMode && <AuthModal mode={authMode} onMode={setAuthMode} onClose={() => setAuthMode('')} onSuccess={(nextUser, nextToken) => {
        localStorage.setItem('sc_token', nextToken)
        localStorage.setItem('sc_user', JSON.stringify(nextUser))
        setToken(nextToken)
        setUser(nextUser)
        setAuthMode('')
        notify(`Welcome${nextUser.name ? `, ${nextUser.name.split(' ')[0]}` : ''}!`)
      }} />}
    </div>
  )
}

function HomePage({ user, token, signedIn, navigate, openAuth }) {
  const [busCount, setBusCount] = useState(null)
  useEffect(() => {
    if (!token) return
    request('/bus/all', { token }).then((data) => setBusCount(Array.isArray(data.data) ? data.data.length : 0)).catch(() => {})
  }, [token])
  return (
    <div className="page home-page">
      <section className="hero">
        <div className="hero-copy">
          <div className="eyebrow"><span className="live-dot" /> YOUR CITY. YOUR WAY.</div>
          <h1>{signedIn ? <>Good to see you,<br /><span>{user.name?.split(' ')[0] || 'traveler'}.</span></> : <>Move around<br /><span>Dhaka, better.</span></>}</h1>
          <p>Your everyday commute, made a little easier. Find the right bus, share a ride, and keep every journey together.</p>
          <div className="hero-buttons">
            <button className="button button-dark" onClick={() => navigate('Find a bus')}>Plan a bus trip <ArrowRight size={16} /></button>
            <button className="button button-outline" onClick={() => navigate('Ride share')}><Users size={16} /> Explore ride shares</button>
          </div>
          <div className="hero-proof"><span className="mini-avatars"><i>R</i><i>M</i><i>A</i></span><span>Better journeys, together</span><span className="proof-divider" /><span className="rating">✦ 4.9</span></div>
        </div>
        <div className="hero-art">
          <div className="sun-disc" />
          <div className="route-label label-start"><span className="pin-dot green" />Uttara</div>
          <div className="route-label label-end"><span className="pin-dot red" />Motijheel</div>
          <svg className="route-path" viewBox="0 0 470 390" fill="none" aria-hidden="true"><path d="M80 310 C110 210 178 280 207 194 C231 126 303 172 325 90 C344 22 384 77 402 47" stroke="#19865e" strokeWidth="3" strokeDasharray="7 9" strokeLinecap="round"/><circle cx="80" cy="310" r="8" fill="#fff" stroke="#19865e" strokeWidth="3"/><circle cx="402" cy="47" r="8" fill="#fff" stroke="#ef7864" strokeWidth="3"/></svg>
          <div className="city-illustration">
            <div className="tower tower-a"><i /><i /><i /><i /></div><div className="tower tower-b"><i /><i /><i /><i /><i /></div>
            <div className="tower tower-c"><i /><i /><i /></div><div className="tower tower-d"><i /><i /><i /><i /></div>
            <div className="bridge"><div /><div /><div /></div>
            <div className="road"><div className="road-line" /></div>
            <div className="bus-illustration"><span className="bus-destination">SMART COMMUTE</span><div className="bus-windows"><i /><i /><i /></div><div className="bus-wheels"><i /><i /></div></div>
            <div className="art-tree tree-one"><i /></div><div className="art-tree tree-two"><i /></div>
          </div>
          <div className="art-card"><span className="art-card-icon"><Compass size={17} /></span><span><b>Dhaka, connected</b><small>One city. Many ways to go.</small></span><ArrowUpRight size={15} /></div>
        </div>
        <div className="hero-grain" />
      </section>

      <section className="quick-search">
        <div className="search-heading"><span className="section-icon"><MapPin size={18} /></span><div><b>Where are you headed?</b><small>Find a bus for your next trip</small></div></div>
        <QuickSearch onSearch={(from, to) => { sessionStorage.setItem('bus_query', JSON.stringify({ from, to })); navigate('Find a bus') }} />
      </section>

      <section className="service-section">
        <div className="section-title"><div><div className="eyebrow">MADE FOR YOUR EVERYDAY</div><h2>Get there your way.</h2></div><span className="section-note">The essentials for a smoother Dhaka commute.</span></div>
        <div className="service-grid">
          <button className="service-card bus-card" onClick={() => navigate('Find a bus')}><span className="service-icon mint"><BusFront size={21} /></span><span className="card-arrow"><ArrowUpRight size={18} /></span><small>01 — PUBLIC TRANSIT</small><b>Find your bus</b><p>Search routes across the city and see which buses get you there.</p><span className="service-foot">{busCount === null ? 'Explore routes' : `${busCount} buses to explore`} <ArrowRight size={15} /></span></button>
          <button className="service-card ride-card" onClick={() => navigate('Ride share')}><span className="service-icon peach"><Users size={21} /></span><span className="card-arrow"><ArrowUpRight size={18} /></span><small>02 — SHARE THE JOURNEY</small><b>Ride together</b><p>Find a travel buddy heading the same way—or post your own ride.</p><span className="service-foot">Browse ride shares <ArrowRight size={15} /></span></button>
          <button className="service-card wallet-card" onClick={() => signedIn ? navigate('Wallet') : openAuth()}><span className="service-icon lilac"><Wallet size={21} /></span><span className="card-arrow"><ArrowUpRight size={18} /></span><small>03 — YOUR TRAVEL WALLET</small><b>Stay in balance</b><p>Keep an eye on your wallet and the trips you have taken.</p><span className="service-foot">{signedIn ? 'View your wallet' : 'Sign in to get started'} <ArrowRight size={15} /></span></button>
        </div>
      </section>

      <section className="bottom-banner"><span className="banner-spark"><Sparkles size={20} /></span><div><b>A city that moves with you.</b><p>From your first stop to your final destination—plan it all in one place.</p></div><button className="button button-light" onClick={() => navigate('Find a bus')}>Start exploring <ArrowRight size={15} /></button><div className="banner-decoration">DHAKA</div></section>
    </div>
  )
}

function QuickSearch({ onSearch }) {
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  return <form className="quick-search-form" onSubmit={(event) => { event.preventDefault(); onSearch(from, to) }}>
    <label><small>FROM</small><span><span className="input-dot" /><input value={from} onChange={(e) => setFrom(e.target.value)} placeholder="Choose starting point" /></span></label>
    <button type="button" className="swap-button" onClick={() => { setFrom(to); setTo(from) }} aria-label="Swap locations"><ArrowDownUp size={15} /></button>
    <label><small>TO</small><span><MapPin size={16} /><input value={to} onChange={(e) => setTo(e.target.value)} placeholder="Where to?" /></span></label>
    <button type="submit" className="button button-green"><Search size={16} /> Find a bus</button>
  </form>
}

function PageHeading({ eyebrow, title, description, icon: Icon }) {
  return <div className="page-heading"><span className="heading-icon">{Icon && <Icon size={21} />}</span><div><div className="eyebrow">{eyebrow}</div><h1>{title}</h1><p>{description}</p></div></div>
}

function BusPage({ token, signedIn, user, notify, openAuth }) {
  const savedQuery = useMemo(() => {
    try { return JSON.parse(sessionStorage.getItem('bus_query') || '{}') } catch { return {} }
  }, [])
  const [from, setFrom] = useState(savedQuery.from || '')
  const [to, setTo] = useState(savedQuery.to || '')
  const [name, setName] = useState('')
  const [mode, setMode] = useState('route')
  const [buses, setBuses] = useState([])
  const [loading, setLoading] = useState(false)
  const [searched, setSearched] = useState(false)
  const [error, setError] = useState('')
  const [favorites, setFavorites] = useState([])

  useEffect(() => {
    if (!signedIn || !user?.id) return
    request(`/api/favbus/user/${encodeURIComponent(user.id)}`, { token })
      .then((data) => setFavorites((data.data || []).map((favorite) => String(favorite.busId))))
      .catch(() => {})
  }, [signedIn, token, user?.id])

  const search = async (event) => {
    event?.preventDefault()
    if (!signedIn) return openAuth()
    const query = mode === 'route' ? (from && to ? `/bus/search-by-route?${new URLSearchParams({ startLocation: from, endLocation: to })}` : '') : (name ? `/bus/search-by-name?${new URLSearchParams({ busName: name })}` : '')
    if (!query) { setError(mode === 'route' ? 'Enter both your starting point and destination.' : 'Enter a bus name to search.'); return }
    setLoading(true); setError(''); setSearched(true)
    try {
      const result = await request(query, { token })
      setBuses(result.data || [])
    } catch (e) { setBuses([]); setError(errorText(e)) } finally { setLoading(false) }
  }
  const saveTrip = async (bus) => {
    if (!signedIn) return openAuth()
    try {
      await request('/trip/add', { token, method: 'POST', body: JSON.stringify({ busId: bus._id, busName: bus.busName, distance: 0, fare: Number(bus.base_fare || 0), source: from, destination: to }) })
      notify('Trip added to your history.')
    } catch (e) { notify(errorText(e)) }
  }
  const toggleFavorite = async (bus) => {
    if (!signedIn) return openAuth()
    const isSaved = favorites.includes(bus._id)
    try {
      await request(isSaved ? '/api/favbus/remove' : '/api/favbus/add', { token, method: isSaved ? 'DELETE' : 'POST', body: JSON.stringify({ userId: user.id, busId: bus._id, busName: bus.busName, routeNumber: bus.routeNumber, operator: bus.operator }) })
      setFavorites((current) => isSaved ? current.filter((id) => id !== bus._id) : [...current, bus._id])
      notify(isSaved ? 'Removed from saved buses.' : 'Bus saved to favorites.')
    } catch (e) { notify(errorText(e)) }
  }
  return <div className="page inner-page">
    <PageHeading eyebrow="GET THERE WITH EASE" title="Find your bus." description="Search Dhaka bus routes by the places you are going—or by bus name." icon={BusFront} />
    <section className="panel bus-search-panel">
      <div className="segmented"><button className={mode === 'route' ? 'selected' : ''} onClick={() => { setMode('route'); setError('') }}><MapPin size={15} /> Search by route</button><button className={mode === 'name' ? 'selected' : ''} onClick={() => { setMode('name'); setError('') }}><Search size={15} /> Search by bus</button></div>
      <form className="bus-search-form" onSubmit={search}>
        {mode === 'route' ? <>
          <label className="field"><small>STARTING POINT</small><span><span className="input-dot" /><input value={from} onChange={(e) => setFrom(e.target.value)} placeholder="e.g. Uttara" /></span></label>
          <button type="button" className="swap-button" onClick={() => { setFrom(to); setTo(from) }} aria-label="Swap locations"><ArrowDownUp size={15} /></button>
          <label className="field"><small>DESTINATION</small><span><MapPin size={16} /><input value={to} onChange={(e) => setTo(e.target.value)} placeholder="e.g. Motijheel" /></span></label>
        </> : <label className="field wide"><small>BUS NAME</small><span><Search size={16} /><input value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. BRTC, Green Line" /></span></label>}
        <button className="button button-green" type="submit" disabled={loading}>{loading ? 'Searching…' : <><Search size={16} /> Search buses</>}</button>
      </form>
      {error && <div className="inline-error">{error}</div>}
      {!signedIn && <div className="signin-hint"><ShieldCheck size={15} /> Bus search requires an account. <button onClick={openAuth}>Sign in</button> to find routes.</div>}
    </section>
    {searched && <section className="results-area">
      <div className="results-heading"><div><div className="eyebrow">YOUR ROUTE</div><h2>{mode === 'route' ? `${from || 'Start'} to ${to || 'Destination'}` : `Buses matching “${name}”`}</h2></div>{!loading && <span className="result-count">{buses.length} {buses.length === 1 ? 'bus' : 'buses'} found</span>}</div>
      {loading ? <div className="empty-state"><span className="spinner" /> Finding your buses…</div> : buses.length ? <div className="bus-list">{buses.map((bus) => <BusCard key={bus._id} bus={bus} from={from} to={to} user={user} signedIn={signedIn} favorite={favorites.includes(bus._id)} onFavorite={() => toggleFavorite(bus)} onAddTrip={() => saveTrip(bus)} notify={notify} />)}</div> : <div className="empty-state"><span className="empty-icon"><BusFront size={23} /></span><b>No buses found just yet</b><span>{error || 'Try another nearby stop or check the spelling.'}</span></div>}
    </section>}
    {!searched && <section className="suggestion"><span className="suggestion-icon"><Compass size={19} /></span><div><b>Start with a familiar landmark</b><p>Try popular Dhaka stops like Farmgate, Mohakhali, Gulistan, or Uttara.</p></div><span className="suggestion-pills"><button onClick={() => { setFrom('Farmgate'); setTo('Uttara') }}>Farmgate</button><button onClick={() => { setFrom('Gulistan'); setTo('Uttara') }}>Gulistan</button></span></section>}
  </div>
}

function BusCard({ bus, from, to, user, signedIn, favorite, onFavorite, onAddTrip, notify }) {
  const stops = getStops(bus)
  return <article className="bus-result panel">
    <span className="bus-result-icon"><BusFront size={20} /></span><div className="bus-result-main">
      <div className="bus-title"><h3>{bus.busName || 'City bus'}</h3>{bus.routeNumber && <span className="route-badge">{bus.routeNumber}</span>}</div>
      <div className="bus-details"><span>{bus.operator || 'Dhaka transit'}</span>{bus.busType && <><i /> <span>{bus.busType}</span></>}{bus.frequency && <><i /> <span>{bus.frequency}</span></>}</div>
      {stops.length > 0 && <div className="stop-line"><span>{from || stops[0]}</span><span className="stop-track" /><span>{to || stops[stops.length - 1]}</span></div>}
      {stops.length > 0 && <div className="stop-count">{stops.length} stops on this route</div>}
    </div><div className="bus-result-side"><button className={favorite ? 'favorite saved' : 'favorite'} onClick={onFavorite} title="Save bus"><Heart size={17} fill={favorite ? 'currentColor' : 'none'} /></button><span className="fare-label">STARTS FROM</span><b>{money(bus.base_fare)}</b><small>per ride</small><button className="button button-dark button-small" onClick={onAddTrip}>Log trip <ArrowRight size={14} /></button></div><BusReviews bus={bus} user={user} signedIn={signedIn} notify={notify} />
  </article>
}

function RidePage({ token, user, signedIn, notify, openAuth }) {
  const [rides, setRides] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [showForm, setShowForm] = useState(false)
  const [search, setSearch] = useState('')
  const [form, setForm] = useState({ source: '', destination: '', maxParticipants: 4 })
  const load = useCallback(async () => {
    setLoading(true)
    try { const data = await request('/api/rideshare'); setRides(data.ridePosts || data.data || data || []); setError('') }
    catch (e) { setRides([]); setError(errorText(e)) } finally { setLoading(false) }
  }, [])
  useEffect(() => { load() }, [load])
  const createRide = async (event) => {
    event.preventDefault()
    if (!signedIn) return openAuth()
    try {
      await request('/api/rideshare', { method: 'POST', body: JSON.stringify({ ...form, maxParticipants: Number(form.maxParticipants), userId: user.id, userName: user.name, gender: user.gender || 'Prefer not to say' }) })
      setShowForm(false); setForm({ source: '', destination: '', maxParticipants: 4 }); notify('Your ride share is live.'); load()
    } catch (e) { notify(errorText(e)) }
  }
  const requestRide = async (ride) => {
    if (!signedIn) return openAuth()
    try {
      await request('/api/riderequests/send', { method: 'POST', body: JSON.stringify({ ridePostId: ride._id, requesterId: user.id, requesterName: user.name, requesterGender: user.gender || 'Prefer not to say' }) })
      notify('Your request has been sent.')
      return true
    } catch (e) { notify(errorText(e)); return false }
  }
  const filtered = rides.filter((ride) => `${ride.source} ${ride.destination} ${ride.userName}`.toLowerCase().includes(search.toLowerCase()))
  return <div className="page inner-page">
    <PageHeading eyebrow="GOOD COMPANY, BETTER RIDES" title="Share the journey." description="Find people heading your way. Split the ride, share the road." icon={Users} />
    <section className="rides-hero"><span className="rides-hero-icon"><Users size={22} /></span><div><b>Going the same way?</b><p>Join a fellow commuter or post your ride so others can come along.</p></div><button className="button button-dark" onClick={() => signedIn ? setShowForm(!showForm) : openAuth()}><Plus size={16} /> Post a ride</button><div className="rides-hero-shape" /></section>
    {showForm && <form className="panel create-ride-form" onSubmit={createRide}><div className="form-head"><div><b>Post a ride share</b><span>Tell other commuters where you are going.</span></div><button className="icon-button" type="button" onClick={() => setShowForm(false)} aria-label="Close"><X size={18} /></button></div><div className="form-fields"><label className="field"><small>FROM</small><input required value={form.source} onChange={(e) => setForm({ ...form, source: e.target.value })} placeholder="Starting point" /></label><label className="field"><small>TO</small><input required value={form.destination} onChange={(e) => setForm({ ...form, destination: e.target.value })} placeholder="Destination" /></label><label className="field participant-field"><small>MAX PEOPLE</small><select value={form.maxParticipants} onChange={(e) => setForm({ ...form, maxParticipants: e.target.value })}>{[2, 3, 4, 5, 6, 7, 8, 9].map((number) => <option key={number} value={number}>{number} people</option>)}</select></label></div><button className="button button-green" type="submit">Publish ride <ArrowRight size={15} /></button></form>}
    <div className="ride-toolbar"><div><div className="eyebrow">COMMUNITY RIDES</div><h2>Rides around Dhaka <span className="count-bubble">{rides.length}</span></h2></div><label className="ride-search"><Search size={15} /><input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search a destination" /></label></div>
    {loading ? <div className="empty-state"><span className="spinner" /> Finding available rides…</div> : filtered.length ? <div className="ride-grid">{filtered.map((ride) => <RideCard key={ride._id} ride={ride} token={token} user={user} signedIn={signedIn} onRequest={() => requestRide(ride)} notify={notify} onRefresh={load} />)}</div> : <div className="empty-state panel"><span className="empty-icon"><Users size={22} /></span><b>{error ? 'Could not load rides' : 'No rides on this route yet'}</b><span>{error || (rides.length ? 'Try searching for another destination.' : 'Be the first to post a ride and find your travel crew.')}</span>{error ? <button className="button button-outline button-small" onClick={load}>Try again</button> : !showForm && <button className="button button-green button-small" onClick={() => signedIn ? setShowForm(true) : openAuth()}><Plus size={14} /> Post the first ride</button>}</div>}
  </div>
}

function RideCard({ ride, token, user, signedIn, onRequest, notify, onRefresh }) {
  const isMine = user?.id === ride.userId || user?.id === ride.userId?._id
  const [requestSent, setRequestSent] = useState(false)
  const [showRequests, setShowRequests] = useState(false)
  const [requests, setRequests] = useState([])
  const [requestError, setRequestError] = useState('')
  const viewRequests = async () => {
    setShowRequests(!showRequests)
    if (showRequests) return
    try { const data = await request(`/api/riderequests/ride/${ride._id}`, { token }); setRequests(data.data || []) }
    catch (e) { setRequestError(errorText(e)) }
  }
  const resolveRequest = async (item, accepted) => {
    try {
      await request(`/api/riderequests/${accepted ? 'accept' : 'reject'}/${item._id}`, { token, method: 'PUT', ...(accepted ? { body: JSON.stringify({ ridePostId: ride._id }) } : {}) })
      notify(accepted ? 'Ride request accepted.' : 'Ride request declined.')
      const data = await request(`/api/riderequests/ride/${ride._id}`, { token })
      setRequests(data.data || [])
      onRefresh()
    } catch (e) { setRequestError(errorText(e)) }
  }
  return <><article className="ride-card panel"><div className="ride-card-top"><span className="ride-pulse"><span /></span><span>{ride.status || 'Open ride'}</span><span className="ride-seats"><Users size={13} /> Up to {ride.maxParticipants || 4}</span></div><div className="ride-route"><div><span className="route-origin" /><b>{ride.source}</b></div><span className="route-connector" /><div><span className="route-destination" /><b>{ride.destination}</b></div></div><div className="ride-card-footer"><span className="host-avatar">{ride.userName?.[0]?.toUpperCase() || 'R'}</span><span className="host-info"><b>{ride.userName || 'Dhaka commuter'}</b><small>{ride.gender ? `${ride.gender} · ` : ''}Ride host</small></span>{isMine ? <button className="button button-outline button-small" onClick={async () => { try { await request(`/api/rideshare/${ride._id}`, { method: 'DELETE' }); notify('Ride post removed.'); onRefresh() } catch (e) { notify(errorText(e)) } }}>Your post</button> : <button className="button button-dark button-small" disabled={requestSent} onClick={async () => { const sent = await onRequest(); if (sent) setRequestSent(true) }}>{requestSent ? <><Check size={14} /> Requested</> : <>Join ride <ArrowRight size={14} /></>}</button>}</div></article>
    {isMine && <div className="ride-owner-actions"><button className="button button-outline button-small" onClick={viewRequests}>{showRequests ? 'Hide requests' : 'View ride requests'} <ArrowRight size={13} /></button>{showRequests && <div className="ride-request-list">{requestError && <small className="ride-request-error">{requestError}</small>}{requests.length ? requests.map((item) => <div className="ride-request-row" key={item._id}><span><b>{item.requesterName || 'Commuter'}</b><small>{item.requesterGender || ''} · {item.status || 'pending'}</small></span>{item.status === 'pending' && <><button className="button button-green button-small" onClick={() => resolveRequest(item, true)}>Accept</button><button className="button button-outline button-small" onClick={() => resolveRequest(item, false)}>Decline</button></>}</div>) : !requestError && <small className="ride-request-error">No requests yet.</small>}</div>}</div>}
  </>
}

function TripsPage({ token, signedIn, openAuth }) {
  const [trips, setTrips] = useState([])
  const [loading, setLoading] = useState(signedIn)
  const [error, setError] = useState('')
  const load = useCallback(() => {
    if (!signedIn) return
    setLoading(true)
    request('/trip/user', { token }).then((data) => setTrips(data.trips || data.data || data || [])).catch((e) => setError(errorText(e))).finally(() => setLoading(false))
  }, [token, signedIn])
  useEffect(() => { load() }, [load])
  return <div className="page inner-page">
    <PageHeading eyebrow="YOUR JOURNEY, ALL IN ONE PLACE" title="Trip history." description="A little record of all the places you have been." icon={History} />
    {!signedIn ? <SignInPrompt openAuth={openAuth} message="Sign in to see your recent journeys." /> : loading ? <div className="empty-state"><span className="spinner" /> Loading your trips…</div> : trips.length ? <div className="trip-list panel">{trips.map((trip, index) => <article className="trip-row" key={trip._id || index}><span className="trip-icon"><BusFront size={18} /></span><div className="trip-route-info"><b>{trip.source || 'Trip'} <ArrowRight size={13} /> {trip.destination || 'Destination'}</b><small>{trip.busName || 'Public transit'} · {trip.createdAt ? new Date(trip.createdAt).toLocaleDateString() : 'Recent trip'}</small></div><span className="trip-fare">{money(trip.fare)}</span></article>)}</div> : <div className="empty-state panel"><span className="empty-icon"><History size={22} /></span><b>{error ? 'Trip history unavailable' : 'Your next trip starts here'}</b><span>{error || 'Log a trip when you find a bus and it will show up here.'}</span>{error && <button className="button button-outline button-small" onClick={load}>Try again</button>}</div>}
  </div>
}

function WalletPage({ token, signedIn, openAuth }) {
  const [wallet, setWallet] = useState(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(signedIn)
  const load = useCallback(() => {
    if (!signedIn) return
    setLoading(true)
    request('/wallet/balance', { token }).then((data) => setWallet(data.wallet || data.data || data)).catch((e) => setError(errorText(e))).finally(() => setLoading(false))
  }, [signedIn, token])
  useEffect(() => { load() }, [load])
  const balance = wallet?.balance ?? wallet?.wallet?.balance ?? 0
  return <div className="page inner-page"><PageHeading eyebrow="YOUR COMMUTE COMPANION" title="Travel wallet." description="Keep your travel balance and recent journeys close at hand." icon={Wallet} />
    {!signedIn ? <SignInPrompt openAuth={openAuth} message="Sign in to check your SmartCommute wallet." /> : loading ? <div className="empty-state"><span className="spinner" /> Checking your balance…</div> : error ? <div className="empty-state panel"><span className="empty-icon"><Wallet size={22} /></span><b>Wallet unavailable</b><span>{error}</span><button className="button button-outline button-small" onClick={load}>Try again</button></div> : <><section className="wallet-overview"><div className="wallet-card-large"><div className="wallet-card-head"><span className="wallet-mark"><BusFront size={18} /></span><span>SMARTCOMMUTE WALLET</span><CreditCard size={20} /></div><div className="balance-caption">AVAILABLE BALANCE</div><div className="balance-amount">{money(balance)}</div><div className="wallet-card-foot"><span>BDT · Bangladeshi Taka</span><span>•••• {String(wallet?._id || '4829').slice(-4)}</span></div><div className="wallet-card-orb" /></div><div className="wallet-note panel"><span className="service-icon mint"><ShieldCheck size={19} /></span><b>Your commute, in good hands.</b><p>Your balance is connected to your SmartCommute account.</p></div></section><div className="wallet-hint"><Clock3 size={16} /><span><b>Easy to keep track.</b> Your trip history keeps a record of each commute you log.</span></div></>}
  </div>
}

function ProfilePage({ user, token, signedIn, updateUser, signOut, openAuth, notify }) {
  const [name, setName] = useState(user?.name || '')
  const [email, setEmail] = useState(user?.email || '')
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [saving, setSaving] = useState(false)
  const [passwordSaving, setPasswordSaving] = useState(false)
  useEffect(() => { setName(user?.name || ''); setEmail(user?.email || '') }, [user])
  const save = async (event) => {
    event.preventDefault()
    setSaving(true)
    try {
      await request('/auth/profile', { token, method: 'PUT', body: JSON.stringify({ name, email }) })
      const nextUser = { ...user, name, email }
      localStorage.setItem('sc_user', JSON.stringify(nextUser)); updateUser(nextUser); notify('Profile updated.')
    } catch (e) { notify(errorText(e)) } finally { setSaving(false) }
  }
  const updatePassword = async (event) => {
    event.preventDefault()
    setPasswordSaving(true)
    try {
      await request('/auth/password', { token, method: 'PUT', body: JSON.stringify({ currentPassword, newPassword }) })
      setCurrentPassword(''); setNewPassword(''); notify('Password updated.')
    } catch (e) { notify(errorText(e)) } finally { setPasswordSaving(false) }
  }
  return <div className="page inner-page"><PageHeading eyebrow="YOUR ACCOUNT, YOUR WAY" title="Your profile." description="Your details stay with you wherever you go." icon={Users} />
    {!signedIn ? <SignInPrompt openAuth={openAuth} message="Sign in to manage your profile." /> : <div className="profile-layout"><section className="panel profile-form"><div className="profile-intro"><span className="avatar avatar-large">{user.name?.[0]?.toUpperCase() || 'U'}</span><div><b>{user.name}</b><small>{user.role || 'Commuter'} · SmartCommute member</small></div></div><form onSubmit={save}><label className="field"><small>FULL NAME</small><input required value={name} onChange={(e) => setName(e.target.value)} /></label><label className="field"><small>EMAIL ADDRESS</small><input type="email" required value={email} onChange={(e) => setEmail(e.target.value)} /></label><label className="field"><small>ACCOUNT TYPE</small><input disabled value={user.role || 'commuter'} /></label><button className="button button-green" disabled={saving}>{saving ? 'Saving…' : 'Save changes'} <Check size={15} /></button></form><div className="profile-divider" /><form className="password-form" onSubmit={updatePassword}><b>Change password</b><label className="field"><small>CURRENT PASSWORD</small><input type="password" required autoComplete="current-password" value={currentPassword} onChange={(e) => setCurrentPassword(e.target.value)} /></label><label className="field"><small>NEW PASSWORD</small><input type="password" required minLength="6" autoComplete="new-password" value={newPassword} onChange={(e) => setNewPassword(e.target.value)} /></label><button className="button button-outline" disabled={passwordSaving}>{passwordSaving ? 'Updating…' : 'Update password'}</button></form></section><aside className="panel profile-aside"><span className="service-icon peach"><ShieldCheck size={20} /></span><b>Your account, protected.</b><p>Your profile is only used to make your commute easier and help you connect with other riders.</p><div className="profile-divider" /><button className="signout-button" onClick={() => signOut()}><LogOut size={16} /> Sign out <ArrowRight size={15} /></button></aside></div>}
  </div>
}

function SignInPrompt({ openAuth, message }) {
  return <div className="signin-prompt panel"><span className="service-icon mint"><ShieldCheck size={20} /></span><div><b>Good to have you here.</b><p>{message}</p></div><button className="button button-dark button-small" onClick={openAuth}>Sign in <ArrowRight size={14} /></button></div>
}

function AuthModal({ mode, onMode, onClose, onSuccess }) {
  const [form, setForm] = useState({ name: '', email: '', password: '', gender: 'Prefer not to say', role: 'commuter' })
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const signup = mode === 'signup'
  const submit = async (event) => {
    event.preventDefault()
    setLoading(true); setError('')
    try {
      if (signup) {
        await request('/auth/signup', { method: 'POST', body: JSON.stringify({ name: form.name, email: form.email, gender: form.gender, role: form.role, password: form.password }) })
      }
      const data = await request('/auth/login', { method: 'POST', body: JSON.stringify({ email: form.email, password: form.password }) })
      onSuccess(data.user, data.token)
    } catch (e) { setError(errorText(e)) } finally { setLoading(false) }
  }
  return <div className="modal-backdrop" onMouseDown={(event) => event.target === event.currentTarget && onClose()}><section className="auth-modal panel"><button className="modal-close icon-button" onClick={onClose} aria-label="Close"><X size={19} /></button><span className="auth-mark"><BusFront size={20} /></span><div className="eyebrow">{signup ? 'YOUR NEXT JOURNEY STARTS HERE' : 'WELCOME BACK'}</div><h2>{signup ? 'Join the commute.' : 'Good to see you.'}</h2><p>{signup ? 'Create an account and make the city feel a little smaller.' : 'Sign in to pick up right where you left off.'}</p>
    <form onSubmit={submit}>{signup && <><label className="field"><small>YOUR NAME</small><input required autoComplete="name" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} placeholder="How should we call you?" /></label><div className="form-fields"><label className="field"><small>GENDER</small><select value={form.gender} onChange={(e) => setForm({ ...form, gender: e.target.value })}><option>Prefer not to say</option><option>Male</option><option>Female</option><option>Other</option></select></label><label className="field"><small>I AM A</small><select value={form.role} onChange={(e) => setForm({ ...form, role: e.target.value })}><option value="commuter">Commuter</option><option value="driver">Driver</option></select></label></div></>}<label className="field"><small>EMAIL ADDRESS</small><input required type="email" autoComplete="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} placeholder="you@example.com" /></label><label className="field"><small>PASSWORD</small><input required type="password" minLength="6" autoComplete={signup ? 'new-password' : 'current-password'} value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} placeholder="At least 6 characters" /></label>{error && <div className="inline-error">{error}</div>}<button className="button button-green auth-submit" disabled={loading}>{loading ? 'One moment…' : signup ? 'Create account' : 'Sign in'} <ArrowRight size={15} /></button></form><div className="auth-switch">{signup ? 'Already on board?' : 'New to SmartCommute?'} <button onClick={() => { setError(''); onMode(signup ? 'login' : 'signup') }}>{signup ? 'Sign in' : 'Create an account'}</button></div><small className="auth-api">Connected to {API_BASE}</small></section></div>
}

createRoot(document.getElementById('root')).render(<React.StrictMode><App /></React.StrictMode>)
