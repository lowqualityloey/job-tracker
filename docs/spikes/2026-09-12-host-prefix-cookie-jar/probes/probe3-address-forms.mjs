import http from 'node:http'
const PORT = 5397
// Attribute-IDENTICAL, only the prefix differs. __Host-JTSession is the real production name.
const SETS = [
  '__Host-JTSession=1; Secure; HttpOnly; Path=/; SameSite=Lax',
  '__Host-JTProbe=1; Secure; HttpOnly; Path=/; SameSite=Lax',
  '__Secure-JTProbe=1; Secure; HttpOnly; Path=/; SameSite=Lax',
  'JTPlain=1; Secure; HttpOnly; Path=/; SameSite=Lax',
  'JTHostNoDom=1; Secure; HttpOnly; Path=/',
]
const srv = http.createServer((req, res) => {
  if (req.url === '/set') { res.setHeader('Set-Cookie', SETS); res.end('set'); return }
  if (req.url === '/echo') { res.end(req.headers.cookie ?? '(none)'); return }
  res.setHeader('Content-Type', 'text/html'); res.end('<!doctype html><title>p</title>')
})
await new Promise(r => srv.listen(PORT, '0.0.0.0', r))
async function connect() {
  for (let i = 0; i < 20; i++) {
    try {
      const { webSocketDebuggerUrl } = await (await fetch('http://127.0.0.1:9222/json/version')).json()
      const ws = new WebSocket(webSocketDebuggerUrl); await new Promise((r, j) => { ws.onopen = r; ws.onerror = j }); return ws
    } catch { await new Promise(r => setTimeout(r, 200)) }
  }
  throw new Error('no CDP')
}
const ws = await connect(); let id = 0; const pending = new Map()
ws.onmessage = e => { const m = JSON.parse(e.data); if (m.id !== undefined) { const p = pending.get(m.id); pending.delete(m.id); m.error ? p.reject(new Error(JSON.stringify(m.error))) : p.resolve(m.result) } }
const call = (method, params = {}, sessionId) => new Promise((resolve, reject) => { const n = ++id; pending.set(n, { resolve, reject }); ws.send(JSON.stringify(sessionId ? { id: n, method, params, sessionId } : { id: n, method, params })) })
const names = ['__Host-JTSession', '__Host-JTProbe', '__Secure-JTProbe', 'JTPlain', 'JTHostNoDom']
for (const host of ['127.0.0.1', 'localhost', process.env.PROBE_HOST_IP]) {
  const base = `http://${host}:${PORT}`
  const { targetId } = await call('Target.createTarget', { url: 'about:blank' })
  const { sessionId } = await call('Target.attachToTarget', { targetId, flatten: true })
  await call('Runtime.enable', {}, sessionId); await call('Page.enable', {}, sessionId)
  await call('Page.navigate', { url: base + '/' }, sessionId)
  await new Promise(r => setTimeout(r, 500))
  const expr = `(async () => { await fetch('/set'); const echo = await fetch('/echo').then(r=>r.text()); return JSON.stringify({sec: window.isSecureContext, echo}) })()`
  const r = await call('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true }, sessionId)
  const o = JSON.parse(r.result.value)
  console.log(`\n### http://${host}  isSecureContext=${o.sec}`)
  for (const n of names) console.log(`  ${(n + ' ').padEnd(17)} accepted: ${o.echo.includes(n + '=') ? 'YES' : 'NO '}`)
  await call('Target.closeTarget', { targetId })
}
ws.close(); srv.close(); process.exit(0)
