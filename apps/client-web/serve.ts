/**
 * In-tree static file server for the built web client (no external dev server). Serves
 * `dist/esm/` over loopback HTTP so the browser can fetch the ES modules referenced by the import
 * map (file:// blocks cross-directory module fetches in some browsers). Dependency-free: node's own
 * http/fs. WHY a server at all: native ES-module + import-map loading needs an http(s) origin.
 *
 * Run: `node serve.ts [port]` (default 8080). Path traversal is rejected (resolved path must stay
 * within dist/esm). Used by the dev workflow and by the headless render check (verify-render.ts).
 */
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { dirname, join, normalize, extname } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), 'dist', 'esm');
const PORT = Number(process.argv[2] ?? process.env.PORT ?? 8080);

const MIME: Record<string, string> = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.map': 'application/json; charset=utf-8',
};

const server = createServer((req, res) => {
  const url = (req.url ?? '/').split('?')[0] ?? '/';
  const rel = url === '/' ? 'index.html' : decodeURIComponent(url).replace(/^\/+/, '');
  const path = normalize(join(ROOT, rel));
  if (!path.startsWith(ROOT)) {
    res.writeHead(403).end('forbidden');
    return;
  }
  readFile(path).then(
    (buf) => {
      res.writeHead(200, { 'content-type': MIME[extname(path)] ?? 'application/octet-stream' });
      res.end(buf);
    },
    () => {
      res.writeHead(404).end('not found');
    },
  );
});

server.listen(PORT, '127.0.0.1', () => {
  console.log(`serving ${ROOT} at http://127.0.0.1:${PORT}/`);
});
