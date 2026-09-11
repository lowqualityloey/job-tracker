// Closes the last "not measured" note in spec §2.8 (gap 10b split left the *browser-render* half as an existence
// claim proved by `-042`, with no number attached). This produces that number.
//
// WHAT IS ACTUALLY BEING TIMED, since a latency figure without its span is a rumour:
//
//   t0 ─ fetch(POST) starts in tab A          ─┐
//   t1 ─ POST response received in tab A       │  "server + transport" leg
//   t2 ─ the marker is found in tab B's DOM    ─┘
//
//   userLatency    = t2 - t0   ← what a person at the second monitor experiences
//   notifyToRender = t2 - t1   ← the SSE→store→re-GET→render pipeline, i.e. the thing M3 added
//
// `notifyToRender` is the number DECISION-006/SSE is buying, so it is reported alongside the bigger one rather than
// instead of it. `-046` already measures commit→frame on the wire (p95 11 ms); this starts where that one ends.
//
// THREE KNOWN QUANTIZATION ERRORS, all of which make the reported latency look BETTER than it is:
//   1. tab B is polled from the CDP side, so t2 is the time of the first *check* that saw the row, not the instant the
//      row appeared: the error is up to one poll interval, and each check costs a round trip (~1–3 ms here).
//   2. t2 is DOM-visible, not painted — bounded by a frame (~16 ms at 60 Hz) in the same favourable direction.
//   3. both clocks are the *browser's* `Date.now()`, so no cross-machine skew; tabs in one Chromium share it exactly.
// Stated because a measurement that flatters the thing under test is still a measurement, but only if you say so.
//
// Run inside the container (see docs/STATE.md §3A for the topology recipe):
//   docker exec -e E2E_APP=http://127.0.0.1:4173 -e E2E_API=http://<host-ip>:5080 jt-bridge \
//     node --experimental-websocket /srv/renderLatency.mjs
const CDP_PORT = 9222
const APP = process.env.E2E_APP ?? 'http://127.0.0.1:4173'
const API = process.env.E2E_API ?? 'http://127.0.0.1:5080'
const LIST = `${APP}/applications`
const SAMPLES = Number(process.env.E2E_N ?? 10)
const POLL_MS = 4

const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

async function findDebuggerUrl() {
  for (const host of ['127.0.0.1', '[::1]']) {
    for (let i = 0; i < 60; i++) {
      try {
        const j = await (await fetch(`http://${host}:${CDP_PORT}/json/version`)).json()
        if (j.webSocketDebuggerUrl) return j.webSocketDebuggerUrl
      } catch { /* not up yet */ }
      await sleep(250)
    }
  }
  throw new Error('no CDP endpoint')
}

function makeClient(ws) {
  let id = 0
  const pending = new Map()
  ws.onmessage = (e) => {
    const m = JSON.parse(e.data)
    if (m.id && pending.has(m.id)) {
      const { resolve, reject } = pending.get(m.id)
      pending.delete(m.id)
      m.error ? reject(new Error(JSON.stringify(m.error))) : resolve(m.result)
    }
  }
  return (method, params = {}, sessionId) => new Promise((resolve, reject) => {
    const nid = ++id
    pending.set(nid, { resolve, reject })
    ws.send(JSON.stringify(sessionId ? { id: nid, method, params, sessionId } : { id: nid, method, params }))
  })
}

