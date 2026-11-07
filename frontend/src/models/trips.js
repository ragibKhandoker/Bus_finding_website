
// Fired after boarding or ending a trip, so every open panel (bus page, trips page, wallet) reloads.
export const TRIP_CHANGED_EVENT = 'smartcommute:trip-changed'

export const announceTripChange = () => window.dispatchEvent(new Event(TRIP_CHANGED_EVENT))

export const stopsOf = (route) => [...(route?.stops || [])].sort((a, b) => (a.order ?? 0) - (b.order ?? 0)).map((stop) => stop.name)

// Stops the rider can get off at, given where they got on: every other stop, or only later ones on a one-way route.
export const destinationsFrom = (stops, source, runsBothWays) => {
  const from = stops.indexOf(source)
  return stops.filter((stop, index) => index !== from && (runsBothWays !== false || index > from))
}
