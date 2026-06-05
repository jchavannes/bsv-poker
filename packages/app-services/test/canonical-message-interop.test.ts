/**
 * Cross-language canonical-message interop vectors (audit 3 / audit 7).
 *
 * WHY THIS EXISTS: the TypeScript client SIGNS `envelopeMessage(...)` and the Go indexer
 * (indexer-go/validate.go `canonicalMessage`) RE-DERIVES the same bytes to verify the Ed25519
 * signature. If the two ever drift, every real signature fails and the validated transcript is empty
 * — a regression that unit tests on each side miss because each only checks itself. These vectors are
 * the SHARED fixture: the EXACT same literal strings are asserted here (TS) and in
 * `indexer-go/indexer/validate_test.go` (`TestCanonicalMessageInteropVector`, Go). Change one side's
 * field order/defaults without the other and one of these two tests fails in CI. The live
 * `tools/validating-indexer-e2e.ts` is the integration backstop; this is the cheap unit guard.
 *
 * If you change `envelopeMessage`, you MUST update BOTH this file and the Go vector in lockstep.
 */

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { envelopeMessage } from '../src/session-auth.ts';

test('canonical message: commit (no d/h/subject → 0,0,-1) matches the Go vector', () => {
  const got = envelopeMessage('t1', { t: 'commit', seat: 0, hand: 0, c: 'ab' });
  assert.equal(got, '["t1","commit",0,0,"",0,"ab","",[],"",0,0,-1]');
});

test('canonical message: action with amount/discard/prev/h matches the Go vector', () => {
  const got = envelopeMessage('tbl', { t: 'action', seat: 2, hand: 1, kind: 'bet', amount: 50, discard: [1, 3], prev: 'ff', h: 7 });
  assert.equal(got, '["tbl","action",2,1,"bet",50,"","",[1,3],"ff",0,7,-1]');
});

test('canonical message: timeout-claim fields (d + subject present) match the Go vector', () => {
  const got = envelopeMessage('tbl', { t: 'action', seat: 1, hand: 0, d: 9, subject: 2 });
  assert.equal(got, '["tbl","action",1,0,"",0,"","",[],"",9,0,2]');
});

test('canonical message: subject present as 0 stays 0 (not coerced to the -1 default)', () => {
  const got = envelopeMessage('tbl', { t: 'timeout-claim' as unknown as string, seat: 1, hand: 0, d: 4, subject: 0 });
  assert.equal(got, '["tbl","timeout-claim",1,0,"",0,"","",[],"",4,0,0]');
});
