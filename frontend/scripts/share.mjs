// Shares the local dev site with any phone, over the internet, with live updates:
//
//   npm run share
//
// Starts Vite on port 5180 (the site calls the local API through /__api, so the phone needs only one address) and a
// temporary Cloudflare tunnel to it, then prints an https://….trycloudflare.com link. Open it on the phone: every saved
// change appears at once. Keep the backend running (`dotnet run`). Stop with Ctrl+C; the link stops working then.
// Anyone with the link can use the dev site while it runs, so share it only with yourself. Needs `cloudflared`.
import { spawn } from 'node:child_process'

const PORT = 5180
const children = []
const stop = () => { children.forEach((child) => child.kill()); process.exit(0) }
process.on('SIGINT', stop)
process.on('SIGTERM', stop)

const vite = spawn('npx', ['vite', '--host', '127.0.0.1', '--port', String(PORT), '--strictPort'], {
  shell: true, stdio: ['ignore', 'inherit', 'inherit'],
  env: { ...process.env, SMARTCOMMUTE_SHARE: '1', VITE_API_BASE_URL: '/__api' },
})
children.push(vite)

const tunnel = spawn('cloudflared', ['tunnel', '--url', `http://127.0.0.1:${PORT}`, '--no-autoupdate'], { shell: true })
children.push(tunnel)
let shown = false
const watch = (chunk) => {
  const link = String(chunk).match(/https:\/\/[a-z0-9-]+\.trycloudflare\.com/)
  if (link && !shown) {
    shown = true
    console.log(`\n  📱  Open this on your phone:  ${link[0]}\n      Saved changes show up there at once. Ctrl+C to stop.\n`)
  }
}
tunnel.stdout.on('data', watch)
tunnel.stderr.on('data', watch)
tunnel.on('exit', (code) => { if (!shown) console.error(`cloudflared stopped (${code}). Is it installed and on PATH?`) })
vite.on('exit', stop)
