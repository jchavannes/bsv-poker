/**
 * LIVE multi-party on-chain settlement, fully PEER-TO-PEER (core §6.6, §8). The contested money move
 * is not done by any single party: N peers, each holding ONLY their own key, independently sign the
 * N-of-N settlement transaction and broadcast their signature OVER THE P2P GOSSIP CHANNEL. When a peer
 * has collected all N signatures it assembles + submits the tx to the BSV node. This is the live
 * hand-end settlement protocol — no party can move the pot alone, and the exchange is over the mesh.
 * The only infrastructure is the BSV node (the decentralized chain); there is NO relay server.
 */

import { spawn, type ChildProcess } from 'node:child_process';
import { join } from 'node:path';
import assert from 'node:assert/strict';
import { RealBsvNode } from '@bsv-poker/adapters/real-node';
import {
  OP,
  genKeyPair,
  signPreimage,
  fairPlayCommitment,
  fundingLocking,
  type Script,
  type KeyPair,
} from '@bsv-poker/script-templates-ts';
import { bytesToHex, type BranchBinding } from '@bsv-poker/protocol-types';
import { type Tx, type TxOutput, serializeTxWire, txidWire, sighashMessage } from '@bsv-poker/tx-builder';
import { coSignSettlement } from './settlement-coordinator.ts';
import { p2pMesh } from './p2p-mesh.ts';

const NODE_PORT = Number(process.env.BSV_NODE_PORT ?? 8745);
const SUBSIDY = 5_000_000_000;
const FEE = 1000;
const BIND: BranchBinding = { gid: 'a1'.repeat(8), rulesetHash: 'b2'.repeat(32), round: 0, stateHash: 'c3'.repeat(32), actingSeat: -1, successorCommitment: '00'.repeat(32) };
const p2pkh = (pub: Uint8Array): Script => [OP.OP_DUP, OP.OP_HASH160, fairPlayCommitment(pub), OP.OP_EQUALVERIFY, OP.OP_CHECKSIG];
const sigT = (msg: Uint8Array, k: KeyPair): Uint8Array => signPreimage(msg, k.priv);

let nodeProc: ChildProcess | null = null;

async function main(): Promise<void> {
  nodeProc = spawn(process.execPath, [join(process.cwd(), 'tools/regtest-node-daemon.ts'), '--port', String(NODE_PORT)], { stdio: 'ignore' });
  const node = new RealBsvNode('127.0.0.1', NODE_PORT);
  const tableId = 'live-settle';
  const N = 3;
  const mesh = await p2pMesh(N); // N peer nodes — no relay server
  try {
    const dl = Date.now() + 30000;
    while (!(await node.ping().catch(() => false))) { if (Date.now() > dl) throw new Error('node down'); await new Promise((r) => setTimeout(r, 400)); }

    const players = Array.from({ length: N }, () => genKeyPair());
    const funder = genKeyPair();
    const cb = await node.generateBlock(bytesToHex(funder.pubCompressed));

    // FUNDING: a pot held by the N-of-N of all seated players.
    const pot = SUBSIDY - 1_000_000;
    const fundingScript = fundingLocking(BIND, players.map((p) => p.pubCompressed));
    const fundingTx: Tx = { version: 1, inputs: [{ prevTxid: cb.coinbaseTxid, vout: 0, sequence: 0xffffffff }], outputs: [{ satoshis: pot, locking: fundingScript }, { satoshis: SUBSIDY - pot - FEE, locking: p2pkh(funder.pubCompressed) }], nLockTime: 0 };
    const fundSig: Script = [sigT(sighashMessage(fundingTx, 0, p2pkh(funder.pubCompressed), SUBSIDY), funder), funder.pubCompressed];
    assert.equal((await node.submitTx(bytesToHex(serializeTxWire(fundingTx, [fundSig])))).ok, true, 'funding');
    await node.generateBlock(bytesToHex(funder.pubCompressed));
    const fundingTxid = txidWire(fundingTx, [fundSig]);

    // Deterministic settlement everyone agrees on (a split between seats 0 and 1).
    const outputs: TxOutput[] = [
      { satoshis: Math.floor((pot - FEE) * 0.6), locking: p2pkh(players[0]!.pubCompressed) },
      { satoshis: (pot - FEE) - Math.floor((pot - FEE) * 0.6), locking: p2pkh(players[1]!.pubCompressed) },
    ];
    const settleTx: Tx = { version: 1, inputs: [{ prevTxid: fundingTxid, vout: 0, sequence: 0xffffffff }], outputs, nLockTime: 0 };

    console.log('[onchain-live] 3 peers co-signing the settlement over the P2P mesh (each holds only its own key)…');
    const results = await Promise.all(
      players.map((k, i) =>
        coSignSettlement({ relay: mesh.transports[i]!, tableId, idx: i, myKey: k, settleTx, fundingScript, potValue: pot, n: N, submit: i === 0, submitTx: (raw) => node.submitTx(raw) }),
      ),
    );
    for (let i = 0; i < N; i++) assert.equal(results[i]!.collected, N, `peer ${i} did not collect all ${N} sigs over the mesh`);

    await node.generateBlock(bytesToHex(funder.pubCompressed));
    assert.equal((await node.outpointStatus(fundingTxid, 0)).unspent, false, 'pot consumed by the co-signed settlement');
    const o0 = await node.outpointStatus(results[0]!.txid!, 0);
    const o1 = await node.outpointStatus(results[0]!.txid!, 1);
    assert.equal(o0.unspent, true, 'seat 0 payout confirmed');
    assert.equal(o1.unspent, true, 'seat 1 payout confirmed');
    assert.equal(o0.value + o1.value, pot - FEE, 'split pot conserved on-chain');
    console.log(`[onchain-live] settled on-chain: seat0=${o0.value} seat1=${o1.value} (sigs exchanged peer-to-peer, N-of-N)`);

    console.log('\n[onchain-live] PASS — live multi-party on-chain settlement: N peers co-signed over the P2P mesh; no party moved the pot alone, no relay server.');
  } finally {
    await node.shutdown();
    nodeProc?.kill();
    mesh.close();
  }
}

main().then(() => process.exit(0), (e) => { console.error('[onchain-live] FAIL:', (e as Error).message); nodeProc?.kill(); process.exit(1); });