async function evalJs(call, sessionId, expression) {
  const r = await call('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }, sessionId)
  if (r.exceptionDetails) throw new Error(`page threw: ${JSON.stringify(r.exceptionDetails).slice(0, 180)}`)
  return r.result?.value
}

async function openTab(call, url) {
  const { targetId } = await call('Target.createTarget', { url })
  const { sessionId } = await call('Target.attachToTarget', { targetId, flatten: true })
  await call('Runtime.enable', {}, sessionId)
  await call('Page.enable', {}, sessionId)
  return { targetId, sessionId }
}

const rendered = `!!document.querySelector('.application-card, .empty-state')`

async function waitUntil(call, sessionId, expression, label, ms = 25000) {
  const deadline = Date.now() + ms
  for (;;) {
    if (await evalJs(call, sessionId, expression)) return
    if (Date.now() > deadline) throw new Error(`timeout waiting for ${label}`)
    await sleep(50)
  }
}

async function main() {
  const ws = new WebSocket(await findDebuggerUrl())
  await new Promise((r, j) => { ws.onopen = r; ws.onerror = j })
  const call = makeClient(ws)

  const a = await openTab(call, LIST)
  const b = await openTab(call, LIST)
  await waitUntil(call, a.sessionId, rendered, 'tab A to render')
  await waitUntil(call, b.sessionId, rendered, 'tab B to render')
  // Both tabs must be subscribed before timing anything, or t2 measures a stream that was never joined.
  const subs = await evalJs(call, b.sessionId,
    `({ es: typeof EventSource !== 'undefined', hasList: !!document.querySelector('.application-card, .empty-state') })`)
  if (!subs.es) throw new Error('tab B has no EventSource — this browser cannot run the claim under test')
  console.log(`warm: tab A + tab B rendered, EventSource available in tab B`)

  const made = []
  const rows = []
  try {
    for (let i = 0; i < SAMPLES; i++) {
      const cid = crypto.randomUUID()
      const marker = `LAT-${String(i).padStart(3, '0')}-${cid.slice(0, 8)}`
      made.push(cid)
      // The write happens *in* tab A's page context so t0/t1 are taken on the browser clock that also produces t2.
      const post = await evalJs(call, a.sessionId, `(async () => {
        const t0 = Date.now()
        const res = await fetch(${JSON.stringify(API)} + '/api/applications', {
          method: 'POST', headers: { 'content-type': 'application/json' },
          body: JSON.stringify({ id: ${JSON.stringify(cid)}, companyName: ${JSON.stringify(marker)},
                                 jobTitle: 'Latency probe', location: 'Dundee', status: 'Saved' })
        })
        const body = await res.json()
        return { t0, t1: Date.now(), status: res.status, revision: body.revision }
      })()`)
      if (post.status !== 201) throw new Error(`POST returned ${post.status}`)

      let t2 = null
      const deadline = Date.now() + 5000
      while (t2 === null && Date.now() < deadline) {
        const seen = await evalJs(call, b.sessionId,
          `({ t: Date.now(), seen: document.body.innerText.includes(${JSON.stringify(marker)}) })`)
        if (seen.seen) t2 = seen.t
        else await sleep(POLL_MS)
      }
      if (t2 === null) { console.log(`  sample ${i + 1}: NEVER RENDERED within 5s`); continue }
      rows.push({ user: t2 - post.t0, notify: t2 - post.t1 })
      console.log(`  sample ${i + 1}: user-visible ${String(t2 - post.t0).padStart(4)} ms   post-response→render ${String(t2 - post.t1).padStart(4)} ms`)
    }
  } finally {
    // `finally`, because gap 10c's whole lesson is that cleanup must run however the probe dies.
    let cleaned = 0
    for (const cid of made) {
      try {
        await evalJs(call, a.sessionId, `(async () => {
          const rows = await (await fetch(${JSON.stringify(API)} + '/api/applications')).json()
          const hit = rows.find(r => r.id === ${JSON.stringify(cid)})
          if (!hit) return 'gone'
          const res = await fetch(${JSON.stringify(API)} + '/api/applications/' + ${JSON.stringify(cid)}, {
            method: 'DELETE', headers: { 'If-Match': '"' + hit.revision + '"' } })
          return res.status
        })()`)
        cleaned++
      } catch { /* one failure must not abandon the rest */ }
    }
    console.log(`cleanup: ${cleaned}/${made.length} probe rows deleted`)
  }

  if (!rows.length) throw new Error('no samples observed — the measurement, not the product, is broken')
  const pct = (arr, q) => { const v = arr.slice().sort((x, y) => x - y); return v[Math.max(0, Math.min(v.length - 1, Math.floor(q * v.length) - 1))] }
  const u = rows.map((r) => r.user), n = rows.map((r) => r.notify)
  console.log(`\ncommit-request→second-tab-visible  n=${rows.length}/${SAMPLES}  p50=${pct(u, .5)}ms  p95=${pct(u, .95)}ms  max=${Math.max(...u)}ms`)
  console.log(`post-response→second-tab-visible   n=${rows.length}/${SAMPLES}  p50=${pct(n, .5)}ms  p95=${pct(n, .95)}ms  max=${Math.max(...n)}ms`)
  console.log(`(quantization: poll interval ≤ ${POLL_MS} ms + one CDP round trip per check; DOM-visible not painted — all favourable to the result)`)
  ws.close()
}

await main()
