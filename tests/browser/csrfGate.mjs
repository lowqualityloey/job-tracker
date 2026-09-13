// BEHAVIOR-m4-auth-063 / AC-11 — the antiforgery gate, proven in a browser, as the owner-approved two cases.
//
// ## The row, and what "two cases" buys
//
// AC-11 as ratified says a cross-site write without `X-CSRF-Token` is rejected **and the same call with the token
// succeeds**. The restatement approved at ~18:10 UTC splits it, because one browser case cannot attribute its own
// result: a cross-site request to this API is refused for a reason that has *nothing to do with the header* (the
// session cookie never arrives), so a single "cross-site write is refused → PASS" run would certify a gate that does
// not exist. Split:
//
//   CASE 1  same-site, authenticated, **state-changing**, no header  → `403 antiforgery`. The header check bites.
//   CASE 2  cross-site, cookie withheld by the platform              → `401`, and *not* `403`: the gate was never
//           reached, which is exactly the ordering the two status codes let us observe.
//
// Each carries its own control, and the controls are not decorative. Without them a dead endpoint answers "refused"
// and every check below could pass on a server that accepts nothing:
//
//   · C1c  the identical POST *with* the real token → `201`, and the record then appears in a read. AC-11's mandatory
//          positive control, in both senses: refused, and served when it should be.
//   · C1b  a header present with a **wrong value** → `403`, so the gate compares a value rather than testing for
//          existence. This is the browser-level twin of `A_header_present_but_wrong_is_refused`.
//   · C1d  the refused write left **no row behind** — a status code says the response; only a read says the effect.
//   · C2b  the same POST from the site origin with `credentials: 'omit'` → `401`. This is what makes case 2's `401`
//          mean "no cookie arrived" rather than "that URL is broken", reproduced on the good origin where nothing else
//          differs.
//   · C2c  a foreign page cannot read the token out of its own jar — the premise the whole scheme rests on.
//
// ## Why case 1 is not login
//
// `POST /api/auth/login` carries no token and answers `204` (measured in the spike, and pinned by
// `Login_needs_no_token_and_still_answers_204`). A browser case aimed at login would therefore pass whether the gate
// worked or not. Every request in case 1 is a `POST /api/applications`.
//
// ## The honest limits of this run
//
//   · Certificate validation is bypassed per-session over CDP. A human still meets the interstitial; recorded in
//     `-042`'s spike and unchanged here.
//   · Case 2's status is read from **CDP network events**, not from the page: a cross-origin response with no CORS
//     headers is unreadable to the requesting script, and `fetch` rejects with a `TypeError` that says nothing about
//     what the server answered. Reading it from the browser's own telemetry is the only way to see the `401`, and it is
//     also why the request is a *simple* one (`text/plain`, no custom headers): a preflighted request would be stopped
//     before it reached the server, and the run would then be measuring CORS rather than the cookie.
//   · `SameSite=Lax` is doing the work in case 2, on a two-origin topology. Under `-071`'s same-origin serving there is
//     no "cross-site" request to make from the app at all, which is the trade recorded alongside that row.

import {
  CSRF_COOKIE,
  connect,
  makeClient,
  evalJs,
  waitUntil,
  openTab,
  authenticate,
  record,
  results,
  sleep,
} from './harness.mjs'

const APP = process.env.E2E_APP ?? 'https://127.0.0.1:5443'
const CROSS = process.env.E2E_CROSS ?? 'http://127.0.0.1:4173'

// Network telemetry for case 2: the cross-site response is unreadable to the page (CORS), so its status can only be
// observed from the browser's own CDP events. Without capturing these, an empty result list is indistinguishable from
// "the server answered something we chose to read wrong".
const events = []
const onEvent = (msg) => {
  if (msg.method === 'Network.responseReceived') {
    events.push({
      sessionId: msg.sessionId,
      url: msg.params.response.url,
      status: msg.params.response.status,
      type: msg.params.response.type,
    })
  }
  if (msg.method === 'Network.loadingFailed') {
    events.push({
      sessionId: msg.sessionId,
      url: msg.params.request ? msg.params.request.url : undefined,
      failed: `${msg.params.errorText}${msg.params.blockedReason ? ` blocked=${msg.params.blockedReason}` : ''}`,
    })
  }
}

