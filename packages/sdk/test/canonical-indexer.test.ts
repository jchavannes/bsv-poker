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
import { sessionAuthFromSeed, deriveSeatSeed, buildManifest, MANIFEST_VERSION, type GameManifest, type GameManifestBody } from '@bsv-poker/app-services';
import { p2pkhScript } from '@bsv-poker/adapters/regtest-node';
import { sha256, bytesToHex } from '@bsv-poker/protocol-types';
import { genKeyPair, signPreimage, type Script } from '@bsv-poker/script-templates-ts';
import { type Tx, serializeTxWire, txidWire, sighashMessage } from '@bsv-poker/tx-builder';
import { RegtestNode } from '@bsv-poker/adapters/regtest-node';

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

test('ingestOnChain performs FULL production validation via the node — bad signature rejected (audit #26)', async () => {
  const SUBSIDY = 5_000_000_000;
  const node = new RegtestNode();
  const miner = genKeyPair();
  const alice = genKeyPair();
  const ci = new CanonicalIndexer();
  const cb = await node.generateBlock(bytesToHex(miner.pubCompressed));
  await node.generateBlock(bytesToHex(miner.pubCompressed));
  ci.addRoot(cb.coinbaseTxid, 0, BigInt(SUBSIDY));

  // A VALID funding spend (correct signature) passes full node validation AND enters the graph.
  const funding: Tx = { version: 1, inputs: [{ prevTxid: cb.coinbaseTxid, vout: 0, sequence: 0xffffffff }], outputs: [{ satoshis: SUBSIDY - 1000, locking: p2pkhScript(alice.pubCompressed) }], nLockTime: 0 };
  const fss: Script = [signPreimage(sighashMessage(funding, 0, p2pkhScript(miner.pubCompressed), SUBSIDY), miner.priv), miner.pubCompressed];
  const ok = await ci.ingestOnChain(bytesToHex(serializeTxWire(funding, [fss])), node);
  assert.equal(ok.validated, true, `valid funding must pass full validation: ${ok.reason}`);
  await node.generateBlock(bytesToHex(miner.pubCompressed));
  const fundTxid = txidWire(funding, [fss]);
  assert.equal(ci.isUnspent(fundTxid, 0), true, 'validated funding output is in the canonical graph');

  // A BAD-SIGNATURE spend (signed by the WRONG key) is REJECTED by the node's interpreter — full
  // production validation, which structure-only validation would miss.
  const FUND_VAL = SUBSIDY - 1000;
  const wrong = genKeyPair();
  const bad: Tx = { version: 1, inputs: [{ prevTxid: fundTxid, vout: 0, sequence: 0xffffffff }], outputs: [{ satoshis: FUND_VAL - 1000, locking: p2pkhScript(alice.pubCompressed) }], nLockTime: 0 };
  const bss: Script = [signPreimage(sighashMessage(bad, 0, p2pkhScript(alice.pubCompressed), FUND_VAL), wrong.priv), alice.pubCompressed];
  const r = await ci.ingestOnChain(bytesToHex(serializeTxWire(bad, [bss])), node);
  assert.equal(r.validated, false, 'a bad signature must be rejected by full node validation');
  assert.equal(ci.isUnspent(fundTxid, 0), true, 'the rejected spend did not consume the funding output');
});

test('registerGame enforces the ONE-GAME key lifecycle: a manifest registers once, keys never reused (audit #27)', async () => {
  const ci = new CanonicalIndexer();
  async function game(nonce: string, seeds: number[]): Promise<GameManifest> {
    const auths = await Promise.all(seeds.map((n) => sessionAuthFromSeed(deriveSeatSeed(new Uint8Array(32).fill(n)))));
    const body: GameManifestBody = {
      v: MANIFEST_VERSION, ruleset: 'holdem', stakes: { sb: 1, bb: 2 }, tableId: 't',
      seats: auths.map((a, i) => ({ seat: i, seatPub: a.pub })), nonce,
    };
    return buildManifest(body, auths);
  }
  // game 1: fresh keys → registers
  const g1 = await game('ab'.repeat(32), [1, 2]);
  const r1 = await ci.registerGame(g1);
  assert.equal(r1.ok, true, r1.reason);
  assert.equal(r1.gameId, g1.gameId);
  assert.equal(ci.isSeatKeyUsed(g1.seats[0]!.seatPub), true);

  // re-registering the SAME manifest is rejected (replayed gameId)
  assert.equal((await ci.registerGame(g1)).ok, false);

  // game 2 that REUSES seat 1's key (seed 1) is rejected — a key serves at most one game
  const g2 = await game('cd'.repeat(32), [1, 3]);
  const r2 = await ci.registerGame(g2);
  assert.equal(r2.ok, false);
  assert.match(r2.reason, /prior game|ONE game/);

  // game 3 with entirely fresh keys registers cleanly
  const g3 = await game('ef'.repeat(32), [5, 6]);
  assert.equal((await ci.registerGame(g3)).ok, true);

  // a structurally invalid manifest (tampered gameId) is rejected
  const forged = { ...g3, stakes: { sb: 9, bb: 9 } } as GameManifest;
  assert.equal((await ci.registerGame(forged)).ok, false);
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
