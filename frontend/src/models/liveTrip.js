// Where the rider is on their bus ride, from their phone's location: the next stop, how far to it and to the stop
// where they get off, and how many stops are left. Plain functions, no React, so they are easy to test.
//
// Distances are straight lines between points (a little shorter than the road), which is enough to say
// "next stop" and "about 3 km to go".

// Within this distance of a stop counts as being at it.
export const AT_STOP_KM = 0.15
// Farther than this from every stop of the ride counts as off the route (e.g. GPS is wrong, or the wrong bus).
export const OFF_ROUTE_KM = 1.5

export function distanceKm(a, b) {
  const rad = Math.PI / 180
  const h = Math.sin((b.lat - a.lat) * rad / 2) ** 2 + Math.cos(a.lat * rad) * Math.cos(b.lat * rad) * Math.sin((b.lng - a.lng) * rad / 2) ** 2
  return 12742 * Math.asin(Math.min(1, Math.sqrt(h)))
}

const same = (a, b) => (a || '').trim().toLowerCase() === (b || '').trim().toLowerCase()
const hasPoint = (s) => Number.isFinite(Number(s?.lat)) && Number.isFinite(Number(s?.lng))

// The stops of this ride in the order the bus passes them: from where the rider got on to where they get off.
// With no destination chosen yet, to the end of the route in the direction of travel (the far end).
// stops: the whole route in stored order, [{ name, lat, lng, order }].
export function rideStops(stops, from, to) {
  const route = [...(stops || [])].filter(hasPoint).sort((a, b) => (a.order ?? 0) - (b.order ?? 0))
    .map((s) => ({ name: s.name, lat: Number(s.lat), lng: Number(s.lng) }))
  const i = route.findIndex((s) => same(s.name, from))
  if (i < 0) return []
  let j = to ? route.findIndex((s) => same(s.name, to)) : -1
  if (j < 0 || j === i) j = i < route.length - 1 ? route.length - 1 : 0
  return i < j ? route.slice(i, j + 1) : route.slice(j, i + 1).reverse()
}

// Progress along the ride for a position { lat, lng }:
//   next         the next stop ahead (null once arrived)
//   toNextKm     distance to it
//   toEndKm      distance to the last stop, along the stops
//   stopsLeft    stops still to pass, counting the last one
//   passed       index in ride of the last stop reached
//   arrived      at the stop where the rider gets off
//   offRoute     too far from every stop to say
export function tripProgress(ride, position) {
  if (!ride?.length || !position) return null
  const last = ride.length - 1
  const away = ride.map((s) => distanceKm(position, s))
  const nearest = away.indexOf(Math.min(...away))
  if (away[nearest] > OFF_ROUTE_KM) return { offRoute: true, next: null, toNextKm: null, toEndKm: null, stopsLeft: null, passed: -1, arrived: false }
  if (away[last] <= AT_STOP_KM) return { offRoute: false, next: null, toNextKm: 0, toEndKm: 0, stopsLeft: 0, passed: last, arrived: true }

  // Heading for the nearest stop, or already past it and on the way to the one after?
  let next = nearest
  if (away[nearest] <= AT_STOP_KM) next = Math.min(nearest + 1, last)
  else if (nearest < last && away[nearest + 1] < distanceKm(ride[nearest], ride[nearest + 1])) next = nearest + 1

  let toEndKm = away[next]
  for (let k = next; k < last; k++) toEndKm += distanceKm(ride[k], ride[k + 1])
  return { offRoute: false, next: ride[next], toNextKm: away[next], toEndKm, stopsLeft: last - next + 1, passed: next - 1, arrived: false }
}
