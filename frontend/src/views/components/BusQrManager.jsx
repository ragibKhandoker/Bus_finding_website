import React, { useCallback, useEffect, useState } from 'react'
import { Download, Power, Printer, QrCode, RefreshCw } from 'lucide-react'
import QRCode from 'qrcode'
import { formatDate, formatNumber, t } from '../../models/i18n'
import { errorText } from '../../models/messages'
import { qrApi, qrLink } from '../../models/busQr'
import '../styles/qr-boarding.css'

// The QR codes of buses, for an admin (every bus on one route: pass routeId) or a driver (their assigned buses).
// Each bus shows its current code with Download and Print, and can get a new code (the old one stops working)
// or have its code turned off. The server decides who may do this; the list is empty for anyone else.
export function BusQrManager({ token, routeId, notify, title = 'Bus QR codes', description }) {
  const [state, setState] = useState({ status: 'loading', list: [], message: '' })
  const [busy, setBusy] = useState('')

  const load = useCallback(() => {
    setState((current) => ({ ...current, status: current.list.length ? 'ready' : 'loading' }))
    qrApi.manageable(token, routeId)
      .then((data) => setState({ status: 'ready', list: data.data || [], message: '' }))
      .catch((e) => setState({ status: 'error', list: [], message: errorText(e) }))
  }, [token, routeId])
  useEffect(() => { load() }, [load])

  const act = async (vehicle, action) => {
    if (action === 'generate' && vehicle.hasCode && !window.confirm(t('Make a new QR code for {bus}? The current code stops working, so replace the printed code in the bus.', { bus: vehicle.busCode }))) return
    if (action === 'off' && !window.confirm(t('Turn off the QR code of {bus}? Riders cannot board by scanning it until you make a new one.', { bus: vehicle.busCode }))) return
    setBusy(vehicle.vehicleId)
    try {
      const data = action === 'off' ? await qrApi.deactivate(token, vehicle.vehicleId) : await qrApi.generate(token, vehicle.vehicleId)
      notify?.(t(data.message))
      load()
    } catch (e) { notify?.(errorText(e)) } finally { setBusy('') }
  }

  if (state.status === 'ready' && !state.list.length && routeId) return null
  return <section className="panel bqr-panel">
    <div className="bqr-head">
      <span><QrCode size={20} /></span>
      <div><h2>{t(title)}</h2>
        <p>{description || t('Print each bus’s code and stick it inside the bus. Riders scan it to board; the fare and gems work as usual.')}</p></div>
    </div>
    {state.status === 'loading' && <div className="bqr-empty"><span className="spinner" /> {t('Loading QR codes…')}</div>}
    {state.status === 'error' && <div className="inline-error">{state.message} <button type="button" className="button button-outline button-small" onClick={load}>{t('Try again')}</button></div>}
    {state.status === 'ready' && (state.list.length
      ? <div className="bqr-list">{state.list.map((vehicle) => <BusQrRow key={vehicle.vehicleId} vehicle={vehicle} busy={busy === vehicle.vehicleId} onAct={act} />)}</div>
      : <p className="bqr-empty">{t('No buses are assigned to you yet.')}</p>)}
  </section>
}

function BusQrRow({ vehicle, busy, onAct }) {
  const [image, setImage] = useState('')
  const link = vehicle.token ? qrLink(vehicle.token) : ''
  useEffect(() => {
    let live = true
    if (!link) { setImage(''); return undefined }
    QRCode.toDataURL(link, { errorCorrectionLevel: 'M', margin: 2, width: 512 }).then((url) => { if (live) setImage(url) }).catch(() => setImage(''))
    return () => { live = false }
  }, [link])

  const name = [vehicle.busName, vehicle.routeNumber].filter(Boolean).map((part) => t(part)).join(' · ')
  const download = () => {
    const a = document.createElement('a')
    a.href = image
    a.download = `smartcommute-qr-${vehicle.busCode}.png`
    a.click()
  }
  const print = () => {
    const win = window.open('', '_blank', 'width=520,height=720')
    if (!win) return
    const esc = (s) => String(s).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]))
    win.document.write(`<!doctype html><html><head><meta charset="utf-8"><title>${esc(vehicle.busCode)} QR</title>
      <style>body{font-family:system-ui,sans-serif;text-align:center;margin:32px;color:#111}img{width:320px;height:320px}
      h1{margin:12px 0 4px;font-size:28px}p{margin:4px 0;font-size:16px}.code{font-size:22px;font-weight:700;letter-spacing:1px}</style></head>
      <body><h1>${esc(t('Scan to board'))}</h1><p>${esc(name)}</p><img src="${image}" alt=""><p class="code">${esc(vehicle.busCode)}</p>
      <p>${esc(t('Open SmartCommute → Scan QR, or use your phone camera.'))}</p><script>onload=()=>{print()}<\/script></body></html>`)
    win.document.close()
  }

  return <div className="bqr-row">
    <div className="bqr-code">{image ? <img src={image} alt={t('QR code of bus {bus}', { bus: vehicle.busCode })} /> : vehicle.hasCode ? <span className="spinner" /> : <span>{t('No code yet')}</span>}</div>
    <div className="bqr-info">
      <b>{vehicle.busCode}</b>
      {name && <small>{name}</small>}
      <span className={`bqr-state ${vehicle.hasCode ? 'is-on' : 'is-off'}`}>{vehicle.hasCode ? t('Active') : t('No QR code')}</span>
      {vehicle.hasCode && <small>{t('Made {date}', { date: formatDate(vehicle.createdAt) })} · {t('{count} scans', { count: formatNumber(vehicle.scanCount) })}</small>}
      <div className="bqr-actions">
        {vehicle.hasCode && image && <>
          <button type="button" className="button button-outline button-small" onClick={print}><Printer size={14} /> {t('Print')}</button>
          <button type="button" className="button button-outline button-small" onClick={download}><Download size={14} /> {t('Download')}</button>
        </>}
        <button type="button" className="button button-green button-small" disabled={busy} onClick={() => onAct(vehicle, 'generate')}>
          {busy ? <span className="button-spinner" aria-hidden="true" /> : vehicle.hasCode ? <RefreshCw size={14} /> : <QrCode size={14} />} {vehicle.hasCode ? t('New code') : t('Create QR code')}
        </button>
        {vehicle.hasCode && <button type="button" className="button button-outline button-small" disabled={busy} onClick={() => onAct(vehicle, 'off')}><Power size={14} /> {t('Turn off')}</button>}
      </div>
    </div>
  </div>
}
