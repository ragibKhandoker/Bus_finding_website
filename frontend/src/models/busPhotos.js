// Photos of bus routes. A route's page shows, in order of preference:
//   1. the photo an admin uploaded (GET /bus/photos lists them, GET /bus/{id}/photo serves one),
//   2. a freely licensed photo from Wikimedia Commons of that operator's bus (FREE_PHOTOS below, with credit),
//   3. a drawn bus front carrying the route number and name (components/BusPhoto).
// Only photos whose licence allows reuse are listed in FREE_PHOTOS, and each shows its author and licence.
import { API_BASE, apiRequest } from './api'

// `src` is a Commons thumbnail (Commons only serves standard widths such as 960px), `page` the file's page where its
// licence is stated, `credit` the attribution the licence asks for, and `caption` says honestly what the photo
// shows: an operator photo is "a BRTC bus", not this route's bus, unless the sign in the photo shows this route.
const commons = (file, thumb) => ({
  src: `https://upload.wikimedia.org/wikipedia/commons/thumb/${thumb}/960px-${file}`,
  page: `https://commons.wikimedia.org/wiki/File:${file}`,
})
const BRTC_SHAHBAG = { ...commons('BRTC_Bus_at_shahbag.jpg', '3/3b/BRTC_Bus_at_shahbag.jpg'), credit: 'Wasiul Bahar, CC BY-SA 4.0', caption: 'A BRTC double-decker at Shahbag, Dhaka' }
const BRTC_TONGI_MOTIJHEEL = { ...commons('BRTC_bus_at_arambag.jpg', '2/23/BRTC_bus_at_arambag.jpg'), credit: 'Wasiul Bahar, CC BY-SA 4.0', caption: 'A BRTC Tongi–Motijheel bus at Arambag, Dhaka' }

// A photo that shows this very route (its destination sign), by route number.
export const FREE_PHOTOS_BY_ROUTE = {
  'DBR-41': BRTC_TONGI_MOTIJHEEL, // BRTC Motijheel – Tongi; the sign in the photo reads "টঙ্গী টু মতিঝিল"
}

// A photo of the operator's bus, by the route's busName as stored in the database. "BRTC Elevated Expressway" and
// "BRTC Articulated" run other vehicles, so they are deliberately not covered by "BRTC".
export const FREE_PHOTOS = {
  'BRTC': BRTC_SHAHBAG,
}

export const freePhotoFor = (bus) => (bus?.routeNumber && FREE_PHOTOS_BY_ROUTE[bus.routeNumber]) || (bus?.busName && FREE_PHOTOS[bus.busName]) || null

// Uploaded photos, fetched once per tab and shared by every route page.
let uploaded = null
// Drops the cached list (tests use it; the page reloads with { fresh: true } after a change instead).
export const forgetUploadedPhotos = () => { uploaded = null }
export function loadUploadedPhotos({ fresh = false } = {}) {
  if (!uploaded || fresh) {
    uploaded = apiRequest('/bus/photos')
      .then((data) => new Map((data.data || []).map((photo) => [photo.busId, { ...photo, url: `${API_BASE}${photo.url}` }])))
      .catch((error) => { uploaded = null; throw error })
  }
  return uploaded
}

export const busPhotoApi = {
  save: (busId, image, credit, token) => apiRequest(`/admin/buses/${busId}/photo`, { method: 'PUT', token, body: JSON.stringify({ image, credit: credit || null }) }),
  remove: (busId, token) => apiRequest(`/admin/buses/${busId}/photo`, { method: 'DELETE', token }),
}

// Reads a picked file and returns it as a JPEG data URL no wider than maxSide, so phone photos (often 5–10 MB)
// upload quickly and stay well under the server's limit.
export function prepareUpload(file, maxSide = 1600) {
  return new Promise((resolve, reject) => {
    if (!/^image\/(jpeg|png|webp)$/.test(file?.type || '')) { reject(new Error('Choose a JPEG, PNG or WebP image.')); return }
    const url = URL.createObjectURL(file)
    const img = new Image()
    img.onload = () => {
      const scale = Math.min(1, maxSide / Math.max(img.naturalWidth, img.naturalHeight))
      const canvas = document.createElement('canvas')
      canvas.width = Math.round(img.naturalWidth * scale)
      canvas.height = Math.round(img.naturalHeight * scale)
      canvas.getContext('2d').drawImage(img, 0, 0, canvas.width, canvas.height)
      URL.revokeObjectURL(url)
      resolve(canvas.toDataURL('image/jpeg', 0.86))
    }
    img.onerror = () => { URL.revokeObjectURL(url); reject(new Error('That image could not be read.')) }
    img.src = url
  })
}
