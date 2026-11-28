// Connects every Android device plugged in over USB (and any running emulator) to this PC's dev servers, so the
// device's Chrome can open http://localhost:5173 and see each saved change at once, with the local backend at
// http://localhost:3000. Run it after plugging a phone in (and again after re-plugging it):
//
//   npm run mobile
//
// Start the site with `npm run dev:mobile` (Vite on 127.0.0.1, which the devices reach through adb). Needs adb (Android
// platform-tools) on PATH and USB debugging allowed on the phone.
import { execFileSync } from 'node:child_process'

const PORTS = [5173, 3000]

const adb = (args) => execFileSync('adb', args, { encoding: 'utf8' }).trim()

let listing
try {
  listing = adb(['devices'])
} catch {
  console.error('adb was not found. Install Android platform-tools and add it to PATH.')
  process.exit(1)
}

const devices = listing.split('\n').slice(1).map((line) => line.trim().split(/\s+/)).filter((parts) => parts.length >= 2)
if (!devices.length) {
  console.error('No Android device found. Plug the phone in with USB debugging on, or start the emulator.')
  process.exit(1)
}

for (const [serial, state] of devices) {
  if (state !== 'device') {
    console.log(`${serial}: ${state === 'unauthorized' ? 'allow USB debugging on the phone, then run this again' : state}`)
    continue
  }
  for (const port of PORTS) adb(['-s', serial, 'reverse', `tcp:${port}`, `tcp:${port}`])
  const kind = serial.startsWith('emulator-') ? 'emulator' : 'phone'
  console.log(`${serial} (${kind}): connected. Open http://localhost:5173 in its Chrome.`)
}
