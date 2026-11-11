// F21 Driver registration & verification: calls to the server, form constants and the same rules the server
// applies (checked here first only for quick feedback; the server checks everything again).
import { apiBlob, apiRequest } from './api'
import { formatDate, t } from './i18n'

const send = (method, path, token, body) => apiRequest(path, { token, method, ...(body === undefined ? {} : { body: JSON.stringify(body) }) })
const query = (params) => new URLSearchParams(Object.fromEntries(Object.entries(params).filter(([, v]) => v !== '' && v !== null && v !== undefined)))

export const driverApi = {
  register: (body) => send('POST', '/api/driver-registration/register', undefined, body),
  status: (token) => apiRequest('/api/driver-registration/status', { token }),
  myProfile: (token) => apiRequest('/api/driver-registration/my-profile', { token }),
  update: (body, token) => send('PUT', '/api/driver-registration/update', token, body),
  resubmit: (body, token) => send('POST', '/api/driver-registration/resubmit', token, body),
  dashboard: (token) => apiRequest('/api/driver/dashboard', { token }),
  assignedBus: (token) => apiRequest('/api/driver/assigned-bus', { token }),
}

export const adminDriverApi = {
  list: (filters, token) => apiRequest(`/api/admin/drivers?${query(filters)}`, { token }),
  stats: (token) => apiRequest('/api/admin/drivers/statistics', { token }),
  get: (driverId, token) => apiRequest(`/api/admin/drivers/${driverId}`, { token }),
  // Documents are private: fetched with the admin's token and shown from memory, never by a public link.
  document: (url, token) => apiBlob(url, { token }),
  startReview: (driverId, token) => send('PUT', `/api/admin/drivers/${driverId}/start-review`, token, {}),
  approve: (driverId, reason, token) => send('PUT', `/api/admin/drivers/${driverId}/approve`, token, { reason }),
  reject: (driverId, reason, token) => send('PUT', `/api/admin/drivers/${driverId}/reject`, token, { reason }),
  requestResubmission: (driverId, reason, fields, token) => send('PUT', `/api/admin/drivers/${driverId}/request-resubmission`, token, { reason, fields }),
  assignBus: (driverId, busId, token) => send('PUT', `/api/admin/drivers/${driverId}/assign-bus`, token, { busId }),
  endAssignment: (driverId, assignmentId, token) => send('PUT', `/api/admin/drivers/${driverId}/assignments/${assignmentId}/end`, token, {}),
}

export const STATUSES = ['PENDING', 'UNDER_REVIEW', 'APPROVED', 'REJECTED', 'RESUBMISSION_REQUIRED']
export const STATUS_LABELS = {
  PENDING: 'Pending', UNDER_REVIEW: 'Under review', APPROVED: 'Approved', REJECTED: 'Rejected', RESUBMISSION_REQUIRED: 'Resubmission required',
}

export const LICENSE_TYPES = ['Professional', 'Heavy Vehicle', 'Other']
export const EMPLOYMENT_TYPES = ['Full-time', 'Part-time', 'Contractual', 'Self-employed']
export const GENDERS = ['Male', 'Female', 'Other', 'Prefer not to say']

export const DISTRICTS = [
  'Bagerhat', 'Bandarban', 'Barguna', 'Barishal', 'Bhola', 'Bogura', 'Brahmanbaria', 'Chandpur', 'Chapainawabganj', 'Chattogram',
  'Chuadanga', "Cox's Bazar", 'Cumilla', 'Dhaka', 'Dinajpur', 'Faridpur', 'Feni', 'Gaibandha', 'Gazipur', 'Gopalganj',
  'Habiganj', 'Jamalpur', 'Jashore', 'Jhalokati', 'Jhenaidah', 'Joypurhat', 'Khagrachhari', 'Khulna', 'Kishoreganj', 'Kurigram',
  'Kushtia', 'Lakshmipur', 'Lalmonirhat', 'Madaripur', 'Magura', 'Manikganj', 'Meherpur', 'Moulvibazar', 'Munshiganj', 'Mymensingh',
  'Naogaon', 'Narail', 'Narayanganj', 'Narsingdi', 'Natore', 'Netrokona', 'Nilphamari', 'Noakhali', 'Pabna', 'Panchagarh',
  'Patuakhali', 'Pirojpur', 'Rajbari', 'Rajshahi', 'Rangamati', 'Rangpur', 'Satkhira', 'Shariatpur', 'Sherpur', 'Sirajganj',
  'Sunamganj', 'Sylhet', 'Tangail', 'Thakurgaon',
]

export const DOCUMENTS = [
  { type: 'license_front', label: 'Driving licence (front)', required: true },
  { type: 'license_back', label: 'Driving licence (back)', required: false },
  { type: 'nid_front', label: 'NID (front)', required: true },
  { type: 'nid_back', label: 'NID (back)', required: false },
  { type: 'profile_photo', label: 'Profile picture', required: false },
]
export const DOCUMENT_LABELS = Object.fromEntries(DOCUMENTS.map((d) => [d.type, d.label]))

