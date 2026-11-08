import React, { useCallback, useState } from 'react'
import { History, Search } from 'lucide-react'
import { useTripChanges } from '../../controllers/useTrips'
import { ActiveTripPanel, TripRow } from '../components/Trips'
import { apiRequest as request } from '../../models/api'
import { formatNumber, t } from '../../models/i18n'
import { money } from '../helpers'
import { errorText } from '../../models/messages'
import { PageHeading, SignInPrompt } from '../components/common'

export function TripsPage({ token, signedIn, openAuth, notify, navigate }) {
  const [trips, setTrips] = useState([])
  const [loading, setLoading] = useState(signedIn)
  const [error, setError] = useState('')
  // Reloads on open and whenever a trip is boarded or ended, so a finished trip shows up straight away.
  const load = useCallback(() => {
    if (!signedIn) return
    setLoading((current) => current || !trips.length)
    request('/trip/user', { token })
      .then((data) => { setTrips(data.data || []); setError('') })
      .catch((e) => setError(errorText(e)))
      .finally(() => setLoading(false))
  }, [token, signedIn]) // eslint-disable-line react-hooks/exhaustive-deps
  useTripChanges(load)
  const completed = trips.filter((trip) => trip.status !== 'active')
  const totals = { trips: completed.length, fare: completed.reduce((sum, trip) => sum + Number(trip.fare || 0), 0), gems: trips.reduce((sum, trip) => sum + Number(trip.gemsEarned || 0), 0) }
  return <div className="page inner-page trips-page">
    <PageHeading eyebrow={t('YOUR JOURNEY, ALL IN ONE PLACE')} title={t('Trip history.')} description={t('A little record of all the places you have been.')} icon={History} />
    {!signedIn ? <SignInPrompt openAuth={openAuth} message={t('Sign in to see your recent journeys.')} /> : <>
      <ActiveTripPanel token={token} signedIn={signedIn} notify={notify} />
      {loading ? <div className="empty-state"><span className="spinner" /> {t('Loading your trips…')}</div>
        : error ? <div className="empty-state panel"><span className="empty-icon"><History size={22} /></span><b>{t('Trip history unavailable')}</b><span>{error}</span><button className="button button-outline button-small" onClick={load}>{t('Try again')}</button></div>
        : trips.length ? <>
          <div className="trip-totals">
            <span><small>{t('Completed trips')}</small><b>{formatNumber(totals.trips)}</b></span>
            <span><small>{t('Spent on fares')}</small><b>{money(totals.fare)}</b></span>
            <span><small>{t('Gems earned')}</small><b>{formatNumber(totals.gems)}</b></span>
          </div>
          <div className="trip-list panel">{trips.map((trip) => <TripRow key={trip._id} trip={trip} onOpenRoute={(route) => navigate('Bus details', { route })} />)}</div>
          <p className="trip-list-hint">{t('Tap a trip to see every detail of the ride.')}</p>
        </>
        : <div className="empty-state panel"><span className="empty-icon"><History size={22} /></span><b>{t('Your next trip starts here')}</b><span>{t('Board a bus from the search results and your trips will show up here.')}</span><button className="button button-green button-small" onClick={() => navigate('Find a bus')}><Search size={14} /> {t('Find a bus')}</button></div>}
    </>}
  </div>
}
