/**
 * Render verification (REQ-BUILD-003 / standalone mandate) — proves the built web client actually
 * RENDERS in a real browser DOM, not merely that a process stays alive.
 *
 * Serves dist/esm, loads index.html in headless Chrome/Edge (shared harness in headless.ts), dumps
 * the post-script DOM, and asserts the lobby's landmark text is present. Any module-resolution
 * failure (the import-map-resolved, bundler-free graph), runtime throw, or empty #root fails it. The
 * relay is intentionally NOT running, so the lobby renders its static structure plus a "failed to
 * load tables" notice — which still proves the whole graph executed and mounted.
 *
 * WHY headless-DOM (not jsdom): jsdom is an external dependency AND not a real browser; the bar is a
 * REAL render. Exits 0 on success; 0 with SKIP if no headless browser is found; 1 if markers absent.
 */
import { existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { findBrowser, dumpDom } from './headless.ts';

const DIST = join(dirname(fileURLToPath(import.meta.url)), 'dist', 'esm');
const REQUIRED_MARKERS = ['Lobby', 'REGTEST — play money', 'Create a table', 'Wallet balance', 'Open tables'];

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
  console.log(`verify-render: ${browser}`);
  const dom = await dumpDom(browser, DIST, 'index.html').catch((e: unknown) => {
    console.error(`verify-render: ${(e as Error).message}`);
    return '';
  });
  if (!dom) {
    console.error('verify-render: FAILED — no DOM captured from headless browser.');
    process.exit(1);
  }
  const missing = REQUIRED_MARKERS.filter((m) => !dom.includes(m));
  if (missing.length > 0) {
    console.error(`verify-render: FAILED — DOM missing markers: ${missing.join(', ')}`);
    console.error(`--- first 2000 chars of dumped DOM ---\n${dom.slice(0, 2000)}`);
    process.exit(1);
  }
  console.log(`verify-render: OK — lobby rendered (all ${REQUIRED_MARKERS.length} markers present, ${dom.length} chars of DOM).`);
  process.exit(0);
}

void main();
