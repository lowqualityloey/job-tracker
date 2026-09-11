// BEHAVIOR-m3-backend-api-042 — the real-browser half of AC-1 and AC-11, against HTTP, in Chromium.
//
// Promoted from the spike at docs/spikes/2026-09-11-real-browser-cross-tab-check/crosstab.mjs, whose CDP helpers
// `connect/openTab/evalJs/typeInto/clickEl/waitUntil` are reused nearly unchanged. Still dependency-free: raw CDP over
// Node 24's global WebSocket. **No new package** — AC-14's runtime count stays at 3, and a browser harness that needs
// an install step is a harness nobody runs twice.
//
// ## Why this file has to exist at all
//
// Slice 3 proved the HTTP adapter end-to-end in jsdom and the SSE stream end-to-end in xUnit, and **the two halves
// have never met**. `-040`'s client tests drive a *fake* `EventSource`, because jsdom implements no such API — so the
// browser's own parsing of `-046`'s framing is unverified by everything before this file. AC-11 is annotated in the
// task record as "verified in two halves" for exactly this reason, and AC-1's "zero changes to any frontend file" is
// only meaningful once a browser has loaded the build.
//
// ## The failure mode only a browser can produce
//
// jsdom's `fetch` is a stub: it cannot be blocked by a server that forgot to say who is allowed to call it. A real
// browser enforces cross-origin rules before the application sees anything — and the deployment plan in
// `docs/aws-deployment.md` puts the frontend and the API on **different origins**, which is precisely what
// `VITE_API_BASE_URL` exists to express. So this harness runs against the two-origin topology (page on :4173, API on
// :5080), which is the real shape, not the same-origin convenience of a dev-server proxy. If the API does not answer
// a preflight, or omits `Access-Control-Allow-Origin`, every check below fails **and every jsdom test stays green**.
// That asymmetry is the finding this file was written to expose.
//
// ## Provenance, asserted twice
//
// `Runtime.evaluate` after load can tell me a row is on screen; it cannot tell me where it came from. So
// `Page.addScriptToEvaluateOnNewDocument` installs a `fetch` recorder *before any page script runs*, and I assert on
// three things: the network saw `/api/applications`, `localStorage` holds **no** application key (the data went to
// the server, not to disk), and the other tab shows the new row **without a reload** — the absence of a reload is the
// test, because a `location.reload()` would also make it appear and would prove nothing about SSE.

import { spawn } from 'node:child_process'
import { openSync } from 'node:fs'

const CDP_PORT = 9222
// Env-driven because *where this runs* changes what the addresses mean. Inside the container (the only topology
// that works — my shell cannot route into the Docker network) :4173 is the co-located static server but the API is
// reachable only at the sandbox's routable IP. Run outside, the defaults are the plain local ports.
const APP = process.env.E2E_APP ?? 'http://127.0.0.1:4173'
// The list route, not the root. `/` is the dashboard, which renders neither an application card nor an empty state,
// so waiting for those anchors there can only ever time out — a harness bug that looked like an app bug.
const LIST = `${APP}/applications`
const API = process.env.E2E_API ?? 'http://127.0.0.1:5080'
const CHROME_IMAGE = 'mcr.microsoft.com/playwright:latest'
const CHROME_BIN = '/ms-playwright/chromium-1129/chrome-linux/chrome'
const CONTAINER = 'jt-browser'

// The host has no libnspr4, so Chromium runs in Docker — which is how the spike did it too. `--network=host` is the
// whole trick that lets the *host* keep running the page (:4173) and the API (:5080): from inside a host-networked
// container, 127.0.0.1 is the host's loopback, so no re-plumbing, no docker-compose file, and no URLs that only work
// in this one environment. Only the browser is containerised.

