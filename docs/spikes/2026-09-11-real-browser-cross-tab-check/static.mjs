// Minimal dependency-free static file server for `vite preview`'s output, used only inside the
// Chromium container so the app and the browser share a network namespace. SPA fallback included,
// because `/applications` is a client-side route the production build must answer itself.
import { createServer } from 'node:http'
import { readFile } from 'node:fs/promises'
import { extname, join, normalize } from 'node:path'

const ROOT = '/srv/dist'
const TYPES = { '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml' }

createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname)
  // normalize + prefix check: a static server that escapes its root would be a real bug, not a
  // convenience issue, even in a throwaway container.
  const target = normalize(join(ROOT, path))
  if (!target.startsWith(ROOT)) {
    res.writeHead(403).end('no')
    return
  }
  let file = target
  try {
    await readFile(file)
  } catch {
    file = join(ROOT, 'index.html') // SPA fallback
    try {
      await readFile(file)
    } catch {
      res.writeHead(404).end('not found')
      return
    }
  }
  const body = await readFile(file)
  res.writeHead(200, { 'content-type': TYPES[extname(file)] ?? 'application/octet-stream', 'cache-control': 'no-store' })
  res.end(body)
}).listen(4173, '127.0.0.1', () => console.log('static server on 4173'))
