/**
 * Architectural-invariant test (core REQ-APP-012 / REQ-DEP-001): no application package may import a
 * dependency repo directly — all access goes through the adapter layer. Only `@bsv-poker/adapters`
 * is permitted to name a dependency repo (it is the boundary). This enforces, as a passing test,
 * that a dependency-repo API change is absorbed in its adapter and never propagates into
 * engine/FSMs/UI (REQ-DEP-002).
 */

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const PACKAGES = join(dirname(fileURLToPath(import.meta.url)), '..', '..'); // .../packages
// Names of the external dependency repos that only the adapter layer may reference.
const DEP_REPO_TOKENS = [/@vaa\//, /overlay-broadcast/, /verifiable-accounting/, /bonded-subsat/];

function tsFiles(dir: string, out: string[] = []): string[] {
  for (const e of readdirSync(dir)) {
    if (e === 'node_modules' || e === 'dist' || e === 'test') continue;
    const p = join(dir, e);
    if (statSync(p).isDirectory()) tsFiles(p, out);
    else if (e.endsWith('.ts')) out.push(p);
  }
  return out;
}

test('no application package imports a dependency repo directly (only adapters may) — REQ-APP-012', () => {
  const violations: string[] = [];
  for (const pkg of readdirSync(PACKAGES)) {
    if (pkg === 'adapters') continue; // the boundary itself is allowed to reference dep repos
    const src = join(PACKAGES, pkg, 'src');
    let files: string[];
    try { files = tsFiles(src); } catch { continue; }
    for (const f of files) {
      const text = readFileSync(f, 'utf8');
      for (const line of text.split('\n')) {
        if (!/\b(import|from|require)\b/.test(line)) continue;
        for (const tok of DEP_REPO_TOKENS) {
          if (tok.test(line)) violations.push(`${pkg}: ${f.slice(PACKAGES.length + 1)} → ${line.trim()}`);
        }
      }
    }
  }
  assert.deepEqual(violations, [], `dependency-repo imports must go through @bsv-poker/adapters:\n${violations.join('\n')}`);
});

test('the adapter layer IS where dependency repos are referenced (boundary exists)', () => {
  const adapters = join(PACKAGES, 'adapters', 'src');
  const text = tsFiles(adapters).map((f) => readFileSync(f, 'utf8')).join('\n');
  assert.ok(DEP_REPO_TOKENS.some((t) => t.test(text)), 'adapters reference the real dependency repos (real-va/real-ob/real-node)');
});
