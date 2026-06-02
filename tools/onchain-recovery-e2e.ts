/**
 * On-chain recovery / two-exit E2E (core §6.2/§6.4, P4 — no table frozen by an absent player).
 * Every funded state has TWO exits that conflict on the same outpoint: a low-sequence
 * **timeout-default** (refund split) and a higher-sequence **cooperative** settlement. The node's
 * original-replacement rule (nSequence) lets the cooperative tx SUPERSEDE the pre-broadcast
 * timeout-default — then it confirms. This is the pre-signed-fallback mechanism on the real node.
 */

import { spawn, type ChildProcess } from 'node:child_process';
import assert from 'node:assert/strict';
import { RealBsvNode } from '@bsv-poker/adapters/real-node';
import {
  OP,
  genKeyPair,
  signPreimage,
  fairPlayCommitment,
  fundingLocking,
  fundingUnlocking,
  type Script,
  type KeyPair,
} from '@bsv-poker/script-templates-ts';
import { bytesToHex, type BranchBinding } from '@bsv-poker/protocol-types';
import { type Tx, type TxOutput, serializeTxWire, txidWire, sighashMessage, SIGHASH_ALL_FORKID } from '@bsv-poker/tx-builder';

const NODE_DIR = process.env.BSV_NODE_DIR ?? 'D:\\claude\\ACM 01\\bonded-subsat-channel';
const PORT = Number(process.env.BSV_NODE_PORT ?? 8744);
const SUBSIDY = 5_000_000_000;
let daemon: ChildProcess | null = null;
const BIND: BranchBinding = { gid: 'a1'.repeat(8), rulesetHash: 'b2'.repeat(32), round: 0, stateHash: 'c3'.repeat(32), actingSeat: -1, successorCommitment: '00'.repeat(32) };

const p2pkh = (pub: Uint8Array): Script => [OP.OP_DUP, OP.OP_HASH160, fairPlayCommitment(pub), OP.OP_EQUALVERIFY, OP.OP_CHECKSIG];
const sigT = (msg: Uint8Array, k: KeyPair): Uint8Array => Uint8Array.from([...signPreimage(msg, k.priv), SIGHASH_ALL_FORKID]);

async function main(): Promise<void> {
  daemon = spawn('python', ['-m', 'channel.cli', 'daemon-start', '--port', String(PORT), '--db', ':memory:'], {
    cwd: NODE_DIR, env: { ...process.env, PYTHONPATH: 'src' }, stdio: 'ignore',
  });
  const node = new RealBsvNode('127.0.0.1', PORT);
  const k0 = genKeyPair(); const p0 = genKeyPair(); const p1 = genKeyPair();
  try {
    const dl = Date.now() + 30000;
    while (!(await node.ping().catch(() => false))) { if (Date.now() > dl) throw new Error('node down'); await new Promise((r) => setTimeout(r, 400)); }

    const cb = await node.generateBlock(bytesToHex(k0.pubCompressed));
    const pot = SUBSIDY - 1000;
    const fundingScript = fundingLocking(BIND, [p0.pubCompressed, p1.pubCompressed]);
    const fundingTx: Tx = { version: 1, inputs: [{ prevTxid: cb.coinbaseTxid, vout: 0, sequence: 0xffffffff }], outputs: [{ satoshis: pot, locking: fundingScript }], nLockTime: 0 };
    const fundSig: Script = [sigT(sighashMessage(fundingTx, 0, p2pkh(k0.pubCompressed), SUBSIDY), k0), k0.pubCompressed];
    assert.equal((await node.submitTx(bytesToHex(serializeTxWire(fundingTx, [fundSig])))).ok, true, 'funding');
    await node.generateBlock(bytesToHex(k0.pubCompressed));
    const fundingTxid = txidWire(fundingTx, [fundSig]);

    // Build a spend of the pot with a given input sequence + outputs (both players co-sign).
    const spendPot = (sequence: number, outputs: TxOutput[]): { raw: string; txid: string } => {
      const tx: Tx = { version: 1, inputs: [{ prevTxid: fundingTxid, vout: 0, sequence }], outputs, nLockTime: 0 };
      const msg = sighashMessage(tx, 0, fundingScript, pot);
      const ss = fundingUnlocking([sigT(msg, p0), sigT(msg, p1)]);
      return { raw: bytesToHex(serializeTxWire(tx, [ss])), txid: txidWire(tx, [ss]) };
    };

    // EXIT A — timeout-default (refund split), LOW sequence (replaceable), broadcast first.
    const timeout = spendPot(0x00000001, [
      { satoshis: (pot - 1000) / 2, locking: p2pkh(p0.pubCompressed) },
      { satoshis: (pot - 1000) / 2, locking: p2pkh(p1.pubCompressed) },
    ]);
    assert.equal((await node.submitTx(timeout.raw)).ok, true, 'timeout-default admitted to mempool');
    console.log(`[onchain-recovery] timeout-default ${timeout.txid.slice(0, 14)}… in mempool (seq 1)`);

    // EXIT B — cooperative settlement to the winner, HIGHER sequence → REPLACES the timeout one.
    const coop = spendPot(0xffffffff, [{ satoshis: pot - 1000, locking: p2pkh(p0.pubCompressed) }]);
    const coopRes = await node.submitTx(coop.raw);
    console.log(`[onchain-recovery] cooperative ${coop.txid.slice(0, 14)}… submit → ok=${coopRes.ok} reason="${coopRes.reason}"`);
    assert.equal(coopRes.ok, true, `cooperative settlement (replacement) rejected: ${coopRes.reason}`);

    await node.generateBlock(bytesToHex(k0.pubCompressed));
    assert.equal((await node.outpointStatus(fundingTxid, 0)).unspent, false, 'pot consumed');
    const winner = await node.outpointStatus(coop.txid, 0);
    const stale = await node.outpointStatus(timeout.txid, 0);
    assert.equal(winner.unspent, true, 'cooperative (winner) output confirmed');
    assert.equal(stale.unspent, false, 'the superseded timeout-default did NOT confirm');
    console.log(`[onchain-recovery] winner confirmed=${winner.value}; superseded timeout-default not mined`);

    console.log('\n[onchain-recovery] PASS — two-exit recovery on-chain: cooperative tx superseded the pre-broadcast timeout-default via the nSequence replacement rule (P4/§6.4).');
  } finally {
    await node.shutdown();
    daemon?.kill();
  }
}

main().then(() => process.exit(0), (e) => { console.error('[onchain-recovery] FAIL:', (e as Error).message); daemon?.kill(); process.exit(1); });
