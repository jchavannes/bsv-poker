import { test } from 'node:test';
import assert from 'node:assert/strict';
import { seatedForNextHand, freezeParticipants, applyBetweenHands } from '../src/table-participants.ts';

const seats = [{ seat: 0 }, { seat: 1 }, { seat: 2 }];

test('seated set for the next hand drops busted (zero-stack) seats (REQ-CRYPTO-011)', () => {
  const stacks = new Map([[0, 100], [1, 0], [2, 50]]);
  const next = seatedForNextHand(seats, (s) => stacks.get(s) ?? 0);
  assert.deepEqual(next.map((s) => s.seat), [0, 2]);
});

test('a frozen participant set is immutable for the duration of the hand', () => {
  const frozen = freezeParticipants(seatedForNextHand(seats, () => 100));
  assert.equal(frozen.length, 3);
  assert.throws(() => (frozen as { seat: number }[]).push({ seat: 9 }), TypeError);
});

test('sit-out/join take effect only between hands (applyBetweenHands)', () => {
  const make = (seat: number) => ({ seat });
  const afterSitOut = applyBetweenHands(seats, [{ kind: 'sit-out', seat: 1 }], make);
  assert.deepEqual(afterSitOut.map((s) => s.seat), [0, 2]);
  const afterJoin = applyBetweenHands(afterSitOut, [{ kind: 'join', seat: 5 }], make);
  assert.deepEqual(afterJoin.map((s) => s.seat), [0, 2, 5]);
});
