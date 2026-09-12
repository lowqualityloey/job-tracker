// Which cookie shape fails, and is it storage or transmission? Three variants per origin.
import http from 'node:http'
const PORT = 5398
const srv = http.createServer((req, res) => {
  if (req.url === '/set') {
    res.setHeader('Set-Cookie', [
      'HOST_SEC=1; Secure; HttpOnly; Path=/; SameSite=Lax',
      'HOST_NOSEC=1; HttpOnly; Path=/; SameSite=Lax',
      'PLAIN_SEC=1; Secure; Path=/; SameSite=Lax',
    ])
    res.setHeader('Content-Type', 'text/plain'); return res.end('set')
  }
  if (req.url === '/echo') { res.setHeader('Content-Type', 'text/plain'); return res.end(req.headers.cookie ?? '(none)') }
  res.setHeader('Content-Type', 'text/html'); res.end('<!doctype html><title>p</title>')
})
await new Promise(r => srv.listen(PORT, '0.0.0.0', r))
async function connect() {
  for (let i = 0; i < 20; i++) {
    try {
      const { webSocketDebuggerUrl } = await (await fetch('http://127.0.0.1:9222/json/version')).json()
      const ws = new WebSocket(webSocketDebuggerUrl)
      await new Promise((r, j) => { ws.onopen = r; ws.onerror = j })
      return ws
    } catch { await new Promise(r => setTimeout(r, 200)) }
  }
  throw new Error('no CDP')
}
const ws = await connect(); let id = 0; const pending = new Map()
ws.onmessage = e => { const m = JSON.parse(e.data); if (m.id !== undefined) { const p = pending.get(m.id); pending.delete(m.id); m.error ? p.reject(new Error(JSON.stringify(m.error))) : p.resolve(m.result) } }
const call = (method, params = {}, sessionId) => new Promise((resolve, reject) => { const n = ++id; pending.set(n, { resolve, reject }); ws.send(JSON.stringify(sessionId ? { id: n, method, params, sessionId } : { id: n, method, params })) })
for (const base of ['http://127.0.0.1:' + PORT, 'http://' + process.env.PROBE_HOST_IP + ':' + PORT]) {
  const host = new URL(base).hostname
  const { targetId } = await call('Target.createTarget', { url: 'about:blank' })
  const { sessionId } = await call('Target.attachToTarget', { targetId, flatten: true })
  await call('Runtime.enable', {}, sessionId); await call('Page.enable', {}, sessionId)
  await call('Page.navigate', { url: base + '/' }, sessionId)
  await new Promise(r => setTimeout(r, 500))
  const expr = `(async () => {
    const setText = await fetch('/set').then(r => r.status + ':' + r.text())
    const st = await Promise.resolve(setText)
    const echo = await fetch('/echo').then(r => r.text())
    return JSON.stringify({ sec: window.isSecureContext, set: st, echo, doc: document.cookie })
  })()`
  const r = await call('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true }, sessionId)
  const o = JSON.parse(r.result.value)
  console.log(`\n### ${host}  isSecureContext=${o.sec}`)
  console.log(`  /set        -> ${o.set}`)
  console.log(`  server saw  -> ${o.echo}`)
  console.log(`  document.cookie (non-HttpOnly only) -> "${o.doc}"`)
  for (const k of ['HOST_SEC', 'HOST_NOSEC', 'PLAIN_SEC']) {
    console.log(`  ${k.padEnd(11)} stored+sent: ${o.echo.includes(k + '=') ? 'YES' : 'NO '}`)
  }
  await call('Target.closeTarget', { targetId })
}
ws.close(); srv.close(); process.exit(0)
