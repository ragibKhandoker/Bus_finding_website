import { t } from './i18n'

// Server messages are in English; the ones with a Bangla entry are shown translated.
export const errorText = (error) => t(error?.message || 'Something went wrong. Please try again.')
