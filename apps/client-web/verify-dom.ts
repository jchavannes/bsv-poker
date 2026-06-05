/**
 * Browser-executed tests for the in-tree DOM view core (packages/ui-core/src/dom.ts). dom.ts replaces
 * React + react-dom; its security-relevant claims must be tested in a REAL browser DOM (jsdom is an
 * external dependency and not a real browser). This loads the BUILT dom.js (from dist/esm, via the
 * same headless harness as verify-render) and runs assertions IN the page, then checks the result.
 *
 * Claims under test (each a positive assertion; #4 is the security claim with a negative):
 *   1. el() sets class/id/attributes and appends string children as text.
 *   2. el() applies a style object and value/checked/disabled as PROPERTIES.
 *   3. on<Event> handlers attach and fire (real addEventListener).
 *   4. SECURITY (no XSS via the view layer): string children become text nodes via textContent — a
 *      string that looks like markup does NOT create elements (no innerHTML anywhere).
 *   5. mount() re-renders the subtree on store change AND preserves input focus + caret selection
 *      (the property that makes controlled form fields usable without a virtual DOM).
 *
 * Skips (exit 0) when no headless browser is found; fails (exit 1) if any assertion fails.
 * Run: `node verify-dom.ts` (from apps/client-web; requires the web build).
 */
import { writeFile, rm } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { findBrowser, dumpDom } from './headless.ts';

const DIST = join(dirname(fileURLToPath(import.meta.url)), 'dist', 'esm');
const TEST_HTML = '__dom_test__.html';

/* The in-page test. Imports the built dom.js (relative to dist/esm) and reports a single result line
 * into <pre id="result"> that the harness greps. Kept as a string so no extra build entry is needed. */
const PAGE = `<!doctype html><html><head><meta charset="utf-8"></head>
<body><div id="app"></div><pre id="result">DOM-TESTS: PENDING</pre>
<script type="module">
import { el, text, mount, replaceChildren } from './packages/ui-core/src/dom.js';
const results = [];
const ok = (name, cond) => results.push({ name, ok: !!cond });

// 1. basics: class/id/attribute + string child as text
const d = el('div', { class: 'card', id: 'd1', 'data-x': 'y' }, 'hello');
ok('tag', d.tagName === 'DIV');
ok('class', d.className === 'card');
ok('id', d.id === 'd1');
ok('attr', d.getAttribute('data-x') === 'y');
ok('text-child', d.textContent === 'hello' && d.childNodes.length === 1 && d.firstChild.nodeType === 3);

// 2. style object + value/checked/disabled as properties
const inp = el('input', { type: 'checkbox', checked: true, disabled: true, style: { color: 'rgb(1, 2, 3)' } });
ok('checked-prop', inp.checked === true);
ok('disabled-prop', inp.disabled === true);
const styled = el('div', { style: { color: 'rgb(1, 2, 3)' } });
ok('style-object', styled.style.color === 'rgb(1, 2, 3)');

// 3. event handler fires
let clicks = 0;
const btn = el('button', { onClick: () => { clicks++; } }, 'go');
btn.dispatchEvent(new MouseEvent('click'));
ok('event-handler', clicks === 1);

// 4. SECURITY: string that looks like markup must NOT become elements
const xss = el('div', {}, '<img src=x onerror="window.__pwned=1"><b>nope</b>');
ok('no-xss-img', xss.querySelector('img') === null);
ok('no-xss-b', xss.querySelector('b') === null);
ok('xss-as-text', xss.textContent.indexOf('<img') === 0);
ok('no-xss-side-effect', window.__pwned === undefined);

// 5. mount() re-render + focus/caret preservation
let model = { n: 0, val: 'abcdef' };
const listeners = new Set();
const store = { subscribe(l){ listeners.add(l); return () => listeners.delete(l); } };
const notify = () => { for (const l of [...listeners]) l(); };
const app = document.getElementById('app');
mount(app, () => el('div', {},
  el('span', { id: 'count' }, String(model.n)),
  el('input', { id: 'fi', value: model.val, onInput: (e) => { model.val = e.target.value; } }),
), store);
ok('mount-initial', document.getElementById('count').textContent === '0');
const fi = document.getElementById('fi');
fi.focus();
fi.setSelectionRange(2, 4);
ok('focus-before', document.activeElement === fi);
model.n = 1; notify();                         // store change -> full subtree replace
ok('mount-rerender', document.getElementById('count').textContent === '1');
const fi2 = document.getElementById('fi');
ok('focus-preserved', document.activeElement === fi2);
ok('caret-preserved', fi2.selectionStart === 2 && fi2.selectionEnd === 4);

// replaceChildren sanity
const box = el('div', {}, 'old'); replaceChildren(box, text('new'));
ok('replaceChildren', box.textContent === 'new');

const failed = results.filter(r => !r.ok);
document.getElementById('result').textContent = failed.length === 0
  ? ('DOM-TESTS: PASS (' + results.length + ' assertions)')
  : ('DOM-TESTS: FAIL: ' + failed.map(r => r.name).join(', '));
</script></body></html>`;

async function main(): Promise<void> {
  if (!existsSync(join(DIST, 'packages', 'ui-core', 'src', 'dom.js'))) {
    console.error('verify-dom: built dom.js missing — run build.ts first.');
    process.exit(1);
  }
  const browser = findBrowser();
  if (!browser) {
    console.log('verify-dom: SKIP — no headless Chrome/Edge found (set BSV_CHROME to force).');
    process.exit(0);
  }
  const htmlPath = join(DIST, TEST_HTML);
  await writeFile(htmlPath, PAGE, 'utf8');
  try {
    console.log(`verify-dom: ${browser}`);
    const dom = await dumpDom(browser, DIST, TEST_HTML);
    const m = dom.match(/DOM-TESTS: (PASS[^<]*|FAIL[^<]*)/);
    const verdict = m?.[1];
    if (!verdict) {
      console.error('verify-dom: FAILED — no result marker (page threw before reporting).');
      console.error(`--- first 1500 chars ---\n${dom.slice(0, 1500)}`);
      process.exit(1);
    }
    if (verdict.startsWith('FAIL')) {
      console.error(`verify-dom: ${verdict}`);
      process.exit(1);
    }
    console.log(`verify-dom: OK — ${verdict}`);
    process.exit(0);
  } finally {
    await rm(htmlPath, { force: true }).catch(() => {});
  }
}

void main();
