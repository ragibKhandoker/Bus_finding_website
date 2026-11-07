import React, { useEffect, useRef, useState } from 'react'
import { ArrowRight, ArrowUpRight, BusFront, Clock3, Gift, GraduationCap, History, Map as MapIcon, MessageCircle, Route, Search, ShieldCheck, Smartphone, UserRound, Users, Wallet } from 'lucide-react'
import womenBusDriver from '../../assets/home/women-bus-driver.jpg'
import womenBusFleet from '../../assets/home/women-bus-fleet.jpg'
import cityBus from '../../assets/home/dhaka-nagar-paribahan-bus.jpg'
import { formatNumber, t } from '../../models/i18n'
import { BusStatsCard, RoutePreview } from '../components/BusRoutes'
import { useBusRoutes } from '../../controllers/useBusRoutes'
import { useScrollMotion } from '../../controllers/useScrollMotion'

// Secondary tools listed on the home page; each opens its own page, which handles sign-in itself.
export const HOME_TOOLS = [
  { target: 'Map', icon: MapIcon, title: 'Map & fare calculator', text: 'Road distance and fare between any two places.' },
  { target: 'Offers', icon: Gift, title: 'Offers & rewards', text: 'Discounts and points on every ride.' },
  { target: 'GPay', icon: Smartphone, title: 'GPay & recharge', text: 'Top up your travel balance in seconds.' },
  { target: 'Verification', icon: GraduationCap, title: 'Account verification', text: 'Verify your NID or student pass.' },
  { target: 'Friends', icon: UserRound, title: 'Friends', text: 'Add commuters with a friend code.' },
  { target: 'Messages', icon: MessageCircle, title: 'Messages', text: 'Coordinate pickups with your ride crew.' },
]

