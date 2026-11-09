import React from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import { BusPhoto } from './BusPhoto'
import { forgetUploadedPhotos, freePhotoFor } from '../../models/busPhotos'

const json = (body) => Promise.resolve(new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }))
const stubPhotos = (data) => vi.stubGlobal('fetch', vi.fn(() => json({ success: true, data })))

afterEach(() => { cleanup(); vi.unstubAllGlobals(); forgetUploadedPhotos() })

describe('bus photos', () => {
  it('prefers a photo of the very route, then the operator, and leaves other vehicles out', () => {
    expect(freePhotoFor({ routeNumber: 'DBR-41', busName: 'BRTC' }).caption).toMatch(/Tongi/)
    expect(freePhotoFor({ routeNumber: 'DBR-43', busName: 'BRTC' }).caption).toMatch(/Shahbag/)
    expect(freePhotoFor({ routeNumber: 'DBR-49', busName: 'BRTC Articulated' })).toBeNull()
    expect(freePhotoFor({ routeNumber: 'DBR-58', busName: 'Dewan' })).toBeNull()
  })

  it('shows visitors nothing when a route has no real photo: no drawing and no links', () => {
    stubPhotos([])
    const { container } = render(<BusPhoto bus={{ _id: 'b1', routeNumber: 'DBR-58', busName: 'Dewan' }} />)
    expect(container.innerHTML).toBe('')
  })

  it('offers admins an upload box when a route has no real photo', () => {
    stubPhotos([])
    render(<BusPhoto bus={{ _id: 'b1', routeNumber: 'DBR-58', busName: 'Dewan' }} isAdmin token="x" />)
    expect(screen.getByRole('button', { name: /Upload a real photo of this bus/ })).toBeTruthy()
    expect(screen.queryByRole('link')).toBeNull()
  })

  it('shows an uploaded photo with its credit, and replace controls to admins', async () => {
    stubPhotos([{ busId: 'b2', url: '/bus/b2/photo?v=1', credit: 'Rahim' }])
    render(<BusPhoto bus={{ _id: 'b2', routeNumber: 'DBR-2', busName: 'Turag' }} isAdmin token="x" />)
    await waitFor(() => expect(screen.getByRole('img', { name: /Turag/ }).getAttribute('src')).toMatch(/\/bus\/b2\/photo\?v=1$/))
    expect(screen.getByText('Photo: Rahim')).toBeTruthy()
    expect(screen.getByText('Replace photo')).toBeTruthy()
  })
})
