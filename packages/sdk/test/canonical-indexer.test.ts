/**
 * Canonical validating indexer (audit #24 + #25): ONE object that validates poker legality over the
 * authenticated transcript AND maintains the canonical, validated transaction graph. Proves both in
 * one place: a legal transcript validates + an illegal one is rejected; a funding→settlement DAG
 * validates + a double-spend is rejected.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { CanonicalIndexer } from '../src/canonical-indexer.ts';
import { createGameModule, universalBot, offlineRuleset, deckFromEntropies, type TxRecord } from '@bsv-poker/app-services';
import { p2pkhScript } from '@bsv-poker/adapters/regtest-node';
import { sha256, bytesToHex } from '@bsv-poker/protocol-types';
import { genKeyPair, type Script } from '@bsv-poker/script-templates-ts';
import { type Tx, serializeTxWire, txidWire } from '@bsv-poker/tx-builder';

const RULESET = offlineRuleset('holdem', 2);
const SEATS = [{ seat: 0, stack: 100 }, { seat: 1, stack: 100 }];
const recOf = (e: object): TxRecord => ({ txid: `t${Math.random().toString(36).slice(2)}`, class: 'protocol', tableId: 't', raw: btoa(JSON.stringify(e)) });

/** A real legal heads-up transcript (commit/reveal + the engine-legal action sequence). */
function legalTranscript(): TxRecord[] {
  const e0 = new Uint8Array(randomBytes(32));
  const e1 = new Uint8Array(randomBytes(32));
  const m = createGameModule('holdem', deckFromEntropies([e0, e1]), 0);
  let st = m.init(RULESET, SEATS.map((s) => ({ seat: s.seat, stack: s.stack })));
  const envs: object[] = [
    { t: 'commit', seat: 0, hand: 0, c: bytesToHex(sha256(e0)) },
    { t: 'commit', seat: 1, hand: 0, c: bytesToHex(sha256(e1)) },
    { t: 'reveal', seat: 0, hand: 0, r: bytesToHex(e0) },
    { t: 'reveal', seat: 1, hand: 0, r: bytesToHex(e1) },
  ];
  for (let g = 0; g < 500 && !st.handComplete; g++) {
    const toAct = st.betting.toAct ?? st.drawToAct ?? null;
    if (toAct === null) break;
    const a = universalBot(m.getLegalActions(st, toAct), toAct);
    envs.push({ t: 'action', seat: toAct, hand: 0, kind: a.kind, amount: a.amount ?? 0 });
    st = m.apply(st, a);
  }
  return envs.map(recOf);
}

test('the canonical indexer validates poker LEGALITY over the transcript (audit #24)', () => {
  const ci = new CanonicalIndexer();
  const records = legalTranscript();
  assert.equal(ci.validateHand(records, RULESET, SEATS).valid, true, 'a legal transcript must validate');
  // An over-stack bet spliced in is rejected.
  const firstAction = records.findIndex((r) => JSON.parse(atob(r.raw!)).t === 'action');
  const env = JSON.parse(atob(records[firstAction]!.raw!));
  const tampered = records.map((r, i) => (i === firstAction ? recOf({ ...env, kind: 'bet', amount: 10_000_000 }) : r));
  const v = ci.validateHand(tampered, RULESET, SEATS);
  assert.equal(v.valid, false, 'an illegal action must be rejected');
  assert.match(v.reason ?? '', /illegal/);
});

test('the canonical indexer maintains a validated transaction GRAPH (audit #25)', () => {
  const ci = new CanonicalIndexer();
  const pay = (): Script => p2pkhScript(genKeyPair().pubCompressed);
  const ss: Script = [];
  const ROOT = 'ab'.repeat(32);
  ci.addRoot(ROOT, 0, 1000n);
  const funding: Tx = { version: 1, inputs: [{ prevTxid: ROOT, vout: 0, sequence: 0xffffffff }], outputs: [{ satoshis: 900, locking: pay() }], nLockTime: 0 };
  const fundTxid = txidWire(funding, [ss]);
  assert.equal(ci.addTransaction(bytesToHex(serializeTxWire(funding, [ss]))).ok, true, 'funding accepted into the graph');
  const settle: Tx = { version: 1, inputs: [{ prevTxid: fundTxid, vout: 0, sequence: 0xffffffff }], outputs: [{ satoshis: 500, locking: pay() }, { satoshis: 380, locking: pay() }], nLockTime: 0 };
  assert.equal(ci.addTransaction(bytesToHex(serializeTxWire(settle, [ss]))).ok, true, 'settlement accepted');
  assert.equal(ci.isUnspent(fundTxid, 0), false, 'funding output spent in the canonical graph');
  assert.equal(ci.utxos().reduce((s, o) => s + o.satoshis, 0n), 880n, 'canonical UTXO set value');
  // A double-spend of the funding output is rejected.
  const ds: Tx = { version: 1, inputs: [{ prevTxid: fundTxid, vout: 0, sequence: 0xffffffff }], outputs: [{ satoshis: 100, locking: pay() }], nLockTime: 0 };
  const r = ci.addTransaction(bytesToHex(serializeTxWire(ds, [ss])));
  assert.equal(r.ok, false);
  assert.match(r.ok ? '' : r.reason, /double-spend/);
});

test('canonicalView returns BOTH the legality verdict and the validated UTXO set (audit #24+#25)', () => {
  const ci = new CanonicalIndexer();
  ci.addRoot('cd'.repeat(32), 0, 500n);
  const tx: Tx = { version: 1, inputs: [{ prevTxid: 'cd'.repeat(32), vout: 0, sequence: 0xffffffff }], outputs: [{ satoshis: 400, locking: p2pkhScript(genKeyPair().pubCompressed) }], nLockTime: 0 };
  ci.addTransaction(bytesToHex(serializeTxWire(tx, [[]])));
  const view = ci.canonicalView(legalTranscript(), RULESET, SEATS);
  assert.equal(view.legality.valid, true, 'served transcript is legality-valid');
  assert.equal(view.utxos.reduce((s, o) => s + o.satoshis, 0n), 400n, 'served the canonical UTXO set');
});
