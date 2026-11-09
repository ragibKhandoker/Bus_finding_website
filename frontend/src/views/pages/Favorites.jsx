import React from 'react'
import { Heart } from 'lucide-react'
import { formatNumber, t } from '../../models/i18n'

export { errorText } from '../../models/messages'
export { busIdOf } from '../../models/favorites'
export { addFavorite } from '../../models/favorites'
export { removeFavorite } from '../../models/favorites'
export { useFavoriteBuses } from '../../controllers/useFavorites'
export { useFavoriteStatus } from '../../controllers/useFavorites'

// Favourite buses: the heart button shared by the bus cards and the route page. The server takes the user from the token, so nothing here sends a user id.

export const money = (value) => `৳${formatNumber(value)}`

// Outline heart when not saved, filled when saved, a spinner while the request runs.
export function FavoriteButton({ active, pending, onClick, withLabel = false, className = '' }) {
  const label = active ? t('Remove from favourite buses') : t('Add to favourite buses')
  return <button type="button" className={`favorite${active ? ' saved' : ''}${withLabel ? ' with-label' : ''} ${className}`.trim()} onClick={onClick} disabled={pending} aria-pressed={active} aria-busy={pending} aria-label={label} title={label}>
    {pending ? <span className="favorite-spinner" aria-hidden="true" /> : <Heart size={17} fill={active ? 'currentColor' : 'none'} />}
    {withLabel && <span>{active ? t('Saved to favourites') : t('Save to favourites')}</span>}
  </button>
}

// The Favourite buses page is the Smart Favourite Bus dashboard (FavouriteDashboard.jsx).
