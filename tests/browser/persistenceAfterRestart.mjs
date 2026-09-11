// BEHAVIOR-m3-backend-api-043 · `type:test` `area:data` — durability *outside the client process*.
//
// AC-3 says: given a record created **in the browser**, when the browser is closed and reopened, then the record is
// present **and** `psql` shows the row. Neither half is reachable from a test-runner process, so this is a harness plus
// quoted command output, exactly as the ladder's Verification column specifies. The record is created by *filling the
// real form in Chromium*, not by a raw POST, because the Given clause is about a browser-created record; a POST would
// quietly change what is being proved.
//
// WHY THIS IS SPLIT ACROSS TWO PROCESSES
// The phase that "closes the browser" is `docker restart` of the container, because the browser *is* the container's
// entrypoint process — restarting kills Chromium and everything co-located, which is a stronger claim than closing a
// tab. Nothing inside a container can restart its own container, so the sequence is driven from the shell
// (`tests/browser/run-043.sh`) and this file implements one phase per invocation, selected by E2E_PHASE.
//
// THE PART THAT MAKES THE POSITIVE ASSERTION MEAN ANYTHING
// "The row was still there after the restart" is also what a stale cache, a service worker, or a localStorage fallback
// would produce. `verify-absent` is therefore part of the behaviour, not a bonus: the record is deleted over HTTP, a
// *fresh* tab is opened, and the marker must be **gone**. Without that control the visible-check cannot distinguish
// "durable in PostgreSQL" from "remembered by the browser", which is precisely the distinction AC-3 is about.
import { setTimeout as sleep } from 'node:timers/promises'

const CDP_PORT = 9222
const APP = process.env.E2E_APP ?? 'http://127.0.0.1:4173'
const LIST = `${APP}/applications`
const MARKER = process.env.E2E_MARKER ?? ''
const PHASE = process.env.E2E_PHASE ?? ''

// A marker that survives the whole round trip is itself part of the claim: it is built from the same classes of
// characters `-041` had to fix a bug in, so a persistence failure and an encoding failure are not conflated later.
const COMPANY = `${MARKER} ✅ café`
const TITLE = `Role ${MARKER}`

const results = []
const record = (name, pass, detail) => {
  results.push({ name, pass })
  console.log(`${pass ? 'PASS' : 'FAIL'}  ${name}${detail ? `  — ${detail}` : ''}`)
}

async function findDebuggerUrl() {
  // Both loopbacks, because Chromium binds whichever it resolves first and this host has IPv6 on lo.
  for (const host of ['127.0.0.1', '[::1]']) {
    for (let i = 0; i < 60; i++) {
      try {
        const r = await fetch(`http://${host}:${CDP_PORT}/json/version`)
        const j = await r.json()
        if (j.webSocketDebuggerUrl) return j.webSocketDebuggerUrl
      } catch { /* not up yet */ }
      await sleep(250)
    }
  }
  throw new Error(`no CDP endpoint on ${CDP_PORT}`)
}

function makeClient(ws) {
  let id = 0
  const pending = new Map()
  ws.onmessage = (e) => {
    const msg = JSON.parse(e.data)
    if (msg.id && pending.has(msg.id)) {
      const { resolve, reject } = pending.get(msg.id)
      pending.delete(msg.id)
      msg.error ? reject(new Error(JSON.stringify(msg.error))) : resolve(msg.result)
    }
  }
  return (method, params = {}, sessionId) => new Promise((resolve, reject) => {
    const nid = ++id
    pending.set(nid, { resolve, reject })
    ws.send(JSON.stringify(sessionId ? { id: nid, method, params, sessionId } : { id: nid, method, params }))
  })
}

