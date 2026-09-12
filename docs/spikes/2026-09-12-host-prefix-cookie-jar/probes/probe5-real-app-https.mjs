// The spike's "Not measured" item 1: the REAL /api/auth/login in a REAL browser.
// Origin is https://<host-ip>:5443 -- non-loopback, self-signed, untrusted. If __Host- survives here,
// (a-prime) works in the only topology the container can actually reach.
const BASE = 'https://' + process.env.PROBE_HOST_IP + ':5443'
const EMAIL = 'pemcheck@example.test', PASS = 'a-sufficiently-long-dev-passphrase'
async function connect() {
  for (let i = 0; i < 20; i++) {
    try {
      const { webSocketDebuggerUrl } = await (await fetch('http://127.0.0.1:9222/json/version')).json()
      const ws = new WebSocket(webSocketDebuggerUrl); await new Promise((r, j) => { ws.onopen = r; ws.onerror = j }); return ws
    } catch (e) { if (i === 0) console.log(`  (attempt: ${e.message})`); await new Promise(r => setTimeout(r, 200)) }
  }
  throw new Error('no CDP')
}
const ws = await connect(); let id = 0; const pending = new Map()
ws.onmessage = e => { const m = JSON.parse(e.data); if (m.id !== undefined) { const p = pending.get(m.id); pending.delete(m.id); m.error ? p.reject(new Error(JSON.stringify(m.error))) : p.resolve(m.result) } }
const call = (method, params = {}, sessionId) => new Promise((resolve, reject) => { const n = ++id; pending.set(n, { resolve, reject }); ws.send(JSON.stringify(sessionId ? { id: n, method, params, sessionId } : { id: n, method, params })) })

const { targetId } = await call('Target.createTarget', { url: 'about:blank' })
const { sessionId } = await call('Target.attachToTarget', { targetId, flatten: true })
await call('Runtime.enable', {}, sessionId); await call('Page.enable', {}, sessionId); await call('Network.enable', {}, sessionId)
await call('Security.setIgnoreCertificateErrors', { ignore: true }, sessionId)
await call('Network.clearBrowserCookies', {}, sessionId)
await call('Page.navigate', { url: BASE + '/api/applications' }, sessionId)
await new Promise(r => setTimeout(r, 1200))

const expr = `(async () => {
  const login = await fetch('/api/auth/login', { method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: ${JSON.stringify(EMAIL)}, password: ${JSON.stringify(PASS)} }),
    credentials: 'same-origin' })
  const loginBody = login.status >= 400 ? (await login.text()).slice(0, 120) : ''
  const protectedRes = await fetch('/api/applications', { credentials: 'same-origin' })
  const protectedStatus = protectedRes.status
  return JSON.stringify({ sec: window.isSecureContext, login: login.status, loginBody, protectedStatus })
})()`
const r = await call('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true }, sessionId)
const o = JSON.parse(r.result.value)
const { cookies } = await call('Storage.getCookies', {}, sessionId)
const jar = cookies.filter(c => /JTSession/i.test(c.name))
console.log(`origin              ${BASE}`)
console.log(`isSecureContext     ${o.sec}`)
console.log(`POST /api/auth/login  -> ${o.login} ${o.loginBody}`)
console.log(`GET  /api/applications -> ${o.protectedStatus}   (401 = no session; 200 = session held)`)
console.log(`cookie jar          ${jar.length ? jar.map(c => `${c.name} secure=${c.secure} httpOnly=${c.httpOnly} domain=${c.domain} sameSite=${c.sameSite}`).join(', ') : 'EMPTY -- __Host-JTSession was discarded'}`)
await call('Target.closeTarget', { targetId }); ws.close(); process.exit(0)
