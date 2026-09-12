// Does __Host- survive over TLS-with-a-self-signed-cert? If yes, the fix is https-in-dev, not origin-shifting.
import https from 'node:https'
import { readFileSync } from 'node:fs'
const PORT = 5396
const SETS = [
  '__Host-JTSession=1; Secure; HttpOnly; Path=/; SameSite=Lax',
  'JTPlain=1; Secure; HttpOnly; Path=/; SameSite=Lax',
]
const srv = https.createServer({ key: readFileSync('/srv/k.pem'), cert: readFileSync('/srv/c.pem') }, (req, res) => {
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
for (const host of ['127.0.0.1', 'localhost']) {
  const base = `https://${host}:${PORT}`
  const { targetId } = await call('Target.createTarget', { url: 'about:blank' })
  const { sessionId } = await call('Target.attachToTarget', { targetId, flatten: true })
  await call('Runtime.enable', {}, sessionId); await call('Page.enable', {}, sessionId)
  await call('Security.setPermission', { origin: base, setting: 'certificateErrorBypass' }, sessionId).catch(e => console.log('  (permission bypass refused:', e.message.slice(0, 40) + ')'))
  try { await call('Security.setIgnoreCertificateErrors', { ignore: true }, sessionId) } catch (e) { console.log('  (setIgnoreCertificateErrors refused:', e.message.slice(0, 40) + ')') }
  let title = null
  try {
    await call('Page.navigate', { url: base + '/' }, sessionId)
    await new Promise(r => setTimeout(r, 900))
    const t = await call('Runtime.evaluate', { expression: 'document.title + "|" + (window.isSecureContext?"SC:true":"SC:false")', returnByValue: true }, sessionId)
    title = t.result?.value
  } catch (e) { title = 'nav error ' + e.message.slice(0, 40) }
  let echo = '(unreachable)'
  try {
    const r = await call('Runtime.evaluate', { expression: `(async () => { await fetch('/set'); return await fetch('/echo').then(x=>x.text()) })()`, awaitPromise: true, returnByValue: true }, sessionId)
    echo = r.result?.value ?? '(undefined result)'
  } catch (e) { echo = 'fetch failed: ' + e.message.slice(0, 50) }
  console.log(`\n### ${base}  page="${title}"`)
  console.log(`  server saw: ${echo}`)
  for (const n of ['__Host-JTSession', 'JTPlain']) console.log(`  ${n.padEnd(17)} accepted: ${echo.includes(n + '=') ? 'YES' : 'NO '}`)
  await call('Target.closeTarget', { targetId })
}
ws.close(); srv.close(); process.exit(0)
