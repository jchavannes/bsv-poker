import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createSessionAuth, verifySig, envelopeMessage } from '../src/session-auth.ts';
import { validateEnvelope } from '../src/message-validation.ts';

const TABLE = 'tbl-abc';

test('a seat-signed envelope verifies against the seat key (audit 1–3)', async () => {
  const seat0 = await createSessionAuth();
  const msg = envelopeMessage(TABLE, { t: 'action', seat: 0, hand: 0, kind: 'raise', amount: 50 });
  const sig = await seat0.sign(msg);
  assert.equal(await verifySig(seat0.pub, msg, sig), true);
});

test('FORGERY rejected: an action for seat 1 signed by an attacker is NOT valid under seat 1’s key', async () => {
  const seat1 = await createSessionAuth(); // the real player at seat 1
  const attacker = await createSessionAuth(); // anyone else on the relay
  const msg = envelopeMessage(TABLE, { t: 'action', seat: 1, hand: 0, kind: 'fold', amount: 0 });
  const forgedSig = await attacker.sign(msg);
  // The honest client verifies against the seat's REGISTERED key (seat1.pub) — the forgery fails.
  assert.equal(await verifySig(seat1.pub, msg, forgedSig), false);
});

test('UNSIGNED forged fold (the audit exploit) has no valid signature for the seat', async () => {
  const seat1 = await createSessionAuth();
  // {"t":"action","seat":1,"hand":0,"kind":"fold","amount":0} with no/garbage sig
  const raw = { t: 'action', seat: 1, hand: 0, kind: 'fold', amount: 0 } as const;
  assert.ok(validateEnvelope(raw), 'structurally valid…');
  assert.equal(await verifySig(seat1.pub, envelopeMessage(TABLE, raw), 'deadbeef'), false, '…but unsigned → rejected');
});

test('a signature does not replay across table / hand / seat (binding)', async () => {
  const k = await createSessionAuth();
  const sig = await k.sign(envelopeMessage(TABLE, { t: 'action', seat: 0, hand: 0, kind: 'check', amount: 0 }));
  assert.equal(await verifySig(k.pub, envelopeMessage('other-table', { t: 'action', seat: 0, hand: 0, kind: 'check', amount: 0 }), sig), false);
  assert.equal(await verifySig(k.pub, envelopeMessage(TABLE, { t: 'action', seat: 0, hand: 1, kind: 'check', amount: 0 }), sig), false);
  assert.equal(await verifySig(k.pub, envelopeMessage(TABLE, { t: 'action', seat: 2, hand: 0, kind: 'check', amount: 0 }), sig), false);
});
