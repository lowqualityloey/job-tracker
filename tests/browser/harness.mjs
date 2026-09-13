// Shared browser-harness recipe for the M4 real-Chromium runs.
//
// Extracted by TDD-EXEC-m4-authentication-075. `csrfGate.mjs` (BEHAVIOR-063 / AC-11) and
// `httpCrossTab.mjs` (BEHAVIOR-064 / AC-12) each carried their own copy of the CDP plumbing and the
// tab / boot / login helpers below. That is two `connect()`s and two duplicated boot scripts, and a fix
// to either one had to be made twice to stay honest. One definition, imported by both.
//
// The proof that the extraction is faithful is not a diff — it is `-063`'s 11/11 and `-064`'s 7/7
// re-run against this module (see the row in docs/tasks/TASK-m4-authentication.md). If a behaviour
// changes, those runs change first.
//
// No new dependency: raw CDP over Node's global WebSocket. A browser harness that needs an install
// step is a harness nobody runs twice, and AC-14's runtime count stays at 3.

export const CDP_PORT = 9222
// Both loopbacks: Chromium binds DevTools to whichever the host resolves first, and this host has IPv6,
// so the endpoint came up on `ws://[::1]:9222` while a `127.0.0.1` probe was refused — a harness that
// reports "browser unreachable" while the browser is perfectly healthy. Trying both per poll is the fix.
export const LOOPBACKS = ['127.0.0.1', '[::1]']
export const SESSION_COOKIE = '__Host-JTSession'
export const CSRF_COOKIE = '__Host-JTCsrf'
// The list route, not the root. `/` is the dashboard, which renders neither an application card nor an
// empty state, so waiting for those anchors there can only ever time out — a harness bug that looked like
// an app bug.
export const LIST_ANCHORS = '.application-card, .empty-state, .app-error'
export const LOGIN_ANCHOR = '#login-email'

export const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

export const results = []
export function record(name, pass, detail) {
  results.push({ name, pass, detail })
  console.log(`${pass ? 'PASS' : 'FAIL'}  ${name}${detail ? `  — ${String(detail).slice(0, 200)}` : ''}`)
}
export function exitWithSummary() {
  const failed = results.filter((r) => !r.pass)
  console.log(`\n${results.length - failed.length}/${results.length} browser checks passed`)
  process.exit(failed.length ? 1 : 0)
}

// Chromium binds DevTools to whichever loopback it resolves first; this host has IPv6, so try both.
export async function connect() {
  for (let i = 0; i < 90; i++) {
    for (const host of LOOPBACKS) {
      try {
        const res = await fetch(`http://${host}:${CDP_PORT}/json/version`)
        const { webSocketDebuggerUrl } = await res.json()
        const ws = new WebSocket(webSocketDebuggerUrl)
        await new Promise((r, j) => { ws.onopen = r; ws.onerror = j })
        return ws
      } catch {
        /* poll again */
      }
    }
    await sleep(200)
  }
  throw new Error(`no CDP endpoint on ${CDP_PORT}`)
}

// `onEvent`, when given, receives every CDP event (id-less message) — used by csrfGate.mjs to capture
// network telemetry for its cross-site case. httpCrossTab.mjs omits it and relies on page-side `__net`.
export function makeClient(ws, onEvent) {
  let nextId = 0
  const pending = new Map()
  ws.onmessage = (e) => {
    const msg = JSON.parse(e.data)
    if (msg.id !== undefined) {
      const { resolve, reject } = pending.get(msg.id) ?? {}
      pending.delete(msg.id)
      msg.error ? reject(new Error(JSON.stringify(msg.error))) : resolve(msg.result)
      return
    }
    if (typeof onEvent === 'function') onEvent(msg)
  }
  return (method, params = {}, sessionId) =>
    new Promise((resolve, reject) => {
      const id = ++nextId
      pending.set(id, { resolve, reject })
      ws.send(JSON.stringify(sessionId ? { id, method, params, sessionId } : { id, method, params }))
    })
}