async function postFromPage(call, sessionId, { withHeader, headerValue, credentials, marker }) {
  const script = `(async () => {
    const token = ${JSON.stringify(withHeader ? (headerValue ?? '') : '')}
    const headers = { 'content-type': 'application/json' }
    ${withHeader ? `if (token) headers['x-csrf-token'] = token` : ''}
    const res = await fetch(${JSON.stringify(`${APP}/api/applications`)}, {
      method: 'POST',
      credentials: ${JSON.stringify(credentials)},
      headers,
      body: JSON.stringify({
        id: crypto.randomUUID(),
        companyName: ${JSON.stringify(marker)},
        jobTitle: 'Csrf probe',
        location: 'Test',
        status: 'Saved',
      }),
    })
    let code = null
    try { code = (await res.json()).code ?? null } catch { code = 'unreadable-body' }
    return JSON.stringify({ status: res.status, code })
  })()`

  const r = await evalJs(call, sessionId, script)
  if (r.exceptionDetails) {
    return { status: 'threw', code: String(r.exceptionDetails.exception?.description ?? 'unknown').slice(0, 90) }
  }
  return JSON.parse(String(r.value))
}

/** Reads the token the way the shipped client does: out of the page's own cookie jar. */
async function tokenInJarLocal(call, sessionId) {
  const r = await evalJs(call, sessionId, `(() => {
    const hit = (document.cookie.match(/(?:^|; )${CSRF_COOKIE}=([^;]*)/) ?? [])[1]
    return hit ?? ''
  })()`)
  return String(r.value ?? '')
}

async function listContains(call, sessionId, marker) {
  const r = await evalJs(call, sessionId, `(async () => {
    const list = await (await fetch(${JSON.stringify(`${APP}/api/applications`)})).json()
    return Array.isArray(list) && list.some((row) => row.companyName === ${JSON.stringify(marker)})
  })()`)
  return r.value === true
}

async function cleanup(call, sessionId, marker) {
  const r = await evalJs(call, sessionId, `(async () => {
    const list = await (await fetch(${JSON.stringify(`${APP}/api/applications`)})).json()
    const hit = (Array.isArray(list) ? list : []).find((row) => row.companyName === ${JSON.stringify(marker)})
    if (!hit) return 'nothing to clean'
    const token = (document.cookie.match(/(?:^|; )${CSRF_COOKIE}=([^;]*)/) ?? [])[1]
    if (!token) return 'no token in the jar; a delete would be refused'
    const res = await fetch(${JSON.stringify(`${APP}/api/applications/`)} + hit.id, {
      method: 'DELETE', headers: { 'x-csrf-token': token, 'if-match': '"' + hit.revision + '"' },
    })
    return res.ok ? 'cleaned' : 'refused ' + res.status
  })()`)
  // The first run said only "cleanup returned nothing", which is what an *exception* looks like when `result` is
  // absent — and a cleanup that fails silently is how the next run inherits rows and starts failing somewhere else.
  if (r.value === undefined && r.exceptionDetails) {
    return `threw ${String(r.exceptionDetails.exception?.description ?? r.exceptionDetails.text).slice(0, 120)}`
  }
  return String(r.value ?? `nothing: ${JSON.stringify(r).slice(0, 140)}`)
}

