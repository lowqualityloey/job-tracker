// Does Chromium accept a `Secure` `__Host-` cookie over plain http, and does the ADDRESS FORM decide it?
// DECISION-007 option (a) rests on this; ASSUMPTION-m4-auth-001 has never been proven.
import http from 'node:http'
const PORT = 5399
const seen = []
const srv = http.createServer((req, res) => {
  if (req.url === '/set') {
    res.setHeader('Set-Cookie', '__Host-JTP=1; Secure; HttpOnly; Path=/; SameSite=Lax')
    res.setHeader('Content-Type', 'text/plain'); return res.end('set')
  }
  if (req.url === '/echo') {
    seen.push(req.headers.cookie ?? '(none)')
    res.setHeader('Content-Type', 'text/plain'); return res.end(seen[seen.length - 1])
  }
  res.setHeader('Content-Type', 'text/html')
  res.end('<!doctype html><title>p</title><body>ok</body>')
})
await new Promise(r => srv.listen(PORT, '0.0.0.0', r))

const LOOPBACKS = ['127.0.0.1', '[::1]']
async function connect() {
  for (let i = 0; i < 40; i++) {
    for (const h of LOOPBACKS) {
      try {
        const { webSocketDebuggerUrl } = await (await fetch(`http://${h}:9222/json/version`)).json()
        const ws = new WebSocket(webSocketDebuggerUrl)
        await new Promise((r, j) => { ws.onopen = r; ws.onerror = j })
        return ws
      } catch (e) {
        // Never an empty block. An empty `catch {}` here is what let a MISSING GLOBAL (`WebSocket` is undefined in
        // this image's Node 20 without --experimental-websocket) surface as "no CDP" — a verdict about the browser
        // built entirely from an exception this line threw away. Report the first failure; stay quiet after it.
        if (i === 0) console.log(`  (attempt via ${h}: ${e.message})`)
      }
    }
    await new Promise(r => setTimeout(r, 200))
  }
  throw new Error('no CDP — see the attempt lines above; the usual cause is a missing --experimental-websocket')
}
const ws = await connect()
let id = 0; const pending = new Map()
ws.onmessage = e => { const m = JSON.parse(e.data); if (m.id !== undefined) { const p = pending.get(m.id); pending.delete(m.id); m.error ? p.reject(new Error(JSON.stringify(m.error))) : p.resolve(m.result) } }
const call = (method, params = {}, sessionId) => new Promise((resolve, reject) => { const n = ++id; pending.set(n, { resolve, reject }); ws.send(JSON.stringify(sessionId ? { id: n, method, params, sessionId } : { id: n, method, params })) })

const myIp = process.env.PROBE_HOST_IP
const targets = ['http://127.0.0.1:' + PORT, ...(myIp ? ['http://' + myIp + ':' + PORT] : [])]
for (const base of targets) {
  const origin = new URL(base).hostname
  let out
  try {
    const { targetId } = await call('Target.createTarget', { url: 'about:blank' })
    const { sessionId } = await call('Target.attachToTarget', { targetId, flatten: true })
    await call('Runtime.enable', {}, sessionId); await call('Page.enable', {}, sessionId)
    await call('Page.navigate', { url: base + '/' }, sessionId)
    await new Promise(r => setTimeout(r, 600))
    const expr = `(async () => {
      const sec = window.isSecureContext
      await fetch('/set').then(r=>r.text())
      const echo = await fetch('/echo').then(r=>r.text())
      return JSON.stringify({ sec, echo })
    })()`
    const r = await call('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true }, sessionId)
    out = JSON.parse(r.result.value)
    await call('Target.closeTarget', { targetId })
  } catch (e) { out = { error: String(e).slice(0, 80) } }
  const carried = out.echo && out.echo.includes('__Host-JTP')
  console.log(`ORIGIN ${origin.padEnd(14)} isSecureContext=${String(out.sec).padEnd(5)} cookie-carried=${carried ? 'YES' : 'no '} ${out.error ?? ''}`)
}
srv.close(); ws.close(); process.exit(0)
