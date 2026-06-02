import { test } from 'node:test';
import assert from 'node:assert/strict';
import { validateTableCreate, rulesetFromForm, type TableCreateForm } from '../src/view-models/lobby.ts';

const good: TableCreateForm = { smallBlind: 1, bigBlind: 2, startingStack: 200, decisionMs: 30000 };

test('table-create form: a valid form passes (REQ-APP-052 component test obligation)', () => {
  const v = validateTableCreate(good);
  assert.equal(v.ok, true);
  assert.deepEqual(v.errors, []);
});

test('table-create form: each invalid field surfaces a clear error', () => {
  assert.match(validateTableCreate({ ...good, smallBlind: 0 }).errors.join(' '), /Small blind must be positive/);
  assert.match(validateTableCreate({ ...good, bigBlind: 1 }).errors.join(' '), /Big blind must exceed/);
  assert.match(validateTableCreate({ ...good, startingStack: 3 }).errors.join(' '), /at least two big blinds/);
  assert.match(validateTableCreate({ ...good, decisionMs: 500 }).errors.join(' '), /at least 1s/);
});

test('rulesetFromForm assembles a regtest play-money NL Hold\'em ruleset from the inputs', () => {
  const r = rulesetFromForm(good);
  assert.equal(r.variant, 'holdem');
  assert.equal(r.bettingStructure, 'NL');
  assert.equal(r.currency, 'play-regtest');
  assert.equal(r.signingMode, 'A');
  assert.deepEqual(r.blinds, { smallBlind: 1, bigBlind: 2, ante: 0, bringIn: 0 });
  assert.equal(r.minBuyIn, 200);
  assert.equal(r.timeouts.decisionMs, 30000);
  assert.equal(r.timeouts.recoveryMs, 120000, 'recovery is 4× decision');
});