export async function evalJs(call, sessionId, expression) {
  const r = await call('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }, sessionId)
  // `Runtime.evaluate` answers with {result:{value}} — reading `r.value` returns undefined for every
  // assertion, which is how the spike's own harness failed on its third run.
  return r.result ?? r
}

export async function waitUntil(call, sessionId, expression, ms = 10000) {
  const deadline = Date.now() + ms
  for (;;) {
    const r = await evalJs(call, sessionId, `(function(){ return (${expression}) })()`)
    if (r.value === true) return true
    if (Date.now() > deadline) return false
    await sleep(150)
  }
}

// Opens a tab, enables the domains, optionally clears the jar and/or installs a fetch recorder before
// navigation, waits for the page to render, and returns where it landed.
//
// `waitFor: 'app'` (default) polls for the application's own anchors — `.application-card` / `.empty-state` /
// `.app-error` (rendered as 'list') or `#login-email` (rendered as 'login'). `login` and `list` are both
// legitimate first renders for the application; conflating them is how a working guard looks like a broken app.
//
// `waitFor: 'body'` polls only for `document.body` to exist. That is the right wait for a foreign cross-site
// probe page (e.g. csrfGate.mjs's CASE 2 origin) whose body intentionally contains none of the app's anchors.
// The shared recipe must not assume every opened page is the application.
export async function openTab(call, url, { instrument = false, clearJar = false, waitFor = 'app' } = {}) {
  const { targetId } = await call('Target.createTarget', { url: 'about:blank' })
  const { sessionId } = await call('Target.attachToTarget', { targetId, flatten: true })
  await call('Runtime.enable', {}, sessionId)
  await call('Page.enable', {}, sessionId)
  await call('Network.enable', {}, sessionId)
  // The dev bundle is served over a self-signed certificate, which no browser will accept; Chromium's
  // net::ERR_CERT_AUTHORITY_INVALID arrives as a failed navigation with no DOM at all. Per-session bypass
  // — valid only against a throwaway dev server.
  await call('Security.setIgnoreCertificateErrors', { ignore: true }, sessionId)
  // `Network.*` is session-scoped: sent on the browser-level connection it answers `-32601 'Network... wasn't
  // found'`, which is exactly how this row's first run failed before any check had a chance to speak.
  if (clearJar) await call('Network.clearBrowserCookies', {}, sessionId)
  if (instrument) {
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
  const renderWait = waitFor === 'app'
    ? `(() => {
        if (document.querySelector(${JSON.stringify(LIST_ANCHORS)})) return 'list'
        if (document.querySelector(${JSON.stringify(LOGIN_ANCHOR)})) return 'login'
        return null
      })()`
    : `(() => document.body !== null)()`
  for (let i = 0; i < 120; i++) {
    const r = await evalJs(call, sessionId, renderWait)
    if (waitFor === 'app') {
      if (r.value === 'list' || r.value === 'login') return { targetId, sessionId, rendered: r.value }
    } else if (r.value === true) {
      return { targetId, sessionId, rendered: 'body' }
    }
    await sleep(250)
  }
  const diag = await evalJs(call, sessionId, 'document.body ? document.body.innerText.slice(0, 160) : "no body"')
  throw new Error(`page never rendered at ${url} — visible text: ${JSON.stringify(diag.value ?? '')}`)
}

// Reads a named cookie the way the shipped client does — out of the page's own jar.
export async function tokenInJar(call, sessionId, name = CSRF_COOKIE) {
  const r = await evalJs(call, sessionId, `(() => {
    const hit = (document.cookie.match(/(?:^|; )${name}=([^;]*)/) ?? [])[1]
    return hit ?? ''
  })()`)
  return String(r.value ?? '')
}

// Logs in through the real form (never by posting to /api/auth/login from the harness — the page, the
// redirect, and the wire are all part of what AC-11/AC-12 depend on). Returns
// `{ detail, session }`; `session` is null when the tab was already authenticated. Throws if login does
// not reach the list, so everything downstream measures an authenticated app instead of a silent no-op.
export async function authenticate(call, tab) {
  if (tab.rendered !== 'login') {
    return { detail: 'no login page appeared; app was already authenticated', session: null }
  }
  const EMAIL = process.env.E2E_LOGIN_EMAIL ?? ''
  const PASSWORD = process.env.E2E_LOGIN_PASSWORD ?? ''
  await evalJs(call, tab.sessionId, `document.querySelector(${JSON.stringify(LOGIN_ANCHOR)}).focus()`)
  await call('Input.insertText', { text: EMAIL }, tab.sessionId)
  await evalJs(call, tab.sessionId, `document.querySelector('#login-password').focus()`)
  await call('Input.insertText', { text: PASSWORD }, tab.sessionId)
  await evalJs(call, tab.sessionId, `document.querySelector('form button[type="submit"]').click()`)
  const landed = await waitUntil(call, tab.sessionId, `!!document.querySelector(${JSON.stringify(LIST_ANCHORS)})`, 15000)
  if (!landed) {
    // Throwing rather than recording a failure: everything downstream measures an unauthenticated app, so
    // one loud abort is honest and seven per-line failures would look like seven independent defects.
    const body = await evalJs(call, tab.sessionId, 'document.body ? document.body.innerText.slice(0,120).replace(/\\s+/g," ") : "no body"')
    throw new Error(`login did not reach the list — visible text: "${body.value ?? ''}"`)
  }
  // The assertion that makes the run's green mean something: the jar persists across CDP targets in this
  // image, so "the list rendered" can be a stale session from an earlier run rather than the login that
  // just happened.
  const { cookies } = await call('Storage.getCookies', {}, tab.sessionId)
  const session = (cookies ?? []).find((c) => /JTSession/i.test(c.name))
  if (!session) {
    throw new Error(`the list rendered but the jar holds no ${SESSION_COOKIE} — a stale render, not a session`)
  }
  return {
    detail: `${SESSION_COOKIE} present (secure=${session.secure} httpOnly=${session.httpOnly} sameSite=${session.sameSite}) after a form login`,
    session,
  }
}