// What an admin can ask a driver to correct.
export const RESUBMISSION_FIELDS = [
  { value: 'personal', label: 'Personal information' },
  { value: 'driving_details', label: 'Driving licence details' },
  { value: 'nid_number', label: 'NID number' },
  { value: 'employment', label: 'Bus & employment information' },
  { value: 'address', label: 'Address' },
  ...DOCUMENTS.map((d) => ({ value: d.type, label: d.label })),
]
export const FIELD_LABELS = Object.fromEntries(RESUBMISSION_FIELDS.map((f) => [f.value, f.label]))

export const MAX_IMAGE_BYTES = 5 * 1024 * 1024
export const IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/webp']
export const MIN_AGE = 18
export const MIN_PASSWORD = 8

// The form's steps, in order, with the fields each one holds.
export const STEPS = [
  { id: 'personal', title: 'Personal', long: 'Personal information' },
  { id: 'driving', title: 'Driving', long: 'Driving information' },
  { id: 'identity', title: 'Identity', long: 'Identity verification' },
  { id: 'bus', title: 'Bus', long: 'Bus information' },
  { id: 'address', title: 'Address', long: 'Address & contact' },
  { id: 'consent', title: 'Review', long: 'Declaration & consent' },
]

export const dhakaToday = (now = Date.now()) => new Date(now + 6 * 3_600_000).toISOString().slice(0, 10)

export const emptyApplication = () => ({
  fullName: '', email: '', phone: '', password: '', confirmPassword: '', dateOfBirth: '', gender: '',
  licenseNumber: '', licenseType: '', licenseExpiryDate: '', drivingExperienceYears: '',
  nidNumber: '',
  busId: '', busName: '', operatorName: '', route: '', busRegistrationNumber: '', employmentType: '',
  presentAddress: '', district: 'Dhaka', area: '', emergencyContact: '',
  confirmAccuracy: false, authorizeVerification: false, acceptTerms: false,
  // type → { data (data URL), name, size }
  documents: {},
})

export function normalizePhone(value) {
  let digits = String(value || '').replace(/[\s()-]/g, '')
  if (digits.startsWith('+')) digits = digits.slice(1)
  if (digits.startsWith('880')) digits = digits.slice(2)
  return /^01[3-9]\d{8}$/.test(digits) ? digits : null
}

export const ageOn = (birth, today = dhakaToday()) => {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(birth)) return NaN
  const [by, bm, bd] = birth.split('-').map(Number)
  const [ty, tm, td] = today.split('-').map(Number)
  return ty - by - (tm < bm || (tm === bm && td < bd) ? 1 : 0)
}

const clean = (value) => String(value || '').replace(/[\s-]/g, '').toUpperCase()

export const passwordRules = (password) => [
  { ok: password.length >= MIN_PASSWORD, text: `At least ${MIN_PASSWORD} characters` },
  { ok: /[A-Za-z]/.test(password), text: 'At least one letter' },
  { ok: /\d/.test(password), text: 'At least one number' },
]