const evalJs = async (call, sessionId, expression) => {
  const r = await call('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }, sessionId)
  if (r.exceptionDetails) throw new Error(`page threw: ${JSON.stringify(r.exceptionDetails).slice(0, 200)}`)
  return r.result?.value
}

// Open a page the way a user does: a brand-new target, no reuse of anything from a previous phase.
async function openTab(call, url) {
  const { targetId } = await call('Target.createTarget', { url })
  const { sessionId } = await call('Target.attachToTarget', { targetId, flatten: true })
  await call('Runtime.enable', {}, sessionId)
  await call('Page.enable', {}, sessionId)
  // Record what the page itself sends, so a write can be confirmed over the wire rather than inferred from the DOM.
  await call('Page.addScriptToEvaluateOnNewDocument', {
    source: `window.__net = []
      const of = window.fetch
      window.fetch = async (u, o) => { const res = await of(u, o); try { window.__net.push({ u: String(u), m: (o && o.method) || 'GET', s: res.status }) } catch {} ; return res }`,
  }, sessionId)
  return { targetId, sessionId }
}

async function waitFor(call, sessionId, expression, label, ms = 25000) {
  const deadline = Date.now() + ms
  let last = null
  while (Date.now() < deadline) {
    try {
      last = await evalJs(call, sessionId, expression)
      if (last) return last
    } catch (e) { last = `err:${e.message}` }
    await sleep(300)
  }
  throw new Error(`timeout waiting for ${label} (last: ${JSON.stringify(last)?.slice(0, 160)})`)
}

const bodyHas = (text) => `document.body.innerText.includes(${JSON.stringify(text)})`
const listRendered = `!!document.querySelector('.application-card, .empty-state')`

// The create path is the real form: navigate, fill the actual inputs, submit. Every selector here is one a user's
// pointer would touch, so the harness breaks when the UI breaks rather than when an implementation detail moves.
async function createThroughForm(call, sessionId) {
  await call('Page.navigate', { url: `${APP}/applications/new` }, sessionId)
  await waitFor(call, sessionId, `!!document.querySelector('#companyName')`, 'the create form')
  const filled = await evalJs(call, sessionId, `
    const set = (sel, val) => {
      const el = document.querySelector(sel)
      const proto = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype
      Object.getOwnPropertyDescriptor(proto, 'value').set.call(el, val)
      el.dispatchEvent(new Event('input', { bubbles: true }))
    }
    set('#companyName', ${JSON.stringify(COMPANY)})
    set('#jobTitle', ${JSON.stringify(TITLE)})
    set('#location', 'Dundee')
    document.querySelector('.application-form button[type="submit"]').click()
    'filled'`, sessionId)
  // The write must leave the browser as an HTTP POST and come back 201: the DOM alone cannot tell a real create from
  // an optimistic local insert, which is the difference between "persisted" and "displayed".
  const post = await waitFor(call, sessionId,
    `(window.__net || []).find(r => r.m === 'POST' && r.u.includes('/api/applications'))`, 'the POST to reach the API')
  await waitFor(call, sessionId, listRendered, 'the list after create')
  return { filled, post }
}

async function main() {
  if (!MARKER) throw new Error('E2E_MARKER is required — the harness asserts on a unique marker, not on "some row"')
  const ws = new WebSocket(await findDebuggerUrl())
  await new Promise((r, j) => { ws.onopen = r; ws.onerror = j })
  const call = makeClient(ws)

  if (PHASE === 'create-then-verify') {
    const { targetId, sessionId } = await openTab(call, LIST)
    const { post } = await createThroughForm(call, sessionId)
    record('the browser created the record over HTTP, not locally', post?.s === 201, `POST status: ${post?.s}`)
    const seen = await waitFor(call, sessionId, bodyHas(COMPANY), 'the marker in the list')
    record('the marker appears in the creating tab', !!seen)
    // localStorage must be empty: if it held the record, every later "it survived" assertion would be worthless.
    const keys = await evalJs(call, sessionId, `Object.keys(localStorage).join(',')`, sessionId)
    record('nothing was cached in localStorage at create time', !/applic/i.test(keys ?? ''), `keys: ${JSON.stringify(keys)}`)
    await call('Target.closeTarget', { targetId })
  } else if (PHASE === 'verify-visible') {
    const { targetId, sessionId } = await openTab(call, LIST)
    await waitFor(call, sessionId, listRendered, 'a rendered list after the restart')
    const seen = await waitFor(call, sessionId, bodyHas(COMPANY), 'the marker after the restart', 15000).catch(() => null)
    record('the record survived a full browser restart', !!seen, seen ? 'marker present in a fresh process' : 'MISSING')
    const navs = await evalJs(call, sessionId, `performance.getEntriesByType('navigation').length`)
    record('this tab loaded the page exactly once', navs === 1, `navigation entries: ${navs}`)
    await call('Target.closeTarget', { targetId })
  } else if (PHASE === 'verify-absent') {
    // The negative control. Same code path, same fresh target, opposite expectation.
    const { targetId, sessionId } = await openTab(call, LIST)
    await waitFor(call, sessionId, listRendered, 'a rendered list')
    await sleep(1200) // give any stale-render path every chance to show the row
    const stillThere = await evalJs(call, sessionId, bodyHas(COMPANY))
    record('NEGATIVE CONTROL: after deletion the marker is gone (so the visible-check has teeth)', !stillThere,
      stillThere ? 'the browser still showed a deleted record — that is a real defect' : 'absent as expected')
    await call('Target.closeTarget', { targetId })
  } else {
    throw new Error(`unknown E2E_PHASE ${JSON.stringify(PHASE)} (want create-then-verify | verify-visible | verify-absent)`)
  }
  ws.close()
}

await main()

const failed = results.filter((r) => !r.pass)
console.log(`${results.length - failed.length}/${results.length} persistence checks passed`)
process.exit(failed.length ? 1 : 0)
