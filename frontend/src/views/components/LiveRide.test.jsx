import React from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'

// The map itself (Leaflet) is replaced: what is tested is when it shows and what the panel says around it.
vi.mock('./LiveTripMap', () => ({ default: ({ position, follow }) => <div data-testid="live-map" data-follow={String(follow)}>{position ? `${position.lat},${position.lng}` : 'no position'}</div> }))
import { LiveRide } from './LiveRide'

const route = { stops: ['Jatrabari', 'Malibagh', 'Rampura', 'Badda'].map((name, order) => ({ name, order, lat: 23.70 + order * 0.01, lng: 90.40 })) }
let watcher
const geo = (impl) => Object.defineProperty(navigator, 'geolocation', { value: impl, configurable: true })
const fakeGeo = () => {
  watcher = {}
  geo({
    watchPosition: vi.fn((ok, fail) => { watcher.ok = ok; watcher.fail = fail; return 7 }),
    clearWatch: vi.fn(),
  })
  return navigator.geolocation
}
const move = (lat, speed = null) => act(() => watcher.ok({ coords: { latitude: lat, longitude: 90.40, accuracy: 12, speed }, timestamp: Date.now() }))

afterEach(() => { cleanup(); geo(undefined) })

describe('Live map while riding', () => {
  it('starts only when asked, then follows the rider and tells the next stop', async () => {
    const g = fakeGeo()
    const user = userEvent.setup()
    render(<LiveRide route={route} source="Malibagh" destination="Badda" />)
    expect(g.watchPosition).not.toHaveBeenCalled()

    await user.click(screen.getByRole('button', { name: /Start live map/ }))
    expect(g.watchPosition).toHaveBeenCalledTimes(1)
    expect(screen.getByText(/Finding your location/)).toBeTruthy()

    move(23.715, 6)
    expect((await screen.findByTestId('live-map')).textContent).toBe('23.715,90.4')
    const stats = screen.getByTestId('live-ride-stats')
    expect(stats.textContent).toContain('Next stop')
    expect(stats.textContent).toContain('Rampura')
    expect(stats.textContent).toContain('2 stops left')
    expect(stats.textContent).toContain('22 km/h')
    expect(screen.getByText('LIVE')).toBeTruthy()

    move(23.7301)
    expect(screen.getByTestId('live-ride-stats').textContent).toContain('You have reached Badda. Remember to end your trip.')

    await user.click(screen.getByRole('button', { name: /Stop live map/ }))
    expect(g.clearWatch).toHaveBeenCalledWith(7)
    expect(screen.getByRole('button', { name: /Start live map/ })).toBeTruthy()
  })

  it('stays on when the page is left and opened again', async () => {
    fakeGeo()
    const user = userEvent.setup()
    const { unmount } = render(<LiveRide route={route} source="Malibagh" destination="Badda" />)
    await user.click(screen.getByRole('button', { name: /Start live map/ }))
    unmount()
    render(<LiveRide route={route} source="Malibagh" destination="Badda" />)
    expect(screen.queryByRole('button', { name: /Start live map/ })).toBeNull()
    expect(screen.getByRole('button', { name: /Stop live map/ })).toBeTruthy()
  })

  it('explains when location is refused', async () => {
    fakeGeo()
    const user = userEvent.setup()
    render(<LiveRide route={route} source="Malibagh" destination="Badda" />)
    await user.click(screen.getByRole('button', { name: /Start live map/ }))
    act(() => watcher.fail({ code: 1 }))
    expect(screen.getByRole('alert').textContent).toContain('Location permission was denied')
  })

  it('shows nothing when the boarding stop is not on the route', () => {
    fakeGeo()
    const { container } = render(<LiveRide route={route} source="Gulistan" destination="" />)
    expect(container.textContent).toBe('')
  })
})