// Problems in one step, by field name. Empty when the step is complete.
export function stepProblems(step, f, today = dhakaToday()) {
  const p = {}
  if (step === 'personal') {
    if (f.fullName.trim().length < 2) p.fullName = t('Enter your full name.')
    if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(f.email.trim())) p.email = t('Please enter a valid email address.')
    if (!normalizePhone(f.phone)) p.phone = t('Enter a valid Bangladesh mobile number, like 01712-345678.')
    if (passwordRules(f.password).some((r) => !r.ok)) p.password = t('Use at least 8 characters with a letter and a number.')
    if (f.confirmPassword !== f.password || !f.confirmPassword) p.confirmPassword = t('Passwords do not match.')
    const age = ageOn(f.dateOfBirth, today)
    if (Number.isNaN(age)) p.dateOfBirth = t('Enter your date of birth.')
    else if (f.dateOfBirth > today) p.dateOfBirth = t('Date of birth cannot be in the future.')
    else if (age < MIN_AGE) p.dateOfBirth = t('Drivers must be at least {age} years old.', { age: MIN_AGE })
    if (!GENDERS.includes(f.gender)) p.gender = t('Choose your gender.')
  }
  if (step === 'driving') {
    const number = clean(f.licenseNumber)
    if (!number) p.licenseNumber = t('Enter your driving licence number.')
    else if (!/^[A-Z0-9][A-Z0-9/-]{4,23}[A-Z0-9]$/.test(number)) p.licenseNumber = t('Enter the number exactly as printed (6 to 25 letters and digits).')
    if (!LICENSE_TYPES.includes(f.licenseType)) p.licenseType = t('Choose the licence type.')
    if (!/^\d{4}-\d{2}-\d{2}$/.test(f.licenseExpiryDate)) p.licenseExpiryDate = t('Enter the licence expiry date.')
    else if (f.licenseExpiryDate <= today) p.licenseExpiryDate = t('Your licence has expired. The expiry date must be in the future.')
    const years = f.drivingExperienceYears === '' ? NaN : Number(f.drivingExperienceYears)
    if (!Number.isInteger(years)) p.drivingExperienceYears = t('Enter your driving experience in whole years.')
    else if (years < 0) p.drivingExperienceYears = t('Driving experience cannot be negative.')
    else if (years > 60) p.drivingExperienceYears = t('Driving experience must be at most 60 years.')
    if (!f.documents.license_front) p.license_front = t('Upload the front of your driving licence.')
  }
  if (step === 'identity') {
    const nid = clean(f.nidNumber)
    if (!nid) p.nidNumber = t('Enter your NID number.')
    else if (!/^(\d{10}|\d{13}|\d{17})$/.test(nid)) p.nidNumber = t('Enter a valid NID number (10, 13 or 17 digits).')
    if (!f.documents.nid_front) p.nid_front = t('Upload the front of your NID.')
  }
  if (step === 'bus') {
    if (!f.busId) {
      if (f.busName.trim().length < 2) p.busName = t('Enter the bus name.')
      if (f.operatorName.trim().length < 2) p.operatorName = t('Enter the bus operator or company name.')
      if (f.route.trim().length < 2) p.route = t('Enter the route the bus runs on.')
    }
    if (!EMPLOYMENT_TYPES.includes(f.employmentType)) p.employmentType = t('Choose your employment type.')
    if (f.busRegistrationNumber.trim() && f.busRegistrationNumber.trim().length < 3) p.busRegistrationNumber = t('Enter the full registration number, or leave it empty.')
  }
  if (step === 'address') {
    if (f.presentAddress.trim().length < 5) p.presentAddress = t('Enter your present address (at least 5 characters).')
    if (!DISTRICTS.includes(f.district)) p.district = t('Choose your district.')
    if (f.area.trim().length < 2) p.area = t('Enter your area or thana.')
    if (f.emergencyContact.trim() && !normalizePhone(f.emergencyContact)) p.emergencyContact = t('Enter a valid number, like 01712-345678, or leave it empty.')
  }
  if (step === 'consent') {
    if (!f.confirmAccuracy || !f.authorizeVerification || !f.acceptTerms) p.consent = t('Tick all three declarations to submit.')
  }
  return p
}

export const toRegistrationBody = (f) => ({
  fullName: f.fullName.trim(), email: f.email.trim(), phone: f.phone.trim(), password: f.password, confirmPassword: f.confirmPassword,
  dateOfBirth: f.dateOfBirth, gender: f.gender,
  licenseNumber: f.licenseNumber.trim(), licenseType: f.licenseType, licenseExpiryDate: f.licenseExpiryDate,
  drivingExperienceYears: Number(f.drivingExperienceYears),
  nidNumber: f.nidNumber.trim(),
  ...(f.busId ? { busId: f.busId } : { busName: f.busName.trim(), operatorName: f.operatorName.trim(), route: f.route.trim() }),
  busRegistrationNumber: f.busRegistrationNumber.trim() || null, employmentType: f.employmentType,
  presentAddress: f.presentAddress.trim(), district: f.district, area: f.area.trim(), emergencyContact: f.emergencyContact.trim() || null,
  confirmAccuracy: f.confirmAccuracy, authorizeVerification: f.authorizeVerification, acceptTerms: f.acceptTerms,
  documents: Object.fromEntries(Object.entries(f.documents).filter(([, d]) => d?.data).map(([type, d]) => [type, d.data])),
})

export function fileProblem(file) {
  if (!file) return t('Choose an image.')
  if (!IMAGE_TYPES.includes(file.type)) return t('Only JPG, PNG or WebP images are allowed.')
  if (file.size === 0) return t('The image is empty.')
  if (file.size > MAX_IMAGE_BYTES) return t('The image must be at most {mb} MB.', { mb: MAX_IMAGE_BYTES / (1024 * 1024) })
  return ''
}

export const readAsDataUrl = (file) => new Promise((resolve, reject) => {
  const reader = new FileReader()
  reader.onload = () => resolve(reader.result)
  reader.onerror = () => reject(new Error(t('That image could not be read.')))
  reader.readAsDataURL(file)
})

export const fileSize = (bytes) => bytes >= 1024 * 1024 ? `${(bytes / (1024 * 1024)).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`
export const day = (value) => (value ? formatDate(value.length === 10 ? `${value}T00:00:00` : value, { day: 'numeric', month: 'short', year: 'numeric' }) : '—')
export const dayTime = (value) => (value ? formatDate(value, { day: 'numeric', month: 'short', year: 'numeric', hour: 'numeric', minute: '2-digit' }) : '—')
