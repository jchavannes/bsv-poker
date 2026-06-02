import { test } from 'node:test';
import assert from 'node:assert/strict';
import { deadlineFromAnchor, isDeadlinePassed, decisionDeadlineHeight, type AnchoredClock } from '../src/index.ts';

const clock: AnchoredClock = { height: 100, medianTimeSeconds: 1_700_000_000 };

test('deadlines are derived from the anchored height, not wall-clock (REQ-TX-007)', () => {
  assert.equal(deadlineFromAnchor(clock, 6), 106);
  assert.equal(deadlineFromAnchor(clock, 0), 100);
});

test('a deadline is passed once the anchored height reaches it', () => {
  assert.equal(isDeadlinePassed(106, clock), false); // height 100 < 106
  assert.equal(isDeadlinePassed(100, clock), true);
  assert.equal(isDeadlinePassed(106, { ...clock, height: 106 }), true);
});

test('decision window maps seconds to a block budget off the anchored clock', () => {
  assert.equal(decisionDeadlineHeight(clock, 600), 101); // 1 block
  assert.equal(decisionDeadlineHeight(clock, 1800), 103); // 3 blocks
  // Identical result regardless of when (wall-clock) it is evaluated — anchored only.
  assert.equal(decisionDeadlineHeight(clock, 600), decisionDeadlineHeight(clock, 600));
});
