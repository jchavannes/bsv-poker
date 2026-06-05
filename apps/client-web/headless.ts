/**
 * Shared headless-browser harness (no external automation library). Serves a built directory over
 * loopback and loads a page in headless Chrome/Edge, returning the post-script DOM dump. Used by both
 * verify-render.ts (the app renders) and verify-dom.ts (the dom.ts view core behaves).
 *
 * Robustness notes baked in from practice: an isolated throwaway profile + disabled background
 * services keep headless Chrome/Edge from hanging on telemetry; async `spawn` drains stdout as it
 * arrives (spawnSync can deadlock on a large DOM dump); the browser's noisy stderr is discarded.
 */
import { createServer, type Server } from 'node:http';
import { spawn } from 'node:child_process';
import { readFile, mkdtemp, rm } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, normalize, extname } from 'node:path';
import type { AddressInfo } from 'node:net';

const MIME: Record<string, string> = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.map': 'application/json; charset=utf-8',
};

/** Locate a headless-capable Chromium (Chrome/Edge). Returns null if none is found. */
export function findBrowser(): string | null {
  const env = process.env.BSV_CHROME;
  if (env && existsSync(env)) return env;
  const candidates = [
    `${process.env.ProgramFiles}\\Google\\Chrome\\Application\\chrome.exe`,
    `${process.env['ProgramFiles(x86)']}\\Google\\Chrome\\Application\\chrome.exe`,
    `${process.env.LOCALAPPDATA}\\Google\\Chrome\\Application\\chrome.exe`,
    `${process.env.ProgramFiles}\\Microsoft\\Edge\\Application\\msedge.exe`,
    `${process.env['ProgramFiles(x86)']}\\Microsoft\\Edge\\Application\\msedge.exe`,
    '/usr/bin/google-chrome',
    '/usr/bin/chromium',
    '/usr/bin/chromium-browser',
  ];
  return candidates.find((p) => p && existsSync(p)) ?? null;
}

function serveDir(dir: string): Promise<Server> {
  const server = createServer((req, res) => {
    const url = (req.url ?? '/').split('?')[0] ?? '/';
    const rel = url === '/' ? 'index.html' : decodeURIComponent(url).replace(/^\/+/, '');
    const path = normalize(join(dir, rel));
    if (!path.startsWith(dir)) { res.writeHead(403).end('forbidden'); return; }
    readFile(path).then(
      (buf) => { res.writeHead(200, { 'content-type': MIME[extname(path)] ?? 'application/octet-stream' }).end(buf); },
      () => { res.writeHead(404).end('not found'); },
    );
  });
  return new Promise((resolve) => server.listen(0, '127.0.0.1', () => resolve(server)));
}

/**
 * Serve `dir`, load `dir/<relPath>` in `browser`, and return the dumped DOM. Throws on timeout or a
 * launch error. `--dump-dom` fires at the load event; the app/test renders synchronously, so the DOM
 * is present by then.
 */
export async function dumpDom(browser: string, dir: string, relPath: string): Promise<string> {
  const server = await serveDir(dir);
  const port = (server.address() as AddressInfo).port;
  const url = `http://127.0.0.1:${port}/${relPath}`;
  const profile = await mkdtemp(join(tmpdir(), 'bsv-headless-'));
  try {
    return await new Promise<string>((resolve, reject) => {
      const child = spawn(
        browser,
        ['--headless=new', '--disable-gpu', '--no-sandbox', '--disable-dev-shm-usage',
          '--disable-background-networking', '--disable-default-apps', '--disable-extensions', '--disable-sync',
          '--disable-component-update', '--metrics-recording-only', '--mute-audio', '--no-first-run',
          '--no-default-browser-check', '--disable-features=Translate,MediaRouter,OptimizationHints,InterestFeedContentSuggestions',
          `--user-data-dir=${profile}`, '--dump-dom', url],
        { stdio: ['ignore', 'pipe', 'ignore'] },
      );
      const chunks: Buffer[] = [];
      child.stdout.on('data', (c: Buffer) => chunks.push(c));
      const timer = setTimeout(() => { child.kill('SIGKILL'); reject(new Error('headless dump timed out (90s)')); }, 90_000);
      child.on('error', (e) => { clearTimeout(timer); reject(e); });
      child.on('close', () => { clearTimeout(timer); resolve(Buffer.concat(chunks).toString('utf8')); });
    });
  } finally {
    server.close();
    await rm(profile, { recursive: true, force: true }).catch(() => {});
  }
}