export function HomePage({ user, token, signedIn, navigate, openAuth }) {
  const [routes, loadRoutes] = useBusRoutes()
  // The hero buttons show a brief loading state, then open their page.
  const [opening, setOpening] = useState('')
  const openTimer = useRef(0)
  useEffect(() => () => window.clearTimeout(openTimer.current), [])
  const openPage = (target) => {
    setOpening(target)
    openTimer.current = window.setTimeout(() => navigate(target), 600)
  }
  const busStats = routes.status === 'ready'
    ? { status: 'ready', routes: routes.buses.length, stops: new Set(routes.buses.flatMap((bus) => (bus.stops || []).map((stop) => stop.name))).size }
    : { status: routes.status }
  const busCount = busStats.status === 'ready' ? busStats.routes : null
  const firstName = user?.name?.split(' ')[0]
  const pageRef = useRef(null)
  useScrollMotion(pageRef)
  return (
    <div className="home-page" ref={pageRef}>
      <section className="hp-hero">
        <div className="hp-hero-inner">
          <div className="hp-hero-copy">
            <span className="hp-badge"><span className="hp-badge-dot" /> {signedIn ? (firstName ? t('Welcome back, {name}', { name: firstName }) : t('Welcome back')) : t('Built for Dhaka commuters')}</span>
            <h1>{signedIn ? <>{t('Where are you')} <span>{t('heading today?')}</span></> : <>{t('Your daily commute,')} <span>{t('sorted.')}</span></>}</h1>
            <p>{t('Find the right bus across Dhaka, share a ride with people going your way, and keep your fares and trips in one place.')}</p>

            <div className="hp-hero-actions">
              <button type="button" className={`button button-green hp-hero-cta${opening === 'Find a bus' ? ' is-loading' : ''}`} disabled={Boolean(opening)} aria-busy={opening === 'Find a bus'} onClick={() => openPage('Find a bus')}>{opening === 'Find a bus' ? <><span className="button-spinner" aria-hidden="true" /> {t('Opening…')}</> : <><Search size={18} /> {t('Find your bus')}</>}</button>
              <button type="button" className={`button button-outline hp-hero-secondary${opening === 'Bus routes' ? ' is-loading' : ''}`} disabled={Boolean(opening)} aria-busy={opening === 'Bus routes'} onClick={() => openPage('Bus routes')}>{opening === 'Bus routes' ? <><span className="button-spinner dark" aria-hidden="true" /> {t('Opening…')}</> : <><Route size={17} /> {t('Browse all routes')}</>}</button>
            </div>

            <div className="hp-hero-stats">
              <span><b>{busStats.status === 'ready' ? formatNumber(busStats.routes) : '—'}</b><small>{t('Bus routes')}</small></span>
              <span><b>{busStats.status === 'ready' ? formatNumber(busStats.stops) : '—'}</b><small>{t('Bus stops')}</small></span>
              <span><b><MapIcon size={18} /> {t('Live map')}</b><small>{t('Distance & travel time')}</small></span>
            </div>

            <ul className="hp-trust">
              <li><ShieldCheck size={15} /> {t('Secure account sign-in')}</li>
              <li><Route size={15} /> {busCount === null ? t('City-wide bus routes') : t('{count} bus routes', { count: busCount })}</li>
              <li><Users size={15} /> {t('Community ride sharing')}</li>
            </ul>
          </div>

          <div className="hp-preview hp-photos">
            <figure className="hp-photo hp-photo-fleet">
              <img src={womenBusFleet} alt={t('A row of pink women-only double-decker buses')} width="857" height="482" decoding="async" fetchPriority="high" />
              <figcaption>{t('Women-only buses')}</figcaption>
            </figure>
            <figure className="hp-photo hp-photo-driver">
              <img src={womenBusDriver} alt={t('A woman driver at the wheel of a pink bus')} width="960" height="540" decoding="async" />
              <figcaption>{t('Women at the wheel')}</figcaption>
            </figure>
            <figure className="hp-photo hp-photo-city">
              <img src={cityBus} alt={t('A green Dhaka Nagar Paribahan city bus on the road')} width="1111" height="630" decoding="async" />
              <figcaption>{t('Dhaka Nagar Paribahan')}</figcaption>
            </figure>
          </div>
        </div>
      </section>

      <div className="page">
        <section className="hp-section">
          <div className="hp-section-head hp-head-row hp-services-head">
            <div>
              <span className="hp-kicker">{t('Core services')}</span>
              <h2>{t('Everything your commute needs')}</h2>
              <p>{t('Three tools that cover the everyday trip, from finding a route to paying for it.')}</p>
            </div>
            <BusStatsCard stats={busStats} onRetry={loadRoutes} onSeeAll={() => navigate('Bus routes')} />
          </div>
          <div className="hp-services">
            <button className="hp-service" onClick={() => navigate('Find a bus')}>
              <span className="hp-service-icon green"><BusFront size={22} /></span>
              <b>{t('Find your bus')}</b>
              <p>{t('Search by start and destination, or by bus name. Compare fares, stops and operators, then save your favourites.')}</p>
              <span className="hp-service-link">{busCount === null ? t('Search routes') : t('Browse {count} buses', { count: busCount })} <ArrowRight size={15} /></span>
            </button>
            <button className="hp-service" onClick={() => navigate('Ride share')}>
              <span className="hp-service-icon orange"><Users size={22} /></span>
              <b>{t('Ride together')}</b>
              <p>{t('Join commuters heading the same way or post your own ride. Accept requests and split the cost.')}</p>
              <span className="hp-service-link">{t('Browse ride shares')} <ArrowRight size={15} /></span>
            </button>
            <button className="hp-service" onClick={() => signedIn ? navigate('Wallet') : openAuth()}>
              <span className="hp-service-icon violet"><Wallet size={22} /></span>
              <b>{t('Travel wallet')}</b>
              <p>{t('Check your balance, log the trips you take and keep a clear record of what you spend on travel.')}</p>
              <span className="hp-service-link">{signedIn ? t('Open your wallet') : t('Sign in to get started')} <ArrowRight size={15} /></span>
            </button>
          </div>
        </section>

        <RoutePreview state={routes} onRetry={loadRoutes} navigate={navigate} />

        <section className="hp-section hp-steps-section">
          <div className="hp-section-head">
            <span className="hp-kicker">{t('How it works')}</span>
            <h2>{t('From search to seat in three steps')}</h2>
          </div>
          <ol className="hp-steps">
            <li><span className="hp-step-no">{formatNumber(1)}</span><Search size={20} /><b>{t('Search your route')}</b><p>{t('Enter where you are and where you want to go.')}</p></li>
            <li><span className="hp-step-no">{formatNumber(2)}</span><Clock3 size={20} /><b>{t('Pick the best option')}</b><p>{t('Compare buses by fare, type and stops, or join a shared ride.')}</p></li>
            <li><span className="hp-step-no">{formatNumber(3)}</span><History size={20} /><b>{t('Ride and keep track')}</b><p>{t('Log the trip and see it in your history and wallet.')}</p></li>
          </ol>
        </section>

        <section className="hp-section">
          <div className="hp-section-head hp-head-row">
            <div><span className="hp-kicker">{t('More tools')}</span><h2>{t('Do more with SmartCommute')}</h2></div>
            <button className="button button-outline button-small" onClick={() => navigate('Map')}>{t('Open the map')} <ArrowUpRight size={14} /></button>
          </div>
          <div className="hp-tools">
            {HOME_TOOLS.map(({ target, icon: Icon, title, text }) => <button key={target} className="hp-tool" onClick={() => navigate(target)}><span className="hp-tool-icon"><Icon size={18} /></span><span><b>{t(title)}</b><small>{t(text)}</small></span><ArrowRight size={15} className="hp-tool-arrow" /></button>)}
          </div>
        </section>

        <section className="hp-cta">
          <div>
            <h2>{signedIn ? t('Ready for your next trip?') : t('Start commuting smarter today')}</h2>
            <p>{signedIn ? t('Search a route, or look back at the journeys you have logged.') : t('Create a free account to search buses, share rides and manage your wallet.')}</p>
          </div>
          <div className="hp-cta-actions">
            {signedIn
              ? <><button className="button button-light" onClick={() => navigate('Find a bus')}>{t('Plan a trip')} <ArrowRight size={15} /></button><button className="button button-ghost" onClick={() => navigate('My trips')}>{t('View my trips')}</button></>
              : <><button className="button button-light" onClick={openAuth}>{t('Get started — it\'s free')} <ArrowRight size={15} /></button><button className="button button-ghost" onClick={() => navigate('Find a bus')}>{t('Search buses')}</button></>}
          </div>
        </section>
      </div>
    </div>
  )
}
