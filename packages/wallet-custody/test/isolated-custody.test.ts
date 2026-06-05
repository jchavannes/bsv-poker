/**
 * Isolated custody boundary (audit #17 / AD-OPEN-3): the master key lives in a SEPARATE CHILD PROCESS,
 * not the host's address space, so a host memory dump cannot recover it. Proves: (1) the worker is a
 * distinct process; (2) the host handle exposes no key material; (3) the host seed buffer is zeroized
 * after init; (4) the worker signs/derives CORRECTLY with the isolated key (derive matches software
 * custody; the isolated signature validates through the real interpreter); (5) dispose ends it.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createIsolatedCustody, createSoftwareCustody } from '../src/index.ts';
import { evaluate, foldLocking, foldUnlocking } from '@bsv-poker/script-templates-ts';
import type { BranchBinding } from '@bsv-poker/protocol-types';

const BIND: BranchBinding = { gid: 'ab'.repeat(8), rulesetHash: 'cd'.repeat(32), round: 0, stateHash: 'ef'.repeat(32), actingSeat: 0, successorCommitment: '01'.repeat(32) };
const seed32 = (): Uint8Array => Uint8Array.from(Array.from({ length: 32 }, (_, i) => (i * 7 + 3) % 251 || 1));

test('the master key lives in a separate process; the host handle holds no key (audit #17)', async () => {
  const iso = await createIsolatedCustody(seed32());
  try {
    assert.equal(typeof iso.workerPid, 'number');
    assert.notEqual(iso.workerPid, process.pid, 'the worker must be a SEPARATE process (own address space)');
    // The host object exposes only the boundary API — no seed/master/scalar field.
    assert.deepEqual(Object.keys(iso).sort(), ['derive', 'dispose', 'sign', 'workerPid']);
  } finally {
    iso.dispose();
  }
});

test('createIsolatedCustody zeroizes the host seed buffer after delivery (forward secrecy)', async () => {
  const seed = seed32();
  const iso = await createIsolatedCustody(seed);
  try {
    assert.ok(seed.every((b) => b === 0), 'the host-side seed must be wiped once delivered to the worker');
  } finally {
    iso.dispose();
  }
});

test('the isolated worker derives + signs CORRECTLY with the key it alone holds (audit #17)', async () => {
  const masterForCompare = seed32(); // software custody over the SAME key, for the equivalence check
  const soft = createSoftwareCustody(masterForCompare);
  const iso = await createIsolatedCustody(seed32());
  try {
    // derive is deterministic → the worker's pubkey matches software custody's (same master key).
    for (const [g, j, r] of [['g', 3, 'sign'], ['g2', 0, 'sign']] as const) {
      assert.equal(await iso.derive(g, j, r), soft.derive(g, j, r), `derive(${g},${j},${r}) must match`);
    }
    // The isolated signature validates through the REAL interpreter against the derived pubkey.
    const pubHex = await iso.derive('g', 3, 'sign');
    const pub = Uint8Array.from(Buffer.from(pubHex, 'hex'));
    const preimage = Uint8Array.from([1, 2, 3, 4, 5]);
    const sig = await iso.sign('g', 3, 'sign', { sighashPreimage: preimage, describe: { action: 'fold' } });
    assert.equal(evaluate(foldUnlocking(sig), foldLocking(BIND, pub), { sighashPreimage: preimage }).ok, true, 'isolated signature must validate');
  } finally {
    iso.dispose();
  }
});

test('after dispose the boundary refuses further signing', async () => {
  const iso = await createIsolatedCustody(seed32());
  iso.dispose();
  await assert.rejects(iso.sign('g', 0, 'sign', { sighashPreimage: Uint8Array.of(1), describe: { action: 'fold' } }), /disposed/);
});
