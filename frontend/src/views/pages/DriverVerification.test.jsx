import React from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RegistrationTypeSelectionPage } from './RegistrationTypeSelectionPage'
import { DriverVerificationStatusPage } from './DriverVerificationStatusPage'
import { AdminDriverVerificationPage } from './AdminDriverVerificationPage'
import { AdminDriverApplicationDetailsPage } from './AdminDriverApplicationDetailsPage'
import { DriverRegistrationForm } from '../components/driver/DriverRegistrationForm'
import { ageOn, normalizePhone, stepProblems, emptyApplication, toRegistrationBody } from '../../models/driver'

let routes, calls
const reply = (status, body) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))

beforeEach(() => {
  calls = []; routes = {}
  localStorage.clear()
  Element.prototype.scrollIntoView = () => {}
  URL.createObjectURL = () => 'blob:doc'
  URL.revokeObjectURL = () => {}
  vi.stubGlobal('fetch', vi.fn((url, options = {}) => {
    const { pathname, search } = new URL(url)
    const method = options.method || 'GET'
    const body = options.body && JSON.parse(options.body)
    calls.push({ method, path: pathname, search, body })
    const handler = routes[`${method} ${pathname}`]
    return handler ? handler({ body, search }) : reply(404, { success: false, message: 'Not found.' })
  }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

describe('driver form rules', () => {
  it('normalises Bangladesh mobile numbers', () => {
    expect(normalizePhone('+880 1712-345678')).toBe('01712345678')
    expect(normalizePhone('01212345678')).toBeNull()
  })

  it('refuses underage drivers and expired licences', () => {
    expect(ageOn('2008-11-12', '2026-11-11')).toBe(17)
    const form = { ...emptyApplication(), dateOfBirth: '2009-01-01' }
    expect(stepProblems('personal', form, '2026-11-11').dateOfBirth).toMatch(/at least 18/)
    const driving = { ...emptyApplication(), licenseNumber: 'DK0123456L00001', licenseType: 'Professional', licenseExpiryDate: '2026-01-01', drivingExperienceYears: '-1' }
    const p = stepProblems('driving', driving, '2026-11-11')
    expect(p.licenseExpiryDate).toMatch(/expired/)
    expect(p.drivingExperienceYears).toMatch(/negative/)
    expect(p.license_front).toBeTruthy()
  })

  it('sends a chosen bus by id only, never its typed details', () => {
    const body = toRegistrationBody({ ...emptyApplication(), busId: 'b1', busName: 'Typed', drivingExperienceYears: '4', documents: { nid_front: { data: 'data:x' } } })
    expect(body.busId).toBe('b1')
    expect(body.busName).toBeUndefined()
    expect(body.documents).toEqual({ nid_front: 'data:x' })
    expect(body).not.toHaveProperty('role')
    expect(body).not.toHaveProperty('verificationStatus')
  })
})

describe('account type selection', () => {
  it('offers commuter and driver registration', async () => {
    const user = userEvent.setup()
    const navigate = vi.fn(), openCommuterSignup = vi.fn()
    render(<RegistrationTypeSelectionPage navigate={navigate} openCommuterSignup={openCommuterSignup} openSignIn={() => {}} signedIn={false} />)
    await user.click(screen.getByRole('button', { name: /Register as Commuter/ }))
    expect(openCommuterSignup).toHaveBeenCalled()
    await user.click(screen.getByRole('button', { name: /Register as Driver/ }))
    expect(navigate).toHaveBeenCalledWith('Driver registration')
  })
})

describe('driver registration form', () => {
  it('does not move on until the step is complete', async () => {
    const user = userEvent.setup()
    render(<DriverRegistrationForm onSubmit={vi.fn()} />)
    await user.click(screen.getByRole('button', { name: /Continue/ }))
    expect(screen.getByText('Enter your full name.')).toBeTruthy()
    expect(screen.getByText(/Step 1 of 6/)).toBeTruthy()

    await user.type(screen.getByLabelText('Full name'), 'Karim Uddin')
    await user.type(screen.getByLabelText('Email address'), 'karim@test.local')
    await user.type(screen.getByLabelText('Phone number'), '01712345678')
    await user.type(screen.getByLabelText('Password'), 'Drive2026')
    await user.type(screen.getByLabelText('Confirm password'), 'Drive2026')
    await user.type(screen.getByLabelText('Date of birth'), '1990-05-01')
    await user.selectOptions(screen.getByLabelText('Gender'), 'Male')
    await user.click(screen.getByRole('button', { name: /Continue/ }))
    expect(screen.getByText(/Step 2 of 6/)).toBeTruthy()
    expect(screen.getByLabelText('Driving licence number')).toBeTruthy()
  })
})

const status = (data) => ({ success: true, data: { registered: true, applicationCode: 'DRV-1A2B3C4D', submittedAt: '2026-11-10T08:00:00Z', requestedFields: [], assignments: [], nextStep: 'Wait.', ...data } })

describe('driver verification status', () => {
  const driver = { _id: 'd1', name: 'Karim', role: 'driver' }

  it('shows the pending message with the application id', async () => {
    routes['GET /api/driver-registration/status'] = () => reply(200, status({ verificationStatus: 'PENDING', accountStatus: 'PENDING_VERIFICATION' }))
    render(<DriverVerificationStatusPage token="tok" user={driver} signedIn navigate={() => {}} notify={() => {}} />)
    expect(await screen.findByText(/Our administration team is reviewing your documents/)).toBeTruthy()
    expect(screen.getByText('DRV-1A2B3C4D')).toBeTruthy()
    expect(screen.getByRole('button', { name: /Contact support/ })).toBeTruthy()
  })

  it('shows the rejection reason', async () => {
    routes['GET /api/driver-registration/status'] = () => reply(200, status({ verificationStatus: 'REJECTED', rejectionReason: 'The licence has been cancelled.' }))
    render(<DriverVerificationStatusPage token="tok" user={driver} signedIn navigate={() => {}} notify={() => {}} />)
    expect(await screen.findByText('The licence has been cancelled.')).toBeTruthy()
    expect(screen.getByText(/Unfortunately, your application was not approved/)).toBeTruthy()
  })

  it('lets the driver resend only what was asked for', async () => {
    const user = userEvent.setup()
    routes['GET /api/driver-registration/status'] = () => reply(200, status({ verificationStatus: 'RESUBMISSION_REQUIRED', canResubmit: true, resubmissionReason: 'Address is incomplete.', requestedFields: ['address'] }))
    routes['GET /api/driver-registration/my-profile'] = () => reply(200, { success: true, data: { fullName: 'Karim', phone: '01712345678', dateOfBirth: '1990-05-01', gender: 'Male', licenseType: 'Professional',
      licenseExpiryDate: '2030-01-01', drivingExperienceYears: 5, claimedBusName: 'Raida', claimedOperatorName: 'Raida', claimedRoute: 'A – B', busRegistrationNumber: '', employmentType: 'Full-time',
      presentAddress: 'Road 1', district: 'Dhaka', area: 'Uttara', emergencyContact: '' } })
    routes['POST /api/driver-registration/resubmit'] = () => reply(200, { success: true, message: 'Your corrections were sent for review.', data: {} })
    render(<DriverVerificationStatusPage token="tok" user={driver} signedIn navigate={() => {}} notify={() => {}} />)
    expect(await screen.findByText('Address is incomplete.')).toBeTruthy()
    const address = await screen.findByLabelText('Present address')
    expect(screen.queryByLabelText('NID number')).toBeNull()
    await user.clear(address)
    await user.type(address, 'House 5, Road 7, Sector 4')
    await user.click(screen.getByRole('button', { name: /Resubmit application/ }))
    await waitFor(() => expect(calls.some((c) => c.path === '/api/driver-registration/resubmit')).toBe(true))
    expect(calls.find((c) => c.path === '/api/driver-registration/resubmit').body.presentAddress).toBe('House 5, Road 7, Sector 4')
  })
})

const row = { driverId: '3f2a9c1b-1111-4222-8333-444455556666', applicationCode: 'DRV-3F2A9C1B', fullName: 'Rahim Mia', email: 'rahim@test.local', phone: '01712345678',
  licenseNumberMasked: '••••••0001', busName: 'Turag', operatorName: 'Turag Transport', submittedAt: '2026-11-10T08:00:00Z', verificationStatus: 'PENDING' }

describe('admin driver verification', () => {
  it('shows the summary cards and a masked application table', async () => {
    const user = userEvent.setup()
    const navigate = vi.fn()
    routes['GET /api/admin/drivers/statistics'] = () => reply(200, { success: true, data: { total: 4, pending: 2, underReview: 1, approved: 1, rejected: 0, resubmissionRequired: 0 } })
    routes['GET /api/admin/drivers'] = () => reply(200, { success: true, data: [row], total: 1, totalPages: 1 })
    render(<AdminDriverVerificationPage token="tok" signedIn isAdmin navigate={navigate} />)
    expect(await screen.findByText('Rahim Mia')).toBeTruthy()
    expect(screen.getByText('••••••0001')).toBeTruthy()
    expect(screen.getByText('Pending applications')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: /View details for Rahim Mia/ }))
    expect(navigate).toHaveBeenCalledWith('Driver application', { route: row.driverId })
  })

  it('filters by status from the summary cards', async () => {
    const user = userEvent.setup()
    routes['GET /api/admin/drivers/statistics'] = () => reply(200, { success: true, data: { total: 1, pending: 1 } })
    routes['GET /api/admin/drivers'] = () => reply(200, { success: true, data: [row], total: 1, totalPages: 1 })
    render(<AdminDriverVerificationPage token="tok" signedIn isAdmin navigate={() => {}} />)
    await screen.findByText('Rahim Mia')
    await user.click(screen.getByRole('button', { name: /Rejected applications/ }))
    await waitFor(() => expect(calls.some((c) => c.path === '/api/admin/drivers' && c.search.includes('status=REJECTED'))).toBe(true))
  })

  it('is closed to non-admins', () => {
    render(<AdminDriverVerificationPage token="tok" signedIn isAdmin={false} navigate={() => {}} />)
    expect(screen.getByText('Admins only')).toBeTruthy()
    expect(calls).toHaveLength(0)
  })

  it('requires a reason and a confirmation before rejecting', async () => {
    const user = userEvent.setup()
    const details = { ...row, applicationId: 'p1', dateOfBirth: '1990-05-01', age: 36, gender: 'Male', licenseType: 'Professional', licenseExpiryDate: '2030-01-01', drivingExperienceYears: 6,
      nidNumberMasked: '••••••6789', presentAddress: 'Road 1', district: 'Dhaka', area: 'Uttara', emergencyContact: '', claimedBusName: 'Turag', claimedOperatorName: 'Turag Transport',
      claimedRoute: 'A – B', busRegistrationNumber: '', employmentType: 'Full-time', requestedFields: [], documents: [], history: [], assignments: [],
      allowedActions: ['UNDER_REVIEW', 'APPROVED', 'REJECTED', 'RESUBMISSION_REQUIRED'] }
    routes[`GET /api/admin/drivers/${row.driverId}`] = () => reply(200, { success: true, data: details })
    routes[`PUT /api/admin/drivers/${row.driverId}/reject`] = ({ body }) => reply(200, { success: true, message: 'Application rejected.', data: { ...details, verificationStatus: 'REJECTED', rejectionReason: body.reason, allowedActions: [] } })
    render(<AdminDriverApplicationDetailsPage driverId={row.driverId} token="tok" signedIn isAdmin navigate={() => {}} notify={() => {}} />)
    await user.click(await screen.findByRole('button', { name: /Reject application/ }))
    const dialog = screen.getByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Continue' }))
    expect(within(dialog).getByText(/at least 5 characters/)).toBeTruthy()
    await user.type(within(dialog).getByLabelText(/Reason/), 'Documents do not match.')
    await user.click(within(dialog).getByRole('button', { name: 'Continue' }))
    expect(calls.some((c) => c.path.endsWith('/reject'))).toBe(false) // still waiting for the confirmation
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: /Yes, reject/ }))
    await waitFor(() => expect(calls.find((c) => c.path.endsWith('/reject'))?.body).toEqual({ reason: 'Documents do not match.' }))
    expect(await screen.findByText('Rejection reason')).toBeTruthy()
  })
})
