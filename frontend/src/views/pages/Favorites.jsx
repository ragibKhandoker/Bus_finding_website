import React from 'react'
import { ArrowRight, BusFront, Heart, HeartOff, Search } from 'lucide-react'
import { formatDate, formatNumber, t } from '../../models/i18n'
import { Feedback, Heading, Loading, RequireSignIn } from './MorePages'
import { useFavoriteBuses } from '../../controllers/useFavorites'

export { errorText } from '../../models/messages'
export { busIdOf } from '../../models/favorites'
export { addFavorite } from '../../models/favorites'
export { removeFavorite } from '../../models/favorites'
export { useFavoriteBuses } from '../../controllers/useFavorites'
export { useFavoriteStatus } from '../../controllers/useFavorites'

// Favourite buses: the signed-in user's list (one request for every card on a page), the heart button, and the
// Favourite buses page. The server takes the user from the token, so nothing here sends a user id.

export const money = (value) => `৳${formatNumber(value)}`

// Outline heart when not saved, filled when saved, a spinner while the request runs.
export function FavoriteButton({ active, pending, onClick, withLabel = false, className = '' }) {
  const label = active ? t('Remove from favourite buses') : t('Add to favourite buses')
  return <button type="button" className={`favorite${active ? ' saved' : ''}${withLabel ? ' with-label' : ''} ${className}`.trim()} onClick={onClick} disabled={pending} aria-pressed={active} aria-busy={pending} aria-label={label} title={label}>
    {pending ? <span className="favorite-spinner" aria-hidden="true" /> : <Heart size={17} fill={active ? 'currentColor' : 'none'} />}
    {withLabel && <span>{active ? t('Saved to favourites') : t('Save to favourites')}</span>}
  </button>
}

export function FavoritesPage({ token, user, signedIn, openAuth, notify, navigate }) {
  const favorites = useFavoriteBuses({ token, user, signedIn, notify, openAuth })
  const heading = <Heading icon={Heart} eyebrow={t('YOUR SAVED BUSES')} title={t('Favourite buses')} subtitle={t('Buses you saved with the heart button. Open one to see its stops, or remove the ones you no longer need.')} />
  if (!signedIn) return <section className="extra-page">{heading}<RequireSignIn openAuth={openAuth} /></section>

  const { items, status } = favorites
  return <section className="extra-page">{heading}
    {status !== 'idle' && status !== 'loading' && status !== 'ready' && <Feedback error={<>{status} <button className="link-button" onClick={favorites.reload}>{t('Try again')}</button></>} />}
    {status === 'loading' && !items.length ? <Loading label="Loading your favourite buses…" />
      : status === 'ready' && !items.length ? <div className="extra-panel fav-empty"><span><HeartOff size={22} /></span><b>{t('No favourite buses yet')}</b><p>{t('Search for a bus and tap the heart on its card to save it here.')}</p><button className="extra-button" onClick={() => navigate('Find a bus')}><Search size={14} /> {t('Find a bus')}</button></div>
      : <>{items.length > 0 && <p className="fav-count">{items.length === 1 ? t('1 saved bus') : t('{count} saved buses', { count: items.length })}</p>}
        <div className="fav-grid">{items.map((bus) => <article key={bus.busId} className="extra-panel fav-card">
          <div className="fav-card-head">
            <span className="fav-card-icon"><BusFront size={19} /></span>
            <div><h2>{t(bus.busName) || t('City bus')}</h2><p>{[bus.operator, bus.busType, bus.frequency].filter(Boolean).map((text) => t(text)).join(' · ') || t('Dhaka transit')}</p></div>
            {bus.routeNumber && <span className="route-badge">{bus.routeNumber}</span>}
          </div>
          {bus.firstStop && <div className="fav-route"><span><i className="start" />{t(bus.firstStop)}</span><ArrowRight size={13} /><span><i className="end" />{t(bus.lastStop)}</span></div>}
          <div className="fav-facts">
            {bus.stopCount != null && <span><small>{t('Stops')}</small><b>{formatNumber(bus.stopCount)}</b></span>}
            {bus.base_fare != null && <span><small>{t('Fare from')}</small><b>{money(bus.base_fare)}</b></span>}
            <span><small>{t('Saved on')}</small><b>{formatDate(bus.createdAt)}</b></span>
          </div>
          <div className="fav-actions">
            {bus.routeNumber && <button className="extra-button" onClick={() => navigate('Bus details', { route: bus.routeNumber })}>{t('View stops')} <ArrowRight size={13} /></button>}
            <button className="extra-button secondary fav-remove" disabled={favorites.isPending(bus)} onClick={() => favorites.toggle(bus)}>{favorites.isPending(bus) ? <span className="favorite-spinner" /> : <HeartOff size={14} />} {t('Remove')}</button>
          </div>
        </article>)}</div></>}
  </section>
}
