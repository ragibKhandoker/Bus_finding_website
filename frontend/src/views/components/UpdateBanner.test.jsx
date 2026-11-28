import React from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { UpdateBanner } from './UpdateBanner'

const serve = (buildId) => vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(new Response(JSON.stringify({ buildId }), { status: 200 }))))
afterEach(() => { cleanup(); vi.unstubAllGlobals() })

describe('UpdateBanner', () => {
  it('offers a refresh when a newer build is deployed', async () => {
    serve('build-2')
    render(<UpdateBanner enabled current="build-1" />)
    expect(await screen.findByText('A new version of SmartCommute is ready.')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Refresh' })).toBeTruthy()
    expect(fetch.mock.calls[0][0]).toMatch(/^\/version\.json\?t=\d+$/)
  })

  it('stays hidden while the page is the deployed version', async () => {
    serve('build-1')
    render(<UpdateBanner enabled current="build-1" />)
    await new Promise((r) => setTimeout(r, 30))
    expect(screen.queryByRole('status')).toBeNull()
  })

  it('does nothing in development', async () => {
    serve('build-2')
    render(<UpdateBanner enabled={false} current="build-1" />)
    await new Promise((r) => setTimeout(r, 30))
    expect(fetch).not.toHaveBeenCalled()
  })
})
