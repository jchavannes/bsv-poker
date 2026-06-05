/**
 * Render verification (REQ-BUILD-003 / standalone mandate) — proves the built web client actually
 * RENDERS in a real browser DOM, not merely that a process stays alive.
 *
 * WHAT: serves dist/esm over loopback, loads index.html in headless Chrome/Edge, and dumps the
 * post-script DOM. The app's module graph (import-map-resolved, no bundler) must load and execute,
 * the vanilla `mount` must build the lobby, and the dumped DOM must contain the lobby's landmark
 * text. Any module-resolution failure, runtime throw, or empty `#root` fails the check.
 *
 * HOW: `--headless=new --dump-dom --virtual-time-budget` renders then prints the serialized DOM to
 * stdout; we assert required markers are present. No external automation library — the browser's own
 * headless DOM dump is the oracle.
 *
 * WHY headless-DOM (not jsdom): jsdom is an external dependency AND not a real browser; the bar is a
 * REAL render. The relay is intentionally NOT running, so the lobby renders its static structure plus
 * a "failed to load tables" notice — which still proves the whole graph executed and mounted.
 *
 * Run: `node verify-render.ts`. Exits 0 on success; 0 with SKIP if no headless browser is found
 * (the build stage still gates the bundle); 1 if the DOM lacks the required markers.
 */
import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { readFile, mkdtemp, rm } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, normalize, extname } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { AddressInfo } from 'node:net';

const APP_DIR = dirname(fileURLToPath(import.meta.url));
const DIST = join(APP_DIR, 'dist', 'esm');

const MIME: Record<string, string> = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.map': 'application/json; charset=utf-8',
};

/** Markers the lobby MUST render once the graph loads and `mount` runs (relay offline is fine). */
const REQUIRED_MARKERS = ['Lobby', 'REGTEST — play money', 'Create a table', 'Wallet balance', 'Open tables'];

function findBrowser(): string | null {
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

async function main(): Promise<void> {
  if (!existsSync(join(DIST, 'index.html'))) {
    console.error('verify-render: dist/esm/index.html missing — run build.ts first.');
    process.exit(1);
  }
  const browser = findBrowser();
  if (!browser) {
    console.log('verify-render: SKIP — no headless Chrome/Edge found (set BSV_CHROME to force).');
    process.exit(0);
  }

  const server = createServer((req, res) => {
    const url = (req.url ?? '/').split('?')[0] ?? '/';
    const rel = url === '/' ? 'index.html' : decodeURIComponent(url).replace(/^\/+/, '');
    const path = normalize(join(DIST, rel));
    if (!path.startsWith(DIST)) { res.writeHead(403).end('forbidden'); return; }
    readFile(path).then(
      (buf) => { res.writeHead(200, { 'content-type': MIME[extname(path)] ?? 'application/octet-stream' }).end(buf); },
      () => { res.writeHead(404).end('not found'); },
    );
  });

  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const port = (server.address() as AddressInfo).port;
  const url = `http://127.0.0.1:${port}/`;
  console.log(`verify-render: ${browser}\nverify-render: loading ${url}`);

  // An isolated, throwaway profile + disabled background services keeps headless Chrome/Edge from
  // hanging on sync/gcm/telemetry before it dumps the DOM (observed in practice). `--dump-dom` fires
  // at the load event and exits; the app's `mount` renders synchronously, so the lobby DOM is present
  // by then (no virtual-time clock needed).
  //
  // We use async `spawn` (NOT spawnSync) and drain stdout as it arrives: the dumped DOM is large
  // (many inline styles) and would overflow the OS pipe buffer that spawnSync only reads AFTER the
  // child exits — a classic deadlock. stderr is discarded (Chrome spams gcm/usb telemetry).
  const profile = await mkdtemp(join(tmpdir(), 'bsv-render-'));
  const dom = await new Promise<string>((resolve, reject) => {
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
    const timer = setTimeout(() => { child.kill('SIGKILL'); reject(new Error('headless render timed out (90s)')); }, 90_000);
    child.on('error', (e) => { clearTimeout(timer); reject(e); });
    child.on('close', () => { clearTimeout(timer); resolve(Buffer.concat(chunks).toString('utf8')); });
  }).catch((e: unknown) => { console.error(`verify-render: ${(e as Error).message}`); return ''; });
  server.close();
  await rm(profile, { recursive: true, force: true }).catch(() => {});

  if (!dom) {
    console.error('verify-render: FAILED — no DOM captured from headless browser.');
    process.exit(1);
  }
  const missing = REQUIRED_MARKERS.filter((m) => !dom.includes(m));
  if (missing.length > 0) {
    console.error(`verify-render: FAILED — DOM missing markers: ${missing.join(', ')}`);
    console.error('--- first 2000 chars of dumped DOM ---');
    console.error(dom.slice(0, 2000));
    process.exit(1);
  }
  console.log(`verify-render: OK — lobby rendered (all ${REQUIRED_MARKERS.length} markers present, ${dom.length} chars of DOM).`);
  process.exit(0);
}

void main();
