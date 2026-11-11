import { t } from './i18n'

// Fields the profile form edits, in the shape PUT /auth/profile accepts.
export const PROFILE_FIELDS = ['name', 'email', 'phone', 'whatsApp', 'gender', 'homeArea', 'workArea', 'bio']

// The server keeps WhatsApp numbers as international digits ("8801712345678"); show them the way people write them.
export function formatWhatsApp(number) {
  const digits = String(number || '').replace(/\D/g, '')
  if (!digits) return ''
  return /^8801\d{9}$/.test(digits) ? `+880 ${digits.slice(3, 7)}-${digits.slice(7)}` : `+${digits}`
}

export const profileFormOf = (user) => Object.fromEntries(PROFILE_FIELDS.map((key) => [key, key === 'whatsApp' ? formatWhatsApp(user?.whatsApp) : user?.[key] || (key === 'gender' ? 'Prefer not to say' : '')]))

export const GENDERS = ['Prefer not to say', 'Male', 'Female', 'Other']

export const DHAKA_AREAS = ['Uttara', 'Mirpur', 'Mohakhali', 'Banani', 'Gulshan', 'Badda', 'Rampura', 'Farmgate', 'Dhanmondi', 'Mohammadpur', 'Motijheel', 'Gulistan', 'Shahbagh', 'Tejgaon', 'Bashundhara', 'Jatrabari', 'Old Dhaka', 'Savar']

export const BIO_LIMIT = 200

// Same rules the server applies to PUT /auth/profile.
export function profileProblem({ name, email, phone, whatsApp = '', bio }) {
  if (name.trim().length < 2 || name.trim().length > 100) return t('Name must be between 2 and 100 characters.')
  if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email.trim())) return t('Please enter a valid email address.')
  if (phone.trim() && !/^\+?[0-9][0-9 -]{5,18}$/.test(phone.trim())) return t('Please enter a valid phone number.')
  if (whatsApp.trim() && !/^\+?[0-9][0-9 ()-]{6,18}$/.test(whatsApp.trim())) return t('Enter a valid WhatsApp number, like 01712-345678 or +8801712345678.')
  if (bio.length > BIO_LIMIT) return t('Bio must be at most {count} characters.', { count: BIO_LIMIT })
  return ''
}

// Same rules the server applies to /auth/signup, checked first so the user gets instant feedback.
export function signupProblem({ name, email, password, confirmPassword, role }) {
  if (name.length < 2 || name.length > 100) return 'Name must be between 2 and 100 characters.'
  if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email)) return 'Please enter a valid email address.'
  if (password.length < 6) return 'Password must be at least 6 characters.'
  if (!/[A-Za-z]/.test(password) || !/\d/.test(password)) return 'Password must contain at least one letter and one number.'
  if (password !== confirmPassword) return 'Passwords do not match.'
  // Drivers register through the driver registration form (F21), never this one.
  if (role !== 'commuter') return 'Role must be commuter.'
  return ''
}

// text is shown as a tooltip on the role toggle.
export const ROLE_OPTIONS = [
  { value: 'commuter', title: 'Commuter', text: 'I travel by bus around the city' },
  { value: 'driver', title: 'Driver', text: 'I drive a bus or offer rides' },
]
