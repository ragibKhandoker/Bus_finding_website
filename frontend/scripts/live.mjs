// Keeps the live site in step with your work:
//
//   npm run live
//
// Watches frontend/ and backend/ while it runs. About 15 seconds after you stop saving, it builds the website and, if
// the build succeeds, commits everything git is not told to ignore and pushes to main. Vercel then deploys the website
// (about 1–2 minutes) and Render the API; pages already open on a phone show "A new version is ready" and refresh.
// A failed build is reported and nothing is pushed, so a broken website never goes live. Stop with Ctrl+C.
import { execFileSync, spawnSync } from 'node:child_process'
import { watch } from 'node:fs'
import { dirname, join, relative, sep } from 'node:path'
import { fileURLToPath } from 'node:url'

const FRONTEND = join(dirname(fileURLToPath(import.meta.url)), '..')
const REPO = join(FRONTEND, '..')
const QUIET_MS = 15_000
const SITE = 'https://smartcommute-dhaka.vercel.app'
// Build output, dependencies and runtime data change on their own; they never trigger a push.
const IGNORED = [`${sep}node_modules${sep}`, `${sep}dist${sep}`, `${sep}bin${sep}`, `${sep}obj${sep}`, `${sep}App_Data${sep}`, `${sep}.git${sep}`, `${sep}.vercel${sep}`, `${sep}TestResults${sep}`]

const git = (...args) => execFileSync('git', args, { cwd: REPO, encoding: 'utf8' }).trim()
const time = () => new Date().toLocaleTimeString()

let timer = 0
let busy = false
let again = false

async function publish() {
  if (busy) { again = true; return }
  busy = true
  try {
    const changed = git('status', '--porcelain')
    if (!changed) return
    console.log(`\n[${time()}] Changes saved. Building the website…`)
    const build = spawnSync('npx', ['vite', 'build'], { cwd: FRONTEND, shell: true, encoding: 'utf8' })
    if (build.status !== 0) {
      console.log(`[${time()}] ✖ Build failed, nothing was pushed. Fix the error and save again:\n${(build.stdout + build.stderr).split('\n').slice(-15).join('\n')}`)
      return
    }
    git('add', '-A')
    const files = git('diff', '--cached', '--name-only').split('\n').filter(Boolean)
    if (!files.length) return
    git('commit', '-m', `Live update: ${files.slice(0, 3).map((f) => f.split('/').pop()).join(', ')}${files.length > 3 ? ` and ${files.length - 3} more` : ''}`)
    git('push', 'origin', 'main')
    console.log(`[${time()}] ✔ Pushed ${files.length} file(s): ${files.join(', ')}`)
    console.log(`           Live at ${SITE} in about 1–2 minutes; open pages will offer to refresh.`)
  } catch (error) {
    console.log(`[${time()}] ✖ Could not publish: ${String(error.message || error).split('\n')[0]}`)
  } finally {
    busy = false
    if (again) { again = false; schedule() }
  }
}

function schedule() {
  clearTimer()
  timer = setTimeout(publish, QUIET_MS)
}
function clearTimer() { if (timer) clearTimeout(timer) }

for (const dir of [FRONTEND, join(REPO, 'backend')]) {
  watch(dir, { recursive: true }, (_event, file) => {
    if (!file) return
    const path = `${sep}${relative(REPO, join(dir, String(file)))}`
    if (IGNORED.some((part) => path.includes(part))) return
    schedule()
  })
}
console.log(`Watching frontend/ and backend/. Each saved change goes live at ${SITE} (Ctrl+C to stop).`)
