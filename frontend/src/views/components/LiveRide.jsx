import React, { useMemo, useState } from 'react'
import { Crosshair, Flag, Gauge, LocateFixed, MapPin, Navigation, Square } from 'lucide-react'
import { formatNumber, t } from '../../models/i18n'
import { rideStops, tripProgress } from '../../models/liveTrip'
import { useLivePosition } from '../../controllers/useLivePosition'
import { usePersistentState } from '../../controllers/usePersistentState'

const LiveTripMap = React.lazy(() => import('./LiveTripMap'))

// The live map while on a bus: started by the rider (it asks for their location and uses battery), then it
// follows them, says the next stop and how far it is to their stop. It stays on when they leave the page and
// come back, until they turn it off or end the trip. The location never leaves this device.
//   route        the bus route ({ stops: [{ name, lat, lng, order }] })
//   source       where they got on
//   destination  where they get off ('' when not chosen yet: then towards the end of the route)
const km = (value) => value < 1 ? t('{m} m', { m: formatNumber(Math.round(value * 1000 / 10) * 10) }) : t('{km} km', { km: formatNumber(value, { maximumFractionDigits: 1 }) })

export function LiveRide({ route, source, destination }) {
  const [on, setOn] = usePersistentState('trip:live-map', false)
  const [follow, setFollow] = useState(true)
  const { status, position } = useLivePosition(on)

  const allStops = useMemo(() => [...(route?.stops || [])].sort((a, b) => (a.order ?? 0) - (b.order ?? 0))
    .map((s) => ({ name: s.name, lat: Number(s.lat), lng: Number(s.lng) })).filter((s) => Number.isFinite(s.lat) && Number.isFinite(s.lng)), [route])
  const ride = useMemo(() => rideStops(route?.stops, source, destination), [route, source, destination])
  const progress = useMemo(() => tripProgress(ride, position), [ride, position])
  const end = ride[ride.length - 1]

  if (!ride.length) return null
  if (!on) return <div className="live-ride-start">
    <span className="live-ride-start-icon" aria-hidden="true"><Navigation size={18} /></span>
    <div><b>{t('Follow your ride on a live map')}</b><small>{t('See where you are, the next stop and how far it is to your stop. Your location stays on this device.')}</small></div>
    <button type="button" className="button button-green button-small" onClick={() => { setOn(true); setFollow(true) }}><MapPin size={14} /> {t('Start live map')}</button>
  </div>

  const speed = position?.speed != null && position.speed >= 0 ? Math.round(position.speed * 3.6) : null
  return <section className="live-ride" aria-label={t('Live map of your ride')} data-testid="live-ride">
    <div className="live-ride-head">
      <span className="live-ride-badge"><i aria-hidden="true" /> {status === 'live' ? t('LIVE') : t('Waiting for location')}</span>
      <div className="live-ride-tools">
        {!follow && position && <button type="button" className="button button-outline button-small" onClick={() => setFollow(true)}><LocateFixed size={14} /> {t('Re-center')}</button>}
        <button type="button" className="button button-outline button-small" onClick={() => setOn(false)}><Square size={12} /> {t('Stop live map')}</button>
      </div>
    </div>

    <div className="live-ride-map">
      <React.Suspense fallback={<div className="live-trip-map" />}>
        <LiveTripMap route={allStops} ride={ride} position={position} passed={progress?.passed ?? -1} follow={follow} onUserMove={() => setFollow(false)} />
      </React.Suspense>
      {status === 'asking' && <p className="live-ride-overlay"><Crosshair size={16} aria-hidden="true" /> {t('Finding your location… Allow location if your browser asks.')}</p>}
      {status === 'denied' && <p className="live-ride-overlay is-error" role="alert">{t('Location permission was denied, so the map cannot follow you. Allow location for this site in your browser settings.')}</p>}
      {status === 'unavailable' && <p className="live-ride-overlay is-error" role="alert">{t('Your location could not be found. Check that location is turned on.')}</p>}
    </div>

    {progress && <div className="live-ride-stats" aria-live="polite" data-testid="live-ride-stats">
      {progress.arrived ? <div className="live-ride-arrived"><Flag size={16} aria-hidden="true" /> {t('You have reached {stop}. Remember to end your trip.', { stop: t(end.name) })}</div>
        : progress.offRoute ? <div className="live-ride-off">{t('You seem to be away from this bus route. The map keeps following you.')}</div>
          : <>
            <div><small>{t('Next stop')}</small><b>{t(progress.next.name)}</b><em>{km(progress.toNextKm)}</em></div>
            <div><small>{t('To {stop}', { stop: t(end.name) })}</small><b>{km(progress.toEndKm)}</b><em>{progress.stopsLeft === 1 ? t('1 stop left') : t('{count} stops left', { count: progress.stopsLeft })}</em></div>
            {speed !== null && <div><small>{t('Speed')}</small><b><Gauge size={15} aria-hidden="true" /> {t('{n} km/h', { n: formatNumber(speed) })}</b></div>}
          </>}
    </div>}
  </section>
}
