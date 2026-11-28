import React, { useCallback, useEffect, useRef, useState } from 'react'
import { Camera, CameraOff, ImageUp, QrCode, RefreshCw, ScanLine } from 'lucide-react'
import jsQR from 'jsqr'
import { t } from '../../models/i18n'
import '../styles/qr-boarding.css'

// The camera view that reads a QR code. It uses the back camera, looks for a code a few times a second (the
// browser's own BarcodeDetector where there is one, else jsQR), and calls onResult once with the text it read.
// When the camera cannot be used (permission refused, no camera, or a page not served over https) it says why and
// offers to read the code from a photo instead.
const SCAN_EVERY_MS = 180
const MAX_SIDE = 720

export function cameraProblem(error) {
  const name = error?.name || ''
  if (name === 'NotAllowedError' || name === 'SecurityError') return t('Camera permission was refused. Allow the camera for this site in your browser settings, then try again.')
  if (name === 'NotFoundError' || name === 'OverconstrainedError') return t('No camera was found on this device.')
  if (name === 'NotReadableError' || name === 'AbortError') return t('The camera is being used by another app. Close it and try again.')
  if (name === 'insecure') return t('The camera only works on a secure (https) page. Open the SmartCommute website link to scan.')
  return t('The camera could not be started. Try again, or scan a photo of the code.')
}

// Reads a QR code from image pixels with jsQR, or returns ''.
function decodePixels(context, width, height) {
  const image = context.getImageData(0, 0, width, height)
  return jsQR(image.data, width, height, { inversionAttempts: 'attemptBoth' })?.data || ''
}

function drawScaled(source, sourceWidth, sourceHeight, canvas) {
  const scale = Math.min(1, MAX_SIDE / Math.max(sourceWidth, sourceHeight))
  canvas.width = Math.max(1, Math.round(sourceWidth * scale))
  canvas.height = Math.max(1, Math.round(sourceHeight * scale))
  const context = canvas.getContext('2d', { willReadFrequently: true })
  context.drawImage(source, 0, 0, canvas.width, canvas.height)
  return context
}

// Reads a QR code from an image file (a photo of the code), or returns ''.
export async function decodeImageFile(file) {
  const url = URL.createObjectURL(file)
  try {
    const image = await new Promise((resolve, reject) => {
      const img = new Image()
      img.onload = () => resolve(img)
      img.onerror = reject
      img.src = url
    })
    const canvas = document.createElement('canvas')
    const context = drawScaled(image, image.naturalWidth, image.naturalHeight, canvas)
    return decodePixels(context, canvas.width, canvas.height)
  } finally {
    URL.revokeObjectURL(url)
  }
}

export function QrScanner({ onResult, paused = false }) {
  const videoRef = useRef(null)
  const canvasRef = useRef(null)
  const fileRef = useRef(null)
  const done = useRef(false)
  const [state, setState] = useState({ status: 'starting', message: '' })
  const [attempt, setAttempt] = useState(0)
  const [photoNote, setPhotoNote] = useState('')

  const report = useCallback((text) => {
    if (done.current || !text) return
    done.current = true
    if (navigator.vibrate) navigator.vibrate(60)
    onResult(text)
  }, [onResult])

  useEffect(() => { if (!paused) done.current = false }, [paused])

  useEffect(() => {
    if (paused) return undefined
    let stream = null
    let timer = 0
    let stopped = false
    const detector = typeof window !== 'undefined' && 'BarcodeDetector' in window
      ? (() => { try { return new window.BarcodeDetector({ formats: ['qr_code'] }) } catch { return null } })()
      : null

    const tick = async () => {
      const video = videoRef.current
      if (stopped || !video || video.readyState < 2 || done.current) return
      try {
        if (detector) {
          const found = await detector.detect(video)
          if (found[0]?.rawValue) { report(found[0].rawValue); return }
        } else {
          const canvas = canvasRef.current
          const context = drawScaled(video, video.videoWidth, video.videoHeight, canvas)
          const text = decodePixels(context, canvas.width, canvas.height)
          if (text) { report(text); return }
        }
      } catch { /* a frame that could not be read; try the next one */ }
    }

    const start = async () => {
      setState({ status: 'starting', message: '' })
      if (!navigator.mediaDevices?.getUserMedia) {
        setState({ status: 'error', message: cameraProblem({ name: window.isSecureContext === false ? 'insecure' : 'NotFoundError' }) })
        return
      }
      try {
        stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: 'environment' }, width: { ideal: 1280 }, height: { ideal: 720 } }, audio: false })
        if (stopped) { stream.getTracks().forEach((track) => track.stop()); return }
        const video = videoRef.current
        video.srcObject = stream
        await video.play().catch(() => {})
        setState({ status: 'live', message: '' })
        timer = window.setInterval(tick, SCAN_EVERY_MS)
      } catch (error) {
        if (!stopped) setState({ status: 'error', message: cameraProblem(error) })
      }
    }
    start()
    return () => {
      stopped = true
      window.clearInterval(timer)
      stream?.getTracks().forEach((track) => track.stop())
    }
  }, [attempt, paused, report])

  const fromPhoto = async (event) => {
    const file = event.target.files?.[0]
    event.target.value = ''
    if (!file) return
    setPhotoNote('')
    try {
      const text = await decodeImageFile(file)
      if (text) report(text)
      else setPhotoNote(t('No QR code was found in that photo. Take it closer, with the whole code in the picture.'))
    } catch { setPhotoNote(t('That photo could not be opened. Try another one.')) }
  }

  return <div className="qrs-scanner">
    <div className={`qrs-viewport is-${state.status}`}>
      <video ref={videoRef} className="qrs-video" playsInline muted aria-label={t('Camera view')} />
      <canvas ref={canvasRef} hidden />
      {state.status !== 'error' && <div className="qrs-frame" aria-hidden="true"><i /><i /><i /><i /><span className="qrs-line" /></div>}
      {state.status === 'starting' && <div className="qrs-overlay"><Camera size={26} /><span>{t('Starting the camera…')}</span></div>}
      {state.status === 'error' && <div className="qrs-overlay qrs-overlay-error" role="alert">
        <CameraOff size={28} />
        <span>{state.message}</span>
        <button type="button" className="button button-green button-small" onClick={() => setAttempt((n) => n + 1)}><RefreshCw size={14} /> {t('Try the camera again')}</button>
      </div>}
    </div>
    <p className="qrs-hint">{state.status === 'live' ? t('Point the camera at the QR code inside the bus. It scans by itself.') : t('Allow the camera when your browser asks.')}</p>
    <div className="qrs-photo">
      <input ref={fileRef} type="file" accept="image/*" capture="environment" hidden onChange={fromPhoto} />
      <button type="button" className="button button-outline button-small" onClick={() => fileRef.current?.click()}><ImageUp size={15} /> {t('Scan from a photo')}</button>
      {photoNote && <span className="qrs-photo-note" role="alert">{photoNote}</span>}
    </div>
  </div>
}

// "Already on a bus? Scan its QR code": the way into QR boarding from Find a bus and the home page.
export function ScanQrCard({ onOpen }) {
  return <section className="panel qrs-entry-card">
    <span aria-hidden="true"><QrCode size={22} /></span>
    <div><b>{t('On the bus already?')}</b><small>{t('Scan the QR code inside the bus to start your trip in seconds.')}</small></div>
    <button type="button" className="button button-green button-small qrs-entry" onClick={onOpen}><ScanLine size={15} /> {t('Scan QR code')}</button>
  </section>
}
