/**
 * Adversarial / fault-injection suite (core §14.6, REQ-TEST-006). Each case maps to a REQ and a
 * deterministic expected outcome: fail-closed, recover, or default. Engine/interpreter-level
 * cases run here; network-level cases (mempool eviction, conflicting broadcast) are exercised
 * against the Go services in the VM self-test.
 */

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { parseCard, type Card, type Ruleset, type Action } from '@bsv-poker/protocol-types';
import { createHoldem } from '@bsv-poker/game-holdem';
import {
  evaluate,
  genKeyPair,
  signPreimage,
  foldLocking,
  foldUnlocking,
  fairPlayLocking,
  fairPlayClaimUnlocking,
  fairPlayCommitment,
  revealOrTimeoutLocking,
  revealUnlocking,
  revealCommitment,
  revealPreimage,
} from '@bsv-poker/script-templates-ts';
import { makeRealCT } from '@bsv-poker/crypto-mentalpoker';
import type { BranchBinding } from '@bsv-poker/protocol-types';

const NL: Ruleset = {
  variant: 'holdem',
  bettingStructure: 'NL',
  forcedBetModel: 'blinds',
  seats: 2,
  blinds: { smallBlind: 1, bigBlind: 2, ante: 0, bringIn: 0 },
  minBuyIn: 100,
  maxBuyIn: 200,
  timeouts: { decisionMs: 30000, recoveryMs: 120000 },
  signingMode: 'A',
  currency: 'play-regtest',
  suitTiebreakHouseRule: false,
  hiLo: false,
};
function deck(): Card[] {
  const head = ['As', 'Ks', 'Ah', 'Kh', 'Qd', 'Jc', '9h', '4s', '3h'].map(parseCard);
  const used = new Set(head);
  const rest: Card[] = [];
  for (let c = 0; c < 52; c++) if (!used.has(c)) rest.push(c);
  return [...head, ...rest];
}
const seats = [
  { seat: 0, stack: 100 },
  { seat: 1, stack: 100 },
];
const BIND: BranchBinding = {
  gid: 'aa'.repeat(8),
  rulesetHash: 'bb'.repeat(32),
  round: 1,
  stateHash: 'cc'.repeat(32),
  actingSeat: 0,
  successorCommitment: 'dd'.repeat(32),
};
const SIGHASH = Uint8Array.from([1, 2, 3, 4, 5, 6]);
const ctx = { sighashPreimage: SIGHASH };

test('out-of-turn action is rejected (fail-closed)', () => {
  const m = createHoldem({ deck: deck() });
  const s = m.init(NL, seats);
  assert.equal(s.betting.toAct, 0);
  assert.throws(() => m.apply(s, { kind: 'check', seat: 1, amount: 0 }), /turn/);
});

test('under-min raise is rejected inside the engine (REQ-POKER-008)', () => {
  const m = createHoldem({ deck: deck() });
  let s = m.init(NL, seats);
  s = m.apply(s, { kind: 'call', seat: 0, amount: 1 }); // complete SB; BB to act, betToCall met
  // BB tries to "raise" to 3 — below the min raise-to of 4 (bet 2 + bb 2)
  assert.throws(() => m.apply(s, { kind: 'raise', seat: 1, amount: 3 }), /illegal raise/);
});

test('stale/duplicate action for a seat not on the clock is rejected', () => {
  const m = createHoldem({ deck: deck() });
  let s = m.init(NL, seats);
  s = m.apply(s, { kind: 'call', seat: 0, amount: 1 });
  // seat 0 already acted this turn; it is seat 1's turn
  assert.throws(() => m.apply(s, { kind: 'check', seat: 0, amount: 0 }), /turn/);
});

test('timeout-default applied keeps the hand progressing (no freeze, P4)', () => {
  const m = createHoldem({ deck: deck() });
  let s = m.init(NL, seats);
  // SB on the clock facing the BB → default is fold; applying it ends the hand uncontested
  const t = m.isTimeoutEligible(s, 0)!;
  assert.equal(t.defaultAction.kind, 'fold');
  s = m.apply(s, t.defaultAction);
  assert.equal(s.handComplete, true);
});

test('withheld/incorrect entropy reveal is detected by the commitment (recovery trigger, §4.1)', async () => {
  const ct = makeRealCT();
  const secret = Uint8Array.from([7, 7, 7, 7]);
  const commitment = await ct.entropyCommit(secret);
  assert.equal(await ct.entropyReveal(commitment, secret), true);
  assert.equal(await ct.entropyReveal(commitment, Uint8Array.from([0])), false); // withheld/wrong
});

test('card-substitution at reveal fails INSIDE the interpreter (REQ-CRYPTO-005)', () => {
  const reveal = genKeyPair();
  const refund = genKeyPair();
  const blind = Uint8Array.from([5, 5, 5, 5]);
  const cmt = revealCommitment(20, blind);
  const locking = revealOrTimeoutLocking(BIND, cmt, reveal.pubCompressed, refund.pubCompressed);
  // attacker substitutes a different face (21) — opening fails inside OP_EQUALVERIFY
  const bad = revealUnlocking(signPreimage(SIGHASH, reveal.priv), revealPreimage(21, blind));
  assert.equal(evaluate(bad, locking, ctx).ok, false);
});

test('fair-play violation (mismatched key) forfeits — fails INSIDE the interpreter (THR-FAIR-2)', () => {
  const honest = genKeyPair();
  const cheat = genKeyPair();
  const refund = genKeyPair();
  const locking = fairPlayLocking(BIND, fairPlayCommitment(honest.pubCompressed), refund.pubCompressed);
  const bad = fairPlayClaimUnlocking(signPreimage(SIGHASH, cheat.priv), cheat.pubCompressed);
  assert.equal(evaluate(bad, locking, ctx).ok, false);
});

test('replayed branch against a different state fails the binding (anti-replay, THR-PROTO-1)', () => {
  // A fold spend valid under one binding must not validate when the sighash (which commits to
  // the tx/outputs incl. the bound locking script) differs — signature is over the wrong message.
  const k = genKeyPair();
  const locking = foldLocking(BIND, k.pubCompressed);
  const sig = signPreimage(SIGHASH, k.priv);
  assert.equal(evaluate(foldUnlocking(sig), locking, ctx).ok, true);
  const replayCtx = { sighashPreimage: Uint8Array.from([9, 9, 9, 9]) };
  assert.equal(evaluate(foldUnlocking(sig), locking, replayCtx).ok, false);
});

test('all-in side-pot conservation across a 3-handed showdown (REQ-POKER-011)', () => {
  const head = ['Qs', 'Ks', 'As', 'Qh', 'Kh', 'Ah', '2c', '7d', '9h', 'Jc', '4s'].map(parseCard);
  const used = new Set(head);
  const rest: Card[] = [];
  for (let c = 0; c < 52; c++) if (!used.has(c)) rest.push(c);
  const m = createHoldem({ deck: [...head, ...rest], buttonIndex: 0 });
  let s = m.init(NL, [
    { seat: 0, stack: 40 },
    { seat: 1, stack: 60 },
    { seat: 2, stack: 100 },
  ]);
  s = m.apply(s, { kind: 'raise', seat: 0, amount: 40 });
  s = m.apply(s, { kind: 'raise', seat: 1, amount: 60 });
  s = m.apply(s, { kind: 'call', seat: 2, amount: 58 });
  const totalChips = s.seats.reduce((a, x) => a + x.stack, 0);
  assert.equal(totalChips, 200); // 40+60+100 conserved end-to-end
});
