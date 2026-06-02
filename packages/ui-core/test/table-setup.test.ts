import { test } from 'node:test';
import assert from 'node:assert/strict';
import { validateTableSetup, buildPracticeSeats, type TableSetupForm } from '../src/view-models/table-setup.ts';

const range = { min: 2, max: 9 };
const base: TableSetupForm = { variant: 'holdem', seats: 4, opponents: null };

test('the game does NOT start until the HUMAN chooses opponents (no device default)', () => {
  const v = validateTableSetup(base, range);
  assert.equal(v.ok, false);
  assert.match(v.errors.join(' '), /Choose your opponents/);
});

test('the human MAY choose other humans, OR bots — both are valid human choices', () => {
  assert.equal(validateTableSetup({ ...base, opponents: 'humans' }, range).ok, true);
  assert.equal(validateTableSetup({ ...base, opponents: 'bots', botCount: 3 }, range).ok, true);
});

test('bot count is human-chosen and bounded so the human always keeps their own seat', () => {
  assert.equal(validateTableSetup({ ...base, opponents: 'bots', botCount: 0 }, range).ok, false, '0 bots is not a bot game');
  assert.equal(validateTableSetup({ ...base, seats: 4, opponents: 'bots', botCount: 4 }, range).ok, false, 'bots cannot take every seat');
  assert.equal(validateTableSetup({ ...base, seats: 4, opponents: 'bots', botCount: 3 }, range).ok, true);
});

test('seat count must be a human selection within the variant range (no silent default)', () => {
  assert.equal(validateTableSetup({ ...base, seats: 1, opponents: 'humans' }, range).ok, false);
  assert.equal(validateTableSetup({ ...base, seats: 12, opponents: 'humans' }, range).ok, false);
});

test('the human always holds seat 0; bots fill the rest and never take the hero seat', () => {
  const plan = buildPracticeSeats(3, 100);
  assert.equal(plan.length, 4);
  assert.deepEqual(plan[0], { seat: 0, isHuman: true, stack: 100 });
  assert.equal(plan.filter((p) => p.isHuman).length, 1, 'exactly one human seat');
  assert.ok(plan.slice(1).every((p) => !p.isHuman), 'opponents are bots, by the human’s choice');
});