const results = []
const record = (name, pass, detail) => {
  results.push({ name, pass, detail })
  console.log(`${pass ? 'PASS' : 'FAIL'}  ${name}${detail ? `  — ${detail.slice(0, 180)}` : ''}`)
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

// SIGKILL to `docker run` can leave the container behind, and a stale container still holding :9222 makes the next
// run fail for a reason that has nothing to do with the code under test. Remove it by name; ignore "no such container".
function stopChrome(chrome) {
  if (!chrome) return
  // Both, in this order: SIGKILL to the `docker` CLI does not reliably reach the container, and `docker rm -f`
  // works without the handle. Doing only one leaves a browser holding :9222 for the next run.
  try {
    chrome.kill('SIGKILL')
  } catch {
    /* already exited */
  }
  spawn('docker', ['rm', '-f', CONTAINER], { stdio: 'ignore' })
}


// Chromium binds DevTools to *whichever* loopback the host resolves first, and this host has IPv6, so the endpoint came
// up on `ws://[::1]:9222` while a `127.0.0.1` probe was refused — a harness that reports "browser unreachable" while
// the browser is perfectly healthy. Trying both addresses per poll is the whole fix, and it is the kind of failure a
// mocked environment can never produce: this is why -042 exists.
const LOOPBACKS = ['127.0.0.1', '[::1]']

async function connect() {
  for (let i = 0; i < 90; i++) {
    for (const host of LOOPBACKS) {
      try {
      const res = await fetch(`http://${host}:${CDP_PORT}/json/version`)
      const { webSocketDebuggerUrl } = await res.json()
      const ws = new WebSocket(webSocketDebuggerUrl)
      await new Promise((r, j) => { ws.onopen = r; ws.onerror = j })
      return ws
      } catch {
        /* try the next loopback, then poll again */
      }
    }
    await sleep(200)
  }
  throw new Error(`no CDP endpoint on ${CDP_PORT} over ${LOOPBACKS.join(' or ')}`)
}

function makeClient(ws) {
  let nextId = 0
  const pending = new Map()
  ws.onmessage = (e) => {
    const msg = JSON.parse(e.data)
    if (msg.id !== undefined) {
      const { resolve, reject } = pending.get(msg.id) ?? {}
      pending.delete(msg.id)
      msg.error ? reject(new Error(JSON.stringify(msg.error))) : resolve(msg.result)
    }
  }
  return (method, params = {}, sessionId) =>
    new Promise((resolve, reject) => {
      const id = ++nextId
      pending.set(id, { resolve, reject })
      ws.send(JSON.stringify(sessionId ? { id, method, params, sessionId } : { id, method, params }))
    })
}

async function evalJs(call, sessionId, expression) {
  const r = await call('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }, sessionId)
  // `Runtime.evaluate` answers with {result:{value}} — reading `r.value` returns undefined for every assertion,
  // which is how the spike's own harness failed on its third run.
  return r.result ?? r
}

async function openTab(call, url, instrument) {
  const { targetId } = await call('Target.createTarget', { url: 'about:blank' })
  const { sessionId } = await call('Target.attachToTarget', { targetId, flatten: true })
  await call('Runtime.enable', {}, sessionId)
  await call('Page.enable', {}, sessionId)
  if (instrument) {
    // Installed before any page script runs, so the adapter's very first read is captured.
    await call('Page.addScriptToEvaluateOnNewDocument', {
      source: `(() => {
        window.__net = []
        const real = window.fetch
        window.fetch = (...args) => {
          const url = typeof args[0] === 'string' ? args[0] : String(args[0]?.url ?? args[0])
          const entry = { url, method: String(args[1]?.method ?? 'GET'), status: null, error: null }
          window.__net.push(entry)
          return real(...args).then(
            (res) => { entry.status = res.status; return res },
            (err) => { entry.error = String(err); throw err }
          )
        }
      })()`,
    }, sessionId)
  }
  await call('Page.navigate', { url }, sessionId)
  for (let i = 0; i < 120; i++) {
    const r = await evalJs(call, sessionId, `!!document.querySelector('.application-card, .empty-state, .app-error')`)
    if (r.value === true) return { targetId, sessionId }
    await sleep(250)
  }
  // Hoisted rather than inlined in the throw: a template literal with a nested `await` and a quoted string containing
  // quotes is how this file failed to parse the first time it ran.
  const diag = await evalJs(call, sessionId, 'document.body ? document.body.innerText.slice(0, 160) : "no body"')
  throw new Error(`app never rendered at ${url} — visible text: ${JSON.stringify(diag.value ?? '')}`)
}

async function typeInto(call, sessionId, selector, text) {
  await evalJs(call, sessionId, `document.querySelector(${JSON.stringify(selector)}).focus()`)
  await call('Input.insertText', { text }, sessionId)
}

// `selector` is a CSS selector STRING, not a JS expression: it is JSON.stringify'd into querySelector(...).
async function clickEl(call, sessionId, selector) {
  await evalJs(call, sessionId, `document.querySelector(${JSON.stringify(selector)}).click()`)
}

async function waitUntil(call, sessionId, expression, ms = 10000) {
  const deadline = Date.now() + ms
  for (;;) {
    const r = await evalJs(call, sessionId, `(function(){ return (${expression}) })()`)
    if (r.value === true) return true
    if (Date.now() > deadline) return false
    await sleep(150)
  }
}

const text = (call, sessionId) => evalJs(call, sessionId, 'document.body.innerText').then((r) => String(r.value ?? ''))

async function main() {
  // stdio captured, not ignored: the last run threw away the only output that could explain a failed CDP bring-up,
  // which is the same mistake AGENTS.md records about `grep`-filtering a gate's output.
  // A file descriptor, not a WriteStream: spawn reads stdio synchronously and an unopened stream has fd:null.
  const log = openSync('/tmp/chrome-cdp.log', 'w')
  // E2E_NO_SPAWN: Chromium is already running alongside us (in-container), so don't try to launch Docker from
  // inside a container — there is no Docker socket there, and the failure would look like a browser bug.
  const spawned = !process.env.E2E_NO_SPAWN
  const chrome = spawned && spawn('docker', ['run', '--rm', '--name', CONTAINER, '--network=host',
    '--shm-size=1g', '--entrypoint', CHROME_BIN, CHROME_IMAGE,
    '--headless=new', '--no-sandbox', '--disable-gpu', '--disable-dev-shm-usage',
    '--remote-debugging-address=0.0.0.0',
    `--remote-debugging-port=${CDP_PORT}`, 'about:blank'], { stdio: ['ignore', log, log] })

  let call
  try {
    call = makeClient(await connect())
  } catch (err) {
    record('Chromium is reachable over CDP', false, String(err))
    stopChrome(chrome)
    process.exit(1)
  }

  const marker = `Crosstab ${Date.now().toString(36)}`
  const tabs = []
  try {
    for (let i = 0; i < 2; i++) tabs.push(await openTab(call, LIST, true))
    record('both tabs rendered the HTTP-backed build', tabs.length === 2, `page ${APP}, api ${API}`)

    const a = tabs[0]
    const b = tabs[1]
    const netOf = (t) => evalJs(call, t.sessionId, 'JSON.stringify(window.__net)').then((r) => JSON.parse(String(r.value ?? '[]')))
    const bodyOf = (t) => text(call, t.sessionId)

    // 1. The list itself came over HTTP, and not from disk.
    const netA0 = await netOf(a)
    const reads = netA0.filter((e) => /\/api\/applications(\?|$)/.test(e.url) && e.method === 'GET')
    record('the first screen is an HTTP read, not a localStorage read',
      reads.length >= 1 && reads[0].status === 200,
      `GETs seen: ${JSON.stringify(reads.map((r) => r.status ?? r.error))}`)

    const storage = await evalJs(call, a.sessionId, `JSON.stringify(Object.keys(localStorage).filter(k => /jobtrackr/i.test(k)))`)
    record('no application data was written to localStorage',
      String(storage.value ?? '[]') === '[]',
      `keys: ${storage.value}`)

    // If CORS blocks the read, everything after this fails for the same reason; say so once, with the browser's own
    // words, rather than reporting four failures that look independent.
    const blocked = netA0.find((e) => e.error !== null)
    if (blocked) {
      record('…and the cause is cross-origin policy, not application code', false,
        `${blocked.method} ${blocked.url} → ${blocked.error}`)
    } else {
      // 2. Write through the real form in tab A. The form is a route (`/applications/new`), not a modal, so a
      // navigation is both deterministic and what a user typing the URL would get. `addScriptToEvaluateOnNewDocument`
      // re-installs on the new document, so `__net` restarts here and the POST below is read from that document.
      await call('Page.navigate', { url: `${APP}/applications/new` }, a.sessionId)
      const opened = await waitUntil(call, a.sessionId, `!!document.querySelector('#companyName')`, 8000)
      record('the create form opens in a real browser', opened)

      if (opened) {
        await typeInto(call, a.sessionId, '#companyName', marker)
        await typeInto(call, a.sessionId, '#jobTitle', 'Engineer')
        await typeInto(call, a.sessionId, '#location', 'Remote')
        await clickEl(call, a.sessionId, 'form button[type="submit"]')

        const posted = await waitUntil(call, a.sessionId,
          `document.body.innerText.includes(${JSON.stringify(marker)})`, 12000)
        const netA = await netOf(a)
        const post = netA.find((e) => e.method === 'POST' && /\/api\/applications$/.test(e.url))
        record('a write through the real form reaches the API over HTTP', posted && !!post,
          `POST status: ${post?.status ?? post?.error ?? 'no POST issued'}`)

        // 3. The headline: tab B shows it with no reload, no click, no interaction of any kind.
        const appeared = await waitUntil(call, b.sessionId,
          `document.body.innerText.includes(${JSON.stringify(marker)})`, 12000)
        const reloads = await evalJs(call, b.sessionId,
          'JSON.stringify({nav: performance.getEntriesByType("navigation").length, hidden: document.hidden})')
        record('the other tab learned without reloading — the SSE join is real', appeared,
          `tab B navigation entries: ${JSON.parse(String(reloads.value)).nav}`)

        if (!appeared) {
          const bNet = await netOf(b)
          const events = bNet.filter((e) => /\/events$/.test(e.url))
          const bText = (await bodyOf(b)).slice(0, 90).replace(/\s+/g, ' ')
          record('…diagnosis: did tab B even open a stream?', events.length > 0 && events[0].status === 200,
            `EventSource is not fetch-based, so __net shows ${events.length}; tab B visible text: "${bText}"`)
        }
      }
    }

    // 4. Cleanup, so the harness is re-runnable against a persistent database.
    const del = await evalJs(call, a.sessionId, `(async () => {
      const list = await (await fetch('${API}/api/applications')).json()
      const hit = list.find((r) => r.companyName === ${JSON.stringify(marker)})
      if (!hit) return 'nothing to clean'
      const res = await fetch('${API}/api/applications/' + hit.id, { method: 'DELETE', headers: { 'If-Match': '"' + hit.revision + '"' } })
      return 'deleted ' + res.status
    })()`)
    record('the marker record is removed again', String(del.value ?? '').startsWith('deleted'), String(del.value))
  } catch (err) {
    record('harness completed without throwing', false, String(err && err.message ? err.message : err))
  } finally {
    for (const t of tabs) {
      try { await call('Target.closeTarget', { targetId: t.targetId }) } catch { /* already gone */ }
    }
    stopChrome(chrome)
  }

  const failed = results.filter((r) => !r.pass)
  console.log(`\n${results.length - failed.length}/${results.length} browser checks passed`)
  process.exit(failed.length === 0 ? 0 : 1)
}

await main()
