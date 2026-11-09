import React, { useEffect, useRef, useState } from 'react'
import { ExternalLink, ImagePlus, Trash2, X } from 'lucide-react'
import { busPhotoApi, freePhotoFor, loadUploadedPhotos, prepareUpload } from '../../models/busPhotos'
import { errorText } from '../../models/messages'
import { t } from '../../models/i18n'

// The route's real photo: the one an admin uploaded, otherwise a freely licensed photo of the operator's bus.
// Only real photos are shown. A route without one shows nothing to visitors (the page lays out without the photo
// column) and an upload box to admins.
export function BusPhoto({ bus, isAdmin, token, notify }) {
  const [uploaded, setUploaded] = useState(null)
  const [broken, setBroken] = useState(false)
  const [draft, setDraft] = useState(null) // { image, credit } while an admin is uploading
  const [busy, setBusy] = useState(false)
  const fileInput = useRef(null)

  useEffect(() => {
    let live = true
    loadUploadedPhotos().then((photos) => { if (live) setUploaded(photos.get(bus._id) || null) }).catch(() => {})
    return () => { live = false }
  }, [bus._id])

  const free = freePhotoFor(bus)
  const photo = !broken && (uploaded ? { src: uploaded.url, credit: uploaded.credit } : free)
  if (!photo && !isAdmin) return null

  const pick = async (event) => {
    const file = event.target.files?.[0]
    event.target.value = ''
    if (!file) return
    try { setDraft({ image: await prepareUpload(file), credit: '' }) }
    catch (error) { notify?.(errorText(error)) }
  }
  const refresh = async () => {
    const photos = await loadUploadedPhotos({ fresh: true })
    setUploaded(photos.get(bus._id) || null)
    setBroken(false)
  }
  const save = async (event) => {
    event.preventDefault()
    setBusy(true)
    try { await busPhotoApi.save(bus._id, draft.image, draft.credit.trim(), token); setDraft(null); await refresh(); notify?.(t('Bus photo saved.')) }
    catch (error) { notify?.(errorText(error)) }
    finally { setBusy(false) }
  }
  const remove = async () => {
    if (!window.confirm(t('Remove the photo of {name}?', { name: t(bus.busName) }))) return
    setBusy(true)
    try { await busPhotoApi.remove(bus._id, token); await refresh(); notify?.(t('Bus photo removed.')) }
    catch (error) { notify?.(errorText(error)) }
    finally { setBusy(false) }
  }

  return <figure className="bus-photo">
    {draft
      ? <div className="bus-photo-frame"><img src={draft.image} alt={t('Preview of the new photo')} /></div>
      : photo
        ? <div className="bus-photo-frame"><img src={photo.src} alt={t('Front of a {name} bus', { name: t(bus.busName) })} loading="lazy" onError={() => setBroken(true)} /></div>
        : <button type="button" className="bus-photo-frame bus-photo-empty" disabled={busy} onClick={() => fileInput.current?.click()}>
          <ImagePlus size={26} /><b>{t('Upload a real photo of this bus')}</b><span>{t('Only visible to admins until a photo is added.')}</span>
        </button>}

    {!draft && photo && <figcaption>
      {photo.page
        ? <>{photo.caption && <span className="bus-photo-caption">{t(photo.caption)}</span>}
          <a href={photo.page} target="_blank" rel="noreferrer">{t('Photo: {credit}', { credit: photo.credit })} <ExternalLink size={11} /></a></>
        : <span>{photo.credit ? t('Photo: {credit}', { credit: photo.credit }) : t('Photo of this route')}</span>}
    </figcaption>}

    {isAdmin && (draft
      ? <form className="bus-photo-admin" onSubmit={save}>
        <input value={draft.credit} maxLength={200} onChange={(e) => setDraft({ ...draft, credit: e.target.value })} placeholder={t('Photo credit (who took it), optional')} aria-label={t('Photo credit')} />
        <div>
          <button type="submit" className="button button-green button-small" disabled={busy}>{busy ? <span className="favorite-spinner" /> : <ImagePlus size={14} />} {t('Save photo')}</button>
          <button type="button" className="button button-outline button-small" disabled={busy} onClick={() => setDraft(null)}><X size={14} /> {t('Cancel')}</button>
        </div>
      </form>
      : <div className="bus-photo-admin">
        <input ref={fileInput} type="file" accept="image/jpeg,image/png,image/webp" hidden onChange={pick} />
        {photo && <div>
          <button type="button" className="button button-outline button-small" disabled={busy} onClick={() => fileInput.current?.click()}><ImagePlus size={14} /> {uploaded ? t('Replace photo') : t('Upload photo')}</button>
          {uploaded && <button type="button" className="button button-outline button-small bus-photo-remove" disabled={busy} onClick={remove}><Trash2 size={14} /> {t('Remove')}</button>}
        </div>}
      </div>)}
  </figure>
}