async function main() {
  const ws = await connect()
  const call = makeClient(ws, onEvent)

  // --- the authenticated app tab -------------------------------------------------------------
  const app = await openTab(call, `${APP}/applications`, { clearJar: true })
  const authResult = await authenticate(call, app)
  console.log(`AUTHENTICATED  ${authResult.detail}`)

  const token = await tokenInJarLocal(call, app.sessionId)
  if (!token) throw new Error(`logged in, but ${CSRF_COOKIE} is not readable from the page`)
  record('the client can read a token out of its own jar', true, `${CSRF_COOKIE} length ${token.length}`)
  // Not a tidiness check. A token the client cannot read cannot be echoed, and every write would then 403 — the
  // failure would surface as "the app is broken", never as the attribute mistake it is. This was originally part of
  // `authenticate`; it lives here now that the login helper is shared with httpCrossTab.mjs.
  const { cookies: csrfCookies } = await call('Storage.getCookies', {}, app.sessionId)
  const csrfCookie = (csrfCookies ?? []).find((c) => c.name === CSRF_COOKIE)
  if (!csrfCookie) throw new Error(`logged in but the jar holds no ${CSRF_COOKIE}; the app has no token to send`)
  if (csrfCookie.httpOnly) {
    throw new Error(`${CSRF_COOKIE} is HttpOnly: the client cannot read it, so the gate is unreachable by design`)
  }

  // --- CASE 1: same-site, authenticated, headerless -------------------------------------------
  const refusedMarker = 'CSRF-063-refused'
  const refused = await postFromPage(call, app.sessionId, {
    withHeader: false, credentials: 'same-origin', marker: refusedMarker,
  })
  record(
    'CASE 1  a same-site write with no X-CSRF-Token is refused with 403 antiforgery',
    refused.status === 403 && refused.code === 'antiforgery',
    `status=${refused.status} code=${refused.code}`,
  )

  // C1d — the effect, not the envelope. A gate that refuses *after* the handler ran would satisfy the check above
  // and leave the row in the table, which is the failure mode that actually matters.
  const leaked = await listContains(call, app.sessionId, refusedMarker)
  record('CASE 1  the refused write left no row behind', !leaked, leaked ? 'the record exists — refused too late' : 'absent from a fresh read')

  // C1b — presence vs value.
  const wrong = await postFromPage(call, app.sessionId, {
    withHeader: true, headerValue: 'not-a-token', credentials: 'same-origin', marker: refusedMarker,
  })
  record(
    'CASE 1b  a header present with the wrong value is refused too',
    wrong.status === 403 && wrong.code === 'antiforgery',
    `status=${wrong.status} code=${wrong.code}`,
  )

  // C1c — AC-11's mandatory positive control.
  const okMarker = 'CSRF-063-served'
  const served = await postFromPage(call, app.sessionId, {
    withHeader: true, headerValue: token, credentials: 'same-origin', marker: okMarker,
  })
  const appears = await listContains(call, app.sessionId, okMarker)
  record(
    'CASE 1c  POSITIVE CONTROL: the same write with the issued token succeeds',
    served.status === 201 && appears,
    `status=${served.status} code=${served.code} row visible=${appears}`,
  )

  // C2b — control for case 2, run while the app tab is still warm: same origin, cookie withheld.
  const omitted = await postFromPage(call, app.sessionId, {
    withHeader: true, headerValue: token, credentials: 'omit', marker: refusedMarker,
  })
  record(
    'CASE 2b  POSITIVE CONTROL for the cross-site case: no cookie, same origin → 401, not 403',
    omitted.status === 401,
    `status=${omitted.status} code=${omitted.code} — proves 401 means "no cookie arrived", and that the URL is live`,
  )

  // No cleanup here: the `app` tab's session is revoked below by case 1e's logout, and a delete issued after that
  // would answer 401 — a cleanup failure indistinguishable from a gate failure. Both markers are removed at the end, in
  // in the tab that still holds a live session. First run cleaned `okMarker` here *and* there, and the final check then
  // reported "nothing to clean" as a defect.


  // --- CASE 2: a foreign origin ---------------------------------------------------------------
  // `CROSS` is a plain static server on another port and scheme. The page there has no relationship to the app's jar,
  // and that *is* the attack: an ordinary form-style POST, cookies offered, no custom headers (a custom header would
  // trigger a preflight and the run would then measure CORS rather than the session).
  const foreign = await openTab(call, `${CROSS}/probe.html`, { waitFor: 'body' })
  await waitUntil(call, foreign.sessionId, 'document.body !== null', 8000)

  const foreignToken = await tokenInJarLocal(call, foreign.sessionId)
  record('CASE 2c  the foreign page cannot read the app token', foreignToken === '', `its jar yields ${JSON.stringify(foreignToken)}`)

  const before = events.length
  // `mode: 'no-cors'`, which is what makes this a *simple* request the platform is willing to put on the wire: a
  // CORS-mode request with credentials gets a response the page cannot read, and the first run of this line produced
  // `TypeError: Failed to fetch` with **no CDP response event at all** — a probe that never reaches the server cannot
  // distinguish "the cookie was withheld" from "the browser refused to ask". With no-cors the request is sent, the
  // response is opaque to the page, and the status survives in the browser's own telemetry, which is the only place
  // it can be read from.
  const cross = await evalJs(call, foreign.sessionId, `(async () => {
    try {
      const res = await fetch(${JSON.stringify(`${APP}/api/applications`)}, {
        mode: 'no-cors',
        method: 'POST', credentials: 'include',
        headers: { 'content-type': 'text/plain' },
        body: 'csrf-probe',
      })
      return 'opaque status ' + res.status + ' type ' + res.type
    } catch (err) {
      return 'rejected ' + String(err).slice(0, 60)
    }
  })()`)

  await sleep(700)
  const seen = events.slice(before).filter((e) => e.url === `${APP}/api/applications`)
  const crossStatus = seen.length ? seen[seen.length - 1].status : null
  record(
    'CASE 2  a cross-site write answers 401 (no cookie), never 403 (the gate was not reached)',
    crossStatus === 401,
    `CDP matched ${JSON.stringify(seen.map((e) => e.status ?? e.failed))}; all events this run `
      + `saw ${JSON.stringify(events.slice(before).map((e) => `${e.status ?? e.failed} ${String(e.url).slice(-28)}`)).slice(0, 400)}; `
      + `page reported ${JSON.stringify(String(cross.value ?? ''))}`,
  )

  // --- C1e / C1f: the token belongs to a session, not to the user -------------------------------------------
  //
  // Logged out and back in through the real form, so the jar now holds a **new** session and a **new** token while the
  // old token string is still in this variable. Refusing the old one is what makes the value *bound*: a token that
  // worked across sessions would be a shared password, stealable once and reusable forever — and `matches()` on a
  // header alone cannot show that, which is why it is here as well as in `A_token_belonging_to_another_session_is_refused`.
  //
  // It doubles as the browser-side evidence for `-052`'s rotation: after a logout the old value is dead, so nothing a
  // previous page held can write on the next one's behalf.
  const staleToken = token
  const out = await evalJs(call, app.sessionId, `(async () => {
    const res = await fetch(${JSON.stringify(`${APP}/api/auth/logout`)}, { method: 'POST', credentials: 'same-origin' })
    return String(res.status)
  })()`)
  if (String(out.value) !== '204') {
    throw new Error(`logout answered ${out.value}; the rotation check below would be measuring a session that never ended`)
  }

  const fresh = await openTab(call, `${APP}/applications`)
  console.log(`re-authenticating: ${await authenticate(call, fresh)}`)
  const newToken = await tokenInJarLocal(call, fresh.sessionId)
  if (!newToken || newToken === staleToken) {
    throw new Error(`login again produced no distinct token (same value as before: ${newToken === staleToken})`)
  }

  const staleMarker = 'CSRF-063-stale'
  const stale = await postFromPage(call, fresh.sessionId, {
    withHeader: true, headerValue: staleToken, credentials: 'same-origin', marker: staleMarker,
  })
  record(
    'CASE 1e  a token from the previous session is refused after rotation',
    stale.status === 403 && stale.code === 'antiforgery',
    `status=${stale.status} code=${stale.code} — the value is bound to a session, not to the account`,
  )

  const freshMarker = 'CSRF-063-fresh'
  const freshWrite = await postFromPage(call, fresh.sessionId, {
    withHeader: true, headerValue: newToken, credentials: 'same-origin', marker: freshMarker,
  })
  record(
    'CASE 1f  POSITIVE CONTROL: the token issued by the new session is served',
    freshWrite.status === 201,
    `status=${freshWrite.status} code=${freshWrite.code}`,
  )

  const leftovers = []
  for (const m of [okMarker, freshMarker]) {
    const r = await cleanup(call, fresh.sessionId, m)
    leftovers.push(`${m}:${r}`)
  }
  record('both control records were deleted again', leftovers.every((l) => l.endsWith(':cleaned')), leftovers.join(' '))

  const failed = results.filter((r) => !r.pass)
  console.log(`\n${results.length - failed.length}/${results.length} browser checks passed`)
  ws.close()
  process.exit(failed.length ? 1 : 0)
}

main().catch((err) => {
  console.error(`HARNESS ABORTED: ${err?.stack ?? err}`)
  process.exit(2)
})
