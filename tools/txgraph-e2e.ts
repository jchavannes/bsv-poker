/**
 * Canonical transaction-graph ↔ node E2E (audit #31). Proves the reconstructed `TransactionGraph`
 * IS the truth the node enforces: the SAME funding→settlement transactions the in-tree node accepts
 * and confirms also validate in the graph, and the graph's UTXO set matches the node's outpoint
 * status exactly. Then a double-spend the node would reject is also rejected by the graph. Standalone
 * — the in-tree node, no external process.
 */
import assert from 'node:assert/strict';
import { RegtestNode, p2pkhScript } from '@bsv-poker/adapters/regtest-node';
import { TransactionGraph } from '@bsv-poker/adapters/transaction-graph';
import { genKeyPair, signPreimage, type Script } from '@bsv-poker/script-templates-ts';
import { bytesToHex } from '@bsv-poker/protocol-types';
import { type Tx, serializeTxWire, txidWire, sighashMessage } from '@bsv-poker/tx-builder';

const SUBSIDY = 5_000_000_000;
const hex = (b: Uint8Array): string => bytesToHex(b);

async function main(): Promise<void> {
  const node = new RegtestNode();
  const miner = genKeyPair();
  const alice = genKeyPair();
  const bob = genKeyPair();
  const graph = new TransactionGraph();

  // Mine a coinbase and register it as a graph ROOT (the node's coinbase the funding spends from).
  const cb = await node.generateBlock(hex(miner.pubCompressed));
  await node.generateBlock(hex(miner.pubCompressed));
  graph.addRoot(cb.coinbaseTxid, 0, BigInt(SUBSIDY)); // root value only; script irrelevant to graph validation

  // FUNDING: coinbase -> a 2-way escrow-ish output set, accepted by the NODE and added to the GRAPH.
  const funding: Tx = {
    version: 1,
    inputs: [{ prevTxid: cb.coinbaseTxid, vout: 0, sequence: 0xffffffff }],
    outputs: [{ satoshis: SUBSIDY - 1000, locking: p2pkhScript(alice.pubCompressed) }],
    nLockTime: 0,
  };
  const fSs: Script = [signPreimage(sighashMessage(funding, 0, p2pkhScript(miner.pubCompressed), SUBSIDY), miner.priv), miner.pubCompressed];
  const fRaw = bytesToHex(serializeTxWire(funding, [fSs]));
  const fundTxid = txidWire(funding, [fSs]);
  assert.equal((await node.submitTx(fRaw)).ok, true, 'node must accept the funding tx');
  await node.generateBlock(hex(miner.pubCompressed));
  const gf = graph.add(fRaw);
  assert.equal(gf.ok, true, `graph must accept the funding tx: ${gf.ok ? '' : gf.reason}`);
  assert.equal(gf.ok && gf.txid, fundTxid, 'graph txid must match the node/txidWire');
  console.log(`[txgraph] funding accepted by node AND graph (fee ${gf.ok ? gf.fee : '?'}).`);

  // SETTLEMENT: spend the funding output to alice+bob; accepted by the NODE and added to the GRAPH.
  const FUND_VAL = SUBSIDY - 1000;
  const settle: Tx = {
    version: 1,
    inputs: [{ prevTxid: fundTxid, vout: 0, sequence: 0xffffffff }],
    outputs: [{ satoshis: 3_000_000_000, locking: p2pkhScript(alice.pubCompressed) }, { satoshis: FUND_VAL - 3_000_000_000 - 1000, locking: p2pkhScript(bob.pubCompressed) }],
    nLockTime: 0,
  };
  const sSs: Script = [signPreimage(sighashMessage(settle, 0, p2pkhScript(alice.pubCompressed), FUND_VAL), alice.priv), alice.pubCompressed];
  const sRaw = bytesToHex(serializeTxWire(settle, [sSs]));
  const setTxid = txidWire(settle, [sSs]);
  assert.equal((await node.submitTx(sRaw)).ok, true, 'node must accept the settlement tx');
  await node.generateBlock(hex(miner.pubCompressed));
  const gs = graph.add(sRaw);
  assert.equal(gs.ok, true, `graph must accept the settlement tx: ${gs.ok ? '' : gs.reason}`);
  console.log('[txgraph] settlement accepted by node AND graph.');

  // The graph's UTXO view MATCHES the node's outpoint status, exactly.
  assert.equal(graph.isUnspent(fundTxid, 0), false, 'graph: funding output spent');
  assert.equal((await node.outpointStatus(fundTxid, 0)).unspent, false, 'node: funding output spent');
  for (const vout of [0, 1]) {
    const ns = await node.outpointStatus(setTxid, vout);
    assert.equal(graph.isUnspent(setTxid, vout), ns.unspent, `graph/node disagree on settlement vout ${vout} spentness`);
    const gOut = graph.utxos().find((o) => o.txid === setTxid && o.vout === vout)!;
    assert.equal(Number(gOut.satoshis), ns.value, `graph/node disagree on settlement vout ${vout} value`);
  }
  console.log('[txgraph] graph UTXO set MATCHES the node outpoint status (value + spentness).');

  // A DOUBLE-SPEND of the funding output the node would reject is ALSO rejected by the graph.
  const ds: Tx = { version: 1, inputs: [{ prevTxid: fundTxid, vout: 0, sequence: 0xffffffff }], outputs: [{ satoshis: 1000, locking: p2pkhScript(bob.pubCompressed) }], nLockTime: 0 };
  const dRaw = bytesToHex(serializeTxWire(ds, [[signPreimage(sighashMessage(ds, 0, p2pkhScript(alice.pubCompressed), FUND_VAL), alice.priv), alice.pubCompressed]]));
  assert.equal((await node.submitTx(dRaw)).ok, false, 'node must reject the double-spend');
  const gd = graph.add(dRaw);
  assert.equal(gd.ok, false, 'graph must reject the double-spend');
  console.log(`[txgraph] double-spend rejected by node AND graph ("${gd.ok ? '' : gd.reason}").`);

  console.log('\n[txgraph] PASS — the reconstructed canonical transaction graph matches the node it was validated against: same accepted funding/settlement, same UTXO set, same double-spend rejection. The truth is the validated tx graph (audit #31), not a projection.');
}

main().then(
  () => process.exit(0),
  (e) => {
    console.error('[txgraph] FAIL:', (e as Error).stack ?? (e as Error).message);
    process.exit(1);
  },
);
