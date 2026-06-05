import { test } from 'node:test';
import assert from 'node:assert/strict';
import { validateEnvelope, parseAndValidate } from '../src/message-validation.ts';

test('valid commit/reveal/action envelopes are accepted (REQ-APP-103)', () => {
  assert.ok(validateEnvelope({ t: 'commit', seat: 0, hand: 1, c: 'deadbeef' }));
  assert.ok(validateEnvelope({ t: 'reveal', seat: 2, hand: 1, r: 'cafe' }));
  assert.ok(validateEnvelope({ t: 'action', seat: 1, hand: 3, kind: 'raise', amount: 200 }));
  assert.ok(validateEnvelope({ t: 'action', seat: 1, hand: 3, kind: 'check' }));
});

test('unrecognized or malformed envelopes are REJECTED (never partially trusted)', () => {
  assert.equal(validateEnvelope({ t: 'evil', seat: 0, hand: 1 }), null, 'unknown kind');
  assert.equal(validateEnvelope({ t: 'commit', seat: -1, hand: 1, c: 'ab' }), null, 'bad seat');
  assert.equal(validateEnvelope({ t: 'commit', seat: 0, hand: 1.5, c: 'ab' }), null, 'non-integer hand');
  assert.equal(validateEnvelope({ t: 'commit', seat: 0, hand: 1 }), null, 'commit missing c');
  assert.equal(validateEnvelope({ t: 'reveal', seat: 0, hand: 1, r: 'nothex!' }), null, 'reveal non-hex');
  assert.equal(validateEnvelope({ t: 'action', seat: 0, hand: 1 }), null, 'action missing kind');
  assert.equal(validateEnvelope({ t: 'action', seat: 0, hand: 1, kind: 'raise', amount: Infinity }), null, 'non-finite amount');
  assert.equal(validateEnvelope(null), null);
  assert.equal(validateEnvelope('a string'), null);
});

test('timeout-claim envelopes: accepted only with a valid distinct subject + height (audit 3)', () => {
  const ok = validateEnvelope({ t: 'timeout-claim', seat: 1, hand: 2, subject: 0, d: 42 });
  assert.ok(ok);
  assert.equal(ok!.subject, 0);
  assert.equal(ok!.d, 42);
  // A claim must name a SUBJECT distinct from the signing claimant.
  assert.equal(validateEnvelope({ t: 'timeout-claim', seat: 1, hand: 2, subject: 1, d: 42 }), null, 'self-claim rejected');
  assert.equal(validateEnvelope({ t: 'timeout-claim', seat: 1, hand: 2, d: 42 }), null, 'missing subject');
  assert.equal(validateEnvelope({ t: 'timeout-claim', seat: 1, hand: 2, subject: 0 }), null, 'missing deadline');
  assert.equal(validateEnvelope({ t: 'timeout-claim', seat: 1, hand: 2, subject: 0, d: -1 }), null, 'negative deadline');
  assert.equal(validateEnvelope({ t: 'timeout-claim', seat: 1, hand: 2, subject: 0, d: 1.5 }), null, 'non-integer deadline');
  assert.equal(validateEnvelope({ t: 'timeout-claim', seat: 1, hand: 2, subject: -1, d: 1 }), null, 'bad subject');
});

test('reveal/action carry an optional anchored height h; a bad h is rejected (audit 3)', () => {
  assert.equal(validateEnvelope({ t: 'reveal', seat: 0, hand: 1, r: 'ab', h: 7 })?.h, 7);
  assert.equal(validateEnvelope({ t: 'action', seat: 0, hand: 1, kind: 'check', h: 9 })?.h, 9);
  assert.equal(validateEnvelope({ t: 'action', seat: 0, hand: 1, kind: 'check', h: -1 }), null, 'negative h');
  assert.equal(validateEnvelope({ t: 'reveal', seat: 0, hand: 1, r: 'ab', h: 2.5 }), null, 'non-integer h');
});

test('parseAndValidate rejects bad JSON and bad envelopes from the wire', () => {
  assert.equal(parseAndValidate('{not json'), null);
  assert.equal(parseAndValidate(JSON.stringify({ t: 'action', seat: 0, hand: 0, kind: 'fold' }))?.kind, 'fold');
  assert.equal(parseAndValidate(JSON.stringify({ t: 'spoof' })), null);
});
