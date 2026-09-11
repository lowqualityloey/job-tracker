// Real-browser cross-tab verification for M2b's DEBT-01 residual + AC-10's unmeasured half.
// Deliberately dependency-free: raw Chrome DevTools Protocol over Node 24's global WebSocket,
// driving a Chromium that runs in Docker. Nothing here is installed into the repository.
//
// Why two targets on ONE browser connection: a StorageEvent only fires in the *other* same-origin
// tab, and only when both tabs share a browser context. Two Playwright contexts (or two profiles)
// would have isolated storage and the test would pass for the wrong reason or fail for one.

const CDP_PORT = 9222
const APP = 'http://127.0.0.1:4173'
const results = []
const record = (name, pass, detail) => {
  results.push({ name, pass, detail })
  console.log(`${pass ? 'PASS' : 'FAIL'}  ${name}${detail ? `  — ${detail}` : ''}`)
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

async function connect() {
  for (let i = 0; i < 60; i++) {
    try {
      const res = await fetch(`http://127.0.0.1:${CDP_PORT}/json/version`)
      const { webSocketDebuggerUrl } = await res.json()
      const ws = new WebSocket(webSocketDebuggerUrl)
      await new Promise((resolve, reject) => {
        ws.addEventListener('open', resolve, { once: true })
        ws.addEventListener('error', reject, { once: true })
      })
      return ws
    } catch {
      await sleep(500)
    }
  }
  throw new Error('Chromium CDP endpoint never came up')
}

function makeClient(ws) {
  let id = 0
  const pending = new Map()
  ws.addEventListener('message', (ev) => {
    const msg = JSON.parse(ev.data)
    if (msg.id && pending.has(msg.id)) {
      const { resolve, reject } = pending.get(msg.id)
      pending.delete(msg.id)
      msg.error ? reject(new Error(JSON.stringify(msg.error))) : resolve(msg.result)
    }
  })
  return (method, params = {}, sessionId) =>
    new Promise((resolve, reject) => {
      const n = ++id
      pending.set(n, { resolve, reject })
      ws.send(JSON.stringify({ id: n, method, params, ...(sessionId ? { sessionId } : {}) }))
    })
}

async function openTab(call, url) {
  const { targetId } = await call('Target.createTarget', { url })
  const { sessionId } = await call('Target.attachToTarget', { targetId, flatten: true })
  await call('Runtime.enable', {}, sessionId)
  await call('Page.enable', {}, sessionId)
  // Wait until the SPA has rendered a real control, not just the shell.
  for (let i = 0; i < 80; i++) {
    const r = await evalJs(call, sessionId, `!!document.querySelector('.application-card, .empty-state')`)
    if (r.value === true) return { targetId, sessionId }
    await sleep(250)
  }
  throw new Error(`app never rendered at ${url}`)
}

async function evalJs(call, sessionId, expression) {
  const r = await call('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true }, sessionId)
  // Surface page-level throws instead of turning them into a silent `value: undefined`. The first run
  // of this harness failed exactly that way: `Runtime.evaluate` answers with {result:{value}}, and
  // reading `.value` off the wrapper made a fully-rendered page look like it had never rendered.
  if (r.exceptionDetails) throw new Error('page threw: ' + JSON.stringify(r.exceptionDetails).slice(0, 300))
  return r.result ?? { value: undefined }
}

async function typeInto(call, sessionId, selector, text) {
  await evalJs(call, sessionId, `document.querySelector(${JSON.stringify(selector)}).focus()`)
  await call('Input.insertText', { text }, sessionId)
}

// `selector` is a CSS selector string, NOT a JS expression: it is JSON.stringify'd into
// document.querySelector(...). Passing an expression yields a DOMException SyntaxError, which is how
// this harness failed on its third run.
async function clickEl(call, sessionId, selector) {
  await evalJs(call, sessionId, `document.querySelector(${JSON.stringify(selector)}).click()`)
}

// Poll an expression in a tab that we never reload. The absence of a reload is the test.
async function waitUntil(call, sessionId, expression, ms = 8000) {
  const deadline = Date.now() + ms
  for (;;) {
    const r = await evalJs(call, sessionId, `(function(){ return (${expression}) })()`)
    if (r.value === true) return true
    if (Date.now() > deadline) return false
    await sleep(150)
  }
}

const ws = await connect()
const call = makeClient(ws)
console.log('CDP connected (one browser, two tabs, shared context)\n')

// ---- tab A: the app, freshly booted from the production build -------------------------------------
const A = await openTab(call, `${APP}/applications`)
const B = await openTab(call, `${APP}/applications`)

const seeded = await evalJs(call, A.sessionId, `document.querySelectorAll('.application-card').length`)
record('production build boots and renders in a real browser', seeded.value >= 5, `cards=${seeded.value}`)

const countA = await evalJs(call, A.sessionId, `document.querySelector('.filter-count')?.textContent ?? '(none)'`)
record('filter count region renders', /Showing \d+ of \d+/.test(countA.value), JSON.stringify(countA.value))

// ---- behaviour 022, for real: write in tab A, watch tab B without reloading it ---------------------
await clickEl(call, A.sessionId, 'a[href="/applications/new"]')
const onForm = await waitUntil(call, A.sessionId, `!!document.querySelector('#companyName')`)
record('navigation to the create form works', onForm, 'client-side routing in a browser, not MemoryRouter')

if (onForm) {
  await typeInto(call, A.sessionId, '#companyName', 'Playwright Verified Co')
  await typeInto(call, A.sessionId, '#jobTitle', 'Cross-Tab Engineer')
  await typeInto(call, A.sessionId, '#location', 'Wellington')
  await clickEl(call, A.sessionId, 'button[type="submit"]')
  const inA = await waitUntil(call, A.sessionId, `document.body.innerText.includes('Playwright Verified Co')`)
  record('tab A shows the record it just created', inA)
  const inB = await waitUntil(call, B.sessionId, `document.body.innerText.includes('Playwright Verified Co')`)
  const cardsB = await evalJs(call, B.sessionId, `document.querySelectorAll('.application-card').length`)
  record('BEHAVIOR-022 in a REAL browser: tab B updated with no reload', inB, `cards in B=${cardsB.value}`)

  const countB = await evalJs(call, B.sessionId, `document.querySelector('.filter-count')?.textContent ?? ''`)
  record('tab B count updated alongside the rows', /of 6 applications/.test(countB.value) || /6/.test(countB.value), JSON.stringify(countB.value))
}

// ---- the direction jsdom can never model: tab B writes, tab A must see it ---------------------------
await clickEl(call, B.sessionId, 'a[href="/applications/new"]')
const onFormB = await waitUntil(call, B.sessionId, `!!document.querySelector('#companyName')`)
if (onFormB) {
  await typeInto(call, B.sessionId, '#companyName', 'Second Tab Ltd')
  await typeInto(call, B.sessionId, '#jobTitle', 'Reverse Direction Test')
  await typeInto(call, B.sessionId, '#location', 'Auckland')
  await clickEl(call, B.sessionId, 'button[type="submit"]')
  const inA = await waitUntil(call, A.sessionId, `document.body.innerText.includes('Second Tab Ltd')`)
  record('the writer-tab rule holds in a browser: tab A saw tab B write', inA)
}

// ---- AC-10's unmeasured half: hit targets and the keyboard focus ring, as computed ------------------
const geometry = await evalJs(
  call,
  A.sessionId,
  `(function(){
     const chip = document.querySelector('.chip');
     const input = document.querySelector('.filter-input');
     const clear = document.querySelector('.clear-filters');
     const box = (el) => el ? Math.round(el.getBoundingClientRect().height) : null;
     return JSON.stringify({ chip: box(chip), input: box(input), clear: box(clear),
                             chipMin: chip ? getComputedStyle(chip).minHeight : null });
   })()`,
)
const g = JSON.parse(geometry.value)
record('chip hit target is >= 44px as rendered', g.chip >= 44, `height=${g.chip}px minHeight=${g.chipMin}`)
record('search input hit target is >= 44px as rendered', g.input >= 44, `height=${g.input}px`)

// Keyboard-only traversal to a chip, then read the ring the browser actually paints.
await evalJs(call, A.sessionId, `document.body.focus()`)
let steps = -1
let outline = 'none'
for (let i = 0; i < 25; i++) {
  for (const type of ['keyDown', 'keyUp']) {
    await call('Input.dispatchKeyEvent', { type, key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 }, A.sessionId)
  }
  const who = await evalJs(
    call,
    A.sessionId,
    `(function(){ const el = document.activeElement;
       return JSON.stringify({ cls: el.className || el.tagName, out: getComputedStyle(el).outlineWidth,
                               style: getComputedStyle(el).outlineStyle, color: getComputedStyle(el).outlineColor });
     })()`,
  )
  const w = JSON.parse(who.value)
  if (String(w.cls).includes('chip')) { steps = i + 1; outline = `${w.style} ${w.out} ${w.color}`; break }
}
record('a filter chip is reachable by Tab alone', steps > 0, `reached after ${steps} tab stops`)
record(':focus-visible paints a real ring on the chip', /3px/.test(outline) && /solid/.test(outline), outline)

// ---- storage-layer truth: what is actually on disk in a browser, right now -------------------------
const stored = await evalJs(
  call,
  A.sessionId,
  `(function(){ const raw = localStorage.getItem('job-tracker:applications');
     const parsed = JSON.parse(raw);
     return JSON.stringify({ keys: Object.keys(localStorage), v: parsed.schemaVersion, n: parsed.applications.length });
   })()`,
)
const st = JSON.parse(stored.value)
record('the envelope on disk is schemaVersion 1 with both new records', st.v === 1 && st.n === 7, JSON.stringify(st))
record('only our own key was written', st.keys.length === 1 && st.keys[0] === 'job-tracker:applications', st.keys.join(','))

ws.close()
console.log('\n================ ' + `${results.filter((r) => r.pass).length}/${results.length} checks passed`)
process.exit(results.every((r) => r.pass) ? 0 : 1)
