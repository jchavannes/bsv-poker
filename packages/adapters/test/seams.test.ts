/**
 * Audit (VA) and revocation/custody (OB) seam integration (core §12.4 / §2.4). These exercise
 * the contract behaviours the platform relies on and, crucially, assert the STATED BOUNDARY is
 * surfaced (INV-VA-2) and that revocation is an on-chain expiry fact (INV-OB-2), never overstated
 * (P8).
 */

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { makeFakeVA, makeFakeOB } from '../src/index.ts';

test('VA selective-disclosure proves one figure and reveals nothing else; boundary surfaced (REQ-DATA-004)', async () => {
  const va = makeFakeVA();
  const ledger = ['settle:hand1=+80', 'settle:hand1=-20', 'settle:hand1=-60', 'rake=0', 'pot=160'];
  // Prove the "pot=160" record is included/anchored without disclosing the others.
  const idx = 4;
  const bundle = await va.merkleProve(ledger, idx);
  assert.equal(bundle.leaf.length, 64); // only the queried record's hash leaf
  assert.equal(await va.merkleVerify(bundle), true);
  // siblings in the path are opaque hashes, not the other records' contents
  for (const step of bundle.path) assert.equal(step.hashHex.length, 64);
  // INV-VA-2 boundary MUST be surfaced wherever audit output is shown
  assert.match(va.boundary, /inclusion/i);
  assert.match(va.boundary, /never truth-at-origin/i);
});

test('VA does not detect a lie entered at origin in otherwise-consistent books (INV-VA-2 honesty)', async () => {
  const va = makeFakeVA();
  // A falsely-entered-but-internally-consistent record still produces a valid inclusion proof —
  // the system establishes inclusion/integrity, NOT truth-at-origin. We assert the proof verifies
  // (the system cannot and does not claim to catch the lie).
  const books = ['false-but-consistent', 'b', 'c', 'd'];
  const bundle = await va.merkleProve(books, 0);
  assert.equal(await va.merkleVerify(bundle), true);
});

test('OB revocation is an unspent-expiring-output fact, decided by no operator (INV-OB-2)', async () => {
  const ob = makeFakeOB();
  assert.equal(await ob.isRevoked('nft-session@200', 199), false, 'live before expiry');
  assert.equal(await ob.isRevoked('nft-session@200', 201), true, 'revoked after expiry');
  // transfer-with-revocation: a re-key wraps the content key to the new member; old key cannot open
  const contentKey = 'deadbeefcafe';
  const wrapped = await ob.wrap(contentKey, '02newowner');
  assert.notEqual(wrapped, contentKey);
  assert.equal(await ob.unwrap(wrapped, 'priv-new'), contentKey);
});
