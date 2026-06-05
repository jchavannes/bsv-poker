/**
 * Canonical transaction-graph validation (audit #31): a funding→settlement DAG validates, and the
 * consensus structural invariants are enforced — no double-spend, no value creation, no spending of a
 * missing parent. Built from real serialized transactions via the project's own parser.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { TransactionGraph } from '../src/transaction-graph.ts';
import { p2pkhScript } from '../src/regtest-node.ts';
import { genKeyPair, type Script } from '@bsv-poker/script-templates-ts';
import { type Tx, serializeTxWire, txidWire } from '@bsv-poker/tx-builder';
import { bytesToHex } from '@bsv-poker/protocol-types';

const EMPTY_SS: Script = []; // unlocking script content is irrelevant to graph validation
const pay = (): Script => p2pkhScript(genKeyPair().pubCompressed);
const raw = (tx: Tx, ss: Script[]): string => bytesToHex(serializeTxWire(tx, ss));

const ROOT_TXID = 'ab'.repeat(32);

/** A graph seeded with a single root coinbase of `value` sats at ROOT_TXID:0. */
function seeded(value = 1000): TransactionGraph {
  const g = new TransactionGraph();
  g.addRoot(ROOT_TXID, 0, BigInt(value)); // root script is irrelevant to graph validation
  return g;
}

test('a funding -> settlement DAG validates and tracks the UTXO set (audit #31)', () => {
  const g = seeded(1000);
  // Funding spends the root coinbase → a 900-sat funding output (fee 100).
  const funding: Tx = { version: 1, inputs: [{ prevTxid: ROOT_TXID, vout: 0, sequence: 0xffffffff }], outputs: [{ satoshis: 900, locking: pay() }], nLockTime: 0 };
  const fundTxid = txidWire(funding, [EMPTY_SS]);
  const rf = g.add(raw(funding, [EMPTY_SS]));
  assert.equal(rf.ok, true, `funding must validate: ${rf.ok ? '' : rf.reason}`);
  assert.equal(rf.ok && rf.txid, fundTxid, 'graph txid must match txidWire');
  assert.equal(rf.ok && rf.fee, 100n);

  // Settlement spends the funding output → two payouts summing 880 (fee 20).
  const settle: Tx = { version: 1, inputs: [{ prevTxid: fundTxid, vout: 0, sequence: 0xffffffff }], outputs: [{ satoshis: 500, locking: pay() }, { satoshis: 380, locking: pay() }], nLockTime: 0 };
  const setTxid = txidWire(settle, [EMPTY_SS]);
  const rs = g.add(raw(settle, [EMPTY_SS]));
  assert.equal(rs.ok, true, `settlement must validate: ${rs.ok ? '' : rs.reason}`);

  // The funding output is now spent; the two settlement outputs are the UTXO set.
  assert.equal(g.isUnspent(fundTxid, 0), false, 'the funding output must be spent');
  assert.equal(g.isUnspent(setTxid, 0), true);
  assert.equal(g.isUnspent(setTxid, 1), true);
  assert.equal(g.size(), 2);
  assert.equal(g.utxos().reduce((s, o) => s + o.satoshis, 0n), 880n, 'UTXO set value = the settlement payouts');
});

test('a DOUBLE-SPEND of the same output is rejected (audit #31)', () => {
  const g = seeded(1000);
  const funding: Tx = { version: 1, inputs: [{ prevTxid: ROOT_TXID, vout: 0, sequence: 0xffffffff }], outputs: [{ satoshis: 900, locking: pay() }], nLockTime: 0 };
  const fundTxid = txidWire(funding, [EMPTY_SS]);
  assert.equal(g.add(raw(funding, [EMPTY_SS])).ok, true);
  // Two settlements both spending fundTxid:0 — the second is a double-spend.
  const s1: Tx = { version: 1, inputs: [{ prevTxid: fundTxid, vout: 0, sequence: 0xffffffff }], outputs: [{ satoshis: 800, locking: pay() }], nLockTime: 0 };
  const s2: Tx = { version: 1, inputs: [{ prevTxid: fundTxid, vout: 0, sequence: 0xffffffff }], outputs: [{ satoshis: 700, locking: pay() }], nLockTime: 0 };
  assert.equal(g.add(raw(s1, [EMPTY_SS])).ok, true);
  const r2 = g.add(raw(s2, [EMPTY_SS]));
  assert.equal(r2.ok, false);
  assert.match(r2.ok ? '' : r2.reason, /double-spend/);
});

test('VALUE CREATION (outputs exceed inputs) is rejected (audit #31)', () => {
  const g = seeded(1000);
  const inflate: Tx = { version: 1, inputs: [{ prevTxid: ROOT_TXID, vout: 0, sequence: 0xffffffff }], outputs: [{ satoshis: 2000, locking: pay() }], nLockTime: 0 };
  const r = g.add(raw(inflate, [EMPTY_SS]));
  assert.equal(r.ok, false);
  assert.match(r.ok ? '' : r.reason, /value creation/);
});

test('spending a MISSING parent output is rejected (audit #31)', () => {
  const g = seeded(1000);
  const orphan: Tx = { version: 1, inputs: [{ prevTxid: 'cd'.repeat(32), vout: 7, sequence: 0xffffffff }], outputs: [{ satoshis: 10, locking: pay() }], nLockTime: 0 };
  const r = g.add(raw(orphan, [EMPTY_SS]));
  assert.equal(r.ok, false);
  assert.match(r.ok ? '' : r.reason, /missing parent|no producing output/);
});
